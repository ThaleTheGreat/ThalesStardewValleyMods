namespace ThaleTheGreat.WalletTools;

internal sealed class ModConfig
{
    public bool ModEnabled { get; set; } = true;
    public bool WalletAxe { get; set; } = true;
    public bool WalletPickaxe { get; set; } = true;
    public bool WalletHoe { get; set; } = true;
    public bool WalletWateringCan { get; set; } = true;
    public bool WalletPan { get; set; } = true;

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
