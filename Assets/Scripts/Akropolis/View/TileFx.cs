using System.Collections.Generic;
using UnityEngine;

namespace Akropolis.View
{
    /// Per-habitat details added on top of placed tokens: water surface, wheat in the wind, chimney smoke.
    /// Shaders live in Resources/Shaders; tweak their material values here or in the .shader files.
    public static class TileFx
    {
        public const string WaterName = "Water";
        public const string WheatName = "DecorWheat";
        /// Decorations an animal would stand in: removed when a cube lands, rebuilt if it is undone.
        public const string DecorPrefix = "Decor";

        public static Material water, wheat, smoke, grass;
        static Mesh wheatMesh, quad;
        static readonly Dictionary<Color, Material> foliage = new Dictionary<Color, Material>();
        static Mesh[] rocks, blobs;
        static Mesh peak;

        // Domain reload is disabled: runtime materials/meshes are destroyed when leaving Play mode.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            water = wheat = smoke = grass = null;
            wheatMesh = quad = null;
            foliage.Clear();
            rocks = blobs = null;
            peak = null;
        }

        public static Material Mat(ref Material cache, string shader)
        {
            if (cache != null) return cache;
            var s = Shader.Find(shader);
            if (s == null)
            {
                Debug.LogWarning($"[TileFx] Shader {shader} introuvable.");
                return null;
            }
            return cache = new Material(s);
        }

        /// Animated water over the token's top face.
        public static void AddWater(Transform token, float top)
        {
            var mat = Mat(ref water, "Akropolis/Water");
            if (mat == null) return;
            var go = new GameObject(WaterName);
            go.transform.SetParent(token, false);
            go.transform.localPosition = new Vector3(0f, top + 0.012f, 0f);
            go.transform.localScale = new Vector3(0.5f * 0.86f, 1f, 0.5f * 0.86f);
            go.AddComponent<MeshFilter>().sharedMesh = HexMesh.Flat;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// Water strip spanning the gap between two neighbouring water tiles (same world-space waves).
        public static void AddWaterBridge(Transform parent, Vector3 pos, Quaternion rot, float width, float length)
        {
            var mat = Mat(ref water, "Akropolis/Water");
            if (mat == null) return;
            if (quad == null)
            {
                quad = new Mesh { name = "FlatQuad" };
                quad.vertices = new[] { new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f) };
                quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                quad.RecalculateNormals();
                quad.RecalculateBounds();
            }
            var go = new GameObject("WaterBridge");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = rot;
            go.transform.localScale = new Vector3(width, 1f, length);
            go.AddComponent<MeshFilter>().sharedMesh = quad;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        }

        /// A tuft of wheat blades that bend with the wind (vertex shader).
        public static void AddWheat(Transform token, float top)
        {
            var mat = Mat(ref wheat, "Akropolis/Wheat");
            if (mat == null) return;
            var go = new GameObject(WheatName);
            go.transform.SetParent(token, false);
            go.transform.localPosition = new Vector3(0f, top, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            go.AddComponent<MeshFilter>().sharedMesh = WheatMesh();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Mover.Of(go.transform).Appear(0.05f, 0.35f);
        }

        /// Brick chimney on the roof with a thin trail of smoke.
        public static void AddChimney(Transform token, float roofTop)
        {
            var chimney = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Object.Destroy(chimney.GetComponent<Collider>());
            chimney.name = "Chimney";
            chimney.transform.SetParent(token, false);
            chimney.transform.localPosition = new Vector3(0.17f, roofTop + 0.09f, -0.04f);
            chimney.transform.localScale = new Vector3(0.09f, 0.24f, 0.09f);
            chimney.GetComponent<Renderer>().sharedMaterial = Mats.Lit(Palette.Hex("#7A3B2E"), 0.15f);

            var mat = Mat(ref smoke, "Akropolis/Smoke");
            if (mat == null) return;
            var go = new GameObject("Smoke");
            go.transform.SetParent(token, false);
            go.transform.localPosition = new Vector3(0.17f, roofTop + 0.23f, -0.04f);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.duration = 5f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2f, 2.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.11f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new Color(0.93f, 0.93f, 0.93f, 1f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 24;

            var emission = ps.emission;
            emission.rateOverTime = 4.5f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = 0.015f;

            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.04f, 0.09f); // the same breeze as the wheat
            vel.y = new ParticleSystem.MinMaxCurve(0.02f, 0.05f);
            vel.z = new ParticleSystem.MinMaxCurve(0.01f, 0.03f);

            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 2.4f));

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(new Color(0.95f, 0.95f, 0.95f), 0f), new GradientColorKey(new Color(0.8f, 0.82f, 0.85f), 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.85f, 0.12f), new GradientAlphaKey(0.45f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = gradient;

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = mat;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            ps.Play();
        }

