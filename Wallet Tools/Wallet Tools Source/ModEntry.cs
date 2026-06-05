using System;
using System.Collections.Generic;
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
    private const string RuntimeToolMarker = "ThaleTheGreat.WalletTools/RuntimeTool";
    private const string RuntimeToolKindMarker = "ThaleTheGreat.WalletTools/RuntimeToolKind";

    private static ModEntry? Instance;

    private ModConfig Config = new();
    private readonly Dictionary<WalletToolKind, WalletToolState> StoredTools = new();
    private TemporaryToolUse? PendingToolUse;
    private Harmony Harmony = null!;
    private bool SuppressInventoryConversion;
    private bool GmcmRegistered;
    private IGenericModConfigMenuApi? GmcmApi;
    private IMobilePhoneApi? MobilePhoneApi;
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
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            AddPower(powers, WalletToolKind.Axe, "axe");
            AddPower(powers, WalletToolKind.Pickaxe, "pickaxe");
            AddPower(powers, WalletToolKind.Hoe, "hoe");
            AddPower(powers, WalletToolKind.WateringCan, "watering can");
            AddPower(powers, WalletToolKind.Pan, "pan");
        });
    }

    private void AddPower(IDictionary<string, PowersData> powers, WalletToolKind kind, string genericName)
    {
        WalletToolState? state = StoredTools.TryGetValue(kind, out WalletToolState? storedState) ? storedState : null;
        string displayName = state?.GetDisplayName() ?? GetFallbackDisplayName(kind);
        string description = state?.GetDescription() ?? $"Stored {genericName}.";

        powers[$"{PowerPrefix}{kind}"] = new PowersData
        {
            DisplayName = displayName,
            Description = description,
            TexturePath = ToolTexturePath,
            TexturePosition = state?.GetTexturePosition() ?? GetFallbackTexturePosition(kind),
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
            _ => new Point(64, 32)
        };
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        RegisterMobilePhoneApp();
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
            Texture2D icon = Helper.ModContent.Load<Texture2D>("assets/MobilePhone.png");
            MobilePhoneApi.AddApp(ModManifest.UniqueID, "Wallet Tools", OpenConfigFromMobilePhone, icon);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools Mobile Phone app: {ex.Message}", LogLevel.Warn);
        }
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
            GmcmApi.Register(ModManifest, reset: () => Config = new ModConfig(), save: () => Helper.WriteConfig(Config));

            AddBool(GmcmApi, nameof(Config.ModEnabled), () => Config.ModEnabled, value => Config.ModEnabled = value, "Mod Enabled", "Enables or disables Wallet Tools.");
            AddBool(GmcmApi, nameof(Config.WalletAxe), () => Config.WalletAxe, value => Config.WalletAxe = value, "Wallet Axe", "Move the axe into the wallet when found in inventory.");
            AddBool(GmcmApi, nameof(Config.WalletPickaxe), () => Config.WalletPickaxe, value => Config.WalletPickaxe = value, "Wallet Pickaxe", "Move the pickaxe into the wallet when found in inventory.");
            AddBool(GmcmApi, nameof(Config.WalletHoe), () => Config.WalletHoe, value => Config.WalletHoe = value, "Wallet Hoe", "Move the hoe into the wallet when found in inventory.");
            AddBool(GmcmApi, nameof(Config.WalletWateringCan), () => Config.WalletWateringCan, value => Config.WalletWateringCan = value, "Wallet Watering Can", "Move the watering can into the wallet when found in inventory.");
            AddBool(GmcmApi, nameof(Config.WalletPan), () => Config.WalletPan, value => Config.WalletPan = value, "Wallet Pan", "Move the copper pan into the wallet when found in inventory.");

            AddKeybind(GmcmApi, nameof(Config.UseAxeHotkey), () => Config.UseAxeHotkey, value => Config.UseAxeHotkey = value, "Use Axe", "Immediately use the wallet axe.");
            AddKeybind(GmcmApi, nameof(Config.UsePickaxeHotkey), () => Config.UsePickaxeHotkey, value => Config.UsePickaxeHotkey = value, "Use Pickaxe", "Immediately use the wallet pickaxe.");
            AddKeybind(GmcmApi, nameof(Config.UseHoeHotkey), () => Config.UseHoeHotkey, value => Config.UseHoeHotkey = value, "Use Hoe", "Immediately use the wallet hoe.");
            AddKeybind(GmcmApi, nameof(Config.UseWateringCanHotkey), () => Config.UseWateringCanHotkey, value => Config.UseWateringCanHotkey = value, "Use Watering Can", "Immediately use the wallet watering can.");
            AddKeybind(GmcmApi, nameof(Config.UsePanHotkey), () => Config.UsePanHotkey, value => Config.UsePanHotkey = value, "Use Pan", "Immediately use the wallet pan.");

            AddBool(GmcmApi, nameof(Config.FallbackSwitchEnabled), () => Config.FallbackSwitchEnabled, value => Config.FallbackSwitchEnabled = value, "Fallback Switching", "Use Wallet Tools' built-in switching only when Tool Smart Switch and AutomateToolSwap are not installed.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForObjects), () => Config.FallbackSwitchForObjects, value => Config.FallbackSwitchForObjects = value, "Fallback: Objects", "Fallback switches to axe, pickaxe, or hoe for matching objects.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForTrees), () => Config.FallbackSwitchForTrees, value => Config.FallbackSwitchForTrees = value, "Fallback: Trees", "Fallback switches to axe, pickaxe, or hoe for trees and saplings.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForResourceClumps), () => Config.FallbackSwitchForResourceClumps, value => Config.FallbackSwitchForResourceClumps = value, "Fallback: Clumps", "Fallback switches to axe or pickaxe for resource clumps.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForCrops), () => Config.FallbackSwitchForCrops, value => Config.FallbackSwitchForCrops = value, "Fallback: Crops", "Fallback switches to hoe for forage crops that require hoe use.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForPan), () => Config.FallbackSwitchForPan, value => Config.FallbackSwitchForPan = value, "Fallback: Pan", "Fallback switches to the copper pan near panning spots.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForWateringCan), () => Config.FallbackSwitchForWateringCan, value => Config.FallbackSwitchForWateringCan = value, "Fallback: Watering Can", "Fallback switches to the watering can for refills, pet bowls, and volcano lava.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForWatering), () => Config.FallbackSwitchForWatering, value => Config.FallbackSwitchForWatering = value, "Fallback: Watering", "Fallback switches to the watering can for dry tilled dirt.");
            AddBool(GmcmApi, nameof(Config.FallbackSwitchForTilling), () => Config.FallbackSwitchForTilling, value => Config.FallbackSwitchForTilling = value, "Fallback: Tilling", "Fallback switches to the hoe for diggable tiles.");



            GmcmRegistered = true;
            Monitor.Log("Registered Wallet Tools options with Generic Mod Config Menu.", LogLevel.Info);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools options with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
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
        LoadStoredTools();
        ConvertInventoryTools(Game1.player);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        ConvertInventoryTools(Game1.player);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        RemoveRuntimeToolCopies(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        RemoveRuntimeToolCopies(Game1.player);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (PendingToolUse is null || !Context.IsWorldReady)
            return;

        if (!IsPlayerUsingTool(Game1.player))
            RestoreTemporaryTool(Game1.player);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (SuppressInventoryConversion || !Context.IsWorldReady || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID || ActiveBlacksmithExposures.Count > 0)
            return;

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

        return null;
    }

    private void TryUseWalletToolHotkey(Farmer player, WalletToolKind kind)
    {
        if (!HasStoredEnabledTool(kind))
            return;

        if (!TrySetTemporaryWalletTool(player, kind))
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
                bool upgradeStarted = IsWalletToolBeingUpgraded(player, exposure.Kind, exposure.Tool);
                Tool? exposedTool = FindExposedTool(player, exposure);

                if (upgradeStarted)
                {
                    StoredTools.Remove(exposure.Kind);
                    changed = true;
                }
                else if (exposedTool is null)
                {
                    StoredTools.Remove(exposure.Kind);
                    changed = true;
                }
                else
                {
                    StoredTools[exposure.Kind] = WalletToolState.FromTool(exposure.Kind, exposedTool);
                    changed = true;
                }

                if (exposedTool is not null)
                    RemoveExposedTool(player, exposedTool);

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
            && (ReferenceEquals(upgradedTool, exposedTool) || (TryGetWalletKind(upgradedTool, out WalletToolKind upgradedKind) && upgradedKind == kind));
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

        if (exposure.Slot >= 0 && exposure.Slot < player.Items.Count && player.Items[exposure.Slot] is Tool tool && TryGetWalletKind(tool, out WalletToolKind kind) && kind == exposure.Kind)
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
            if (StoredTools.ContainsKey(kind))
                player.modData[key] = "true";
            else
                player.modData.Remove(key);
        }
    }

    private void ConvertInventoryTools(Farmer player)
    {
        if (!Config.ModEnabled || ActiveBlacksmithExposures.Count > 0)
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || !TryGetWalletKind(tool, out WalletToolKind kind) || !IsWalletEnabled(kind))
                    continue;

                if (!StoredTools.ContainsKey(kind))
                {
                    StoredTools[kind] = WalletToolState.FromTool(kind, tool);
                    player.Items[i] = null;
                    changed = true;
                }
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
        if (tool is Pan)
        {
            kind = WalletToolKind.Pan;
            return true;
        }

        kind = WalletToolKind.Axe;
        return false;
    }

    private bool IsWalletEnabled(WalletToolKind kind)
    {
        return kind switch
        {
            WalletToolKind.Axe => Config.WalletAxe,
            WalletToolKind.Pickaxe => Config.WalletPickaxe,
            WalletToolKind.Hoe => Config.WalletHoe,
            WalletToolKind.WateringCan => Config.WalletWateringCan,
            WalletToolKind.Pan => Config.WalletPan,
            _ => false
        };
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

        return TrySetTemporaryWalletTool(player, kind.Value);
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

        if (currentItem.GetType() == toolType)
            return true;

        return anyTool && currentItem is Axe or Pickaxe or Hoe;
    }

    private bool TryGetWalletKindForRequestedType(Type toolType, bool anyTool, out WalletToolKind kind)
    {
        if (toolType == typeof(Axe))
        {
            kind = WalletToolKind.Axe;
            return HasStoredEnabledTool(kind);
        }

        if (toolType == typeof(Pickaxe))
        {
            kind = WalletToolKind.Pickaxe;
            return HasStoredEnabledTool(kind);
        }

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

        if (anyTool)
        {
            foreach (WalletToolKind candidate in new[] { WalletToolKind.Axe, WalletToolKind.Pickaxe, WalletToolKind.Hoe })
            {
                if (HasStoredEnabledTool(candidate))
                {
                    kind = candidate;
                    return true;
                }
            }
        }

        kind = WalletToolKind.Axe;
        return false;
    }

    private bool HasStoredEnabledTool(WalletToolKind kind)
    {
        return IsWalletEnabled(kind) && StoredTools.ContainsKey(kind);
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

        if (PendingToolUse is not null || !Config.ModEnabled || !Config.FallbackSwitchEnabled || Game1.fadeToBlack || !Context.CanPlayerMove)
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

        if (Config.FallbackSwitchForObjects && player.currentLocation.objects.TryGetValue(tile, out Object obj) && TryGetToolRequestForObject(obj, out toolType, out anyTool))
            return true;

        if (player.currentLocation.terrainFeatures.TryGetValue(tile, out TerrainFeature feature))
        {
            if (TryGetToolRequestForTerrainFeature(feature, out toolType, out anyTool))
                return true;

            if (Config.FallbackSwitchForWatering && player.currentLocation is Farm && feature is HoeDirt dirt && dirt.state.Value == 0)
            {
                toolType = typeof(WateringCan);
                anyTool = false;
                return true;
            }

            if (Config.FallbackSwitchForCrops && feature is HoeDirt cropDirt && cropDirt.crop is not null && cropDirt.crop.forageCrop.Value && cropDirt.crop.whichForageCrop.Value == Crop.forageCrop_ginger.ToString())
            {
                toolType = typeof(Hoe);
                anyTool = false;
                return true;
            }
        }

        if (Config.FallbackSwitchForResourceClumps)
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

        if (Config.FallbackSwitchForPan)
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

        if (Config.FallbackSwitchForWateringCan && TryNeedsWateringCan(player, tile))
        {
            toolType = typeof(WateringCan);
            anyTool = false;
            return true;
        }

        if (Config.FallbackSwitchForTilling && IsDiggableHoeTile(player.currentLocation, tile))
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

        if (Config.FallbackSwitchForTrees && feature is Tree tree)
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
        for (int i = player.Items.Count - 1; i >= 0; i--)
        {
            if (IsRuntimeTool(player.Items[i]))
                player.Items.RemoveAt(i);
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
        for (int i = player.Items.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(player.Items[i], tool))
            {
                player.Items.RemoveAt(i);
                return;
            }
        }
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
    Pan
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
            UpgradeLevel = GetIntMember(tool, "UpgradeLevel", "upgradeLevel"),
            MenuSpriteIndex = GetOptionalIntMember(tool, "IndexOfMenuItemView", "indexOfMenuItemView", "currentParentTileIndex", "CurrentParentTileIndex") ?? -1
        };

        foreach (KeyValuePair<string, string> pair in tool.modData.Pairs)
        {
            if (pair.Key == "ThaleTheGreat.WalletTools/RuntimeTool")
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

    public Tool? CreateTool(IMonitor? monitor)
    {
        string itemId = string.IsNullOrWhiteSpace(QualifiedItemId) ? GetDefaultQualifiedItemId(Kind) : QualifiedItemId;
        try
        {
            Tool tool = ItemRegistry.Create<Tool>(itemId);
            SetIntMember(tool, UpgradeLevel, "UpgradeLevel", "upgradeLevel");
            foreach (KeyValuePair<string, string> pair in ModData)
            {
                if (pair.Key == "ThaleTheGreat.WalletTools/RuntimeTool")
                    continue;

                tool.modData[pair.Key] = pair.Value;
            }
            return tool;
        }
        catch (Exception ex)
        {
            monitor?.Log($"Could not recreate wallet tool '{Name}' ({itemId}): {ex.Message}", LogLevel.Warn);
            return null;
        }
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
            _ => "Tool"
        };
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

    private static int GetIntMember(object obj, params string[] names)
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
