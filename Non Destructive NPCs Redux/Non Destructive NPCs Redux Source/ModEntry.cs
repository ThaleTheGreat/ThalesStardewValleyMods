using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.NonDestructiveNPCsRedux;

internal sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        Harmony harmony = new(this.ModManifest.UniqueID);
        PatchLocationDestroyMethods(harmony);
        PatchLocationCollisionMethods(harmony);
        PatchPassableMethods(harmony);
    }

    private static void PatchLocationDestroyMethods(Harmony harmony)
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(GameLocation)))
        {
            if (!method.Name.Contains("characterDestroyObjectWithinRectangle", StringComparison.OrdinalIgnoreCase))
                continue;

            string prefixName = method.ReturnType == typeof(bool)
                ? nameof(PreventCharacterDestroyBool)
                : nameof(PreventCharacterDestroyVoid);

            harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModEntry), prefixName));
        }
    }

    private static void PatchLocationCollisionMethods(Harmony harmony)
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(GameLocation)))
        {
            if (method.ReturnType != typeof(bool))
                continue;

            if (method.Name == nameof(GameLocation.isCollidingPosition))
            {
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ModEntry), nameof(AllowNpcThroughProtectedPlantingsByRectangle)));
                continue;
            }

            if (method.Name.StartsWith("isTileOccupied", StringComparison.OrdinalIgnoreCase))
                harmony.Patch(method, postfix: new HarmonyMethod(typeof(ModEntry), nameof(AllowNpcThroughProtectedPlantingsByTile)));
        }
    }

    private static void PatchPassableMethods(Harmony harmony)
    {
        foreach (Type type in new[] { typeof(SObject), typeof(Tree), typeof(FruitTree), typeof(Bush) })
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.ReturnType == typeof(bool) && method.Name.Equals("isPassable", StringComparison.OrdinalIgnoreCase))
                    harmony.Patch(method, postfix: new HarmonyMethod(typeof(ModEntry), nameof(AllowProtectedPlantingPassableForNpc)));
            }
        }
    }

    private static bool PreventCharacterDestroyBool(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool PreventCharacterDestroyVoid()
    {
        return false;
    }

    private static void AllowNpcThroughProtectedPlantingsByRectangle(GameLocation __instance, ref bool __result, object[] __args)
    {
        if (!__result || !ArgsAreForNpc(__args) || !TryGetRectangle(__args, out Rectangle rectangle))
            return;

        foreach (Vector2 tile in GetTiles(rectangle))
        {
            if (IsProtectedPlanting(__instance, tile))
            {
                __result = false;
                return;
            }
        }
    }

    private static void AllowNpcThroughProtectedPlantingsByTile(GameLocation __instance, ref bool __result, object[] __args)
    {
        if (!__result || !ArgsAreForNpc(__args) || !TryGetTile(__args, out Vector2 tile))
            return;

        if (IsProtectedPlanting(__instance, tile))
            __result = false;
    }

    private static void AllowProtectedPlantingPassableForNpc(object __instance, ref bool __result, object[] __args)
    {
        if (!ArgsAreForNpc(__args))
            return;

        if (__instance is Tree or FruitTree or Bush || __instance is SObject obj && IsTreeSeedOrSaplingObject(obj))
            __result = true;
    }

    private static IEnumerable<Vector2> GetTiles(Rectangle rectangle)
    {
        int left = Math.Max(0, rectangle.Left / Game1.tileSize);
        int right = Math.Max(left, (rectangle.Right - 1) / Game1.tileSize);
        int top = Math.Max(0, rectangle.Top / Game1.tileSize);
        int bottom = Math.Max(top, (rectangle.Bottom - 1) / Game1.tileSize);

        for (int x = left; x <= right; x++)
        {
            for (int y = top; y <= bottom; y++)
                yield return new Vector2(x, y);
        }
    }

    private static bool TryGetRectangle(object[] args, out Rectangle rectangle)
    {
        foreach (object? arg in args)
        {
            if (arg is Rectangle value)
            {
                rectangle = value;
                return true;
            }
        }

        rectangle = default;
        return false;
    }

    private static bool TryGetTile(object[] args, out Vector2 tile)
    {
        foreach (object? arg in args)
        {
            switch (arg)
            {
                case Vector2 value:
                    tile = value;
                    return true;
                case Point value:
                    tile = new Vector2(value.X, value.Y);
                    return true;
                case xTile.Dimensions.Location value:
                    tile = new Vector2(value.X, value.Y);
                    return true;
                case Rectangle value:
                    tile = new Vector2(value.X / Game1.tileSize, value.Y / Game1.tileSize);
                    return true;
            }
        }

        tile = default;
        return false;
    }

    private static bool ArgsAreForNpc(object[] args)
    {
        bool foundNpc = false;

        foreach (object? arg in args)
        {
            if (arg is Farmer)
                return false;

            if (arg is NPC)
                foundNpc = true;

            if (arg is Character character)
            {
                if (character is Farmer)
                    return false;

                if (character is NPC)
                    foundNpc = true;
            }
        }

        return foundNpc;
    }

    private static bool IsProtectedPlanting(GameLocation location, Vector2 tile)
    {
        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) && feature is Tree or FruitTree or Bush)
            return true;

        return location.objects.TryGetValue(tile, out SObject? obj) && IsTreeSeedOrSaplingObject(obj);
    }

    private static bool IsTreeSeedOrSaplingObject(SObject obj)
    {
        if (InvokeBool(obj, "isSapling") || InvokeBool(obj, "IsSapling"))
            return true;

        string itemId = obj.ItemId ?? string.Empty;
        string qualifiedItemId = obj.QualifiedItemId ?? string.Empty;
        string name = obj.Name ?? string.Empty;
        string displayName = obj.DisplayName ?? string.Empty;
        string search = string.Join('\n', itemId, qualifiedItemId, name, displayName);

        return itemId is "309" or "310" or "311" or "292" or "891"
            || search.Contains("Acorn", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Maple Seed", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Pine Cone", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Mahogany Seed", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Mushroom Tree Seed", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Mossy Seed", StringComparison.OrdinalIgnoreCase)
            || search.Contains("Sapling", StringComparison.OrdinalIgnoreCase);
    }

    private static bool InvokeBool(object instance, string name)
    {
        MethodInfo? method = instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, binder: null, types: Type.EmptyTypes, modifiers: null);
        return method is not null && method.Invoke(instance, null) is bool value && value;
    }
}
