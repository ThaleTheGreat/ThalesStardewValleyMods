using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletToolsForNatureInTheValley;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool AutoUseEnabled { get; set; } = true;
    public bool RequireLeftShiftForAutoUse { get; set; } = false;
    public bool PlayToolSwapSound { get; set; } = true;
    public bool ShowHudMessageWhenStored { get; set; } = true;

    public KeybindList ToggleAutoUseHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.LeftShift, SButton.D9));
    public KeybindList UseNatureNetHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.N));
}
