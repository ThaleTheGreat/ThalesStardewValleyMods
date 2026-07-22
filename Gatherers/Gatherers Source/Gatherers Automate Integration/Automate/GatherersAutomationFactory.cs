using Microsoft.Xna.Framework;
using Pathoschild.Stardew.Automate;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using ThaleTheGreat.GatherersAutomateIntegration.Integration;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.GatherersAutomateIntegration.Automate;

public sealed class GatherersAutomationFactory : IAutomationFactory
{
    private readonly IGatherersApi GatherersApi;

    public GatherersAutomationFactory(IGatherersApi gatherersApi)
    {
        GatherersApi = gatherersApi;
    }

    public IAutomatable? GetFor(SObject obj, GameLocation location, in Vector2 tile)
    {
        if (obj is Chest chest && GatherersApi.IsGathererStorage(chest))
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

        if (!GatherersApi.IsGathererStorage(chest) || !chest.HasContextTag("automate_storage"))
            return null;

        return new GatherersMachine(chest, location, tile);
    }
}
