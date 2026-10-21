using System.Collections.Generic;

namespace Akropolis
{
    public struct Cell
    {
        public HexFace Face;
        public int Level;
        public int TileId;

        public Cell(HexFace face, int level, int tileId) { Face = face; Level = level; TileId = tileId; }
    }

    public struct Placement
    {
        public HexCoord Anchor;
        public int Rotation;
        public int Level;
        public bool Valid;
        public string Reason;
        public HexCoord[] Coords;
    }

    public sealed class ScoreBreakdown
    {
        public readonly int[] Stars = new int[6];
        public readonly int[] Heights = new int[6];
        public readonly int[] Points = new int[6];
        public readonly int Stones;
        public int DistrictTotal { get; private set; }
        public int Total => DistrictTotal + Stones;

        public ScoreBreakdown(int stones) { Stones = stones; }

        internal void Finish()
        {
            DistrictTotal = 0;
            for (int i = 1; i < 6; i++)
            {
                Points[i] = Stars[i] * Heights[i];
                DistrictTotal += Points[i];
            }
        }
    }

    /// <summary>La cité d'un joueur : on ne garde que la case visible (la plus haute) de chaque colonne.</summary>
    public sealed class City
    {
        public const int StartingTileId = 0;

        readonly Dictionary<HexCoord, Cell> cells;
        public IReadOnlyDictionary<HexCoord, Cell> Cells => cells;
        public int MaxLevel { get; private set; }

        public City() { cells = new Dictionary<HexCoord, Cell>(); }

        City(City other)
        {
            cells = new Dictionary<HexCoord, Cell>(other.cells);
            MaxLevel = other.MaxLevel;
        }

        public City Clone() => new City(this);

        /// <summary>Remet la cité dans l'état d'une copie (annulation d'une pose).</summary>
        public void RestoreFrom(City other)
        {
            cells.Clear();
            foreach (var kv in other.cells) cells[kv.Key] = kv.Value;
            MaxLevel = other.MaxLevel;
        }

        /// <summary>Tuile de départ : une place Habitations entourée de 3 carrières.</summary>
        public static City CreateWithStartingTile()
        {
            var city = new City();
            var center = new HexCoord(0, 0);
            city.cells[center] = new Cell(new HexFace(District.Houses, true), 1, StartingTileId);
            for (int d = 0; d < 6; d += 2)
                city.cells[center.Neighbor(d)] = new Cell(new HexFace(District.Quarry, false), 1, StartingTileId);
            city.MaxLevel = 1;
            return city;
        }

        public int LevelAt(HexCoord c) => cells.TryGetValue(c, out var cell) ? cell.Level : 0;

        public Placement Evaluate(TileData tile, HexCoord anchor, int rotation)
        {
            var p = new Placement { Anchor = anchor, Rotation = rotation, Coords = new HexCoord[3] };
            int h0 = 0;
            bool flat = true;
            for (int i = 0; i < 3; i++)
            {
                var c = anchor + TileData.Offset(i, rotation);
                p.Coords[i] = c;
                int h = LevelAt(c);
                if (i == 0) h0 = h;
                else if (h != h0) flat = false;
            }

            if (!flat)
            {
                p.Reason = "La tuile doit reposer à plat : aucun vide en dessous.";
                return p;
            }

            if (h0 == 0)
            {
                bool adjacent = false;
                foreach (var c in p.Coords)
                    for (int d = 0; d < 6 && !adjacent; d++)
                        if (cells.ContainsKey(c.Neighbor(d))) adjacent = true;
                if (!adjacent)
                {
                    p.Reason = "La tuile doit toucher votre cité.";
                    return p;
                }
                p.Level = 1;
                p.Valid = true;
                return p;
            }

            int id0 = cells[p.Coords[0]].TileId;
            bool severalTiles = cells[p.Coords[1]].TileId != id0 || cells[p.Coords[2]].TileId != id0;
            if (!severalTiles)
            {
                p.Reason = "À l'étage, la tuile doit chevaucher au moins 2 tuiles.";
                return p;
            }
            p.Level = h0 + 1;
            p.Valid = true;
            return p;
        }

