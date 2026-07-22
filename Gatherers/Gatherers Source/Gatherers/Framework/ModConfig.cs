namespace ThaleTheGreat.Gatherers.Framework;

public sealed class ModConfig
{
    public static readonly int[] AllowedStorageCapacities =
    {
        36,
        48,
        60,
        72,
        84,
        96,
        108,
        120
    };

    public bool EatExcessCrops { get; set; } = true;
    public bool HarvestGardenPots { get; set; } = true;
    public bool HarvestFruitTrees { get; set; } = true;
    public bool HarvestFlowers { get; set; } = true;
    public bool SowSeedsAfterHarvest { get; set; } = true;
    public bool EnableHarvestMessage { get; set; } = true;
    public int MinimumFruitOnTreeBeforeHarvest { get; set; } = 3;
    public int StorageCapacity { get; set; } = 36;

    public bool DoJunimosAppearAfterHarvest { get; set; } = true;
    public int MaxAmountOfJunimosToAppearAfterHarvest { get; set; } = -1;
    public bool ForceRecipeUnlock { get; set; }

    public bool DoParrotsAppearAfterHarvest { get; set; } = true;

    public void Reset()
    {
        ModConfig defaults = new();

        EatExcessCrops = defaults.EatExcessCrops;
        HarvestGardenPots = defaults.HarvestGardenPots;
        HarvestFruitTrees = defaults.HarvestFruitTrees;
        HarvestFlowers = defaults.HarvestFlowers;
        SowSeedsAfterHarvest = defaults.SowSeedsAfterHarvest;
        EnableHarvestMessage = defaults.EnableHarvestMessage;
        MinimumFruitOnTreeBeforeHarvest = defaults.MinimumFruitOnTreeBeforeHarvest;
        StorageCapacity = defaults.StorageCapacity;
        DoJunimosAppearAfterHarvest = defaults.DoJunimosAppearAfterHarvest;
        MaxAmountOfJunimosToAppearAfterHarvest = defaults.MaxAmountOfJunimosToAppearAfterHarvest;
        ForceRecipeUnlock = defaults.ForceRecipeUnlock;
        DoParrotsAppearAfterHarvest = defaults.DoParrotsAppearAfterHarvest;
    }

    public void Normalize(bool expandedStorageInstalled)
    {
        MinimumFruitOnTreeBeforeHarvest = Math.Clamp(MinimumFruitOnTreeBeforeHarvest, 1, 3);
        MaxAmountOfJunimosToAppearAfterHarvest = Math.Clamp(MaxAmountOfJunimosToAppearAfterHarvest, -1, 50);

        if (!expandedStorageInstalled)
        {
            StorageCapacity = 36;
            return;
        }

        if (StorageCapacity > AllowedStorageCapacities[^1])
            StorageCapacity = AllowedStorageCapacities[^1];
        else if (!AllowedStorageCapacities.Contains(StorageCapacity))
            StorageCapacity = 36;
    }
}
