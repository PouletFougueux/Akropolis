using System.Collections.Generic;

namespace Akropolis
{
    /// <summary>IA gloutonne : teste toutes les poses possibles et garde celle qui rapporte le plus tout de suite.</summary>
    public static class AIPlanner
    {
        public struct Move
        {
            public int SiteIndex;
            public Placement Placement;
            public float Value;
        }

        public static Move FindBest(City city, int stones, IReadOnlyList<TileData> site, System.Random rng)
        {
            var anchors = new HashSet<HexCoord>();
            foreach (var c in city.Cells.Keys)
            {
                anchors.Add(c);
                for (int d = 0; d < 6; d++) anchors.Add(c.Neighbor(d));
            }

            var best = new Move { SiteIndex = -1, Value = float.MinValue };
            for (int i = 0; i < site.Count && i <= stones; i++)
            {
                var tile = site[i];
                foreach (var a in anchors)
                {
                    for (int rot = 0; rot < 6; rot++)
                    {
                        var p = city.Evaluate(tile, a, rot);
                        if (!p.Valid) continue;
                        var clone = city.Clone();
                        int gained = clone.Apply(p, tile);
                        float value = clone.ComputeScore(0).DistrictTotal
                                      + (stones - i + gained)
                                      + p.Level * 0.15f
                                      + (float)rng.NextDouble() * 0.3f;
                        if (value > best.Value)
                            best = new Move { SiteIndex = i, Placement = p, Value = value };
                    }
                }
            }
            return best;
        }
    }
}
