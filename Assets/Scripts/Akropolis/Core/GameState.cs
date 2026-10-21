using System.Collections.Generic;

namespace Akropolis
{
    /// <summary>Ce qui s'est passé pendant un tour, pour que la vue puisse l'animer.</summary>
    public sealed class TurnInfo
    {
        public int Seat, SiteIndex, Paid, Gained, Level;
        public TileData Tile;
        public Placement Placement;
        public readonly List<HexCoord> CoveredQuarries = new List<HexCoord>();
        public bool Refilled;
        public readonly List<TileData> Dealt = new List<TileData>();
        public int NewChief = -1;
        public bool GameOver;
    }

    /// <summary>
    /// État d'une partie (règles officielles) : 11 piles de (joueurs + 1) tuiles, un chantier de (joueurs + 2) tuiles,
    /// la dernière tuile du chantier n'est jamais jouée, l'Architecte en chef passe à gauche à chaque réapprovisionnement.
    /// </summary>
    public sealed class GameState
    {
        public const int PileCount = 11;

        public readonly List<Player> Players = new List<Player>();
        public readonly List<List<TileData>> Piles = new List<List<TileData>>();
        public readonly List<TileData> Site = new List<TileData>();

        public int CurrentIndex { get; private set; }
        public int ChiefIndex { get; private set; }
        public int Round { get; private set; } = 1;
        public bool IsOver { get; private set; }

        public Player Current => Players[CurrentIndex];
        public bool LastRound => Piles.Count == 0;

        public int TilesInPiles
        {
            get
            {
                int n = 0;
                foreach (var p in Piles) n += p.Count;
                return n;
            }
        }

        /// <param name="tilesFor">Nombre de joueurs utilisé pour la pioche (aperçu du menu sans joueurs).</param>
        public GameState(IList<string> names, IList<bool> ai, int seed, int tilesFor = 0)
        {
            var rng = new System.Random(seed);
            for (int i = 0; i < names.Count; i++) Players.Add(new Player(i, names[i], ai[i], i + 1));
            int n = tilesFor > 0 ? tilesFor : names.Count;
            int perPile = n + 1;
            var deck = TileFactory.CreateDeck(PileCount * perPile + n + 2, rng);
            int k = 0;
            for (int i = 0; i < n + 2; i++) Site.Add(deck[k++]);
            for (int p = 0; p < PileCount; p++)
            {
                var pile = new List<TileData>();
                for (int j = 0; j < perPile; j++) pile.Add(deck[k++]);
                Piles.Add(pile);
            }
        }

        public bool CanAfford(int siteIndex) => siteIndex >= 0 && siteIndex < Site.Count && siteIndex <= Current.Stones;

        public ScoreBreakdown Score(int player) => Players[player].City.ComputeScore(Players[player].Stones);

        /// <summary>Tuile posée ce tour-ci, pas encore validée par « Fin du tour ».</summary>
        public TurnInfo Pending { get; private set; }
        public bool HasPending => Pending != null;
        City backupCity;
        int backupStones;

        /// <summary>Prend la tuile n° index du chantier (1 pierre par tuile sautée) et la pose. null + error si refusé.</summary>
        public TurnInfo Play(int index, HexCoord anchor, int rotation, out string error)
        {
            error = null;
            if (IsOver)
            {
                error = "La partie est terminée.";
                return null;
            }
            if (Pending != null)
            {
                error = "Vous avez déjà posé une tuile : annulez-la ou terminez votre tour.";
                return null;
            }
            if (index < 0 || index >= Site.Count)
            {
                error = "Cette tuile n'est plus au chantier.";
                return null;
            }
            if (index > Current.Stones)
            {
                error = $"Il vous faut {index} pierre(s) pour prendre cette tuile.";
                return null;
            }
            var tile = Site[index];
            var city = Current.City;
            var p = city.Evaluate(tile, anchor, rotation);
            if (!p.Valid)
            {
                error = p.Reason;
                return null;
            }

            var info = new TurnInfo { Seat = CurrentIndex, SiteIndex = index, Tile = tile, Placement = p, Level = p.Level, Paid = index };
            foreach (var c in p.Coords)
                if (city.Cells.TryGetValue(c, out var cell) && cell.Face.IsQuarry) info.CoveredQuarries.Add(c);

            backupCity = city.Clone();
            backupStones = Current.Stones;
            Current.Stones -= index;
            Site.RemoveAt(index);
            info.Gained = city.Apply(p, tile);
            Current.Stones += info.Gained;
            Pending = info;
            return info;
        }

        /// <summary>Retire la tuile posée ce tour-ci : elle retourne au chantier, les pierres sont rendues.</summary>
        public TurnInfo Undo()
        {
            var info = Pending;
            if (info == null) return null;
            Current.City.RestoreFrom(backupCity);
            Current.Stones = backupStones;
            Site.Insert(info.SiteIndex, info.Tile);
            Pending = null;
            return info;
        }

        /// <summary>Valide la pose : réapprovisionne le chantier si besoin et passe au joueur suivant.</summary>
        public TurnInfo EndTurn()
        {
            var info = Pending;
            if (info == null) return null;
            Pending = null;
            if (Site.Count <= 1)
            {
                if (Piles.Count == 0)
                {
                    IsOver = true;
                    info.GameOver = true;
                    return info;
                }
                info.Refilled = true;
                info.Dealt.AddRange(Piles[0]);
                Site.AddRange(Piles[0]);
                Piles.RemoveAt(0);
                ChiefIndex = (ChiefIndex + 1) % Players.Count;
                info.NewChief = ChiefIndex;
                CurrentIndex = ChiefIndex;
                Round++;
                return info;
            }
            CurrentIndex = (CurrentIndex + 1) % Players.Count;
            return info;
        }
    }
}
