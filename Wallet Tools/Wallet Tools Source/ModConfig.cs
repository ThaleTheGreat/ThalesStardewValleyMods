using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletTools;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool AxeEnabled { get; set; } = true;
    public bool PickaxeEnabled { get; set; } = true;
    public bool HoeEnabled { get; set; } = true;
    public bool WateringCanEnabled { get; set; } = true;
    public bool PanEnabled { get; set; } = true;
    public bool MilkPailEnabled { get; set; } = true;
    public bool ShearsEnabled { get; set; } = true;

    public bool AutoUseEnabled { get; set; } = true;
    public bool AxeAutoUseEnabled { get; set; } = true;
    public bool PickaxeAutoUseEnabled { get; set; } = true;
    public bool HoeAutoUseEnabled { get; set; } = true;
    public bool WateringCanAutoUseEnabled { get; set; } = true;
    public bool PanAutoUseEnabled { get; set; } = true;
    public bool MilkPailAutoUseEnabled { get; set; } = true;
    public bool ShearsAutoUseEnabled { get; set; } = true;

    public bool PlayToolSwapSound { get; set; } = true;
    public bool UseNewToolUseLogic { get; set; } = false;
    public bool ItemExtensionsCompatibilityEnabled { get; set; } = true;
    public bool ItemExtensionsDebugLogging { get; set; } = false;

    public KeybindList ToggleAutoUseHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D8));
    public KeybindList HeldItemAutoUseModifierHotkey { get; set; } = new(new Keybind(SButton.LeftShift));

    public KeybindList UseAxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D1));
    public KeybindList UsePickaxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D2));
    public KeybindList UseHoeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D3));
    public KeybindList UseWateringCanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D4));
    public KeybindList UsePanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D5));
    public KeybindList UseMilkPailHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D6));
    public KeybindList UseShearsHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D7));
}

