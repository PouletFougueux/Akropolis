using System;
using System.Collections.Generic;
using Akropolis.Audio;
using UnityEngine;

namespace Akropolis.View
{
    /// <summary>
    /// La table, comme dans Harmonies : les architectes assis autour, le Chantier, les piles, la réserve de pierres et
    /// la cloche au centre (qui tourne avec la caméra). Toutes les animations passent par Mover et Fx.
    /// </summary>
    public sealed class TableView : MonoBehaviour
    {
        public const float SeatRadius = 12f;
        const float TableRadius = 21f, FeltRadius = 20f;
        const float SiteScale = 0.66f, SiteSpacing = 1.9f;
        const float PileScale = 0.42f;
        static readonly Vector3 HandLocal = new Vector3(0f, 1.5f, -3.4f);
        static readonly Vector3 ReserveSlot = new Vector3(-5f, 0f, 3f);
        static readonly Vector3 BellSlot = new Vector3(5f, 0f, 3f);

        public sealed class SeatView
        {
            public Transform Root, City, Tray, Plaque;
            public float Yaw;
            public Vector3 MarkerLocal, ChiefLocal;
            public readonly Dictionary<HexCoord, Transform> Tops = new Dictionary<HexCoord, Transform>();
            public readonly List<Transform> Stones = new List<Transform>();
        }

        sealed class SiteEntry
        {
            public Transform Holder, Tile;
            public Transform[] Hexes;
        }

        public readonly List<SeatView> Seats = new List<SeatView>();
        public Transform Center { get; private set; }

        readonly List<SiteEntry> site = new List<SiteEntry>();
        readonly List<GameObject> siteHits = new List<GameObject>();
        readonly List<Transform> piles = new List<Transform>();
        Transform siteRoot, costRoot, reserve, chiefToken, turnMarker, ghost;
        readonly Renderer[] ghostPlates = new Renderer[3];
        Renderer[] markerParts;
        int markerSeat = -1, chiefSeat = -1;
        SiteEntry held, placed;
        Transform[] placedPrev;
        int heldIndex = -1, heldSeat = -1, heldRotation;
        Transform bell, bellSwing;
        float bellAngle, bellVelocity, centerYaw;
        Font font;
        Material ghostValid, ghostInvalid;
        static Mesh pawnMesh, bellMesh;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => pawnMesh = bellMesh = null;

        public static float SeatYaw(int seat, int count)
        {
            switch (count)
            {
                case 2: return seat * 180f;
                case 3: return seat * 120f;
                case 4: return seat * 90f;
                default: return 0f;
            }
        }

        void Awake()
        {
            font = Resources.Load<Font>("UI/Fonts/Fredoka");
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            ghostValid = Mats.UnlitInstance(new Color(0.55f, 1f, 0.55f, 0.55f));
            ghostInvalid = Mats.UnlitInstance(new Color(1f, 0.4f, 0.35f, 0.55f));
        }

        void Update()
        {
            float pulse = 0.4f + 0.25f * Mathf.Sin(Time.time * 5f);
            var c = ghostValid.color; c.a = pulse; ghostValid.color = c;
            c = ghostInvalid.color; c.a = pulse; ghostInvalid.color = c;

            if (Center != null) Center.localRotation = Quaternion.Euler(0f, centerYaw, 0f);

            if (bellSwing != null)
            {
                float dt = Mathf.Min(Time.deltaTime, 0.05f);
                bellVelocity += (-bellAngle * 90f - bellVelocity * 4.5f) * dt;
                bellAngle += bellVelocity * dt;
                bellSwing.localRotation = Quaternion.Euler(bellAngle, 0f, 0f);
            }
        }

        public void SetCenterYaw(float yaw) => centerYaw = yaw;

        // ---------------- Build ----------------

        public void Build(GameState g, bool intro, bool withSeats)
        {
            for (int i = transform.childCount - 1; i >= 0; i--) Destroy(transform.GetChild(i).gameObject);
            Seats.Clear();
            site.Clear();
            siteHits.Clear();
            piles.Clear();
            held = placed = null;
            districtPlates.Clear();
            heldIndex = heldSeat = -1;
            markerSeat = chiefSeat = -1;
            turnMarker = chiefToken = null;
            bellAngle = bellVelocity = 0f;

            BuildTable();
            BuildCenter(g, intro);
            if (withSeats)
                for (int p = 0; p < g.Players.Count; p++) BuildSeat(p, g.Players.Count, g.Players[p], intro);

            ghost = new GameObject("Ghost").transform;
            ghost.SetParent(transform, false);
            for (int i = 0; i < 3; i++)
            {
                var plate = MeshObj("Plate", HexMesh.Flat, ghostValid, ghost, Vector3.zero,
                    new Vector3(TileView.Radius * 1.02f, 1f, TileView.Radius * 1.02f), 30f);
                plate.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                ghostPlates[i] = plate.GetComponent<Renderer>();
            }
            ghost.gameObject.SetActive(false);

            if (withSeats && Seats.Count > 0)
            {
                centerYaw = Seats[g.CurrentIndex].Yaw;
                MoveTurnMarker(g.CurrentIndex, !intro);
                MoveChief(g.ChiefIndex, !intro);
            }
        }

        void BuildTable()
        {
            MeshObj("Table", HexMesh.Prism, Mats.Lit(Palette.Wood, 0.25f), transform, new Vector3(0f, -0.5f, 0f),
                new Vector3(TableRadius, 0.4f, TableRadius), 30f);
            MeshObj("Felt", HexMesh.Prism, Mats.Lit(Palette.Felt, 0.05f), transform, new Vector3(0f, -0.1f, 0f),
                new Vector3(FeltRadius, 0.04f, FeltRadius), 30f);
        }

