using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.View
{
    /// Lightweight particle bursts made of tiny hex prisms.
    public sealed class Fx : MonoBehaviour
    {
        struct Particle
        {
            public Transform T;
            public Vector3 Velocity, Spin, Scale;
            public float Age, Life;
        }

        static Fx instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => instance = null;
        readonly List<Particle> particles = new List<Particle>();

        static Fx I
        {
            get
            {
                if (instance == null) instance = new GameObject("Fx").AddComponent<Fx>();
                return instance;
            }
        }

        public static void Burst(Vector3 pos, Color color, int count = 10, float speed = 2.5f, float size = 0.12f)
        {
            for (int i = 0; i < count; i++)
            {
                var dir = Random.onUnitSphere;
                dir.y = Mathf.Abs(dir.y) + 0.6f;
                I.Spawn(pos, color, dir.normalized * speed * Random.Range(0.6f, 1.2f), size * Random.Range(0.7f, 1.3f),
                    Random.Range(0.5f, 0.8f));
            }
        }

        public static void Confetti(Vector3 pos, int count = 90)
        {
            var colors = new[] { "#3E8FC9", "#4E9A4B", "#EDC14B", "#C8483A", "#E0745A", "#8A6BBE", "#FBF4E4" };
            for (int i = 0; i < count; i++)
            {
                var dir = Random.insideUnitSphere;
                dir.y = Random.Range(1.2f, 2.2f);
                I.Spawn(pos, Palette.Hex(colors[i % colors.Length]), dir * Random.Range(3f, 5.5f),
                    Random.Range(0.1f, 0.18f), Random.Range(1.6f, 2.6f));
            }
        }

        void Spawn(Vector3 pos, Color color, Vector3 velocity, float size, float life)
        {
            var go = new GameObject("Particle");
            go.AddComponent<MeshFilter>().sharedMesh = HexMesh.Prism;
            go.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit(color);
            go.transform.SetParent(transform, false);
            go.transform.position = pos;
            go.transform.rotation = Random.rotation;
            var scale = new Vector3(size, size * 0.4f, size);
            go.transform.localScale = scale;
            particles.Add(new Particle
            {
                T = go.transform, Velocity = velocity, Spin = Random.insideUnitSphere * 540f, Scale = scale, Life = life
            });
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.Age += dt;
                if (p.Age >= p.Life || p.T == null)
                {
                    if (p.T != null) Destroy(p.T.gameObject);
                    particles.RemoveAt(i);
                    continue;
                }
                p.Velocity += Physics.gravity * 0.6f * dt;
                p.Velocity *= 1f - 1.2f * dt;
                p.T.position += p.Velocity * dt;
                p.T.Rotate(p.Spin * dt, Space.Self);
                float k = p.Age / p.Life;
                p.T.localScale = p.Scale * (1f - k * k);
                particles[i] = p;
            }
        }
    }
}
