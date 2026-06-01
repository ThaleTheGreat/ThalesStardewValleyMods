using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace ThaleTheGreat.ModPatcher;

internal sealed partial class ModEntry
{
    private const string PocketsId = "aedenthorn.Pockets";
    private const string AutomateToolSwapId = "Trapyy.AutomatetoolSwap";
    private const string ToolSmartSwitchId = "aedenthorn.ToolSmartSwitch";
    private const string AutoToolSelectId = "lolmaj.AutoToolSelect";
    private const string UtilityBeltId = "ModderDrew.UtilityBelt";
    private const string UtilityPocketId = "ModderDrew.UtilityPocket";
    private const string PocketsModDataKey = "aedenthorn.Pockets/pocket";

    private readonly List<PendingProxyItem> pendingProxyItems = new();
    private readonly List<IPocketProvider> pocketProviders = new();
    private Harmony? bridgeHarmony;
    private PocketsBridge? pockets;
    private UtilityBeltBridge? utilityBelt;
    private UtilityPocketBridge? utilityPocket;
    private MethodInfo? toolSmartSwitchSmartSwitchMethod;
    private MethodInfo? toolSmartSwitchSwitchForTerrainFeatureMethod;
    private MethodInfo? toolSmartSwitchGetToolsMethod;
    private FieldInfo? toolSmartSwitchConfigField;
    private FieldInfo? autoToolSelectConfigField;
    private FieldInfo? autoToolSelectToggleModField;
    private FieldInfo? autoToolSelectButtonPressedField;

    internal static ModEntry Instance { get; private set; } = null!;

    private readonly List<RegisteredRuntimeProxyPatch> RegisteredRuntimeProxyPatches = new();

    private static readonly Dictionary<MethodBase, RegisteredRuntimeMethodPatch> RuntimeMethodPatchesByOriginal = new();

    private readonly List<RegisteredRuntimeMethodPatch> RegisteredRuntimeMethodPatches = new();

    private void TryRegisterRuntimeChange(IContentPack pack, AssetPatchChange change)
    {
        if (!string.IsNullOrWhiteSpace(change.Type) && !string.IsNullOrWhiteSpace(change.Method))
        {
            this.TryRegisterRuntimeMethodChange(pack, change);
            return;
        }

        this.TryRegisterRuntimePatchChange(pack, change);
    }

    private void TryRegisterRuntimeMethodChange(IContentPack pack, AssetPatchChange change)
    {
        if (change.Prefix is null)
        {
            this.Monitor.Log($"Ignored PatchRuntime change from {pack.Manifest.UniqueID}: Prefix is required for Type/Method patches.", LogLevel.Warn);
            return;
        }

        RegisteredRuntimeMethodPatch patch = new()
        {
            PackId = pack.Manifest.UniqueID,
            Name = string.IsNullOrWhiteSpace(change.Name) ? pack.Manifest.Name : change.Name.Trim(),
            Type = change.Type.Trim(),
            Method = change.Method.Trim(),
            PatchAllOverloads = change.PatchAllOverloads,
            Prefix = change.Prefix
        };

        this.RegisteredRuntimeMethodPatches.Add(patch);
        this.LogDebug($"Registered PatchRuntime method patch '{patch.Name}' from {pack.Manifest.UniqueID}.");
    }

    private void TryRegisterRuntimePatchChange(IContentPack pack, AssetPatchChange change)
    {
        if (change.Bridge is null)
        {
            this.Monitor.Log($"Ignored PatchRuntime change from {pack.Manifest.UniqueID}: Bridge or Type/Method is required.", LogLevel.Warn);
            return;
        }

        RegisteredRuntimeProxyPatch patch = new()
        {
            PackId = pack.Manifest.UniqueID,
            Name = string.IsNullOrWhiteSpace(change.Name) ? pack.Manifest.Name : change.Name.Trim(),
            RequireAnySource = NormalizeIds(change.RequireAnySource.Count > 0 ? change.RequireAnySource : change.Sources.Select(source => source.UniqueID)),
            RequireAnyTarget = NormalizeIds(change.RequireAnyTarget.Count > 0 ? change.RequireAnyTarget : change.Targets.Select(target => target.UniqueID)),
            Bridge = change.Bridge
        };

        if (patch.RequireAnySource.Count == 0 || patch.RequireAnyTarget.Count == 0)
        {
            this.Monitor.Log($"Ignored PatchRuntime change from {pack.Manifest.UniqueID}: RequireAnySource and RequireAnyTarget are required.", LogLevel.Warn);
            return;
        }

        this.RegisteredRuntimeProxyPatches.Add(patch);
        this.LogDebug($"Registered PatchRuntime proxy patch '{patch.Name}' from {pack.Manifest.UniqueID}.");
    }