        void BuildCenter(GameState g, bool intro)
        {
            Center = new GameObject("Center").transform;
            Center.SetParent(transform, false);
            Center.localRotation = Quaternion.Euler(0f, centerYaw, 0f);

            int slots = Mathf.Max(g.Site.Count, 4);
            float halfW = (slots - 1) * SiteSpacing * 0.5f + 1.55f;
            MeshObj("SiteRim", HexMesh.Prism, Mats.Lit(Palette.Teal, 0.2f), Center, new Vector3(0f, -0.08f, 0f), new Vector3(halfW + 0.15f, 0.1f, 1.78f));
            MeshObj("SiteBoard", HexMesh.Prism, Mats.Lit(Palette.Cream, 0.2f), Center, new Vector3(0f, -0.06f, 0f), new Vector3(halfW, 0.1f, 1.62f));
            var title = Text(Center, "Chantier", new Vector3(0f, -0.05f, 1.75f), 0.3f, Palette.Teal);
            title.fontStyle = FontStyle.Bold;

            siteRoot = new GameObject("Site").transform;
            siteRoot.SetParent(Center, false);
            costRoot = new GameObject("Costs").transform;
            costRoot.SetParent(Center, false);
            for (int i = 0; i < g.Site.Count; i++)
            {
                var e = BuildSiteEntry(g.Site[i], SlotPos(i, g.Site.Count));
                site.Add(e);
                if (intro) Mover.Of(e.Holder).Drop(4f, 0.6f + i * 0.08f);
            }
            RebuildHits(g.Site.Count);
            RebuildCosts(g.Site.Count, intro);
            BuildPiles(g, intro);
            BuildReserve(intro);
            BuildBell(intro);
        }

        static Vector3 SlotPos(int i, int count) => new Vector3((i - (count - 1) * 0.5f) * SiteSpacing, 0.04f, 0f);

        SiteEntry BuildSiteEntry(TileData tile, Vector3 slot)
        {
            var e = new SiteEntry();
            e.Holder = new GameObject("Slot").transform;
            e.Holder.SetParent(siteRoot, false);
            e.Holder.localPosition = slot;
            e.Tile = TileView.BuildTile(e.Holder, tile, out e.Hexes);
            e.Tile.localScale = Vector3.one * SiteScale;
            e.Tile.localPosition = -TileView.ShapeCentroid * SiteScale;
            Mover.Of(e.Holder);
            return e;
        }

        void RebuildHits(int count)
        {
            foreach (var h in siteHits) if (h != null) Destroy(h);
            siteHits.Clear();
            for (int i = 0; i < count; i++)
            {
                var hit = new GameObject($"SiteHit {i}");
                hit.transform.SetParent(Center, false);
                hit.transform.localPosition = SlotPos(i, count);
                var col = hit.AddComponent<BoxCollider>();
                col.center = new Vector3(0f, 0.3f, 0f);
                col.size = new Vector3(1.9f, 0.7f, 1.9f);
                Pickable.Add(hit, PickKind.Site, -1, i);
                siteHits.Add(hit);
            }
        }

        /// <summary>Sous chaque emplacement, son prix : une pierre par tuile qui la précède.</summary>
        void RebuildCosts(int count, bool animate)
        {
            for (int i = costRoot.childCount - 1; i >= 0; i--) Destroy(costRoot.GetChild(i).gameObject);
            for (int i = 0; i < count; i++)
            {
                var mark = new GameObject($"Cost {i}").transform;
                mark.SetParent(costRoot, false);
                mark.localPosition = SlotPos(i, count) + new Vector3(0f, 0f, -1.12f);
                if (i == 0)
                {
                    var t = Text(mark, "gratuit", new Vector3(0f, 0.01f, 0f), 0.2f, Palette.Teal);
                    t.fontStyle = FontStyle.Bold;
                }
                else
                {
                    for (int k = 0; k < i; k++)
                    {
                        var cube = TileFx.Box(mark, StoneMat(), new Vector3((k - (i - 1) * 0.5f) * 0.18f, 0.07f, 0f), Vector3.one * 0.13f);
                        cube.localRotation = Quaternion.Euler(0f, 12f * k, 0f);
                    }
                }
                Mover.Of(mark);
                if (animate) Mover.Of(mark).Appear(0.3f + i * 0.05f, 0.35f);
            }
        }

        void BuildPiles(GameState g, bool intro)
        {
            for (int k = 0; k < g.Piles.Count; k++)
            {
                var pile = new GameObject($"Pile {k}").transform;
                pile.SetParent(Center, false);
                pile.localPosition = PileSlot(k);
                pile.localScale = Vector3.one * PileScale;
                pile.localRotation = Quaternion.Euler(0f, (k * 37) % 20 - 10f, 0f);
                for (int j = 0; j < g.Piles[k].Count; j++)
                {
                    var back = TileView.BuildBack(pile);
                    back.localPosition = Vector3.up * (j * TileView.LevelHeight);
                    back.localRotation = Quaternion.Euler(0f, (j * 53) % 9 - 4f, 0f);
                }
                Mover.Of(pile);
                if (intro) Mover.Of(pile).Drop(3f, 0.2f + k * 0.05f);
                piles.Add(pile);
            }
        }

        static Vector3 PileSlot(int k) => k < 6
            ? new Vector3((k - 2.5f) * 1.25f, -0.06f, 2.35f)
            : new Vector3((k - 6 - 2f) * 1.25f, -0.06f, 3.45f);

        static Material StoneMat() => Mats.Lit(Palette.Hex("#B5B9BD"), 0.2f);

