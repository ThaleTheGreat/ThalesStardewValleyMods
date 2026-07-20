namespace ThaleTheGreat.Gatherers.Framework;

public sealed class ModConfig
{
    public bool DoJunimosEatExcessCrops { get; set; } = true;
    public bool DoJunimosHarvestFromPots { get; set; } = true;
    public bool DoJunimosAppearAfterHarvest { get; set; } = true;
    public bool DoJunimosHarvestFromFruitTrees { get; set; } = true;
    public bool DoJunimosHarvestFromFlowers { get; set; } = true;
    public bool DoJunimosSowSeedsAfterHarvest { get; set; } = true;
    public bool ForceRecipeUnlock { get; set; }
    public bool EnableHarvestMessage { get; set; } = true;
    public int MaxAmountOfJunimosToAppearAfterHarvest { get; set; } = -1;
    public int MinimumFruitOnTreeBeforeHarvest { get; set; } = 3;

    public bool DoParrotsEatExcessCrops { get; set; } = true;
    public bool DoParrotsHarvestFromPots { get; set; } = true;
    public bool DoParrotsAppearAfterHarvest { get; set; } = true;
    public bool DoParrotsHarvestFromFruitTrees { get; set; } = true;
    public bool DoParrotsHarvestFromFlowers { get; set; } = true;
    public bool DoParrotsSowSeedsAfterHarvest { get; set; } = true;
    public bool EnableParrotHarvestMessage { get; set; } = true;
    public int MinimumFruitOnTreeBeforeParrotHarvest { get; set; } = 3;

    public void Reset()
    {
        ModConfig defaults = new();

        DoJunimosEatExcessCrops = defaults.DoJunimosEatExcessCrops;
        DoJunimosHarvestFromPots = defaults.DoJunimosHarvestFromPots;
        DoJunimosAppearAfterHarvest = defaults.DoJunimosAppearAfterHarvest;
        DoJunimosHarvestFromFruitTrees = defaults.DoJunimosHarvestFromFruitTrees;
        DoJunimosHarvestFromFlowers = defaults.DoJunimosHarvestFromFlowers;
        DoJunimosSowSeedsAfterHarvest = defaults.DoJunimosSowSeedsAfterHarvest;
        ForceRecipeUnlock = defaults.ForceRecipeUnlock;
        EnableHarvestMessage = defaults.EnableHarvestMessage;
        MaxAmountOfJunimosToAppearAfterHarvest = defaults.MaxAmountOfJunimosToAppearAfterHarvest;
        MinimumFruitOnTreeBeforeHarvest = defaults.MinimumFruitOnTreeBeforeHarvest;

        DoParrotsEatExcessCrops = defaults.DoParrotsEatExcessCrops;
        DoParrotsHarvestFromPots = defaults.DoParrotsHarvestFromPots;
        DoParrotsAppearAfterHarvest = defaults.DoParrotsAppearAfterHarvest;
        DoParrotsHarvestFromFruitTrees = defaults.DoParrotsHarvestFromFruitTrees;
        DoParrotsHarvestFromFlowers = defaults.DoParrotsHarvestFromFlowers;
        DoParrotsSowSeedsAfterHarvest = defaults.DoParrotsSowSeedsAfterHarvest;
        EnableParrotHarvestMessage = defaults.EnableParrotHarvestMessage;
        MinimumFruitOnTreeBeforeParrotHarvest = defaults.MinimumFruitOnTreeBeforeParrotHarvest;
    }
}
