using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Framework;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Services;

internal static class MigrationService
{
    private const string CombinedConvertedFlag = "ThaleTheGreat.Gatherers/HasConvertedLegacyGatherers";

    internal static void Run()
    {
        if (!Game1.IsMasterGame)
            return;

        foreach (GameLocation location in LocationScanner.AllLocationsAndBuildingInteriors())
            ConvertLocation(location);

        Game1.MasterPlayer.modData[CombinedConvertedFlag] = bool.TrueString;
        Game1.MasterPlayer.modData[ModConstants.LegacyHarvestStatueConvertedFlag] = bool.TrueString;
        Game1.MasterPlayer.modData[ModConstants.LegacyParrotPotConvertedFlag] = bool.TrueString;
        MigrateRecipeKey();
    }

    private static void ConvertLocation(GameLocation location)
    {
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs.Where(pair => pair.Value is Chest).ToList())
        {
            Chest chest = (Chest)pair.Value;
            GathererKind? kind = StorageMarker.IsHarvestStatue(chest)
                ? GathererKind.HarvestStatue
                : StorageMarker.IsParrotPot(chest)
                    ? GathererKind.ParrotPot
                    : null;
            if (kind is null)
                continue;

            string expectedItemId = kind.Value == GathererKind.HarvestStatue
                ? ModConstants.HarvestStatueItemId
                : ModConstants.ParrotPotItemId;

            if (!string.Equals(chest.ItemId, expectedItemId, StringComparison.Ordinal))
                chest = ReplaceChest(location, pair.Key, chest, expectedItemId);

            StorageMarker.Mark(chest, kind.Value);
        }
    }

    private static Chest ReplaceChest(GameLocation location, Vector2 tile, Chest source, string itemId)
    {
        Chest replacement = new(true, tile, itemId)
        {
            Location = location,
            TileLocation = tile
        };

        foreach (KeyValuePair<string, string> pair in source.modData.Pairs)
            replacement.modData[pair.Key] = pair.Value;

        foreach (Item item in source.Items.OfType<Item>())
            replacement.Items.Add(item);

        location.objects[tile] = replacement;
        return replacement;
    }

    private static void MigrateRecipeKey()
    {
        var recipes = Game1.MasterPlayer.craftingRecipes;
        if (!recipes.TryGetValue(ModConstants.PreviousCombinedHarvestStatueRecipeKey, out int previousCount))
            return;

        recipes.Remove(ModConstants.PreviousCombinedHarvestStatueRecipeKey);
        if (recipes.TryGetValue(ModConstants.HarvestStatueRecipeKey, out int currentCount))
            recipes[ModConstants.HarvestStatueRecipeKey] = Math.Max(currentCount, previousCount);
        else
            recipes[ModConstants.HarvestStatueRecipeKey] = previousCount;
    }
}
