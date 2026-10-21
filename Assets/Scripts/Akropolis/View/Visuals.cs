using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.View
{
    public enum Ease { InOut, Out, OutBack, In }

    public static class Easing
    {
        public static float Evaluate(Ease ease, float t)
        {
            switch (ease)
            {
                case Ease.Out: return 1f - Mathf.Pow(1f - t, 3f);
                case Ease.In: return t * t * t;
                case Ease.OutBack:
                    const float c1 = 1.70158f, c3 = c1 + 1f;
                    return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
                default: return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
            }
        }
    }

    /// Flat-top hexagonal prism (radius 1, height 1, base at y=0) with a small bevel.
    public static class HexMesh
    {
        static Mesh prism, flat;

        public static Mesh Prism => prism != null ? prism : prism = BuildPrism();
        public static Mesh Flat => flat != null ? flat : flat = BuildFlat();

        static Vector3 Corner(int k, float r, float y)
        {
            float a = Mathf.Deg2Rad * 60f * k;
            return new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r);
        }

        static Mesh BuildPrism()
        {
            const float bevelY = 0.8f, topR = 0.86f;
            var v = new List<Vector3>();
            var n = new List<Vector3>();
            var tris = new List<int>();

            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c); v.Add(d);
                for (int k = 0; k < 4; k++) n.Add(normal);
                tris.Add(i + 3); tris.Add(i + 2); tris.Add(i + 1);
                tris.Add(i + 3); tris.Add(i + 1); tris.Add(i);
            }

            for (int k = 0; k < 6; k++)
            {
                float mid = Mathf.Deg2Rad * (60f * k + 30f);
                var outward = new Vector3(Mathf.Cos(mid), 0f, Mathf.Sin(mid));
                Quad(Corner(k, 1f, 0f), Corner(k + 1, 1f, 0f), Corner(k + 1, 1f, bevelY), Corner(k, 1f, bevelY), outward);
                Quad(Corner(k, 1f, bevelY), Corner(k + 1, 1f, bevelY), Corner(k + 1, topR, 1f), Corner(k, topR, 1f),
                    (outward + Vector3.up * 1.3f).normalized);
            }

            int top = v.Count;
            v.Add(new Vector3(0f, 1f, 0f)); n.Add(Vector3.up);
            for (int k = 0; k < 6; k++) { v.Add(Corner(k, topR, 1f)); n.Add(Vector3.up); }
            for (int k = 0; k < 6; k++)
            {
                tris.Add(top); tris.Add(top + 1 + (k + 1) % 6); tris.Add(top + 1 + k);
            }

            int bottom = v.Count;
            v.Add(Vector3.zero); n.Add(Vector3.down);
            for (int k = 0; k < 6; k++) { v.Add(Corner(k, 1f, 0f)); n.Add(Vector3.down); }
            for (int k = 0; k < 6; k++)
            {
                tris.Add(bottom); tris.Add(bottom + 1 + k); tris.Add(bottom + 1 + (k + 1) % 6);
            }

            var mesh = new Mesh { name = "HexPrism" };
            mesh.SetVertices(v);
            mesh.SetNormals(n);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh BuildFlat()
        {
            var v = new List<Vector3> { Vector3.zero };
            var tris = new List<int>();
            for (int k = 0; k < 6; k++) v.Add(Corner(k, 1f, 0f));
            for (int k = 0; k < 6; k++)
            {
                tris.Add(0); tris.Add(1 + (k + 1) % 6); tris.Add(1 + k);
            }
            var mesh = new Mesh { name = "HexFlat" };
            mesh.SetVertices(v);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }

    public static class Mats
    {
        static Material litBase, unlitBase;
        static readonly Dictionary<(Color, float), Material> lit = new Dictionary<(Color, float), Material>();
        static readonly Dictionary<Color, Material> unlit = new Dictionary<Color, Material>();

        /// Domain reload is disabled: cached materials are destroyed when leaving Play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            lit.Clear();
            unlit.Clear();
            litBase = null;
            unlitBase = null;
        }

        // Real assets in Resources: a primitive's default material is the built-in (non-URP) one in players.
        static Material LitBase => litBase != null ? litBase : litBase = Resources.Load<Material>("Materials/Lit");
        static Material UnlitBase => unlitBase != null ? unlitBase : unlitBase = Resources.Load<Material>("Materials/Unlit");

        public static Material Lit(Color c, float smoothness = 0.3f)
        {
            if (lit.TryGetValue((c, smoothness), out var m) && m != null) return m;
            m = new Material(LitBase) { color = c };
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
            lit[(c, smoothness)] = m;
            return m;
        }

        /// Unlit, alpha-blended, double-sided.
        public static Material Unlit(Color c)
        {
            if (unlit.TryGetValue(c, out var m) && m != null) return m;
            m = UnlitInstance(c);
            unlit[c] = m;
            return m;
        }

        /// A fresh material that can be animated without affecting others.
        public static Material UnlitInstance(Color c) => new Material(UnlitBase) { color = c, renderQueue = 3100 };
    }

}
