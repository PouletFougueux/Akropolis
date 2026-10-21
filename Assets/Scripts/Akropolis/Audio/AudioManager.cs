using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.Audio
{
    /// Entry point used by gameplay code: Sound.Play(Sfx.PlaceToken), Sound.Music(MusicMood.Game).
    public static class Sound
    {
        public static void Play(Sfx id, float pitch = 1f, float volume = 1f) => AudioManager.Instance.PlaySfx(id, pitch, volume);
        public static void Music(MusicMood mood) => AudioManager.Instance.PlayMusic(mood);
        /// Draws the random part of an effect once, so it can be replayed identically elsewhere (network).
        public static void Roll(Sfx id, out float pitch, out bool rare) => AudioManager.Instance.RollSfx(id, out pitch, out rare);
        public static void PlayExact(Sfx id, float pitch, bool rare) => AudioManager.Instance.PlaySfxExact(id, pitch, rare);
    }

    /// Plays the SoundBank: pooled 2D effects, cross-faded music playlists and a looping ambience.
    /// Player volumes (0-1) are stored in PlayerPrefs and multiply the SoundBank's own volumes.
    public sealed class AudioManager : MonoBehaviour
    {
        const string BankPath = "Audio/SoundBank";
        const int PoolSize = 16;
        const float CutFade = 0.3f;

        static AudioManager instance;

        // Domain reload is disabled in this project: statics survive between Play sessions.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;

        public static AudioManager Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("Audio");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<AudioManager>();
                }
                return instance;
            }
        }

        sealed class Voice
        {
            public AudioSource Source;
            public float BaseVolume;
            public float StopAt; // unscaled time, 0 = play to the end
        }

        SoundBank bank;
        readonly List<Voice> pool = new List<Voice>();
        readonly Dictionary<Sfx, float> lastPlayed = new Dictionary<Sfx, float>();
        readonly Dictionary<object, AudioClip> lastClip = new Dictionary<object, AudioClip>();
        int nextVoice;

        AudioSource musicCurrent, musicPrevious, ambience;
        MusicMood mood = MusicMood.None;
        readonly List<AudioClip> playlist = new List<AudioClip>();
        int playlistIndex;
        float fade = 1f;

        // ---------------- Player volumes ----------------

        public float MasterVolume
        {
            get => AudioListener.volume;
            set
            {
                AudioListener.volume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("audio.master", AudioListener.volume);
            }
        }

        public float MusicVolume { get => Pref("audio.music"); set => SetPref("audio.music", value); }
        public float AmbienceVolume { get => Pref("audio.ambience"); set => SetPref("audio.ambience", value); }
        public float SfxVolume { get => Pref("audio.sfx"); set => SetPref("audio.sfx", value); }

        static float Pref(string key) => PlayerPrefs.GetFloat(key, 1f);
        static void SetPref(string key, float value) => PlayerPrefs.SetFloat(key, Mathf.Clamp01(value));

        // ---------------- Lifecycle ----------------

        void Awake()
        {
            bank = Resources.Load<SoundBank>(BankPath);
            if (bank == null) Debug.LogWarning($"[Audio] SoundBank introuvable (Resources/{BankPath}) : le jeu sera muet.");

            for (int i = 0; i < PoolSize; i++) pool.Add(new Voice { Source = NewSource($"Sfx {i}", false) });
            musicCurrent = NewSource("Music A", true);
            musicPrevious = NewSource("Music B", true);
            ambience = NewSource("Ambience", true);

            AudioListener.volume = PlayerPrefs.GetFloat("audio.master", 1f);
            if (FindAnyObjectByType<AudioListener>() == null) gameObject.AddComponent<AudioListener>();

            if (bank != null && bank.Ambience != null)
            {
                ambience.clip = bank.Ambience;
                ambience.Play();
            }
        }

        AudioSource NewSource(string name, bool loop)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var s = go.AddComponent<AudioSource>();
            s.playOnAwake = false;
            s.loop = loop;
            s.spatialBlend = 0f;
            return s;
        }

        void Update()
        {
            if (bank == null) return;
            float dt = Time.unscaledDeltaTime;
            float now = Time.unscaledTime;

            // Effects with a MaxDuration fade out and stop early.
            foreach (var v in pool)
            {
                if (v.StopAt <= 0f) continue;
                float left = v.StopAt - now;
                if (left <= 0f || !v.Source.isPlaying)
                {
                    v.Source.Stop();
                    v.StopAt = 0f;
                }
                else if (left < CutFade) v.Source.volume = v.BaseVolume * left / CutFade;
            }

            ambience.volume = bank.AmbienceVolume * AmbienceVolume;

            float musicTarget = bank.MusicVolume * MusicVolume;
            fade = Mathf.MoveTowards(fade, 1f, dt / bank.MusicCrossfade);
            musicCurrent.volume = musicTarget * fade;
            musicPrevious.volume = musicTarget * (1f - fade);
            if (fade >= 1f && musicPrevious.isPlaying) musicPrevious.Stop();

            // Playlists with several tracks chain them, starting the next one before the current ends.
            var clip = musicCurrent.clip;
            if (clip != null && !musicCurrent.loop && musicCurrent.isPlaying &&
                clip.length - musicCurrent.time < bank.MusicCrossfade && playlist.Count > 1)
            {
                playlistIndex = (playlistIndex + 1) % playlist.Count;
                CrossfadeTo(playlist[playlistIndex]);
            }
        }

        // ---------------- Effects ----------------

        public void PlaySfx(Sfx id, float pitch, float volume)
        {
            var entry = bank != null ? bank.Get(id) : null;
            if (entry == null || entry.Clips == null || entry.Clips.Length == 0) return;

            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(id, out float last) && now - last < entry.Cooldown) return;
            lastPlayed[id] = now;

            if (entry.RareClip != null && Random.value < entry.RareChance)
            {
                PlayClip(entry.RareClip, entry.Volume * volume, 1f, 0f);
                return;
            }
            var clip = PickClip(id, entry.Clips);
            PlayClip(clip, entry.Volume * volume, Random.Range(entry.PitchMin, entry.PitchMax) * pitch, entry.MaxDuration);
        }

        public void RollSfx(Sfx id, out float pitch, out bool rare)
        {
            var e = bank != null ? bank.Get(id) : null;
            pitch = e != null ? Random.Range(e.PitchMin, e.PitchMax) : 1f;
            rare = e != null && e.RareClip != null && Random.value < e.RareChance;
        }

        public void PlaySfxExact(Sfx id, float pitch, bool rare)
        {
            var e = bank != null ? bank.Get(id) : null;
            if (e == null) return;
            if (rare && e.RareClip != null)
            {
                PlayClip(e.RareClip, e.Volume, 1f, 0f);
                return;
            }
            if (e.Clips == null || e.Clips.Length == 0) return;
            PlayClip(PickClip(id, e.Clips), e.Volume, pitch, e.MaxDuration);
        }

        void PlayClip(AudioClip clip, float volume, float pitch, float maxDuration)
        {
            if (clip == null) return;
            var v = pool[nextVoice];
            nextVoice = (nextVoice + 1) % pool.Count;
            v.BaseVolume = volume * bank.SfxVolume * SfxVolume;
            v.StopAt = maxDuration > 0f ? Time.unscaledTime + maxDuration : 0f;
            v.Source.clip = clip;
            v.Source.pitch = pitch;
            v.Source.volume = v.BaseVolume;
            v.Source.Play();
        }

        AudioClip PickClip(object key, AudioClip[] clips)
        {
            lastClip.TryGetValue(key, out var previous);
            AudioClip clip = null;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                clip = clips[Random.Range(0, clips.Length)];
                if (clip != null && (clip != previous || clips.Length == 1)) break;
            }
            lastClip[key] = clip;
            return clip;
        }

        // ---------------- Music ----------------

        public void PlayMusic(MusicMood next)
        {
            if (next == mood || bank == null) return;
            mood = next;
            playlist.Clear();
            foreach (var c in bank.MusicFor(next))
                if (c != null) playlist.Add(c);
            for (int i = playlist.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (playlist[i], playlist[j]) = (playlist[j], playlist[i]);
            }
            playlistIndex = 0;
            CrossfadeTo(playlist.Count > 0 ? playlist[0] : null);
        }

        void CrossfadeTo(AudioClip clip)
        {
            (musicCurrent, musicPrevious) = (musicPrevious, musicCurrent);
            musicCurrent.Stop();
            musicCurrent.clip = clip;
            musicCurrent.loop = playlist.Count <= 1;
            musicCurrent.volume = 0f;
            if (clip != null) musicCurrent.Play();
            fade = 0f;
        }
    }
}
