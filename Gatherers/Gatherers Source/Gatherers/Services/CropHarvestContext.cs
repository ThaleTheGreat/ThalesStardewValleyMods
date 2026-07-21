using StardewValley.Objects;

namespace ThaleTheGreat.Gatherers.Services;

internal static class CropHarvestContext
{
    [ThreadStatic]
    private static Chest? CurrentChest;

    [ThreadStatic]
    private static bool EatExcess;

    [ThreadStatic]
    private static bool Active;

    [ThreadStatic]
    private static bool HarvestSucceeded;

    [ThreadStatic]
    private static bool StorageBlocked;

    internal static bool WasHarvestSuccessful => HarvestSucceeded;
    internal static bool WasStorageBlocked => StorageBlocked;

    internal static bool TryGet(out Chest chest, out bool eatExcess)
    {
        chest = CurrentChest!;
        eatExcess = EatExcess;
        return Active && CurrentChest is not null;
    }

    internal static IDisposable Enter(Chest chest, bool eatExcess)
    {
        CurrentChest = chest;
        EatExcess = eatExcess;
        Active = true;
        ResetHarvestResult();
        return new Scope();
    }

    internal static void ResetHarvestResult()
    {
        HarvestSucceeded = false;
        StorageBlocked = false;
    }

    internal static void MarkHarvestSucceeded()
    {
        HarvestSucceeded = true;
    }

    internal static void MarkStorageBlocked()
    {
        StorageBlocked = true;
    }

    private sealed class Scope : IDisposable
    {
        public void Dispose()
        {
            CurrentChest = null;
            EatExcess = false;
            Active = false;
            HarvestSucceeded = false;
            StorageBlocked = false;
        }
    }
}
