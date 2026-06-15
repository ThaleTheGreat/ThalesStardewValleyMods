using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Powers;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using xTile.Dimensions;
using Object = StardewValley.Object;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace ThaleTheGreat.WalletTools;

public sealed class ModEntry : Mod
{
    private const string SaveDataKey = "WalletTools.ToolStates";
    private const string FlagPrefix = "ThaleTheGreat.WalletTools/Has";
    private const string PowerPrefix = "ThaleTheGreat.WalletTools_";
    private const string ToolTexturePath = "TileSheets/tools";
    private const string PowerCategoryIconTexturePath = "Mods/ThaleTheGreat.WalletTools/Powers";
    private const string RuntimeToolMarker = "ThaleTheGreat.WalletTools/RuntimeTool";
    private const string RuntimeToolKindMarker = "ThaleTheGreat.WalletTools/RuntimeToolKind";
    private const string OvernightExposureMarker = "ThaleTheGreat.WalletTools/OvernightExposure";
    private const string OvernightExposureKindMarker = "ThaleTheGreat.WalletTools/OvernightExposureKind";
    private const string MenuVirtualToolMarker = "ThaleTheGreat.WalletTools/MenuVirtualTool";
    private const string MenuVirtualToolKindMarker = "ThaleTheGreat.WalletTools/MenuVirtualToolKind";
    private const string MenuVirtualToolPurposeMarker = "ThaleTheGreat.WalletTools/MenuVirtualToolPurpose";
    private const string OwnerPlayerIdMarker = "ThaleTheGreat.WalletTools/OwnerPlayerId";

    private static ModEntry? Instance;

    private ModConfig Config = new();
    private readonly Dictionary<long, Dictionary<WalletToolKind, WalletToolState>> StoredToolsByPlayer = new();
    private Dictionary<WalletToolKind, WalletToolState> StoredTools => GetStoredTools(Game1.player);
    private readonly PerScreen<TemporaryToolUse?> PendingToolUseScreen = new();
    private TemporaryToolUse? PendingToolUse
    {
        get => PendingToolUseScreen.Value;
        set => PendingToolUseScreen.Value = value;
    }

    private readonly PerScreen<SButton?> PendingManualUseHotkeyScreen = new();
    private SButton? PendingManualUseHotkey
    {
        get => PendingManualUseHotkeyScreen.Value;
        set => PendingManualUseHotkeyScreen.Value = value;
    }

    private Harmony Harmony = null!;
    private readonly PerScreen<bool> SuppressInventoryConversionScreen = new();
    private bool SuppressInventoryConversion
    {
        get => SuppressInventoryConversionScreen.Value;
        set => SuppressInventoryConversionScreen.Value = value;
    }

    private readonly PerScreen<bool> PendingInventoryConversionScreen = new();
    private bool PendingInventoryConversion
    {
        get => PendingInventoryConversionScreen.Value;
        set => PendingInventoryConversionScreen.Value = value;
    }

    private readonly PerScreen<bool> MayHaveMenuVirtualToolsInInventoryScreen = new();
    private bool MayHaveMenuVirtualToolsInInventory
    {
        get => MayHaveMenuVirtualToolsInInventoryScreen.Value;
        set => MayHaveMenuVirtualToolsInInventoryScreen.Value = value;
    }

    private bool GmcmRegistered;
    private IGenericModConfigMenuApi? GmcmApi;
    private IMobilePhoneApi? MobilePhoneApi;
    private ISpecialPowerAPI? SpecialPowerApi;
    private bool GmcmMissingLogged;
    private bool ToolSmartSwitchPatched;
    private bool ToolSmartSwitchPatchAttempted;
    private bool AutomateToolSwapPatched;
    private bool AutomateToolSwapPatchAttempted;
    private readonly Dictionary<long, Dictionary<WalletToolKind, Tool>> ToolSmartSwitchToolCacheByPlayer = new();
    private readonly PerScreen<List<BlacksmithExposure>> ActiveBlacksmithExposuresScreen = new(() => new List<BlacksmithExposure>());
    private List<BlacksmithExposure> ActiveBlacksmithExposures => ActiveBlacksmithExposuresScreen.Value;

    private readonly PerScreen<object?> PatchedBlacksmithMenuScreen = new();
    private object? PatchedBlacksmithMenu
    {
        get => PatchedBlacksmithMenuScreen.Value;
        set => PatchedBlacksmithMenuScreen.Value = value;
    }

    private readonly PerScreen<object?> ActiveForgeMenuScreen = new();
    private object? ActiveForgeMenu
    {
        get => ActiveForgeMenuScreen.Value;
        set => ActiveForgeMenuScreen.Value = value;
    }


    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.Saved += OnSaved;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;

