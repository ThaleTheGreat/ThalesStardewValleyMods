using System.Collections.Generic;

namespace ThaleTheGreat.CoinCollectorRedux
{
    public class CoinData
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string SetName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Description { get; set; } = "";
        public string TexturePath { get; set; } = "";
        public int SpriteIndex { get; set; }
        public List<string> Locations { get; set; } = new();
        public float Rarity { get; set; } = 1f;
        public int Price { get; set; } = 750;
        public string Type { get; set; } = "Minerals";
        public int Category { get; set; } = -2;
        public bool CreateObject { get; set; } = true;
        public List<string> ContextTags { get; set; } = new();

        public string QualifiedItemId()
        {
            return $"(O){UnqualifiedObjectId()}";
        }

        public string UnqualifiedObjectId()
        {
            return Id.StartsWith("(O)") ? Id[3..] : Id;
        }
    }
}
