namespace Akropolis
{
    public enum ActionType : byte { Take, Cancel, Place, Undo, EndTurn }

    /// <summary>
    /// Un coup, assez petit pour passer sur le réseau. Take(A = index au chantier), Place(A = case visée empaquetée,
    /// B = rotation), Cancel / Undo / EndTurn sans paramètre.
    /// </summary>
    public struct GameAction
    {
        public ActionType Type;
        public int Seat;
        public int A;
        public int B;

        public GameAction(ActionType type, int seat, int a = 0, int b = 0)
        {
            Type = type;
            Seat = seat;
            A = a;
            B = b;
        }

        public static int Pack(HexCoord c) => (c.Q + 512) * 1024 + (c.R + 512);
        public static HexCoord Unpack(int v) => new HexCoord(v / 1024 - 512, v % 1024 - 512);

        public override string ToString() => $"{Type}(siège {Seat}, {A}, {B})";
    }
}
