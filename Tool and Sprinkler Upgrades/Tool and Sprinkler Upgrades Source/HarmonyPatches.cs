using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

[HarmonyPatch(typeof(Tool), "tilesAffected")]
internal static class ToolTilesAffectedPatch
{
    public static void Postfix(Tool __instance, List<Vector2> __result, Vector2 tileLocation, int power, Farmer who)
    {
        if (__instance is not Hoe and not WateringCan)
            return;

        if (__instance.UpgradeLevel is < Constants.CobaltLevel or > Constants.RadioactiveLevel)
            return;

        if (power < 6)
            return;

        ToolTierUtility.GetAreaShape(__instance.UpgradeLevel, out int length, out int width);
        if (length <= 0)
            return;

        __result.Clear();
        ToolTierUtility.AddRectangleTiles(__result, tileLocation, who, length, width);
    }
}

internal static class ToolStaminaPatch
{
    public static void BeforeUseAxe(Axe __instance, Farmer who, ref float __state)
    {
        BeforeUseCore(__instance, who, ref __state);
    }

    public static void AfterUseAxe(Axe __instance, Farmer who, float __state)
    {
        AfterUseCore(__instance, who, __state);
    }

    public static void BeforeUsePickaxe(Pickaxe __instance, Farmer who, ref float __state)
    {
        BeforeUseCore(__instance, who, ref __state);
    }

    public static void AfterUsePickaxe(Pickaxe __instance, Farmer who, float __state)
    {
        AfterUseCore(__instance, who, __state);
    }

    public static void BeforeUseHoe(Hoe __instance, Farmer who, ref float __state)
    {
        BeforeUseCore(__instance, who, ref __state);
    }

    public static void AfterUseHoe(Hoe __instance, Farmer who, float __state)
    {
        AfterUseCore(__instance, who, __state);
    }

    public static void BeforeUseWateringCan(WateringCan __instance, Farmer who, ref float __state)
    {
        BeforeUseCore(__instance, who, ref __state);
    }

    public static void AfterUseWateringCan(WateringCan __instance, Farmer who, float __state)
    {
        AfterUseCore(__instance, who, __state);
    }

    private static void BeforeUseCore(Tool tool, Farmer who, ref float state)
    {
        state = -1f;

        if (!ToolTierUtility.IsCoreUpgradeableTool(tool))
            return;

        if (tool.UpgradeLevel is < Constants.CobaltLevel or > Constants.RadioactiveLevel)
            return;

        state = who.Stamina;
    }

    private static void AfterUseCore(Tool tool, Farmer who, float state)
    {
        if (state < 0f)
            return;

        if (!ToolTierUtility.IsCoreUpgradeableTool(tool))
            return;

        float refundRatio = ToolTierUtility.GetStaminaRefundRatio(tool.UpgradeLevel);
        if (refundRatio <= 0f)
            return;

        float spent = Math.Max(0f, state - who.Stamina);
        if (spent <= 0f)
            return;

        who.Stamina = Math.Min(who.MaxStamina, who.Stamina + spent * refundRatio);
    }
}


[HarmonyPatch(typeof(Tree), nameof(Tree.performToolAction))]
internal static class RadioactiveAxeTreeOneHitPatch
{
    public static void Prefix(Tree __instance, Tool t, int explosion)
    {
        if (explosion > 0 || t is not Axe axe || axe.UpgradeLevel != Constants.RadioactiveLevel)
            return;

        if (__instance.tapped.Value)
            return;

        __instance.health.Value = 0f;
    }
}

[HarmonyPatch(typeof(FruitTree), nameof(FruitTree.performToolAction))]
internal static class RadioactiveAxeFruitTreeOneHitPatch
{
    public static void Prefix(FruitTree __instance, Tool t, int explosion)
    {
        if (explosion > 0 || t is not Axe axe || axe.UpgradeLevel != Constants.RadioactiveLevel)
            return;

        __instance.health.Value = 0f;
    }
}

[HarmonyPatch(typeof(ResourceClump), nameof(ResourceClump.performToolAction))]
internal static class RadioactiveToolResourceClumpOneHitPatch
{
    public static void Prefix(ResourceClump __instance, Tool t)
    {
        if (t is Axe axe && axe.UpgradeLevel == Constants.RadioactiveLevel)
        {
            __instance.health.Value = 0;
            return;
        }

        if (t is Pickaxe pickaxe && pickaxe.UpgradeLevel == Constants.RadioactiveLevel)
            __instance.health.Value = 0;
    }
}

[HarmonyPatch(typeof(Pickaxe), nameof(Pickaxe.DoFunction))]
internal static class RadioactivePickaxeOneHitPatch
{
    public static void Prefix(Pickaxe __instance, GameLocation location, int x, int y)
    {
        if (__instance.UpgradeLevel != Constants.RadioactiveLevel)
            return;

        Vector2 tile = new(x / Game1.tileSize, y / Game1.tileSize);
        if (TryPrepareObjectForOneHit(location, tile))
            return;

        TryPrepareObjectForOneHit(location, new Vector2((x - 8) / Game1.tileSize, y / Game1.tileSize));
        TryPrepareObjectForOneHit(location, new Vector2((x + 8) / Game1.tileSize, y / Game1.tileSize));
        TryPrepareObjectForOneHit(location, new Vector2(x / Game1.tileSize, (y - 8) / Game1.tileSize));
        TryPrepareObjectForOneHit(location, new Vector2(x / Game1.tileSize, (y + 8) / Game1.tileSize));
    }

    private static bool TryPrepareObjectForOneHit(GameLocation location, Vector2 tile)
    {
        if (!location.Objects.TryGetValue(tile, out SObject obj))
            return false;

        if (!IsMineObject(obj))
            return false;

        obj.MinutesUntilReady = 0;
        return true;
    }

    private static bool IsMineObject(SObject obj)
    {
        string name = obj.Name ?? string.Empty;
        if (name.Contains("Stone", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Node", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Ore", StringComparison.OrdinalIgnoreCase))
            return true;
        if (name.Contains("Boulder", StringComparison.OrdinalIgnoreCase))
            return true;

        return obj.MinutesUntilReady > 0 && obj.Category is -12 or -2;
    }
}


internal static class CustomSprinklerRecognitionPatch
{
    public static void AfterIsSprinkler(SObject __instance, ref bool __result)
    {
        if (__result)
            return;

        __result = ModEntry.IsCustomSprinkler(__instance.ItemId);
    }
}
