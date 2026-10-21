using System.Collections.Generic;
using UnityEngine;

namespace Akropolis
{
    public enum District { Quarry = 0, Houses = 1, Markets = 2, Barracks = 3, Temples = 4, Gardens = 5 }

    /// <summary>Une case hexagonale : un quartier, une place (avec étoiles) ou une carrière.</summary>
    public readonly struct HexFace
    {
        public readonly District District;
        public readonly bool IsPlaza;

        public HexFace(District district, bool isPlaza)
        {
            District = district;
            IsPlaza = isPlaza && district != Akropolis.District.Quarry;
        }

        public int Stars => IsPlaza ? Rules.PlazaStars(District) : 0;
        public bool IsQuarry => District == Akropolis.District.Quarry;
        public bool IsDistrict => !IsPlaza && District != Akropolis.District.Quarry;
    }

    public static class Rules
    {
        public static readonly District[] ScoringDistricts =
            { District.Houses, District.Markets, District.Barracks, District.Temples, District.Gardens };

        public static int PlazaStars(District d)
        {
            switch (d)
            {
                case District.Houses: return 1;
                case District.Gardens: return 3;
                case District.Quarry: return 0;
                default: return 2;
            }
        }

        public static string Name(District d)
        {
            switch (d)
            {
                case District.Houses: return "Habitations";
                case District.Markets: return "Marchés";
                case District.Barracks: return "Casernes";
                case District.Temples: return "Temples";
                case District.Gardens: return "Jardins";
                default: return "Carrière";
            }
        }

        public static string Condition(District d)
        {
            switch (d)
            {
                case District.Houses: return "seul le plus grand groupe compte";
                case District.Markets: return "ne doit toucher aucun autre marché";
                case District.Barracks: return "doit être en bordure de cité";
                case District.Temples: return "doit être entièrement entouré";
                case District.Gardens: return "compte toujours";
                default: return "";
            }
        }

        public static Color DistrictColor(District d)
        {
            switch (d)
            {
                case District.Houses: return new Color(0.27f, 0.48f, 0.85f);
                case District.Markets: return new Color(0.96f, 0.78f, 0.22f);
                case District.Barracks: return new Color(0.86f, 0.28f, 0.22f);
                case District.Temples: return new Color(0.58f, 0.38f, 0.80f);
                case District.Gardens: return new Color(0.36f, 0.72f, 0.30f);
                default: return new Color(0.56f, 0.56f, 0.56f);
            }
        }

        /// <summary>Nombre de tuiles utilisées (approximation de la boîte : 37 / 49 / 61).</summary>
        public static int DeckSize(int players) => players <= 2 ? 37 : players == 3 ? 49 : 61;

        /// <summary>Nombre de tuiles sur le chantier : joueurs + 2.</summary>
        public static int SiteSize(int players) => players + 2;
    }

    /// <summary>Tuile de 3 hexagones disposés en triangle.</summary>
    public sealed class TileData
    {
        public static readonly HexCoord[] Shape = { new HexCoord(0, 0), new HexCoord(1, 0), new HexCoord(0, 1) };

        public readonly int Id;
        public readonly HexFace[] Faces;

        public TileData(int id, HexFace[] faces) { Id = id; Faces = faces; }

        public static HexCoord Offset(int index, int rotation) => Shape[index].Rotate60(rotation);
    }

    /// <summary>
    /// Génère une pioche aléatoire. La répartition réelle des tuiles de la boîte n'est pas reproduite :
    /// les poids ci-dessous sont à ajuster.
    /// </summary>
    public static class TileFactory
    {
        static readonly District[] Kinds =
            { District.Quarry, District.Houses, District.Markets, District.Barracks, District.Temples, District.Gardens };
        static readonly int[] FaceWeights = { 24, 27, 13, 13, 12, 11 };
        static readonly int[] PlazaWeights = { 0, 30, 18, 18, 18, 16 };
        const double PlazaChance = 0.3;

        public static List<TileData> CreateDeck(int count, System.Random rng)
        {
            var deck = new List<TileData>(count);
            for (int i = 0; i < count; i++)
            {
                var faces = new HexFace[3];
                int start = 0;
                if (rng.NextDouble() < PlazaChance)
                {
                    faces[0] = new HexFace(Pick(PlazaWeights, rng), true);
                    start = 1;
                }
                int quarries;
                do
                {
                    quarries = 0;
                    for (int f = start; f < 3; f++)
                    {
                        var d = Pick(FaceWeights, rng);
                        faces[f] = new HexFace(d, false);
                        if (d == District.Quarry) quarries++;
                    }
                } while (quarries >= 2);

                for (int k = faces.Length - 1; k > 0; k--)
                {
                    int j = rng.Next(k + 1);
                    var tmp = faces[k]; faces[k] = faces[j]; faces[j] = tmp;
                }
                deck.Add(new TileData(i + 1, faces));
            }
            return deck;
        }

        static District Pick(int[] weights, System.Random rng)
        {
            int total = 0;
            foreach (var w in weights) total += w;
            int r = rng.Next(total);
            for (int i = 0; i < weights.Length; i++)
            {
                if (r < weights[i]) return Kinds[i];
                r -= weights[i];
            }
            return District.Houses;
        }
    }
}