        Harmony = new Harmony(ModManifest.UniqueID);
        PatchCoreGameMethods();
        PatchBlacksmithToolUpgradeFlow();
    }


    private Dictionary<WalletToolKind, WalletToolState> GetStoredTools(Farmer? player)
    {
        long playerId = GetWalletOwnerId(player);
        if (!StoredToolsByPlayer.TryGetValue(playerId, out Dictionary<WalletToolKind, WalletToolState>? tools))
        {
            tools = new Dictionary<WalletToolKind, WalletToolState>();
            StoredToolsByPlayer[playerId] = tools;
        }

        return tools;
    }

    private Dictionary<WalletToolKind, Tool> GetToolSmartSwitchToolCache(Farmer? player)
    {
        long playerId = GetWalletOwnerId(player);
        if (!ToolSmartSwitchToolCacheByPlayer.TryGetValue(playerId, out Dictionary<WalletToolKind, Tool>? cache))
        {
            cache = new Dictionary<WalletToolKind, Tool>();
            ToolSmartSwitchToolCacheByPlayer[playerId] = cache;
        }

        return cache;
    }

    private void RemoveToolSmartSwitchCachedKind(WalletToolKind kind)
    {
        foreach (Dictionary<WalletToolKind, Tool> cache in ToolSmartSwitchToolCacheByPlayer.Values)
            cache.Remove(kind);
    }

    private static long GetWalletOwnerId(Farmer? player)
    {
        return player?.UniqueMultiplayerID ?? 0L;
    }

    private static string GetWalletOwnerIdString(Farmer? player)
    {
        return GetWalletOwnerId(player).ToString();
    }

    private static bool IsOwnedByPlayer(Tool tool, Farmer player)
    {
        if (!tool.modData.TryGetValue(OwnerPlayerIdMarker, out string? rawOwnerId))
            return !Context.IsMultiplayer;

        return string.Equals(rawOwnerId, GetWalletOwnerIdString(player), StringComparison.Ordinal);
    }

    private static bool HasOwnerMarker(Tool tool)
    {
        return tool.modData.ContainsKey(OwnerPlayerIdMarker);
    }

    private static void MarkWalletOwner(Tool tool, Farmer player)
    {
        tool.modData[OwnerPlayerIdMarker] = GetWalletOwnerIdString(player);
    }

    private static void ClearWalletOwner(Tool tool)
    {
        tool.modData.Remove(OwnerPlayerIdMarker);
    }

    public override object GetApi()
    {
        return new WalletToolsApi(this);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(PowerCategoryIconTexturePath))
        {
            e.LoadFrom(() => LoadEmbeddedTexture("Powers.png"), AssetLoadPriority.Exclusive);
            return;
        }

        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers") || !Config.ModEnabled || !Context.IsWorldReady)
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            AddPower(powers, WalletToolKind.Axe);
            AddPower(powers, WalletToolKind.Pickaxe);
            AddPower(powers, WalletToolKind.Hoe);
            AddPower(powers, WalletToolKind.WateringCan);
            AddPower(powers, WalletToolKind.Pan);
            AddPower(powers, WalletToolKind.MilkPail);
            AddPower(powers, WalletToolKind.Shears);
        });
    }

    private void AddPower(IDictionary<string, PowersData> powers, WalletToolKind kind)
    {
        if (!IsWalletEnabled(kind))
            return;

        WalletToolState state = TryGetWalletDisplayState(kind, out WalletToolState storedState)
            ? storedState
            : CreateLockedPowerDisplayState(kind);

        powers[$"{PowerPrefix}{kind}"] = new PowersData
        {
            DisplayName = state.GetDisplayName(),
            Description = state.GetDescription(),
            TexturePath = state.GetTexturePath(),
            TexturePosition = state.GetTexturePosition(),
            UnlockedCondition = $"PLAYER_MOD_DATA Current {FlagPrefix}{kind} true"
        };
    }

    private static WalletToolState CreateLockedPowerDisplayState(WalletToolKind kind)
    {
        string name = GetFallbackDisplayName(kind);
        return new WalletToolState
        {
            Kind = kind,
            Name = name,
            DisplayName = name,
            UpgradeLevel = 0,
            TexturePath = ToolTexturePath,
            MenuSpriteIndex = -1
        };
    }

    private static string GetFallbackDisplayName(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => "Axe",
            WalletToolKind.Pickaxe => "Pickaxe",
            WalletToolKind.Hoe => "Hoe",
            WalletToolKind.WateringCan => "Watering Can",
            WalletToolKind.Pan => "Pan",
            WalletToolKind.MilkPail => "Milk Pail",
            WalletToolKind.Shears => "Shears",
            _ => "Tool"
        };
    }

    private static Point GetFallbackTexturePosition(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => new Point(64, 32),
            WalletToolKind.Pickaxe => new Point(0, 32),
            WalletToolKind.Hoe => new Point(32, 32),
            WalletToolKind.WateringCan => new Point(16, 32),
            WalletToolKind.Pan => new Point(192, 0),
            WalletToolKind.MilkPail => new Point(128, 0),
            WalletToolKind.Shears => new Point(176, 0),
            _ => new Point(64, 32)
        };
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        RegisterMobilePhoneApp();
        RegisterSpecialPowerUtilities();
        PatchToolSmartSwitch();
        PatchAutomateToolSwap();
        WarnIfNoSwitchModLoaded();
    }

    private void PatchCoreGameMethods()
    {
        MethodInfo? keysDownPostfix = AccessTools.Method(typeof(IsOneOfTheseKeysDownPatch), nameof(IsOneOfTheseKeysDownPatch.Postfix));
        if (keysDownPostfix is not null)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Game1)).Where(method => method.Name == nameof(Game1.isOneOfTheseKeysDown)))
                Harmony.Patch(method, postfix: new HarmonyMethod(keysDownPostfix));
        }

        MethodInfo? keysUpPostfix = AccessTools.Method(typeof(AreAllOfTheseKeysUpPatch), nameof(AreAllOfTheseKeysUpPatch.Postfix));
        if (keysUpPostfix is not null)
        {
            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(typeof(Game1)).Where(method => method.Name == nameof(Game1.areAllOfTheseKeysUp)))
                Harmony.Patch(method, postfix: new HarmonyMethod(keysUpPostfix));
        }

        MethodInfo? missingTool = AccessTools.Method(typeof(Game1), "checkIsMissingTool");
        MethodInfo? missingToolPostfix = AccessTools.Method(typeof(CheckIsMissingToolPatch), nameof(CheckIsMissingToolPatch.Postfix));
        if (missingTool is not null && missingToolPostfix is not null)
            Harmony.Patch(missingTool, postfix: new HarmonyMethod(missingToolPostfix));

        MethodInfo? pressUseTool = AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton));
        MethodInfo? pressUseToolPrefix = AccessTools.Method(typeof(PressUseToolButtonPatch), nameof(PressUseToolButtonPatch.Prefix));
        if (pressUseTool is not null && pressUseToolPrefix is not null)
            Harmony.Patch(pressUseTool, prefix: new HarmonyMethod(pressUseToolPrefix));
    }

    private void PatchBlacksmithToolUpgradeFlow()
    {
        MethodInfo? prefix = AccessTools.Method(typeof(ModEntry), nameof(BlacksmithToolFlowPrefix));
        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(BlacksmithToolFlowPostfix));
        if (prefix is null || postfix is null)
            return;

        PatchNamedMethods(typeof(GameLocation), "performAction", prefix, postfix);
        PatchNamedMethods(typeof(GameLocation), "answerDialogueAction", prefix, postfix);
        PatchNamedMethods(typeof(GameLocation), "answerDialogue", prefix, postfix);
    }

    private static bool IsSupportedBlacksmithActionMethod(MethodInfo method)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (method.Name == "performAction")
            return parameters.Length >= 2 && parameters[0].ParameterType == typeof(string[]);

        if (method.Name == "answerDialogueAction")
            return parameters.Length >= 2 && parameters[0].ParameterType == typeof(string);

        if (method.Name == "answerDialogue")
            return parameters.Length >= 1 && parameters[0].ParameterType == typeof(Response);

        return false;
    }

    private void PatchNamedMethods(Type type, string methodName, MethodInfo prefix, MethodInfo postfix)
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type).Where(method => method.Name == methodName && IsSupportedBlacksmithActionMethod(method)))
            Harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
    }

    private static void BlacksmithToolFlowPrefix()
    {
        Instance?.PrepareForBlacksmithToolFlow(Game1.player);
    }

    private static void BlacksmithToolFlowPostfix()
    {
        Instance?.FinishBlacksmithToolFlow(Game1.player);
    }

    private void PatchToolSmartSwitch()
    {
        if (ToolSmartSwitchPatched || ToolSmartSwitchPatchAttempted)
            return;

        ToolSmartSwitchPatchAttempted = true;

        Type? modEntryType = AccessTools.TypeByName("ToolSmartSwitch.ModEntry");
        if (modEntryType is null)
            return;

        MethodInfo? getTools = AccessTools.Method(modEntryType, "GetTools");
        MethodInfo? switchTool = AccessTools.Method(modEntryType, "SwitchTool");
        MethodInfo? getToolsPostfix = AccessTools.Method(typeof(ModEntry), nameof(ToolSmartSwitchGetToolsPostfix));
        MethodInfo? switchToolPrefix = AccessTools.Method(typeof(ModEntry), nameof(ToolSmartSwitchSwitchToolPrefix));

        if (getTools is null || switchTool is null || getToolsPostfix is null || switchToolPrefix is null)
            return;

        Harmony.Patch(getTools, postfix: new HarmonyMethod(getToolsPostfix));
        Harmony.Patch(switchTool, prefix: new HarmonyMethod(switchToolPrefix));
        ToolSmartSwitchPatched = true;
    }

    private void PatchAutomateToolSwap()
    {
        if (AutomateToolSwapPatched || AutomateToolSwapPatchAttempted)
            return;

        AutomateToolSwapPatchAttempted = true;

        Type? inventoryHandler = AccessTools.TypeByName("Core.InventoryHandler");
        if (inventoryHandler is null)
            return;

        MethodInfo? setTool = AccessTools.Method(
            inventoryHandler,
            "SetTool",
            new[] { typeof(Farmer), typeof(Type), typeof(string), typeof(bool) }
        );
        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(AutomateToolSwapSetToolPostfix));

        if (setTool is null || postfix is null)
            return;

        Harmony.Patch(setTool, postfix: new HarmonyMethod(postfix));
        AutomateToolSwapPatched = true;
    }

    private void WarnIfNoSwitchModLoaded()
    {
        if (ToolSmartSwitchPatched || AutomateToolSwapPatched)
            return;

    }

    private static void AutomateToolSwapSetToolPostfix(Farmer player, Type toolType, string aux = "", bool anyTool = false)
    {
        Instance?.TrySupplyRequestedWalletTool(player, toolType, anyTool);
    }

    private static void ToolSmartSwitchGetToolsPostfix(Farmer f, ref Dictionary<int, Tool> __result)
    {
        Instance?.AddWalletToolsToToolSmartSwitch(f, __result);
    }

    private static bool ToolSmartSwitchSwitchToolPrefix(Farmer f, int which)
    {
        return Instance?.TrySwitchToolSmartSwitchWalletTool(f, which) != true;
    }

    private void RegisterMobilePhoneApp()
    {
        MobilePhoneApi = Helper.ModRegistry.GetApi<IMobilePhoneApi>("aedenthorn.MobilePhone");
        if (MobilePhoneApi is null)
        {
            return;
        }

        try
        {
            Texture2D icon = LoadEmbeddedTexture("MobilePhone.png");
            MobilePhoneApi.AddApp(ModManifest.UniqueID, "Wallet Tools", OpenConfigFromMobilePhone, icon);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools Mobile Phone app: {ex.Message}", LogLevel.Warn);
        }
    }

    private void RegisterSpecialPowerUtilities()
    {
        SpecialPowerApi = Helper.ModRegistry.GetApi<ISpecialPowerAPI>("Spiderbuttons.SpecialPowerUtilities");
        if (SpecialPowerApi is null)
        {
            return;
        }

        try
        {
            SpecialPowerApi.RegisterPowerCategory(ModManifest.UniqueID, () => "Wallet Tools", PowerCategoryIconTexturePath);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools Special Power Utilities category: {ex.Message}", LogLevel.Warn);
        }
    }

    private static Texture2D LoadEmbeddedTexture(string fileName)
    {
        using MemoryStream stream = new(EmbeddedAssets.GetBytes(fileName));
        return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
    }

    private void OpenConfigFromMobilePhone()
    {
        MobilePhoneApi?.SetPhoneOpened(false);
        MobilePhoneApi?.SetAppRunning(false);

        GmcmApi ??= Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (GmcmApi is null)
        {
            Game1.addHUDMessage(new HUDMessage("Generic Mod Config Menu is not installed.", HUDMessage.error_type));
            return;
        }

        GmcmApi.OpenModMenu(ModManifest);
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        GmcmApi = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (GmcmApi is null)
        {
            if (!GmcmMissingLogged)
            {
                Monitor.Log("Generic Mod Config Menu was not detected. Wallet Tools settings remain available through config.json.", LogLevel.Warn);
                GmcmMissingLogged = true;
            }
            return;
        }

        try
        {
            GmcmApi.Unregister(ModManifest);
            GmcmApi.Register(ModManifest, reset: () => { Config = new ModConfig(); ApplyConfigVisibilityState(); }, save: () => { ApplyConfigVisibilityState(); Helper.WriteConfig(Config); });

            AddBool(GmcmApi, nameof(Config.ModEnabled), () => Config.ModEnabled, SetModEnabled, "Mod Enabled", "Enables or disables Wallet Tools.");
            AddBool(GmcmApi, nameof(Config.PlayToolSwapSound), () => Config.PlayToolSwapSound, value => Config.PlayToolSwapSound = value, "Play Tool Swap Sound", "Play the Wallet Tools tool swap sound when a wallet tool is readied for use.");
            AddBool(GmcmApi, nameof(Config.AxeEnabled), () => Config.AxeEnabled, value => SetToolEnabled(WalletToolKind.Axe, value), "Wallet Axe", "Store and use the axe from the wallet.");
            AddBool(GmcmApi, nameof(Config.PickaxeEnabled), () => Config.PickaxeEnabled, value => SetToolEnabled(WalletToolKind.Pickaxe, value), "Wallet Pickaxe", "Store and use the pickaxe from the wallet.");
            AddBool(GmcmApi, nameof(Config.HoeEnabled), () => Config.HoeEnabled, value => SetToolEnabled(WalletToolKind.Hoe, value), "Wallet Hoe", "Store and use the hoe from the wallet.");
            AddBool(GmcmApi, nameof(Config.WateringCanEnabled), () => Config.WateringCanEnabled, value => SetToolEnabled(WalletToolKind.WateringCan, value), "Wallet Watering Can", "Store and use the watering can from the wallet.");
            AddBool(GmcmApi, nameof(Config.PanEnabled), () => Config.PanEnabled, value => SetToolEnabled(WalletToolKind.Pan, value), "Wallet Pan", "Store and use the pan from the wallet.");
            AddBool(GmcmApi, nameof(Config.MilkPailEnabled), () => Config.MilkPailEnabled, value => SetToolEnabled(WalletToolKind.MilkPail, value), "Wallet Milk Pail", "Store and use the milk pail from the wallet.");
            AddBool(GmcmApi, nameof(Config.ShearsEnabled), () => Config.ShearsEnabled, value => SetToolEnabled(WalletToolKind.Shears, value), "Wallet Shears", "Store and use the shears from the wallet.");

            AddBool(GmcmApi, nameof(Config.AutoUseEnabled), () => Config.AutoUseEnabled, value => SetAutoUseMasterEnabled(value, showMessage: false), "Auto Use Enabled", "Master toggle for automatic Wallet Tools logic. Manual wallet hotkeys still work.");
            AddKeybind(GmcmApi, nameof(Config.ToggleAutoUseHotkey), () => Config.ToggleAutoUseHotkey, value => Config.ToggleAutoUseHotkey = value, "Toggle Auto Use", "Hotkey to toggle automatic Wallet Tools logic on or off.");
            AddBool(GmcmApi, nameof(Config.AxeAutoUseEnabled), () => Config.AxeAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.Axe, value), "Auto Axe", "Allows automatic axe supply. Manual axe hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.PickaxeAutoUseEnabled), () => Config.PickaxeAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.Pickaxe, value), "Auto Pickaxe", "Allows automatic pickaxe supply. Manual pickaxe hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.HoeAutoUseEnabled), () => Config.HoeAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.Hoe, value), "Auto Hoe", "Allows automatic hoe supply. Manual hoe hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.WateringCanAutoUseEnabled), () => Config.WateringCanAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.WateringCan, value), "Auto Watering Can", "Allows automatic watering can supply. Manual watering can hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.PanAutoUseEnabled), () => Config.PanAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.Pan, value), "Auto Pan", "Allows automatic pan supply. Manual pan hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.MilkPailAutoUseEnabled), () => Config.MilkPailAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.MilkPail, value), "Auto Milk Pail", "Allows automatic milk pail supply. Manual milk pail hotkey still works when this is off.");
            AddBool(GmcmApi, nameof(Config.ShearsAutoUseEnabled), () => Config.ShearsAutoUseEnabled, value => SetToolAutoUseEnabled(WalletToolKind.Shears, value), "Auto Shears", "Allows automatic shears supply. Manual shears hotkey still works when this is off.");

            AddKeybind(GmcmApi, nameof(Config.UseAxeHotkey), () => Config.UseAxeHotkey, value => Config.UseAxeHotkey = value, "Use Axe", "Immediately use the wallet axe.");
            AddKeybind(GmcmApi, nameof(Config.UsePickaxeHotkey), () => Config.UsePickaxeHotkey, value => Config.UsePickaxeHotkey = value, "Use Pickaxe", "Immediately use the wallet pickaxe.");
            AddKeybind(GmcmApi, nameof(Config.UseHoeHotkey), () => Config.UseHoeHotkey, value => Config.UseHoeHotkey = value, "Use Hoe", "Immediately use the wallet hoe.");
            AddKeybind(GmcmApi, nameof(Config.UseWateringCanHotkey), () => Config.UseWateringCanHotkey, value => Config.UseWateringCanHotkey = value, "Use Watering Can", "Immediately use the wallet watering can.");
            AddKeybind(GmcmApi, nameof(Config.UsePanHotkey), () => Config.UsePanHotkey, value => Config.UsePanHotkey = value, "Use Pan", "Immediately use the wallet pan.");
            AddKeybind(GmcmApi, nameof(Config.UseMilkPailHotkey), () => Config.UseMilkPailHotkey, value => Config.UseMilkPailHotkey = value, "Use Milk Pail", "Immediately use the wallet milk pail.");
            AddKeybind(GmcmApi, nameof(Config.UseShearsHotkey), () => Config.UseShearsHotkey, value => Config.UseShearsHotkey = value, "Use Shears", "Immediately use the wallet shears.");


            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools options with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
    }


    private void ApplyConfigVisibilityState()
    {
        if (!Context.IsWorldReady)
            return;

        if (!Config.ModEnabled)
        {
            DisableWalletTools(Game1.player, "config save/reset");
            return;
        }

        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
        {
            if (!IsWalletEnabled(kind))
                MaterializeStoredToolToInventory(Game1.player, kind, "config save/reset");
        }

        ReconcileLoadedToolState(Game1.player, "config save/reset");
        ConvertInventoryTools(Game1.player);
        UpdateWalletFlags(Game1.player);
        InvalidatePowers();
    }

    private void SetModEnabled(bool enabled)
    {
        if (Config.ModEnabled == enabled)
            return;

        bool wasEnabled = Config.ModEnabled;
        Config.ModEnabled = enabled;

        if (!Context.IsWorldReady)
            return;

        if (!enabled)
        {
            if (wasEnabled)
                DisableWalletTools(Game1.player, "GMCM disable");
            else
                ClearStoredWalletState();
            return;
        }

        LoadStoredTools();
        CollectOvernightExposedTools(Game1.player, "mod enabled");
        ReconcileLoadedToolState(Game1.player, "mod enabled");
        ConvertInventoryTools(Game1.player);
    }

    private void SetToolEnabled(WalletToolKind kind, bool enabled)
    {
        if (GetToolEnabled(kind) == enabled)
            return;

        SetToolEnabledValue(kind, enabled);

        if (!Context.IsWorldReady)
            return;

        if (!Config.ModEnabled)
        {
            UpdateWalletFlags(Game1.player);
            InvalidatePowers();
            return;
        }

        if (enabled)
            EnableWalletTool(Game1.player, kind);
        else
            DisableWalletTool(Game1.player, kind);
    }

    private void EnableWalletTool(Farmer player, WalletToolKind kind)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);

        CollectOvernightExposedTools(player, $"{GetFallbackDisplayName(kind)} enabled");
        ReconcileLoadedToolState(player, $"{GetFallbackDisplayName(kind)} enabled");
        ConvertInventoryTools(player);
    }

    private void DisableWalletTool(Farmer player, WalletToolKind kind)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);

        bool returnedTool = MaterializeStoredToolToInventory(player, kind, $"{GetFallbackDisplayName(kind)} disabled");

        RemoveRuntimeToolCopies(player);
        RemoveToolSmartSwitchCachedKind(kind);
        UpdateWalletFlags(player);
        InvalidatePowers();

        if (returnedTool)
            Game1.addHUDMessage(new HUDMessage($"{GetFallbackDisplayName(kind)} returned to inventory.", HUDMessage.newQuest_type));
    }

    private bool MaterializeStoredToolToInventory(Farmer player, WalletToolKind kind, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (!storedTools.TryGetValue(kind, out WalletToolState? state))
            return false;

        Tool? tool = state.CreateTool(Monitor);
        storedTools.Remove(kind);

        if (tool is null || WalletToolState.IsErrorTool(tool))
        {
            RefreshWalletStateAfterStoredToolChange(player);
            return false;
        }

        SuppressInventoryConversion = true;
        try
        {
            ClearWalletMarkers(tool);
            AddToolToInventoryOrOverflowMenu(player, tool);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        RefreshWalletStateAfterStoredToolChange(player);
        return true;
    }

    private bool GetToolEnabled(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => Config.AxeEnabled,
            WalletToolKind.Pickaxe => Config.PickaxeEnabled,
            WalletToolKind.Hoe => Config.HoeEnabled,
            WalletToolKind.WateringCan => Config.WateringCanEnabled,
            WalletToolKind.Pan => Config.PanEnabled,
            WalletToolKind.MilkPail => Config.MilkPailEnabled,
            WalletToolKind.Shears => Config.ShearsEnabled,
            _ => false
        };
    }

    private void SetToolEnabledValue(WalletToolKind kind, bool enabled)
    {
        switch (kind)
        {
            case WalletToolKind.Axe:
                Config.AxeEnabled = enabled;
                break;
            case WalletToolKind.Pickaxe:
                Config.PickaxeEnabled = enabled;
                break;
            case WalletToolKind.Hoe:
                Config.HoeEnabled = enabled;
                break;
            case WalletToolKind.WateringCan:
                Config.WateringCanEnabled = enabled;
                break;
            case WalletToolKind.Pan:
                Config.PanEnabled = enabled;
                break;
            case WalletToolKind.MilkPail:
                Config.MilkPailEnabled = enabled;
                break;
            case WalletToolKind.Shears:
                Config.ShearsEnabled = enabled;
                break;
        }
    }

    private void SetAutoUseMasterEnabled(bool enabled, bool showMessage)
    {
        if (Config.AutoUseEnabled == enabled)
            return;

        Config.AutoUseEnabled = enabled;
        ToolSmartSwitchToolCacheByPlayer.Clear();

        if (Context.IsWorldReady)
        {
            if (!enabled && PendingToolUse is not null)
                RestoreTemporaryTool(Game1.player);

            if (showMessage)
                Game1.addHUDMessage(new HUDMessage(enabled ? "Wallet Tools auto use enabled." : "Wallet Tools auto use disabled.", HUDMessage.newQuest_type));
        }

        Helper.WriteConfig(Config);
    }

    private bool IsAutoUseEnabled(WalletToolKind kind)
    {
        return IsWalletEnabled(kind) && Config.AutoUseEnabled && GetToolAutoUseEnabled(kind);
    }

    private bool GetToolAutoUseEnabled(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => Config.AxeAutoUseEnabled,
            WalletToolKind.Pickaxe => Config.PickaxeAutoUseEnabled,
            WalletToolKind.Hoe => Config.HoeAutoUseEnabled,
            WalletToolKind.WateringCan => Config.WateringCanAutoUseEnabled,
            WalletToolKind.Pan => Config.PanAutoUseEnabled,
            WalletToolKind.MilkPail => Config.MilkPailAutoUseEnabled,
            WalletToolKind.Shears => Config.ShearsAutoUseEnabled,
            _ => false
        };
    }

    private void SetToolAutoUseEnabled(WalletToolKind kind, bool enabled)
    {
        if (GetToolAutoUseEnabled(kind) == enabled)
            return;

        SetToolAutoUseEnabledValue(kind, enabled);
        RemoveToolSmartSwitchCachedKind(kind);

        if (Context.IsWorldReady && !enabled && PendingToolUse?.Kind == kind)
            RestoreTemporaryTool(Game1.player);

        Helper.WriteConfig(Config);
    }

    private void SetToolAutoUseEnabledValue(WalletToolKind kind, bool enabled)
    {
        switch (kind)
        {
            case WalletToolKind.Axe:
                Config.AxeAutoUseEnabled = enabled;
                break;
            case WalletToolKind.Pickaxe:
                Config.PickaxeAutoUseEnabled = enabled;
                break;
            case WalletToolKind.Hoe:
                Config.HoeAutoUseEnabled = enabled;
                break;
            case WalletToolKind.WateringCan:
                Config.WateringCanAutoUseEnabled = enabled;
                break;
            case WalletToolKind.Pan:
                Config.PanAutoUseEnabled = enabled;
                break;
            case WalletToolKind.MilkPail:
                Config.MilkPailAutoUseEnabled = enabled;
                break;
            case WalletToolKind.Shears:
                Config.ShearsAutoUseEnabled = enabled;
                break;
        }
    }

    private void DisableWalletTools(Farmer player, string reason)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);

        bool returnedTools = MaterializeStoredToolsToInventory(player, markForOvernight: false, clearStoredState: true, useOverflowMenu: true, reason: reason);

        RemoveRuntimeToolCopies(player);
        ClearWalletMarkersFromInventory(player);
        GetStoredTools(player).Clear();
        ToolSmartSwitchToolCacheByPlayer.Clear();
        ClearLegacyStoredToolsData();
        UpdateWalletFlags(player);
        InvalidatePowers();

        if (returnedTools)
            Game1.addHUDMessage(new HUDMessage("Wallet tools returned to inventory.", HUDMessage.newQuest_type));
    }

    private void ClearStoredWalletState()
    {
        if (Context.IsWorldReady && PendingToolUse is not null)
            RestoreTemporaryTool(Game1.player);

        if (Context.IsWorldReady)
        {
            RemoveRuntimeToolCopies(Game1.player);
            ClearWalletMarkersFromInventory(Game1.player);
        }

        GetStoredTools(Game1.player).Clear();
        ToolSmartSwitchToolCacheByPlayer.Clear();
        ClearLegacyStoredToolsData();
        if (Context.IsWorldReady)
            UpdateWalletFlags(Game1.player);
        InvalidatePowers();
    }

    private void AddBool(IGenericModConfigMenuApi gmcm, string fieldId, Func<bool> getValue, Action<bool> setValue, string name, string tooltip)
    {
        gmcm.AddBoolOption(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void AddKeybind(IGenericModConfigMenuApi gmcm, string fieldId, Func<StardewModdingAPI.Utilities.KeybindList> getValue, Action<StardewModdingAPI.Utilities.KeybindList> setValue, string name, string tooltip)
    {
        gmcm.AddKeybindList(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ClearVolatileWalletState(invalidatePowers: false);
        if (!Config.ModEnabled)
        {
            ClearStoredWalletState();
            return;
        }

        LoadStoredTools();
        CollectOvernightExposedTools(Game1.player, "save load");
        ReconcileLoadedToolState(Game1.player, "save load");
        ConvertInventoryTools(Game1.player);
    }


    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        ClearVolatileWalletState();
    }

    private void ClearVolatileWalletState(bool invalidatePowers = true)
    {
        if (Context.IsWorldReady)
            RestorePendingToolUseToInventoryBeforeVolatileClear(Game1.player);

        StoredToolsByPlayer.Clear();
        ToolSmartSwitchToolCacheByPlayer.Clear();
        ActiveBlacksmithExposures.Clear();
        PendingToolUse = null;
        PendingManualUseHotkey = null;
        PendingInventoryConversion = false;
        MayHaveMenuVirtualToolsInInventory = false;
        SuppressInventoryConversion = false;

        if (invalidatePowers)
            InvalidatePowers();
    }


    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        CollectOvernightExposedTools(Game1.player, "day start");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        ReconcileLoadedToolState(Game1.player, "day start");
        ConvertInventoryTools(Game1.player);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        SyncBlacksmithUpgradeState(Game1.player);
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        SyncBlacksmithUpgradeState(Game1.player);
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        ExposeStoredToolsForOvernight(Game1.player, "saving");
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        CollectOvernightExposedTools(Game1.player, "post-save cleanup");
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        ReconcileLoadedToolState(Game1.player, "post-save cleanup");
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        UpdateBlacksmithAndCompatibilityMenus();
        UpdatePeriodicLostFoundScan(e);
        UpdatePendingTemporaryToolUse();
        UpdatePendingInventoryConversion();
    }

    private void UpdateBlacksmithAndCompatibilityMenus()
    {
        SyncBlacksmithUpgradeState(Game1.player);
        TrackWalletCompatibilityMenus(Game1.player);

        if (MayHaveMenuVirtualToolsInInventory)
            CollectMenuVirtualToolsFromInventory(Game1.player, "update tick safety");
    }

    private void UpdatePeriodicLostFoundScan(UpdateTickedEventArgs e)
    {
        if (Config.ModEnabled && e.IsMultipleOf(300))
            ScanLostFoundForWalletTools("periodic lost and found scan");
    }

    private void UpdatePendingTemporaryToolUse()
    {
        if (PendingToolUse is null)
            return;

        if (PendingManualUseHotkey.HasValue)
        {
            if (IsManualWalletToolHotkeyPhysicallyHeld())
                UpdateManualWalletToolCharge(Game1.player);
            else
                FinishManualWalletToolHotkeyUse(Game1.player);
        }
        else if (!IsPlayerUsingTool(Game1.player))
            RestoreTemporaryTool(Game1.player);
    }

    private void UpdatePendingInventoryConversion()
    {
        if (!PendingInventoryConversion || !CanConvertInventoryTools(Game1.player))
            return;

        PendingInventoryConversion = false;
        ConvertInventoryTools(Game1.player);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (SuppressInventoryConversion || !Context.IsWorldReady || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID || ActiveBlacksmithExposures.Count > 0)
            return;


        if (!CanConvertInventoryTools(e.Player))
        {
            PendingInventoryConversion = true;
            return;
        }

        ConvertInventoryTools(e.Player);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.ModEnabled || Game1.fadeToBlack)
            return;

        if (Config.ToggleAutoUseHotkey.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            SetAutoUseMasterEnabled(!Config.AutoUseEnabled, showMessage: true);
            return;
        }

        if (Game1.activeClickableMenu is not null)
        {
            if (TryHandleForgeWalletHotkey(e.Button))
                Helper.Input.Suppress(e.Button);
            return;
        }

        if (!Context.CanPlayerMove)
            return;

        WalletToolKind? requested = GetRequestedHotkeyTool();
        if (requested is null)
            return;

        Helper.Input.Suppress(e.Button);
        TryUseWalletToolHotkey(Game1.player, requested.Value, e.Button);
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (!Context.IsWorldReady || PendingManualUseHotkey != e.Button)
            return;

        if (IsManualWalletToolHotkeyPhysicallyHeld())
            return;

        FinishManualWalletToolHotkeyUse(Game1.player);
    }

    private void UpdateManualWalletToolCharge(Farmer player)
    {
        if (PendingToolUse is null || PendingManualUseHotkey is null || player.CurrentTool is null)
            return;

        if (Game1.dialogueUp || player.Stamina < 1f)
            return;

        player.canReleaseTool = true;
    }

    private void FinishManualWalletToolHotkeyUse(Farmer player)
    {
        if (PendingManualUseHotkey is null)
            return;

        bool vanillaStartedToolUse = player.UsingTool && player.CurrentTool is not null;
        PendingManualUseHotkey = null;

        if (vanillaStartedToolUse)
            player.EndUsingTool();
        else if (PendingToolUse is not null && player.CurrentTool is not null)
        {
            Game1.pressUseToolButton();
            if (player.UsingTool)
                player.EndUsingTool();
        }

        if (PendingToolUse is not null && !IsPlayerUsingTool(player))
            RestoreTemporaryTool(player);
    }

    private bool IsManualWalletToolHotkeyHeld()
    {
        return Context.IsWorldReady
            && Config.ModEnabled
            && PendingToolUse is not null
            && PendingManualUseHotkey.HasValue
            && IsManualWalletToolHotkeyPhysicallyHeld();
    }

    private bool IsManualWalletToolHotkeyPhysicallyHeld()
    {
        return PendingManualUseHotkey.HasValue
            && (Helper.Input.IsDown(PendingManualUseHotkey.Value) || Helper.Input.IsSuppressed(PendingManualUseHotkey.Value));
    }

    private static bool IsUseToolButtonBinding(InputButton[] keys)
    {
        return ReferenceEquals(keys, Game1.options?.useToolButton);
    }

    private WalletToolKind? GetRequestedHotkeyTool()
    {
        if (Config.UseAxeHotkey.JustPressed())
            return WalletToolKind.Axe;
        if (Config.UsePickaxeHotkey.JustPressed())
            return WalletToolKind.Pickaxe;
        if (Config.UseHoeHotkey.JustPressed())
            return WalletToolKind.Hoe;
        if (Config.UseWateringCanHotkey.JustPressed())
            return WalletToolKind.WateringCan;
        if (Config.UsePanHotkey.JustPressed())
            return WalletToolKind.Pan;
        if (Config.UseMilkPailHotkey.JustPressed())
            return WalletToolKind.MilkPail;
        if (Config.UseShearsHotkey.JustPressed())
            return WalletToolKind.Shears;

        return null;
    }


    private bool TryHandleForgeWalletHotkey(SButton pressedButton)
    {
        object? menu = Game1.activeClickableMenu;
        if (!IsForgeMenu(menu))
            return false;

        if (pressedButton == SButton.Escape && HasMenuVirtualToolInObject(menu!, "Forge", Game1.player))
        {
            CollectMenuVirtualToolsFromObject(menu!, Game1.player, "Forge Escape cancel");
            return true;
        }

        WalletToolKind? requested = GetRequestedHotkeyTool();
        if (requested is null)
            return false;

        if (!IsForgeCompatibleKind(requested.Value))
            return true;

        TrySupplyWalletToolToForge(menu!, requested.Value);
        return true;
    }

    private void TrackWalletCompatibilityMenus(Farmer player)
    {
        if (!Config.ModEnabled)
        {
            PatchedBlacksmithMenu = null;
            ActiveForgeMenu = null;
            return;
        }

        object? menu = Game1.activeClickableMenu;
        if (menu is null)
        {
            if (ActiveForgeMenu is not null)
            {
                CollectMenuVirtualToolsFromObject(ActiveForgeMenu, player, "Forge menu close");
                ActiveForgeMenu = null;
            }

            PatchedBlacksmithMenu = null;
            return;
        }

        if (IsToolUpgradeLocation(player.currentLocation))
        {
            if (!ReferenceEquals(PatchedBlacksmithMenu, menu))
            {
                if (TryInjectWalletToolsIntoItemSelectionMenu(menu, player, "Clint"))
                    PatchedBlacksmithMenu = menu;
            }
        }
        else if (PatchedBlacksmithMenu is not null)
        {
            PatchedBlacksmithMenu = null;
        }

        if (IsForgeMenu(menu))
        {
            if (!ReferenceEquals(ActiveForgeMenu, menu))
            {
                ActiveForgeMenu = menu;
            }
        }
        else if (ActiveForgeMenu is not null && !ReferenceEquals(ActiveForgeMenu, menu))
        {
            CollectMenuVirtualToolsFromObject(ActiveForgeMenu, player, "Forge menu replaced");
            ActiveForgeMenu = null;
        }
    }

    private static bool IsItemGrabMenu(object? menu)
    {
        if (menu is null)
            return false;

        Type type = menu.GetType();
        return type.Name.Equals("ItemGrabMenu", StringComparison.OrdinalIgnoreCase)
            || type.FullName?.Equals("StardewValley.Menus.ItemGrabMenu", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsForgeMenu(object? menu)
    {
        if (menu is null)
            return false;

        Type type = menu.GetType();
        return type.Name.Equals("ForgeMenu", StringComparison.OrdinalIgnoreCase)
            || type.FullName?.Equals("StardewValley.Menus.ForgeMenu", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool IsBlacksmithCompatibleKind(WalletToolKind kind)
    {
        return kind is WalletToolKind.Axe
            or WalletToolKind.Pickaxe
            or WalletToolKind.Hoe
            or WalletToolKind.WateringCan
            or WalletToolKind.Pan;
    }

    private static bool IsForgeCompatibleKind(WalletToolKind kind)
    {
        return kind is WalletToolKind.Axe
            or WalletToolKind.Pickaxe
            or WalletToolKind.Hoe
            or WalletToolKind.WateringCan
            or WalletToolKind.Pan;
    }

    private bool TryInjectWalletToolsIntoItemSelectionMenu(object menu, Farmer player, string purpose)
    {
        bool patched = false;
        foreach (object inventoryMenu in EnumerateInventoryMenuMembers(menu))
        {
            if (!TryGetActualInventoryList(inventoryMenu, out IList list))
                continue;

            IList candidateList = list;
            bool detachedForMenu = !ReferenceEquals(candidateList, player.Items);
            if (!detachedForMenu)
            {
                IList? detached = TryCreateDetachedMenuInventory(candidateList);
                if (detached is null || !TrySetActualInventoryList(inventoryMenu, detached))
                {
                    continue;
                }

                candidateList = detached;
                detachedForMenu = true;
            }

            int added = InjectMenuVirtualTools(candidateList, player, purpose);
            if (added > 0)
            {
                patched = true;
            }
        }

        return patched;
    }

    private int InjectMenuVirtualTools(IList candidateList, Farmer player, string purpose)
    {
        int added = 0;
        foreach (WalletToolKind kind in GetMenuCompatibleStoredToolKinds(player, purpose))
        {
            if (ContainsMenuVirtualTool(candidateList, kind, purpose, player))
                continue;

            Tool? tool = CreateMenuVirtualTool(player, kind, purpose);
            if (tool is null)
                continue;

            candidateList.Add(tool);
            added++;
        }

        return added;
    }

    private static IList? TryCreateDetachedMenuInventory(IList source)
    {
        try
        {
            List<Item?> detached = new();
            foreach (object? item in source)
                detached.Add(item as Item);

            return detached;
        }
        catch
        {
            return null;
        }
    }

    private IEnumerable<WalletToolKind> GetMenuCompatibleStoredToolKinds(Farmer player, string purpose)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        foreach (WalletToolKind kind in GetToolRecoveryOrder())
        {
            if (!IsWalletEnabled(kind) || !storedTools.ContainsKey(kind))
                continue;

            if (purpose.Equals("Clint", StringComparison.OrdinalIgnoreCase) && !IsBlacksmithCompatibleKind(kind))
                continue;

            if (purpose.Equals("Forge", StringComparison.OrdinalIgnoreCase) && !IsForgeCompatibleKind(kind))
                continue;

            yield return kind;
        }
    }

    private Tool? CreateMenuVirtualTool(Farmer player, WalletToolKind kind, string purpose)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (!storedTools.TryGetValue(kind, out WalletToolState? state))
            return null;

        Tool? tool = state.CreateTool(Monitor);
        if (tool is null || WalletToolState.IsErrorTool(tool))
            return null;

        ClearWalletMarkers(tool);
        MarkMenuVirtualTool(tool, kind, purpose, player);
        MayHaveMenuVirtualToolsInInventory = true;
        return tool;
    }

    private static void MarkMenuVirtualTool(Tool tool, WalletToolKind kind, string purpose, Farmer player)
    {
        tool.modData[MenuVirtualToolMarker] = "true";
        tool.modData[MenuVirtualToolKindMarker] = kind.ToString();
        tool.modData[MenuVirtualToolPurposeMarker] = purpose;
        MarkWalletOwner(tool, player);
    }

    private static bool IsMenuVirtualTool(Item? item, out WalletToolKind kind, out string purpose)
    {
        kind = WalletToolKind.Axe;
        purpose = string.Empty;
        if (item is not Tool tool || !tool.modData.ContainsKey(MenuVirtualToolMarker))
            return false;

        if (!tool.modData.TryGetValue(MenuVirtualToolKindMarker, out string? rawKind) || !Enum.TryParse(rawKind, out kind))
            return false;

        tool.modData.TryGetValue(MenuVirtualToolPurposeMarker, out purpose);
        purpose ??= string.Empty;
        return true;
    }

    private static bool IsMenuVirtualToolForPlayer(Item? item, Farmer player, out WalletToolKind kind, out string purpose)
    {
        if (!IsMenuVirtualTool(item, out kind, out purpose))
            return false;

        return item is Tool tool && IsOwnedByPlayer(tool, player);
    }

    private static void ClearMenuVirtualMarkers(Tool tool)
    {
        tool.modData.Remove(MenuVirtualToolMarker);
        tool.modData.Remove(MenuVirtualToolKindMarker);
        tool.modData.Remove(MenuVirtualToolPurposeMarker);
    }

    private static bool ContainsMenuVirtualTool(IList list, WalletToolKind kind, string purpose, Farmer player)
    {
        foreach (object? item in list)
        {
            if (IsMenuVirtualToolForPlayer(item as Item, player, out WalletToolKind existingKind, out string existingPurpose)
                && existingKind == kind
                && existingPurpose.Equals(purpose, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private IEnumerable<object> EnumerateInventoryMenuMembers(object menu)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in menu.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            object? value = GetMemberValue(member, menu);
            if (value is null)
                continue;

            if (value.GetType().Name.Equals("InventoryMenu", StringComparison.OrdinalIgnoreCase))
                yield return value;
        }
    }

    private static bool TryGetActualInventoryList(object inventoryMenu, out IList list)
    {
        list = null!;
        if (!TryGetActualInventoryMember(inventoryMenu, out MemberInfo? member) || member is null)
            return false;

        object? value = GetMemberValue(member, inventoryMenu);
        if (value is IList inventoryList)
        {
            list = inventoryList;
            return true;
        }

        return false;
    }

    private static bool TrySetActualInventoryList(object inventoryMenu, IList list)
    {
        return TryGetActualInventoryMember(inventoryMenu, out MemberInfo? member)
            && member is not null
            && SetMemberValue(member, inventoryMenu, list);
    }

    private static bool TryGetActualInventoryMember(object inventoryMenu, out MemberInfo? actualInventoryMember)
    {
        actualInventoryMember = null;
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in inventoryMenu.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            if (!member.Name.Equals("actualInventory", StringComparison.OrdinalIgnoreCase))
                continue;

            actualInventoryMember = member;
            return true;
        }

        return false;
    }

    private void TrySupplyWalletToolToForge(object forgeMenu, WalletToolKind requestedKind)
    {
        Farmer player = Game1.player;
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);

        if (HasMenuVirtualToolInObject(forgeMenu, "Forge", player))
            CollectMenuVirtualToolsFromObject(forgeMenu, player, "Forge tool cycle");

        if (!TryFindStoredEnabledToolForRequest(player, requestedKind, out WalletToolKind storedKind) || !IsForgeCompatibleKind(storedKind))
            return;

        Tool? tool = CreateMenuVirtualTool(player, storedKind, "Forge");
        if (tool is null)
            return;

        if (!TrySetForgeHeldItem(forgeMenu, tool) && !TrySetForgeLeftItem(forgeMenu, tool))
            return;

        storedTools.Remove(storedKind);
        RefreshWalletStateAfterStoredToolChange(player);
    }

    private static bool TrySetForgeHeldItem(object forgeMenu, Tool tool)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in forgeMenu.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            if (!member.Name.Contains("held", StringComparison.OrdinalIgnoreCase) || !MemberCanHoldItem(member))
                continue;

            object? current = GetMemberValue(member, forgeMenu);
            if (current is not null)
                continue;

            if (SetMemberValue(member, forgeMenu, tool))
                return true;
        }

        return false;
    }

    private static bool TrySetForgeLeftItem(object forgeMenu, Tool tool)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in forgeMenu.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            string name = member.Name;
            if (!MemberCanHoldItem(member)
                || name.Contains("right", StringComparison.OrdinalIgnoreCase)
                || name.Contains("gem", StringComparison.OrdinalIgnoreCase)
                || name.Contains("second", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!name.Contains("left", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("base", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("equipment", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("ingredient", StringComparison.OrdinalIgnoreCase))
                continue;

            object? current = GetMemberValue(member, forgeMenu);
            if (current is not null)
                continue;

            if (SetMemberValue(member, forgeMenu, tool))
                return true;
        }

        return false;
    }

    private static bool MemberCanHoldItem(MemberInfo member)
    {
        Type? type = member switch
        {
            FieldInfo field => field.FieldType,
            PropertyInfo property => property.PropertyType,
            _ => null
        };

        return type is not null && typeof(Item).IsAssignableFrom(type);
    }

    private bool HasMenuVirtualToolInObject(object owner, string purpose, Farmer player)
    {
        return EnumerateItemsInObject(owner).Any(item => IsMenuVirtualToolForPlayer(item, player, out _, out string itemPurpose) && itemPurpose.Equals(purpose, StringComparison.OrdinalIgnoreCase));
    }

    private void CollectMenuVirtualToolsFromObject(object owner, Farmer player, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        bool changed = false;
        foreach ((object container, MemberInfo member, Item? item) in EnumerateItemMembersInObject(owner).ToArray())
        {
            if (!IsMenuVirtualToolForPlayer(item, player, out WalletToolKind kind, out string purpose) || item is not Tool tool)
                continue;

            ClearMenuVirtualMarkers(tool);
            if (IsWalletEnabled(kind))
            {
                storedTools[kind] = WalletToolState.FromTool(kind, tool);
                SetMemberValue(member, container, null);
                changed = true;
            }
        }

        if (changed)
        {
            MayHaveMenuVirtualToolsInInventory = true;
            RefreshWalletStateAfterStoredToolChange(player);
        }
    }

    private void CollectMenuVirtualToolsFromInventory(Farmer player, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        bool changed = false;
        bool foundVirtualTool = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (!IsMenuVirtualToolForPlayer(player.Items[i], player, out WalletToolKind kind, out string purpose) || player.Items[i] is not Tool tool)
                    continue;

                foundVirtualTool = true;
                ClearMenuVirtualMarkers(tool);
                if (IsWalletEnabled(kind))
                {
                    storedTools[kind] = WalletToolState.FromTool(kind, tool);
                    player.Items[i] = null;
                    changed = true;
                }
            }

            if (changed)
                NormalizePlayerItemsAfterWalletCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        MayHaveMenuVirtualToolsInInventory = foundVirtualTool || ActiveForgeMenu is not null || PatchedBlacksmithMenu is not null;

        if (changed)
            RefreshWalletStateAfterStoredToolChange(player);
    }

    private IEnumerable<Item?> EnumerateItemsInObject(object owner)
    {
        foreach ((object Container, MemberInfo Member, Item? Item) entry in EnumerateItemMembersInObject(owner))
            yield return entry.Item;
    }

    private IEnumerable<(object Container, MemberInfo Member, Item? Item)> EnumerateItemMembersInObject(object owner)
    {
        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in owner.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            object? value = GetMemberValue(member, owner);
            if (value is Item item)
                yield return (owner, member, item);
        }
    }

    private void TryUseWalletToolHotkey(Farmer player, WalletToolKind requestedKind, SButton pressedButton)
    {
        if (!TryFindStoredEnabledToolForRequest(player, requestedKind, out WalletToolKind storedKind))
            return;

        if (!TryMaterializeWalletToolForManualUse(player, storedKind))
            return;

        PendingManualUseHotkey = pressedButton;
    }

    private static bool IsToolUpgradeLocation(GameLocation? location)
    {
        if (location is null)
            return false;

        string name = location.NameOrUniqueName;
        return name.Equals("Blacksmith", StringComparison.OrdinalIgnoreCase) || name.Equals("Clint", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetDaysLeftForToolUpgrade(Farmer player)
    {
        Type type = player.GetType();
        foreach (string name in new[] { "daysLeftForToolUpgrade", "DaysLeftForToolUpgrade" })
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
            {
                object? propertyValue = property.GetValue(player);
                if (propertyValue is int propertyInt)
                    return propertyInt;
                if (propertyValue is Netcode.NetInt propertyNetInt)
                    return propertyNetInt.Value;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                object? fieldValue = field.GetValue(player);
                if (fieldValue is int fieldInt)
                    return fieldInt;
                if (fieldValue is Netcode.NetInt fieldNetInt)
                    return fieldNetInt.Value;
            }
        }

        return 0;
    }

    private bool IsCompletedBlacksmithToolReadyForVanillaPickup(Farmer player)
    {
        Tool? completedTool = GetToolBeingUpgraded(player);
        return completedTool is not null
            && GetDaysLeftForToolUpgrade(player) <= 0
            && TryGetWalletKindForStorage(completedTool, out WalletToolKind kind)
            && IsWalletEnabled(kind);
    }

    private void PrepareForBlacksmithToolFlow(Farmer player)
    {
        if (!Config.ModEnabled || !IsToolUpgradeLocation(player.currentLocation))
            return;

        NormalizePlayerItemsAfterWalletCleanup(player);

        if (IsCompletedBlacksmithToolReadyForVanillaPickup(player))
            return;

        SyncBlacksmithUpgradeState(player);
        RemoveRuntimeToolCopies(player);
        ExposeStoredToolsForBlacksmithDialogue(player);
    }

    private void FinishBlacksmithToolFlow(Farmer player)
    {
        if (!Config.ModEnabled || !IsToolUpgradeLocation(player.currentLocation))
            return;

        SyncBlacksmithUpgradeState(player);
        RestoreBlacksmithDialogueExposures(player, "blacksmith flow finished");
        RemoveRuntimeToolCopies(player);
        NormalizePlayerItemsAfterWalletCleanup(player);
    }

    private void ExposeStoredToolsForBlacksmithDialogue(Farmer player)
    {
        if (ActiveBlacksmithExposures.Count > 0)
            return;

        int maxItems = GetPlayerMaxItemCount(player);
        SuppressInventoryConversion = true;
        try
        {
            foreach (WalletToolKind kind in GetMenuCompatibleStoredToolKinds(player, "Clint"))
            {
                Tool? tool = CreateMenuVirtualTool(player, kind, "Clint");
                if (tool is null)
                    continue;

                int slot = FindBlacksmithExposureSlot(player, maxItems);
                Item? previousItem = null;
                if (slot < player.Items.Count)
                {
                    previousItem = player.Items[slot];
                    player.Items[slot] = tool;
                }
                else
                {
                    player.Items.Add(tool);
                }

                ActiveBlacksmithExposures.Add(new BlacksmithExposure(slot, previousItem, kind, tool));
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

    }

    private static int FindBlacksmithExposureSlot(Farmer player, int maxItems)
    {
        int searchCount = Math.Min(player.Items.Count, maxItems);
        for (int i = 0; i < searchCount; i++)
        {
            if (player.Items[i] is null)
                return i;
        }

        if (player.Items.Count < maxItems)
            return player.Items.Count;

        return player.Items.Count;
    }

    private void RestoreBlacksmithDialogueExposures(Farmer player, string reason)
    {
        if (ActiveBlacksmithExposures.Count == 0)
            return;

        Tool? upgradingTool = player.toolBeingUpgraded.Value;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = ActiveBlacksmithExposures.Count - 1; i >= 0; i--)
            {
                BlacksmithExposure exposure = ActiveBlacksmithExposures[i];
                bool becameUpgradeTool = upgradingTool is not null && ReferenceEquals(upgradingTool, exposure.Tool);

                if (exposure.Slot < player.Items.Count && ReferenceEquals(player.Items[exposure.Slot], exposure.Tool))
                {
                    ClearWalletMarkers(exposure.Tool);
                    player.Items[exposure.Slot] = exposure.PreviousItem;
                }
                else if (becameUpgradeTool)
                {
                }
                else
                {
                }
            }
        }
        finally
        {
            ActiveBlacksmithExposures.Clear();
            SuppressInventoryConversion = false;
        }

        NormalizePlayerItemsAfterWalletCleanup(player);
    }

    private void ExposeStoredToolsForOvernight(Farmer player, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (!Config.ModEnabled || storedTools.Count == 0)
            return;

        bool changed = MaterializeStoredToolsToInventory(player, markForOvernight: true, clearStoredState: true, useOverflowMenu: false, reason: reason);
        if (!changed && !HasAnyOvernightExposure(player))
            return;

        ClearLegacyStoredToolsData();
        ClearWalletFlagModData(player);
        ToolSmartSwitchToolCacheByPlayer.Clear();
        InvalidatePowers();

    }

    private bool MaterializeStoredToolsToInventory(Farmer player, bool markForOvernight, bool clearStoredState, bool useOverflowMenu, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (storedTools.Count == 0)
            return false;


        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            foreach (KeyValuePair<WalletToolKind, WalletToolState> pair in storedTools.ToArray())
            {
                WalletToolKind kind = pair.Key;
                if (!IsWalletEnabled(kind) && markForOvernight)
                    continue;

                if (markForOvernight && HasOvernightExposure(player, kind))
                    continue;

                Tool? tool = pair.Value.CreateTool(Monitor);
                if (tool is null || WalletToolState.IsErrorTool(tool))
                {
                    storedTools.Remove(kind);
                    changed = true;
                    continue;
                }

                ClearWalletMarkers(tool);
                if (markForOvernight)
                    MarkOvernightExposure(tool, kind, player);

                if (useOverflowMenu)
                    AddToolToInventoryOrOverflowMenu(player, tool);
                else
                    AddToolToInventoryOrAppend(player, tool);
                if (clearStoredState)
                    storedTools.Remove(kind);
                changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        return changed;
    }

    private static void AddToolToInventoryOrAppend(Farmer player, Tool tool)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        int searchCount = Math.Min(player.Items.Count, maxItems);
        for (int i = 0; i < searchCount; i++)
        {
            if (player.Items[i] is null)
            {
                player.Items[i] = tool;
                NormalizePlayerItemsAfterWalletCleanup(player);
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(tool);
            NormalizePlayerItemsAfterWalletCleanup(player);
            return;
        }

        player.Items.Add(tool);
        NormalizePlayerItemsAfterWalletCleanup(player);
    }

    private static void AddToolToInventoryOrOverflowMenu(Farmer player, Tool tool)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        int searchCount = Math.Min(player.Items.Count, maxItems);
        for (int i = 0; i < searchCount; i++)
        {
            if (player.Items[i] is null)
            {
                player.Items[i] = tool;
                NormalizePlayerItemsAfterWalletCleanup(player);
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(tool);
            NormalizePlayerItemsAfterWalletCleanup(player);
            return;
        }

        player.addItemByMenuIfNecessary(tool);
        NormalizePlayerItemsAfterWalletCleanup(player);
    }

    private void CollectOvernightExposedTools(Farmer player, string reason)
    {
        if (!Config.ModEnabled)
            return;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || !IsOvernightExposureForPlayer(tool, player, out WalletToolKind kind))
                    continue;

                ClearWalletMarkers(tool);
                if (IsWalletEnabled(kind) && TryGetWalletKindForStorage(tool, out WalletToolKind storageKind))
                {
                    storedTools[storageKind] = WalletToolState.FromTool(storageKind, tool);
                    player.Items[i] = null;
                }

                changed = true;
            }

            if (changed)
                NormalizePlayerItemsAfterWalletCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
        {
            RefreshWalletStateAfterStoredToolChange(player);
        }
    }

    private static bool HasOvernightExposure(Farmer player, WalletToolKind kind)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && IsOvernightExposureForPlayer(tool, player, out WalletToolKind exposedKind) && exposedKind == kind)
                return true;
        }

        return false;
    }

    private static bool HasAnyOvernightExposure(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && IsOvernightExposureForPlayer(tool, player, out _))
                return true;
        }

        return false;
    }

    private static void ClearWalletMarkersFromInventory(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && HasAnyWalletMarker(tool))
                ClearWalletMarkers(tool);
        }
    }

    private static void ClearWalletFlagModData(Farmer player)
    {
        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
            player.modData.Remove($"{FlagPrefix}{kind}");
    }

    private static void MarkOvernightExposure(Tool tool, WalletToolKind kind, Farmer player)
    {
        tool.modData[OvernightExposureMarker] = "true";
        tool.modData[OvernightExposureKindMarker] = kind.ToString();
        MarkWalletOwner(tool, player);
    }

    private static bool IsOvernightExposure(Tool tool, out WalletToolKind kind)
    {
        if (tool.modData.TryGetValue(OvernightExposureKindMarker, out string? rawKind)
            && Enum.TryParse(rawKind, out kind)
            && tool.modData.ContainsKey(OvernightExposureMarker))
            return true;

        kind = WalletToolKind.Axe;
        return false;
    }

    private static bool IsOvernightExposureForPlayer(Tool tool, Farmer player, out WalletToolKind kind)
    {
        if (!IsOvernightExposure(tool, out kind))
            return false;

        return IsOwnedByPlayer(tool, player);
    }

    private static bool HasAnyWalletMarker(Tool tool)
    {
        return tool.modData.ContainsKey(RuntimeToolMarker)
            || tool.modData.ContainsKey(RuntimeToolKindMarker)
            || tool.modData.ContainsKey(OvernightExposureMarker)
            || tool.modData.ContainsKey(OvernightExposureKindMarker)
            || tool.modData.ContainsKey(MenuVirtualToolMarker)
            || tool.modData.ContainsKey(MenuVirtualToolKindMarker)
            || tool.modData.ContainsKey(MenuVirtualToolPurposeMarker)
            || tool.modData.ContainsKey(OwnerPlayerIdMarker);
    }

    private static void ClearWalletMarkers(Tool tool)
    {
        tool.modData.Remove(RuntimeToolMarker);
        tool.modData.Remove(RuntimeToolKindMarker);
        tool.modData.Remove(OvernightExposureMarker);
        tool.modData.Remove(OvernightExposureKindMarker);
        ClearMenuVirtualMarkers(tool);
        ClearWalletOwner(tool);
    }

    private static void NormalizePlayerItemsAfterWalletCleanup(Farmer player)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        if (player.Items.Count <= maxItems)
        {
            return;
        }

        for (int i = maxItems; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not null)
            {
                return;
            }
        }

        int oldCount = player.Items.Count;
        while (player.Items.Count > maxItems)
            player.Items.RemoveAt(player.Items.Count - 1);

    }

    private static int GetPlayerMaxItemCount(Farmer player)
    {
        int maxItems = WalletToolState.GetIntMember(player, "MaxItems", "maxItems");
        return Math.Max(12, maxItems <= 0 ? 36 : maxItems);
    }

    private void LoadStoredTools()
    {
        GetStoredTools(Game1.player).Clear();
        ToolSmartSwitchToolCacheByPlayer.Clear();
        ClearLegacyStoredToolsData();
        UpdateWalletFlags(Game1.player);
        InvalidatePowers();
    }

    private void RefreshWalletStateAfterStoredToolChange(bool invalidatePowers = true)
    {
        RefreshWalletStateAfterStoredToolChange(Game1.player, invalidatePowers);
    }

    private void RefreshWalletStateAfterStoredToolChange(Farmer player, bool invalidatePowers = true)
    {
        ToolSmartSwitchToolCacheByPlayer.Clear();
        ClearLegacyStoredToolsData();
        UpdateWalletFlags(player);
        if (invalidatePowers)
            InvalidatePowers();
    }

    private void ClearLegacyStoredToolsData()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        Helper.Data.WriteSaveData<Dictionary<WalletToolKind, WalletToolState>>(SaveDataKey, null);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private void UpdateWalletFlags(Farmer player)
    {
        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
        {
            string key = $"{FlagPrefix}{kind}";
            if (TryGetWalletDisplayState(player, kind, out _))
                player.modData[key] = "true";
            else
                player.modData.Remove(key);
        }
    }

    private bool TryGetWalletDisplayState(WalletToolKind kind, out WalletToolState state)
    {
        return TryGetWalletDisplayState(Game1.player, kind, out state);
    }

    private bool TryGetWalletDisplayState(Farmer player, WalletToolKind kind, out WalletToolState state)
    {
        if (!IsWalletEnabled(kind))
        {
            state = null!;
            return false;
        }

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (storedTools.TryGetValue(kind, out WalletToolState? directState))
        {
            state = directState;
            return true;
        }

        state = null!;
        return false;
    }

    private void SyncBlacksmithUpgradeState(Farmer player)
    {
        if (!Config.ModEnabled)
            return;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        Tool? upgradingTool = GetToolBeingUpgraded(player);
        if (upgradingTool is null)
            return;

        if (!TryGetWalletKindForStorage(upgradingTool, out WalletToolKind upgradingKind))
        {
            return;
        }

        ClearWalletMarkers(upgradingTool);

        bool changed = false;
        if (storedTools.Remove(upgradingKind))
        {
            changed = true;
        }

        if (RemoveExactToolReferenceFromInventory(player, upgradingTool))
        {
            changed = true;
        }

        if (RemoveInventoryCopiesOfToolKind(player, upgradingKind, upgradingTool))
        {
            changed = true;
        }

        if (RemoveLostFoundCopiesOfToolKind(player, upgradingKind))
        {
            changed = true;
        }

        if (changed)
        {
            RefreshWalletStateAfterStoredToolChange(player);
            UpdateWalletFlags(player);
            InvalidatePowers();
        }
    }


    private static Tool? GetToolBeingUpgraded(Farmer player)
    {
        return player.toolBeingUpgraded.Value;
    }

    private static IEnumerable<WalletToolKind> GetToolRecoveryOrder()
    {
        yield return WalletToolKind.Pickaxe;
        yield return WalletToolKind.Axe;
        yield return WalletToolKind.Hoe;
        yield return WalletToolKind.WateringCan;
        yield return WalletToolKind.Pan;
        yield return WalletToolKind.MilkPail;
        yield return WalletToolKind.Shears;
    }

    private void ReconcileLoadedToolState(Farmer player, string reason)
    {
        if (!Config.ModEnabled)
            return;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            RemoveRuntimeToolCopies(player);
            if (RemoveInvalidStoredTools(player, reason))
                changed = true;

            Tool? upgradingTool = GetToolBeingUpgraded(player);
            WalletToolKind? upgradingKind = null;
            if (upgradingTool is not null && TryGetWalletKindForStorage(upgradingTool, out WalletToolKind kindBeingUpgraded))
            {
                upgradingKind = kindBeingUpgraded;
                ClearWalletMarkers(upgradingTool);
                if (RemoveExactToolReferenceFromInventory(player, upgradingTool))
                    changed = true;
                if (RemoveInventoryCopiesOfToolKind(player, kindBeingUpgraded, upgradingTool))
                    changed = true;
                if (RemoveLostFoundCopiesOfToolKind(player, kindBeingUpgraded))
                    changed = true;
                if (storedTools.Remove(kindBeingUpgraded))
                    changed = true;
            }

            foreach (WalletToolKind kind in GetToolRecoveryOrder())
            {
                if (!IsWalletEnabled(kind) || upgradingKind == kind)
                    continue;

                if (storedTools.ContainsKey(kind))
                {
                    if (RemoveInventoryCopiesOfToolKind(player, kind, null))
                        changed = true;
                    if (CollectLostFoundToolsOfKind(player, kind))
                        changed = true;
                    continue;
                }

                if (TryTakeInventoryTool(player, kind, out Tool? inventoryTool))
                {
                    storedTools[kind] = WalletToolState.FromTool(kind, inventoryTool);
                    if (CollectLostFoundToolsOfKind(player, kind))
                        changed = true;
                    changed = true;
                    continue;
                }

                if (CollectLostFoundToolsOfKind(player, kind))
                    changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
        {
            RefreshWalletStateAfterStoredToolChange(player);
        }
        else
        {
            UpdateWalletFlags(player);
            InvalidatePowers();
        }
    }

    private bool TryTakeInventoryTool(Farmer player, WalletToolKind kind, out Tool tool)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is Tool candidate && TryGetWalletKindForStorage(candidate, out WalletToolKind storageKind) && storageKind == kind)
            {
                ClearWalletMarkers(candidate);
                player.Items[i] = null;
                NormalizePlayerItemsAfterWalletCleanup(player);
                tool = candidate;
                return true;
            }
        }

        tool = null!;
        return false;
    }

    private bool CollectLostFoundToolsOfKind(WalletToolKind kind)
    {
        return CollectLostFoundToolsOfKind(Game1.player, kind);
    }

    private bool CollectLostFoundToolsOfKind(Farmer player, WalletToolKind kind)
    {
        bool changed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundTool(player, kind, out Tool? recoveredTool))
                break;

            if (TryReplaceStoredToolIfBetter(player, kind, recoveredTool))
                changed = true;

            changed = true;
        }

        return changed;
    }

    private void ScanLostFoundForWalletTools(string reason)
    {
        if (!Config.ModEnabled || !Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        bool changed = false;
        foreach (WalletToolKind kind in GetToolRecoveryOrder())
        {
            if (!IsWalletEnabled(kind))
                continue;

            if (CollectLostFoundToolsOfKind(player, kind))
                changed = true;
        }

        if (!changed)
            return;

        RefreshWalletStateAfterStoredToolChange(player);
    }

    private bool RemoveLostFoundCopiesOfToolKind(WalletToolKind kind)
    {
        return RemoveLostFoundCopiesOfToolKind(Game1.player, kind);
    }

    private bool RemoveLostFoundCopiesOfToolKind(Farmer player, WalletToolKind kind)
    {
        bool removed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundTool(player, kind, out _))
                break;

            removed = true;
        }

        return removed;
    }

    private static bool IsLikelyLostFoundToolContainer(MemberInfo member, object? value)
    {
        if (value is null || value is string)
            return false;

        string name = member.Name;
        bool nameLooksRelevant = name.Contains("lost", StringComparison.OrdinalIgnoreCase)
            || name.Contains("found", StringComparison.OrdinalIgnoreCase)
            || name.Contains("return", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tool", StringComparison.OrdinalIgnoreCase);

        if (!nameLooksRelevant)
            return false;

        Type valueType = value.GetType();
        if (typeof(Tool).IsAssignableFrom(valueType) || typeof(IList).IsAssignableFrom(valueType) || typeof(IDictionary).IsAssignableFrom(valueType))
            return true;

        if (TryGetNetValue(value) is Tool)
            return true;

        PropertyInfo? valueProperty = valueType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return valueProperty is not null && valueProperty.GetIndexParameters().Length == 0 && typeof(Tool).IsAssignableFrom(valueProperty.PropertyType);
    }

    private bool TryTakeLostFoundTool(WalletToolKind kind, out Tool tool)
    {
        return TryTakeLostFoundTool(Game1.player, kind, out tool);
    }

    private bool TryTakeLostFoundTool(Farmer player, WalletToolKind kind, out Tool tool)
    {
        tool = null!;
        object? team = player.team;
        if (team is null)
            return false;

        bool requireOwner = Context.IsMultiplayer;

        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in team.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            object? value = GetMemberValue(member, team);
            if (!IsLikelyLostFoundToolContainer(member, value))
                continue;

            if (TryTakeToolFromValue(value, kind, out tool, player, requireOwner))
                return true;
        }

        return false;
    }


    private static bool SetMemberValue(MemberInfo member, object owner, object? value)
    {
        try
        {
            switch (member)
            {
                case FieldInfo field when !field.IsInitOnly:
                    field.SetValue(owner, value);
                    return true;
                case PropertyInfo property when property.GetIndexParameters().Length == 0 && property.CanWrite:
                    property.SetValue(owner, value);
                    return true;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static object? GetMemberValue(MemberInfo member, object owner)
    {
        try
        {
            return member switch
            {
                FieldInfo field => field.GetValue(owner),
                PropertyInfo property when property.GetIndexParameters().Length == 0 => property.GetValue(owner),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static bool TryTakeToolFromValue(object? value, WalletToolKind kind, out Tool tool)
    {
        return TryTakeToolFromValue(value, kind, out tool, null, requireOwner: false);
    }

    private static bool TryTakeToolFromValue(object? value, WalletToolKind kind, out Tool tool, Farmer? player, bool requireOwner)
    {
        tool = null!;
        if (value is null || value is string)
            return false;

        if (TryTakeToolFromWrappedValue(value, kind, out tool, player, requireOwner))
            return true;

        object? unwrapped = TryGetNetValue(value);
        if (!ReferenceEquals(unwrapped, value) && unwrapped is not Tool && TryTakeToolFromValue(unwrapped, kind, out tool, player, requireOwner))
            return true;

        if (value is Tool directTool && ToolMatchesWalletKind(directTool, kind) && ToolIsEligibleForOwner(directTool, player, requireOwner))
        {
            ClearWalletMarkers(directTool);
            tool = directTool;
            return true;
        }

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Tool candidate && ToolMatchesWalletKind(candidate, kind) && ToolIsEligibleForOwner(candidate, player, requireOwner))
                {
                    ClearWalletMarkers(candidate);
                    list.RemoveAt(i);
                    tool = candidate;
                    return true;
                }
            }
        }

        if (value is IDictionary dictionary)
        {
            foreach (object? key in dictionary.Keys.Cast<object?>().ToArray())
            {
                if (key is null)
                    continue;

                object? dictionaryValue = dictionary[key];
                if (dictionaryValue is Tool candidate && ToolMatchesWalletKind(candidate, kind) && ToolIsEligibleForOwner(candidate, player, requireOwner))
                {
                    ClearWalletMarkers(candidate);
                    dictionary.Remove(key);
                    tool = candidate;
                    return true;
                }

                if (TryTakeToolFromValue(dictionaryValue, kind, out tool, player, requireOwner))
                    return true;
            }
        }

        return false;
    }

    private static bool ToolIsEligibleForOwner(Tool tool, Farmer? player, bool requireOwner)
    {
        if (!requireOwner)
            return true;

        return player is not null && HasOwnerMarker(tool) && IsOwnedByPlayer(tool, player);
    }

    private static bool TryTakeToolFromWrappedValue(object value, WalletToolKind kind, out Tool tool)
    {
        return TryTakeToolFromWrappedValue(value, kind, out tool, null, requireOwner: false);
    }

    private static bool TryTakeToolFromWrappedValue(object value, WalletToolKind kind, out Tool tool, Farmer? player, bool requireOwner)
    {
        tool = null!;
        try
        {
            PropertyInfo? property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null || property.GetIndexParameters().Length != 0 || !property.CanRead)
                return false;

            object? inner = property.GetValue(value);
            if (inner is not Tool candidate || !ToolMatchesWalletKind(candidate, kind))
                return false;

            if (!ToolIsEligibleForOwner(candidate, player, requireOwner))
                return false;

            if (!property.CanWrite)
                return false;

            ClearWalletMarkers(candidate);
            property.SetValue(value, null);
            tool = candidate;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static object? TryGetNetValue(object value)
    {
        try
        {
            PropertyInfo? property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return property?.GetIndexParameters().Length == 0 ? property.GetValue(value) : value;
        }
        catch
        {
            return value;
        }
    }


    private static bool RemoveExactToolReferenceFromInventory(Farmer player, Tool? tool)
    {
        if (tool is null)
            return false;

        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (ReferenceEquals(player.Items[i], tool))
            {
                player.Items[i] = null;
                removed = true;
            }
        }

        if (removed)
            NormalizePlayerItemsAfterWalletCleanup(player);

        return removed;
    }

    private bool RemoveInventoryCopiesOfToolKind(Farmer player, WalletToolKind kind, Tool? exceptTool)
    {
        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not Tool tool || ReferenceEquals(tool, exceptTool) || IsRuntimeTool(tool) || IsCurrentlySelectedTool(player, i, tool))
                continue;

            if (ToolMatchesWalletKind(tool, kind))
            {
                TryReplaceStoredToolIfBetter(player, kind, tool);
                player.Items[i] = null;
                removed = true;
            }
        }

        if (removed)
            NormalizePlayerItemsAfterWalletCleanup(player);

        return removed;
    }

    private bool CanConvertInventoryTools(Farmer player)
    {
        return Config.ModEnabled
            && PendingToolUse is null
            && ActiveBlacksmithExposures.Count == 0
            && !IsPlayerUsingTool(player);
    }

    private void ConvertInventoryTools(Farmer player)
    {
        if (!CanConvertInventoryTools(player))
        {
            return;
        }

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool, out _) || IsCurrentlySelectedTool(player, i, tool) || !TryGetWalletKindForStorage(tool, out WalletToolKind kind))
                    continue;

                if (TryReplaceStoredToolIfBetter(player, kind, tool))
                    changed = true;

                player.Items[i] = null;
                changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
        {
            NormalizePlayerItemsAfterWalletCleanup(player);
            RefreshWalletStateAfterStoredToolChange(player);
            Game1.addHUDMessage(new HUDMessage("Tool moved to wallet.", HUDMessage.newQuest_type));
        }
    }

    private bool RemoveInvalidStoredTools(string reason)
    {
        return RemoveInvalidStoredTools(Game1.player, reason);
    }

    private bool RemoveInvalidStoredTools(Farmer player, string reason)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        bool changed = false;
        foreach (KeyValuePair<WalletToolKind, WalletToolState> pair in storedTools.ToArray())
        {
            if (!pair.Value.IsInvalidStoredTool())
                continue;

            storedTools.Remove(pair.Key);
            changed = true;
        }

        return changed;
    }

    private bool TryReplaceStoredToolIfBetter(WalletToolKind kind, Tool candidate)
    {
        return TryReplaceStoredToolIfBetter(Game1.player, kind, candidate);
    }

    private bool TryReplaceStoredToolIfBetter(Farmer player, WalletToolKind kind, Tool candidate)
    {
        if (!IsWalletEnabled(kind) || WalletToolState.IsErrorTool(candidate))
            return false;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        ClearWalletMarkers(candidate);
        WalletToolState candidateState = WalletToolState.FromTool(kind, candidate);
        if (candidateState.IsInvalidStoredTool())
            return false;

        if (!storedTools.TryGetValue(kind, out WalletToolState? existingState)
            || existingState.IsInvalidStoredTool()
            || candidateState.GetPowerScore() > existingState.GetPowerScore())
        {
            storedTools[kind] = candidateState;
            return true;
        }

        return false;
    }

    private bool TryGetWalletKindForStorage(Tool tool, out WalletToolKind kind)
    {
        if (WalletToolState.IsErrorTool(tool))
        {
            kind = WalletToolKind.Axe;
            return false;
        }


        if (!TryGetWalletKind(tool, out kind))
            return false;

        return IsWalletEnabled(kind);
    }

    private static bool TryGetWalletKind(Tool tool, out WalletToolKind kind)
    {
        if (tool is Axe || IsToolAndSprinklerUpgradeTool(tool, "Axe"))
        {
            kind = WalletToolKind.Axe;
            return true;
        }
        if (tool is Pickaxe || IsToolAndSprinklerUpgradeTool(tool, "Pickaxe"))
        {
            kind = WalletToolKind.Pickaxe;
            return true;
        }
        if (tool is Hoe || IsToolAndSprinklerUpgradeTool(tool, "Hoe"))
        {
            kind = WalletToolKind.Hoe;
            return true;
        }
        if (tool is WateringCan || IsToolAndSprinklerUpgradeTool(tool, "WateringCan"))
        {
            kind = WalletToolKind.WateringCan;
            return true;
        }
        if (WalletToolState.IsPanTool(tool))
        {
            kind = WalletToolKind.Pan;
            return true;
        }
        if (WalletToolState.IsMilkPailTool(tool))
        {
            kind = WalletToolKind.MilkPail;
            return true;
        }
        if (WalletToolState.IsShearsTool(tool))
        {
            kind = WalletToolKind.Shears;
            return true;
        }

        kind = WalletToolKind.Axe;
        return false;
    }

    private static bool IsToolAndSprinklerUpgradeTool(Tool tool, string suffix)
    {
        return ToolAndSprinklerUpgradesCompat.IsUpgradeTool(tool, suffix);
    }

    private static string GetToolIdentityText(Tool tool)
    {
        return string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
    }

    private static bool ToolMatchesWalletKind(Tool tool, WalletToolKind kind)
    {
        if (WalletToolState.IsErrorTool(tool))
            return false;

        return TryGetWalletKind(tool, out WalletToolKind actualKind) && actualKind == kind;
    }

    private bool IsWalletEnabled(WalletToolKind kind)
    {
        return Config.ModEnabled && Enum.IsDefined(typeof(WalletToolKind), kind) && GetToolEnabled(kind);
    }

    private void AddWalletToolsToToolSmartSwitch(Farmer player, Dictionary<int, Tool> tools)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (!Config.ModEnabled || IsToolUpgradeLocation(player.currentLocation) || storedTools.Count == 0)
            return;

        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
        {
            if (!IsAutoUseEnabled(kind) || !storedTools.ContainsKey(kind))
                continue;

            int index = GetToolSmartSwitchIndex(kind);
            if (tools.ContainsKey(index))
                continue;

            Tool? tool = GetCachedToolSmartSwitchTool(player, kind);
            if (tool is not null)
                tools[index] = tool;
        }

    }

    private Tool? GetCachedToolSmartSwitchTool(WalletToolKind kind)
    {
        return GetCachedToolSmartSwitchTool(Game1.player, kind);
    }

    private Tool? GetCachedToolSmartSwitchTool(Farmer player, WalletToolKind kind)
    {
        Dictionary<WalletToolKind, Tool> cache = GetToolSmartSwitchToolCache(player);
        if (cache.TryGetValue(kind, out Tool? cachedTool))
            return cachedTool;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (!storedTools.TryGetValue(kind, out WalletToolState? state))
            return null;

        Tool? tool = state.CreateTool(Monitor);
        if (tool is not null)
            cache[kind] = tool;

        return tool;
    }

    private bool TrySwitchToolSmartSwitchWalletTool(Farmer player, int which)
    {
        WalletToolKind? kind = GetToolKindFromToolSmartSwitchIndex(which);
        if (kind is null)
            return false;

        if (!TryFindStoredAutoUseToolForRequest(kind.Value, out WalletToolKind storedKind))
            return false;

        return TryMaterializeWalletToolForAutomaticUse(player, storedKind);
    }

    private static int GetToolSmartSwitchIndex(WalletToolKind kind)
    {
        return -1000 - (int)kind;
    }

    private static WalletToolKind? GetToolKindFromToolSmartSwitchIndex(int index)
    {
        if (index > -1000)
            return null;

        int raw = -1000 - index;
        return Enum.IsDefined(typeof(WalletToolKind), raw) ? (WalletToolKind)raw : null;
    }

    private bool TrySupplyRequestedWalletTool(Farmer player, Type toolType, bool anyTool)
    {
        if (PendingToolUse is not null || !Config.ModEnabled || !Config.AutoUseEnabled || IsToolUpgradeLocation(player.currentLocation))
            return false;

        if (PlayerIsHoldingRequestedTool(player, toolType, anyTool))
            return false;

        if (!TryGetWalletKindForRequestedType(toolType, anyTool, out WalletToolKind kind))
            return false;

        if (!IsAutoUseEnabled(kind))
            return false;

        return TryMaterializeWalletToolForAutomaticUse(player, kind);
    }

    private static bool PlayerIsHoldingRequestedTool(Farmer player, Type toolType, bool anyTool)
    {
        Item? currentItem = player.CurrentItem;
        if (currentItem is null)
            return false;

        if (toolType.IsInstanceOfType(currentItem))
            return true;


        return anyTool && currentItem is Axe or Pickaxe or Hoe;
    }

    private bool TryGetWalletKindForRequestedType(Type toolType, bool anyTool, out WalletToolKind kind)
    {
        if (toolType == typeof(Axe))
            return TryFindStoredAutoUseToolForRequest(WalletToolKind.Axe, out kind);

        if (toolType == typeof(Pickaxe))
            return TryFindStoredAutoUseToolForRequest(WalletToolKind.Pickaxe, out kind);

        if (toolType == typeof(Hoe))
        {
            kind = WalletToolKind.Hoe;
            return HasStoredAutoUseTool(kind);
        }

        if (toolType == typeof(WateringCan))
        {
            kind = WalletToolKind.WateringCan;
            return HasStoredAutoUseTool(kind);
        }

        if (toolType == typeof(Pan))
        {
            kind = WalletToolKind.Pan;
            return HasStoredAutoUseTool(kind);
        }

        if (toolType == typeof(MilkPail))
        {
            kind = WalletToolKind.MilkPail;
            return HasStoredAutoUseTool(kind);
        }

        if (toolType == typeof(Shears))
        {
            kind = WalletToolKind.Shears;
            return HasStoredAutoUseTool(kind);
        }

        if (anyTool)
        {
            foreach (WalletToolKind candidate in new[] { WalletToolKind.Axe, WalletToolKind.Pickaxe, WalletToolKind.Hoe })
            {
                if (TryFindStoredAutoUseToolForRequest(candidate, out kind))
                    return true;
            }
        }

        kind = WalletToolKind.Axe;
        return false;
    }

    private bool HasStoredEnabledTool(WalletToolKind kind)
    {
        return HasStoredEnabledTool(Game1.player, kind);
    }

    private bool HasStoredEnabledTool(Farmer player, WalletToolKind kind)
    {
        return IsWalletEnabled(kind) && GetStoredTools(player).ContainsKey(kind);
    }

    private bool TryFindStoredEnabledToolForRequest(WalletToolKind requestedKind, out WalletToolKind storedKind)
    {
        return TryFindStoredEnabledToolForRequest(Game1.player, requestedKind, out storedKind);
    }

    private bool TryFindStoredEnabledToolForRequest(Farmer player, WalletToolKind requestedKind, out WalletToolKind storedKind)
    {
        if (HasStoredEnabledTool(player, requestedKind))
        {
            storedKind = requestedKind;
            return true;
        }

        storedKind = requestedKind;
        return false;
    }

    private bool HasStoredAutoUseTool(WalletToolKind kind)
    {
        return HasStoredAutoUseTool(Game1.player, kind);
    }

    private bool HasStoredAutoUseTool(Farmer player, WalletToolKind kind)
    {
        return IsAutoUseEnabled(kind) && GetStoredTools(player).ContainsKey(kind);
    }

    private bool TryFindStoredAutoUseToolForRequest(WalletToolKind requestedKind, out WalletToolKind storedKind)
    {
        if (HasStoredAutoUseTool(requestedKind))
        {
            storedKind = requestedKind;
            return true;
        }

        storedKind = requestedKind;
        return false;
    }


    private bool IsExternalSwitchModLoaded()
    {
        return Helper.ModRegistry.IsLoaded("aedenthorn.ToolSmartSwitch")
            || Helper.ModRegistry.IsLoaded("Trapyy.AutomatetoolSwap");
    }

    private bool TryPrepareWalletToolUse(Farmer player)
    {
        if (PendingToolUse is not null || !Config.ModEnabled || !Config.AutoUseEnabled || Game1.fadeToBlack || !Context.CanPlayerMove || IsToolUpgradeLocation(player.currentLocation))
            return false;

        if (TryGetFallbackAnimalToolRequest(player, out Type animalToolType))
            return TrySupplyRequestedWalletTool(player, animalToolType, false);

        if (IsExternalSwitchModLoaded())
            return false;

        if (!TryGetFallbackToolRequest(player, out Type toolType, out bool anyTool))
            return false;

        return TrySupplyRequestedWalletTool(player, toolType, anyTool);
    }

    private static bool TryGetFallbackAnimalToolRequest(Farmer player, out Type toolType)
    {
        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        tile = new Vector2((int)tile.X, (int)tile.Y);

        return TryGetToolRequestForFarmAnimal(player.currentLocation, tile, player, out toolType);
    }

    private bool TryGetFallbackToolRequest(Farmer player, out Type toolType, out bool anyTool)
    {
        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        tile = new Vector2((int)tile.X, (int)tile.Y);

        if (TryGetToolRequestForFarmAnimal(player.currentLocation, tile, player, out toolType))
        {
            anyTool = false;
            return true;
        }

        if (player.currentLocation.objects.TryGetValue(tile, out Object obj) && TryGetToolRequestForObject(obj, out toolType, out anyTool))
            return true;

        if (player.currentLocation.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
        {
            if (TryGetToolRequestForTerrainFeature(feature, out toolType, out anyTool))
                return true;

            if (player.currentLocation is Farm && feature is HoeDirt dirt && dirt.state.Value == 0)
            {
                toolType = typeof(WateringCan);
                anyTool = false;
                return true;
            }

            if (feature is HoeDirt cropDirt && cropDirt.crop is not null && cropDirt.crop.forageCrop.Value && cropDirt.crop.whichForageCrop.Value == Crop.forageCrop_ginger.ToString())
            {
                toolType = typeof(Hoe);
                anyTool = false;
                return true;
            }
        }

        {
            Rectangle tileRect = new((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
            foreach (ResourceClump clump in player.currentLocation.resourceClumps)
            {
                if (clump.getBoundingBox().Intersects(tileRect) && TryGetToolRequestForClump(clump, out toolType))
                {
                    anyTool = false;
                    return true;
                }
            }
        }

        {
            int panX = player.currentLocation.orePanPoint.X;
            int panY = player.currentLocation.orePanPoint.Y;
            if (panX != 0 || panY != 0)
            {
                Rectangle panRect = new(panX * 64 - 64, panY * 64 - 64, 256, 256);
                if (panRect.Contains((int)tile.X * 64, (int)tile.Y * 64) && Utility.distance(player.getStandingPosition().X, panRect.Center.X, player.getStandingPosition().Y, panRect.Center.Y) <= 192f)
                {
                    toolType = typeof(Pan);
                    anyTool = false;
                    return true;
                }
            }
        }

        if (TryNeedsWateringCan(player, tile))
        {
            toolType = typeof(WateringCan);
            anyTool = false;
            return true;
        }

        if (IsDiggableHoeTile(player.currentLocation, tile))
        {
            toolType = typeof(Hoe);
            anyTool = false;
            return true;
        }

        toolType = typeof(Axe);
        anyTool = false;
        return false;
    }

    private static bool TryGetToolRequestForFarmAnimal(GameLocation location, Vector2 tile, Farmer player, out Type toolType)
    {
        if (location is not (Farm or AnimalHouse))
        {
            toolType = typeof(Axe);
            return false;
        }

        foreach (FarmAnimal animal in location.getAllFarmAnimals())
        {
            if (animal.currentLocation != player.currentLocation)
                continue;

            if (Vector2.Distance(tile, animal.Tile) > 1f)
                continue;

            if (animal.type.Contains("Cow") || animal.type.Contains("Goat"))
            {
                toolType = typeof(MilkPail);
                return true;
            }

            if (animal.type.Contains("Sheep"))
            {
                toolType = typeof(Shears);
                return true;
            }
        }

        toolType = typeof(Axe);
        return false;
    }

    private static bool TryGetToolRequestForObject(Object obj, out Type toolType, out bool anyTool)
    {
        anyTool = false;

        if (obj.Name.Equals("Stone", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(Pickaxe);
            return true;
        }

        if (obj.Name.Contains("Twig", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(Axe);
            return true;
        }

        if (obj.ParentSheetIndex == 590)
        {
            toolType = typeof(Hoe);
            return true;
        }

        if (obj is BreakableContainer)
        {
            toolType = typeof(Axe);
            anyTool = true;
            return true;
        }

        toolType = typeof(Axe);
        return false;
    }

    private bool TryGetToolRequestForTerrainFeature(TerrainFeature feature, out Type toolType, out bool anyTool)
    {
        anyTool = false;

        if (feature is Tree tree)
        {
            if (tree.growthStage.Value >= 3)
            {
                toolType = typeof(Axe);
                return true;
            }

            toolType = typeof(Axe);
            anyTool = true;
            return true;
        }

        toolType = typeof(Axe);
        return false;
    }

    private static bool TryGetToolRequestForClump(ResourceClump clump, out Type toolType)
    {
        if (clump.parentSheetIndex.Value == 600 || clump.parentSheetIndex.Value == 602)
        {
            toolType = typeof(Axe);
            return true;
        }

        if (new[] { 622, 672, 752, 754, 756, 758 }.Contains(clump.parentSheetIndex.Value))
        {
            toolType = typeof(Pickaxe);
            return true;
        }

        toolType = typeof(Axe);
        return false;
    }

    private static bool IsDiggableHoeTile(GameLocation location, Vector2 tile)
    {
        int x = (int)tile.X;
        int y = (int)tile.Y;

        if (location.objects.ContainsKey(tile) || location.terrainFeatures.ContainsKey(tile))
            return false;

        return location.doesTileHaveProperty(x, y, "Diggable", "Back") is not null
            || location.doesTileHaveProperty(x, y, "Diggable", "Buildings") is not null;
    }

    private static bool TryNeedsWateringCan(Farmer player, Vector2 tile)
    {
        GameLocation location = player.currentLocation;
        int x = (int)tile.X;
        int y = (int)tile.Y;

        if (location is VolcanoDungeon volcano && volcano.level.Value != 5 && location.isTileOnMap(tile) && location.waterTiles[x, y] && !volcano.cooledLavaTiles.ContainsKey(tile))
            return true;

        if (location.isTileOnMap(tile) && location.CanRefillWateringCanOnTile(x, y))
            return true;

        if (location is Farm farm)
        {
            foreach (Building building in farm.buildings)
            {
                if (!building.isMoving && building.occupiesTile(tile, true))
                {
                    string? tileProperty = null;
                    if (building.doesTileHaveProperty(x, y, "PetBowl", "Buildings", ref tileProperty))
                        return true;
                    break;
                }
            }
        }

        return location.objects.TryGetValue(tile, out Object bowl) && bowl.Name.EndsWith("Pet Bowl", StringComparison.OrdinalIgnoreCase);
    }

    private bool TryMaterializeWalletToolForManualUse(Farmer player, WalletToolKind kind)
    {
        return TrySetTemporaryWalletTool(player, kind);
    }

    private bool TryMaterializeWalletToolForAutomaticUse(Farmer player, WalletToolKind kind)
    {
        return IsAutoUseEnabled(kind) && TrySetTemporaryWalletTool(player, kind);
    }

    private bool TrySetTemporaryWalletTool(Farmer player, WalletToolKind kind)
    {
        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        if (PendingToolUse is not null || !Config.ModEnabled || IsToolUpgradeLocation(player.currentLocation) || !storedTools.TryGetValue(kind, out WalletToolState? state))
        {
            return false;
        }

        Tool? walletTool = state.CreateTool(Monitor);
        if (walletTool is null)
            return false;

        int previousToolIndex = player.CurrentToolIndex;
        int maxItems = GetPlayerMaxItemCount(player);
        if (previousToolIndex < 0 || previousToolIndex >= maxItems)
        {
            return false;
        }

        SuppressInventoryConversion = true;
        bool leasedFromWallet = false;
        try
        {
            while (player.Items.Count <= previousToolIndex && player.Items.Count < maxItems)
                player.Items.Add(null);

            if (previousToolIndex >= player.Items.Count)
            {
                return false;
            }

            Item? previousItem = player.Items[previousToolIndex];
            UpdateToolEnchantmentsForSelectedSlot(player, previousItem as Tool, walletTool, "temporary wallet tool activation");
            MarkRuntimeTool(walletTool, kind, player);
            storedTools.Remove(kind);
            leasedFromWallet = true;
            RefreshWalletStateAfterStoredToolChange(player, false);
            player.Items[previousToolIndex] = walletTool;
            player.CurrentToolIndex = previousToolIndex;
            PendingToolUse = new TemporaryToolUse(previousToolIndex, previousToolIndex, kind, walletTool, previousItem);
        }
        catch
        {
            if (leasedFromWallet)
            {
                ClearWalletMarkers(walletTool);
                storedTools[kind] = WalletToolState.FromTool(kind, walletTool);
                RefreshWalletStateAfterStoredToolChange(player, false);
            }

            throw;
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (Config.PlayToolSwapSound)
            Game1.playSound("toolSwap");

        return true;
    }

    private void UpdateToolEnchantmentsForSelectedSlot(Farmer player, Tool? oldTool, Tool? newTool, string reason)
    {
        if (ReferenceEquals(oldTool, newTool))
            return;

        try
        {
            if (oldTool is not null)
            {
                foreach (var enchantment in oldTool.enchantments)
                    enchantment.OnUnequip(player);
            }

            if (newTool is not null)
            {
                foreach (var enchantment in newTool.enchantments)
                    enchantment.OnEquip(player);
            }

        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not update vanilla enchantment equip state during {reason}: {ex.Message}", LogLevel.Warn);
        }
    }

    private static void MarkRuntimeTool(Tool tool, WalletToolKind kind, Farmer player)
    {
        tool.modData[RuntimeToolMarker] = "true";
        tool.modData[RuntimeToolKindMarker] = kind.ToString();
        MarkWalletOwner(tool, player);
    }

    private static bool IsRuntimeTool(Item? item)
    {
        return item is Tool tool && tool.modData.ContainsKey(RuntimeToolMarker);
    }

    private static bool IsRuntimeToolForPlayer(Item? item, Farmer player)
    {
        return item is Tool tool && tool.modData.ContainsKey(RuntimeToolMarker) && IsOwnedByPlayer(tool, player);
    }

    private void RestorePendingToolUseBeforeDestructiveCleanup(Farmer player)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);
    }

    private void RestorePendingToolUseToInventoryBeforeVolatileClear(Farmer player)
    {
        if (PendingToolUse is null)
            return;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        TemporaryToolUse pending = PendingToolUse;
        PendingToolUse = null;
        PendingManualUseHotkey = null;

        SuppressInventoryConversion = true;
        try
        {
            int temporarySlot = FindTemporaryToolSlot(player, pending);
            Tool? toolToPreserve = temporarySlot >= 0 && temporarySlot < player.Items.Count
                ? player.Items[temporarySlot] as Tool
                : null;

            if (toolToPreserve is not null && temporarySlot >= 0 && temporarySlot < player.Items.Count)
                player.Items[temporarySlot] = null;
            else if (storedTools.TryGetValue(pending.Kind, out WalletToolState? storedState))
            {
                toolToPreserve = storedState.CreateTool(Monitor);
                storedTools.Remove(pending.Kind);
            }

            if (toolToPreserve is not null)
                ClearWalletMarkers(toolToPreserve);

            RestoreTemporaryToolSlot(player, pending);

            if (toolToPreserve is not null)
                AddToolToInventoryOrAppend(player, toolToPreserve);

            player.CurrentToolIndex = Math.Max(0, Math.Min(pending.PreviousToolIndex, Math.Max(0, player.Items.Count - 1)));
            NormalizePlayerItemsAfterWalletCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private static void RemoveRuntimeToolCopies(Farmer player)
    {
        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (IsRuntimeToolForPlayer(player.Items[i], player))
            {
                player.Items[i] = null;
                removed = true;
            }
        }

        if (removed)
            NormalizePlayerItemsAfterWalletCleanup(player);
    }

    private void RestoreTemporaryTool(Farmer player)
    {
        if (PendingToolUse is null)
            return;

        Dictionary<WalletToolKind, WalletToolState> storedTools = GetStoredTools(player);
        TemporaryToolUse pending = PendingToolUse;
        PendingToolUse = null;
        PendingManualUseHotkey = null;
        SuppressInventoryConversion = true;
        try
        {
            int temporarySlot = FindTemporaryToolSlot(player, pending);
            Tool? usedTool = temporarySlot >= 0 && temporarySlot < player.Items.Count
                ? player.Items[temporarySlot] as Tool
                : null;

            if (usedTool is not null)
            {
                if (temporarySlot >= 0 && temporarySlot < player.Items.Count)
                    player.Items[temporarySlot] = null;

                ClearWalletMarkers(usedTool);
                storedTools[pending.Kind] = WalletToolState.FromTool(pending.Kind, usedTool);
                RefreshWalletStateAfterStoredToolChange(player, false);
            }
            else
            {
                RemoveRuntimeToolCopies(player);
                storedTools.Remove(pending.Kind);
                RefreshWalletStateAfterStoredToolChange(player, false);
            }

            UpdateToolEnchantmentsForSelectedSlot(player, usedTool, pending.PreviousItem as Tool, "temporary wallet tool restore");
            RestoreTemporaryToolSlot(player, pending);
            player.CurrentToolIndex = Math.Max(0, Math.Min(pending.PreviousToolIndex, Math.Max(0, player.Items.Count - 1)));
            NormalizePlayerItemsAfterWalletCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private static int FindTemporaryToolSlot(Farmer player, TemporaryToolUse pending)
    {
        if (pending.Slot >= 0 && pending.Slot < player.Items.Count)
        {
            Item? slotItem = player.Items[pending.Slot];
            if (ReferenceEquals(slotItem, pending.TemporaryTool))
                return pending.Slot;

            if (IsPendingRuntimeToolForPlayer(slotItem, player, pending.Kind))
                return pending.Slot;
        }

        for (int i = 0; i < player.Items.Count; i++)
        {
            Item? item = player.Items[i];
            if (ReferenceEquals(item, pending.TemporaryTool))
                return i;

            if (IsPendingRuntimeToolForPlayer(item, player, pending.Kind))
                return i;
        }

        return -1;
    }

    private static bool IsPendingRuntimeToolForPlayer(Item? item, Farmer player, WalletToolKind kind)
    {
        if (item is not Tool tool || !tool.modData.ContainsKey(RuntimeToolMarker) || !IsOwnedByPlayer(tool, player))
            return false;

        if (!tool.modData.TryGetValue(RuntimeToolKindMarker, out string? rawKind) || !Enum.TryParse(rawKind, out WalletToolKind runtimeKind))
            return false;

        return runtimeKind == kind;
    }

    private static void RestoreTemporaryToolSlot(Farmer player, TemporaryToolUse pending)
    {
        if (pending.Slot < 0 || pending.Slot >= player.Items.Count)
        {
            if (pending.PreviousItem is not null && !InventoryContainsReference(player, pending.PreviousItem))
                player.addItemByMenuIfNecessary(pending.PreviousItem);
            return;
        }

        Item? currentItem = player.Items[pending.Slot];
        if (currentItem is null || ReferenceEquals(currentItem, pending.TemporaryTool) || IsPendingRuntimeToolForPlayer(currentItem, player, pending.Kind))
        {
            player.Items[pending.Slot] = pending.PreviousItem;
            return;
        }

        if (pending.PreviousItem is not null && !ReferenceEquals(currentItem, pending.PreviousItem) && !InventoryContainsReference(player, pending.PreviousItem))
            player.addItemByMenuIfNecessary(pending.PreviousItem);
    }

    private static bool InventoryContainsReference(Farmer player, Item item)
    {
        foreach (Item? existingItem in player.Items)
        {
            if (ReferenceEquals(existingItem, item))
                return true;
        }

        return false;
    }

    private static bool IsCurrentlySelectedTool(Farmer player, int slot, Tool tool)
    {
        if (slot == player.CurrentToolIndex)
            return true;

        return ReferenceEquals(player.CurrentTool, tool);
    }

    private static bool IsPlayerUsingTool(Farmer player)
    {
        return player.UsingTool;
    }

    internal int GetHighestStoredCoreToolLevelForApi()
    {
        int highest = 0;
        foreach (WalletToolKind kind in new[] { WalletToolKind.Axe, WalletToolKind.Pickaxe, WalletToolKind.Hoe, WalletToolKind.WateringCan })
        {
            if (StoredTools.TryGetValue(kind, out WalletToolState? state) && !state.IsInvalidStoredTool())
                highest = Math.Max(highest, state.GetPowerScore());
        }

        return highest;
    }

    internal bool TryGetStoredToolForApi(string toolKind, out WalletToolState state)
    {
        state = null!;
        if (!TryParseWalletToolKind(toolKind, out WalletToolKind kind))
            return false;

        if (!StoredTools.TryGetValue(kind, out WalletToolState? storedState) || storedState.IsInvalidStoredTool())
            return false;

        state = storedState;
        return true;
    }

    internal bool IsToolAutoUseEnabledForApi(string toolKind)
    {
        return TryParseWalletToolKind(toolKind, out WalletToolKind kind) && IsAutoUseEnabled(kind);
    }

    internal IEnumerable<string> GetStoredToolKindsForApi()
    {
        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
        {
            if (StoredTools.TryGetValue(kind, out WalletToolState? state) && !state.IsInvalidStoredTool())
                yield return kind.ToString();
        }
    }


    private static bool TryParseWalletToolKind(string toolKind, out WalletToolKind kind)
    {
        string normalized = (toolKind ?? string.Empty).Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        foreach (WalletToolKind candidate in Enum.GetValues<WalletToolKind>())
        {
            if (candidate.ToString().Equals(normalized, StringComparison.OrdinalIgnoreCase))
            {
                kind = candidate;
                return true;
            }
        }

        kind = WalletToolKind.Axe;
        return false;
    }
    private static class IsOneOfTheseKeysDownPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(Game1)).Where(method => method.Name == nameof(Game1.isOneOfTheseKeysDown));
        }

        public static void Postfix(object[] __args, ref bool __result)
        {
            if (!__result && TryGetInputButtons(__args, out InputButton[] keys) && IsUseToolButtonBinding(keys) && Instance?.IsManualWalletToolHotkeyHeld() == true)
                __result = true;
        }
    }
    private static class AreAllOfTheseKeysUpPatch
    {
        public static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(Game1)).Where(method => method.Name == nameof(Game1.areAllOfTheseKeysUp));
        }

        public static void Postfix(object[] __args, ref bool __result)
        {
            if (__result && TryGetInputButtons(__args, out InputButton[] keys) && IsUseToolButtonBinding(keys) && Instance?.IsManualWalletToolHotkeyHeld() == true)
                __result = false;
        }
    }

    private static bool TryGetInputButtons(object[] args, out InputButton[] keys)
    {
        foreach (object arg in args)
        {
            if (arg is InputButton[] inputButtons)
            {
                keys = inputButtons;
                return true;
            }
        }

        keys = Array.Empty<InputButton>();
        return false;
    }
    private static class CheckIsMissingToolPatch
    {
        public static void Postfix(Dictionary<Type, int> missingTools, ref int missingScythes, Item item)
        {
            if (Instance is null || item is not Tool tool || !IsOvernightExposure(tool, out WalletToolKind kind))
                return;

            if (Context.IsMultiplayer && !HasOwnerMarker(tool))
                return;

            Instance.ApplyWalletToolOwnershipToMissingToolScan(missingTools, ref missingScythes, kind, tool);
        }
    }

    private void ApplyWalletToolOwnershipToMissingToolScan(Dictionary<Type, int> missingTools, ref int missingScythes, WalletToolKind kind, Tool tool)
    {
        Type? vanillaType = GetVanillaMissingToolType(kind);
        if (vanillaType is not null && missingTools.TryGetValue(vanillaType, out int count) && count > 0)
        {
            missingTools[vanillaType] = count - 1;
        }
    }

    private static Type? GetVanillaMissingToolType(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => typeof(Axe),
            WalletToolKind.Pickaxe => typeof(Pickaxe),
            WalletToolKind.Hoe => typeof(Hoe),
            WalletToolKind.WateringCan => typeof(WateringCan),
            WalletToolKind.Pan => typeof(Pan),
            WalletToolKind.MilkPail => typeof(MilkPail),
            WalletToolKind.Shears => typeof(Shears),
            _ => null
        };
    }
    private static class PressUseToolButtonPatch
    {
        public static void Prefix()
        {
            Instance?.TryPrepareWalletToolUse(Game1.player);
        }
    }
}

