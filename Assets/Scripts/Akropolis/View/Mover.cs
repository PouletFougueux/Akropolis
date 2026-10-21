using System;
using UnityEngine;

namespace Akropolis.View
{
    /// Drives every animated table object: flights, hover lift, idle bob and squash.
    public sealed class Mover : MonoBehaviour
    {
        public Vector3 Home;
        public Quaternion HomeRot = Quaternion.identity;
        public Vector3 HomeScale = Vector3.one;
        public float Lift;
        public float Grow;
        public bool Bob;
        public float BobAmount = 0.05f;

        Vector3 fromPos, fromScale;
        Quaternion fromRot;
        float t = 1f, duration = 1f, arc;
        Ease ease;
        Action arrive;
        float lift, grow, squashT = 10f, squashAmp, phase;

        public bool Flying => t < 1f;

        public static Mover Of(Transform tr)
        {
            if (!tr.TryGetComponent(out Mover m))
            {
                m = tr.gameObject.AddComponent<Mover>();
                m.Home = tr.localPosition;
                m.HomeRot = tr.localRotation;
                m.HomeScale = tr.localScale;
            }
            return m;
        }

        void Awake()
        {
            phase = UnityEngine.Random.value * 6.28f;
        }

        public void FlyTo(Vector3 home, Quaternion rot, Vector3 scale, float dur, float arcHeight = 0f,
            Ease e = Ease.InOut, float delay = 0f, Action onArrive = null)
        {
            fromPos = transform.localPosition;
            fromRot = transform.localRotation;
            fromScale = transform.localScale;
            Home = home;
            HomeRot = rot;
            HomeScale = scale;
            duration = Mathf.Max(0.01f, dur);
            t = -delay / duration;
            arc = arcHeight;
            ease = e;
            arrive = onArrive;
        }

        /// Pops the object in from zero scale at its current home.
        public void Appear(float delay, float dur = 0.4f)
        {
            transform.localPosition = Home;
            transform.localScale = Vector3.zero;
            FlyTo(Home, HomeRot, HomeScale, dur, 0f, Ease.OutBack, delay);
        }

        /// Drops the object onto its home from above.
        public void Drop(float height, float delay, float dur = 0.45f, Action onArrive = null)
        {
            transform.localPosition = Home + Vector3.up * height;
            transform.localRotation = HomeRot;
            transform.localScale = HomeScale;
            FlyTo(Home, HomeRot, HomeScale, dur, 0f, Ease.In, delay, () =>
            {
                Squash(0.35f);
                onArrive?.Invoke();
            });
        }

        public void Squash(float amp = 0.3f)
        {
            squashT = 0f;
            squashAmp = amp;
        }

        void Update()
        {
            float dt = Time.deltaTime;
            lift = Mathf.Lerp(lift, Lift, 1f - Mathf.Exp(-14f * dt));
            grow = Mathf.Lerp(grow, Grow, 1f - Mathf.Exp(-14f * dt));

            Vector3 pos, scale;
            Quaternion rot;
            if (t < 1f)
            {
                t += dt / duration;
                float k = Mathf.Clamp01(t);
                float e = Easing.Evaluate(ease, k);
                pos = Vector3.LerpUnclamped(fromPos, Home, e) + Vector3.up * (arc * 4f * k * (1f - k));
                rot = Quaternion.Slerp(fromRot, HomeRot, Mathf.Clamp01(e));
                scale = Vector3.LerpUnclamped(fromScale, HomeScale, e);
                if (t >= 1f)
                {
                    var a = arrive;
                    arrive = null;
                    a?.Invoke();
                }
            }
            else
            {
                pos = Home;
                rot = HomeRot;
                scale = HomeScale;
            }

            if (squashT < 1.2f)
            {
                squashT += dt;
                float s = squashAmp * Mathf.Exp(-6f * squashT) * Mathf.Cos(20f * squashT);
                scale = Vector3.Scale(scale, new Vector3(1f + s, 1f - s, 1f + s));
            }

            float bob = Bob ? Mathf.Sin(Time.time * 4f + phase) * BobAmount : 0f;
            transform.localPosition = pos + Vector3.up * (lift + bob);
            transform.localRotation = rot;
            transform.localScale = scale * (1f + grow);
        }
    }
}