    private static List<string> NormalizeIds(IEnumerable<string> ids)
    {
        return ids.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void ClearBridgeProxies()
    {
        pendingProxyItems.Clear();
    }

    private void ApplyRuntimeMethodPatches()
    {
        if (this.RegisteredRuntimeMethodPatches.Count == 0)
            return;

        bridgeHarmony ??= new Harmony($"{ModManifest.UniqueID}.PatchRuntime");

        foreach (RegisteredRuntimeMethodPatch patch in this.RegisteredRuntimeMethodPatches)
            this.ApplyRuntimeMethodPatch(patch);
    }

    private void ApplyRuntimeMethodPatch(RegisteredRuntimeMethodPatch patch)
    {
        Type? targetType = AccessTools.TypeByName(patch.Type);
        if (targetType is null)
        {
            this.Monitor.Log($"Ignored PatchRuntime method patch '{patch.Name}': type '{patch.Type}' was not found.", LogLevel.Warn);
            return;
        }

        IEnumerable<MethodInfo> methods = AccessTools.GetDeclaredMethods(targetType)
            .Where(method => string.Equals(method.Name, patch.Method, StringComparison.Ordinal));

        if (!patch.PatchAllOverloads)
            methods = methods.Take(1);

        int patched = 0;
        foreach (MethodInfo method in methods)
        {
            MethodInfo? prefixMethod = method.ReturnType == typeof(bool)
                ? AccessTools.Method(typeof(ModEntry), nameof(PatchRuntimeBoolPrefix))
                : method.ReturnType == typeof(void)
                    ? AccessTools.Method(typeof(ModEntry), nameof(PatchRuntimeVoidPrefix))
                    : null;

            if (prefixMethod is null)
            {
                this.Monitor.Log($"Ignored PatchRuntime method patch '{patch.Name}' for {patch.Type}.{patch.Method}: only bool and void returns are supported.", LogLevel.Warn);
                continue;
            }

            RuntimeMethodPatchesByOriginal[method] = patch;
            bridgeHarmony!.Patch(method, prefix: new HarmonyMethod(prefixMethod));
            patched++;
        }

        if (patched == 0)
            this.Monitor.Log($"Ignored PatchRuntime method patch '{patch.Name}': no matching methods were patched.", LogLevel.Warn);
        else
            this.LogDebug($"Applied PatchRuntime method patch '{patch.Name}' to {patched} method(s).");
    }

    private static bool PatchRuntimeBoolPrefix(MethodBase __originalMethod, ref bool __result)
    {
        if (!RuntimeMethodPatchesByOriginal.TryGetValue(__originalMethod, out RegisteredRuntimeMethodPatch? patch))
            return true;

        if (patch.Prefix.Return is not null)
        {
            try
            {
                __result = Convert.ToBoolean(patch.Prefix.Return);
            }
            catch
            {
                __result = false;
            }
        }

        return !patch.Prefix.SkipOriginal;
    }

    private static bool PatchRuntimeVoidPrefix(MethodBase __originalMethod)
    {
        if (!RuntimeMethodPatchesByOriginal.TryGetValue(__originalMethod, out RegisteredRuntimeMethodPatch? patch))
            return true;

        return !patch.Prefix.SkipOriginal;
    }

    private void ApplyRuntimePatches()
    {
        this.ApplyRuntimeMethodPatches();

        if (this.RegisteredRuntimeProxyPatches.Count == 0)
            return;

        Instance = this;

        bool automateToolSwapLoaded = Helper.ModRegistry.IsLoaded(AutomateToolSwapId);
        bool toolSmartSwitchLoaded = Helper.ModRegistry.IsLoaded(ToolSmartSwitchId);
        bool autoToolSelectLoaded = Helper.ModRegistry.IsLoaded(AutoToolSelectId);
        bool hasDeclaredPair = this.RegisteredRuntimeProxyPatches.Any(patch =>
            patch.RequireAnySource.Any(id => Helper.ModRegistry.IsLoaded(id))
            && patch.RequireAnyTarget.Any(id => Helper.ModRegistry.IsLoaded(id)));

        if (!hasDeclaredPair || (!automateToolSwapLoaded && !toolSmartSwitchLoaded && !autoToolSelectLoaded))
            return;

        bridgeHarmony = new Harmony($"{ModManifest.UniqueID}.PatchRuntime");

        if (Helper.ModRegistry.IsLoaded(PocketsId))
        {
            pockets = PocketsBridge.TryCreate(Monitor);
            if (pockets is not null)
                pocketProviders.Add(pockets);
            else
                Monitor.Log("Could not find Pockets internals. Pockets compatibility was not applied.", LogLevel.Warn);
        }

        if (Helper.ModRegistry.IsLoaded(UtilityBeltId))
        {
            utilityBelt = UtilityBeltBridge.TryCreate(Monitor);
            if (utilityBelt is not null)
            {
                pocketProviders.Add(utilityBelt);
                PatchUtilityBeltCapture();
            }
            else
                Monitor.Log("Could not find Utility Belt internals. Utility Belt compatibility was not applied.", LogLevel.Warn);
        }

        if (Helper.ModRegistry.IsLoaded(UtilityPocketId))
        {
            utilityPocket = UtilityPocketBridge.TryCreate(Monitor);
            if (utilityPocket is not null)
            {
                pocketProviders.Add(utilityPocket);
                PatchUtilityPocketCapture();
            }
            else
                Monitor.Log("Could not find Utility Pocket internals. Utility Pocket compatibility was not applied.", LogLevel.Warn);
        }

        if (pocketProviders.Count == 0)
            return;

        if (automateToolSwapLoaded)
            PatchAutomateToolSwap();

        if (toolSmartSwitchLoaded)
            PatchToolSmartSwitch();

        if (autoToolSelectLoaded)
            PatchAutoToolSelect();
    }

    private void PatchUtilityBeltCapture()
    {
        if (bridgeHarmony is null || utilityBelt?.ModEntryType is null)
            return;

        MethodInfo? onSaveLoaded = AccessTools.Method(utilityBelt.ModEntryType, "OnSaveLoaded");
        MethodInfo? onUpdateTicked = AccessTools.Method(utilityBelt.ModEntryType, "OnUpdateTicked");
        HarmonyMethod capture = new(typeof(ModEntry), nameof(UtilityBeltCapturePostfix));

        if (onSaveLoaded is not null)
            bridgeHarmony.Patch(onSaveLoaded, postfix: capture);
        if (onUpdateTicked is not null)
            bridgeHarmony.Patch(onUpdateTicked, postfix: capture);
    }

    private void PatchUtilityPocketCapture()
    {
        if (bridgeHarmony is null || utilityPocket?.ModEntryType is null)
            return;

        MethodInfo? onLoading = AccessTools.Method(utilityPocket.ModEntryType, "OnLoading");
        MethodInfo? onUpdateTicked = AccessTools.Method(utilityPocket.ModEntryType, "OnUpdateTicked");
        HarmonyMethod capture = new(typeof(ModEntry), nameof(UtilityPocketCapturePostfix));

        if (onLoading is not null)
            bridgeHarmony.Patch(onLoading, postfix: capture);
        if (onUpdateTicked is not null)
            bridgeHarmony.Patch(onUpdateTicked, postfix: capture);
    }

    private static void UtilityBeltCapturePostfix(object __instance)
    {
        Instance.utilityBelt?.Capture(__instance);
    }

    private static void UtilityPocketCapturePostfix(object __instance)
    {
        Instance.utilityPocket?.Capture(__instance);
    }


    private void PatchAutomateToolSwap()
    {
        Type? inventoryHandler = AccessTools.TypeByName("Core.InventoryHandler");
        if (inventoryHandler is null)
        {
            Monitor.Log("Could not find Automate Tool Swap's inventory handler. Automate Tool Swap compatibility was not applied.", LogLevel.Warn);
            return;
        }

        MethodInfo? setTool = AccessTools.Method(inventoryHandler, "SetTool");
        MethodInfo? setItem = AccessTools.Method(inventoryHandler, "SetItem");
        if (setTool is null || setItem is null || bridgeHarmony is null)
        {
            Monitor.Log("Could not find Automate Tool Swap's switch methods. Automate Tool Swap compatibility was not applied.", LogLevel.Warn);
            return;
        }

        bridgeHarmony.Patch(
            original: setTool,
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetToolPrefix))
        );
        bridgeHarmony.Patch(
            original: setItem,
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetItemPrefix))
        );
    }

    private void PatchToolSmartSwitch()
    {
        Type? modEntry = AccessTools.TypeByName("ToolSmartSwitch.ModEntry");
        MethodInfo? switchToolType = modEntry is not null
            ? AccessTools.Method(modEntry, "SwitchToolType")
            : null;

        if (switchToolType is null || bridgeHarmony is null)
        {
            Monitor.Log("Could not find Tool Smart Switch's switch method. Tool Smart Switch compatibility was not applied.", LogLevel.Warn);
            return;
        }

        toolSmartSwitchSmartSwitchMethod = AccessTools.Method(modEntry, "SmartSwitch", new[] { typeof(Farmer) });
        toolSmartSwitchSwitchForTerrainFeatureMethod = AccessTools.Method(modEntry, "SwitchForTerrainFeature", new[] { typeof(Farmer), typeof(TerrainFeature), typeof(Dictionary<int, Tool>) });
        toolSmartSwitchGetToolsMethod = AccessTools.Method(modEntry, "GetTools", new[] { typeof(Farmer) });
        toolSmartSwitchConfigField = AccessTools.Field(modEntry, "Config");

        bridgeHarmony.Patch(
            original: switchToolType,
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ToolSmartSwitchPrefix))
        );

        if (toolSmartSwitchSmartSwitchMethod is not null)
        {
            HarmonyMethod useToolPrefix = new(typeof(ModEntry), nameof(ToolSmartSwitchUseToolButtonPrefix))
            {
                priority = Priority.First
            };

            bridgeHarmony.Patch(
                original: AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton)),
                prefix: useToolPrefix
            );
        }

        if (toolSmartSwitchSwitchForTerrainFeatureMethod is not null && toolSmartSwitchGetToolsMethod is not null)
        {
            HarmonyMethod cropPrefix = new(typeof(ModEntry), nameof(ToolSmartSwitchHoeDirtPrefix))
            {
                priority = Priority.First
            };

            bridgeHarmony.Patch(
                original: AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performUseAction)),
                prefix: cropPrefix
            );
        }
    }

    private void PatchAutoToolSelect()
    {
        Type? autoToolSelect = AccessTools.TypeByName("AutoToolSelect.AutoToolSelectMod");
        if (autoToolSelect is null || bridgeHarmony is null)
        {
            Monitor.Log("Could not find AutoToolSelect's mod class. AutoToolSelect compatibility was not applied.", LogLevel.Warn);
            return;
        }

        MethodInfo? setTool = AccessTools.Method(autoToolSelect, "SetTool", new[] { typeof(Type), typeof(int), typeof(int) });
        MethodInfo? setItem = AccessTools.Method(autoToolSelect, "SetItem", new[] { typeof(string), typeof(int) });
        MethodInfo? setScythe = AccessTools.Method(autoToolSelect, "SetScythe", new[] { typeof(int) });
        MethodInfo? setWeapon = AccessTools.Method(autoToolSelect, "SetWeapon", new[] { typeof(int) });
        autoToolSelectConfigField = AccessTools.Field(autoToolSelect, "Config");
        autoToolSelectToggleModField = AccessTools.Field(autoToolSelect, "togglemod");
        autoToolSelectButtonPressedField = AccessTools.Field(autoToolSelect, "buttonPressed");

        if (setTool is null || setItem is null || setScythe is null || setWeapon is null)
        {
            Monitor.Log("Could not find AutoToolSelect's switch methods. AutoToolSelect compatibility was not applied.", LogLevel.Warn);
            return;
        }

        bridgeHarmony.Patch(setTool, prefix: new HarmonyMethod(typeof(ModEntry), nameof(AutoToolSelectSetToolPrefix)));
        bridgeHarmony.Patch(setItem, prefix: new HarmonyMethod(typeof(ModEntry), nameof(AutoToolSelectSetItemPrefix)));
        bridgeHarmony.Patch(setScythe, prefix: new HarmonyMethod(typeof(ModEntry), nameof(AutoToolSelectSetScythePrefix)));
        bridgeHarmony.Patch(setWeapon, prefix: new HarmonyMethod(typeof(ModEntry), nameof(AutoToolSelectSetWeaponPrefix)));

        HarmonyMethod useToolPrefix = new(typeof(ModEntry), nameof(AutoToolSelectUseToolButtonPrefix))
        {
            priority = Priority.First
        };

        bridgeHarmony.Patch(
            original: AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton)),
            prefix: useToolPrefix
        );
    }

    private static bool SetToolPrefix(Farmer player, Type toolType, string aux = "", bool anyTool = false)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, player.maxItems.Value, item => MatchesTool(item, toolType, aux, anyTool)))
            return true;

        return !Instance.TryUsePocketProxy(player, item => MatchesTool(item, toolType, aux, anyTool));
    }

    private static bool SetItemPrefix(Farmer player, string category, string item = "", string crops = "Both", int aux = 0)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, player.maxItems.Value, candidate => MatchesItem(candidate, category, item, crops, aux)))
            return true;

        return !Instance.TryUsePocketProxy(player, candidate => MatchesItem(candidate, category, item, crops, aux));
    }

    private static bool ToolSmartSwitchPrefix(Farmer f, Type? type, Dictionary<int, Tool> tools, ref bool __result)
    {
        if (!Context.IsWorldReady || f is null || !Instance.HasPocketProviders())
            return true;

        if (MatchesToolSmartSwitch(f.CurrentTool, type))
            return true;

        foreach (Tool tool in tools.Values)
        {
            if (MatchesToolSmartSwitch(tool, type))
                return true;
        }

        if (!Instance.TryUsePocketProxy(f, item => MatchesToolSmartSwitch(item, type), playSound: true))
            return true;

        __result = true;
        return false;
    }

    private static bool ToolSmartSwitchUseToolButtonPrefix()
    {
        Farmer player = Game1.player;
        if (!ShouldRunToolSmartSwitchHoldingToolBypass(player, requireCropSwitch: false) || Game1.fadeToBlack || !Context.CanPlayerMove)
            return true;

        try
        {
            Instance.toolSmartSwitchSmartSwitchMethod?.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Tool Smart Switch holding-tool bypass failed: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        return true;
    }

    private static bool ToolSmartSwitchHoeDirtPrefix(HoeDirt __instance)
    {
        Farmer player = Game1.player;
        if (!ShouldRunToolSmartSwitchHoldingToolBypass(player, requireCropSwitch: true))
            return true;

        try
        {
            object? tools = Instance.toolSmartSwitchGetToolsMethod?.Invoke(null, new object[] { player });
            if (tools is Dictionary<int, Tool> toolMap)
                Instance.toolSmartSwitchSwitchForTerrainFeatureMethod?.Invoke(null, new object[] { player, __instance, toolMap });
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Tool Smart Switch crop bypass failed: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        return true;
    }

    private static bool ShouldRunToolSmartSwitchHoldingToolBypass(Farmer? player, bool requireCropSwitch)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders() || Instance.toolSmartSwitchConfigField is null)
            return false;

        if (player.CurrentTool is not null || !Instance.HasAnyPocketTool(player))
            return false;

        object? config = Instance.toolSmartSwitchConfigField.GetValue(null);
        if (config is null)
            return false;

        if (!GetBoolProperty(config, "ModEnabled") || !GetBoolProperty(config, "SwitchEnabled") || !GetBoolProperty(config, "HoldingTool"))
            return false;

        return !requireCropSwitch || GetBoolProperty(config, "SwitchForCrops");
    }

    private bool HasAnyPocketTool(Farmer player)
    {
        if (!HasPocketProviders())
            return false;

        foreach (PocketInventory pocketInventory in GetPocketInventories(player))
        {
            foreach (Item? item in pocketInventory.Items)
            {
                if (item is Tool)
                    return true;
            }
        }

        return false;
    }

    private bool HasPocketProviders()
    {
        return pocketProviders.Count > 0;
    }

    private IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
    {
        foreach (IPocketProvider provider in pocketProviders)
        {
            foreach (PocketInventory inventory in provider.GetPocketInventories(player))
                yield return inventory;
        }
    }

    private static bool GetBoolProperty(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance) is bool value && value;
    }

    private static bool AutoToolSelectUseToolButtonPrefix()
    {
        Farmer player = Game1.player;
        if (!ShouldRunAutoToolSelectUseBypass(player))
            return true;

        try
        {
            Instance.TryAutoToolSelectForCurrentTarget(player);
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"AutoToolSelect Pockets use-button bypass failed: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        return true;
    }

    private static bool ShouldRunAutoToolSelectUseBypass(Farmer? player)
    {
        if (!Context.IsWorldReady || !Context.CanPlayerMove || player is null || !Instance.HasPocketProviders())
            return false;

        if (Game1.activeClickableMenu is not null || Game1.fadeToBlack)
            return false;

        if (player.CurrentItem is Tool || Instance.HasActiveProxy(player))
            return false;

        if (Instance.autoToolSelectConfigField is null || Instance.autoToolSelectToggleModField is null || Instance.autoToolSelectButtonPressedField is null)
            return false;

        object? config = Instance.autoToolSelectConfigField.GetValue(null);
        if (config is null)
            return false;

        bool toggleMod = Instance.autoToolSelectToggleModField.GetValue(null) is bool toggle && toggle;
        bool buttonPressed = Instance.autoToolSelectButtonPressedField.GetValue(null) is bool pressed && pressed;
        return toggleMod || buttonPressed;
    }

    private bool TryAutoToolSelectForCurrentTarget(Farmer player)
    {
        object? config = autoToolSelectConfigField?.GetValue(null);
        if (config is null)
            return false;

        int range = GetBoolProperty(config, "CheckWholeBackpack") ? player.Items.Count : 12;
        Vector2 targetTile = GetAutoToolSelectTargetTile(player, config);
        Point targetPoint = new((int)targetTile.X * Game1.tileSize + Game1.tileSize / 2, (int)targetTile.Y * Game1.tileSize + Game1.tileSize / 2);
        Rectangle targetRect = new((int)targetTile.X * Game1.tileSize, (int)targetTile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
        GameLocation location = player.currentLocation;

        Predicate<Item>? desired = null;

        void SetDesired(Predicate<Item> predicate)
        {
            desired = predicate;
        }

        void SetTool(Type type, int level = 0)
        {
            SetDesired(item => MatchesAutoToolSelectTool(item, type, level));
        }

        void SetItemName(string name)
        {
            SetDesired(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        }

        void SetScythe()
        {
            if (HasMatchingInventoryItem(player, range, MatchesAutoToolSelectScythe) || HasMatchingPocketItem(player, MatchesAutoToolSelectScythe))
                SetDesired(MatchesAutoToolSelectScythe);
            else
                SetDesired(item => item is MeleeWeapon);
        }

        void SetWeapon()
        {
            if (location is Farm || location.IsGreenhouse)
            {
                SetScythe();
                return;
            }

            Predicate<Item> weapon = item => item is MeleeWeapon && !item.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase);
            if (HasMatchingInventoryItem(player, range, weapon) || HasMatchingPocketItem(player, weapon))
                SetDesired(weapon);
            else
                SetDesired(MatchesAutoToolSelectScythe);
        }

        if (GetBoolProperty(config, "IfNoneToolChooseWeapon"))
            SetWeapon();

        if (location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "Diggable", "Back") is not null && GetBoolProperty(config, "HoeSelect"))
            SetTool(typeof(Hoe));

        if (location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "Water", "Back") is not null && location is not VolcanoDungeon)
            SetTool(typeof(FishingRod));

        if (location is AnimalHouse animalHouse && location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "Trough", "Back") is not null && !animalHouse.objects.ContainsKey(targetTile))
            SetItemName("Hay");

        bool waterSource = location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "Water", "Back") is not null
            || location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "WaterSource", "Back") is not null;
        bool buildingWaterSource = location.IsBuildableLocation()
            && location.getBuildingAt(targetTile) is { } building
            && (building.buildingType.Value.Equals("Well", StringComparison.OrdinalIgnoreCase) && building.daysOfConstructionLeft.Value <= 0
                || building.buildingType.Value.Equals("Pet Bowl", StringComparison.OrdinalIgnoreCase));
        bool cooledLava = location is VolcanoDungeon volcanoDungeon && volcanoDungeon.IsCooledLava((int)targetTile.X, (int)targetTile.Y);
        if ((location is Farm || location.IsGreenhouse || location is VolcanoDungeon && !cooledLava) && (waterSource || buildingWaterSource))
            SetTool(typeof(WateringCan));

        Rectangle orePanRect = new(location.orePanPoint.X * 64 - 64, location.orePanPoint.Y * 64 - 64, 256, 256);
        if (location.doesTileHaveProperty((int)targetTile.X, (int)targetTile.Y, "Water", "Back") is not null
            && ((orePanRect.Contains(targetPoint) && Utility.distance(player.StandingPixel.X, orePanRect.Center.X, player.StandingPixel.Y, orePanRect.Center.Y) <= 256f) || player.GetBoundingBox().Intersects(orePanRect)))
            SetTool(typeof(Pan));

        if (location.objects.TryGetValue(targetTile, out StardewValley.Object obj))
        {
            if (obj.Name.Equals("Garden Pot", StringComparison.OrdinalIgnoreCase) && obj is IndoorPot pot && !pot.hoeDirt.Value.isWatered())
                SetTool(typeof(WateringCan));
            if (obj.Name.Equals("Artifact Spot", StringComparison.OrdinalIgnoreCase) || obj.Name.Equals("Seed Spot", StringComparison.OrdinalIgnoreCase))
                SetTool(typeof(Hoe));
            if (obj.IsBreakableStone())
                SetTool(typeof(Pickaxe));
            if (obj.IsTwig())
                SetTool(typeof(Axe));
            if (obj.IsWeeds() || obj.Name.Equals("Barrel", StringComparison.OrdinalIgnoreCase))
                SetWeapon();

            Predicate<Item> dropIn = item => obj.performObjectDropInAction(item, true, player);
            if (HasMatchingInventoryItem(player, range, dropIn) || HasMatchingPocketItem(player, dropIn))
                SetDesired(dropIn);
        }

        if (location.terrainFeatures.TryGetValue(targetTile, out TerrainFeature terrainFeature))
        {
            if (terrainFeature is HoeDirt dirt)
            {
                if (dirt.crop is not null && ((dirt.crop.GetHarvestMethod() == StardewValley.GameData.Crops.HarvestMethod.Scythe && dirt.crop.fullyGrown.Value) || dirt.crop.dead.Value))
                    SetScythe();
                else if (dirt.crop is not null && dirt.crop.whichForageCrop.Value == "2")
                    SetTool(typeof(Hoe));
                else if (GetBoolProperty(config, "PickaxeOverWateringCan"))
                    SetTool(typeof(Pickaxe));
                else
                    SetTool(typeof(WateringCan));
            }

            if (terrainFeature is GiantCrop)
                SetTool(typeof(Axe));

            if (terrainFeature is Tree)
            {
                if (location.getObjectAtTile((int)targetTile.X, (int)targetTile.Y)?.IsTapper() == true)
                    SetScythe();
                else
                    SetTool(typeof(Axe));
            }

            if (terrainFeature is Grass)
                SetWeapon();
        }

        foreach (FarmAnimal animal in location.animals.Values)
        {
            if (animal.GetHarvestBoundingBox().Intersects(targetRect) && animal.CanGetProduceWithTool(new Shears()) && animal.currentProduce.Value is not null && animal.isAdult())
                SetItemName("Shears");
            if (animal.GetHarvestBoundingBox().Intersects(targetRect) && animal.CanGetProduceWithTool(new MilkPail()) && animal.currentProduce.Value is not null && animal.isAdult())
                SetItemName("Milk Pail");
        }

        for (int i = location.resourceClumps.Count - 1; i >= 0; --i)
        {
            ResourceClump clump = location.resourceClumps[i];
            if (!clump.getBoundingBox().Contains(targetPoint))
                continue;

            int index = clump.parentSheetIndex.Value;
            if (index == 600)
                SetTool(typeof(Axe), 1);
            if (index == 602)
                SetTool(typeof(Axe), 2);
            if (index == 148 || index == 622)
                SetTool(typeof(Pickaxe), 3);
            if (index == 672)
                SetTool(typeof(Pickaxe), 2);
            if (index is 752 or 754 or 756 or 758)
                SetTool(typeof(Pickaxe));
        }

        if (location is MineShaft)
        {
            foreach (Character character in location.characters)
            {
                if (character is RockCrab crab && !crab.isMoving() && targetRect.Contains(crab.Position))
                {
                    if (crab.Name.Equals("Stick Bug", StringComparison.OrdinalIgnoreCase))
                        SetTool(typeof(Axe));
                    else
                        SetTool(typeof(Pickaxe));
                }
            }
        }

        if (desired is null || MatchesCurrentItem(player, desired))
            return false;

        if (TrySelectInventoryItem(player, range, desired))
            return true;

        return TryUsePocketProxy(player, desired, insertToolbarSlot: false);
    }

    private static Vector2 GetAutoToolSelectTargetTile(Farmer player, object config)
    {
        if ((player.isRidingHorse() && GetBoolProperty(config, "RideHorseCursor")) || GetBoolProperty(config, "CursorOverToolHitLocation"))
            return Game1.currentCursorTile;

        return new Vector2((int)player.GetToolLocation().X / Game1.tileSize, (int)player.GetToolLocation().Y / Game1.tileSize);
    }

    private bool HasMatchingPocketItem(Farmer player, Predicate<Item> predicate)
    {
        if (!HasPocketProviders())
            return false;

        foreach (PocketInventory pocketInventory in GetPocketInventories(player))
        {
            foreach (Item? item in pocketInventory.Items)
            {
                if (item is not null && predicate(item))
                    return true;
            }
        }

        return false;
    }

    private static bool MatchesCurrentItem(Farmer player, Predicate<Item> predicate)
    {
        return player.CurrentItem is Item item && predicate(item);
    }

    private static bool HasMatchingInventoryItem(Farmer player, int range, Predicate<Item> predicate)
    {
        return Contains(player.Items, range, predicate);
    }

    private static bool TrySelectInventoryItem(Farmer player, int range, Predicate<Item> predicate)
    {
        int count = Math.Min(range, player.Items.Count);
        for (int i = 0; i < count; i++)
        {
            if (player.Items[i] is not Item item || !predicate(item))
                continue;

            player.CurrentToolIndex = i % 12;
            for (int j = 0; j < i / 12; j++)
                ShiftToolbarLikeAutoToolSelect(player);

            return true;
        }

        return false;
    }

    private static void ShiftToolbarLikeAutoToolSelect(Farmer player)
    {
        player.CurrentItem?.actionWhenStopBeingHeld(player);
        IList<Item?> toolbarItems = player.Items.GetRange(0, 12);
        player.Items.RemoveRange(0, 12);
        player.Items.AddRange(toolbarItems);
        player.netItemStowed.Set(newValue: false);
        player.CurrentItem?.actionWhenBeingHeld(player);
        for (int j = 0; j < Game1.onScreenMenus.Count; j++)
        {
            if (Game1.onScreenMenus[j] is StardewValley.Menus.Toolbar toolbar)
            {
                toolbar.shifted(true);
                break;
            }
        }
    }

    private static bool AutoToolSelectSetToolPrefix(Type t, int range, int Level)
    {
        Farmer player = Game1.player;
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, range, item => MatchesAutoToolSelectTool(item, t, Level)))
            return true;

        return !Instance.TryUsePocketProxy(player, item => MatchesAutoToolSelectTool(item, t, Level), insertToolbarSlot: true);
    }

    private static bool AutoToolSelectSetItemPrefix(string name, int range)
    {
        Farmer player = Game1.player;
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, range, item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return true;

        return !Instance.TryUsePocketProxy(player, item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase), insertToolbarSlot: true);
    }

    private static bool AutoToolSelectSetScythePrefix(int range)
    {
        Farmer player = Game1.player;
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, range, MatchesAutoToolSelectScythe))
            return true;

        if (Instance.TryUsePocketProxy(player, MatchesAutoToolSelectScythe, insertToolbarSlot: true))
            return false;

        if (Contains(player.Items, range, item => item is MeleeWeapon))
            return true;

        return !Instance.TryUsePocketProxy(player, item => item is MeleeWeapon, insertToolbarSlot: true);
    }

    private static bool AutoToolSelectSetWeaponPrefix(int range)
    {
        Farmer player = Game1.player;
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (player.currentLocation is Farm || player.currentLocation.IsGreenhouse)
            return AutoToolSelectSetScythePrefix(range);

        Predicate<Item> preferred = item => item is MeleeWeapon && !item.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase);

        if (Contains(player.Items, range, preferred))
            return true;

        if (Instance.TryUsePocketProxy(player, preferred, insertToolbarSlot: true))
            return false;

        if (Contains(player.Items, range, MatchesAutoToolSelectScythe))
            return true;

        return !Instance.TryUsePocketProxy(player, MatchesAutoToolSelectScythe, insertToolbarSlot: true);
    }

    private bool HasActiveProxy(Farmer player)
    {
        return pendingProxyItems.Any(proxy => proxy.PlayerId == player.UniqueMultiplayerID);
    }

    private bool TryUsePocketProxy(Farmer player, Predicate<Item> predicate, bool playSound = false, bool insertToolbarSlot = false)
    {
        if (!HasPocketProviders())
            return false;

        TemporarySlot? temporarySlot = null;

        foreach (PocketInventory pocketInventory in GetPocketInventories(player))
        {
            for (int slot = 0; slot < pocketInventory.Items.Count; slot++)
            {
                Item? pocketItem = pocketInventory.Items[slot];
                if (pocketItem is null || !predicate(pocketItem))
                    continue;

                temporarySlot ??= CreateTemporaryInventorySlot(player, insertToolbarSlot);
                if (temporarySlot is null)
                    return false;

                Item proxyItem = CreateProxyItem(pocketItem);
                int previousSlot = player.CurrentToolIndex;
                int proxySlot = temporarySlot.Value.Slot;

                if (temporarySlot.Value.Inserted)
                    player.Items.Insert(proxySlot, proxyItem);
                else
                    player.Items[proxySlot] = proxyItem;

                player.CurrentToolIndex = proxySlot;

                pendingProxyItems.RemoveAll(proxy => proxy.PlayerId == player.UniqueMultiplayerID && ReferenceEquals(proxy.ProxyItem, proxyItem));
                pendingProxyItems.Add(new PendingProxyItem(player.UniqueMultiplayerID, previousSlot, proxySlot, temporarySlot.Value.OriginalMaxItems, temporarySlot.Value.OriginalItemCount, temporarySlot.Value.Inserted, pocketInventory.Items, slot, pocketItem, proxyItem));
                if (playSound)
                    Game1.playSound("toolSwap");

                return true;
            }
        }

        return false;
    }

    private static TemporarySlot? CreateTemporaryInventorySlot(Farmer player, bool insertToolbarSlot)
    {
        int originalMaxItems = Math.Max(0, player.maxItems.Value);
        int originalItemCount = player.Items.Count;

        if (insertToolbarSlot)
        {
            int slot = Math.Clamp(player.CurrentToolIndex, 0, Math.Min(11, player.Items.Count));
            player.maxItems.Value = Math.Max(player.maxItems.Value, originalMaxItems + 1);
            return new TemporarySlot(slot, originalMaxItems, originalItemCount, Inserted: true);
        }

        int appendedSlot = player.Items.Count;
        player.Items.Add(null);
        if (player.maxItems.Value <= appendedSlot)
            player.maxItems.Value = appendedSlot + 1;

        return new TemporarySlot(appendedSlot, originalMaxItems, originalItemCount, Inserted: false);
    }

    private static Item CreateProxyItem(Item source)
    {
        Item proxy = source.getOne();

        if (source.maximumStackSize() > 1)
            proxy.Stack = Math.Min(Math.Max(1, source.Stack), Math.Max(1, proxy.maximumStackSize()));

        return proxy;
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Context.IsWorldReady || pendingProxyItems.Count == 0)
            return;

        for (int i = pendingProxyItems.Count - 1; i >= 0; i--)
        {
            Farmer? player = Game1.GetPlayer(pendingProxyItems[i].PlayerId);
            if (player is not null)
                RestoreProxyItem(player, pendingProxyItems[i], force: true);

            pendingProxyItems.RemoveAt(i);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.ProcessPendingInteractions();

        if (!Context.IsWorldReady || pendingProxyItems.Count == 0)
            return;

        for (int i = pendingProxyItems.Count - 1; i >= 0; i--)
        {
            PendingProxyItem proxy = pendingProxyItems[i];
            proxy.Ticks++;

            Farmer? player = Game1.GetPlayer(proxy.PlayerId);
            if (player is null)
            {
                pendingProxyItems.RemoveAt(i);
                continue;
            }

            if (proxy.Ticks < 8 || IsUseToolInputDown() || !player.canMove)
                continue;

            RestoreProxyItem(player, proxy, force: false);
            pendingProxyItems.RemoveAt(i);
        }
    }

    private static void RestoreProxyItem(Farmer player, PendingProxyItem proxy, bool force)
    {
        if (proxy.PocketSlot < 0 || proxy.PocketSlot >= proxy.PocketItems.Count)
        {
            RestoreInventorySize(player, proxy);
            return;
        }

        int proxySlot = proxy.InsertedSlot ? FindItemSlot(player.Items, proxy.ProxyItem) : proxy.ProxySlot;
        if (proxySlot < 0 || proxySlot >= player.Items.Count)
        {
            RestoreInventorySize(player, proxy);
            return;
        }

        Item? currentProxy = player.Items[proxySlot];
        if (!ReferenceEquals(currentProxy, proxy.ProxyItem))
        {
            if (force && currentProxy is not null && currentProxy.canStackWith(proxy.OriginalPocketItem))
            {
                Item? leftover = player.addItemToInventory(currentProxy);
                if (leftover is null)
                    player.Items[proxySlot] = null;
            }

            RestoreInventorySize(player, proxy);
            return;
        }

        SyncPocketItemFromProxy(proxy, currentProxy);
        if (proxy.InsertedSlot)
            player.Items.RemoveAt(proxySlot);
        else
            player.Items[proxySlot] = null;

        if (proxy.InsertedSlot || player.CurrentToolIndex == proxySlot)
            player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, proxy.OriginalMaxItems - 1));

        RestoreInventorySize(player, proxy);
    }

    private static void RestoreInventorySize(Farmer player, PendingProxyItem proxy)
    {
        if (proxy.InsertedSlot)
        {
            int proxySlot = FindItemSlot(player.Items, proxy.ProxyItem);
            if (proxySlot >= 0)
                player.Items.RemoveAt(proxySlot);
        }
        else
        {
            for (int i = player.Items.Count - 1; i >= proxy.OriginalItemCount; i--)
            {
                if (player.Items[i] is null || ReferenceEquals(player.Items[i], proxy.ProxyItem))
                    player.Items.RemoveAt(i);
                else
                    break;
            }
        }

        player.maxItems.Value = proxy.OriginalMaxItems;
        if (proxy.InsertedSlot || player.CurrentToolIndex >= player.maxItems.Value)
            player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, player.maxItems.Value - 1));
    }

    private static int FindItemSlot(IList<Item?> items, Item target)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], target))
                return i;
        }

        return -1;
    }

    private static void SyncPocketItemFromProxy(PendingProxyItem proxy, Item proxyItem)
    {
        Item? pocketItem = proxy.PocketItems[proxy.PocketSlot];
        if (!ReferenceEquals(pocketItem, proxy.OriginalPocketItem))
            return;

        if (proxy.OriginalPocketItem.maximumStackSize() > 1)
        {
            int consumed = Math.Max(0, proxy.StartingProxyStack - Math.Max(0, proxyItem.Stack));
            if (consumed <= 0)
                return;

            proxy.OriginalPocketItem.Stack -= consumed;
            if (proxy.OriginalPocketItem.Stack <= 0)
                proxy.PocketItems[proxy.PocketSlot] = null;

            return;
        }

        proxy.PocketItems[proxy.PocketSlot] = proxyItem;
    }

    private bool IsUseToolInputDown()
    {
        foreach (InputButton button in Game1.options.useToolButton)
        {
            SButton sButton = button.ToSButton();
            if (sButton != SButton.None && Helper.Input.IsDown(sButton))
                return true;
        }

        return Helper.Input.IsDown(SButton.MouseLeft) || Helper.Input.IsDown(SButton.ControllerX);
    }

    private static bool Contains(IList<Item?> items, int maxItems, Predicate<Item> predicate)
    {
        int count = Math.Min(maxItems, items.Count);
        for (int i = 0; i < count; i++)
        {
            if (items[i] is Item item && predicate(item))
                return true;
        }

        return false;
    }

    private static bool MatchesAutoToolSelectTool(Item? item, Type toolType, int level)
    {
        return item is Tool tool && item.GetType() == toolType && tool.UpgradeLevel >= level;
    }

    private static bool MatchesAutoToolSelectScythe(Item item)
    {
        return item.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesTool(Item? item, Type toolType, string aux, bool anyTool)
    {
        if (item is null)
            return false;

        if (toolType == typeof(MeleeWeapon))
        {
            if (item.GetType() != toolType)
                return false;

            bool isScythe = item.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase);
            return aux switch
            {
                "Scythe" or "ScytheOnly" => isScythe,
                _ => !isScythe
            };
        }

        return item.GetType() == toolType || anyTool && item is Axe or Pickaxe or Hoe;
    }


    private static bool MatchesToolSmartSwitch(Item? item, Type? type)
    {
        if (item is not Tool tool)
            return false;

        if (type is null)
            return tool.GetType() == typeof(MeleeWeapon) && ((MeleeWeapon)tool).isScythe();

        if (type == typeof(MeleeWeapon))
            return tool.GetType() == typeof(MeleeWeapon) && !((MeleeWeapon)tool).isScythe();

        return tool.GetType() == type;
    }

    private static bool MatchesItem(Item? item, string category, string itemName, string crops, int aux)
    {
        if (item is null)
            return false;

        return category switch
        {
            "Trash" or "Fertilizer" => item.Category == aux && !item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase),
            "Minerals" => item.Category == -15 && item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase) && item.Stack >= 5,
            "Cask" => item.Category == -26 && (item.Name.Contains("Cheese", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Wine", StringComparison.OrdinalIgnoreCase)),
            "Seed" => item.Category == -74 && !item.HasContextTag("tree_seed_item"),
            "Crops" => MatchesCrop(item, crops),
            "Dehydratable" => MatchesDehydratable(item, crops),
            "Oil Maker" => IsOilMakerIngredient(item),
            _ => item.Category == aux && item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesCrop(Item item, string crops)
    {
        bool canFruit = crops is "Both" or "Fruit";
        bool canVegetable = crops is "Both" or "Vegetable";
        return canFruit && item.Category == -79 || canVegetable && item.Category == -75;
    }

    private static bool MatchesDehydratable(Item item, string crops)
    {
        bool canFruit = crops is "Both" or "Fruit";
        bool canMushroom = crops is "Both" or "Mushroom";
        return canFruit && item.Category == -79 || canMushroom && item.Category == -81 && item.Name != "Red Mushroom";
    }

    private static bool IsOilMakerIngredient(Item item)
    {
        return item.Name == "Truffle" && item.Category == -17
            || item.Name == "Sunflower" && item.Category == -80
            || item.Name == "Sunflower Seeds" && item.Category == -74
            || item.Name == "Corn" && item.Category == -75;
    }

    private readonly record struct TemporarySlot(int Slot, int OriginalMaxItems, int OriginalItemCount, bool Inserted);

    private sealed class PendingProxyItem
    {
        public PendingProxyItem(long playerId, int previousSlot, int proxySlot, int originalMaxItems, int originalItemCount, bool insertedSlot, IList<Item?> pocketItems, int pocketSlot, Item originalPocketItem, Item proxyItem)
        {
            PlayerId = playerId;
            PreviousSlot = previousSlot;
            ProxySlot = proxySlot;
            OriginalMaxItems = originalMaxItems;
            OriginalItemCount = originalItemCount;
            InsertedSlot = insertedSlot;
            PocketItems = pocketItems;
            PocketSlot = pocketSlot;
            OriginalPocketItem = originalPocketItem;
            ProxyItem = proxyItem;
            StartingProxyStack = Math.Max(0, proxyItem.Stack);
        }

        public long PlayerId { get; }
        public int PreviousSlot { get; }
        public int ProxySlot { get; }
        public int OriginalMaxItems { get; }
        public int OriginalItemCount { get; }
        public bool InsertedSlot { get; }
        public IList<Item?> PocketItems { get; }
        public int PocketSlot { get; }
        public Item OriginalPocketItem { get; }
        public Item ProxyItem { get; }
        public int StartingProxyStack { get; }
        public int Ticks { get; set; }
    }

    private sealed class PocketInventory
    {
        public PocketInventory(IList<Item?> items)
        {
            Items = items;
        }

        public IList<Item?> Items { get; }
    }

    private interface IPocketProvider
    {
        IEnumerable<PocketInventory> GetPocketInventories(Farmer player);
    }

    private sealed class SingleItemList : IList<Item?>
    {
        private readonly Func<Item?> getter;
        private readonly Action<Item?> setter;

        public SingleItemList(Func<Item?> getter, Action<Item?> setter)
        {
            this.getter = getter;
            this.setter = setter;
        }

        public Item? this[int index]
        {
            get
            {
                ValidateIndex(index);
                return getter();
            }
            set
            {
                ValidateIndex(index);
                setter(value);
            }
        }

        public int Count => 1;
        public bool IsReadOnly => false;
        public void Add(Item? item) => throw new NotSupportedException();
        public void Clear() => setter(null);
        public bool Contains(Item? item) => ReferenceEquals(getter(), item);
        public void CopyTo(Item?[] array, int arrayIndex) => array[arrayIndex] = getter();
        public IEnumerator<Item?> GetEnumerator()
        {
            yield return getter();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
        public int IndexOf(Item? item) => ReferenceEquals(getter(), item) ? 0 : -1;
        public void Insert(int index, Item? item) => throw new NotSupportedException();
        public bool Remove(Item? item)
        {
            if (!ReferenceEquals(getter(), item))
                return false;

            setter(null);
            return true;
        }
        public void RemoveAt(int index)
        {
            ValidateIndex(index);
            setter(null);
        }

        private static void ValidateIndex(int index)
        {
            if (index != 0)
                throw new ArgumentOutOfRangeException(nameof(index));
        }
    }

    private sealed class UtilityBeltBridge : IPocketProvider
    {
        private readonly IMonitor monitor;
        private readonly FieldInfo pocketManagerField;
        private readonly MethodInfo getActivePocketsMethod;
        private readonly PropertyInfo pocketItemProperty;
        private object? modEntryInstance;

        private UtilityBeltBridge(IMonitor monitor, Type modEntryType, FieldInfo pocketManagerField, MethodInfo getActivePocketsMethod, PropertyInfo pocketItemProperty)
        {
            this.monitor = monitor;
            ModEntryType = modEntryType;
            this.pocketManagerField = pocketManagerField;
            this.getActivePocketsMethod = getActivePocketsMethod;
            this.pocketItemProperty = pocketItemProperty;
        }

        public Type ModEntryType { get; }

        public static UtilityBeltBridge? TryCreate(IMonitor monitor)
        {
            Type? modEntryType = AccessTools.TypeByName("UtilityBelt.ModEntry");
            Type? managerType = AccessTools.TypeByName("UtilityBelt.PocketManager");
            Type? stateType = AccessTools.TypeByName("UtilityBelt.PocketState");
            if (modEntryType is null || managerType is null || stateType is null)
                return null;

            FieldInfo? pocketManagerField = AccessTools.Field(modEntryType, "PocketManager");
            MethodInfo? getActivePockets = AccessTools.Method(managerType, "GetActivePockets");
            PropertyInfo? itemProperty = AccessTools.Property(stateType, "Item");
            if (pocketManagerField is null || getActivePockets is null || itemProperty is null)
                return null;

            return new UtilityBeltBridge(monitor, modEntryType, pocketManagerField, getActivePockets, itemProperty);
        }

        public void Capture(object instance)
        {
            modEntryInstance = instance;
        }

        public IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
        {
            if (modEntryInstance is null)
                yield break;

            object? manager = pocketManagerField.GetValue(modEntryInstance);
            if (manager is null)
                yield break;

            IEnumerable? pocketStates;
            try
            {
                pocketStates = getActivePocketsMethod.Invoke(manager, Array.Empty<object>()) as IEnumerable;
            }
            catch (Exception ex)
            {
                monitor.Log($"Could not read Utility Belt pockets while preparing tool swap compatibility: {ex.GetBaseException().Message}", LogLevel.Warn);
                yield break;
            }

            if (pocketStates is null)
                yield break;

            foreach (object pocketState in pocketStates)
            {
                yield return new PocketInventory(new SingleItemList(
                    () => pocketItemProperty.GetValue(pocketState) as Item,
                    item => pocketItemProperty.SetValue(pocketState, item)
                ));
            }
        }
    }

    private sealed class UtilityPocketBridge : IPocketProvider
    {
        private readonly FieldInfo pocketManagerField;
        private readonly FieldInfo pocketedItemField;
        private object? modEntryInstance;

        private UtilityPocketBridge(Type modEntryType, FieldInfo pocketManagerField, FieldInfo pocketedItemField)
        {
            ModEntryType = modEntryType;
            this.pocketManagerField = pocketManagerField;
            this.pocketedItemField = pocketedItemField;
        }

        public Type ModEntryType { get; }

        public static UtilityPocketBridge? TryCreate(IMonitor monitor)
        {
            Type? modEntryType = AccessTools.TypeByName("FoodPocket.ModEntry");
            Type? managerType = AccessTools.TypeByName("UtilityPocket.PocketManager");
            if (modEntryType is null || managerType is null)
                return null;

            FieldInfo? pocketManagerField = AccessTools.Field(modEntryType, "pocketManager");
            FieldInfo? pocketedItemField = AccessTools.Field(managerType, "pocketedItem");
            if (pocketManagerField is null || pocketedItemField is null)
                return null;

            return new UtilityPocketBridge(modEntryType, pocketManagerField, pocketedItemField);
        }

        public void Capture(object instance)
        {
            modEntryInstance = instance;
        }

        public IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
        {
            if (modEntryInstance is null)
                yield break;

            object? manager = pocketManagerField.GetValue(modEntryInstance);
            if (manager is null)
                yield break;

            yield return new PocketInventory(new SingleItemList(
                () => pocketedItemField.GetValue(manager) as Item,
                item => pocketedItemField.SetValue(manager, item)
            ));
        }
    }

    private sealed class PocketsBridge : IPocketProvider
    {
        private readonly IMonitor monitor;
        private readonly Type modEntryType;
        private readonly PropertyInfo pocketDictProperty;
        private readonly FieldInfo configField;
        private readonly FieldInfo inventoryDictField;
        private readonly FieldInfo defaultInventoryDictField;
        private readonly MethodInfo makeInventoryFromXmlMethod;

        private PocketsBridge(IMonitor monitor, Type modEntryType, PropertyInfo pocketDictProperty, FieldInfo configField, FieldInfo inventoryDictField, FieldInfo defaultInventoryDictField, MethodInfo makeInventoryFromXmlMethod)
        {
            this.monitor = monitor;
            this.modEntryType = modEntryType;
            this.pocketDictProperty = pocketDictProperty;
            this.configField = configField;
            this.inventoryDictField = inventoryDictField;
            this.defaultInventoryDictField = defaultInventoryDictField;
            this.makeInventoryFromXmlMethod = makeInventoryFromXmlMethod;
        }

        public static PocketsBridge? TryCreate(IMonitor monitor)
        {
            Type? type = AccessTools.TypeByName("Pockets.ModEntry");
            if (type is null)
                return null;

            PropertyInfo? pocketDict = AccessTools.Property(type, "PocketDict");
            FieldInfo? config = AccessTools.Field(type, "Config");
            FieldInfo? inventoryDict = AccessTools.Field(type, "InventoryDict");
            FieldInfo? defaultInventoryDict = AccessTools.Field(type, "DefaultInventoryDict");
            MethodInfo? makeInventoryFromXml = AccessTools.Method(type, "MakeInventoryFromXML");

            if (pocketDict is null || config is null || inventoryDict is null || defaultInventoryDict is null || makeInventoryFromXml is null)
                return null;

            return new PocketsBridge(monitor, type, pocketDict, config, inventoryDict, defaultInventoryDict, makeInventoryFromXml);
        }

        public IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
        {
            foreach (PocketSource source in GetPocketSources(player))
            {
                IDictionary? inventoryMap = GetOrCreateInventoryMap(source);
                if (inventoryMap is null)
                    continue;

                foreach (var definition in source.Definitions)
                {
                    if (!inventoryMap.Contains(definition.Id))
                        inventoryMap[definition.Id] = new Inventory();

                    if (inventoryMap[definition.Id] is not IList<Item?> inventory)
                        continue;

                    while (inventory.Count < definition.Slots)
                        inventory.Add(null);

                    yield return new PocketInventory(inventory);
                }
            }
        }

        private IEnumerable<PocketSource> GetPocketSources(Farmer player)
        {
            object? config = configField.GetValue(null);
            bool globalDefaultPockets = GetProperty<bool>(config, "GlobalDefaultPockets");
            List<PocketDefinition> defaultDefinitions = GetDefaultDefinitions(config).ToList();

            if (player.pantsItem.Value is Clothing pants)
            {
                List<PocketDefinition> definitions = GetDefinitionsForItem(pants).ToList();
                if (definitions.Count > 0)
                    yield return PocketSource.ForClothing(pants, definitions);
                else if (globalDefaultPockets)
                    yield return PocketSource.ForDefault(player.UniqueMultiplayerID, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
                else
                    yield return PocketSource.ForClothing(pants, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
            }

            if (player.shirtItem.Value is Clothing shirt)
            {
                List<PocketDefinition> definitions = GetDefinitionsForItem(shirt).ToList();
                if (definitions.Count > 0)
                    yield return PocketSource.ForClothing(shirt, definitions);
                else if (globalDefaultPockets)
                    yield return PocketSource.ForDefault(player.UniqueMultiplayerID, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.SHIRT).ToList());
                else
                    yield return PocketSource.ForClothing(shirt, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.SHIRT).ToList());
            }
        }

        private IEnumerable<PocketDefinition> GetDefinitionsForItem(Item item)
        {
            if (pocketDictProperty.GetValue(null) is not IDictionary pocketDict || !pocketDict.Contains(item.ItemId) || pocketDict[item.ItemId] is not IDictionary itemDefinitions)
                yield break;

            foreach (DictionaryEntry entry in itemDefinitions)
            {
                if (entry.Key is string id && TryReadDefinition(id, entry.Value, out PocketDefinition definition))
                    yield return definition;
            }
        }

        private IEnumerable<PocketDefinition> GetDefaultDefinitions(object? config)
        {
            object? defaults = GetPropertyObject(config, "DefaultPockets");
            if (defaults is not IDictionary defaultMap)
                yield break;

            foreach (DictionaryEntry entry in defaultMap)
            {
                if (entry.Key is string id && TryReadDefinition(id, entry.Value, out PocketDefinition definition))
                    yield return definition;
            }
        }

        private bool TryReadDefinition(string id, object? data, out PocketDefinition definition)
        {
            definition = default;
            if (data is null)
                return false;

            int slots = Math.Max(1, GetProperty<int>(data, "PocketSlots"));
            object? clothesTypeObject = GetPropertyObject(data, "ClothesType");
            Clothing.ClothesType clothesType = clothesTypeObject is Clothing.ClothesType typed ? typed : Clothing.ClothesType.PANTS;
            definition = new PocketDefinition(id, clothesType, slots);
            return true;
        }

        private IDictionary? GetOrCreateInventoryMap(PocketSource source)
        {
            IDictionary? root = source.ClothingItem is not null
                ? inventoryDictField.GetValue(null) as IDictionary
                : defaultInventoryDictField.GetValue(null) as IDictionary;

            if (root is null)
                return null;

            object key = source.ClothingItem is not null
                ? source.ClothingItem
                : source.PlayerId;
            if (!root.Contains(key))
                root[key] = LoadInventoryMap(source);

            return root[key] as IDictionary;
        }

        private IDictionary LoadInventoryMap(PocketSource source)
        {
            Dictionary<string, Inventory> map = new();
            string? serialized = null;

            if (source.ClothingItem is not null)
                source.ClothingItem.modData.TryGetValue(PocketsModDataKey, out serialized);
            else
            {
                Farmer? player = Game1.GetPlayer(source.PlayerId);
                if (player is not null)
                    player.modData.TryGetValue(PocketsModDataKey, out serialized);
            }

            if (!string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    Dictionary<string, string>? xmlByPocket = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
                    if (xmlByPocket is not null)
                    {
                        foreach (KeyValuePair<string, string> pair in xmlByPocket)
                        {
                            int max = source.Definitions.FirstOrDefault(def => def.Id == pair.Key).Slots;
                            if (max <= 0)
                                max = 999;

                            if (makeInventoryFromXmlMethod.Invoke(null, new object[] { pair.Value, max }) is Inventory inventory)
                                map[pair.Key] = inventory;
                        }
                    }
                }
                catch (Exception ex)
                {
                    monitor.Log($"Could not read a Pockets inventory while preparing tool swap compatibility: {ex.Message}", LogLevel.Warn);
                }
            }

            return map;
        }

        private static T GetProperty<T>(object? instance, string name)
        {
            object? value = GetPropertyObject(instance, name);
            return value is T typed ? typed : default!;
        }

        private static object? GetPropertyObject(object? instance, string name)
        {
            return instance?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        }

        private readonly record struct PocketDefinition(string Id, Clothing.ClothesType ClothesType, int Slots);

        private sealed class PocketSource
        {
            private PocketSource(Item? clothingItem, long playerId, List<PocketDefinition> definitions)
            {
                ClothingItem = clothingItem;
                PlayerId = playerId;
                Definitions = definitions;
            }

            public Item? ClothingItem { get; }
            public long PlayerId { get; }
            public List<PocketDefinition> Definitions { get; }

            public static PocketSource ForClothing(Item clothingItem, List<PocketDefinition> definitions)
            {
                return new PocketSource(clothingItem, 0, definitions);
            }

            public static PocketSource ForDefault(long playerId, List<PocketDefinition> definitions)
            {
                return new PocketSource(null, playerId, definitions);
            }
        }
    }
}
