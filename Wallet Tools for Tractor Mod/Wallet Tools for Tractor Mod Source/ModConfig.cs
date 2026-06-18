using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletToolsForTractorMod;

internal sealed class ModConfig
{
    public bool EnableTractorWalletSelector { get; set; } = true;
    public KeybindList CyclePreviousToolKey { get; set; } = new(new Keybind(SButton.Q));
    public KeybindList CycleNextToolKey { get; set; } = new(new Keybind(SButton.E));
    public bool ShowSelectorOverlay { get; set; } = true;
    public string SelectorOverlayPosition { get; set; } = "Top of Screen";
    public bool PlayCycleSound { get; set; } = true;
    public bool DebugLogging { get; set; } = false;
}