        Transform NewStone(Transform parent)
        {
            var s = TileFx.Box(parent, StoneMat(), Vector3.zero, Vector3.one * 0.2f);
            s.name = "Pierre";
            s.localRotation = Quaternion.Euler(0f, UnityEngine.Random.Range(-25f, 25f), 0f);
            return s;
        }

        void BuildReserve(bool intro)
        {
            reserve = new GameObject("Reserve").transform;
            reserve.SetParent(Center, false);
            reserve.localPosition = ReserveSlot;
            TileFx.Prim(reserve, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#8A5A3B"), 0.25f), new Vector3(0f, -0.02f, 0f), new Vector3(1.7f, 0.04f, 1.7f));
            TileFx.Prim(reserve, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#A8754B"), 0.25f), new Vector3(0f, 0f, 0f), new Vector3(1.5f, 0.04f, 1.5f));
            var rng = new System.Random(7);
            for (int k = 0; k < 26; k++)
            {
                int layer = k < 14 ? 0 : k < 22 ? 1 : 2;
                float r = (0.55f - layer * 0.18f) * Mathf.Sqrt((float)rng.NextDouble());
                float a = (float)rng.NextDouble() * Mathf.PI * 2f;
                var s = NewStone(reserve);
                s.localPosition = new Vector3(Mathf.Cos(a) * r, 0.14f + layer * 0.17f, Mathf.Sin(a) * r);
                s.localRotation = Quaternion.Euler((float)rng.NextDouble() * 30f - 15f, (float)rng.NextDouble() * 360f, (float)rng.NextDouble() * 30f - 15f);
            }
            var t = Text(reserve, "Réserve", new Vector3(0f, 0.01f, -1.05f), 0.22f, Palette.Ink);
            t.fontStyle = FontStyle.Bold;
            Mover.Of(reserve);
            if (intro) Mover.Of(reserve).Appear(0.8f, 0.45f);
        }

        void BuildSeat(int p, int count, Player player, bool intro)
        {
            var seat = new SeatView { Yaw = SeatYaw(p, count) };
            seat.Root = new GameObject($"Seat {p}").transform;
            seat.Root.SetParent(transform, false);
            seat.Root.localRotation = Quaternion.Euler(0f, seat.Yaw, 0f);
            seat.Root.localPosition = seat.Root.localRotation * new Vector3(0f, 0f, -SeatRadius);
            float d0 = 0.1f + p * 0.15f;

            var rim = MeshObj("CityRim", HexMesh.Prism, Mats.Lit(Palette.Player(p), 0.25f), seat.Root, new Vector3(0f, -0.1f, 0f),
                new Vector3(4.75f, 0.08f, 4.75f), 30f);
            var board = MeshObj("CityBoard", HexMesh.Prism, Mats.Lit(Palette.Hex("#EFE6D2"), 0.15f), seat.Root, new Vector3(0f, -0.09f, 0f),
                new Vector3(4.5f, 0.09f, 4.5f), 30f);
            if (intro)
            {
                Mover.Of(rim.transform).Appear(d0, 0.5f);
                Mover.Of(board.transform).Appear(d0 + 0.05f, 0.5f);
            }
            var hit = new GameObject("BoardHit");
            hit.transform.SetParent(seat.Root, false);
            var col = hit.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.05f, 0f);
            col.size = new Vector3(8.6f, 0.3f, 8.6f);
            Pickable.Add(hit, PickKind.Board, p, 0);

            seat.City = new GameObject("City").transform;
            seat.City.SetParent(seat.Root, false);
            var start = TileView.BuildStartingTile(seat.City, seat.Tops);
            Mover.Of(start);
            if (intro) Mover.Of(start).Drop(3f, d0 + 0.35f);

            seat.Plaque = new GameObject("Plaque").transform;
            seat.Plaque.SetParent(seat.Root, false);
            seat.Plaque.localPosition = new Vector3(0f, -0.08f, 5.3f);
            MeshObj("PlaqueRim", HexMesh.Prism, Mats.Lit(Palette.Player(p) * 0.8f, 0.2f), seat.Plaque, Vector3.down * 0.02f, new Vector3(1.75f, 0.12f, 0.42f));
            MeshObj("PlaqueBody", HexMesh.Prism, Mats.Lit(Palette.Player(p), 0.3f), seat.Plaque, Vector3.zero, new Vector3(1.62f, 0.14f, 0.36f));
            var name = Text(seat.Plaque, player.Name, new Vector3(0f, 0.15f, 0f), 0.36f, Palette.Cream);
            name.fontStyle = FontStyle.Bold;
            Mover.Of(seat.Plaque);
            if (intro) Mover.Of(seat.Plaque).Appear(0.5f + p * 0.15f, 0.5f);

            seat.Tray = new GameObject("Pierres").transform;
            seat.Tray.SetParent(seat.Root, false);
            seat.Tray.localPosition = new Vector3(4.1f, -0.06f, 4.6f);
            var wood = Mats.Lit(Palette.Hex("#8A5A3B"), 0.25f);
            TileFx.Box(seat.Tray, Mats.Lit(Palette.Hex("#A8754B"), 0.25f), new Vector3(0f, 0.04f, 0f), new Vector3(1.5f, 0.08f, 1.15f));
            TileFx.Box(seat.Tray, wood, new Vector3(0f, 0.1f, 0.6f), new Vector3(1.6f, 0.2f, 0.08f));
            TileFx.Box(seat.Tray, wood, new Vector3(0f, 0.1f, -0.6f), new Vector3(1.6f, 0.2f, 0.08f));
            TileFx.Box(seat.Tray, wood, new Vector3(0.78f, 0.1f, 0f), new Vector3(0.08f, 0.2f, 1.15f));
            TileFx.Box(seat.Tray, wood, new Vector3(-0.78f, 0.1f, 0f), new Vector3(0.08f, 0.2f, 1.15f));
            Mover.Of(seat.Tray);
            if (intro) Mover.Of(seat.Tray).Appear(0.6f + p * 0.15f, 0.4f);
            for (int k = 0; k < player.Stones; k++)
            {
                var s = NewStone(seat.Tray);
                s.localPosition = StoneSlot(k);
                seat.Stones.Add(s);
                var m = Mover.Of(s);
                if (intro) m.Drop(2f, 0.9f + p * 0.15f + k * 0.08f, 0.35f, () => Sound.Play(Sfx.StoneGain, 1.2f, 0.4f));
            }

            seat.MarkerLocal = new Vector3(-2.6f, -0.06f, 5.35f);
            seat.ChiefLocal = new Vector3(2.45f, -0.06f, 5.35f);
            Seats.Add(seat);
        }

        static Vector3 StoneSlot(int k)
        {
            int layer = k / 12, i = k % 12;
            return new Vector3(-0.48f + (i % 4) * 0.32f, 0.18f + layer * 0.2f, -0.3f + (i / 4) * 0.3f);
        }

        // ---------------- Construction site ----------------

        public void SetSiteHover(int index)
        {
            for (int i = 0; i < site.Count; i++)
            {
                if (site[i] == held || site[i] == placed) continue;
                var m = Mover.Of(site[i].Holder);
                m.Lift = i == index ? 0.4f : 0f;
                m.Grow = i == index ? 0.14f : 0f;
            }
        }

        Vector3 HandPose => HandLocal - TileView.ShapeCentroid;

        /// <summary>La tuile quitte le chantier et vient flotter devant la cité du joueur.</summary>
        public void TakeTile(int index, int seat)
        {
            if (index < 0 || index >= site.Count) return;
            ReturnHeld();
            held = site[index];
            heldIndex = index;
            heldSeat = seat;
            heldRotation = 0;
            var hm = Mover.Of(held.Holder);
            hm.Lift = 0f;
            hm.Grow = 0f;
            hm.Squash(0.3f);
            held.Tile.SetParent(Seats[seat].City, true);
            var m = Mover.Of(held.Tile);
            m.Bob = true;
            m.BobAmount = 0.06f;
            m.FlyTo(HandPose, Quaternion.identity, Vector3.one, 0.55f, 1.6f, Ease.InOut);
        }

        public void ReturnHeld()
        {
            HideGhost();
            if (held == null) return;
            var e = held;
            held = null;
            heldIndex = -1;
            e.Tile.SetParent(e.Holder, true);
            var m = Mover.Of(e.Tile);
            m.Bob = false;
            m.FlyTo(-TileView.ShapeCentroid * SiteScale, Quaternion.identity, Vector3.one * SiteScale, 0.45f, 1.4f, Ease.InOut, 0f,
                () => m.Squash(0.25f));
        }

        /// <summary>Plaques vertes / rouges sous l'emplacement visé, la tuile flotte juste au-dessus.</summary>
        public void ShowPlacement(int seat, City city, Placement p, int rotation)
        {
            var sv = Seats[seat];
            int level = p.Level;
            if (!p.Valid)
            {
                level = 0;
                foreach (var c in p.Coords) level = Mathf.Max(level, city.LevelAt(c));
                level += 1;
            }
            float baseY = (level - 1) * TileView.LevelHeight;
            ghost.SetParent(sv.City, false);
            ghost.gameObject.SetActive(true);
            for (int i = 0; i < 3; i++)
            {
                ghostPlates[i].transform.localPosition = TileView.Local(p.Coords[i]) + Vector3.up * (baseY + (level > 1 ? 0.03f : 0.012f));
                ghostPlates[i].sharedMaterial = p.Valid ? ghostValid : ghostInvalid;
            }
            if (held == null) return;
            heldRotation = rotation;
            var target = TileView.Local(p.Anchor) + Vector3.up * (baseY + 0.55f);
            var m = Mover.Of(held.Tile);
            m.FlyTo(target, Quaternion.Euler(0f, -60f * rotation, 0f), Vector3.one, 0.14f, 0f, Ease.Out);
        }

        /// <summary>Plus de case visée : la tuile revient devant le joueur.</summary>
        public void HoldIdle(int rotation)
        {
            HideGhost();
            if (held == null) return;
            var m = Mover.Of(held.Tile);
            var rot = Quaternion.Euler(0f, -60f * rotation, 0f);
            if ((m.Home - HandPose).sqrMagnitude > 0.001f || Quaternion.Angle(m.HomeRot, rot) > 1f)
                m.FlyTo(HandPose, rot, Vector3.one, 0.3f, 0.3f, Ease.InOut);
        }

        public void HideGhost()
        {
            if (ghost != null) ghost.gameObject.SetActive(false);
        }

        /// <summary>La tuile tombe à sa place, écrase le décor recouvert, pop de particules.</summary>
        public void PlaceHeld(TurnInfo info, Action onLand)
        {
            HideGhost();
            var sv = Seats[info.Seat];
            var e = held ?? (info.SiteIndex < site.Count ? site[info.SiteIndex] : null);
            if (e == null)
            {
                onLand?.Invoke();
                return;
            }
            if (e.Tile.parent != sv.City) e.Tile.SetParent(sv.City, true);
            placed = e;
            held = null;
            heldIndex = -1;
            placedPrev = new Transform[3];
            for (int i = 0; i < 3; i++)
                placedPrev[i] = sv.Tops.TryGetValue(info.Placement.Coords[i], out var prev) ? prev : null;

            var p = info.Placement;
            var target = TileView.Local(p.Anchor) + Vector3.up * ((p.Level - 1) * TileView.LevelHeight);
            var m = Mover.Of(e.Tile);
            m.Bob = false;
            m.Lift = 0f;
            var hexes = e.Hexes;
            m.FlyTo(target, Quaternion.Euler(0f, -60f * p.Rotation, 0f), Vector3.one, 0.3f, 0.2f, Ease.In, 0f, () =>
            {
                m.Squash(0.3f);
                for (int i = 0; i < 3; i++)
                {
                    var c = p.Coords[i];
                    if (sv.Tops.TryGetValue(c, out var below)) TileView.StripDecor(below);
                    sv.Tops[c] = hexes[i];
                    var deco = TileView.DecorOf(hexes[i]);
                    if (deco != null) Mover.Of(deco).Squash(0.6f);
                    Fx.Burst(hexes[i].position + Vector3.up * 0.25f, TileView.TopColor(info.Tile.Faces[i]), 7, 2f, 0.08f);
                }
                onLand?.Invoke();
            });
        }

        /// <summary>Après la pose : le chantier se resserre, se réapprovisionne, l'Architecte en chef et le pion avancent.</summary>
        public void AfterTurn(GameState g, TurnInfo info)
        {
            if (placed != null)
            {
                site.Remove(placed);
                Destroy(placed.Holder.gameObject, 0.05f);
                placed = null;
            }
            int count = g.Site.Count;
            for (int i = 0; i < site.Count; i++)
                Mover.Of(site[i].Holder).FlyTo(SlotPos(i, count), Quaternion.identity, Vector3.one, 0.45f, 0.3f, Ease.InOut, i * 0.04f);

            if (info.Refilled)
            {
                var pile = piles.Count > 0 ? piles[0] : null;
                var from = pile != null ? pile.position : Center.position;
                for (int k = 0; k < info.Dealt.Count; k++)
                {
                    int slot = site.Count;
                    var e = BuildSiteEntry(info.Dealt[k], SlotPos(slot, count));
                    site.Add(e);
                    e.Holder.position = from + Vector3.up * (0.4f + k * 0.12f);
                    e.Holder.localScale = Vector3.one * 0.6f;
                    e.Holder.localRotation = Quaternion.Euler(0f, 0f, 180f);
                    var m = Mover.Of(e.Holder);
                    m.FlyTo(SlotPos(slot, count), Quaternion.identity, Vector3.one, 0.6f, 1.6f, Ease.InOut, 0.25f + k * 0.13f, () =>
                    {
                        m.Squash(0.25f);
                        Sound.Play(Sfx.DealCard);
                    });
                }
                if (pile != null)
                {
                    piles.RemoveAt(0);
                    var pm = Mover.Of(pile);
                    pm.FlyTo(pile.localPosition + Vector3.up * 0.6f, pile.localRotation, Vector3.zero, 0.35f, 0f, Ease.In,
                        0.25f + info.Dealt.Count * 0.13f, () => Destroy(pile.gameObject));
                }
                if (info.NewChief >= 0) MoveChief(info.NewChief, false);
            }
            RebuildHits(count);
            RebuildCosts(count, true);
            if (!g.IsOver) MoveTurnMarker(g.CurrentIndex, false);
        }

        /// <summary>Annuler : la tuile repart au chantier, le décor recouvert réapparaît, les pierres font le chemin inverse.</summary>
        public void UndoPlace(TurnInfo info, bool toHand)
        {
            var e = placed;
            if (e == null) return;
            placed = null;
            var sv = Seats[info.Seat];
            for (int i = 0; i < 3; i++)
            {
                var c = info.Placement.Coords[i];
                var prev = placedPrev != null ? placedPrev[i] : null;
                if (prev != null)
                {
                    sv.Tops[c] = prev;
                    TileView.ShowDecor(prev);
                }
                else sv.Tops.Remove(c);
            }
            var m = Mover.Of(e.Tile);
            m.Lift = 0f;
            if (toHand)
            {
                // La tuile se relève et reste en main, prête à être reposée ailleurs.
                held = e;
                heldIndex = site.IndexOf(e);
                heldSeat = info.Seat;
                m.Bob = true;
                m.BobAmount = 0.06f;
                m.FlyTo(HandPose, Quaternion.Euler(0f, -60f * info.Placement.Rotation, 0f), Vector3.one, 0.45f, 1.2f, Ease.InOut);
            }
            else
            {
                e.Tile.SetParent(e.Holder, true);
                m.Bob = false;
                m.FlyTo(-TileView.ShapeCentroid * SiteScale, Quaternion.identity, Vector3.one * SiteScale, 0.55f, 1.8f, Ease.InOut, 0f,
                    () => m.Squash(0.25f));
            }
            if (info.Gained > 0) PayStones(info.Seat, info.Gained);
            if (info.Paid > 0) RefundStones(info.Seat, info.Paid);
        }

        /// <summary>Des pierres sortent de la réserve et reviennent dans le bac du joueur.</summary>
        public void RefundStones(int seat, int count)
        {
            var sv = Seats[seat];
            for (int k = 0; k < count; k++)
            {
                var s = NewStone(sv.Tray);
                s.position = reserve.position + Vector3.up * 0.5f;
                s.localScale = Vector3.zero;
                var m = Mover.Of(s);
                int slot = sv.Stones.Count;
                sv.Stones.Add(s);
                m.FlyTo(StoneSlot(slot), Quaternion.Euler(0f, UnityEngine.Random.Range(-25f, 25f), 0f), Vector3.one * 0.2f, 0.6f, 2f,
                    Ease.InOut, k * 0.1f, () => m.Squash(0.4f));
            }
            Mover.Of(reserve).Squash(0.2f);
        }

        // ---------------- Stones ----------------

        /// <summary>Les pierres dépensées retournent dans la réserve.</summary>
        public void PayStones(int seat, int count)
        {
            var sv = Seats[seat];
            for (int k = 0; k < count && sv.Stones.Count > 0; k++)
            {
                var s = sv.Stones[sv.Stones.Count - 1];
                sv.Stones.RemoveAt(sv.Stones.Count - 1);
                s.SetParent(reserve, true);
                var m = Mover.Of(s);
                var spot = new Vector3(UnityEngine.Random.Range(-0.35f, 0.35f), 0.5f, UnityEngine.Random.Range(-0.35f, 0.35f));
                m.FlyTo(spot, UnityEngine.Random.rotation, Vector3.one * 0.12f, 0.55f, 2f, Ease.InOut, k * 0.09f, () =>
                {
                    Fx.Burst(s.position, Palette.Stone, 4, 1.2f, 0.06f);
                    Destroy(s.gameObject);
                });
            }
            Mover.Of(reserve).Squash(0.2f);
        }

        /// <summary>Une pierre jaillit de chaque carrière recouverte et file vers le plateau du joueur.</summary>
        public void GainStones(int seat, IList<HexCoord> quarries)
        {
            var sv = Seats[seat];
            for (int k = 0; k < quarries.Count; k++)
            {
                var s = NewStone(sv.Tray);
                var top = sv.Tops.TryGetValue(quarries[k], out var hex) ? hex.position : sv.City.position;
                s.position = top + Vector3.up * 0.4f;
                s.localScale = Vector3.zero;
                var m = Mover.Of(s);
                int slot = sv.Stones.Count, order = k;
                sv.Stones.Add(s);
                m.FlyTo(StoneSlot(slot), Quaternion.Euler(0f, UnityEngine.Random.Range(-25f, 25f), 0f), Vector3.one * 0.2f, 0.7f, 2.2f,
                    Ease.InOut, 0.1f + k * 0.15f, () =>
                    {
                        m.Squash(0.4f);
                        Sound.Play(Sfx.StoneGain, 1f + order * 0.08f);
                    });
            }
        }

        public void BounceStones(int seat)
        {
            var sv = Seats[seat];
            foreach (var s in sv.Stones) Mover.Of(s).Squash(0.5f);
            Fx.Burst(TrayTop(seat), Palette.Stone, 8, 1.8f, 0.08f);
        }

        public Vector3 PlaquePosition(int seat) => Seats[seat].Plaque.position + Vector3.up * 0.9f;

        public Vector3 TrayTop(int seat) => Seats[seat].Tray.position + Vector3.up * 0.6f;

        // ---------------- Score helpers ----------------

        public Vector3 HexTop(int seat, HexCoord c)
        {
            var sv = Seats[seat];
            return sv.Tops.TryGetValue(c, out var t) ? t.position + Vector3.up * TileView.Top : sv.City.position;
        }

        public Vector3 CellsCenter(int seat, IList<HexCoord> cells)
        {
            if (cells == null || cells.Count == 0) return Seats[seat].City.position;
            var p = Vector3.zero;
            foreach (var c in cells) p += HexTop(seat, c);
            return p / cells.Count;
        }

        public void Bounce(int seat, IEnumerable<HexCoord> cells, Color color)
        {
            var sv = Seats[seat];
            foreach (var c in cells)
            {
                if (!sv.Tops.TryGetValue(c, out var t)) continue;
                Mover.Of(t).Squash(0.35f);
                Fx.Burst(t.position + Vector3.up * 0.35f, color, 5, 1.6f, 0.08f);
            }
        }

        /// <summary>Case de la cité sous le curseur : on teste le dessus de chaque niveau, du plus haut au sol.</summary>
        public bool TryCityCoord(int seat, Ray ray, City city, out HexCoord coord)
        {
            coord = default;
            var c = Seats[seat].City;
            var o = c.InverseTransformPoint(ray.origin);
            var d = c.InverseTransformDirection(ray.direction);
            if (Mathf.Abs(d.y) < 1e-4f) return false;
            for (int level = city.MaxLevel; level >= 0; level--)
            {
                float y = level * TileView.LevelHeight + (level > 0 ? 0.02f : 0f);
                float t = (y - o.y) / d.y;
                if (t <= 0f) continue;
                var p = o + d * t;
                var h = HexCoord.FromLocal(p.x / TileView.HexScale, p.z / TileView.HexScale);
                if (level == 0)
                {
                    if (new Vector2(p.x, p.z).magnitude > 6.5f) return false;
                    coord = h;
                    return true;
                }
                if (city.LevelAt(h) == level)
                {
                    coord = h;
                    return true;
                }
            }
            return false;
        }

        readonly List<Renderer> districtPlates = new List<Renderer>();

        /// <summary>Plaques vertes sur les quartiers qui comptent, rouges sur ceux qui ne comptent pas.</summary>
        public void ShowDistrictHighlight(int seat, City city, IList<HexCoord> scoring, IList<HexCoord> ignored)
        {
            HideDistrictHighlight();
            if (seat < 0 || seat >= Seats.Count) return;
            var parent = Seats[seat].City;
            int n = 0;
            void Plate(HexCoord c, Material mat)
            {
                Renderer r;
                if (n < districtPlates.Count && districtPlates[n] != null) r = districtPlates[n];
                else
                {
                    var go = MeshObj("DistrictPlate", HexMesh.Flat, mat, parent, Vector3.zero,
                        new Vector3(TileView.Radius * 0.9f, 1f, TileView.Radius * 0.9f), 30f);
                    go.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    r = go.GetComponent<Renderer>();
                    if (n < districtPlates.Count) districtPlates[n] = r; else districtPlates.Add(r);
                }
                r.transform.SetParent(parent, false);
                r.transform.localPosition = TileView.Local(c) + Vector3.up * ((city.LevelAt(c) - 1) * TileView.LevelHeight + TileView.Top + 0.01f);
                r.sharedMaterial = mat;
                r.gameObject.SetActive(true);
                n++;
            }
            foreach (var c in scoring) Plate(c, ghostValid);
            foreach (var c in ignored) Plate(c, ghostInvalid);
        }

        public void HideDistrictHighlight()
        {
            foreach (var r in districtPlates) if (r != null) r.gameObject.SetActive(false);
        }

        // ---------------- Turn marker & chief architect ----------------

        /// <summary>Un grand pion saute de cité en cité pour montrer qui joue.</summary>
        public void MoveTurnMarker(int seat, bool instant)
        {
            if (seat < 0 || seat >= Seats.Count || seat == markerSeat) return;
            bool first = turnMarker == null;
            if (first) BuildTurnMarker();
            var target = transform.InverseTransformPoint(Seats[seat].Root.TransformPoint(Seats[seat].MarkerLocal));
            var rot = Quaternion.Euler(0f, Seats[seat].Yaw, 0f);
            markerSeat = seat;
            var m = Mover.Of(turnMarker);
            if (instant || first)
            {
                m.Home = target;
                m.HomeRot = rot;
                turnMarker.localPosition = target;
                turnMarker.localRotation = rot;
                Paint(seat);
                if (!instant) m.Appear(1.1f, 0.5f);
                return;
            }
            m.FlyTo(target, rot, m.HomeScale, 0.85f, 3f, Ease.InOut, 0.15f, () =>
            {
                Paint(seat);
                m.Squash(0.35f);
            });
        }

        void Paint(int seat)
        {
            var mat = Mats.Lit(Palette.Player(seat), 0.55f);
            foreach (var r in markerParts) r.sharedMaterial = mat;
        }

        void BuildTurnMarker()
        {
            turnMarker = new GameObject("TurnMarker").transform;
            turnMarker.SetParent(transform, false);
            if (pawnMesh == null)
                pawnMesh = Lathe(new[]
                {
                    new Vector2(0f, 0.66f), new Vector2(0.13f, 0.66f), new Vector2(0.27f, 0.62f), new Vector2(0.27f, 0.57f),
                    new Vector2(0.15f, 0.54f), new Vector2(0.13f, 0.4f), new Vector2(0.2f, 0.18f), new Vector2(0.36f, 0.1f),
                    new Vector2(0.4f, 0.06f), new Vector2(0.4f, 0f), new Vector2(0f, 0f),
                }, 24, "Pawn");
            var body = new GameObject("PawnBody");
            body.transform.SetParent(turnMarker, false);
            body.AddComponent<MeshFilter>().sharedMesh = pawnMesh;
            var bodyRenderer = body.AddComponent<MeshRenderer>();
            var head = TileFx.Prim(turnMarker, PrimitiveType.Sphere, Mats.Lit(Color.white, 0.5f), new Vector3(0f, 0.84f, 0f), Vector3.one * 0.38f);
            markerParts = new[] { (Renderer)bodyRenderer, head.GetComponent<Renderer>() };
            turnMarker.localScale = Vector3.one * 1.15f;
        }

        /// <summary>Le pion Architecte en Chef passe au joueur de gauche à chaque nouvelle manche.</summary>
        public void MoveChief(int seat, bool instant)
        {
            if (seat < 0 || seat >= Seats.Count || seat == chiefSeat) return;
            bool first = chiefToken == null;
            if (first) BuildChiefToken();
            var target = transform.InverseTransformPoint(Seats[seat].Root.TransformPoint(Seats[seat].ChiefLocal));
            var rot = Quaternion.Euler(0f, Seats[seat].Yaw, 0f);
            chiefSeat = seat;
            var m = Mover.Of(chiefToken);
            if (instant || first)
            {
                m.Home = target;
                m.HomeRot = rot;
                chiefToken.localPosition = target;
                chiefToken.localRotation = rot;
                if (!instant) m.Appear(1.3f, 0.5f);
                return;
            }
            m.FlyTo(target, rot, m.HomeScale, 1f, 3.5f, Ease.InOut, 0.4f, () =>
            {
                m.Squash(0.4f);
                Fx.Burst(chiefToken.position + Vector3.up * 0.3f, Palette.Gold, 10, 2.2f, 0.09f);
            });
        }

        void BuildChiefToken()
        {
            chiefToken = new GameObject("ArchitecteEnChef").transform;
            chiefToken.SetParent(transform, false);
            var gold = Mats.Lit(Palette.Gold, 0.75f);
            var marble = Mats.Lit(Palette.Hex("#F7F4EE"), 0.35f);
            TileFx.Prim(chiefToken, PrimitiveType.Cylinder, Mats.Lit(Palette.Hex("#B88A2E"), 0.5f), new Vector3(0f, 0.03f, 0f), new Vector3(0.62f, 0.03f, 0.62f));
            TileFx.Prim(chiefToken, PrimitiveType.Cylinder, gold, new Vector3(0f, 0.07f, 0f), new Vector3(0.56f, 0.03f, 0.56f));
            TileFx.Box(chiefToken, marble, new Vector3(0f, 0.12f, 0f), new Vector3(0.3f, 0.04f, 0.18f));
            for (int x = -1; x <= 1; x++)
                TileFx.Prim(chiefToken, PrimitiveType.Cylinder, marble, new Vector3(0.1f * x, 0.2f, 0f), new Vector3(0.035f, 0.06f, 0.035f));
            TileFx.Box(chiefToken, gold, new Vector3(0f, 0.28f, 0f), new Vector3(0.34f, 0.04f, 0.2f));
            chiefToken.localScale = Vector3.one * 1.3f;
        }

        static Mesh Lathe(Vector2[] profile, int seg, string name)
        {
            var verts = new List<Vector3>();
            var tris = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                float a = s * Mathf.PI * 2f / seg;
                foreach (var p in profile) verts.Add(new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x));
            }
            int n = profile.Length;
            for (int s = 0; s < seg; s++)
                for (int i = 0; i < n - 1; i++)
                {
                    int a = s * n + i, b = (s + 1) * n + i;
                    tris.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 });
                }
            var mesh = new Mesh { name = name };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            return mesh;
        }

        // ---------------- Bell ----------------

        void BuildBell(bool intro)
        {
            bell = new GameObject("Bell").transform;
            bell.SetParent(Center, false);
            bell.localPosition = BellSlot;
            var wood = Mats.Lit(Palette.Hex("#7A4E2D"), 0.25f);
            var darkWood = Mats.Lit(Palette.Hex("#5C3A21"), 0.2f);
            TileFx.Box(bell, darkWood, new Vector3(0f, 0.04f, 0f), new Vector3(1.15f, 0.08f, 0.5f));
            TileFx.Prim(bell, PrimitiveType.Cylinder, wood, new Vector3(-0.45f, 0.62f, 0f), new Vector3(0.09f, 0.56f, 0.09f));
            TileFx.Prim(bell, PrimitiveType.Cylinder, wood, new Vector3(0.45f, 0.62f, 0f), new Vector3(0.09f, 0.56f, 0.09f));
            TileFx.Prim(bell, PrimitiveType.Cylinder, wood, new Vector3(0f, 1.2f, 0f), new Vector3(0.08f, 0.5f, 0.08f)).localRotation = Quaternion.Euler(0f, 0f, 90f);
            TileFx.Prim(bell, PrimitiveType.Sphere, darkWood, new Vector3(-0.45f, 1.2f, 0f), Vector3.one * 0.13f);
            TileFx.Prim(bell, PrimitiveType.Sphere, darkWood, new Vector3(0.45f, 1.2f, 0f), Vector3.one * 0.13f);

            bellSwing = new GameObject("Swing").transform;
            bellSwing.SetParent(bell, false);
            bellSwing.localPosition = new Vector3(0f, 1.17f, 0f);
            TileFx.Prim(bellSwing, PrimitiveType.Cylinder, darkWood, new Vector3(0f, -0.06f, 0f), new Vector3(0.05f, 0.06f, 0.05f));
            var body = new GameObject("BellBody");
            body.transform.SetParent(bellSwing, false);
            body.transform.localPosition = new Vector3(0f, -0.1f, 0f);
            body.AddComponent<MeshFilter>().sharedMesh = BellMesh();
            body.AddComponent<MeshRenderer>().sharedMaterial = Mats.Lit(Palette.Hex("#E9B949"), 0.8f);
            TileFx.Prim(bellSwing, PrimitiveType.Sphere, Mats.Lit(Palette.Hex("#9C7A2E"), 0.5f), new Vector3(0f, -0.62f, 0f), Vector3.one * 0.1f);

            var col = bell.gameObject.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, 0.65f, 0f);
            col.size = new Vector3(1.2f, 1.35f, 0.6f);
            Pickable.Add(bell.gameObject, PickKind.Bell, -1, 0);
            Mover.Of(bell);
            if (intro) Mover.Of(bell).Appear(0.9f, 0.5f);
        }

        public Vector3 BellTop => bell != null ? bell.position + Vector3.up * 1.7f : Vector3.zero;

        public void RingBell()
        {
            if (bellSwing == null) return;
            bellVelocity = Mathf.Clamp(bellVelocity + (bellAngle >= 0f ? 320f : -320f) * UnityEngine.Random.Range(0.8f, 1.2f), -700f, 700f);
            Mover.Of(bell).Squash(0.12f);
        }

        static Mesh BellMesh()
        {
            if (bellMesh != null) return bellMesh;
            bellMesh = Lathe(new[]
            {
                new Vector2(0f, 0.02f), new Vector2(0.1f, 0.01f), new Vector2(0.17f, -0.05f), new Vector2(0.2f, -0.15f),
                new Vector2(0.21f, -0.28f), new Vector2(0.25f, -0.4f), new Vector2(0.33f, -0.5f), new Vector2(0.34f, -0.53f),
                new Vector2(0.28f, -0.52f), new Vector2(0f, -0.48f),
            }, 20, "Bell");
            return bellMesh;
        }

        // ---------------- Helpers ----------------

        static GameObject MeshObj(string name, Mesh mesh, Material mat, Transform parent, Vector3 pos, Vector3 scale, float yaw = 0f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        TextMesh Text(Transform parent, string text, Vector3 pos, float height, Color color)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.font = font;
            tm.fontSize = 128;
            tm.characterSize = height * 10f / 128f;
            tm.anchor = TextAnchor.MiddleCenter;
            tm.alignment = TextAlignment.Center;
            tm.color = color;
            tm.text = text;
            go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            return tm;
        }
    }
}
