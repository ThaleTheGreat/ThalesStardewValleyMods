using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletTools;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool WalletAxe { get; set; } = true;
    public bool WalletPickaxe { get; set; } = true;
    public bool WalletHoe { get; set; } = true;
    public bool WalletWateringCan { get; set; } = true;
    public bool WalletPan { get; set; } = true;


    public KeybindList UseAxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D1));
    public KeybindList UsePickaxeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D2));
    public KeybindList UseHoeHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D3));
    public KeybindList UseWateringCanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D4));
    public KeybindList UsePanHotkey { get; set; } = new(new Keybind(SButton.LeftControl, SButton.D5));

    public bool FallbackSwitchEnabled { get; set; } = true;
    public bool FallbackSwitchForObjects { get; set; } = true;
    public bool FallbackSwitchForTrees { get; set; } = true;
    public bool FallbackSwitchForResourceClumps { get; set; } = true;
    public bool FallbackSwitchForCrops { get; set; } = true;
    public bool FallbackSwitchForPan { get; set; } = true;
    public bool FallbackSwitchForWateringCan { get; set; } = true;
    public bool FallbackSwitchForWatering { get; set; } = true;
    public bool FallbackSwitchForTilling { get; set; } = true;

}
