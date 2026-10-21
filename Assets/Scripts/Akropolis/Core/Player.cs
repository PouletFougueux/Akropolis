namespace Akropolis
{
    public sealed class Player
    {
        public readonly int Index;
        public readonly string Name;
        /// <summary>Modifiable : un joueur en ligne qui part est remplacé par une IA.</summary>
        public bool IsAI;
        public readonly City City;
        public int Stones;

        public Player(int index, string name, bool isAI, int stones)
        {
            Index = index;
            Name = name;
            IsAI = isAI;
            Stones = stones;
            City = City.CreateWithStartingTile();
        }
    }
}