        /// Removes wheat, canopy or rocks when an animal settles on the token so the figure does not stand in them.
        public static void ClearForCube(Transform token)
        {
            for (int i = token.childCount - 1; i >= 0; i--)
                if (token.GetChild(i).name.StartsWith(DecorPrefix)) Object.Destroy(token.GetChild(i).gameObject);
        }

        // ---------------- Mountains: pebbles, then a crag, then a snowy peak ----------------

        public static void AddMountain(Transform token, float top, int height)
        {
            var root = DecorRoot(token, "DecorMountain", top);
            var stone = Mats.Lit(Palette.Hex("#8D9096"), 0.15f);
            var dark = Mats.Lit(Palette.Hex("#6F737A"), 0.15f);
            var light = Mats.Lit(Palette.Hex("#B4B6BA"), 0.2f);
            switch (height)
            {
                case 1:
                    for (int i = 0; i < 3; i++)
                    {
                        float a = i * 2.1f + Random.Range(-0.4f, 0.4f);
                        float s = Random.Range(0.07f, 0.11f);
                        Rock(root, i % 2 == 0 ? stone : light, new Vector3(Mathf.Cos(a) * 0.22f, s * 0.3f, Mathf.Sin(a) * 0.22f),
                            new Vector3(s, s * 0.75f, s));
                    }
                    break;
                case 2:
                    Rock(root, stone, new Vector3(-0.04f, 0.08f, 0.02f), new Vector3(0.22f, 0.24f, 0.2f));
                    Rock(root, dark, new Vector3(0.17f, 0.04f, -0.08f), new Vector3(0.12f, 0.1f, 0.12f));
                    Rock(root, light, new Vector3(-0.2f, 0.03f, -0.12f), Vector3.one * 0.08f);
                    break;
                default:
                    var p = Part(root, "Peak", Peak(), stone, new Vector3(0f, -0.01f, 0f), new Vector3(0.36f, 0.44f, 0.36f));
                    p.localRotation = Quaternion.Euler(0f, Random.Range(0f, 60f), 0f);
                    var snow = Part(root, "Snow", Peak(), Mats.Lit(Color.white, 0.55f), new Vector3(0f, 0.225f, 0f),
                        new Vector3(0.18f, 0.205f, 0.18f));
                    snow.localRotation = p.localRotation;
                    Rock(root, dark, new Vector3(0.22f, 0.03f, 0.1f), Vector3.one * 0.08f);
                    break;
            }
            Mover.Of(root).Appear(0.05f, 0.35f);
        }

        // ---------------- Trees: a bush, then a crown, then a big crown ----------------

