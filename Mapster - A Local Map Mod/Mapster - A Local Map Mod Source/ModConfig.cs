using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.Mapster
{
    public sealed class ModConfig
    {
        public bool ModEnabled { get; set; } = true;
        public bool AllowTeleport { get; set; } = false;
        public bool ShowLocationDropdown { get; set; } = true;
        public bool ShowMiniMap { get; set; } = true;
        public string MiniMapSize { get; set; } = "Medium";
        public int MiniMapXPercent { get; set; } = 0;
        public int MiniMapYPercent { get; set; } = 0;
        public bool ShowNpcMapLocationsOnMap { get; set; } = true;
        public bool ShowNpcMapLocationsOnMiniMap { get; set; } = true;
        public bool ShowNpcMapLocationTooltipsOnMap { get; set; } = true;
        public bool ShowNpcMapLocationTooltipsOnMiniMap { get; set; } = true;
        public bool ShowMobilePhoneApp { get; set; } = true;
        public bool AnimatePreview { get; set; } = true;
        public int AnimatedPreviewRefreshTicks { get; set; } = 12;
        public bool DebugLogging { get; set; } = false;
        public KeybindList MapKey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.M));
    }
}
