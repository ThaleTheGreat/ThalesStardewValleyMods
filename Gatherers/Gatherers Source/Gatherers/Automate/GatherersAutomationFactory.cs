using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Automate;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using ThaleTheGreat.Gatherers.Services;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Automate;

public sealed class GatherersAutomationFactory : IAutomationFactory
{
    public IAutomatable? GetFor(SObject obj, GameLocation location, in Vector2 tile)
    {
        if (obj is Chest chest && StorageMarker.IsGathererStorage(chest))
            return new GatherersMachine(chest, location, tile);

        return null;
    }

    public IAutomatable? GetFor(TerrainFeature feature, GameLocation location, in Vector2 tile)
    {
        return null;
    }

    public IAutomatable? GetFor(Building building, GameLocation location, in Vector2 tile)
    {
        return null;
    }

    public IAutomatable? GetForTile(GameLocation location, in Vector2 tile)
    {
        if (!location.objects.TryGetValue(tile, out SObject? obj) || obj is not Chest chest)
            return null;

        if (!StorageMarker.IsGathererStorage(chest) || !chest.HasContextTag("automate_storage"))
            return null;

        return new GatherersMachine(chest, location, tile);
    }
}
