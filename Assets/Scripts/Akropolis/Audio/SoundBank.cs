using System;
using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.Audio
{
    /// <summary>
    /// Tous les sons du jeu. Les premières valeurs reprennent la banque d'Harmonies (mêmes index dans l'asset) :
    /// HoverTokens = survol du chantier, TakeTokens = prendre une tuile, PlaceToken = poser, CancelTokens = reposer,
    /// DealCard = tuile distribuée au chantier. Ajoutez une valeur à la fin puis remplissez son slot dans l'asset.
    /// </summary>
    public enum Sfx
    {
        UiHover,
        UiClick,
        GameStart,
        TurnStart,
        HoverTokens,
        TakeTokens,
        SelectToken,
        PlaceToken,
        CancelTokens,
        Undo,
        HoverCard,
        TakeCard,
        DealCard,
        SelectCard,
        PlaceCube,
        CardCompleted,
        SpiritChosen,
        Error,
        GameOver,
        ScoreItem,
        Victory,
        Bell,
        ChatMessage,
        Emote,
        RotateTile,
        StoneGain,
        StonePay,
        ChiefPass,
    }

    public enum MusicMood { None, Menu, Game, End }

    [Serializable]
    public sealed class SfxEntry
    {
        [HideInInspector] public string Name;
        public Sfx Id;
        [Tooltip("Un clip est tiré au hasard à chaque lecture (jamais deux fois le même d'affilée).")]
        public AudioClip[] Clips = new AudioClip[0];
        [Range(0f, 1f)] public float Volume = 1f;
        [Range(0.5f, 2f)] public float PitchMin = 0.95f;
        [Range(0.5f, 2f)] public float PitchMax = 1.05f;
        [Tooltip("Délai minimal entre deux lectures de cet effet, en secondes.")]
        [Min(0f)] public float Cooldown = 0.03f;
        [Tooltip("Coupe le son (avec un court fondu) après cette durée, en secondes. 0 = clip entier.")]
        [Min(0f)] public float MaxDuration;
        [Tooltip("Clip rare joué à la place des clips normaux (easter egg), sans variation de hauteur.")]
        public AudioClip RareClip;
        [Tooltip("Probabilité de jouer le clip rare à chaque lecture (0.002 = 1 chance sur 500).")]
        [Range(0f, 1f)] public float RareChance;
    }

    /// <summary>Tout l'audio du jeu dans un asset éditable (Resources/Audio/SoundBank).</summary>
    [CreateAssetMenu(menuName = "Akropolis/Sound Bank", fileName = "SoundBank")]
    public sealed class SoundBank : ScriptableObject
    {
        [Header("Effets sonores")]
        [Range(0f, 1f)] public float SfxVolume = 0.9f;
        public SfxEntry[] Effects = new SfxEntry[0];

        [Header("Musique (les pistes d'une humeur s'enchaînent en fondu, ordre aléatoire)")]
        [Range(0f, 1f)] public float MusicVolume = 0.4f;
        [Min(0.1f)] public float MusicCrossfade = 2.5f;
        public AudioClip[] MenuMusic = new AudioClip[0];
        public AudioClip[] GameMusic = new AudioClip[0];
        public AudioClip[] EndMusic = new AudioClip[0];

        [Header("Ambiance (boucle permanente)")]
        [Range(0f, 1f)] public float AmbienceVolume = 0.3f;
        public AudioClip Ambience;

        public AudioClip[] MusicFor(MusicMood mood)
        {
            switch (mood)
            {
                case MusicMood.Menu: return MenuMusic;
                case MusicMood.Game: return GameMusic;
                case MusicMood.End: return EndMusic;
                default: return new AudioClip[0];
            }
        }

        public SfxEntry Get(Sfx id)
        {
            foreach (var e in Effects)
                if (e != null && e.Id == id) return e;
            return null;
        }

        /// <summary>Garde un slot par valeur de Sfx, pour que les nouvelles apparaissent dans l'Inspector.</summary>
        void OnValidate()
        {
            var effects = new List<SfxEntry>();
            foreach (Sfx id in Enum.GetValues(typeof(Sfx)))
            {
                var e = Get(id) ?? new SfxEntry { Id = id };
                e.Name = id.ToString();
                if (e.PitchMax < e.PitchMin) e.PitchMax = e.PitchMin;
                effects.Add(e);
            }
            Effects = effects.ToArray();
        }

#if UNITY_EDITOR
        [ContextMenu("Remplir les slots manquants")]
        void FillSlots() => OnValidate();
#endif
    }
}
