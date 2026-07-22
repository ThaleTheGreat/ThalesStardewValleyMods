using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Framework;
using ThaleTheGreat.Gatherers.Services;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Patches;

internal static class PlacementPatch
{
    internal static bool Prefix(SObject __instance, GameLocation location, int x, int y, Farmer? who, ref bool __result, out GathererKind? __state)
    {
        __state = null;

        if (__instance.QualifiedItemId == ModConstants.HarvestStatueQualifiedId)
        {
            __state = GathererKind.HarvestStatue;
            if (location.IsOutdoors)
            {
                Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("placement.harvest-statue.only-indoors"));
                __result = false;
                return false;
            }

            if (location.objects.Values.OfType<Chest>().Any(StorageMarker.IsHarvestStatue))
            {
                Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("placement.harvest-statue.one-per-location"));
                __result = false;
                return false;
            }
        }
        else if (__instance.QualifiedItemId == ModConstants.ParrotPotQualifiedId)
        {
            __state = GathererKind.ParrotPot;
            if (!IsIslandWest(location))
            {
                Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("placement.parrot-pot.only-island"));
                __result = false;
                return false;
            }

            if (location.objects.Values.OfType<Chest>().Any(StorageMarker.IsParrotPot))
            {
                Game1.showRedMessage(ModEntry.Instance.Helper.Translation.Get("placement.parrot-pot.one-only"));
                __result = false;
                return false;
            }
        }

        return true;
    }

    internal static void Postfix(GameLocation location, int x, int y, bool __result, GathererKind? __state)
    {
        if (!__result || __state is null)
            return;

        Vector2 tile = new(x / 64, y / 64);
        if (location.objects.TryGetValue(tile, out SObject? placedObject) && placedObject is Chest placedChest)
        {
            placedChest.Location = location;
            placedChest.TileLocation = tile;
            StorageMarker.Mark(placedChest, __state.Value);
            return;
        }

        location.objects.Remove(tile);
        string itemId = __state.Value == GathererKind.HarvestStatue
            ? ModConstants.HarvestStatueItemId
            : ModConstants.ParrotPotItemId;

        Chest chest = new(true, tile, itemId)
        {
            Location = location,
            TileLocation = tile
        };
        StorageMarker.Mark(chest, __state.Value);
        location.objects[tile] = chest;
    }

    private static bool IsIslandWest(GameLocation location)
    {
        return string.Equals(location.NameOrUniqueName, "IslandWest", StringComparison.OrdinalIgnoreCase)
            || string.Equals(location.Name, "IslandWest", StringComparison.OrdinalIgnoreCase);
    }
}
