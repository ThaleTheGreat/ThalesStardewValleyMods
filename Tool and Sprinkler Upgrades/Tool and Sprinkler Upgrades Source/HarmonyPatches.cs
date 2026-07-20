using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal static class CustomPanAnimationPatch
{
    private static readonly FieldInfo? PositionOffsetField = AccessTools.Field(typeof(FarmerRenderer), "positionOffset");

    public static void AfterFarmerRendererDraw(
        SpriteBatch b,
        FarmerSprite.AnimationFrame animationFrame,
        int currentFrame,
        Rectangle sourceRect,
        Vector2 position,
        Vector2 origin,
        float layerDepth,
        Color overrideColor,
        float rotation,
        float scale,
        Farmer who,
        FarmerRenderer __instance)
    {
        if (who?.UsingTool != true || who.CurrentTool is not Pan pan)
            return;

        if (!TryGetCustomPanSourceRect(currentFrame, out Rectangle panSourceRect))
            return;

        if (!ModEntry.TryGetCustomPanAnimationTexture(pan, out Texture2D? texture) || texture == null)
            return;

        Vector2 positionOffset = PositionOffsetField?.GetValue(__instance) is Vector2 offset ? offset : Vector2.Zero;
        Vector2 drawPosition = position + origin + positionOffset + who.armOffset;
        b.Draw(texture, drawPosition, panSourceRect, overrideColor, rotation, origin, 4f * scale, animationFrame.flip ? SpriteEffects.FlipHorizontally : SpriteEffects.None, layerDepth + 5.0E-05f);
    }

    private static bool TryGetCustomPanSourceRect(int currentFrame, out Rectangle panSourceRect)
    {
        panSourceRect = Rectangle.Empty;

        int frameOffset = currentFrame - Constants.PanAnimationFirstFrame;
        int lastFrameOffset = Constants.PanAnimationLastFrame - Constants.PanAnimationFirstFrame;
        if (frameOffset < 0 || frameOffset > lastFrameOffset)
            return false;

        panSourceRect = new Rectangle(
            Constants.PanAnimationIridiumSourceX + frameOffset * Constants.PanAnimationFrameWidth,
            Constants.PanAnimationSourceY,
            Constants.PanAnimationFrameWidth,
            Constants.PanAnimationFrameHeight
        );
        return true;
    }
}

[HarmonyPatch(typeof(Tool), "tilesAffected")]
internal static class ToolTilesAffectedPatch
{
    public static void Postfix(Tool __instance, List<Vector2> __result, Vector2 tileLocation, int power, Farmer who)
    {
        if (__instance is not Hoe and not WateringCan)
            return;

        if (__instance.UpgradeLevel is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
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

        if (tool.UpgradeLevel is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
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
internal static class HighestTierAxeTreeOneHitPatch
{
    public static void Prefix(Tree __instance, Tool t, int explosion)
    {
        if (explosion > 0 || t is not Axe axe || axe.UpgradeLevel != Constants.HighestCustomLevel)
            return;

        if (__instance.tapped.Value)
            return;

        __instance.health.Value = 0f;
    }
}

[HarmonyPatch(typeof(FruitTree), nameof(FruitTree.performToolAction))]
internal static class HighestTierAxeFruitTreeOneHitPatch
{
    public static void Prefix(FruitTree __instance, Tool t, int explosion)
    {
        if (explosion > 0 || t is not Axe axe || axe.UpgradeLevel != Constants.HighestCustomLevel)
            return;

        __instance.health.Value = 0f;
    }
}

[HarmonyPatch(typeof(ResourceClump), nameof(ResourceClump.performToolAction))]
internal static class HighestTierToolResourceClumpOneHitPatch
{
    public static void Prefix(ResourceClump __instance, Tool t)
    {
        if (t is Axe axe && axe.UpgradeLevel == Constants.HighestCustomLevel)
        {
            __instance.health.Value = 0;
            return;
        }

        if (t is Pickaxe pickaxe && pickaxe.UpgradeLevel == Constants.HighestCustomLevel)
            __instance.health.Value = 0;
    }
}

[HarmonyPatch(typeof(Pickaxe), nameof(Pickaxe.DoFunction))]
internal static class HighestTierPickaxeOneHitPatch
{
    public static void Prefix(Pickaxe __instance, GameLocation location, int x, int y)
    {
        if (__instance.UpgradeLevel != Constants.HighestCustomLevel)
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


[HarmonyPatch(typeof(Farmer), "get_FishingLevel")]
internal static class FishingRodLevelBonusPatch
{
    public static void Postfix(Farmer __instance, ref int __result)
    {
        if (__instance.CurrentTool is not FishingRod rod)
            return;

        int bonus = ToolTierUtility.GetFishingRodLevelBonus(rod.UpgradeLevel);
        if (bonus > 0)
            __result += bonus;
    }
}


[HarmonyPatch(typeof(SObject), nameof(SObject.IsScarecrow))]
internal static class HighestTierSprinklerScarecrowRecognitionPatch
{
    public static void Postfix(SObject __instance, ref bool __result)
    {
        if (!__result && ModEntry.IsHighestTierSprinklerScarecrow(__instance))
            __result = true;
    }
}

[HarmonyPatch(typeof(SObject), nameof(SObject.GetRadiusForScarecrow))]
internal static class HighestTierSprinklerScarecrowRadiusPatch
{
    public static void Postfix(SObject __instance, ref int __result)
    {
        if (ModEntry.IsHighestTierSprinklerScarecrow(__instance))
            __result = ModEntry.GetSprinklerRange(__instance);
    }
}


[HarmonyPatch(typeof(SObject), nameof(SObject.GetBaseRadiusForSprinkler))]
internal static class CustomSprinklerBaseRadiusPatch
{
    public static void Postfix(SObject __instance, ref int __result)
    {
        int range = ModEntry.GetBaseSprinklerRange(__instance.ItemId);
        if (range > 0)
            __result = range;
    }
}

[HarmonyPatch(typeof(SObject), nameof(SObject.GetModifiedRadiusForSprinkler))]
[HarmonyAfter(Constants.NozzleAndEnricherModId)]
internal static class CustomSprinklerModifiedRadiusPatch
{
    public static void Postfix(SObject __instance, ref int __result)
    {
        int range = ModEntry.GetSprinklerRange(__instance);
        if (range > 0)
            __result = range;
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
internal static class ImmersiveSprinklerRadiusCompatibilityPatch
{
    public static void AfterGetSprinklerRadius(SObject __0, ref int __result)
    {
        if (!ModEntry.IsCustomSprinkler(__0.ItemId))
            return;

        int range = ModEntry.GetSprinklerRange(__0);
        if (range > 0)
            __result = range;
    }
}