internal enum WalletToolKind
{
    Axe,
    Pickaxe,
    Hoe,
    WateringCan,
    Pan,
    MilkPail,
    Shears
}

internal static class ToolAndSprinklerUpgradesCompat
{
    public static bool IsUpgradeTool(Tool tool, string suffix)
    {
        string text = GetIdentityText(tool);
        return text.Contains($"ToolAndSprinklerUpgrades_Cobalt{suffix}", StringComparison.OrdinalIgnoreCase)
            || text.Contains($"ToolAndSprinklerUpgrades_Prismatic{suffix}", StringComparison.OrdinalIgnoreCase)
            || text.Contains($"ToolAndSprinklerUpgrades_Radioactive{suffix}", StringComparison.OrdinalIgnoreCase);
    }

    public static int GetUpgradeLevel(Tool tool)
    {
        string text = GetIdentityText(tool);
        if (text.Contains("ToolAndSprinklerUpgrades_Radioactive", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (text.Contains("ToolAndSprinklerUpgrades_Prismatic", StringComparison.OrdinalIgnoreCase))
            return 6;
        if (text.Contains("ToolAndSprinklerUpgrades_Cobalt", StringComparison.OrdinalIgnoreCase))
            return 5;

        return 0;
    }

    public static bool IsPan(Tool tool)
    {
        return IsPanIdentity(tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
    }

    public static bool IsPanIdentity(params string[] values)
    {
        return values.Any(value =>
            !string.IsNullOrWhiteSpace(value)
            && (value.Contains("ToolAndSprinklerUpgrades_CobaltPan", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ToolAndSprinklerUpgrades_PrismaticPan", StringComparison.OrdinalIgnoreCase)
                || value.Contains("ToolAndSprinklerUpgrades_RadioactivePan", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Cobalt Pan", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Prismatic Pan", StringComparison.OrdinalIgnoreCase)
                || value.Equals("Radioactive Pan", StringComparison.OrdinalIgnoreCase)));
    }

    public static int GetPanMenuSpriteIndex()
    {
        return 20;
    }

    private static string GetIdentityText(Tool tool)
    {
        return string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
    }
}

internal sealed class BlacksmithExposure
{
    public BlacksmithExposure(int slot, Item? previousItem, WalletToolKind kind, Tool tool)
    {
        Slot = slot;
        PreviousItem = previousItem;
        Kind = kind;
        Tool = tool;
    }

    public int Slot { get; }
    public Item? PreviousItem { get; }
    public WalletToolKind Kind { get; }
    public Tool Tool { get; }
}

internal sealed class TemporaryToolUse
{
    public TemporaryToolUse(int slot, int previousToolIndex, WalletToolKind kind, Tool temporaryTool, Item? previousItem)
    {
        Slot = slot;
        PreviousToolIndex = previousToolIndex;
        Kind = kind;
        TemporaryTool = temporaryTool;
        PreviousItem = previousItem;
    }

    public int Slot { get; }
    public int PreviousToolIndex { get; }
    public WalletToolKind Kind { get; }
    public Tool TemporaryTool { get; }
    public Item? PreviousItem { get; }
}

internal sealed class WalletToolState
{
    private Tool? LiveTool;

    public WalletToolKind Kind { get; set; }
    public string QualifiedItemId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int UpgradeLevel { get; set; }
    public int MenuSpriteIndex { get; set; } = -1;
    public string TexturePath { get; set; } = string.Empty;
    public Dictionary<string, string> ModData { get; set; } = new();

    public static WalletToolState FromTool(WalletToolKind kind, Tool tool)
    {
        WalletToolState state = new()
        {
            Kind = kind,
            QualifiedItemId = string.IsNullOrWhiteSpace(tool.QualifiedItemId) ? GetDefaultQualifiedItemId(kind) : tool.QualifiedItemId,
            Name = tool.Name,
            DisplayName = tool.DisplayName,
            Description = tool.getDescription(),
            UpgradeLevel = GetEffectiveUpgradeLevel(kind, tool),
            MenuSpriteIndex = GetToolMenuSpriteIndex(tool),
            TexturePath = GetToolTexturePath(tool)
        };

        if (tool.getOne() is Tool liveCopy)
            state.LiveTool = liveCopy;

        foreach (KeyValuePair<string, string> pair in tool.modData.Pairs)
        {
            if (pair.Key == "ThaleTheGreat.WalletTools/RuntimeTool" || pair.Key == "ThaleTheGreat.WalletTools/RuntimeToolKind" || pair.Key == "ThaleTheGreat.WalletTools/OvernightExposure" || pair.Key == "ThaleTheGreat.WalletTools/OvernightExposureKind")
                continue;

            state.ModData[pair.Key] = pair.Value;
        }

        return state;
    }

    public string GetDisplayName()
    {
        Tool? tool = CreateTool(null);
        if (tool is not null && !string.IsNullOrWhiteSpace(tool.DisplayName))
            return tool.DisplayName;

        if (!string.IsNullOrWhiteSpace(DisplayName))
            return DisplayName;

        if (!string.IsNullOrWhiteSpace(Name))
            return Name;

        return GetFallbackName(Kind);
    }

    public string GetDescription()
    {
        Tool? tool = CreateTool(null);
        if (tool is not null)
            return AppendVanillaEnhancementSummary(tool.getDescription(), tool);

        if (!string.IsNullOrWhiteSpace(Description))
            return Description;

        return string.Empty;
    }

    private static string AppendVanillaEnhancementSummary(string description, Tool tool)
    {
        List<string> lines = new();

        foreach (var enchantment in tool.enchantments)
        {
            string name = GetVanillaEnchantmentName(enchantment);
            int level = GetVanillaEnchantmentLevel(enchantment);
            lines.Add(level > 0 ? $"{name} {level}" : name);
        }

        if (tool.attachments is not null)
        {
            foreach (Object? attachment in tool.attachments)
            {
                if (attachment is not null)
                    lines.Add(attachment.DisplayName);
            }
        }

        if (lines.Count == 0)
            return description;

        string suffix = "Enhancement: " + string.Join(", ", lines.Distinct());
        if (string.IsNullOrWhiteSpace(description))
            return suffix;

        return description + Environment.NewLine + suffix;
    }

    private static string GetVanillaEnchantmentName(object enchantment)
    {
        try
        {
            MethodInfo? getName = AccessTools.Method(enchantment.GetType(), "GetName", Type.EmptyTypes);
            if (getName?.Invoke(enchantment, null) is string name && !string.IsNullOrWhiteSpace(name))
                return name;
        }
        catch
        {
        }

        string typeName = enchantment.GetType().Name;
        return typeName.EndsWith("Enchantment", StringComparison.OrdinalIgnoreCase)
            ? typeName[..^"Enchantment".Length]
            : typeName;
    }

    private static int GetVanillaEnchantmentLevel(object enchantment)
    {
        try
        {
            PropertyInfo? levelProperty = AccessTools.Property(enchantment.GetType(), "Level");
            if (levelProperty?.GetValue(enchantment) is int propertyLevel)
                return propertyLevel;

            FieldInfo? levelField = AccessTools.Field(enchantment.GetType(), "Level");
            if (levelField?.GetValue(enchantment) is int fieldLevel)
                return fieldLevel;
        }
        catch
        {
        }

        return 0;
    }

    public string GetTexturePath()
    {
        Tool? tool = CreateTool(null);
        if (tool is not null)
        {
            string currentTexturePath = GetToolTexturePath(tool);
            if (!string.IsNullOrWhiteSpace(currentTexturePath))
                return currentTexturePath;
        }

        if (!string.IsNullOrWhiteSpace(TexturePath))
            return TexturePath;

        if (Kind == WalletToolKind.Pan && IsBasicPanItemId(QualifiedItemId))
        {
            string panTexturePath = GetTexturePathForQualifiedItemId(GetDefaultPanQualifiedItemId(UpgradeLevel));
            if (!string.IsNullOrWhiteSpace(panTexturePath))
                return panTexturePath;
        }

        return "TileSheets/tools";
    }

    public Point GetTexturePosition()
    {
        if (Kind == WalletToolKind.Pan && IsToolAndSprinklerPanIdentity(QualifiedItemId, Name, DisplayName))
            return GetTexturePositionFromMenuIndex(GetToolAndSprinklerPanMenuSpriteIndex());

        Tool? tool = CreateTool(null);
        if (tool is not null && IsToolAndSprinklerPanTool(tool))
            return GetTexturePositionFromMenuIndex(GetToolAndSprinklerPanMenuSpriteIndex());

        int menuSpriteIndex = tool is not null ? GetToolMenuSpriteIndex(tool) : MenuSpriteIndex;

        if (menuSpriteIndex >= 0)
            return GetTexturePositionFromMenuIndex(menuSpriteIndex);

        if (Kind == WalletToolKind.Pan && IsBasicPanItemId(QualifiedItemId))
        {
            int panSpriteIndex = GetMenuSpriteIndexForQualifiedItemId(GetDefaultPanQualifiedItemId(UpgradeLevel));
            if (panSpriteIndex >= 0)
                return GetTexturePositionFromMenuIndex(panSpriteIndex);
        }

        return GetFallbackTexturePosition(Kind, UpgradeLevel);
    }

    public int GetPowerScore()
    {
        return Math.Max(0, UpgradeLevel);
    }

    public bool IsInvalidStoredTool()
    {
        Tool? tool = CreateTool(null);
        return tool is null || IsErrorTool(tool);
    }

    public static bool IsErrorTool(Tool tool)
    {
        string name = string.Join("\n", tool.Name, tool.DisplayName, tool.ItemId, tool.QualifiedItemId);
        return name.Contains("Error Item", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ErrorItem", StringComparison.OrdinalIgnoreCase);
    }


    public Tool? CreateTool(IMonitor? monitor)
    {
        if (LiveTool?.getOne() is Tool liveCopy && !IsErrorTool(liveCopy))
            return liveCopy;

        Exception? lastException = null;
        foreach (string itemId in GetCreationQualifiedItemIds())
        {
            try
            {
                Tool tool = ItemRegistry.Create<Tool>(itemId);
                SetIntMember(tool, UpgradeLevel, "UpgradeLevel", "upgradeLevel");
                foreach (KeyValuePair<string, string> pair in ModData)
                {
                    if (pair.Key == "ThaleTheGreat.WalletTools/RuntimeTool" || pair.Key == "ThaleTheGreat.WalletTools/RuntimeToolKind" || pair.Key == "ThaleTheGreat.WalletTools/OvernightExposure" || pair.Key == "ThaleTheGreat.WalletTools/OvernightExposureKind")
                        continue;

                    tool.modData[pair.Key] = pair.Value;
                }

                if (!IsErrorTool(tool))
                    return tool;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }
        }

        if (lastException is not null)
            monitor?.Log($"Could not recreate wallet tool '{Name}' ({QualifiedItemId}): {lastException.Message}", LogLevel.Warn);

        return null;
    }

    private static string GetFallbackName(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => "Axe",
            WalletToolKind.Pickaxe => "Pickaxe",
            WalletToolKind.Hoe => "Hoe",
            WalletToolKind.WateringCan => "Watering Can",
            WalletToolKind.Pan => "Pan",
            WalletToolKind.MilkPail => "Milk Pail",
            WalletToolKind.Shears => "Shears",
            _ => "Tool"
        };
    }


    private IEnumerable<string> GetCreationQualifiedItemIds()
    {
        string itemId = string.IsNullOrWhiteSpace(QualifiedItemId) ? GetDefaultQualifiedItemId(Kind) : QualifiedItemId;
        string primaryItemId = itemId;

        if (Kind == WalletToolKind.Pan && IsBasicPanItemId(itemId) && UpgradeLevel > 0)
            primaryItemId = GetDefaultPanQualifiedItemId(UpgradeLevel);

        yield return primaryItemId;

        if (Kind == WalletToolKind.Pan && IsVanillaPanItemId(primaryItemId))
        {
            string levelItemId = GetDefaultPanQualifiedItemId(UpgradeLevel);
            if (!levelItemId.Equals(primaryItemId, StringComparison.OrdinalIgnoreCase))
                yield return levelItemId;

            if (!"(T)Pan".Equals(primaryItemId, StringComparison.OrdinalIgnoreCase))
                yield return "(T)Pan";
        }
    }

    public static bool IsPanTool(Tool tool)
    {
        if (tool is Pan)
            return true;

        string itemId = NormalizeToolItemId(tool);
        if (itemId is "Pan" or "CopperPan" or "SteelPan" or "GoldPan" or "IridiumPan")
            return true;

        string text = string.Join("\n", tool.Name, tool.DisplayName);
        return text.Contains("Pan", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Pants", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMilkPailTool(Tool tool)
    {
        if (tool is MilkPail)
            return true;

        string itemId = NormalizeToolItemId(tool);
        if (itemId.Equals("MilkPail", StringComparison.OrdinalIgnoreCase))
            return true;

        string text = string.Join("\n", tool.Name, tool.DisplayName);
        return text.Contains("MilkPail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Milk Pail", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShearsTool(Tool tool)
    {
        if (tool is Shears)
            return true;

        string itemId = NormalizeToolItemId(tool);
        if (itemId.Equals("Shears", StringComparison.OrdinalIgnoreCase))
            return true;

        string text = string.Join("\n", tool.Name, tool.DisplayName);
        return text.Contains("Shears", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToolItemId(Tool tool)
    {
        string itemId = string.IsNullOrWhiteSpace(tool.QualifiedItemId) ? tool.ItemId : tool.QualifiedItemId;
        if (string.IsNullOrWhiteSpace(itemId))
            return string.Empty;

        if (itemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
            itemId = itemId[3..];

        int slash = itemId.LastIndexOf('/');
        if (slash >= 0 && slash < itemId.Length - 1)
            itemId = itemId[(slash + 1)..];

        return itemId.Trim();
    }


    private static int GetEffectiveUpgradeLevel(WalletToolKind kind, Tool tool)
    {
        int upgradeLevel = GetIntMember(tool, "UpgradeLevel", "upgradeLevel");
        if (kind == WalletToolKind.Pan)
        {
            int panLevel = GetPanUpgradeLevel(tool);
            string panItemId = NormalizeToolItemId(tool);
            if (panItemId is "Pan" or "CopperPan" or "SteelPan" or "GoldPan" or "IridiumPan")
                upgradeLevel = panLevel;
            else
                upgradeLevel = Math.Max(upgradeLevel, panLevel);
        }

        upgradeLevel = Math.Max(upgradeLevel, GetToolAndSprinklerUpgradeLevel(tool));
        return Math.Max(0, upgradeLevel);
    }

    private static int GetToolAndSprinklerUpgradeLevel(Tool tool)
    {
        return ToolAndSprinklerUpgradesCompat.GetUpgradeLevel(tool);
    }

    private static bool IsToolAndSprinklerPanTool(Tool tool)
    {
        return ToolAndSprinklerUpgradesCompat.IsPan(tool);
    }

    private static bool IsToolAndSprinklerPanIdentity(params string[] values)
    {
        return ToolAndSprinklerUpgradesCompat.IsPanIdentity(values);
    }

    private static int GetToolAndSprinklerPanMenuSpriteIndex()
    {
        return ToolAndSprinklerUpgradesCompat.GetPanMenuSpriteIndex();
    }

    private static int GetPanUpgradeLevel(Tool tool)
    {
        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        if (text.Contains("ToolAndSprinklerUpgrades_RadioactivePan", StringComparison.OrdinalIgnoreCase) || text.Contains("Radioactive Pan", StringComparison.OrdinalIgnoreCase))
            return 7;
        if (text.Contains("ToolAndSprinklerUpgrades_PrismaticPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Prismatic Pan", StringComparison.OrdinalIgnoreCase))
            return 6;
        if (text.Contains("ToolAndSprinklerUpgrades_CobaltPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Cobalt Pan", StringComparison.OrdinalIgnoreCase))
            return 5;
        if (text.Contains("IridiumPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Iridium Pan", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (text.Contains("GoldPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Gold Pan", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (text.Contains("SteelPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Steel Pan", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (text.Contains("CopperPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Copper Pan", StringComparison.OrdinalIgnoreCase))
            return 0;

        return 0;
    }

    private static bool IsBasicPanItemId(string itemId)
    {
        string normalized = itemId.Trim();
        return normalized.Equals("Pan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("(T)Pan", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVanillaPanItemId(string itemId)
    {
        string normalized = itemId.Trim();
        if (normalized.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];

        return normalized.Equals("Pan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("CopperPan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("SteelPan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("GoldPan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("IridiumPan", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultPanQualifiedItemId(int upgradeLevel)
    {
        return Math.Max(0, Math.Min(upgradeLevel, 3)) switch
        {
            1 => "(T)SteelPan",
            2 => "(T)GoldPan",
            3 => "(T)IridiumPan",
            _ => "(T)Pan"
        };
    }

    private static int GetToolMenuSpriteIndex(Tool tool)
    {
        if (IsToolAndSprinklerPanTool(tool))
            return GetToolAndSprinklerPanMenuSpriteIndex();

        object? data = GetParsedItemData(tool);
        int? dataMenuIndex = GetOptionalIntMemberFromObject(data, "MenuSpriteIndex", "menuSpriteIndex", "IndexOfMenuItemView", "indexOfMenuItemView");
        if (dataMenuIndex.HasValue && dataMenuIndex.Value >= 0)
            return dataMenuIndex.Value;

        int? dataSpriteIndex = GetOptionalIntMemberFromObject(data, "SpriteIndex", "spriteIndex");
        if (dataSpriteIndex.HasValue && dataSpriteIndex.Value >= 0)
            return dataSpriteIndex.Value;

        int? directIndex = GetOptionalIntMember(tool, "IndexOfMenuItemView", "indexOfMenuItemView", "currentParentTileIndex", "CurrentParentTileIndex");
        if (directIndex.HasValue && directIndex.Value >= 0)
            return directIndex.Value;

        return -1;
    }

    private static string GetToolTexturePath(Tool tool)
    {
        object? data = GetParsedItemData(tool);
        string? texture = GetOptionalStringMemberFromObject(data, "TextureName", "textureName", "Texture", "texture");
        return string.IsNullOrWhiteSpace(texture) ? "TileSheets/tools" : texture;
    }

    private static int GetMenuSpriteIndexForQualifiedItemId(string qualifiedItemId)
    {
        object? data = GetParsedItemData(qualifiedItemId);
        int? dataMenuIndex = GetOptionalIntMemberFromObject(data, "MenuSpriteIndex", "menuSpriteIndex", "IndexOfMenuItemView", "indexOfMenuItemView");
        if (dataMenuIndex.HasValue && dataMenuIndex.Value >= 0)
            return dataMenuIndex.Value;

        int? dataSpriteIndex = GetOptionalIntMemberFromObject(data, "SpriteIndex", "spriteIndex");
        return dataSpriteIndex ?? -1;
    }

    private static string GetTexturePathForQualifiedItemId(string qualifiedItemId)
    {
        object? data = GetParsedItemData(qualifiedItemId);
        string? texture = GetOptionalStringMemberFromObject(data, "TextureName", "textureName", "Texture", "texture");
        return string.IsNullOrWhiteSpace(texture) ? "TileSheets/tools" : texture;
    }

    private static object? GetParsedItemData(Tool tool)
    {
        string itemId = string.IsNullOrWhiteSpace(tool.QualifiedItemId) ? tool.ItemId : tool.QualifiedItemId;
        return GetParsedItemData(itemId);
    }

    private static object? GetParsedItemData(string itemId)
    {
        if (string.IsNullOrWhiteSpace(itemId))
            return null;

        try
        {
            MethodInfo? method = typeof(ItemRegistry).GetMethod("GetDataOrErrorItem", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
            return method?.Invoke(null, new object[] { itemId });
        }
        catch
        {
            return null;
        }
    }

    private static string? GetOptionalStringMemberFromObject(object? obj, params string[] names)
    {
        if (obj is null)
            return null;

        Type type = obj.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetValue(obj) is string propertyValue)
                return propertyValue;

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null && field.GetValue(obj) is string fieldValue)
                return fieldValue;
        }

        return null;
    }

    private static int? GetOptionalIntMemberFromObject(object? obj, params string[] names)
    {
        if (obj is null)
            return null;

        Type type = obj.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
            {
                object? propertyRawValue = property.GetValue(obj);
                if (propertyRawValue is int propertyValue)
                    return propertyValue;
                if (propertyRawValue is Netcode.NetInt propertyNetInt)
                    return propertyNetInt.Value;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                object? value = field.GetValue(obj);
                if (value is int fieldValue)
                    return fieldValue;
                if (value is Netcode.NetInt netInt)
                    return netInt.Value;
            }
        }

        return null;
    }

    private static Point GetTexturePositionFromMenuIndex(int menuSpriteIndex)
    {
        const int ToolSheetWidth = 336;
        int pixels = Math.Max(0, menuSpriteIndex) * 16;
        return new Point(pixels % ToolSheetWidth, pixels / ToolSheetWidth * 16);
    }

    private static Point GetFallbackTexturePosition(WalletToolKind kind, int upgradeLevel)
    {
        int level = Math.Max(0, Math.Min(upgradeLevel, 4));

        return kind switch
        {
            WalletToolKind.Pickaxe => new Point(level * 16, 32),
            WalletToolKind.WateringCan => new Point(16 + level * 16, 32),
            WalletToolKind.Hoe => new Point(32 + level * 16, 32),
            WalletToolKind.Axe => new Point(64 + level * 16, 32),
            WalletToolKind.Pan => new Point(192 + level * 16, 0),
            WalletToolKind.MilkPail => new Point(128, 0),
            WalletToolKind.Shears => new Point(176, 0),
            _ => new Point(64, 32)
        };
    }

    private static string GetDefaultQualifiedItemId(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => "(T)Axe",
            WalletToolKind.Pickaxe => "(T)Pickaxe",
            WalletToolKind.Hoe => "(T)Hoe",
            WalletToolKind.WateringCan => "(T)WateringCan",
            WalletToolKind.Pan => "(T)Pan",
            WalletToolKind.MilkPail => "(T)MilkPail",
            WalletToolKind.Shears => "(T)Shears",
            _ => "(T)Axe"
        };
    }

    private static int? GetOptionalIntMember(object obj, params string[] names)
    {
        Type type = obj.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
            {
                object? propertyRawValue = property.GetValue(obj);
                if (propertyRawValue is int propertyValue)
                    return propertyValue;
                if (propertyRawValue is Netcode.NetInt propertyNetInt)
                    return propertyNetInt.Value;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                object? value = field.GetValue(obj);
                if (value is int fieldValue)
                    return fieldValue;
                if (value is Netcode.NetInt netInt)
                    return netInt.Value;
            }
        }

        return null;
    }

    internal static int GetIntMember(object obj, params string[] names)
    {
        Type type = obj.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetValue(obj) is int propertyValue)
                return propertyValue;

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                object? value = field.GetValue(obj);
                if (value is int fieldValue)
                    return fieldValue;
                if (value is Netcode.NetInt netInt)
                    return netInt.Value;
            }
        }

        return 0;
    }

    private static void SetIntMember(object obj, int value, params string[] names)
    {
        Type type = obj.GetType();
        foreach (string name in names)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.CanWrite && property.PropertyType == typeof(int))
            {
                property.SetValue(obj, value);
                return;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                if (field.FieldType == typeof(int))
                {
                    field.SetValue(obj, value);
                    return;
                }
                if (field.GetValue(obj) is Netcode.NetInt netInt)
                {
                    netInt.Value = value;
                    return;
                }
            }
        }
    }
}
