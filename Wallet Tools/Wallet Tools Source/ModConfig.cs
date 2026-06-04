using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletTools;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool SwitchEnabled { get; set; } = true;
    public KeybindList ToggleSwitchKey { get; set; } = KeybindList.Parse("None");
    public bool WalletAxe { get; set; } = true;
    public bool WalletPickaxe { get; set; } = true;
    public bool WalletHoe { get; set; } = true;
    public bool WalletWateringCan { get; set; } = true;
    public bool WalletPan { get; set; } = true;

    public bool SwitchForObjects { get; set; } = true;
    public bool SwitchForTrees { get; set; } = true;
    public bool SwitchForResourceClumps { get; set; } = true;
    public bool SwitchForCrops { get; set; } = true;
    public bool SwitchForPan { get; set; } = true;
    public bool SwitchForWateringCan { get; set; } = true;
    public bool SwitchForWatering { get; set; } = true;
    public bool SwitchForTilling { get; set; } = true;
}
