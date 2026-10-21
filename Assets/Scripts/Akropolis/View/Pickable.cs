using UnityEngine;

namespace Akropolis.View
{
    public enum PickKind { Site, Board, Bell }

    public sealed class Pickable : MonoBehaviour
    {
        public PickKind Kind;
        public int Player;
        public int Index;

        public static Pickable Add(GameObject go, PickKind kind, int player, int index)
        {
            var p = go.AddComponent<Pickable>();
            p.Kind = kind;
            p.Player = player;
            p.Index = index;
            return p;
        }
    }
}
