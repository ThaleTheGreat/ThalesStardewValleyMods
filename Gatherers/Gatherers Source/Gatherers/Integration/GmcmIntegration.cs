using StardewModdingAPI;
using StardewValley;
using System.Globalization;
using ThaleTheGreat.Gatherers.Framework;
using ThaleTheGreat.Gatherers.Services;

namespace ThaleTheGreat.Gatherers.Integration;

public sealed class GmcmIntegration
{
    private static readonly string[] StorageCapacityValues = ModConfig.AllowedStorageCapacities
        .Select(value => value.ToString(CultureInfo.InvariantCulture))
        .ToArray();

    private readonly IModHelper Helper;
    private readonly IManifest Manifest;
    private readonly ModConfig Config;
    private readonly bool ExpandedStorageInstalled;

    public GmcmIntegration(IModHelper helper, IManifest manifest, ModConfig config)
    {
        Helper = helper;
        Manifest = manifest;
        Config = config;
        ExpandedStorageInstalled = helper.ModRegistry.IsLoaded(ModConstants.ExpandedStorageModId);
    }

    public void Register()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(Manifest, Config.Reset, Save);
        gmcm.AddParagraph(Manifest, () => Helper.Translation.Get("gmcm.intro"));

        gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.harvesting"));
        gmcm.AddBoolOption(Manifest, () => Config.EnableHarvestMessage, value => Config.EnableHarvestMessage = value, () => Helper.Translation.Get("gmcm.harvest-message.name"), () => Helper.Translation.Get("gmcm.harvest-message.tooltip"), nameof(ModConfig.EnableHarvestMessage));
        gmcm.AddBoolOption(Manifest, () => Config.EatExcessCrops, value => Config.EatExcessCrops = value, () => Helper.Translation.Get("gmcm.eat-excess.name"), () => Helper.Translation.Get("gmcm.eat-excess.tooltip"), nameof(ModConfig.EatExcessCrops));
        gmcm.AddBoolOption(Manifest, () => Config.HarvestGardenPots, value => Config.HarvestGardenPots = value, () => Helper.Translation.Get("gmcm.harvest-pots.name"), () => Helper.Translation.Get("gmcm.harvest-pots.tooltip"), nameof(ModConfig.HarvestGardenPots));
        gmcm.AddBoolOption(Manifest, () => Config.HarvestFruitTrees, value => Config.HarvestFruitTrees = value, () => Helper.Translation.Get("gmcm.harvest-fruit-trees.name"), () => Helper.Translation.Get("gmcm.harvest-fruit-trees.tooltip"), nameof(ModConfig.HarvestFruitTrees));
        gmcm.AddBoolOption(Manifest, () => Config.HarvestFlowers, value => Config.HarvestFlowers = value, () => Helper.Translation.Get("gmcm.harvest-flowers.name"), () => Helper.Translation.Get("gmcm.harvest-flowers.tooltip"), nameof(ModConfig.HarvestFlowers));
        gmcm.AddBoolOption(Manifest, () => Config.SowSeedsAfterHarvest, value => Config.SowSeedsAfterHarvest = value, () => Helper.Translation.Get("gmcm.sow-seeds.name"), () => Helper.Translation.Get("gmcm.sow-seeds.tooltip"), nameof(ModConfig.SowSeedsAfterHarvest));
        gmcm.AddNumberOption(Manifest, () => Config.MinimumFruitOnTreeBeforeHarvest, value => Config.MinimumFruitOnTreeBeforeHarvest = value, () => Helper.Translation.Get("gmcm.minimum-fruit.name"), () => Helper.Translation.Get("gmcm.minimum-fruit.tooltip"), min: 1, max: 3, interval: 1, fieldId: nameof(ModConfig.MinimumFruitOnTreeBeforeHarvest));

        if (ExpandedStorageInstalled)
        {
            gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.storage"));
            gmcm.AddTextOption(
                Manifest,
                () => Config.StorageCapacity.ToString(CultureInfo.InvariantCulture),
                SetStorageCapacity,
                () => Helper.Translation.Get("gmcm.storage-capacity.name"),
                () => Helper.Translation.Get("gmcm.storage-capacity.tooltip"),
                StorageCapacityValues,
                fieldId: nameof(ModConfig.StorageCapacity));
        }

        gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.junimos"));
        gmcm.AddBoolOption(Manifest, () => Config.DoJunimosAppearAfterHarvest, value => Config.DoJunimosAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.appear.junimos.name"), () => Helper.Translation.Get("gmcm.appear.junimos.tooltip"), nameof(ModConfig.DoJunimosAppearAfterHarvest));
        gmcm.AddNumberOption(Manifest, () => Config.MaxAmountOfJunimosToAppearAfterHarvest, value => Config.MaxAmountOfJunimosToAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.max-junimos.name"), () => Helper.Translation.Get("gmcm.max-junimos.tooltip"), min: -1, max: 50, interval: 1, fieldId: nameof(ModConfig.MaxAmountOfJunimosToAppearAfterHarvest));
        gmcm.AddBoolOption(Manifest, () => Config.ForceRecipeUnlock, value => Config.ForceRecipeUnlock = value, () => Helper.Translation.Get("gmcm.force-recipe.name"), () => Helper.Translation.Get("gmcm.force-recipe.tooltip"), nameof(ModConfig.ForceRecipeUnlock));

        gmcm.AddSectionTitle(Manifest, () => Helper.Translation.Get("gmcm.section.parrots"));
        gmcm.AddBoolOption(Manifest, () => Config.DoParrotsAppearAfterHarvest, value => Config.DoParrotsAppearAfterHarvest = value, () => Helper.Translation.Get("gmcm.appear.parrots.name"), () => Helper.Translation.Get("gmcm.appear.parrots.tooltip"), nameof(ModConfig.DoParrotsAppearAfterHarvest));
    }

    private void SetStorageCapacity(string value)
    {
        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int capacity)
            && ModConfig.AllowedStorageCapacities.Contains(capacity))
        {
            Config.StorageCapacity = capacity;
        }
    }

    private void Save()
    {
        Config.Normalize(ExpandedStorageInstalled);
        Helper.WriteConfig(Config);

        if (Context.IsWorldReady && Game1.IsMasterGame)
            StorageMarker.ApplyCapacityToAllGathererStorage(Config.StorageCapacity);
    }
}
