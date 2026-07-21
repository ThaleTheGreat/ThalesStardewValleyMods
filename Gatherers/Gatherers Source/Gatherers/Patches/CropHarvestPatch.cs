using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Crops;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using ThaleTheGreat.Gatherers.Services;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Patches;

internal static class CropHarvestPatch
{
    [HarmonyPriority(Priority.Low)]
    internal static bool Prefix(
        Crop __instance,
        Vector2 ___tilePosition,
        int xTile,
        int yTile,
        HoeDirt soil,
        JunimoHarvester? junimoHarvester,
        bool isForcedScytheHarvest,
        ref bool __result)
    {
        if (!CropHarvestContext.TryGet(out Chest chest, out bool eatExcess))
            return true;

        __result = false;

        if (__instance.dead.Value)
            return false;

        if (__instance.forageCrop.Value)
        {
            HarvestForageCrop(__instance, soil, chest, eatExcess, xTile, yTile, ref __result);
            return false;
        }

        if (__instance.currentPhase.Value < __instance.phaseDays.Count - 1 || (__instance.fullyGrown.Value && __instance.dayOfCurrentPhase.Value > 0))
            return false;

        string? harvestItemId = __instance.indexOfHarvest.Value;
        if (harvestItemId is null)
        {
            CropHarvestContext.MarkHarvestSucceeded();
            __result = true;
            return false;
        }

        CropData? data = __instance.GetData();
        Random random = Utility.CreateRandom((double)xTile * 7.0, (double)yTile * 11.0, Game1.stats.DaysPlayed, Game1.uniqueIDForThisGame);
        int fertilizerQualityLevel = soil.GetFertilizerQualityBoostLevel();
        double chanceForGoldQuality = 0.2 * ((double)Game1.player.FarmingLevel / 10.0)
            + 0.2 * fertilizerQualityLevel * (((double)Game1.player.FarmingLevel + 2.0) / 12.0)
            + 0.01;
        double chanceForSilverQuality = Math.Min(0.75, chanceForGoldQuality * 2.0);
        int cropQuality = 0;

        if (fertilizerQualityLevel >= 3 && random.NextDouble() < chanceForGoldQuality / 2.0)
            cropQuality = SObject.bestQuality;
        else if (random.NextDouble() < chanceForGoldQuality)
            cropQuality = SObject.highQuality;
        else if (random.NextDouble() < chanceForSilverQuality || fertilizerQualityLevel >= 3)
            cropQuality = SObject.medQuality;

        cropQuality = MathHelper.Clamp(cropQuality, data?.HarvestMinQuality ?? 0, data?.HarvestMaxQuality ?? cropQuality);

        int numberToHarvest = 1;
        if (data is not null)
        {
            int minStack = data.HarvestMinStack;
            int maxStack = Math.Max(minStack, data.HarvestMaxStack);
            if (data.HarvestMaxIncreasePerFarmingLevel > 0f)
                maxStack += (int)(Game1.player.FarmingLevel * data.HarvestMaxIncreasePerFarmingLevel);

            if (minStack > 1 || maxStack > 1)
                numberToHarvest = random.Next(minStack, maxStack + 1);

            if (data.ExtraHarvestChance > 0.0)
            {
                while (random.NextDouble() < Math.Min(0.9, data.ExtraHarvestChance))
                    numberToHarvest++;
            }
        }

        List<Item> outputs = new()
        {
            CreateHarvestItem(__instance, harvestItemId, cropQuality, applyQuality: true)
        };

        HarvestMethod harvestMethod = data?.HarvestMethod ?? HarvestMethod.Grab;
        if (harvestMethod != HarvestMethod.Scythe && !isForcedScytheHarvest)
        {
            if (random.NextDouble() < Game1.player.team.AverageLuckLevel() / 1500.0
                + Game1.player.team.AverageDailyLuck() / 1200.0
                + 9.999999747378752E-05)
            {
                numberToHarvest *= 2;
            }
        }

        string remainingHarvestItemId = harvestItemId;
        bool changesHarvestItemId = harvestItemId == "421";
        if (changesHarvestItemId)
        {
            remainingHarvestItemId = "431";
            numberToHarvest = random.Next(1, 4);
        }

        for (int i = 0; i < numberToHarvest - 1; i++)
            outputs.Add(CreateHarvestItem(__instance, remainingHarvestItemId, 0, applyQuality: false));

        if (remainingHarvestItemId == "262" && random.NextDouble() < 0.4)
            outputs.Add(ItemRegistry.Create("(O)178"));
        else if (remainingHarvestItemId == "771" && random.NextDouble() < 0.1)
            outputs.Add(ItemRegistry.Create("(O)770"));

        if (!GathererService.TryAddItemsToChest(chest, outputs, eatExcess))
        {
            CropHarvestContext.MarkStorageBlocked();
            return false;
        }

        if (changesHarvestItemId)
            __instance.indexOfHarvest.Value = remainingHarvestItemId;

        CropHarvestContext.MarkHarvestSucceeded();
        int regrowDays = data?.RegrowDays ?? -1;
        if (regrowDays <= 0)
        {
            __result = true;
            return false;
        }

        __instance.fullyGrown.Value = true;
        if (__instance.dayOfCurrentPhase.Value == regrowDays)
            __instance.updateDrawMath(___tilePosition);
        __instance.dayOfCurrentPhase.Value = regrowDays;
        return false;
    }

    private static void HarvestForageCrop(Crop crop, HoeDirt soil, Chest chest, bool eatExcess, int xTile, int yTile, ref bool result)
    {
        if (crop.whichForageCrop.Value == "2")
        {
            soil.shake((float)Math.PI / 48f, (float)Math.PI / 40f, xTile * 64f < Game1.player.Position.X);
            return;
        }

        if (crop.whichForageCrop.Value != "1")
            return;

        SObject item = ItemRegistry.Create<SObject>("(O)399");
        Random random = Utility.CreateDaySaveRandom(xTile * 1000, yTile * 2000);
        if (Game1.player.professions.Contains(16))
            item.Quality = SObject.bestQuality;
        else if (random.NextDouble() < Game1.player.ForagingLevel / 30f)
            item.Quality = SObject.highQuality;
        else if (random.NextDouble() < Game1.player.ForagingLevel / 15f)
            item.Quality = SObject.medQuality;

        if (!GathererService.TryAddItemsToChest(chest, new[] { item }, eatExcess))
        {
            CropHarvestContext.MarkStorageBlocked();
            return;
        }

        Game1.stats.ItemsForaged += (uint)item.Stack;
        CropHarvestContext.MarkHarvestSucceeded();
        result = true;
    }

    private static Item CreateHarvestItem(Crop crop, string itemId, int quality, bool applyQuality)
    {
        if (crop.programColored.Value)
        {
            ColoredObject item = new(itemId, 1, crop.tintColor.Value);
            if (applyQuality)
                item.Quality = quality;
            return item;
        }

        return applyQuality
            ? ItemRegistry.Create(itemId, 1, quality)
            : ItemRegistry.Create(itemId);
    }
}
