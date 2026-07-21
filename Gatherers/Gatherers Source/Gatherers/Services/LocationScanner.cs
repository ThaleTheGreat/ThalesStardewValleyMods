using StardewValley;
using StardewValley.Buildings;

namespace ThaleTheGreat.Gatherers.Services;

internal static class LocationScanner
{
    internal static IEnumerable<GameLocation> AllLocationsAndBuildingInteriors()
    {
        HashSet<GameLocation> seen = new();
        foreach (GameLocation location in Game1.locations)
        {
            foreach (GameLocation found in Traverse(location, seen))
                yield return found;
        }
    }

    private static IEnumerable<GameLocation> Traverse(GameLocation location, HashSet<GameLocation> seen)
    {
        if (!seen.Add(location))
            yield break;

        yield return location;

        foreach (Building building in location.buildings)
        {
            GameLocation? indoor = building.GetIndoors();
            if (indoor is null)
                continue;

            foreach (GameLocation found in Traverse(indoor, seen))
                yield return found;
        }
    }
}
