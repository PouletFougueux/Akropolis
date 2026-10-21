using UnityEngine;

namespace Akropolis.View
{
    public static class Palette
    {
        public static readonly Color Cream = Hex("#FBF4E4");
        public static readonly Color Paper = Hex("#F1E3C4");
        public static readonly Color Ink = Hex("#4A3A2C");
        public static readonly Color Teal = Hex("#2E6E6A");
        public static readonly Color Wood = Hex("#B07A4E");
        public static readonly Color Felt = Hex("#D9C8A2");
        public static readonly Color Sky = Hex("#BFE0EA");
        public static readonly Color Marble = Hex("#EFE9DD");
        public static readonly Color Stone = Hex("#A9ADB1");
        public static readonly Color Gold = Hex("#E9B949");

        static readonly Color[] Players = { Hex("#2E8C85"), Hex("#E0745A"), Hex("#E2A93A"), Hex("#8A6BBE") };

        public static Color Player(int i) => Players[((i % Players.Length) + Players.Length) % Players.Length];

        public static Color DistrictColor(District d)
        {
            switch (d)
            {
                case District.Houses: return Hex("#3F7FD0");
                case District.Markets: return Hex("#EDB93A");
                case District.Barracks: return Hex("#D4553E");
                case District.Temples: return Hex("#8E62C6");
                case District.Gardens: return Hex("#4FA052");
                default: return Hex("#A3A6AA");
            }
        }

        public static Color Hex(string html)
        {
            ColorUtility.TryParseHtmlString(html, out var c);
            return c;
        }

        public static string Html(Color c) => "#" + ColorUtility.ToHtmlStringRGB(c);
    }
}
