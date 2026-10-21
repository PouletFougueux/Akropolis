using System;
using UnityEngine;

namespace Akropolis
{
    /// <summary>Coordonnées axiales (q, r) d'une grille hexagonale « pointe en haut ».</summary>
    public readonly struct HexCoord : IEquatable<HexCoord>
    {
        public const float Size = 1f;
        static readonly float Sqrt3 = Mathf.Sqrt(3f);

        /// <summary>Les 6 voisins, dans le sens anti-horaire en partant de l'est.</summary>
        public static readonly HexCoord[] Directions =
        {
            new HexCoord(1, 0), new HexCoord(1, -1), new HexCoord(0, -1),
            new HexCoord(-1, 0), new HexCoord(-1, 1), new HexCoord(0, 1)
        };

        public readonly int Q;
        public readonly int R;

        public HexCoord(int q, int r) { Q = q; R = r; }

        public HexCoord Neighbor(int dir) => new HexCoord(Q + Directions[dir].Q, R + Directions[dir].R);

        /// <summary>Rotation de 60° × times autour de l'origine.</summary>
        public HexCoord Rotate60(int times)
        {
            int q = Q, r = R;
            times = ((times % 6) + 6) % 6;
            for (int i = 0; i < times; i++)
            {
                int nq = -r;
                int nr = q + r;
                q = nq;
                r = nr;
            }
            return new HexCoord(q, r);
        }

        public Vector3 ToLocal(float y = 0f) => new Vector3(Size * Sqrt3 * (Q + R * 0.5f), y, Size * 1.5f * R);

        public static HexCoord FromLocal(float x, float z)
        {
            float fq = (Sqrt3 / 3f * x - 1f / 3f * z) / Size;
            float fr = (2f / 3f * z) / Size;
            float fs = -fq - fr;
            int rq = Mathf.RoundToInt(fq), rr = Mathf.RoundToInt(fr), rs = Mathf.RoundToInt(fs);
            float dq = Mathf.Abs(rq - fq), dr = Mathf.Abs(rr - fr), ds = Mathf.Abs(rs - fs);
            if (dq > dr && dq > ds) rq = -rr - rs;
            else if (dr > ds) rr = -rq - rs;
            return new HexCoord(rq, rr);
        }

        public static HexCoord operator +(HexCoord a, HexCoord b) => new HexCoord(a.Q + b.Q, a.R + b.R);
        public static bool operator ==(HexCoord a, HexCoord b) => a.Q == b.Q && a.R == b.R;
        public static bool operator !=(HexCoord a, HexCoord b) => !(a == b);
        public bool Equals(HexCoord other) => Q == other.Q && R == other.R;
        public override bool Equals(object obj) => obj is HexCoord h && Equals(h);
        public override int GetHashCode() => unchecked((Q * 397) ^ R);
        public override string ToString() => $"({Q},{R})";
    }
}