        public static void AddCanopy(Transform token, float top, int height)
        {
            var root = DecorRoot(token, "DecorCanopy", top);
            var baseColor = Palette.Hex("#4E9A4B");
            var mid = Foliage(baseColor);
            var lightLeaf = Foliage(Color.Lerp(baseColor, new Color(0.85f, 0.95f, 0.45f), 0.3f));
            var darkLeaf = Foliage(Color.Lerp(baseColor, Color.black, 0.2f));
            switch (height)
            {
                case 1:
                    Blob(root, mid, new Vector3(-0.07f, 0.07f, 0.03f), new Vector3(0.24f, 0.17f, 0.24f));
                    Blob(root, lightLeaf, new Vector3(0.11f, 0.05f, -0.06f), new Vector3(0.17f, 0.13f, 0.17f));
                    break;
                case 2:
                    Blob(root, mid, new Vector3(0f, 0.2f, 0f), new Vector3(0.44f, 0.36f, 0.44f));
                    Blob(root, lightLeaf, new Vector3(0.14f, 0.3f, -0.08f), Vector3.one * 0.24f);
                    Blob(root, darkLeaf, new Vector3(-0.15f, 0.12f, 0.06f), Vector3.one * 0.22f);
                    break;
                default:
                    Blob(root, mid, new Vector3(0f, 0.3f, 0f), new Vector3(0.58f, 0.48f, 0.58f));
                    Blob(root, lightLeaf, new Vector3(0.12f, 0.5f, -0.06f), Vector3.one * 0.32f);
                    Blob(root, darkLeaf, new Vector3(-0.22f, 0.2f, 0.1f), Vector3.one * 0.3f);
                    Blob(root, lightLeaf, new Vector3(0.2f, 0.18f, 0.14f), Vector3.one * 0.26f);
                    break;
            }
            Mover.Of(root).Appear(0.05f, 0.4f);
        }

        static Transform DecorRoot(Transform token, string name, float top)
        {
            var go = new GameObject(name);
            go.transform.SetParent(token, false);
            go.transform.localPosition = new Vector3(0f, top, 0f);
            go.transform.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            return go.transform;
        }

        public static Transform Part(Transform parent, string name, Mesh mesh, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go.transform;
        }

        public static void Rock(Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            if (rocks == null) rocks = new[] { LowPolySphere(0, 0.22f, 11), LowPolySphere(0, 0.22f, 23), LowPolySphere(0, 0.22f, 37) };
            var r = Part(parent, "Rock", rocks[Random.Range(0, rocks.Length)], mat, pos, scale);
            r.localRotation = Quaternion.Euler(Random.Range(-10f, 10f), Random.Range(0f, 360f), Random.Range(-10f, 10f));
        }

