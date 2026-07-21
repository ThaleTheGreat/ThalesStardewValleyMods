using StardewModdingAPI;
using ThaleTheGreat.Gatherers.Framework;

namespace ThaleTheGreat.Gatherers.Integration;

internal sealed class GmcmIntegration
{
    private readonly IModHelper Helper;
    private readonly IManifest Manifest;
    private readonly ModConfig Config;

    internal GmcmIntegration(IModHelper helper, IManifest manifest, ModConfig config)
    {
        Helper = helper;
        Manifest = manifest;
        Config = config;
    }

    internal void Register()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(Manifest, Config.Reset, () => Helper.WriteConfig(Config));

        gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.greenhouse"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosEatExcessCrops, value => Config.DoJunimosEatExcessCrops = value, () => Helper.Translation.Get("gmcm.eat-excess.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosHarvestFromPots, value => Config.DoJunimosHarvestFromPots = value, () => Helper.Translation.Get("gmcm.harvest-pots.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosAppearAfterHarvest, value => Config.DoJunimosAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.appear.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosHarvestFromFruitTrees, value => Config.DoJunimosHarvestFromFruitTrees = value, () => Helper.Translation.Get("gmcm.harvest-fruit-trees.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosHarvestFromFlowers, value => Config.DoJunimosHarvestFromFlowers = value, () => Helper.Translation.Get("gmcm.harvest-flowers.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosSowSeedsAfterHarvest, value => Config.DoJunimosSowSeedsAfterHarvest = value, () => Helper.Translation.Get("gmcm.sow-seeds.junimos.name"));
        gmcm.AddBoolOption(Manifest, () => Config.ForceRecipeUnlock, value => Config.ForceRecipeUnlock = value, () => Helper.Translation.Get("gmcm.force-recipe.name"));
        gmcm.AddBoolOption(Manifest, () => Config.EnableHarvestMessage, value => Config.EnableHarvestMessage = value, () => Helper.Translation.Get("gmcm.harvest-message.name"));
        gmcm.AddNumberOption(Manifest, () => Config.MaxAmountOfJunimosToAppearAfterHarvest, value => Config.MaxAmountOfJunimosToAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.max-junimos.name"), min: -1, max: 50, interval: 1);
        gmcm.AddNumberOption(Manifest, () => Config.MinimumFruitOnTreeBeforeHarvest, value => Config.MinimumFruitOnTreeBeforeHarvest = value, () => Helper.Translation.Get("gmcm.minimum-fruit.junimos.name"), min: 1, max: 3, interval: 1);

        gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.island"));
        gmcm.AddBoolOption(Manifest, () => Config.EnableParrotHarvestMessage, value => Config.EnableParrotHarvestMessage = value, () => Helper.Translation.Get("gmcm.harvest-message.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsEatExcessCrops, value => Config.DoParrotsEatExcessCrops = value, () => Helper.Translation.Get("gmcm.eat-excess.parrots.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsHarvestFromPots, value => Config.DoParrotsHarvestFromPots = value, () => Helper.Translation.Get("gmcm.harvest-pots.parrots.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsAppearAfterHarvest, value => Config.DoParrotsAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.appear.parrots.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsHarvestFromFruitTrees, value => Config.DoParrotsHarvestFromFruitTrees = value, () => Helper.Translation.Get("gmcm.harvest-fruit-trees.parrots.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsHarvestFromFlowers, value => Config.DoParrotsHarvestFromFlowers = value, () => Helper.Translation.Get("gmcm.harvest-flowers.parrots.name"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsSowSeedsAfterHarvest, value => Config.DoParrotsSowSeedsAfterHarvest = value, () => Helper.Translation.Get("gmcm.sow-seeds.parrots.name"));
        gmcm.AddNumberOption(Manifest, () => Config.MinimumFruitOnTreeBeforeParrotHarvest, value => Config.MinimumFruitOnTreeBeforeParrotHarvest = value, () => Helper.Translation.Get("gmcm.minimum-fruit.parrots.name"), min: 1, max: 3, interval: 1);
    }
}
