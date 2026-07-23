using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace ThaleTheGreat.PyromancersJourney
{
    internal static class ReticlePalette
    {
        public static readonly string[] Names =
        {
            "White",
            "Black",
            "Gray",
            "Red",
            "Orange",
            "Yellow",
            "Lime",
            "Green",
            "Cyan",
            "Blue",
            "Purple",
            "Pink"
        };

        private static readonly IReadOnlyDictionary<string, Color> Colors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
        {
            ["White"] = Color.White,
            ["Black"] = Color.Black,
            ["Gray"] = Color.Gray,
            ["Red"] = Color.Red,
            ["Orange"] = Color.Orange,
            ["Yellow"] = Color.Yellow,
            ["Lime"] = Color.Lime,
            ["Green"] = Color.Green,
            ["Cyan"] = Color.Cyan,
            ["Blue"] = Color.Blue,
            ["Purple"] = Color.Purple,
            ["Pink"] = Color.HotPink
        };

        public static Color Get(string? name)
        {
            return name is not null && Colors.TryGetValue(name, out Color color)
                ? color
                : Color.White;
        }
    }
}
