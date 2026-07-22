using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Services;

namespace ThaleTheGreat.Gatherers.Api;

public sealed class GatherersApi : IGatherersApi
{
    public bool IsGathererStorage(Chest chest)
    {
        return StorageMarker.IsGathererStorage(chest);
    }
}
