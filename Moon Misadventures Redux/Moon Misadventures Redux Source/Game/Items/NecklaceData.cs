using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ThaleTheGreat.MoonMisadventures.Game.Items
{
    public class NecklaceData
    {
        public string DisplayName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Texture { get; set; } = string.Empty;
        public int TextureIndex { get; set; }
        public bool CanBeSelectedAtAltar { get; set; } = true;
    }
}
