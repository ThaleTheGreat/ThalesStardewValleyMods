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
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Powers;
using StardewValley.Locations;
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

    private static ModEntry? Instance;

    private ModConfig Config = new();
    private readonly Dictionary<WalletToolKind, WalletToolState> StoredTools = new();
    private TemporaryToolUse? PendingToolUse;
    private Harmony Harmony = null!;
    private bool SuppressInventoryConversion;
    private bool PendingInventoryConversion;
    private bool GmcmRegistered;
    private IGenericModConfigMenuApi? GmcmApi;
    private IMobilePhoneApi? MobilePhoneApi;
    private ISpecialPowerAPI? SpecialPowerApi;
    private bool GmcmMissingLogged;
    private bool ToolSmartSwitchPatched;
    private bool ToolSmartSwitchPatchAttempted;
    private bool AutomateToolSwapPatched;
    private bool AutomateToolSwapPatchAttempted;
    private readonly Dictionary<WalletToolKind, Tool> ToolSmartSwitchToolCache = new();
    private readonly List<BlacksmithExposure> ActiveBlacksmithExposures = new();

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;

        Harmony = new Harmony(ModManifest.UniqueID);
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
        PatchBlacksmithToolUpgradeFlow();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(PowerCategoryIconTexturePath))
        {
            e.LoadFrom(() => LoadEmbeddedTexture("Powers.png"), AssetLoadPriority.Exclusive);
            return;
        }

        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers") || !Config.ModEnabled)
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
        if (!TryGetWalletDisplayState(kind, out WalletToolState state))
            return;

        powers[$"{PowerPrefix}{kind}"] = new PowersData
        {
            DisplayName = state.GetDisplayName(),
            Description = state.GetDescription(),
            TexturePath = state.GetTexturePath(),
            TexturePosition = state.GetTexturePosition(),
            UnlockedCondition = $"PLAYER_MOD_DATA Current {FlagPrefix}{kind} true"
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

    private void PatchBlacksmithToolUpgradeFlow()
    {
        MethodInfo? prefix = AccessTools.Method(typeof(ModEntry), nameof(BlacksmithToolFlowPrefix));
        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(BlacksmithToolFlowPostfix));
        if (prefix is null || postfix is null)
            return;

        PatchNamedMethods(typeof(GameLocation), "performAction", prefix, postfix);
        PatchNamedMethods(typeof(GameLocation), "answerDialogueAction", prefix, postfix);
    }

    private void PatchNamedMethods(Type type, string methodName, MethodInfo prefix, MethodInfo postfix)
    {
        foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type).Where(method => method.Name == methodName))
            Harmony.Patch(method, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
    }

    private static void BlacksmithToolFlowPrefix()
    {
        Instance?.BeginBlacksmithToolExposure(Game1.player);
    }

    private static void BlacksmithToolFlowPostfix()
    {
        Instance?.EndBlacksmithToolExposure(Game1.player);
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

        Monitor.Log("Wallet Tools did not detect Tool Smart Switch or AutomateToolSwap. Using Wallet Tools fallback switching logic.", LogLevel.Info);
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
            return;

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
            return;

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

            AddKeybind(GmcmApi, nameof(Config.UseAxeHotkey), () => Config.UseAxeHotkey, value => Config.UseAxeHotkey = value, "Use Axe", "Immediately use the wallet axe.");
            AddKeybind(GmcmApi, nameof(Config.UsePickaxeHotkey), () => Config.UsePickaxeHotkey, value => Config.UsePickaxeHotkey = value, "Use Pickaxe", "Immediately use the wallet pickaxe.");
            AddKeybind(GmcmApi, nameof(Config.UseHoeHotkey), () => Config.UseHoeHotkey, value => Config.UseHoeHotkey = value, "Use Hoe", "Immediately use the wallet hoe.");
            AddKeybind(GmcmApi, nameof(Config.UseWateringCanHotkey), () => Config.UseWateringCanHotkey, value => Config.UseWateringCanHotkey = value, "Use Watering Can", "Immediately use the wallet watering can.");
            AddKeybind(GmcmApi, nameof(Config.UsePanHotkey), () => Config.UsePanHotkey, value => Config.UsePanHotkey = value, "Use Pan", "Immediately use the wallet pan.");
            AddKeybind(GmcmApi, nameof(Config.UseMilkPailHotkey), () => Config.UseMilkPailHotkey, value => Config.UseMilkPailHotkey = value, "Use Milk Pail", "Immediately use the wallet milk pail.");
            AddKeybind(GmcmApi, nameof(Config.UseShearsHotkey), () => Config.UseShearsHotkey, value => Config.UseShearsHotkey = value, "Use Shears", "Immediately use the wallet shears.");





            GmcmRegistered = true;
            Monitor.Log("Registered Wallet Tools options with Generic Mod Config Menu.", LogLevel.Info);
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

    private void DisableWalletTools(Farmer player, string reason)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);

        bool returnedTools = MaterializeStoredToolsToInventory(player, markForOvernight: false, clearStoredState: true, reason: reason);

        RemoveRuntimeToolCopies(player);
        ClearWalletMarkersFromInventory(player);
        StoredTools.Clear();
        ToolSmartSwitchToolCache.Clear();
        Helper.Data.WriteSaveData<Dictionary<WalletToolKind, WalletToolState>>(SaveDataKey, null);
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

        StoredTools.Clear();
        ToolSmartSwitchToolCache.Clear();
        Helper.Data.WriteSaveData<Dictionary<WalletToolKind, WalletToolState>>(SaveDataKey, null);
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

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        CollectOvernightExposedTools(Game1.player, "day start");
        ReconcileLoadedToolState(Game1.player, "day start");
        ConvertInventoryTools(Game1.player);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        SyncBlacksmithUpgradeState(Game1.player);
        ExposeStoredToolsForOvernight(Game1.player, "day ending");
        RemoveRuntimeToolCopies(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        SyncBlacksmithUpgradeState(Game1.player);
        ExposeStoredToolsForOvernight(Game1.player, "saving");
        RemoveRuntimeToolCopies(Game1.player);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        SyncBlacksmithUpgradeState(Game1.player);

        if (Config.ModEnabled && e.IsMultipleOf(300))
            ScanLostFoundForWalletTools("periodic lost and found scan");

        if (PendingToolUse is not null && !IsPlayerUsingTool(Game1.player))
            RestoreTemporaryTool(Game1.player);

        if (PendingInventoryConversion && CanConvertInventoryTools(Game1.player))
        {
            PendingInventoryConversion = false;
            ConvertInventoryTools(Game1.player);
        }
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
        if (!Context.IsWorldReady || !Config.ModEnabled || Game1.activeClickableMenu is not null || Game1.fadeToBlack || !Context.CanPlayerMove)
            return;

        WalletToolKind? requested = GetRequestedHotkeyTool();
        if (requested is null)
            return;

        Helper.Input.Suppress(e.Button);
        TryUseWalletToolHotkey(Game1.player, requested.Value);
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

    private void TryUseWalletToolHotkey(Farmer player, WalletToolKind requestedKind)
    {
        if (!TryFindStoredEnabledToolForRequest(requestedKind, out WalletToolKind storedKind))
            return;

        if (!TrySetTemporaryWalletTool(player, storedKind))
            return;

        Game1.pressUseToolButton();
    }

    private static bool IsToolUpgradeLocation(GameLocation? location)
    {
        if (location is null)
            return false;

        string name = location.NameOrUniqueName;
        return name.Equals("Blacksmith", StringComparison.OrdinalIgnoreCase) || name.Equals("Clint", StringComparison.OrdinalIgnoreCase);
    }

    private void BeginBlacksmithToolExposure(Farmer player)
    {
        if (!Config.ModEnabled || ActiveBlacksmithExposures.Count > 0 || !IsToolUpgradeLocation(player.currentLocation) || StoredTools.Count == 0)
            return;

        SuppressInventoryConversion = true;
        try
        {
            foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
            {
                if (!StoredTools.TryGetValue(kind, out WalletToolState? state) || !IsWalletEnabled(kind))
                    continue;

                Tool? tool = state.CreateTool(Monitor);
                if (tool is null)
                    continue;

                int slot = player.Items.Count;
                MarkRuntimeTool(tool, kind);
                player.Items.Add(tool);
                ActiveBlacksmithExposures.Add(new BlacksmithExposure(slot, null, kind, tool));
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private void EndBlacksmithToolExposure(Farmer player)
    {
        if (ActiveBlacksmithExposures.Count == 0)
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            foreach (BlacksmithExposure exposure in ActiveBlacksmithExposures)
            {
                Tool? exposedTool = FindExposedTool(player, exposure);
                bool vanillaHasUpgrade = IsWalletToolBeingUpgraded(player, exposure.Kind, exposure.Tool);

                if (!vanillaHasUpgrade && exposedTool is null)
                {
                    StoredTools[exposure.Kind] = WalletToolState.FromTool(exposure.Kind, exposure.Tool);
                    RestoreExposureSlot(player, exposure);
                    changed = true;
                    continue;
                }

                if (vanillaHasUpgrade)
                {
                    Tool? upgradingTool = GetToolBeingUpgraded(player);
                    PrepareToolForVanillaUpgrade(player, exposure.Kind);
                    if (RemoveExactToolReferenceFromInventory(player, upgradingTool))
                        changed = true;
                    if (RemoveInventoryCopiesOfToolKind(player, exposure.Kind, upgradingTool))
                        changed = true;
                    if (StoredTools.Remove(exposure.Kind))
                        changed = true;
                }
                else if (exposedTool is not null)
                {
                    StoredTools[exposure.Kind] = WalletToolState.FromTool(exposure.Kind, exposedTool);
                    RemoveExposedTool(player, exposedTool);
                    changed = true;
                }

                RestoreExposureSlot(player, exposure);
            }

            ActiveBlacksmithExposures.Clear();
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
            SaveStoredTools();
    }

    private static bool IsWalletToolBeingUpgraded(Farmer player, WalletToolKind kind, Tool exposedTool)
    {
        Tool? upgradedTool = GetToolBeingUpgraded(player);
        return upgradedTool is not null
            && (ReferenceEquals(upgradedTool, exposedTool) || ToolMatchesWalletKind(upgradedTool, kind));
    }

    private static Tool? GetToolBeingUpgraded(Farmer player)
    {
        return player.toolBeingUpgraded.Value;
    }

    private static Tool? FindExposedTool(Farmer player, BlacksmithExposure exposure)
    {
        foreach (Item? item in player.Items)
        {
            if (ReferenceEquals(item, exposure.Tool))
                return exposure.Tool;
        }

        if (exposure.Slot >= 0 && exposure.Slot < player.Items.Count && player.Items[exposure.Slot] is Tool tool && ToolMatchesWalletKind(tool, exposure.Kind))
            return tool;

        return null;
    }

    private static void RemoveExposedTool(Farmer player, Tool tool)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (ReferenceEquals(player.Items[i], tool))
            {
                player.Items[i] = null;
                return;
            }
        }
    }

    private static void RestoreExposureSlot(Farmer player, BlacksmithExposure exposure)
    {
        if (exposure.Slot < 0 || exposure.Slot >= player.Items.Count)
            return;

        if (ReferenceEquals(player.Items[exposure.Slot], exposure.Tool) || player.Items[exposure.Slot] is null)
            player.Items[exposure.Slot] = exposure.PreviousItem;
    }


    private void ExposeStoredToolsForOvernight(Farmer player, string reason)
    {
        if (!Config.ModEnabled || StoredTools.Count == 0)
            return;

        bool changed = MaterializeStoredToolsToInventory(player, markForOvernight: true, clearStoredState: false, reason: reason);
        if (!changed && !HasAnyOvernightExposure(player))
            return;

        Helper.Data.WriteSaveData<Dictionary<WalletToolKind, WalletToolState>>(SaveDataKey, null);
        ClearWalletFlagModData(player);
        InvalidatePowers();

        if (changed)
            Monitor.Log($"Wallet Tools exposed stored tools during {reason} so the save contains real upgraded tools.", LogLevel.Trace);
    }

    private bool MaterializeStoredToolsToInventory(Farmer player, bool markForOvernight, bool clearStoredState, string reason)
    {
        if (StoredTools.Count == 0)
            return false;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            foreach (KeyValuePair<WalletToolKind, WalletToolState> pair in StoredTools.ToArray())
            {
                WalletToolKind kind = pair.Key;
                if (!IsWalletEnabled(kind) && markForOvernight)
                    continue;

                if (markForOvernight && HasOvernightExposure(player, kind))
                    continue;

                Tool? tool = pair.Value.CreateTool(Monitor);
                if (tool is null || WalletToolState.IsErrorTool(tool))
                {
                    StoredTools.Remove(kind);
                    changed = true;
                    continue;
                }

                ClearWalletMarkers(tool);
                if (markForOvernight)
                    MarkOvernightExposure(tool, kind);

                AddToolToInventoryOrAppend(player, tool);
                if (clearStoredState)
                    StoredTools.Remove(kind);
                changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
            Monitor.Log($"Wallet Tools materialized stored tools during {reason}.", LogLevel.Trace);

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
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(tool);
            return;
        }

        player.Items.Add(tool);
    }

    private void CollectOvernightExposedTools(Farmer player, string reason)
    {
        if (!Config.ModEnabled)
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || !IsOvernightExposure(tool, out WalletToolKind kind))
                    continue;

                ClearWalletMarkers(tool);
                if (IsWalletEnabled(kind) && TryGetWalletKindForStorage(tool, out WalletToolKind storageKind))
                    StoredTools[storageKind] = WalletToolState.FromTool(storageKind, tool);

                player.Items[i] = null;
                changed = true;
            }

            if (changed)
                TrimOvernightInventoryTail(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
        {
            SaveStoredTools();
            Monitor.Log($"Wallet Tools collected overnight-exposed tools during {reason}.", LogLevel.Trace);
        }
    }

    private static bool HasOvernightExposure(Farmer player, WalletToolKind kind)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && IsOvernightExposure(tool, out WalletToolKind exposedKind) && exposedKind == kind)
                return true;
        }

        return false;
    }

    private static bool HasAnyOvernightExposure(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && IsOvernightExposure(tool, out _))
                return true;
        }

        return false;
    }

    private static void ClearWalletMarkersFromInventory(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool)
                ClearWalletMarkers(tool);
        }
    }

    private static void ClearWalletFlagModData(Farmer player)
    {
        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
            player.modData.Remove($"{FlagPrefix}{kind}");
    }

    private static void MarkOvernightExposure(Tool tool, WalletToolKind kind)
    {
        tool.modData[OvernightExposureMarker] = "true";
        tool.modData[OvernightExposureKindMarker] = kind.ToString();
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

    private static void ClearWalletMarkers(Tool tool)
    {
        tool.modData.Remove(RuntimeToolMarker);
        tool.modData.Remove(RuntimeToolKindMarker);
        tool.modData.Remove(OvernightExposureMarker);
        tool.modData.Remove(OvernightExposureKindMarker);
    }

    private static void TrimOvernightInventoryTail(Farmer player)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        while (player.Items.Count > maxItems && player.Items.Count > 0 && player.Items[player.Items.Count - 1] is null)
            player.Items.RemoveAt(player.Items.Count - 1);
    }

    private static int GetPlayerMaxItemCount(Farmer player)
    {
        int maxItems = WalletToolState.GetIntMember(player, "MaxItems", "maxItems");
        return Math.Max(12, maxItems <= 0 ? 36 : maxItems);
    }

    private void LoadStoredTools()
    {
        StoredTools.Clear();
        ToolSmartSwitchToolCache.Clear();
        Dictionary<WalletToolKind, WalletToolState>? data = Helper.Data.ReadSaveData<Dictionary<WalletToolKind, WalletToolState>>(SaveDataKey);
        if (data is not null)
        {
            foreach (KeyValuePair<WalletToolKind, WalletToolState> pair in data)
                StoredTools[pair.Key] = pair.Value;
        }

        UpdateWalletFlags(Game1.player);
        InvalidatePowers();
    }

    private void SaveStoredTools(bool invalidatePowers = true)
    {
        ToolSmartSwitchToolCache.Clear();
        Helper.Data.WriteSaveData(SaveDataKey, StoredTools);
        UpdateWalletFlags(Game1.player);
        if (invalidatePowers)
            InvalidatePowers();
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
            if (TryGetWalletDisplayState(kind, out _))
                player.modData[key] = "true";
            else
                player.modData.Remove(key);
        }
    }

    private bool TryGetWalletDisplayState(WalletToolKind kind, out WalletToolState state)
    {
        if (!Config.ModEnabled)
        {
            state = null!;
            return false;
        }

        if (StoredTools.TryGetValue(kind, out WalletToolState? directState))
        {
            state = directState;
            return true;
        }


        state = null!;
        return false;
    }

    private static void PrepareToolForVanillaUpgrade(Farmer player, WalletToolKind kind)
    {
        Tool? upgradingTool = GetToolBeingUpgraded(player);
        if (upgradingTool is null || !ToolMatchesWalletKind(upgradingTool, kind))
            return;

        ClearWalletMarkers(upgradingTool);
    }

    private void SyncBlacksmithUpgradeState(Farmer player)
    {
        if (!Config.ModEnabled)
            return;

        Tool? upgradingTool = GetToolBeingUpgraded(player);
        if (upgradingTool is null || !TryGetWalletKindForStorage(upgradingTool, out WalletToolKind upgradingKind))
            return;

        ClearWalletMarkers(upgradingTool);

        bool changed = StoredTools.Remove(upgradingKind);
        if (RemoveExactToolReferenceFromInventory(player, upgradingTool))
            changed = true;
        if (RemoveInventoryCopiesOfToolKind(player, upgradingKind, upgradingTool))
            changed = true;

        if (changed)
            SaveStoredTools();
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

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            RemoveRuntimeToolCopies(player);
            if (RemoveInvalidStoredTools(reason))
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
                if (RemoveLostFoundCopiesOfToolKind(kindBeingUpgraded))
                    changed = true;
                if (StoredTools.Remove(kindBeingUpgraded))
                    changed = true;
            }

            foreach (WalletToolKind kind in GetToolRecoveryOrder())
            {
                if (!IsWalletEnabled(kind) || upgradingKind == kind)
                    continue;

                if (StoredTools.ContainsKey(kind))
                {
                    if (RemoveInventoryCopiesOfToolKind(player, kind, null))
                        changed = true;
                    if (CollectLostFoundToolsOfKind(kind))
                        changed = true;
                    continue;
                }

                if (TryTakeInventoryTool(player, kind, out Tool? inventoryTool))
                {
                    StoredTools[kind] = WalletToolState.FromTool(kind, inventoryTool);
                    if (CollectLostFoundToolsOfKind(kind))
                        changed = true;
                    changed = true;
                    continue;
                }

                if (CollectLostFoundToolsOfKind(kind))
                    changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
        {
            SaveStoredTools();
            Monitor.Log($"Wallet Tools repaired wallet/tool state during {reason}.", LogLevel.Info);
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
                tool = candidate;
                return true;
            }
        }

        tool = null!;
        return false;
    }

    private bool CollectLostFoundToolsOfKind(WalletToolKind kind)
    {
        bool changed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundTool(kind, out Tool? recoveredTool))
                break;

            if (TryReplaceStoredToolIfBetter(kind, recoveredTool))
                changed = true;

            changed = true;
        }

        return changed;
    }

    private void ScanLostFoundForWalletTools(string reason)
    {
        if (!Config.ModEnabled || !Context.IsWorldReady)
            return;

        bool changed = false;
        foreach (WalletToolKind kind in GetToolRecoveryOrder())
        {
            if (!IsWalletEnabled(kind))
                continue;

            if (CollectLostFoundToolsOfKind(kind))
                changed = true;
        }

        if (!changed)
            return;

        SaveStoredTools();
        Monitor.Log($"Wallet Tools collected tool(s) from Lost and Found during {reason}.", LogLevel.Info);
    }

    private static bool RemoveLostFoundCopiesOfToolKind(WalletToolKind kind)
    {
        bool removed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundTool(kind, out _))
                break;

            removed = true;
        }

        return removed;
    }

    private static bool TryTakeLostFoundTool(WalletToolKind kind, out Tool tool)
    {
        tool = null!;
        object? team = Game1.player?.team;
        if (team is null)
            return false;

        BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (MemberInfo member in team.GetType().GetMembers(flags))
        {
            if (member.MemberType != MemberTypes.Field && member.MemberType != MemberTypes.Property)
                continue;

            string name = member.Name;
            if (!name.Contains("lost", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("found", StringComparison.OrdinalIgnoreCase)
                && !name.Contains("return", StringComparison.OrdinalIgnoreCase))
                continue;

            object? value = GetMemberValue(member, team);
            if (TryTakeToolFromValue(value, kind, out tool))
                return true;
        }

        return false;
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
        tool = null!;
        if (value is null || value is string)
            return false;

        if (TryTakeToolFromWrappedValue(value, kind, out tool))
            return true;

        object? unwrapped = TryGetNetValue(value);
        if (!ReferenceEquals(unwrapped, value) && unwrapped is not Tool && TryTakeToolFromValue(unwrapped, kind, out tool))
            return true;

        if (value is Tool directTool && ToolMatchesWalletKind(directTool, kind))
        {
            ClearWalletMarkers(directTool);
            tool = directTool;
            return true;
        }

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Tool candidate && ToolMatchesWalletKind(candidate, kind))
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
                if (dictionaryValue is Tool candidate && ToolMatchesWalletKind(candidate, kind))
                {
                    ClearWalletMarkers(candidate);
                    dictionary.Remove(key);
                    tool = candidate;
                    return true;
                }

                if (TryTakeToolFromValue(dictionaryValue, kind, out tool))
                    return true;
            }
        }

        return false;
    }


    private static bool TryTakeToolFromWrappedValue(object value, WalletToolKind kind, out Tool tool)
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
                TryReplaceStoredToolIfBetter(kind, tool);
                player.Items[i] = null;
                removed = true;
            }
        }

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
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool, out _) || IsCurrentlySelectedTool(player, i, tool) || !TryGetWalletKindForStorage(tool, out WalletToolKind kind))
                    continue;

                if (TryReplaceStoredToolIfBetter(kind, tool))
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
            SaveStoredTools();
            Game1.addHUDMessage(new HUDMessage("Tool moved to wallet.", HUDMessage.newQuest_type));
        }
    }

    private bool RemoveInvalidStoredTools(string reason)
    {
        bool changed = false;
        foreach (KeyValuePair<WalletToolKind, WalletToolState> pair in StoredTools.ToArray())
        {
            if (!pair.Value.IsInvalidStoredTool())
                continue;

            StoredTools.Remove(pair.Key);
            changed = true;
        }

        if (changed)
            Monitor.Log($"Wallet Tools removed missing/error stored tools during {reason}.", LogLevel.Info);

        return changed;
    }

    private bool TryReplaceStoredToolIfBetter(WalletToolKind kind, Tool candidate)
    {
        if (!IsWalletEnabled(kind) || WalletToolState.IsErrorTool(candidate))
            return false;

        ClearWalletMarkers(candidate);
        WalletToolState candidateState = WalletToolState.FromTool(kind, candidate);
        if (candidateState.IsInvalidStoredTool())
            return false;

        if (!StoredTools.TryGetValue(kind, out WalletToolState? existingState)
            || existingState.IsInvalidStoredTool()
            || candidateState.GetPowerScore() > existingState.GetPowerScore())
        {
            StoredTools[kind] = candidateState;
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
        if (tool is Axe)
        {
            kind = WalletToolKind.Axe;
            return true;
        }
        if (tool is Pickaxe)
        {
            kind = WalletToolKind.Pickaxe;
            return true;
        }
        if (tool is Hoe)
        {
            kind = WalletToolKind.Hoe;
            return true;
        }
        if (tool is WateringCan)
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


    private static bool ToolMatchesWalletKind(Tool tool, WalletToolKind kind)
    {
        if (WalletToolState.IsErrorTool(tool))
            return false;

        return TryGetWalletKind(tool, out WalletToolKind actualKind) && actualKind == kind;
    }

    private bool IsWalletEnabled(WalletToolKind kind)
    {
        return Config.ModEnabled && Enum.IsDefined(typeof(WalletToolKind), kind);
    }

    private void AddWalletToolsToToolSmartSwitch(Farmer player, Dictionary<int, Tool> tools)
    {
        if (!Config.ModEnabled || IsToolUpgradeLocation(player.currentLocation) || StoredTools.Count == 0)
            return;

        foreach (WalletToolKind kind in Enum.GetValues<WalletToolKind>())
        {
            if (!IsWalletEnabled(kind) || !StoredTools.ContainsKey(kind))
                continue;

            int index = GetToolSmartSwitchIndex(kind);
            if (tools.ContainsKey(index))
                continue;

            Tool? tool = GetCachedToolSmartSwitchTool(kind);
            if (tool is not null)
                tools[index] = tool;
        }

    }

    private Tool? GetCachedToolSmartSwitchTool(WalletToolKind kind)
    {
        if (ToolSmartSwitchToolCache.TryGetValue(kind, out Tool? cachedTool))
            return cachedTool;

        if (!StoredTools.TryGetValue(kind, out WalletToolState? state))
            return null;

        Tool? tool = state.CreateTool(Monitor);
        if (tool is not null)
            ToolSmartSwitchToolCache[kind] = tool;

        return tool;
    }

    private bool TrySwitchToolSmartSwitchWalletTool(Farmer player, int which)
    {
        WalletToolKind? kind = GetToolKindFromToolSmartSwitchIndex(which);
        if (kind is null)
            return false;

        if (!TryFindStoredEnabledToolForRequest(kind.Value, out WalletToolKind storedKind))
            return false;

        return TrySetTemporaryWalletTool(player, storedKind);
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
        if (PendingToolUse is not null || !Config.ModEnabled || IsToolUpgradeLocation(player.currentLocation))
            return false;

        if (PlayerIsHoldingRequestedTool(player, toolType, anyTool))
            return false;

        if (!TryGetWalletKindForRequestedType(toolType, anyTool, out WalletToolKind kind))
            return false;

        return TrySetTemporaryWalletTool(player, kind);
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
            return TryFindStoredEnabledToolForRequest(WalletToolKind.Axe, out kind);

        if (toolType == typeof(Pickaxe))
            return TryFindStoredEnabledToolForRequest(WalletToolKind.Pickaxe, out kind);

        if (toolType == typeof(Hoe))
        {
            kind = WalletToolKind.Hoe;
            return HasStoredEnabledTool(kind);
        }

        if (toolType == typeof(WateringCan))
        {
            kind = WalletToolKind.WateringCan;
            return HasStoredEnabledTool(kind);
        }

        if (toolType == typeof(Pan))
        {
            kind = WalletToolKind.Pan;
            return HasStoredEnabledTool(kind);
        }

        if (toolType == typeof(MilkPail))
        {
            kind = WalletToolKind.MilkPail;
            return HasStoredEnabledTool(kind);
        }

        if (toolType == typeof(Shears))
        {
            kind = WalletToolKind.Shears;
            return HasStoredEnabledTool(kind);
        }

        if (anyTool)
        {
            foreach (WalletToolKind candidate in new[] { WalletToolKind.Axe, WalletToolKind.Pickaxe, WalletToolKind.Hoe })
            {
                if (TryFindStoredEnabledToolForRequest(candidate, out kind))
                    return true;
            }
        }

        kind = WalletToolKind.Axe;
        return false;
    }

    private bool HasStoredEnabledTool(WalletToolKind kind)
    {
        return IsWalletEnabled(kind) && StoredTools.ContainsKey(kind);
    }

    private bool TryFindStoredEnabledToolForRequest(WalletToolKind requestedKind, out WalletToolKind storedKind)
    {
        if (HasStoredEnabledTool(requestedKind))
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
        if (IsExternalSwitchModLoaded())
            return false;

        if (PendingToolUse is not null || !Config.ModEnabled || Game1.fadeToBlack || !Context.CanPlayerMove)
            return false;

        if (!TryGetFallbackToolRequest(player, out Type toolType, out bool anyTool))
            return false;

        return TrySupplyRequestedWalletTool(player, toolType, anyTool);
    }

    private bool TryGetFallbackToolRequest(Farmer player, out Type toolType, out bool anyTool)
    {
        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        tile = new Vector2((int)tile.X, (int)tile.Y);

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

    private bool TrySetTemporaryWalletTool(Farmer player, WalletToolKind kind)
    {
        if (PendingToolUse is not null || !Config.ModEnabled || IsToolUpgradeLocation(player.currentLocation) || !StoredTools.TryGetValue(kind, out WalletToolState? state))
            return false;

        Tool? walletTool = state.CreateTool(Monitor);
        if (walletTool is null)
            return false;

        int previousToolIndex = player.CurrentToolIndex;
        int hiddenSlot = player.Items.Count;

        MarkRuntimeTool(walletTool, kind);

        SuppressInventoryConversion = true;
        player.Items.Add(walletTool);
        player.CurrentToolIndex = hiddenSlot;
        PendingToolUse = new TemporaryToolUse(hiddenSlot, previousToolIndex, kind, walletTool);
        SuppressInventoryConversion = false;

        Game1.playSound("toolSwap");
        return true;
    }

    private static void MarkRuntimeTool(Tool tool, WalletToolKind kind)
    {
        tool.modData[RuntimeToolMarker] = "true";
        tool.modData[RuntimeToolKindMarker] = kind.ToString();
    }

    private static bool IsRuntimeTool(Item? item)
    {
        return item is Tool tool && tool.modData.ContainsKey(RuntimeToolMarker);
    }

    private static void RemoveRuntimeToolCopies(Farmer player)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (IsRuntimeTool(player.Items[i]))
                player.Items[i] = null;
        }
    }

    private void RestoreTemporaryTool(Farmer player)
    {
        if (PendingToolUse is null)
            return;

        TemporaryToolUse pending = PendingToolUse;
        PendingToolUse = null;

        SuppressInventoryConversion = true;
        try
        {
            Tool? usedTool = FindTemporaryTool(player, pending);

            if (usedTool is not null)
            {
                StoredTools[pending.Kind] = WalletToolState.FromTool(pending.Kind, usedTool);
                RemoveTemporaryTool(player, usedTool);
                SaveStoredTools(false);
            }
            else
            {
                RemoveRuntimeToolCopies(player);
            }

            player.CurrentToolIndex = Math.Max(0, Math.Min(pending.PreviousToolIndex, player.Items.Count - 1));
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private static Tool? FindTemporaryTool(Farmer player, TemporaryToolUse pending)
    {
        if (pending.Slot >= 0 && pending.Slot < player.Items.Count && ReferenceEquals(player.Items[pending.Slot], pending.TemporaryTool))
            return pending.TemporaryTool;

        foreach (Item? item in player.Items)
        {
            if (ReferenceEquals(item, pending.TemporaryTool))
                return pending.TemporaryTool;
        }

        return null;
    }

    private static void RemoveTemporaryTool(Farmer player, Tool tool)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (ReferenceEquals(player.Items[i], tool))
            {
                player.Items[i] = null;
                return;
            }
        }
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

    [HarmonyPatch(typeof(Game1), nameof(Game1.pressUseToolButton))]
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
    public TemporaryToolUse(int slot, int previousToolIndex, WalletToolKind kind, Tool temporaryTool)
    {
        Slot = slot;
        PreviousToolIndex = previousToolIndex;
        Kind = kind;
        TemporaryTool = temporaryTool;
    }

    public int Slot { get; }
    public int PreviousToolIndex { get; }
    public WalletToolKind Kind { get; }
    public Tool TemporaryTool { get; }
}

internal sealed class WalletToolState
{
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
            return tool.getDescription();

        if (!string.IsNullOrWhiteSpace(Description))
            return Description;

        return string.Empty;
    }

    public string GetTexturePath()
    {
        return string.IsNullOrWhiteSpace(TexturePath) ? "TileSheets/tools" : TexturePath;
    }

    public Point GetTexturePosition()
    {
        Tool? tool = CreateTool(null);
        int menuSpriteIndex = tool is not null
            ? GetOptionalIntMember(tool, "IndexOfMenuItemView", "indexOfMenuItemView", "currentParentTileIndex", "CurrentParentTileIndex") ?? MenuSpriteIndex
            : MenuSpriteIndex;

        if (menuSpriteIndex >= 0)
            return GetTexturePositionFromMenuIndex(menuSpriteIndex);

        return GetFallbackTexturePosition(Kind, UpgradeLevel);
    }

    public int GetPowerScore()
    {
        return Math.Clamp(UpgradeLevel, 0, 4);
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

        if (Kind == WalletToolKind.Pan)
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

        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        return text.Contains("Pan", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("Pants", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsMilkPailTool(Tool tool)
    {
        if (tool is MilkPail)
            return true;

        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        return text.Contains("MilkPail", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Milk Pail", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsShearsTool(Tool tool)
    {
        if (tool is Shears)
            return true;

        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        return text.Contains("Shears", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetEffectiveUpgradeLevel(WalletToolKind kind, Tool tool)
    {
        int upgradeLevel = GetIntMember(tool, "UpgradeLevel", "upgradeLevel");
        if (kind == WalletToolKind.Pan)
            upgradeLevel = Math.Max(upgradeLevel, GetPanUpgradeLevel(tool));

        return Math.Clamp(upgradeLevel, 0, 4);
    }

    private static int GetPanUpgradeLevel(Tool tool)
    {
        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        if (text.Contains("IridiumPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Iridium Pan", StringComparison.OrdinalIgnoreCase))
            return 4;
        if (text.Contains("GoldPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Gold Pan", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (text.Contains("SteelPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Steel Pan", StringComparison.OrdinalIgnoreCase))
            return 2;
        if (text.Contains("CopperPan", StringComparison.OrdinalIgnoreCase) || text.Contains("Copper Pan", StringComparison.OrdinalIgnoreCase))
            return 1;

        return 0;
    }

    private static bool IsBasicPanItemId(string itemId)
    {
        string normalized = itemId.Trim();
        return normalized.Equals("Pan", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("(T)Pan", StringComparison.OrdinalIgnoreCase);
    }

    private static string GetDefaultPanQualifiedItemId(int upgradeLevel)
    {
        return Math.Clamp(upgradeLevel, 0, 4) switch
        {
            1 => "(T)CopperPan",
            2 => "(T)SteelPan",
            3 => "(T)GoldPan",
            4 => "(T)IridiumPan",
            _ => "(T)Pan"
        };
    }

    private static int GetToolMenuSpriteIndex(Tool tool)
    {
        int? directIndex = GetOptionalIntMember(tool, "IndexOfMenuItemView", "indexOfMenuItemView", "currentParentTileIndex", "CurrentParentTileIndex");
        if (directIndex.HasValue && directIndex.Value >= 0)
            return directIndex.Value;

        object? data = GetParsedItemData(tool);
        int? dataIndex = GetOptionalIntMemberFromObject(data, "SpriteIndex", "spriteIndex");
        return dataIndex ?? -1;
    }

    private static string GetToolTexturePath(Tool tool)
    {
        object? data = GetParsedItemData(tool);
        string? texture = GetOptionalStringMemberFromObject(data, "TextureName", "textureName", "Texture", "texture");
        return string.IsNullOrWhiteSpace(texture) ? "TileSheets/tools" : texture;
    }

    private static object? GetParsedItemData(Tool tool)
    {
        string itemId = string.IsNullOrWhiteSpace(tool.QualifiedItemId) ? tool.ItemId : tool.QualifiedItemId;
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
        int level = Math.Clamp(upgradeLevel, 0, 4);

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
