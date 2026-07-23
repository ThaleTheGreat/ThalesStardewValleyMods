namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal sealed record AlternativeCoreToolTierRegistration(
    string OwnerModId,
    string TierId,
    int UpgradeLevel,
    string TextureAsset,
    string BarItemId,
    string AxeId,
    string AxeDisplayName,
    string PickaxeId,
    string PickaxeDisplayName,
    string HoeId,
    string HoeDisplayName,
    string WateringCanId,
    string WateringCanDisplayName
);
