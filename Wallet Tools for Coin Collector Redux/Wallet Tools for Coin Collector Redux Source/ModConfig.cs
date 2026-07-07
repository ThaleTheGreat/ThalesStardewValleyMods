using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletToolsForCoinCollectorRedux;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool EnablePassiveWalletDetection { get; set; } = true;
    public bool EnableManualUseHotkey { get; set; } = true;
    public bool PlayToolSwapSound { get; set; } = true;
    public bool ShowHudMessageWhenStored { get; set; } = true;

    public KeybindList UseMetalDetectorHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.M));
}
