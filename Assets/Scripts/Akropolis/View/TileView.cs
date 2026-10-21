using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.View
{
    /// <summary>Hexagones de tuile (marbre + face colorée + décor) construits en code, au style d'Harmonies.</summary>
    public static class TileView
    {
        public const float HexScale = 0.55f;
        public const float Radius = HexScale * 0.985f;
        public const float LevelHeight = 0.22f;
        /// <summary>Hauteur du dessus de la face colorée, par rapport à la base de l'hexagone.</summary>
        public const float Top = LevelHeight + 0.02f;
        public const string DecorName = "Decor";

        static readonly Quaternion Pointy = Quaternion.Euler(0f, 30f, 0f);
        static Mesh star, tileOutline, startOutline;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => star = tileOutline = startOutline = null;

        public static Vector3 Local(HexCoord c) => c.ToLocal() * HexScale;

        public static Vector3 ShapeCentroid
        {
            get
            {
                var s = Vector3.zero;
                foreach (var c in TileData.Shape) s += Local(c);
                return s / TileData.Shape.Length;
            }
        }

        public static int Seed(HexCoord c, int id) => unchecked(c.Q * 73856093 ^ c.R * 19349663 ^ id * 83492791);

        public static Color TopColor(HexFace f)
        {
            if (f.IsQuarry) return Palette.Hex("#B4B7BA");
            var c = Palette.DistrictColor(f.District);
            return f.IsPlaza ? c : Color.Lerp(c, Color.white, 0.32f);
        }

        static Color Darken(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        static Transform Obj(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale, Quaternion rot)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        // ---------------- Hexes & tiles ----------------

        public static Transform BuildHex(Transform parent, HexFace face, Vector3 localPos, int seed, bool decorate = true)
        {
            var root = new GameObject(face.IsPlaza ? "Place " + Rules.Name(face.District) : Rules.Name(face.District)).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            Obj("Body", HexMesh.Prism, Mats.Lit(Palette.Marble, 0.3f), root, Vector3.zero, new Vector3(Radius, LevelHeight, Radius), Pointy);
            Obj("Face", HexMesh.Prism, Mats.Lit(TopColor(face), face.IsPlaza ? 0.45f : 0.25f), root,
                new Vector3(0f, LevelHeight - 0.02f, 0f), new Vector3(Radius * 0.84f, 0.04f, Radius * 0.84f), Pointy);
            if (decorate) Decorate(root, face, seed);
            return root;
        }

        /// <summary>Une tuile Cité : pivot sur son premier hexagone, forme non pivotée.</summary>
        public static Transform BuildTile(Transform parent, TileData tile, out Transform[] hexes)
        {
            var root = new GameObject("Tuile " + tile.Id).transform;
            root.SetParent(parent, false);
            hexes = new Transform[3];
            for (int i = 0; i < 3; i++)
                hexes[i] = BuildHex(root, tile.Faces[i], Local(TileData.Shape[i]), Seed(TileData.Shape[i], tile.Id));
            if (tileOutline == null) tileOutline = OutlineMesh(TileData.Shape);
            Outline(root, tileOutline);
            return root;
        }

        /// <summary>Tuile de départ : une Place Habitations entourée de 3 carrières.</summary>
        public static Transform BuildStartingTile(Transform parent, Dictionary<HexCoord, Transform> tops)
        {
            var root = new GameObject("Tuile de départ").transform;
            root.SetParent(parent, false);
            var center = new HexCoord(0, 0);
            var coords = new List<HexCoord> { center };
            tops[center] = BuildHex(root, new HexFace(District.Houses, true), Vector3.zero, 11);
            for (int d = 0; d < 6; d += 2)
            {
                var c = center.Neighbor(d);
                coords.Add(c);
                tops[c] = BuildHex(root, new HexFace(District.Quarry, false), Local(c), 13 + d);
            }
            if (startOutline == null) startOutline = OutlineMesh(coords);
            Outline(root, startOutline);
            return root;
        }

        /// <summary>Dos de tuile, pour les piles.</summary>
        public static Transform BuildBack(Transform parent)
        {
            var root = new GameObject("Dos").transform;
            root.SetParent(parent, false);
            var c = ShapeCentroid;
            var mat = Mats.Lit(Palette.Hex("#E6DECD"), 0.2f);
            foreach (var s in TileData.Shape)
                Obj("Back", HexMesh.Prism, mat, root, Local(s) - c, new Vector3(Radius, LevelHeight, Radius), Pointy);
            Obj("Emblem", HexMesh.Prism, Mats.Lit(Palette.Hex("#C7B994"), 0.25f), root, new Vector3(0f, LevelHeight - 0.01f, 0f),
                new Vector3(0.3f, 0.03f, 0.3f), Pointy);
            return root;
        }

        public static void StripDecor(Transform hex)
        {
            if (hex == null) return;
            var d = hex.Find(DecorName);
            if (d != null) d.gameObject.SetActive(false);
        }

        public static void ShowDecor(Transform hex)
        {
            var d = DecorOf(hex);
            if (d == null) return;
            d.gameObject.SetActive(true);
            Mover.Of(d).Squash(0.4f);
        }

        public static Transform DecorOf(Transform hex) => hex != null ? hex.Find(DecorName) : null;

        // ---------------- Outline ----------------

        static Vector3 Corner(int i, float r)
        {
            float a = Mathf.Deg2Rad * (60f * i - 30f);
            return new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
        }

        static Mesh OutlineMesh(IList<HexCoord> coords)
        {
            var set = new HashSet<HexCoord>(coords);
            var v = new List<Vector3>();
            var t = new List<int>();
            float outer = Radius * 0.8f, inner = Radius * 0.72f;
            foreach (var c in coords)
            {
                var center = Local(c) + Vector3.up * (Top + 0.004f);
                for (int d = 0; d < 6; d++)
                {
                    if (set.Contains(c.Neighbor(d))) continue;
                    int a = (6 - d) % 6, b = (7 - d) % 6;
                    int i = v.Count;
                    v.Add(center + Corner(a, outer));
                    v.Add(center + Corner(a, inner));
                    v.Add(center + Corner(b, inner));
                    v.Add(center + Corner(b, outer));
                    t.AddRange(new[] { i, i + 1, i + 2, i, i + 2, i + 3 });
                }
            }
            var m = new Mesh { name = "Outline" };
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        static void Outline(Transform root, Mesh mesh)
        {
            var o = Obj("Outline", mesh, Mats.Lit(Palette.Hex("#6B5A48"), 0.1f), root, Vector3.zero, Vector3.one, Quaternion.identity);
            o.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        // ---------------- Decor ----------------

        static void Decorate(Transform hex, HexFace face, int seed)
        {
            var deco = new GameObject(DecorName).transform;
            deco.SetParent(hex, false);
            var rng = new System.Random(seed);
            deco.localPosition = new Vector3(0f, Top, 0f);
            deco.localRotation = Quaternion.Euler(0f, rng.Next(6) * 60f, 0f);
            deco.localScale = Vector3.one * Radius;
            var c = Palette.DistrictColor(face.District);
            if (face.IsPlaza)
            {
                Plaza(deco, face, c);
                return;
            }
            switch (face.District)
            {
                case District.Quarry: Quarry(deco); break;
                case District.Houses: Houses(deco, c); break;
                case District.Markets: Markets(deco, c); break;
                case District.Barracks: Barracks(deco, c); break;
                case District.Temples: Temples(deco, c); break;
                case District.Gardens: Gardens(deco, rng); break;
            }
        }

        static void Plaza(Transform deco, HexFace face, Color c)
        {
            TileFx.Prim(deco, PrimitiveType.Cylinder, Mats.Lit(Darken(c, 0.8f), 0.3f), new Vector3(0f, 0.008f, 0f), new Vector3(1.34f, 0.008f, 1.34f));
            TileFx.Prim(deco, PrimitiveType.Cylinder, Mats.Lit(Palette.Marble, 0.45f), new Vector3(0f, 0.02f, 0f), new Vector3(1.16f, 0.012f, 1.16f));
            Vector3[] pos;
            switch (face.Stars)
            {
                case 1: pos = new[] { Vector3.zero }; break;
                case 2: pos = new[] { new Vector3(-0.24f, 0f, 0f), new Vector3(0.24f, 0f, 0f) }; break;
                default: pos = new[] { new Vector3(0f, 0f, 0.26f), new Vector3(-0.23f, 0f, -0.14f), new Vector3(0.23f, 0f, -0.14f) }; break;
            }
            var mat = Mats.Lit(Darken(c, 0.92f), 0.55f);
            foreach (var p in pos)
                Obj("Star", StarMesh(), mat, deco, p + new Vector3(0f, 0.03f, 0f), new Vector3(1.3f, 1.4f, 1.3f), Quaternion.identity);
        }

        static void Quarry(Transform deco)
        {
            var stone = Mats.Lit(Palette.Hex("#8D9096"), 0.15f);
            var light = Mats.Lit(Palette.Hex("#B4B6BA"), 0.2f);
            var dark = Mats.Lit(Palette.Hex("#6F737A"), 0.15f);
            TileFx.Rock(deco, stone, new Vector3(-0.22f, 0.1f, -0.1f), new Vector3(0.44f, 0.34f, 0.38f));
            TileFx.Rock(deco, light, new Vector3(0.25f, 0.07f, 0.16f), new Vector3(0.3f, 0.22f, 0.28f));
            TileFx.Rock(deco, dark, new Vector3(-0.02f, 0.05f, 0.36f), new Vector3(0.2f, 0.16f, 0.2f));
            TileFx.Box(deco, light, new Vector3(0.22f, 0.08f, -0.28f), new Vector3(0.22f, 0.16f, 0.18f)).localRotation = Quaternion.Euler(0f, 20f, 0f);
        }

        static void Houses(Transform deco, Color c)
        {
            var wall = Mats.Lit(Palette.Hex("#F6F2EA"), 0.2f);
            var roof = Mats.Lit(Darken(c, 0.85f), 0.35f);
            float[,] h = { { -0.28f, -0.14f, 0.34f, 0.3f }, { 0.24f, -0.2f, 0.3f, 0.42f }, { 0.02f, 0.3f, 0.36f, 0.26f } };
            for (int i = 0; i < 3; i++)
            {
                float x = h[i, 0], z = h[i, 1], w = h[i, 2], ht = h[i, 3];
                TileFx.Box(deco, wall, new Vector3(x, ht * 0.5f, z), new Vector3(w, ht, w));
                TileFx.Box(deco, roof, new Vector3(x, ht + 0.03f, z), new Vector3(w * 1.08f, 0.06f, w * 1.08f));
                TileFx.Box(deco, roof, new Vector3(x, 0.08f, z - w * 0.5f - 0.004f), new Vector3(0.08f, 0.14f, 0.01f));
            }
            TileFx.Prim(deco, PrimitiveType.Sphere, roof, new Vector3(0.24f, 0.48f, -0.2f), new Vector3(0.22f, 0.2f, 0.22f));
        }

        static void Markets(Transform deco, Color c)
        {
            var wood = Mats.Lit(Palette.Hex("#8A5A3B"), 0.2f);
            Vector3[] stalls = { new Vector3(-0.24f, 0f, -0.08f), new Vector3(0.26f, 0f, 0.14f) };
            Material[] canopies = { Mats.Lit(c, 0.3f), Mats.Lit(Palette.Cream, 0.3f) };
            for (int i = 0; i < 2; i++)
            {
                TileFx.Box(deco, wood, stalls[i] + new Vector3(0f, 0.06f, 0f), new Vector3(0.34f, 0.12f, 0.18f));
                TileFx.Prim(deco, PrimitiveType.Cylinder, wood, stalls[i] + new Vector3(0f, 0.2f, 0f), new Vector3(0.04f, 0.2f, 0.04f));
                TileFx.Prim(deco, PrimitiveType.Cylinder, canopies[i], stalls[i] + new Vector3(0f, 0.42f, 0f), new Vector3(0.5f, 0.03f, 0.5f));
            }
            TileFx.Box(deco, Mats.Lit(Palette.Hex("#C98A4B"), 0.2f), new Vector3(0.05f, 0.08f, -0.36f), new Vector3(0.16f, 0.16f, 0.16f))
                .localRotation = Quaternion.Euler(0f, 25f, 0f);
            TileFx.Prim(deco, PrimitiveType.Sphere, Mats.Lit(Palette.Hex("#B5653A"), 0.3f), new Vector3(-0.12f, 0.09f, 0.34f), new Vector3(0.14f, 0.18f, 0.14f));
        }

        static void Barracks(Transform deco, Color c)
        {
            var stone = Mats.Lit(Palette.Hex("#CDBFAE"), 0.2f);
            var accent = Mats.Lit(Darken(c, 0.9f), 0.3f);
            TileFx.Prim(deco, PrimitiveType.Cylinder, stone, new Vector3(0.2f, 0.36f, 0.05f), new Vector3(0.36f, 0.36f, 0.36f));
            TileFx.Prim(deco, PrimitiveType.Cylinder, accent, new Vector3(0.2f, 0.73f, 0.05f), new Vector3(0.4f, 0.02f, 0.4f));
            for (int k = 0; k < 4; k++)
            {
                float a = Mathf.Deg2Rad * (45f + 90f * k);
                TileFx.Box(deco, stone, new Vector3(0.2f + Mathf.Cos(a) * 0.15f, 0.79f, 0.05f + Mathf.Sin(a) * 0.15f), Vector3.one * 0.09f);
            }
            TileFx.Prim(deco, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#7A5232"), 0.2f), new Vector3(0.2f, 0.95f, 0.05f), new Vector3(0.02f, 0.15f, 0.02f));
            TileFx.Box(deco, accent, new Vector3(0.29f, 1.02f, 0.05f), new Vector3(0.16f, 0.1f, 0.01f));
            TileFx.Box(deco, stone, new Vector3(-0.2f, 0.14f, -0.05f), new Vector3(0.55f, 0.28f, 0.18f));
            TileFx.Box(deco, accent, new Vector3(-0.2f, 0.17f, -0.145f), new Vector3(0.12f, 0.12f, 0.02f));
        }

        static void Temples(Transform deco, Color c)
        {
            var marble = Mats.Lit(Palette.Hex("#F7F4EE"), 0.35f);
            var roof = Mats.Lit(Darken(c, 0.95f), 0.35f);
            TileFx.Box(deco, marble, new Vector3(0f, 0.05f, 0f), new Vector3(0.78f, 0.1f, 0.56f));
            TileFx.Box(deco, marble, new Vector3(0f, 0.12f, 0f), new Vector3(0.68f, 0.04f, 0.48f));
            for (int x = -1; x <= 1; x++)
                for (int z = -1; z <= 1; z += 2)
                    TileFx.Prim(deco, PrimitiveType.Cylinder, marble, new Vector3(0.26f * x, 0.29f, 0.17f * z), new Vector3(0.08f, 0.16f, 0.08f));
            TileFx.Box(deco, roof, new Vector3(0f, 0.48f, 0f), new Vector3(0.76f, 0.06f, 0.54f));
            TileFx.Box(deco, roof, new Vector3(0f, 0.52f, 0f), new Vector3(0.78f, 0.2f, 0.2f)).localRotation = Quaternion.Euler(45f, 0f, 0f);
        }

        static void Gardens(Transform deco, System.Random rng)
        {
            var trunk = Mats.Lit(Palette.Hex("#7A5232"), 0.2f);
            var leaf = TileFx.Foliage(Palette.Hex("#4E9A4B")) ?? Mats.Lit(Palette.Hex("#4E9A4B"), 0.25f);
            var light = TileFx.Foliage(Palette.Hex("#6DB35F")) ?? Mats.Lit(Palette.Hex("#6DB35F"), 0.25f);
            var cypress = TileFx.Foliage(Palette.Hex("#2F6B3A")) ?? Mats.Lit(Palette.Hex("#2F6B3A"), 0.25f);
            TileFx.Prim(deco, PrimitiveType.Cylinder, trunk, new Vector3(-0.22f, 0.1f, -0.12f), new Vector3(0.06f, 0.1f, 0.06f));
            TileFx.Blob(deco, leaf, new Vector3(-0.22f, 0.32f, -0.12f), new Vector3(0.42f, 0.34f, 0.42f));
            TileFx.Prim(deco, PrimitiveType.Cylinder, trunk, new Vector3(0.27f, 0.06f, -0.04f), new Vector3(0.04f, 0.06f, 0.04f));
            TileFx.Blob(deco, cypress, new Vector3(0.27f, 0.4f, -0.04f), new Vector3(0.2f, 0.66f, 0.2f));
            TileFx.Blob(deco, light, new Vector3(0.02f, 0.1f, 0.3f), new Vector3(0.32f, 0.2f, 0.32f));
            string[] petals = { "#FFFFFF", "#F4A6C0", "#FFD95A" };
            for (int i = 0; i < 3; i++)
            {
                var at = new Vector3((float)rng.NextDouble() * 0.3f - 0.05f, 0.04f, (float)rng.NextDouble() * 0.2f - 0.25f);
                TileFx.Prim(deco, PrimitiveType.Sphere, Mats.Lit(Palette.Hex(petals[i]), 0.2f), at, new Vector3(0.07f, 0.05f, 0.07f));
            }
        }

        static Mesh StarMesh()
        {
            if (star != null) return star;
            const float outer = 0.16f, inner = 0.07f, h = 0.06f;
            var pts = new Vector3[10];
            for (int i = 0; i < 10; i++)
            {
                float a = Mathf.Deg2Rad * (90f + 36f * i);
                float r = i % 2 == 0 ? outer : inner;
                pts[i] = new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
            }
            var v = new List<Vector3>();
            var t = new List<int>();
            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                int i = v.Count;
                v.Add(a); v.Add(b); v.Add(c);
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
            }
            var up = Vector3.up * h;
            for (int i = 0; i < 10; i++)
            {
                Vector3 a = pts[i], b = pts[(i + 1) % 10];
                Tri(up, b + up, a + up);
                Tri(a + up, b + up, b);
                Tri(a + up, b, a);
            }
            star = new Mesh { name = "Star" };
            star.SetVertices(v);
            star.SetTriangles(t, 0);
            star.RecalculateNormals();
            star.RecalculateBounds();
            return star;
        }
    }
}
