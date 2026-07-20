using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using ThaleTheGreat.Gatherers.Framework;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Services;

internal sealed class GathererService
{
    private readonly ModConfig Config;

    internal GathererService(ModConfig config)
    {
        Config = config;
    }

    internal void HarvestLocation(GameLocation location, Chest chest, GathererKind kind)
    {
        StorageMarker.ResetDailyFlags(chest);

        HarvestOptions options = kind == GathererKind.HarvestStatue
            ? new HarvestOptions(
                Config.EnableHarvestMessage,
                Config.DoJunimosEatExcessCrops,
                Config.DoJunimosHarvestFromPots,
                Config.DoJunimosHarvestFromFruitTrees,
                Config.DoJunimosHarvestFromFlowers,
                Config.DoJunimosSowSeedsAfterHarvest,
                Config.MinimumFruitOnTreeBeforeHarvest)
            : new HarvestOptions(
                Config.EnableParrotHarvestMessage,
                Config.DoParrotsEatExcessCrops,
                Config.DoParrotsHarvestFromPots,
                Config.DoParrotsHarvestFromFruitTrees,
                Config.DoParrotsHarvestFromFlowers,
                Config.DoParrotsSowSeedsAfterHarvest,
                Config.MinimumFruitOnTreeBeforeParrotHarvest);

        string locationName = SplitCamelCaseText(location.Name);
        List<Vector2> harvestedTiles = new();
        bool harvestedToday = false;
        bool isFull = false;

        using (CropHarvestContext.Enter(chest, options.EatExcessCrops))
        {
            HarvestGroundCropsAndForage(location, chest, options, harvestedTiles, ref harvestedToday, ref isFull);
            if (!isFull && options.HarvestFromPots)
                HarvestIndoorPots(location, chest, options, ref harvestedToday, ref isFull);
            if (!isFull && options.HarvestFromFruitTrees)
                HarvestFruitTrees(location, chest, options, ref harvestedToday, ref isFull);
        }

        if (harvestedToday)
            StorageMarker.MarkHarvestedTiles(chest, harvestedTiles);

        if (isFull)
        {
            ShowFullMessage(kind, locationName);
            return;
        }

        if (StorageMarker.AteCrops(chest))
        {
            ShowAteMessage(kind, locationName);
            return;
        }

        if (harvestedToday && options.EnableHarvestMessage)
            ShowHarvestedMessage(kind, locationName);
    }

    private static void HarvestGroundCropsAndForage(GameLocation location, Chest chest, HarvestOptions options, List<Vector2> harvestedTiles, ref bool harvestedToday, ref bool isFull)
    {
        foreach (KeyValuePair<Vector2, TerrainFeature> pair in location.terrainFeatures.Pairs.Where(pair => pair.Value is HoeDirt { crop: not null }).ToList())
        {
            Vector2 tile = pair.Key;
            HoeDirt hoeDirt = (HoeDirt)pair.Value;
            Crop crop = hoeDirt.crop;
            if (!hoeDirt.readyForHarvest())
                continue;

            if (!options.HarvestFlowers && IsFlowerCrop(crop))
                continue;

            string? seedId = crop.netSeedIndex.Value;
            CropHarvestContext.ResetHarvestResult();
            bool removeCrop = crop.harvest((int)tile.X, (int)tile.Y, hoeDirt, null, false);

            if (CropHarvestContext.WasStorageBlocked)
            {
                isFull = true;
                return;
            }

            if (!CropHarvestContext.WasHarvestSuccessful)
                continue;

            harvestedToday = true;
            harvestedTiles.Add(tile);
            if (!removeCrop)
                continue;

            hoeDirt.crop = null;
            if (options.SowSeedsAfterHarvest && !string.IsNullOrWhiteSpace(seedId))
                AttemptSowSeed(seedId, hoeDirt, tile, location, chest);
        }

        List<Vector2> forageTiles = new();
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs.Where(pair => pair.Value.isForage()).ToList())
        {
            if (!TryAddItemsToChest(chest, new[] { CloneItem(pair.Value.getOne()) }, options.EatExcessCrops))
            {
                isFull = true;
                return;
            }

            forageTiles.Add(pair.Key);
            harvestedToday = true;
            harvestedTiles.Add(pair.Key);
        }

