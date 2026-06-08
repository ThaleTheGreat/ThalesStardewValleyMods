using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.ClearBuildSites;

internal sealed class ModEntry : Mod
{
    internal static ModEntry? Instance { get; private set; }

    internal ModConfig Config { get; private set; } = new();

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();

        Harmony harmony = new(ModManifest.UniqueID);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        PatchBuildStructureMethods(harmony);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void PatchBuildStructureMethods(Harmony harmony)
    {
        MethodInfo prefix = AccessTools.Method(typeof(BuildStructurePatch), nameof(BuildStructurePatch.Prefix));
        int patched = 0;

        foreach (MethodBase method in FindBuildStructureMethods())
        {
            harmony.Patch(method, prefix: new HarmonyMethod(prefix));
            patched++;
        }

        if (patched == 0)
            Monitor.Log("Could not find any Stardew buildStructure methods to patch. Clear Build Sites will only affect the placement preview.", LogLevel.Warn);
    }

    private static IEnumerable<MethodBase> FindBuildStructureMethods()
    {
        Assembly gameAssembly = typeof(GameLocation).Assembly;
        foreach (Type type in gameAssembly.GetTypes())
        {
            if (!typeof(GameLocation).IsAssignableFrom(type))
                continue;

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (method.Name != "buildStructure")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Any(parameter => parameter.ParameterType == typeof(Vector2)))
                    yield return method;
            }
        }
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(ModManifest, () => Config = new ModConfig(), () => Helper.WriteConfig(Config));

        gmcm.AddBoolOption(ModManifest, () => Config.Enabled, value => Config.Enabled = value, () => "Enable Mod");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearStones, value => Config.ClearStones = value, () => "Clear Stones");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearTwigs, value => Config.ClearTwigs = value, () => "Clear Twigs");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearWeeds, value => Config.ClearWeeds = value, () => "Clear Weeds");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearGrass, value => Config.ClearGrass = value, () => "Clear Grass");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearTreeSeedsAndSaplings, value => Config.ClearTreeSeedsAndSaplings = value, () => "Clear Tree Seeds and Saplings");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearWildTrees, value => Config.ClearWildTrees = value, () => "Clear Wild Trees");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearResourceClumps, value => Config.ClearResourceClumps = value, () => "Clear Stumps, Logs, and Boulders");
        gmcm.AddBoolOption(ModManifest, () => Config.ClearFlooringAndPaths, value => Config.ClearFlooringAndPaths = value, () => "Clear Flooring and Paths");
    }

    internal void OnIsBuildable(GameLocation location, Vector2 tileLocation, ref bool result)
    {
        if (result || !Config.Enabled || !Context.IsWorldReady || !IsActiveConstructionPlacementMenu())
            return;

        if (location.isWaterTile((int)tileLocation.X, (int)tileLocation.Y))
            return;

        TileBlockerState state = GetTileBlockerState(location, tileLocation);
        if (state == TileBlockerState.OnlySafeClearable)
            result = true;
    }

    internal void BeforeBuildStructure(GameLocation location, object[] args)
    {
        if (!Config.Enabled || !Context.IsWorldReady || !IsActiveConstructionPlacementMenu())
            return;

        if (args.Any(arg => arg is Building))
            return;

        if (!TryGetBuildTile(args, out Vector2 tileLocation))
            return;

        object? blueprint = args.FirstOrDefault(IsBlueprintLike);
        if (blueprint is null || !TryGetBlueprintSize(blueprint, out int width, out int height))
        {
            return;
        }

        Rectangle footprint = new((int)tileLocation.X, (int)tileLocation.Y, width, height);
        if (!CanClearFootprint(location, footprint))
        {
            return;
        }

        ClearSafeBlockers(location, footprint);
    }

    private static bool IsActiveConstructionPlacementMenu()
    {
        if (Game1.activeClickableMenu is not CarpenterMenu menu)
            return false;

        if (!menu.onFarm || menu.freeze || Game1.IsFading())
            return false;

        return HasCurrentBlueprint(menu);
    }

    private static bool HasCurrentBlueprint(CarpenterMenu menu)
    {
        object? blueprint = GetCurrentBlueprint(menu);
        return blueprint is not null && IsBlueprintLike(blueprint);
    }

    private static object? GetCurrentBlueprint(CarpenterMenu menu)
    {
        return GetMemberValue(menu.GetType(), menu, "Blueprint")
            ?? GetMemberValue(menu.GetType(), menu, "CurrentBlueprint");
    }

    private static bool IsBlueprintLike(object? value)
    {
        if (value is null || value is Building || value is Vector2 || value is Farmer || value is bool)
            return false;

        Type type = value.GetType();
        if (type.Name.Contains("Blueprint", StringComparison.OrdinalIgnoreCase))
            return true;

        return GetMemberValue(type, value, "Data") is not null
            || GetMemberValue(type, value, "Size") is not null
            || GetMemberValue(type, value, "tilesWidth") is not null
            || GetMemberValue(type, value, "tilesHeight") is not null;
    }

    private static bool TryGetBuildTile(object[] args, out Vector2 tile)
    {
        foreach (object arg in args)
        {
            if (arg is Vector2 vector)
            {
                tile = vector;
                return true;
            }
        }

        tile = Vector2.Zero;
        return false;
    }

    private static bool TryGetBlueprintSize(object blueprint, out int width, out int height)
    {
        width = ReadIntMember(blueprint, "tilesWidth", "TilesWidth", "width", "Width");
        height = ReadIntMember(blueprint, "tilesHeight", "TilesHeight", "height", "Height");
        if (width > 0 && height > 0)
            return true;

        object? data = GetMemberValue(blueprint.GetType(), blueprint, "Data")
            ?? GetMemberValue(blueprint.GetType(), blueprint, "data");
        object? size = data is null
            ? GetMemberValue(blueprint.GetType(), blueprint, "Size") ?? GetMemberValue(blueprint.GetType(), blueprint, "size")
            : GetMemberValue(data.GetType(), data, "Size") ?? GetMemberValue(data.GetType(), data, "size");

        if (TryReadPoint(size, out Point point) && point.X > 0 && point.Y > 0)
        {
            width = point.X;
            height = point.Y;
            return true;
        }

        if (TryReadVector2(size, out Vector2 vector) && vector.X > 0 && vector.Y > 0)
        {
            width = (int)vector.X;
            height = (int)vector.Y;
            return true;
        }

        object? source = size ?? data ?? blueprint;
        width = ReadIntMember(source, "X", "x", "Width", "width");
        height = ReadIntMember(source, "Y", "y", "Height", "height");
        return width > 0 && height > 0;
    }

    private bool CanClearFootprint(GameLocation location, Rectangle footprint)
    {
        foreach (Building building in GetEnumerableMember<Building>(location, "buildings", "Buildings"))
        {
            if (GetBuildingFootprint(building).Intersects(footprint))
                return false;
        }

        foreach (Vector2 tile in TilesIn(footprint))
        {
            TileBlockerState state = GetTileBlockerState(location, tile);
            if (state == TileBlockerState.Unsafe)
                return false;
        }

        return true;
    }

    private TileBlockerState GetTileBlockerState(GameLocation location, Vector2 tile)
    {
        bool hasSafeClearable = false;

        if (location.objects.TryGetValue(tile, out SObject obj))
        {
            if (!IsSafeObject(obj))
                return TileBlockerState.Unsafe;

            hasSafeClearable = true;
        }

        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
        {
            if (!IsSafeTerrainFeature(feature))
                return TileBlockerState.Unsafe;

            hasSafeClearable = true;
        }

        foreach (ResourceClump clump in GetResourceClumps(location))
        {
            Rectangle clumpFootprint = GetResourceClumpFootprint(clump);
            if (!clumpFootprint.Contains((int)tile.X, (int)tile.Y))
                continue;

            if (!Config.ClearResourceClumps)
                return TileBlockerState.Unsafe;

            hasSafeClearable = true;
        }

        if (HasCharacterAt(location, tile))
            return TileBlockerState.Unsafe;

        return hasSafeClearable ? TileBlockerState.OnlySafeClearable : TileBlockerState.None;
    }

    private int ClearSafeBlockers(GameLocation location, Rectangle footprint)
    {
        int removed = 0;

        foreach (Vector2 tile in TilesIn(footprint).ToArray())
        {
            if (location.objects.TryGetValue(tile, out SObject obj) && IsSafeObject(obj))
            {
                InvokeRemoveAction(obj, tile, location);
                location.objects.Remove(tile);
                removed++;
            }

            if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) && IsSafeTerrainFeature(feature))
            {
                InvokeRemoveAction(feature, tile, location);
                location.terrainFeatures.Remove(tile);
                removed++;
            }
        }

        foreach (ResourceClump clump in GetResourceClumps(location).ToArray())
        {
            Rectangle clumpFootprint = GetResourceClumpFootprint(clump);
            if (Config.ClearResourceClumps && clumpFootprint.Width > 0 && clumpFootprint.Height > 0 && clumpFootprint.Intersects(footprint))
            {
                InvokeRemoveAction(clump, new Vector2(clumpFootprint.X, clumpFootprint.Y), location);
                if (RemoveResourceClump(location, clump))
                    removed++;
            }
        }

        return removed;
    }

    private bool IsSafeObject(SObject obj)
    {
        if (obj.bigCraftable.Value)
            return false;

        string name = (obj.Name ?? string.Empty).ToLowerInvariant();
        string displayName = (obj.DisplayName ?? string.Empty).ToLowerInvariant();
        string itemId = (obj.QualifiedItemId ?? obj.ItemId ?? string.Empty).ToLowerInvariant();
        string haystack = $"{name} {displayName} {itemId}";

        if (Config.ClearStones && haystack.Contains("stone"))
            return true;

        if (Config.ClearTwigs && (haystack.Contains("twig") || haystack.Contains("branch")))
            return true;

        if (Config.ClearWeeds && haystack.Contains("weed"))
            return true;

        return false;
    }

    private bool IsSafeTerrainFeature(TerrainFeature feature)
    {
        return feature switch
        {
            Grass => Config.ClearGrass,
            Tree tree => IsSafeTree(tree),
            Flooring => Config.ClearFlooringAndPaths,
            HoeDirt => false,
            FruitTree => false,
            _ => false
        };
    }

    private bool IsSafeTree(Tree tree)
    {
        int growthStage = tree.growthStage.Value;
        return growthStage < 5 ? Config.ClearTreeSeedsAndSaplings : Config.ClearWildTrees;
    }

    private static bool HasCharacterAt(GameLocation location, Vector2 tile)
    {
        foreach (NPC character in location.characters)
        {
            if (character.TilePoint.X == (int)tile.X && character.TilePoint.Y == (int)tile.Y)
                return true;
        }

        foreach (Farmer farmer in location.farmers)
        {
            if (farmer.TilePoint.X == (int)tile.X && farmer.TilePoint.Y == (int)tile.Y)
                return true;
        }

        return false;
    }

    private static Rectangle GetBuildingFootprint(Building building)
    {
        return new Rectangle(building.tileX.Value, building.tileY.Value, building.tilesWide.Value, building.tilesHigh.Value);
    }

    private static IEnumerable<ResourceClump> GetResourceClumps(GameLocation location)
    {
        return GetEnumerableMember<ResourceClump>(location, "resourceClumps", "ResourceClumps");
    }

    private static bool RemoveResourceClump(GameLocation location, ResourceClump clump)
    {
        object? collection = GetMemberValue(location.GetType(), location, "resourceClumps")
            ?? GetMemberValue(location.GetType(), location, "ResourceClumps");
        if (collection is null)
            return false;

        MethodInfo? remove = collection.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(method => method.Name == "Remove" && method.GetParameters().Length == 1);
        if (remove is not null)
            return remove.Invoke(collection, new object[] { clump }) is bool removed && removed;

        if (collection is IList list)
        {
            int index = list.IndexOf(clump);
            if (index >= 0)
            {
                list.RemoveAt(index);
                return true;
            }
        }

        return false;
    }

    private static Rectangle GetResourceClumpFootprint(ResourceClump clump)
    {
        if (TryReadVector2Member(clump, out Vector2 tile, "tile", "Tile")
            && ReadIntMember(clump, "width", "Width") is int width && width > 0
            && ReadIntMember(clump, "height", "Height") is int height && height > 0)
        {
            return new Rectangle((int)tile.X, (int)tile.Y, width, height);
        }

        if (TryGetRectangleFromMethod(clump, out Rectangle rectangle))
            return rectangle;

        return Rectangle.Empty;
    }

    private static bool TryGetRectangleFromMethod(ResourceClump clump, out Rectangle rectangle)
    {
        rectangle = Rectangle.Empty;
        string[] methodNames = { "getBoundingBox", "GetBoundingBox" };

        foreach (string name in methodNames)
        {
            MethodInfo? method = clump.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (method?.Invoke(clump, null) is Rectangle pixels)
            {
                rectangle = new Rectangle(
                    pixels.X / 64,
                    pixels.Y / 64,
                    Math.Max(1, (int)Math.Ceiling(pixels.Width / 64f)),
                    Math.Max(1, (int)Math.Ceiling(pixels.Height / 64f)));
                return true;
            }
        }

        return false;
    }

    private static void InvokeRemoveAction(object instance, Vector2 tile, GameLocation location)
    {
        MethodInfo? method = instance.GetType().GetMethod("performRemoveAction", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Vector2), typeof(GameLocation) }, null);
        method?.Invoke(instance, new object[] { tile, location });
    }

    private static IEnumerable<T> GetEnumerableMember<T>(object instance, params string[] names)
    {
        foreach (string name in names)
        {
            object? value = GetMemberValue(instance.GetType(), instance, name);
            if (value is IEnumerable enumerable)
            {
                foreach (object? item in enumerable)
                {
                    if (item is T typed)
                        yield return typed;
                }

                yield break;
            }
        }
    }

    private static object? GetMemberValue(Type type, object instance, string name)
    {
        for (Type? current = type; current is not null; current = current.BaseType)
        {
            FieldInfo? field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
                return field.GetValue(instance);

            PropertyInfo? property = current.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance);
        }

        return null;
    }

    private static int ReadIntMember(object? instance, params string[] names)
    {
        if (instance is null)
            return 0;

        Type type = instance.GetType();
        foreach (string name in names)
        {
            object? value = GetMemberValue(type, instance, name);
            if (TryConvertToInt(value, out int result))
                return result;
        }

        return 0;
    }

    private static bool TryConvertToInt(object? value, out int result)
    {
        result = 0;
        if (value is null)
            return false;

        if (value is int direct)
        {
            result = direct;
            return true;
        }

        object? wrapped = GetWrappedValue(value);
        if (wrapped is int wrappedInt)
        {
            result = wrappedInt;
            return true;
        }

        return int.TryParse(value.ToString(), out result);
    }

    private static object? GetWrappedValue(object? value)
    {
        if (value is null)
            return null;

        Type type = value.GetType();
        return type.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value)
            ?? type.GetField("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(value);
    }

    private static bool TryReadPoint(object? value, out Point point)
    {
        point = Point.Zero;
        if (value is null)
            return false;

        if (value is Point direct)
        {
            point = direct;
            return true;
        }

        object? wrapped = GetWrappedValue(value);
        if (wrapped is Point wrappedPoint)
        {
            point = wrappedPoint;
            return true;
        }

        return false;
    }

    private static bool TryReadVector2(object? value, out Vector2 vector)
    {
        vector = Vector2.Zero;
        if (value is null)
            return false;

        if (value is Vector2 direct)
        {
            vector = direct;
            return true;
        }

        object? wrapped = GetWrappedValue(value);
        if (wrapped is Vector2 wrappedVector)
        {
            vector = wrappedVector;
            return true;
        }

        return false;
    }

    private static bool TryReadVector2Member(object instance, out Vector2 result, params string[] names)
    {
        result = Vector2.Zero;
        Type type = instance.GetType();
        foreach (string name in names)
        {
            object? value = GetMemberValue(type, instance, name);
            if (TryReadVector2(value, out result))
                return true;
        }

        return false;
    }

    private static IEnumerable<Vector2> TilesIn(Rectangle rectangle)
    {
        for (int x = rectangle.Left; x < rectangle.Right; x++)
        {
            for (int y = rectangle.Top; y < rectangle.Bottom; y++)
                yield return new Vector2(x, y);
        }
    }

    private enum TileBlockerState
    {
        None,
        OnlySafeClearable,
        Unsafe
    }
}

[HarmonyPatch(typeof(GameLocation), nameof(GameLocation.isBuildable), new[] { typeof(Vector2), typeof(bool) })]
internal static class GameLocationIsBuildablePatch
{
    private static void Postfix(GameLocation __instance, Vector2 tileLocation, ref bool __result)
    {
        ModEntry.Instance?.OnIsBuildable(__instance, tileLocation, ref __result);
    }
}

internal static class BuildStructurePatch
{
    public static void Prefix(object __instance, object[] __args)
    {
        if (__instance is GameLocation location)
            ModEntry.Instance?.BeforeBuildStructure(location, __args);
    }
}
