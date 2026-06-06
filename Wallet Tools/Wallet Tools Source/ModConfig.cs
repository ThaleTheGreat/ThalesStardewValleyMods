using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletTools;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;

    public KeybindList UseAxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D1));
    public KeybindList UsePickaxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D2));
    public KeybindList UseHoeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D3));
    public KeybindList UseWateringCanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D4));
    public KeybindList UsePanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D5));
    public KeybindList UseMilkPailHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D6));
    public KeybindList UseShearsHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D7));
}

