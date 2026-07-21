using StardewValley.Objects;
using ThaleTheGreat.Gatherers.Framework;

namespace ThaleTheGreat.Gatherers.Services;

public static class StorageMarker
{
    public static bool IsGathererStorage(Chest chest)
    {
        return IsHarvestStatue(chest) || IsParrotPot(chest);
    }

    public static bool IsHarvestStatue(Chest chest)
    {
        return chest.modData.ContainsKey(ModConstants.HarvestStatueFlag)
            || chest.modData.ContainsKey(ModConstants.LegacyHarvestStatueFlag)
            || chest.modData.ContainsKey(ModConstants.LegacyOldHarvestStatueFlag);
    }

    public static bool IsParrotPot(Chest chest)
    {
        return chest.modData.ContainsKey(ModConstants.ParrotPotFlag)
            || chest.modData.ContainsKey(ModConstants.LegacyParrotPotFlag)
            || chest.modData.ContainsKey(ModConstants.LegacyOldParrotPotFlag);
    }

    internal static void Mark(Chest chest, GathererKind kind)
    {
        EnsureAutomateMachineOnly(chest);
        chest.modData[ModConstants.AteCropsFlag] = bool.FalseString;
        chest.modData[ModConstants.HasSpawnedFlag] = bool.FalseString;
        chest.modData[ModConstants.HarvestedTodayFlag] = bool.FalseString;
        chest.modData[ModConstants.HarvestedTilesFlag] = string.Empty;

        if (kind == GathererKind.HarvestStatue)
        {
            chest.modData[ModConstants.HarvestStatueFlag] = bool.TrueString;
            chest.modData[ModConstants.LegacyHarvestStatueFlag] = bool.TrueString;
            chest.modData[ModConstants.LegacyHarvestStatueAteFlag] = bool.FalseString;
            chest.modData[ModConstants.LegacyHarvestStatueSpawnedFlag] = bool.FalseString;
        }
        else
        {
            chest.modData[ModConstants.ParrotPotFlag] = bool.TrueString;
            chest.modData[ModConstants.LegacyParrotPotFlag] = bool.TrueString;
            chest.modData[ModConstants.LegacyParrotPotAteFlag] = bool.FalseString;
            chest.modData[ModConstants.LegacyParrotPotSpawnedFlag] = bool.FalseString;
        }
    }

    internal static void EnsureAutomateMachineOnly(Chest chest)
    {
        chest.modData[ModConstants.AutomateStoreItemsFlag] = "Disabled";
        chest.modData[ModConstants.AutomateTakeItemsFlag] = "Disabled";
    }

    internal static void ResetDailyFlags(Chest chest)
    {
        chest.modData[ModConstants.AteCropsFlag] = bool.FalseString;
        chest.modData[ModConstants.HasSpawnedFlag] = bool.FalseString;
        chest.modData[ModConstants.HarvestedTodayFlag] = bool.FalseString;
        chest.modData[ModConstants.HarvestedTilesFlag] = string.Empty;

        if (IsHarvestStatue(chest))
        {
            chest.modData[ModConstants.LegacyHarvestStatueAteFlag] = bool.FalseString;
            chest.modData[ModConstants.LegacyHarvestStatueSpawnedFlag] = bool.FalseString;
        }

        if (IsParrotPot(chest))
        {
            chest.modData[ModConstants.LegacyParrotPotAteFlag] = bool.FalseString;
            chest.modData[ModConstants.LegacyParrotPotSpawnedFlag] = bool.FalseString;
        }
    }

    internal static void MarkAteCrops(Chest chest)
    {
        chest.modData[ModConstants.AteCropsFlag] = bool.TrueString;
        if (IsHarvestStatue(chest))
            chest.modData[ModConstants.LegacyHarvestStatueAteFlag] = bool.TrueString;
        if (IsParrotPot(chest))
            chest.modData[ModConstants.LegacyParrotPotAteFlag] = bool.TrueString;
    }

    internal static bool AteCrops(Chest chest)
    {
        return IsTrue(chest.modData.TryGetValue(ModConstants.AteCropsFlag, out string? ate) ? ate : null)
            || IsTrue(chest.modData.TryGetValue(ModConstants.LegacyHarvestStatueAteFlag, out string? oldHarvest) ? oldHarvest : null)
            || IsTrue(chest.modData.TryGetValue(ModConstants.LegacyParrotPotAteFlag, out string? oldParrot) ? oldParrot : null);
    }

    internal static void MarkHarvestedTiles(Chest chest, IEnumerable<Microsoft.Xna.Framework.Vector2> tiles)
    {
        string value = string.Join(";", tiles.Select(tile => $"{(int)tile.X},{(int)tile.Y}"));
        chest.modData[ModConstants.HarvestedTodayFlag] = bool.TrueString;
        chest.modData[ModConstants.HarvestedTilesFlag] = value;
    }

    internal static List<Microsoft.Xna.Framework.Vector2> GetHarvestedTiles(Chest chest)
    {
        if (!chest.modData.TryGetValue(ModConstants.HarvestedTilesFlag, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return new List<Microsoft.Xna.Framework.Vector2>();

        List<Microsoft.Xna.Framework.Vector2> tiles = new();
        foreach (string piece in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string[] split = piece.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (split.Length == 2 && int.TryParse(split[0], out int x) && int.TryParse(split[1], out int y))
                tiles.Add(new Microsoft.Xna.Framework.Vector2(x, y));
        }
        return tiles;
    }

    internal static bool HasSpawned(Chest chest)
    {
        return IsTrue(chest.modData.TryGetValue(ModConstants.HasSpawnedFlag, out string? value) ? value : null)
            || IsTrue(chest.modData.TryGetValue(ModConstants.LegacyHarvestStatueSpawnedFlag, out string? harvest) ? harvest : null)
            || IsTrue(chest.modData.TryGetValue(ModConstants.LegacyParrotPotSpawnedFlag, out string? parrot) ? parrot : null);
    }

    internal static void MarkSpawned(Chest chest)
    {
        chest.modData[ModConstants.HasSpawnedFlag] = bool.TrueString;
        if (IsHarvestStatue(chest))
            chest.modData[ModConstants.LegacyHarvestStatueSpawnedFlag] = bool.TrueString;
        if (IsParrotPot(chest))
            chest.modData[ModConstants.LegacyParrotPotSpawnedFlag] = bool.TrueString;
    }

    private static bool IsTrue(string? value)
    {
        return bool.TryParse(value, out bool parsed) && parsed;
    }
}
