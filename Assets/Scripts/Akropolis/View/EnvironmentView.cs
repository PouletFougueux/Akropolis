using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Akropolis.View
{
    /// Landscape around the table, inspired by both board sides: terraced hex land with contour-like steps,
    /// lakes, forests, fields and hamlets (side A) opening onto a turquoise lagoon (side B).
    /// Static geometry is merged into one mesh per material.
    public sealed class EnvironmentView : MonoBehaviour
    {
        const float Tile = 1.8f;
        const float Step = 0.4f;
        const float Ground = -0.52f;
        const float SeaLevel = -2.2f;
        const float Bottom = -3.6f;
        // Tiles closer than InnerRadius are fully hidden under the table (apothem 14.5).
        const float InnerRadius = 12.5f, FlatRadius = 20f, OuterRadius = 64f;
        const float BayAngle = 25f;

        enum Biome { Meadow, Forest, Field, Lake, Beach, Rock, Snow }

        sealed class Bird
        {
            public Transform Root, WingL, WingR;
            public float Radius, Height, Speed, Angle, Phase;
        }

        readonly Dictionary<Material, List<CombineInstance>> batches = new Dictionary<Material, List<CombineInstance>>();
        readonly List<Bird> birds = new List<Bird>();
        System.Random rng;
        Mesh cube;
        Transform clouds;

        Material earth, sandSide, rockSide, beach, rock, snow, lake, cropRow, trunk, pine, houseBase, houseWall, roof, lily;
        Material[] meadows, forests, fields, canopies;

        void Awake()
        {
            rng = new System.Random(20240);
            var probe = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube = probe.GetComponent<MeshFilter>().sharedMesh;
            Destroy(probe);

            CreateMaterials();
            SetupAtmosphere();
            BuildSea();
            BuildLand();
            Flush("Land", transform, ShadowCastingMode.On);
            BuildClouds();
            BuildBirds();
        }

        void Update()
        {
            if (clouds != null) clouds.localRotation = Quaternion.Euler(0f, Time.time * 0.8f, 0f);

            float t = Time.time;
            foreach (var b in birds)
            {
                b.Angle += b.Speed * Time.deltaTime;
                float sin = Mathf.Sin(b.Angle), cos = Mathf.Cos(b.Angle);
                b.Root.localPosition = new Vector3(cos * b.Radius, b.Height + Mathf.Sin(t * 0.5f + b.Phase) * 0.6f, sin * b.Radius);
                b.Root.localRotation = Quaternion.LookRotation(new Vector3(-sin, 0f, cos) * Mathf.Sign(b.Speed));
                float flap = Mathf.Sin(t * 9f + b.Phase) * 35f;
                b.WingL.localRotation = Quaternion.Euler(0f, 0f, flap);
                b.WingR.localRotation = Quaternion.Euler(0f, 0f, -flap);
            }
        }

        // ---------------- Setup ----------------

        void CreateMaterials()
        {
            Material M(string hex, float smooth = 0.15f) => Mats.Lit(Palette.Hex(hex), smooth);
            earth = M("#B8875A");
            sandSide = M("#D9B77E");
            rockSide = M("#8E9398");
            beach = M("#EED9A8");
            rock = M("#9EA3A8", 0.25f);
            snow = M("#F4F6F8", 0.4f);
            lake = M("#3E8FC9", 0.92f);
            cropRow = M("#D9A92E");
            trunk = M("#8A5A3B");
            pine = M("#3D7A4A");
            houseBase = M("#D8CBB3", 0.25f);
            houseWall = M("#F4F0E6", 0.2f);
            roof = M("#3E6FB0", 0.3f);
            lily = M("#5DAA55", 0.3f);
            meadows = new[] { M("#9CBF8F"), M("#A7C98A"), M("#8DB97A") };
            forests = new[] { M("#6E9E58"), M("#5F8F4E") };
            fields = new[] { M("#EDC14B", 0.2f), M("#E8B93E", 0.2f), M("#F0CD6A", 0.2f) };
            canopies = new[] { M("#4E9A4B", 0.25f), M("#5DAA55", 0.25f), M("#3F8442", 0.25f) };
        }

        static void SetupAtmosphere()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = Palette.Sky;
            RenderSettings.fogStartDistance = 45f;
            RenderSettings.fogEndDistance = 140f;
        }

        void BuildSea()
        {
            var shader = Shader.Find("Akropolis/Sea");
            var mat = shader != null ? new Material(shader) : Mats.Lit(Palette.Hex("#3E8FC9"), 0.8f);
            var go = new GameObject("Sea");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(0f, SeaLevel, 0f);
            go.transform.localScale = new Vector3(400f, 1f, 400f);
            go.AddComponent<MeshFilter>().sharedMesh = HexMesh.Flat;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------- Land ----------------

        void BuildLand()
        {
            float sqrt3 = Mathf.Sqrt(3f);
            int n = Mathf.CeilToInt(OuterRadius / (Tile * 1.5f)) + 1;
            for (int q = -n; q <= n; q++)
                for (int r = -2 * n; r <= 2 * n; r++)
                {
                    var p = new Vector2(Tile * 1.5f * q, -Tile * sqrt3 * (r + q * 0.5f));
                    float d = p.magnitude;
                    if (d < InnerRadius || d > OuterRadius) continue;
                    BuildTile(p, d);
                }
        }

        void BuildTile(Vector2 p, float d)
        {
            float e = d < FlatRadius ? 0f : Elevation(p, d);
            float top = Ground + Mathf.Round(e / Step) * Step;
            if (top < SeaLevel + 0.15f) return;

            var biome = PickBiome(p, d, e, top);
            if (biome == Biome.Lake) top -= 0.25f;

            var pos = new Vector3(p.x, 0f, p.y);
            var side = biome == Biome.Rock || biome == Biome.Snow ? rockSide : biome == Biome.Beach ? sandSide : earth;
            const float cap = 0.14f;
            AddPrism(pos + Vector3.up * Bottom, Tile * 0.97f, top - cap - Bottom, side);
            AddPrism(pos + Vector3.up * (top - cap), Tile * 0.97f, cap, CapMaterial(biome));
            // Keep the ring along the table clear so nothing hides the players' boards.
            if (d > 21f) Decorate(biome, pos + Vector3.up * top, d);
        }

        /// Amphitheatre of terraces rising away from the table, with mountains far out and a bay toward BayAngle.
        static float Elevation(Vector2 p, float d)
        {
            float ang = Mathf.Atan2(p.y, p.x);
            float coast = 52f + (Mathf.PerlinNoise(Mathf.Cos(ang) * 1.7f + 11f, Mathf.Sin(ang) * 1.7f + 7f) - 0.5f) * 16f;
            float bay = Mathf.Max(0f, Mathf.Cos(ang - BayAngle * Mathf.Deg2Rad));
            coast -= bay * bay * bay * 26f;

            float e = Mathf.SmoothStep(0f, 1f, (d - 19f) / 24f) * 3.2f;
            e += (Fbm(p * 0.07f) - 0.5f) * 3.2f * Mathf.SmoothStep(0f, 1f, (d - 19f) / 8f);
            float m = Fbm(p * 0.035f + new Vector2(50f, 20f));
            if (m > 0.55f) e += (m - 0.55f) * 30f * Mathf.SmoothStep(0f, 1f, (d - 28f) / 12f);
            e -= Mathf.Max(0f, d - Mathf.Max(coast - 8f, 22f)) * 0.5f;
            return e;
        }

        static float Fbm(Vector2 p)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            p += new Vector2(1000f, 1000f);
            for (int i = 0; i < 3; i++)
            {
                sum += Mathf.PerlinNoise(p.x, p.y) * amp;
                norm += amp;
                amp *= 0.5f;
                p = p * 2.03f + new Vector2(17.3f, 9.1f);
            }
            return sum / norm;
        }

        static Biome PickBiome(Vector2 p, float d, float e, float top)
        {
            if (top <= SeaLevel + 0.95f) return Biome.Beach;
            if (e > 5f) return Biome.Snow;
            if (e > 3.4f) return Biome.Rock;
            float n = Mathf.PerlinNoise(p.x * 0.09f + 300f, p.y * 0.09f + 300f);
            if (n < 0.3f && d > 22f) return Biome.Lake;
            if (n > 0.68f) return Biome.Field;
            if (n > 0.5f) return Biome.Forest;
            return Biome.Meadow;
        }

        Material CapMaterial(Biome biome)
        {
            switch (biome)
            {
                case Biome.Forest: return Pick(forests);
                case Biome.Field: return Pick(fields);
                case Biome.Lake: return lake;
                case Biome.Beach: return beach;
                case Biome.Rock: return rock;
                case Biome.Snow: return snow;
                default: return Pick(meadows);
            }
        }

        void Decorate(Biome biome, Vector3 top, float d)
        {
            switch (biome)
            {
                case Biome.Forest:
                    int count = 2 + rng.Next(2);
                    for (int i = 0; i < count; i++) Tree(top + Scatter(0.55f));
                    break;
                case Biome.Meadow:
                    double roll = rng.NextDouble();
                    if (roll < 0.15) Tree(top + Scatter(0.4f));
                    else if (roll < 0.2 && d > 21f) Hamlet(top);
                    else if (roll < 0.26) Pebble(top + Scatter(0.5f));
                    break;
                case Biome.Field:
                    Crops(top);
                    if (rng.NextDouble() < 0.07 && d > 21f) Hamlet(top);
                    break;
                case Biome.Rock:
                    if (rng.NextDouble() < 0.35) AddPrism(top, Tile * 0.55f, 0.5f, rock, Rand(0f, 60f));
                    break;
                case Biome.Snow:
                    if (rng.NextDouble() < 0.3) AddPrism(top, Tile * 0.45f, 0.4f, snow, Rand(0f, 60f));
                    break;
                case Biome.Lake:
                    if (rng.NextDouble() < 0.2) AddPrism(top + Scatter(0.5f), 0.25f, 0.03f, lily, Rand(0f, 60f));
                    break;
                case Biome.Beach:
                    if (rng.NextDouble() < 0.06) Pebble(top + Scatter(0.5f));
                    break;
            }
        }

        /// Trees read like the game's tokens: brown trunk discs under a green one, or a stepped pine.
        void Tree(Vector3 at)
        {
            float s = Rand(1.1f, 1.6f);
            float yaw = Rand(0f, 60f);
            if (rng.NextDouble() < 0.35)
            {
                AddPrism(at, 0.18f * s, 0.25f * s, trunk, yaw);
                float y = 0.25f * s;
                float[] radii = { 0.6f, 0.45f, 0.28f };
                foreach (float r in radii)
                {
                    AddPrism(at + Vector3.up * y, r * s, 0.35f * s, pine, yaw);
                    y += 0.3f * s;
                }
                return;
            }
            int trunks = 1 + rng.Next(2);
            for (int i = 0; i < trunks; i++)
                AddPrism(at + Vector3.up * (i * 0.3f * s), 0.22f * s, 0.3f * s, trunk, yaw);
            AddPrism(at + Vector3.up * (trunks * 0.3f * s), 0.55f * s, 0.5f * s, Pick(canopies), yaw);
        }

        /// Grey base, red walls and a pitched roof, like a building stack on the player board.
        void Hamlet(Vector3 top)
        {
            int houses = 1 + rng.Next(2);
            for (int i = 0; i < houses; i++)
            {
                var at = top + (houses > 1 ? Scatter(0.45f) : Vector3.zero);
                float yaw = Rand(0f, 360f);
                var rot = Quaternion.Euler(0f, yaw, 0f);
                AddPrism(at, 0.5f, 0.3f, houseBase, yaw);
                AddPrism(at + Vector3.up * 0.3f, 0.5f, 0.35f, houseWall, yaw);
                AddBox(at + Vector3.up * 0.67f, new Vector3(0.62f, 0.32f, 0.32f) * 1.25f, rot * Quaternion.Euler(45f, 0f, 0f), roof);
                AddBox(at + rot * new Vector3(0.22f, 0.85f, 0.05f), new Vector3(0.1f, 0.3f, 0.1f), rot, roof);
            }
        }

        void Crops(Vector3 top)
        {
            var rot = Quaternion.Euler(0f, rng.Next(3) * 60f, 0f);
            for (int i = -1; i <= 1; i++)
            {
                float len = i == 0 ? 2.1f : 1.5f;
                AddBox(top + rot * new Vector3(0f, 0.04f, i * 0.5f), new Vector3(len, 0.08f, 0.16f), rot, cropRow);
            }
        }

        void Pebble(Vector3 at)
        {
            float s = Rand(0.15f, 0.3f);
            AddBox(at + Vector3.up * s * 0.3f, new Vector3(s * 1.4f, s, s), Quaternion.Euler(Rand(0f, 20f), Rand(0f, 360f), Rand(0f, 20f)), rock);
        }

        // ---------------- Sky ----------------

        void BuildClouds()
        {
            clouds = new GameObject("Clouds").transform;
            clouds.SetParent(transform, false);
            var mat = Mats.Lit(Color.white, 0f);
            for (int i = 0; i < 9; i++)
            {
                float a = Rand(0f, Mathf.PI * 2f), radius = Rand(32f, 60f);
                var center = new Vector3(Mathf.Cos(a) * radius, Rand(15f, 22f), Mathf.Sin(a) * radius);
                int puffs = 3 + rng.Next(3);
                for (int k = 0; k < puffs; k++)
                {
                    var offset = new Vector3(Rand(-2.2f, 2.2f), Rand(0f, 0.6f), Rand(-1.2f, 1.2f));
                    AddPrism(center + offset, Rand(1.2f, 2.2f), Rand(0.8f, 1.4f), mat, Rand(0f, 60f));
                }
            }
            Flush("Clouds", clouds, ShadowCastingMode.Off);
        }

        void BuildBirds()
        {
            var mat = Mats.Lit(Palette.Hex("#3A3A44"), 0.2f);
            for (int i = 0; i < 5; i++)
            {
                var root = new GameObject("Bird").transform;
                root.SetParent(transform, false);
                root.localScale = Vector3.one * 1.6f;
                Part(root, Vector3.zero, new Vector3(0.12f, 0.1f, 0.35f), mat);
                var b = new Bird
                {
                    Root = root,
                    WingL = Wing(root, -1f, mat),
                    WingR = Wing(root, 1f, mat),
                    Radius = Rand(20f, 40f),
                    Height = Rand(9f, 14f),
                    Speed = Rand(0.12f, 0.22f) * (rng.Next(2) == 0 ? -1f : 1f),
                    Angle = Rand(0f, Mathf.PI * 2f),
                    Phase = Rand(0f, 10f),
                };
                birds.Add(b);
            }
        }

        static Transform Wing(Transform body, float side, Material mat)
        {
            var pivot = new GameObject(side < 0f ? "WingL" : "WingR").transform;
            pivot.SetParent(body, false);
            pivot.localPosition = new Vector3(0.05f * side, 0f, 0f);
            Part(pivot, new Vector3(0.25f * side, 0f, 0f), new Vector3(0.5f, 0.03f, 0.2f), mat);
            return pivot;
        }

        static void Part(Transform parent, Vector3 pos, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------------- Batching ----------------

        void AddPrism(Vector3 basePos, float radius, float height, Material mat, float yaw = 0f) =>
            Add(HexMesh.Prism, mat, Matrix4x4.TRS(basePos, Quaternion.Euler(0f, yaw, 0f), new Vector3(radius, height, radius)));

        void AddBox(Vector3 center, Vector3 size, Quaternion rot, Material mat) =>
            Add(cube, mat, Matrix4x4.TRS(center, rot, size));

        void Add(Mesh mesh, Material mat, Matrix4x4 m)
        {
            if (!batches.TryGetValue(mat, out var list)) batches[mat] = list = new List<CombineInstance>();
            list.Add(new CombineInstance { mesh = mesh, transform = m });
        }

        void Flush(string name, Transform parent, ShadowCastingMode shadows)
        {
            foreach (var kv in batches)
            {
                var mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
                mesh.CombineMeshes(kv.Value.ToArray(), true, true);
                var go = new GameObject(name);
                go.transform.SetParent(parent, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = kv.Key;
                r.shadowCastingMode = shadows;
            }
            batches.Clear();
        }

        Vector3 Scatter(float radius)
        {
            float a = Rand(0f, Mathf.PI * 2f), d = Mathf.Sqrt(Rand(0f, 1f)) * radius * Tile;
            return new Vector3(Mathf.Cos(a) * d, 0f, Mathf.Sin(a) * d);
        }

        float Rand(float a, float b) => a + (float)rng.NextDouble() * (b - a);

        Material Pick(Material[] options) => options[rng.Next(options.Length)];
    }
}