        foreach (Vector2 tile in forageTiles)
            location.removeObject(tile, false);
    }

    private static void HarvestIndoorPots(GameLocation location, Chest chest, HarvestOptions options, ref bool harvestedToday, ref bool isFull)
    {
        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs.Where(pair => pair.Value is IndoorPot).ToList())
        {
            Vector2 tile = pair.Key;
            IndoorPot pot = (IndoorPot)pair.Value;
            HoeDirt hoeDirt = pot.hoeDirt.Value;

            if (hoeDirt.crop is not null && hoeDirt.readyForHarvest())
            {
                Crop crop = hoeDirt.crop;
                if (!options.HarvestFlowers && IsFlowerCrop(crop))
                    continue;

                string? seedId = crop.netSeedIndex.Value;
                CropHarvestContext.ResetHarvestResult();
                bool removeCrop = crop.harvest((int)tile.X, (int)tile.Y, hoeDirt, null, false);

                if (CropHarvestContext.WasStorageBlocked)
                {
                    isFull = true;
                    return;
                }

                if (CropHarvestContext.WasHarvestSuccessful)
                {
                    harvestedToday = true;
                    if (removeCrop)
                    {
                        hoeDirt.crop = null;
                        if (options.SowSeedsAfterHarvest && !string.IsNullOrWhiteSpace(seedId))
                            AttemptSowSeed(seedId, hoeDirt, tile, location, chest);
                    }
                }
            }
        }

        foreach (KeyValuePair<Vector2, SObject> pair in location.objects.Pairs.Where(pair => pair.Value is IndoorPot pot && pot.heldObject.Value is not null).ToList())
        {
            Vector2 tile = pair.Key;
            IndoorPot pot = (IndoorPot)pair.Value;
            SObject? held = pot.heldObject.Value;
            if (held is null || !held.isForage())
                continue;

            if (!TryAddItemsToChest(chest, new[] { CloneItem(held) }, options.EatExcessCrops))
            {
                isFull = true;
                return;
            }

            pot.heldObject.Value = null;
            harvestedToday = true;
        }
    }

    private static void HarvestFruitTrees(GameLocation location, Chest chest, HarvestOptions options, ref bool harvestedToday, ref bool isFull)
    {
        int minimumFruitBeforeHarvest = Math.Min(options.MinimumFruitBeforeHarvest, 3);
        foreach (KeyValuePair<Vector2, TerrainFeature> pair in location.terrainFeatures.Pairs.Where(pair => pair.Value is FruitTree).ToList())
        {
            FruitTree tree = (FruitTree)pair.Value;
            List<Item> fruit = tree.fruit.Where(item => item is not null).Select(CloneItem).ToList();
            if (fruit.Count < minimumFruitBeforeHarvest)
                continue;

            if (!TryAddItemsToChest(chest, fruit, options.EatExcessCrops))
            {
                isFull = true;
                return;
            }

            tree.fruit.Clear();
            harvestedToday = true;
        }
    }

    internal static bool TryAddItemsToChest(Chest chest, IEnumerable<Item> items, bool eatExcess)
    {
        List<Item> output = items.Where(item => item is not null && item.Stack > 0).Select(CloneItem).ToList();
        if (output.Count == 0)
            return true;

        if (eatExcess)
        {
            bool ateAny = false;
            foreach (Item item in output)
            {
                Item? remainder = chest.addItem(CloneItem(item));
                if (remainder is not null && remainder.Stack > 0)
                    ateAny = true;
            }

            if (ateAny)
                StorageMarker.MarkAteCrops(chest);
            return true;
        }

        Chest simulation = new(true, Vector2.Zero, chest.ItemId);
        foreach (Item existing in chest.Items.Where(item => item is not null))
        {
            if (simulation.addItem(CloneItem(existing)) is not null)
                return false;
        }

        foreach (Item item in output)
        {
            if (simulation.addItem(CloneItem(item)) is not null)
                return false;
        }

        List<Item> snapshot = chest.Items.Where(item => item is not null).Select(CloneItem).ToList();
        foreach (Item item in output)
        {
            if (chest.addItem(CloneItem(item)) is null)
                continue;

            RestoreItems(chest, snapshot);
            return false;
        }

        return true;
    }

    private static Item CloneItem(Item item)
    {
        Item clone = item.getOne();
        clone.Stack = item.Stack;
        return clone;
    }

    private static void RestoreItems(Chest chest, IEnumerable<Item> items)
    {
        chest.Items.Clear();
        foreach (Item item in items)
            chest.Items.Add(CloneItem(item));
    }

    private static bool IsFlowerCrop(Crop crop)
    {
        string? harvestId = crop.indexOfHarvest.Value;
        return !string.IsNullOrWhiteSpace(harvestId) && ItemFactory.IsFlower(harvestId);
    }

    private static void AttemptSowSeed(string seedId, HoeDirt hoeDirt, Vector2 tile, GameLocation location, Chest chest)
    {
        string qualifiedSeedId = ItemRegistry.QualifyItemId(seedId) ?? $"(O){seedId}";
        Item? seedItem = chest.Items.FirstOrDefault(item => item is not null
            && item.Category == SObject.SeedsCategory
            && (item.ItemId == seedId || item.QualifiedItemId == qualifiedSeedId));
        if (seedItem is null)
            return;

        hoeDirt.Location = location;
        hoeDirt.Tile = tile;

        Farmer farmer = Game1.MasterPlayer;
        GameLocation previousLocation = farmer.currentLocation;
        bool planted;
        try
        {
            farmer.currentLocation = location;
            planted = hoeDirt.plant(seedId, farmer, false);
        }
        finally
        {
            farmer.currentLocation = previousLocation;
        }

        if (!planted)
            return;

        seedItem.Stack--;
        if (seedItem.Stack <= 0)
            chest.Items.Remove(seedItem);
    }

    private void ShowFullMessage(GathererKind kind, string locationName)
    {
        if (kind == GathererKind.HarvestStatue)
            Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("message.junimos.full", new { location = locationName }));
        else
            Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("message.parrots.full"));
    }

    private void ShowAteMessage(GathererKind kind, string locationName)
    {
        if (kind == GathererKind.HarvestStatue)
            Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("message.junimos.ate", new { location = locationName }));
        else
            Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("message.parrots.ate"));
    }

    private void ShowHarvestedMessage(GathererKind kind, string locationName)
    {
        if (kind == GathererKind.HarvestStatue)
            Game1.addHUDMessage(new HUDMessage(ModEntry.Instance.Helper.Translation.Get("message.junimos.harvested", new { location = locationName }), 2));
        else
            Game1.addHUDMessage(new HUDMessage(ModEntry.Instance.Helper.Translation.Get("message.parrots.harvested"), 2));
    }

    internal static string SplitCamelCaseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return text;

        return System.Text.RegularExpressions.Regex.Replace(text, "(?<!^)([A-Z])", " $1");
    }

    private readonly record struct HarvestOptions(
        bool EnableHarvestMessage,
        bool EatExcessCrops,
        bool HarvestFromPots,
        bool HarvestFromFruitTrees,
        bool HarvestFlowers,
        bool SowSeedsAfterHarvest,
        int MinimumFruitBeforeHarvest);
}