        public int QuarriesUnder(Placement p)
        {
            int n = 0;
            foreach (var c in p.Coords)
                if (cells.TryGetValue(c, out var cell) && cell.Face.IsQuarry) n++;
            return n;
        }

        /// <summary>Pose la tuile et renvoie le nombre de pierres gagnées (carrières recouvertes).</summary>
        public int Apply(Placement p, TileData tile)
        {
            int gained = QuarriesUnder(p);
            for (int i = 0; i < 3; i++)
                cells[p.Coords[i]] = new Cell(tile.Faces[i], p.Level, tile.Id);
            if (p.Level > MaxLevel) MaxLevel = p.Level;
            return gained;
        }

        int CountNeighbors(HexCoord c)
        {
            int n = 0;
            for (int d = 0; d < 6; d++) if (cells.ContainsKey(c.Neighbor(d))) n++;
            return n;
        }

        public bool IsScoringDistrict(HexCoord c)
        {
            if (!cells.TryGetValue(c, out var cell) || !cell.Face.IsDistrict) return false;
            switch (cell.Face.District)
            {
                case District.Markets:
                    for (int d = 0; d < 6; d++)
                        if (cells.TryGetValue(c.Neighbor(d), out var n) && n.Face.IsDistrict && n.Face.District == District.Markets)
                            return false;
                    return true;
                case District.Barracks: return CountNeighbors(c) < 6;
                case District.Temples: return CountNeighbors(c) == 6;
                default: return true;
            }
        }

        /// <summary>Le plus grand groupe d'Habitations adjacentes (à taille égale, le plus haut).</summary>
        public List<HexCoord> LargestHouseGroup()
        {
            var visited = new HashSet<HexCoord>();
            var queue = new Queue<HexCoord>();
            var best = new List<HexCoord>();
            int bestHeight = 0;
            foreach (var kv in cells)
            {
                if (!IsHouse(kv.Value) || visited.Contains(kv.Key)) continue;
                var group = new List<HexCoord>();
                int height = 0;
                visited.Add(kv.Key);
                queue.Enqueue(kv.Key);
                while (queue.Count > 0)
                {
                    var c = queue.Dequeue();
                    group.Add(c);
                    height += cells[c].Level;
                    for (int d = 0; d < 6; d++)
                    {
                        var nb = c.Neighbor(d);
                        if (!visited.Contains(nb) && cells.TryGetValue(nb, out var n) && IsHouse(n))
                        {
                            visited.Add(nb);
                            queue.Enqueue(nb);
                        }
                    }
                }
                if (group.Count > best.Count || (group.Count == best.Count && height > bestHeight))
                {
                    best = group;
                    bestHeight = height;
                }
            }
            return best;
        }

        int LargestHouseGroupHeight()
        {
            int h = 0;
            foreach (var c in LargestHouseGroup()) h += cells[c].Level;
            return h;
        }

        /// <summary>Quartiers de ce type qui rapportent des points (condition de placement respectée).</summary>
        public List<HexCoord> ScoringCoords(District d)
        {
            if (d == District.Houses) return LargestHouseGroup();
            var list = new List<HexCoord>();
            foreach (var kv in cells)
                if (kv.Value.Face.IsDistrict && kv.Value.Face.District == d && IsScoringDistrict(kv.Key)) list.Add(kv.Key);
            return list;
        }

        public List<HexCoord> PlazaCoords(District d)
        {
            var list = new List<HexCoord>();
            foreach (var kv in cells)
                if (kv.Value.Face.IsPlaza && kv.Value.Face.District == d) list.Add(kv.Key);
            return list;
        }

        static bool IsHouse(Cell c) => c.Face.IsDistrict && c.Face.District == District.Houses;

        public ScoreBreakdown ComputeScore(int stones)
        {
            var s = new ScoreBreakdown(stones);
            foreach (var kv in cells)
            {
                var f = kv.Value.Face;
                if (f.IsPlaza)
                {
                    s.Stars[(int)f.District] += f.Stars;
                    continue;
                }
                if (!f.IsDistrict || f.District == District.Houses) continue;
                if (IsScoringDistrict(kv.Key)) s.Heights[(int)f.District] += kv.Value.Level;
            }
            s.Heights[(int)District.Houses] = LargestHouseGroupHeight();
            s.Finish();
            return s;
        }
    }
}