        public static void Blob(Transform parent, Material mat, Vector3 pos, Vector3 scale)
        {
            if (mat == null) return;
            if (blobs == null) blobs = new[] { LowPolySphere(1, 0.1f, 5), LowPolySphere(1, 0.1f, 17) };
            var b = Part(parent, "Leaves", blobs[Random.Range(0, blobs.Length)], mat, pos, scale);
            b.localRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        public static Material Foliage(Color color)
        {
            if (foliage.TryGetValue(color, out var m) && m != null) return m;
            var s = Shader.Find("Akropolis/Foliage");
            if (s == null) return null;
            m = new Material(s);
            m.SetColor("_Color", color);
            return foliage[color] = m;
        }

        /// Icosahedron (optionally subdivided once) with jittered vertices and flat shading, radius 0.5.
        static Mesh LowPolySphere(int subdivisions, float jitter, int seed)
        {
            float t = (1f + Mathf.Sqrt(5f)) * 0.5f;
            var v = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1),
            };
            var f = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1,
            };
            for (int s = 0; s < subdivisions; s++)
            {
                var mid = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (mid.TryGetValue(key, out int i)) return i;
                    v.Add((v[a] + v[b]) * 0.5f);
                    return mid[key] = v.Count - 1;
                }
                var nf = new List<int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2];
                    int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = nf;
            }
            var rng = new System.Random(seed);
            for (int i = 0; i < v.Count; i++)
                v[i] = v[i].normalized * 0.5f * (1f + ((float)rng.NextDouble() * 2f - 1f) * jitter);
            return FlatMesh(v, f, "LowPolySphere");
        }

        public static Mesh Peak()
        {
            if (peak != null) return peak;
            var v = new List<Vector3> { new Vector3(0.04f, 1f, -0.03f) };
            var rng = new System.Random(3);
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f;
                float r = 1f + ((float)rng.NextDouble() - 0.5f) * 0.25f;
                v.Add(new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
            var f = new List<int>();
            for (int i = 0; i < 6; i++) f.AddRange(new[] { 0, 1 + (i + 1) % 6, 1 + i });
            return peak = FlatMesh(v, f, "Peak");
        }

        static Mesh FlatMesh(List<Vector3> v, List<int> f, string name)
        {
            // Make every face point away from the shape's center, whatever the source winding.
            var center = Vector3.zero;
            foreach (var p in v) center += p;
            center /= v.Count;
            var verts = new Vector3[f.Count];
            var tris = new int[f.Count];
            for (int i = 0; i < f.Count; i += 3)
            {
                Vector3 a = v[f[i]], b = v[f[i + 1]], c = v[f[i + 2]];
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - center) < 0f) (b, c) = (c, b);
                verts[i] = a;
                verts[i + 1] = b;
                verts[i + 2] = c;
                tris[i] = i;
                tris[i + 1] = i + 1;
                tris[i + 2] = i + 2;
            }
            var mesh = new Mesh { name = name };
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        /// How high an animal should stand so it sits on the tile's details instead of inside them.
        public static float DecorLift(Transform token, float top)
        {
            float lift = 0f;
            for (int i = 0; i < token.childCount; i++)
            {
                var child = token.GetChild(i);
                if (!child.name.StartsWith(DecorPrefix)) continue;
                // Wheat and soil are soft: the figure sinks into them a little.
                float share = child.name == WheatName || child.name == "DecorSoil" ? 0.45f : 0.85f;
                foreach (var r in child.GetComponentsInChildren<Renderer>())
                {
                    if (r is ParticleSystemRenderer) continue;
                    var mf = r.GetComponent<MeshFilter>();
                    if (mf == null || mf.sharedMesh == null) continue;
                    var b = mf.sharedMesh.bounds;
                    float y = token.InverseTransformPoint(r.transform.TransformPoint(b.center + Vector3.up * b.extents.y)).y;
                    lift = Mathf.Max(lift, (y - top) * share);
                }
            }
            return lift;
        }

        // ---------------- Red alone: house foundations ----------------

        public static void AddFoundation(Transform token, float top)
        {
            var root = DecorRoot(token, "DecorFoundation", top);
            var stone = Mats.Lit(Palette.Hex("#BDB2A2"), 0.15f);
            var dark = Mats.Lit(Palette.Hex("#8F8577"), 0.15f);
            var earth = Mats.Lit(Palette.Hex("#7E5B3C"), 0.1f);
            Box(root, earth, new Vector3(0f, 0.004f, 0f), new Vector3(0.46f, 0.008f, 0.32f));
            Box(root, stone, new Vector3(0f, 0.03f, 0.17f), new Vector3(0.54f, 0.06f, 0.05f));
            Box(root, stone, new Vector3(0f, 0.03f, -0.17f), new Vector3(0.54f, 0.06f, 0.05f));
            Box(root, stone, new Vector3(0.25f, 0.03f, 0f), new Vector3(0.05f, 0.06f, 0.3f));
            Box(root, stone, new Vector3(-0.25f, 0.025f, 0.07f), new Vector3(0.05f, 0.05f, 0.16f)); // doorway gap
            foreach (var c in new[] { new Vector3(0.25f, 0f, 0.17f), new Vector3(-0.25f, 0f, 0.17f), new Vector3(0.25f, 0f, -0.17f), new Vector3(-0.25f, 0f, -0.17f) })
                Box(root, dark, c + Vector3.up * 0.045f, new Vector3(0.075f, 0.09f, 0.075f));
            var brick = Mats.Lit(Palette.Hex("#B4513F"), 0.15f);
            Box(root, brick, new Vector3(0.05f, 0.015f, 0.02f), new Vector3(0.08f, 0.03f, 0.04f)).localRotation = Quaternion.Euler(0f, 25f, 0f);
            Box(root, brick, new Vector3(-0.08f, 0.015f, -0.06f), new Vector3(0.08f, 0.03f, 0.04f)).localRotation = Quaternion.Euler(0f, -40f, 0f);
            Mover.Of(root).Appear(0.05f, 0.35f);
        }

        // ---------------- Brown: tilled soil, then a sapling on two trunks ----------------

        public static void AddSoil(Transform token, float top, int height)
        {
            var root = DecorRoot(token, height >= 2 ? "DecorSapling" : "DecorSoil", top);
            var soil = Mats.Lit(Palette.Hex("#6A4427"), 0.05f);
            var furrow = Mats.Lit(Palette.Hex("#55341C"), 0.05f);
            if (height < 2)
            {
                for (int i = -1; i <= 1; i++)
                {
                    var f = Box(root, furrow, new Vector3(0f, 0.01f, i * 0.12f), new Vector3(0.5f, 0.02f, 0.045f));
                    f.localScale = new Vector3(0.5f - Mathf.Abs(i) * 0.12f, 0.02f, 0.045f);
                }
                for (int i = 0; i < 3; i++)
                    Rock(root, soil, new Vector3(Random.Range(-0.2f, 0.2f), 0.01f, Random.Range(-0.18f, 0.18f)), new Vector3(0.14f, 0.04f, 0.1f));
                Rock(root, Mats.Lit(Palette.Hex("#9A9187"), 0.15f), new Vector3(0.18f, 0.015f, 0.16f), Vector3.one * 0.045f);
            }
            else
            {
                Rock(root, soil, new Vector3(0f, 0.01f, 0f), new Vector3(0.24f, 0.06f, 0.24f));
                var trunk = Prim(root, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#7A5232"), 0.2f), new Vector3(0f, 0.1f, 0f), new Vector3(0.035f, 0.09f, 0.035f));
                trunk.localRotation = Quaternion.Euler(4f, 0f, -3f);
                var leaf = Foliage(Color.Lerp(Palette.Hex("#4E9A4B"), new Color(0.8f, 0.95f, 0.4f), 0.35f));
                Blob(root, leaf, new Vector3(0.03f, 0.21f, 0f), new Vector3(0.12f, 0.1f, 0.12f));
                Blob(root, leaf, new Vector3(-0.05f, 0.17f, 0.03f), new Vector3(0.08f, 0.07f, 0.08f));
            }
            Mover.Of(root).Appear(0.05f, 0.35f);
        }

        // ---------------- Player board surroundings ----------------

        /// One small, low piece of scenery (grass, flowers, pebbles, mushroom) at a spot around a board.
        public static Transform BoardDecor(Transform parent, Vector3 pos, System.Random rng)
        {
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject("BoardDecor").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            double kind = rng.NextDouble();
            if (kind < 0.4)
            {
                var mat = GrassMat();
                if (mat != null)
                {
                    var tuft = Part(root, "Grass", WheatMesh(), mat, Vector3.zero, Vector3.one * Rand(0.45f, 0.65f));
                    tuft.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            else if (kind < 0.7)
            {
                var petals = new[] { "#FFFFFF", "#FFD95A", "#F4A6C0", "#C9A7F0", "#FF8A65" };
                var stem = Mats.Lit(Palette.Hex("#4E7A35"), 0.1f);
                int count = rng.Next(2, 4);
                for (int i = 0; i < count; i++)
                {
                    var at = new Vector3(Rand(-0.09f, 0.09f), 0f, Rand(-0.09f, 0.09f));
                    float h = Rand(0.07f, 0.12f);
                    Prim(root, PrimitiveType.Cylinder, stem, at + Vector3.up * h * 0.5f, new Vector3(0.012f, h * 0.5f, 0.012f));
                    var head = Mats.Lit(Palette.Hex(petals[rng.Next(petals.Length)]), 0.2f);
                    Prim(root, PrimitiveType.Sphere, head, at + Vector3.up * h, new Vector3(0.05f, 0.03f, 0.05f));
                }
            }
            else if (kind < 0.9)
            {
                var stone = Mats.Lit(Palette.Hex(rng.NextDouble() < 0.5 ? "#A39C93" : "#8D9096"), 0.15f);
                Rock(root, stone, new Vector3(0f, 0.02f, 0f), new Vector3(0.11f, 0.07f, 0.09f));
                Rock(root, stone, new Vector3(0.09f, 0.01f, 0.05f), Vector3.one * 0.05f);
            }
            else
            {
                Prim(root, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#F3EAD7"), 0.2f), new Vector3(0f, 0.03f, 0f), new Vector3(0.025f, 0.03f, 0.025f));
                Prim(root, PrimitiveType.Sphere, Mats.Lit(Palette.Hex("#D9473A"), 0.35f), new Vector3(0f, 0.065f, 0f), new Vector3(0.09f, 0.05f, 0.09f));
                Prim(root, PrimitiveType.Sphere, Mats.Lit(Palette.Hex("#E2B07A"), 0.2f), new Vector3(0.07f, 0.03f, 0.04f), new Vector3(0.05f, 0.03f, 0.05f));
            }
            return root;
        }

        /// Bigger table scenery in the style of the landscape around the table and of the token details.
        /// size 1: bush, crop patch, rocks, log, pond. size 2 (kept far from boards): tree, pine, house, grove.
        public static Transform TableProp(Transform parent, Vector3 pos, System.Random rng, int size)
        {
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject(size == 2 ? "TableProp L" : "TableProp M").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            root.localRotation = Quaternion.Euler(0f, Rand(0f, 360f), 0f);
            var trunk = Mats.Lit(Palette.Hex("#8A5A3B"), 0.15f);
            Color[] leaves = { Palette.Hex("#4E9A4B"), Palette.Hex("#5DAA55"), Palette.Hex("#3F8442") };
            Material Leaf() => Foliage(leaves[rng.Next(leaves.Length)]) ?? Mats.Lit(leaves[0], 0.25f);
            Transform Prism(Material mat, Vector3 at, float r, float h) =>
                Part(root, "Prism", HexMesh.Prism, mat, at, new Vector3(r, h, r));

            void RoundTree(Vector3 at, float s)
            {
                int trunks = rng.NextDouble() < 0.5 ? 1 : 2;
                for (int i = 0; i < trunks; i++) Prism(trunk, at + Vector3.up * (i * 0.18f * s), 0.12f * s, 0.18f * s);
                Prism(Leaf(), at + Vector3.up * (trunks * 0.18f * s), 0.38f * s, 0.34f * s);
            }

            if (size >= 2)
            {
                double k = rng.NextDouble();
                if (k < 0.35) RoundTree(Vector3.zero, Rand(1f, 1.3f));
                else if (k < 0.65)
                {
                    float s = Rand(1f, 1.3f);
                    Prism(trunk, Vector3.zero, 0.1f * s, 0.16f * s);
                    float y = 0.16f * s;
                    foreach (float r in new[] { 0.4f, 0.3f, 0.18f })
                    {
                        Prism(Foliage(Palette.Hex("#3D7A4A")) ?? trunk, Vector3.up * y, r * s, 0.24f * s);
                        y += 0.2f * s;
                    }
                }
                else if (k < 0.82)
                {
                    // Same build as the buildings: grey base, red walls, pitched roof, chimney.
                    Prism(Mats.Lit(Palette.Hex("#9EA3A8"), 0.25f), Vector3.zero, 0.34f, 0.18f);
                    Prism(Mats.Lit(Palette.Hex("#C8483A"), 0.2f), Vector3.up * 0.18f, 0.34f, 0.22f);
                    var roofMat = Mats.Lit(Palette.Hex("#8E2F25"), 0.2f);
                    Box(root, roofMat, Vector3.up * 0.44f, new Vector3(0.52f, 0.25f, 0.25f)).localRotation = Quaternion.Euler(45f, 0f, 0f);
                    Box(root, roofMat, new Vector3(0.14f, 0.56f, 0.04f), new Vector3(0.07f, 0.18f, 0.07f));
                }
                else
                {
                    RoundTree(new Vector3(-0.3f, 0f, 0.1f), Rand(0.8f, 1f));
                    RoundTree(new Vector3(0.32f, 0f, -0.12f), Rand(0.65f, 0.85f));
                    Blob(root, Leaf(), new Vector3(0.05f, 0.06f, 0.35f), new Vector3(0.28f, 0.18f, 0.28f));
                }
                return root;
            }

            double m = rng.NextDouble();
            if (m < 0.3)
            {
                Blob(root, Leaf(), new Vector3(0f, 0.1f, 0f), new Vector3(0.36f, 0.26f, 0.36f));
                Blob(root, Leaf(), new Vector3(0.2f, 0.07f, 0.08f), new Vector3(0.24f, 0.18f, 0.24f));
                Blob(root, Leaf(), new Vector3(-0.15f, 0.06f, -0.14f), new Vector3(0.2f, 0.15f, 0.2f));
            }
            else if (m < 0.5)
            {
                var crop = Mats.Lit(Palette.Hex("#D9A92E"), 0.2f);
                var wheatMat = Mat(ref wheat, "Akropolis/Wheat");
                for (int i = -1; i <= 1; i++)
                {
                    Box(root, crop, new Vector3(0f, 0.03f, i * 0.2f), new Vector3(i == 0 ? 0.8f : 0.6f, 0.06f, 0.08f));
                    if (wheatMat != null)
                        Part(root, "Wheat", WheatMesh(), wheatMat, new Vector3(Rand(-0.2f, 0.2f), 0.05f, i * 0.2f), Vector3.one * 0.55f)
                            .GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
            }
            else if (m < 0.7)
            {
                var stone = Mats.Lit(Palette.Hex("#9EA3A8"), 0.25f);
                var dark = Mats.Lit(Palette.Hex("#7E8389"), 0.2f);
                Rock(root, stone, new Vector3(0f, 0.08f, 0f), new Vector3(0.3f, 0.26f, 0.26f));
                Rock(root, dark, new Vector3(0.24f, 0.04f, 0.1f), new Vector3(0.16f, 0.13f, 0.15f));
                Rock(root, stone, new Vector3(-0.2f, 0.03f, -0.14f), Vector3.one * 0.11f);
            }
            else if (m < 0.85)
            {
                var log = Prim(root, PrimitiveType.Cylinder, trunk, new Vector3(0f, 0.07f, 0f), new Vector3(0.14f, 0.3f, 0.14f));
                log.localRotation = Quaternion.Euler(0f, 0f, 90f);
                Prim(root, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#E2C49A"), 0.1f), new Vector3(0.301f, 0.07f, 0f), new Vector3(0.12f, 0.005f, 0.12f))
                    .localRotation = Quaternion.Euler(0f, 0f, 90f);
                Prim(root, PrimitiveType.Sphere, Mats.Lit(Palette.Hex("#D9473A"), 0.35f), new Vector3(-0.1f, 0.16f, 0.03f), new Vector3(0.07f, 0.04f, 0.07f));
            }
            else
            {
                var waterMat = Mat(ref water, "Akropolis/Water");
                Part(root, "PondRim", HexMesh.Prism, Mats.Lit(Palette.Hex("#B8A88A"), 0.15f), Vector3.zero, new Vector3(0.52f, 0.04f, 0.52f));
                if (waterMat != null) Part(root, "Pond", HexMesh.Flat, waterMat, Vector3.up * 0.045f, new Vector3(0.46f, 1f, 0.46f));
                var lily = Mats.Lit(Palette.Hex("#5DAA55"), 0.3f);
                Prim(root, PrimitiveType.Cylinder, lily, new Vector3(0.12f, 0.055f, 0.05f), new Vector3(0.12f, 0.004f, 0.12f));
                Prim(root, PrimitiveType.Cylinder, lily, new Vector3(-0.1f, 0.055f, -0.12f), new Vector3(0.09f, 0.004f, 0.09f));
            }
            return root;
        }

        static Material GrassMat()
        {
            if (grass != null) return grass;
            var s = Shader.Find("Akropolis/Wheat");
            if (s == null) return null;
            grass = new Material(s);
            grass.SetColor("_Base", Palette.Hex("#3F6B2D"));
            grass.SetColor("_Tip", Palette.Hex("#A2D163"));
            grass.SetFloat("_WindStrength", 0.03f);
            return grass;
        }

        public static Transform Box(Transform parent, Material mat, Vector3 pos, Vector3 scale) =>
            Prim(parent, PrimitiveType.Cube, mat, pos, scale);

        public static Transform Prim(Transform parent, PrimitiveType type, Material mat, Vector3 pos, Vector3 scale)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
            return go.transform;
        }

        public static Mesh WheatMesh()
        {
            if (wheatMesh != null) return wheatMesh;
            var rng = new System.Random(7);
            float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            var verts = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            const float area = 0.5f * 0.74f;
            for (int i = 0; i < 90; i++)
            {
                // Uniform point inside the top face (disc approximation of the hex).
                float a = Rand(0f, Mathf.PI * 2f);
                float d = Mathf.Sqrt(Rand(0f, 1f)) * area;
                var basePos = new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
                float h = Rand(0.13f, 0.22f);
                float w = Rand(0.016f, 0.026f);
                float yaw = Rand(0f, Mathf.PI);
                var side = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * w;
                var lean = new Vector3(Rand(-0.03f, 0.03f), 0f, Rand(-0.03f, 0.03f));
                float phase = Rand(0f, 1f);

                // Blade: two base vertices, a narrower middle, and the ear at the tip.
                int v0 = verts.Count;
                verts.Add(basePos - side);
                verts.Add(basePos + side);
                verts.Add(basePos - side * 0.7f + Vector3.up * h * 0.6f + lean * 0.5f);
                verts.Add(basePos + side * 0.7f + Vector3.up * h * 0.6f + lean * 0.5f);
                verts.Add(basePos + Vector3.up * h + lean);
                uvs.Add(new Vector2(phase, 0f));
                uvs.Add(new Vector2(phase, 0f));
                uvs.Add(new Vector2(phase, 0.6f));
                uvs.Add(new Vector2(phase, 0.6f));
                uvs.Add(new Vector2(phase, 1f));
                tris.AddRange(new[] { v0, v0 + 2, v0 + 1, v0 + 1, v0 + 2, v0 + 3, v0 + 2, v0 + 4, v0 + 3 });
            }
            wheatMesh = new Mesh { name = "WheatTuft" };
            wheatMesh.SetVertices(verts);
            wheatMesh.SetUVs(0, uvs);
            wheatMesh.SetTriangles(tris, 0);
            wheatMesh.RecalculateNormals();
            // Bounds include the wind sway so the tuft is never culled while bending.
            var b = wheatMesh.bounds;
            b.Expand(0.15f);
            wheatMesh.bounds = b;
            return wheatMesh;
        }
    }
}
