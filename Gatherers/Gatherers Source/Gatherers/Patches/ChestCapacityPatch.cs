using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Services;

namespace ThaleTheGreat.Gatherers.Patches;

internal static class ChestCapacityPatch
{
    internal static void Postfix(Chest __instance, ref int __result)
    {
        if (StorageMarker.IsGathererStorage(__instance))
            __result = StorageMarker.GetCapacity(__instance);
    }
}
