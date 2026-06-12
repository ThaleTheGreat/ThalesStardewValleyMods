using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletToolsForAnimalHusbandry;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool AutoUseEnabled { get; set; } = true;
    public bool RequireLeftShiftForAutoUse { get; set; } = true;
    public bool PlayToolSwapSound { get; set; } = true;
    public bool ShowHudMessageWhenStored { get; set; } = true;

    public KeybindList ToggleAutoUseHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D9));
    public KeybindList UseMeatToolHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D0));
}
