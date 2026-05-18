using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.Mapster
{
    public sealed class ModConfig
    {
        public bool ModEnabled { get; set; } = true;
        public bool AllowTeleport { get; set; } = false;
        public bool AnimatePreview { get; set; } = true;
        public int AnimatedPreviewRefreshTicks { get; set; } = 12;
        public bool DebugLogging { get; set; } = false;
        public KeybindList MapKey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.M));
    }
}
