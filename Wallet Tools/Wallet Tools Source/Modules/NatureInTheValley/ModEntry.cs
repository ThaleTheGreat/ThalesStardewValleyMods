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
using StardewValley.GameData.Powers;
using StardewValley.Tools;

using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletToolsForNatureInTheValley;

internal sealed class NatureInTheValleyModule : WalletModule
{
    internal const string ModuleKey = "NatureInTheValley";
    internal const string LegacyUniqueId = "ThaleTheGreat.WalletToolsForNatureInTheValley";

    internal NatureInTheValleyModule(ThaleTheGreat.WalletTools.ModEntry host)
        : base(host, ModuleKey, "Nature in the Valley", LegacyUniqueId, "Nature.NatureInTheValley", "Nature.NatureInValleyContent")
    {
    }
    private static NatureInTheValleyModule? Instance;
    private const string LegacySaveDataKey = "WalletToolsForNatureInTheValley.NatureNet";
    private const string NatureModUniqueId = "Nature.NatureInTheValley";
    private const string NatureEntryTypeName = "NatureInTheValley.NatureInTheValleyEntry, NatureInTheValley";
    private const string AssetPrefix = "Mods/ThaleTheGreat.WalletToolsForNatureInTheValley";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_NatureInTheValleyNet";
    private const string HasNatureNetFlag = "ThaleTheGreat.WalletToolsForNatureInTheValley/HasNatureNet";
    private const string RuntimeToolMarker = "ThaleTheGreat.WalletToolsForNatureInTheValley/RuntimeTool";
    private const string RuntimeOwnerMarker = "ThaleTheGreat.WalletToolsForNatureInTheValley/OwnerPlayerId";
    private const string OvernightExposureMarker = "ThaleTheGreat.WalletToolsForNatureInTheValley/OvernightExposure";
    private const string NatureNetInstanceIdMarker = "ThaleTheGreat.WalletToolsForNatureInTheValley/NetInstanceId";
    private const string NatureNetSpeedBuffId = "NatCSpeedN";
    private const string WalletToolsRuntimeToolMarker = "ThaleTheGreat.WalletTools/RuntimeTool";
    private const string WalletToolsOwnerPlayerIdMarker = "ThaleTheGreat.WalletTools/OwnerPlayerId";
    private const string WalletToolsModEntryTypeName = "ThaleTheGreat.WalletTools.ModEntry, WalletTools";

    private static readonly NetDefinition[] NetDefinitions =
    {
        new("NIVNet", "(T)NIVNet", 0, "Nature Net", AssetPrefix + "/NormalNet", "NormalNet.png", "NatureInTheValley.NatInValeyNet"),
        new("NIVSaphNet", "(T)NIVSaphNet", 1, "Sapphire Nature Net", AssetPrefix + "/SaphireNet", "SaphireNet.png", "NatureInTheValley.NatInValeySaphNet"),
        new("NIVGoldNet", "(T)NIVGoldNet", 2, "Golden Nature Net", AssetPrefix + "/GoldenNet", "GoldenNet.png", "NatureInTheValley.NatInValeyGoldenNet"),
        new("NIVJadeNet", "(T)NIVJadeNet", 3, "Jade Nature Net", AssetPrefix + "/JadeNet", "JadeNet.png", "NatureInTheValley.NatInValeyJadeNet", "NIVjadeNet", "(T)NIVjadeNet")
    };

    private ModConfig Config = new();
    private Harmony Harmony = null!;
    private bool GmcmRegistered;
    private readonly Dictionary<long, NatureNetState?> StoredToolByPlayer = new();
    private NatureNetState? StoredTool
    {
        get => GetStoredTool(Game1.player);
        set => SetStoredTool(Game1.player, value);
    }

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

    internal override void Initialize()
    {
        IModHelper helper = Helper;
        Instance = this;
        Config = Host.ReadModuleConfig<ModConfig>(ModuleKey);

        helper.Events.Content.AssetRequested += OnAssetRequested;
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

        Harmony = new Harmony(LegacyUniqueId);
        PatchCoreGameMethods();
    }

    private void PatchCoreGameMethods()
    {
        MethodInfo? pressUseTool = AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton));
        MethodInfo? pressUseToolPrefix = AccessTools.Method(typeof(PressUseToolButtonPatch), nameof(PressUseToolButtonPatch.Prefix));
        if (pressUseTool is not null && pressUseToolPrefix is not null)
            Harmony.Patch(pressUseTool, prefix: new HarmonyMethod(pressUseToolPrefix));

        Type? walletToolsEntryType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ModEntry");
        MethodInfo? walletToolsPrepareUse = walletToolsEntryType is null
            ? null
            : AccessTools.Method(walletToolsEntryType, "TryPrepareWalletToolUse", new[] { typeof(Farmer) });
        MethodInfo? walletToolsPrepareUsePrefix = AccessTools.Method(typeof(WalletToolsPrepareWalletToolUsePatch), nameof(WalletToolsPrepareWalletToolUsePatch.Prefix));
        if (walletToolsPrepareUse is not null && walletToolsPrepareUsePrefix is not null)
            Harmony.Patch(walletToolsPrepareUse, prefix: new HarmonyMethod(walletToolsPrepareUsePrefix));
    }

    private static long GetWalletOwnerId(Farmer? player)
    {
        return player?.UniqueMultiplayerID ?? 0L;
    }

    private NatureNetState? GetStoredTool(Farmer? player)
    {
        StoredToolByPlayer.TryGetValue(GetWalletOwnerId(player), out NatureNetState? state);
        return state;
    }

    private void SetStoredTool(Farmer? player, NatureNetState? state)
    {
        long ownerId = GetWalletOwnerId(player);
        if (state is null)
            StoredToolByPlayer.Remove(ownerId);
        else
            StoredToolByPlayer[ownerId] = state;
    }

    private static bool IsOwnedByPlayer(Tool tool, Farmer player)
    {
        if (!tool.modData.TryGetValue(RuntimeOwnerMarker, out string? rawOwnerId))
            return !Context.IsMultiplayer;

        return long.TryParse(rawOwnerId, out long ownerId) && ownerId == player.UniqueMultiplayerID;
    }

    private static void MarkWalletOwner(Tool tool, Farmer player)
    {
        tool.modData[RuntimeOwnerMarker] = player.UniqueMultiplayerID.ToString();
    }

    internal override void OnGameLaunched()
    {
        RegisterGmcm();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        foreach (NetDefinition definition in NetDefinitions)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(definition.TextureAssetPath))
            {
                e.LoadFrom(() => LoadEmbeddedTexture(definition.EmbeddedTextureName), AssetLoadPriority.Exclusive);
                return;
            }
        }

        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers") || !Context.IsWorldReady || !IsNatureNetEnabled())
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            NatureNetState state = GetStoredTool(Game1.player) ?? NatureNetState.CreatePlaceholder(Monitor);
            NatureNetIconData icon = state.GetIconData(Monitor);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = state.GetDisplayName(Monitor),
                Description = state.GetDescription(Monitor),
                TexturePath = icon.TexturePath,
                TexturePosition = icon.TexturePosition,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {HasNatureNetFlag} true",
                CustomFields = Host.GetWalletPowerCustomFields()
            };
        });
    }

    private static Texture2D LoadEmbeddedTexture(string fileName)
    {
        using MemoryStream stream = new(EmbeddedAssets.GetBytes(fileName));
        return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = Host.GetGmcmAdapter<IGenericModConfigMenuApi>(ModuleKey);
        if (gmcm is null)
            return;

        try
        {
            gmcm.Unregister(ModManifest);
            gmcm.Register(ModManifest, ResetConfig, SaveConfig);
            AddBool(gmcm, nameof(Config.ModEnabled), () => Config.ModEnabled, SetModEnabled, "Mod Enabled", "Enables Wallet Tools for Nature in the Valley.");
            AddBool(gmcm, nameof(Config.AutoUseEnabled), () => Config.AutoUseEnabled, value => SetAutoUseMasterEnabled(value, false), "Enable Auto Use", "Automatically supplies the stored Nature in the Valley net when using it on a nearby Nature in the Valley creature. Manual hotkey use still works when this is disabled.");
            AddBool(gmcm, nameof(Config.RequireLeftShiftForAutoUse), () => Config.RequireLeftShiftForAutoUse, value => Config.RequireLeftShiftForAutoUse = value, "Require Left Shift + Click", "Require Left Shift to be held while clicking near a Nature in the Valley creature before the wallet Nature in the Valley net is supplied automatically.");
            AddBool(gmcm, nameof(Config.PlayToolSwapSound), () => Config.PlayToolSwapSound, value => Config.PlayToolSwapSound = value, "Play Tool Swap Sound", "Play the Wallet Tools swap sound when the nature net is readied.");
            AddBool(gmcm, nameof(Config.ShowHudMessageWhenStored), () => Config.ShowHudMessageWhenStored, value => Config.ShowHudMessageWhenStored = value, "Show Stored Message", "Show a HUD message when the Nature in the Valley net is moved to the wallet.");
            AddKeybind(gmcm, nameof(Config.ToggleAutoUseHotkey), () => Config.ToggleAutoUseHotkey, value => Config.ToggleAutoUseHotkey = value, "Toggle Auto Use", "Hotkey to toggle automatic Nature in the Valley wallet use on or off.");
            AddKeybind(gmcm, nameof(Config.UseNatureNetHotkey), () => Config.UseNatureNetHotkey, value => Config.UseNatureNetHotkey = value, "Use Nature Net", "Immediately use the wallet Nature in the Valley net.");
            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools for Nature in the Valley options with Generic Mod Config Menu: {ex}", LogLevel.Error);
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

    private void ResetConfig()
    {
        Config = new ModConfig();
        if (!Context.IsWorldReady)
            return;

        ReconcileLoadedToolState(Game1.player);
        ConvertInventoryTools(Game1.player);
        UpdateWalletFlag(Game1.player);
        InvalidatePowers();
    }

    private void SaveConfig()
    {
        Host.WriteModuleConfig(ModuleKey, Config);
        if (!Context.IsWorldReady)
            return;

        if (!IsNatureNetEnabled())
            MaterializeStoredToolToInventory(Game1.player, true);

        ReconcileLoadedToolState(Game1.player);
        ConvertInventoryTools(Game1.player);
        UpdateWalletFlag(Game1.player);
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
                DisableWalletBehavior(Game1.player, true);
            else
                ClearStoredWalletState();
            return;
        }

        CollectOvernightExposedTool(Game1.player);
        ReconcileLoadedToolState(Game1.player);
        ConvertInventoryTools(Game1.player);
    }

    private void SetAutoUseMasterEnabled(bool enabled, bool showMessage)
    {
        if (Config.AutoUseEnabled == enabled)
            return;

        Config.AutoUseEnabled = enabled;
        if (Context.IsWorldReady)
        {
            if (!enabled && PendingToolUse is not null)
                RestoreTemporaryTool(Game1.player);

            if (showMessage)
                Game1.addHUDMessage(new HUDMessage(enabled ? "Nature in the Valley wallet auto use enabled." : "Nature in the Valley wallet auto use disabled.", HUDMessage.newQuest_type));
        }

        Host.WriteModuleConfig(ModuleKey, Config);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ClearVolatileState(false);

        if (Context.IsMainPlayer)
        {
            NatureNetState? legacyState = Host.ReadSaveDataWithLegacy<NatureNetState>(LegacySaveDataKey, LegacyUniqueId);
            if (legacyState is not null)
                SetStoredTool(Game1.player, legacyState);

            Helper.Data.WriteSaveData<NatureNetState>(LegacySaveDataKey, null);
        }

        if (!Config.ModEnabled)
        {
            ClearStoredWalletState();
            return;
        }

        CollectOvernightExposedTool(Game1.player);
        ReconcileLoadedToolState(Game1.player);
        ConvertInventoryTools(Game1.player);
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        ClearVolatileState();
    }

    private void ClearVolatileState(bool invalidatePowers = true)
    {
        if (Context.IsWorldReady)
            RestorePendingToolUseToInventoryBeforeVolatileClear(Game1.player);

        StoredToolByPlayer.Clear();
        PendingToolUse = null;
        PendingManualUseHotkey = null;
        PendingInventoryConversion = false;
        SuppressInventoryConversion = false;

        if (invalidatePowers)
            InvalidatePowers();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        CollectOvernightExposedTool(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
        ReconcileLoadedToolState(Game1.player);
        ConvertInventoryTools(Game1.player);
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
        ExposeStoredToolForOvernight(Game1.player);
        ClearLegacySaveData();
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
        CollectOvernightExposedTool(Game1.player);
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
        ReconcileLoadedToolState(Game1.player);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        UpdatePendingTemporaryToolUse();

        if (PendingInventoryConversion && CanConvertInventoryTools(Game1.player))
        {
            PendingInventoryConversion = false;
            ConvertInventoryTools(Game1.player);
        }

        if (Config.ModEnabled && e.IsMultipleOf(300))
            ScanLostFoundForNatureNet();
    }

    private void UpdatePendingTemporaryToolUse()
    {
        TemporaryToolUse? pending = PendingToolUse;
        if (pending is null)
            return;

        Farmer player = Game1.player;
        if (PendingManualUseHotkey.HasValue)
        {
            if (IsManualHotkeyPhysicallyHeld())
                UpdateManualWalletToolCharge(player);
            else
                FinishManualWalletToolHotkeyUse(player);
        }
        else if (!player.UsingTool && !IsTemporaryNatureNetUseInProgress(player, pending))
            RestoreTemporaryTool(player);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (SuppressInventoryConversion || !Context.IsWorldReady || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
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
            SetAutoUseMasterEnabled(!Config.AutoUseEnabled, true);
            return;
        }

        if (Game1.activeClickableMenu is not null || !Context.CanPlayerMove)
            return;

        if (Config.UseNatureNetHotkey.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            TryUseWalletToolHotkey(Game1.player, e.Button);
            return;
        }

        if (IsAutoUseEnabled() && IsAutoUseTrigger(e.Button))
            PendingInventoryConversion = false;
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (PendingToolUse is not null && PendingManualUseHotkey is null && IsAutoUseReleaseTrigger(e.Button))
        {
            TryReleaseTemporaryNatureNetUse(Game1.player);
            return;
        }

        if (PendingManualUseHotkey != e.Button)
            return;

        if (IsManualHotkeyPhysicallyHeld())
            return;

        FinishManualWalletToolHotkeyUse(Game1.player);
    }

    private void TryUseWalletToolHotkey(Farmer player, SButton pressedButton)
    {
        if (!IsNatureNetEnabled() || GetStoredTool(player) is null)
            return;

        if (!TrySetTemporaryWalletTool(player))
            return;

        PendingManualUseHotkey = pressedButton;
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
            if (!TryReleaseTemporaryNatureNetUse(player) && player.UsingTool)
                player.EndUsingTool();
        }

        if (PendingToolUse is not null && !player.UsingTool && !IsTemporaryNatureNetUseInProgress(player, PendingToolUse))
            RestoreTemporaryTool(player);
    }

    private bool IsManualHotkeyPhysicallyHeld()
    {
        return PendingManualUseHotkey.HasValue
            && (Helper.Input.IsDown(PendingManualUseHotkey.Value) || Helper.Input.IsSuppressed(PendingManualUseHotkey.Value));
    }

    private bool IsNatureNetEnabled()
    {
        return Config.ModEnabled && Helper.ModRegistry.IsLoaded(NatureModUniqueId);
    }

    private bool IsAutoUseEnabled()
    {
        return IsNatureNetEnabled() && Config.AutoUseEnabled;
    }


    private bool TryPrepareWalletToolUse(Farmer player)
    {
        if (PendingToolUse is not null || !IsAutoUseEnabled() || Game1.fadeToBlack || !Context.CanPlayerMove || Game1.activeClickableMenu is not null)
            return false;

        if (IsNatureNet(player.CurrentItem))
            return false;

        if (Config.RequireLeftShiftForAutoUse && !Helper.Input.IsDown(SButton.LeftShift) && !Helper.Input.IsSuppressed(SButton.LeftShift))
            return false;

        if (!IsNatureCreatureInNetReach(player))
            return false;

        return TrySetTemporaryWalletTool(player);
    }

    private bool IsAutoUseTrigger(SButton button)
    {
        if (!Config.RequireLeftShiftForAutoUse)
            return button is SButton.MouseLeft or SButton.ControllerA;

        return button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftShift);
    }

    private static bool IsAutoUseReleaseTrigger(SButton button)
    {
        return button is SButton.MouseLeft or SButton.ControllerA;
    }

    private bool ShouldReservePressForNatureNet(Farmer player)
    {
        if (PendingToolUse is not null && IsNatureNet(PendingToolUse))
            return true;

        if (!IsAutoUseEnabled() || Game1.fadeToBlack || !Context.CanPlayerMove || Game1.activeClickableMenu is not null)
            return false;

        if (Config.RequireLeftShiftForAutoUse && !Helper.Input.IsDown(SButton.LeftShift) && !Helper.Input.IsSuppressed(SButton.LeftShift))
            return false;

        return GetStoredTool(player) is not null && IsNatureCreatureInNetReach(player);
    }

    private static bool IsNatureNet(TemporaryToolUse? pending)
    {
        return pending is not null && IsNatureNet(pending.TemporaryTool);
    }

    private bool CanConvertInventoryTools(Farmer player)
    {
        return IsNatureNetEnabled() && PendingToolUse is null && !IsPlayerUsingTool(player);
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
                if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || IsCurrentlySelectedItem(player, i, tool) || !IsNatureNet(tool))
                    continue;

                StoreBestNatureNet(player, tool);
                player.Items[i] = null;
                changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (!changed)
            return;

        NormalizePlayerItemsAfterCleanup(player);
        RefreshWalletStateAfterStoredToolChange(player);
        if (Config.ShowHudMessageWhenStored)
            Game1.addHUDMessage(new HUDMessage("Nature in the Valley net moved to wallet.", HUDMessage.newQuest_type));
    }

    private void ReconcileLoadedToolState(Farmer player)
    {
        if (!Config.ModEnabled)
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            RestorePendingToolUseBeforeDestructiveCleanup(player);
            RemoveRuntimeToolCopies(player);

            if (GetStoredTool(player) is not null)
            {
                if (RemoveInventoryCopiesOfNatureNet(player))
                    changed = true;
                if (CollectLostFoundNatureNet(player))
                    changed = true;
            }
            else
            {
                if (TryTakeInventoryNatureNet(player, out Tool? inventoryTool))
                {
                    StoreBestNatureNet(player, inventoryTool);
                    changed = true;
                }

                if (CollectLostFoundNatureNet(player))
                    changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
            RefreshWalletStateAfterStoredToolChange(player);
        else
        {
            UpdateWalletFlag(player);
            InvalidatePowers();
        }
    }

    private bool MaterializeStoredToolToInventory(Farmer player, bool useOverflowMenu)
    {
        NatureNetState? storedTool = GetStoredTool(player);
        if (storedTool is null)
            return false;

        Tool? tool = storedTool.CreateTool(Monitor);
        if (tool is null)
            return false;

        SetStoredTool(player, null);
        ClearWalletMarkers(tool);
        if (useOverflowMenu)
            AddToolToInventoryOrOverflowMenu(player, tool);
        else
            AddToolToInventoryOrAppend(player, tool);

        RefreshWalletStateAfterStoredToolChange(player);
        return true;
    }

    private void DisableWalletBehavior(Farmer player, bool showMessage)
    {
        RestorePendingToolUseBeforeDestructiveCleanup(player);

        bool returnedTool = MaterializeStoredToolToInventory(player, true);
        RemoveRuntimeToolCopies(player);
        ClearWalletMarkersFromInventory(player);
        SetStoredTool(player, null);
        ClearLegacySaveData();
        UpdateWalletFlag(player);
        InvalidatePowers();

        if (showMessage && returnedTool)
            Game1.addHUDMessage(new HUDMessage("Nature in the Valley net returned to inventory.", HUDMessage.newQuest_type));
    }

    private void ClearStoredWalletState()
    {
        if (Context.IsWorldReady)
        {
            RestorePendingToolUseBeforeDestructiveCleanup(Game1.player);
            RemoveRuntimeToolCopies(Game1.player);
            ClearWalletMarkersFromInventory(Game1.player);
        }

        SetStoredTool(Game1.player, null);
        ClearLegacySaveData();
        if (Context.IsWorldReady)
            UpdateWalletFlag(Game1.player);
        InvalidatePowers();
    }

    private void ExposeStoredToolForOvernight(Farmer player)
    {
        NatureNetState? storedTool = GetStoredTool(player);
        if (!IsNatureNetEnabled() || storedTool is null || HasOvernightExposure(player))
            return;

        Tool? tool = storedTool.CreateTool(Monitor);
        if (tool is null)
            return;

        MarkOvernightExposure(tool, player);
        AddToolToInventoryOrAppend(player, tool);
        SetStoredTool(player, null);
        RefreshWalletStateAfterStoredToolChange(player);
    }

    private void CollectOvernightExposedTool(Farmer player)
    {
        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = player.Items.Count - 1; i >= 0; i--)
            {
                if (player.Items[i] is Tool tool && IsOvernightExposureForPlayer(tool, player) && IsNatureNet(tool))
                {
                    ClearWalletMarkers(tool);
                    SetStoredTool(player, NatureNetState.FromTool(tool));
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
            NormalizePlayerItemsAfterCleanup(player);
            RefreshWalletStateAfterStoredToolChange(player);
        }
    }

    private bool TrySetTemporaryWalletTool(Farmer player)
    {
        NatureNetState? storedTool = GetStoredTool(player);
        if (PendingToolUse is not null || !IsNatureNetEnabled() || storedTool is null)
            return false;

        Tool? walletTool = storedTool.CreateTool(Monitor);
        if (walletTool is null)
            return false;

        int previousToolIndex = player.CurrentToolIndex;
        int maxItems = GetPlayerMaxItemCount(player);
        if (previousToolIndex < 0 || previousToolIndex >= maxItems)
            return false;

        SuppressInventoryConversion = true;
        bool leasedFromWallet = false;
        try
        {
            while (player.Items.Count <= previousToolIndex && player.Items.Count < maxItems)
                player.Items.Add(null);

            if (previousToolIndex >= player.Items.Count)
                return false;

            Item? previousItem = player.Items[previousToolIndex];
            UpdateToolEnchantmentsForSelectedSlot(player, previousItem as Tool, walletTool);
            MarkRuntimeTool(walletTool, player);
            SetStoredTool(player, null);
            leasedFromWallet = true;
            RefreshWalletStateAfterStoredToolChange(player, false);
            player.Items[previousToolIndex] = walletTool;
            player.CurrentToolIndex = previousToolIndex;
            PendingToolUse = new TemporaryToolUse(previousToolIndex, walletTool, previousItem, player.addedSpeed);
        }
        catch
        {
            if (leasedFromWallet)
            {
                ClearWalletMarkers(walletTool);
                SetStoredTool(player, NatureNetState.FromTool(walletTool));
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

    private void RestoreTemporaryTool(Farmer player)
    {
        if (PendingToolUse is null)
            return;

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

            RestorePlayerSpeedAfterTemporaryNetUse(player, pending);

            if (usedTool is not null)
            {
                if (temporarySlot >= 0 && temporarySlot < player.Items.Count)
                    player.Items[temporarySlot] = null;

                ClearWalletMarkers(usedTool);
                SetStoredTool(player, NatureNetState.FromTool(usedTool));
                RefreshWalletStateAfterStoredToolChange(player, false);
            }
            else
            {
                RemoveRuntimeToolCopies(player);
                SetStoredTool(player, null);
                RefreshWalletStateAfterStoredToolChange(player, false);
            }

            UpdateToolEnchantmentsForSelectedSlot(player, usedTool, pending.PreviousItem as Tool);
            RestoreTemporaryToolSlot(player, pending);
            if (IsWalletToolsRuntimeToolForPlayer(pending.PreviousItem, player) || HasWalletToolsRuntimeTool(player))
                TryRestoreWalletToolsPendingToolUse(player);
            player.CurrentToolIndex = Math.Max(0, Math.Min(pending.Slot, Math.Max(0, player.Items.Count - 1)));
            NormalizePlayerItemsAfterCleanup(player);
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

            if (IsPendingRuntimeToolForPlayer(slotItem, player))
                return pending.Slot;
        }

        for (int i = 0; i < player.Items.Count; i++)
        {
            Item? item = player.Items[i];
            if (ReferenceEquals(item, pending.TemporaryTool))
                return i;

            if (IsPendingRuntimeToolForPlayer(item, player))
                return i;
        }

        return -1;
    }

    private static bool IsPendingRuntimeToolForPlayer(Item? item, Farmer player)
    {
        return item is Tool tool && IsRuntimeToolForPlayer(tool, player) && IsNatureNet(tool);
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
        if (currentItem is null || ReferenceEquals(currentItem, pending.TemporaryTool) || IsPendingRuntimeToolForPlayer(currentItem, player))
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

    private void UpdateToolEnchantmentsForSelectedSlot(Farmer player, Tool? oldTool, Tool? newTool)
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
            Monitor.Log($"Could not update vanilla enchantment equip state for Nature in the Valley wallet tool: {ex.Message}", LogLevel.Warn);
        }
    }

    private bool TryTakeInventoryNatureNet(Farmer player, out Tool tool)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is Tool candidate && IsNatureNet(candidate) && !IsRuntimeTool(candidate) && !IsOvernightExposure(candidate) && !IsCurrentlySelectedItem(player, i, candidate))
            {
                ClearWalletMarkers(candidate);
                player.Items[i] = null;
                NormalizePlayerItemsAfterCleanup(player);
                tool = candidate;
                return true;
            }
        }

        tool = null!;
        return false;
    }

    private bool RemoveInventoryCopiesOfNatureNet(Farmer player)
    {
        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || IsCurrentlySelectedItem(player, i, tool) || !IsNatureNet(tool))
                continue;

            ClearWalletMarkers(tool);
            StoreBestNatureNet(player, tool);
            player.Items[i] = null;
            removed = true;
        }

        if (removed)
            NormalizePlayerItemsAfterCleanup(player);

        return removed;
    }

    private bool CollectLostFoundNatureNet(Farmer player)
    {
        bool changed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundNatureNet(player, out Tool? recoveredTool))
                break;

            StoreBestNatureNet(player, recoveredTool);
            changed = true;
        }

        return changed;
    }

    private void ScanLostFoundForNatureNet()
    {
        if (!IsNatureNetEnabled() || !Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        if (CollectLostFoundNatureNet(player))
            RefreshWalletStateAfterStoredToolChange(player);
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

    private static bool TryTakeLostFoundNatureNet(Farmer player, out Tool tool)
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

            if (TryTakeNatureNetFromValue(value, out tool, player, requireOwner))
                return true;
        }

        return false;
    }

    private static bool TryTakeNatureNetFromValue(object? value, out Tool tool, Farmer? player = null, bool requireOwner = false)
    {
        tool = null!;
        if (value is null || value is string)
            return false;

        if (TryTakeNatureNetFromWrappedValue(value, out tool, player, requireOwner))
            return true;

        object? unwrapped = TryGetNetValue(value);
        if (!ReferenceEquals(unwrapped, value) && unwrapped is not Tool && TryTakeNatureNetFromValue(unwrapped, out tool, player, requireOwner))
            return true;

        if (value is Tool directTool && IsNatureNet(directTool) && ToolIsEligibleForOwner(directTool, player, requireOwner))
        {
            ClearWalletMarkers(directTool);
            tool = directTool;
            return true;
        }

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Tool candidate && IsNatureNet(candidate) && ToolIsEligibleForOwner(candidate, player, requireOwner))
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
                if (dictionaryValue is Tool candidate && IsNatureNet(candidate) && ToolIsEligibleForOwner(candidate, player, requireOwner))
                {
                    ClearWalletMarkers(candidate);
                    dictionary.Remove(key);
                    tool = candidate;
                    return true;
                }

                if (TryTakeNatureNetFromValue(dictionaryValue, out tool, player, requireOwner))
                    return true;
            }
        }

        return false;
    }

    private static bool TryTakeNatureNetFromWrappedValue(object value, out Tool tool, Farmer? player = null, bool requireOwner = false)
    {
        tool = null!;
        try
        {
            PropertyInfo? property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null || property.GetIndexParameters().Length != 0 || !property.CanRead)
                return false;

            object? inner = property.GetValue(value);
            if (inner is not Tool candidate || !IsNatureNet(candidate))
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

    private static bool ToolIsEligibleForOwner(Tool tool, Farmer? player, bool requireOwner)
    {
        if (!requireOwner)
            return true;

        return player is not null && tool.modData.ContainsKey(RuntimeOwnerMarker) && IsOwnedByPlayer(tool, player);
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

    private static bool IsNatureCreatureInNetReach(Farmer player)
    {
        GameLocation? location = player.currentLocation;
        if (location is null || location.Name.Equals("NIVInnerInsec", StringComparison.OrdinalIgnoreCase))
            return false;

        Type? entryType = Type.GetType(NatureEntryTypeName);
        FieldInfo? creaturesField = entryType?.GetField("creatures", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (creaturesField?.GetValue(null) is not IEnumerable creatures)
            return false;

        Vector2 catchPoint = GetNatureNetCatchPoint(player);
        foreach (object? creature in creatures)
        {
            if (creature is null)
                continue;

            if (!CreatureIsCatchable(creature, location, catchPoint))
                continue;

            return true;
        }

        return false;
    }

    private static Vector2 GetNatureNetCatchPoint(Farmer player)
    {
        return player.FacingDirection switch
        {
            0 => player.Position + new Vector2(0f, -96f),
            1 => player.Position + new Vector2(96f, 0f),
            2 => player.Position + new Vector2(0f, 96f),
            3 => player.Position + new Vector2(-96f, 0f),
            _ => player.Position
        };
    }

    private static bool CreatureIsCatchable(object creature, GameLocation location, Vector2 catchPoint)
    {
        Type type = creature.GetType();
        if (IsCreatureExplicitlyExcluded(type, creature))
            return false;

        GameLocation? creatureLocation = InvokeGameLocationMethod(type, creature, "GetLocation");
        if (creatureLocation is null || !creatureLocation.Name.Equals(location.Name, StringComparison.OrdinalIgnoreCase))
            return false;

        Vector2 effectivePosition = InvokeVector2Method(type, creature, "GetEffectivePosition");
        bool isGrounded = GetBoolMember(type, creature, "IsGrounded", true);
        float scale = Math.Max(0.01f, GetFloatMember(type, creature, "scale", 1f));
        float catchingDifficultyMultiplier = Math.Max(0.01f, GetNatureConfigFloat("catchingDifficultyMultiplier", 1f));
        Vector2 adjustedPosition = effectivePosition + (isGrounded ? Vector2.Zero : new Vector2(0f, -30f));
        float catchRadius = (isGrounded ? 80f : 105f) * catchingDifficultyMultiplier * (float)Math.Sqrt(scale);
        return Vector2.Distance(adjustedPosition, catchPoint) < catchRadius;
    }

    private static bool IsCreatureExplicitlyExcluded(Type creatureType, object creature)
    {
        string name = GetStringMember(creatureType, creature, "name");
        if (string.IsNullOrWhiteSpace(name))
            return false;

        Type? entryType = Type.GetType(NatureEntryTypeName);
        FieldInfo? dataField = entryType?.GetField("staticCreatureData", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (dataField?.GetValue(null) is not IDictionary data || !data.Contains(name) || data[name] is not IList values)
            return false;

        return values.Count > 36 && values[35]?.ToString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static GameLocation? InvokeGameLocationMethod(Type type, object instance, string methodName)
    {
        try
        {
            return type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(instance, null) as GameLocation;
        }
        catch
        {
            return null;
        }
    }

    private static Vector2 InvokeVector2Method(Type type, object instance, string methodName)
    {
        try
        {
            object? value = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(instance, null);
            return value is Vector2 vector ? vector : Vector2.Zero;
        }
        catch
        {
            return Vector2.Zero;
        }
    }

    private static bool GetBoolMember(Type type, object instance, string name, bool fallback = false)
    {
        object? value = GetNamedMemberValue(type, instance, name);
        return value is bool boolValue ? boolValue : fallback;
    }

    private static float GetFloatMember(Type type, object instance, string name, float fallback)
    {
        object? value = GetNamedMemberValue(type, instance, name);
        return value switch
        {
            float f => f,
            double d => (float)d,
            int i => i,
            _ => fallback
        };
    }

    private static string GetStringMember(Type type, object instance, string name)
    {
        return GetNamedMemberValue(type, instance, name)?.ToString() ?? string.Empty;
    }

    private static object? GetNamedMemberValue(Type type, object instance, string name)
    {
        try
        {
            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
                return field.GetValue(instance);

            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null && property.GetIndexParameters().Length == 0)
                return property.GetValue(instance);
        }
        catch
        {
        }

        return null;
    }

    private static float GetNatureConfigFloat(string memberName, float fallback)
    {
        try
        {
            Type? entryType = Type.GetType(NatureEntryTypeName);
            object? config = entryType?.GetField("staticConfig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(null);
            if (config is null)
                return fallback;

            Type configType = config.GetType();
            object? value = GetNamedMemberValue(configType, config, memberName);
            return value switch
            {
                float f => f,
                double d => (float)d,
                int i => i,
                _ => fallback
            };
        }
        catch
        {
            return fallback;
        }
    }

    private static bool IsNatureNet(Item? item)
    {
        return item is Tool tool && !NatureNetState.IsErrorTool(tool) && TryGetNetDefinition(tool, out _);
    }

    private static void MarkRuntimeTool(Tool tool, Farmer player)
    {
        EnsureNatureNetInstanceId(tool);
        tool.modData[RuntimeToolMarker] = "true";
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

    private static void MarkOvernightExposure(Tool tool, Farmer player)
    {
        EnsureNatureNetInstanceId(tool);
        tool.modData[OvernightExposureMarker] = "true";
        MarkWalletOwner(tool, player);
    }

    private static bool IsOvernightExposure(Item? item)
    {
        return item is Tool tool && tool.modData.ContainsKey(OvernightExposureMarker);
    }

    private static bool IsOvernightExposureForPlayer(Item? item, Farmer player)
    {
        return item is Tool tool && tool.modData.ContainsKey(OvernightExposureMarker) && IsOwnedByPlayer(tool, player);
    }

    private static bool HasOvernightExposure(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (IsOvernightExposure(item) && IsNatureNet(item))
                return true;
        }

        return false;
    }

    private static void ClearWalletMarkers(Tool tool)
    {
        tool.modData.Remove(RuntimeToolMarker);
        tool.modData.Remove(OvernightExposureMarker);
        tool.modData.Remove(RuntimeOwnerMarker);
    }

    private static bool IsWalletToolsRuntimeToolForPlayer(Item? item, Farmer player)
    {
        if (item is not Tool tool || !tool.modData.ContainsKey(WalletToolsRuntimeToolMarker))
            return false;

        if (!tool.modData.TryGetValue(WalletToolsOwnerPlayerIdMarker, out string? rawOwnerId))
            return !Context.IsMultiplayer;

        return long.TryParse(rawOwnerId, out long ownerId) && ownerId == player.UniqueMultiplayerID;
    }

    private static bool HasWalletToolsRuntimeTool(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (IsWalletToolsRuntimeToolForPlayer(item, player))
                return true;
        }

        return false;
    }

    private void TryRestoreWalletToolsPendingToolUse(Farmer player)
    {
        try
        {
            Type? walletToolsEntryType = Type.GetType(WalletToolsModEntryTypeName);
            object? walletToolsEntry = walletToolsEntryType?.GetField("Instance", BindingFlags.Static | BindingFlags.NonPublic)?.GetValue(null);
            MethodInfo? restoreMethod = walletToolsEntryType?.GetMethod("RestorePendingToolUseBeforeDestructiveCleanup", BindingFlags.Instance | BindingFlags.NonPublic);
            restoreMethod?.Invoke(walletToolsEntry, new object[] { player });
        }
        catch
        {
        }
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

        NatureNetState? storedTool = GetStoredTool(player);
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
            else if (storedTool is not null)
            {
                toolToPreserve = storedTool.CreateTool(Monitor);
                SetStoredTool(player, null);
            }

            if (toolToPreserve is not null)
                ClearWalletMarkers(toolToPreserve);

            RestoreTemporaryToolSlot(player, pending);

            if (toolToPreserve is not null)
                AddToolToInventoryOrAppend(player, toolToPreserve);

            player.CurrentToolIndex = Math.Max(0, Math.Min(pending.Slot, Math.Max(0, player.Items.Count - 1)));
            NormalizePlayerItemsAfterCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private void ClearLegacySaveData()
    {
        if (!Context.IsWorldReady || !Context.IsMainPlayer)
            return;

        Helper.Data.WriteSaveData<NatureNetState>(LegacySaveDataKey, null);
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
            NormalizePlayerItemsAfterCleanup(player);
    }

    private static void ClearWalletMarkersFromInventory(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && (tool.modData.ContainsKey(RuntimeToolMarker) || tool.modData.ContainsKey(OvernightExposureMarker) || tool.modData.ContainsKey(RuntimeOwnerMarker)))
                ClearWalletMarkers(tool);
        }
    }

    private static bool IsCurrentlySelectedItem(Farmer player, int slot, Item item)
    {
        return slot == player.CurrentToolIndex && ReferenceEquals(player.CurrentItem, item);
    }

    private static bool IsPlayerUsingTool(Farmer player)
    {
        return player.UsingTool || player.hasBuff(NatureNetSpeedBuffId);
    }

    private static bool IsTemporaryNatureNetUseInProgress(Farmer player, TemporaryToolUse? pending)
    {
        return pending is not null
            && IsNatureNet(pending.TemporaryTool)
            && (IsNatureNetHeld(pending.TemporaryTool) || player.hasBuff(NatureNetSpeedBuffId));
    }

    private static bool IsTemporaryNatureNetHeld(TemporaryToolUse? pending)
    {
        return pending is not null && IsNatureNet(pending.TemporaryTool) && IsNatureNetHeld(pending.TemporaryTool);
    }

    private static bool IsNatureNetHeld(Tool tool)
    {
        try
        {
            FieldInfo? field = tool.GetType().GetField("isHeld", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(tool) is bool held && held;
        }
        catch
        {
            return false;
        }
    }

    private bool TryReleaseTemporaryNatureNetUse(Farmer player)
    {
        TemporaryToolUse? pending = PendingToolUse;
        if (!IsTemporaryNatureNetHeld(pending) || player.currentLocation is null)
            return false;

        try
        {
            pending!.TemporaryTool.onRelease(player.currentLocation, 0, 0, player);
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to release Nature in the Valley wallet net: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private static void RestorePlayerSpeedAfterTemporaryNetUse(Farmer player, TemporaryToolUse pending)
    {
        if (!IsNatureNet(pending.TemporaryTool))
            return;

        ClearTemporaryNatureNetEffects(player, pending);
        player.addedSpeed = pending.PreviousAddedSpeed;
    }

    private static void ClearTemporaryNatureNetEffects(Farmer player, TemporaryToolUse pending)
    {
        if (!IsNatureNet(pending.TemporaryTool))
            return;

        if (player.hasBuff(NatureNetSpeedBuffId))
            player.buffs.Remove(NatureNetSpeedBuffId);

        player.stopJittering();
        player.jitterStrength = 0f;
        player.canMove = true;
        player.UsingTool = false;

        try
        {
            FieldInfo? field = pending.TemporaryTool.GetType().GetField("isHeld", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            field?.SetValue(pending.TemporaryTool, false);
        }
        catch
        {
        }
    }

    private static void EnsureNatureNetInstanceId(Tool tool)
    {
        if (!tool.modData.ContainsKey(NatureNetInstanceIdMarker) || string.IsNullOrWhiteSpace(tool.modData[NatureNetInstanceIdMarker]))
            tool.modData[NatureNetInstanceIdMarker] = Game1.random.Next().ToString();
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
                NormalizePlayerItemsAfterCleanup(player);
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(tool);
            NormalizePlayerItemsAfterCleanup(player);
            return;
        }

        player.Items.Add(tool);
        NormalizePlayerItemsAfterCleanup(player);
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
                NormalizePlayerItemsAfterCleanup(player);
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(tool);
            NormalizePlayerItemsAfterCleanup(player);
            return;
        }

        player.addItemByMenuIfNecessary(tool);
        NormalizePlayerItemsAfterCleanup(player);
    }

    private static int GetPlayerMaxItemCount(Farmer player)
    {
        foreach (string memberName in new[] { "MaxItems", "maxItems" })
        {
            try
            {
                PropertyInfo? property = player.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(player) is int propertyValue && propertyValue > 0)
                    return Math.Max(12, propertyValue);

                FieldInfo? field = player.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(player) is int fieldValue && fieldValue > 0)
                    return Math.Max(12, fieldValue);
            }
            catch
            {
            }
        }

        return 36;
    }

    private static void NormalizePlayerItemsAfterCleanup(Farmer player)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        if (player.Items.Count <= maxItems)
            return;

        for (int i = maxItems; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not null)
                return;
        }

        while (player.Items.Count > maxItems)
            player.Items.RemoveAt(player.Items.Count - 1);
    }

    private void RefreshWalletStateAfterStoredToolChange(Farmer player, bool invalidatePowers = true)
    {
        ClearLegacySaveData();
        UpdateWalletFlag(player);
        if (invalidatePowers)
            InvalidatePowers();
    }

    private void UpdateWalletFlag(Farmer player)
    {
        if (IsNatureNetEnabled() && GetStoredTool(player) is not null)
            player.modData[HasNatureNetFlag] = "true";
        else
            player.modData.Remove(HasNatureNetFlag);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }
    private static class WalletToolsPrepareWalletToolUsePatch
    {
        public static bool Prefix(Farmer player, ref bool __result)
        {
            if (Instance?.ShouldReservePressForNatureNet(player) == true)
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    private static class PressUseToolButtonPatch
    {
        public static void Prefix()
        {
            Instance?.TryPrepareWalletToolUse(Game1.player);
        }
    }

    private sealed class TemporaryToolUse
    {
        public TemporaryToolUse(int slot, Tool temporaryTool, Item? previousItem, float previousAddedSpeed)
        {
            Slot = slot;
            TemporaryTool = temporaryTool;
            PreviousItem = previousItem;
            PreviousAddedSpeed = previousAddedSpeed;
        }

        public int Slot { get; }
        public Tool TemporaryTool { get; }
        public Item? PreviousItem { get; }
        public float PreviousAddedSpeed { get; }
    }

    private sealed class NatureNetIconData
    {
        public NatureNetIconData(string texturePath, Point texturePosition)
        {
            TexturePath = texturePath;
            TexturePosition = texturePosition;
        }

        public string TexturePath { get; }
        public Point TexturePosition { get; }
    }

    private sealed class NatureNetState
    {
        public string ItemId { get; set; } = "NIVNet";
        public string QualifiedItemId { get; set; } = "(T)NIVNet";
        public int UpgradeLevel { get; set; }
        public Dictionary<string, string> ModData { get; set; } = new();

        public static NatureNetState CreatePlaceholder(IMonitor monitor)
        {
            Tool? tool = CreateFreshTool(GetDefinitionForItemId("NIVNet"), monitor);
            return tool is null ? new NatureNetState() : FromTool(tool);
        }

        public static NatureNetState FromTool(Tool tool)
        {
            EnsureNatureNetInstanceId(tool);
            NetDefinition definition = TryGetNetDefinition(tool, out NetDefinition matchedDefinition)
                ? matchedDefinition
                : GetDefinitionForItemId("NIVNet");

            NatureNetState state = new()
            {
                ItemId = definition.ItemId,
                QualifiedItemId = definition.QualifiedItemId,
                UpgradeLevel = definition.UpgradeLevel
            };

            foreach (KeyValuePair<string, string> pair in tool.modData.Pairs)
            {
                if (pair.Key == RuntimeToolMarker || pair.Key == OvernightExposureMarker || pair.Key == RuntimeOwnerMarker)
                    continue;

                state.ModData[pair.Key] = pair.Value;
            }

            return state;
        }

        public string GetDisplayName(IMonitor monitor)
        {
            Tool? tool = CreateTool(monitor);
            if (tool is not null && !string.IsNullOrWhiteSpace(tool.DisplayName) && !IsErrorTool(tool))
                return tool.DisplayName;

            return GetDefinitionForItemId(ItemId).DisplayNameFallback;
        }

        public string GetDescription(IMonitor monitor)
        {
            Tool? tool = CreateTool(monitor);
            return tool is not null && !IsErrorTool(tool) ? tool.getDescription() : string.Empty;
        }

        public NatureNetIconData GetIconData(IMonitor monitor)
        {
            NetDefinition definition = GetDefinitionForItemId(ItemId);
            return new NatureNetIconData(definition.TextureAssetPath, Point.Zero);
        }

        public Tool? CreateTool(IMonitor monitor)
        {
            NetDefinition definition = GetDefinitionForItemId(ItemId);
            Tool? tool = CreateFreshTool(definition, monitor);
            if (tool is null)
                return null;

            foreach (KeyValuePair<string, string> pair in ModData)
            {
                if (pair.Key == RuntimeToolMarker || pair.Key == OvernightExposureMarker || pair.Key == RuntimeOwnerMarker)
                    continue;

                tool.modData[pair.Key] = pair.Value;
            }

            EnsureNatureNetInstanceId(tool);
            return tool;
        }

        private static Tool? CreateFreshTool(NetDefinition definition, IMonitor monitor)
        {
            foreach (string id in definition.ItemRegistryIds)
            {
                try
                {
                    Tool tool = ItemRegistry.Create<Tool>(id);
                    EnsureNatureNetInstanceId(tool);
                    if (!IsErrorTool(tool) && TryGetNetDefinition(tool, out _))
                        return tool;
                }
                catch
                {
                }
            }

            return null;
        }

        public static bool IsErrorTool(Tool tool)
        {
            string text = string.Join("\n", tool.Name, tool.DisplayName, tool.ItemId, tool.QualifiedItemId, tool.GetType().FullName);
            return text.Contains("Error Item", StringComparison.OrdinalIgnoreCase) || text.Contains("ErrorItem", StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class NetDefinition
    {
        public NetDefinition(string itemId, string qualifiedItemId, int upgradeLevel, string displayNameFallback, string textureAssetPath, string embeddedTextureName, string className, params string[] aliases)
        {
            ItemId = itemId;
            QualifiedItemId = qualifiedItemId;
            UpgradeLevel = upgradeLevel;
            DisplayNameFallback = displayNameFallback;
            TextureAssetPath = textureAssetPath;
            EmbeddedTextureName = embeddedTextureName;
            ClassName = className;
            Aliases = aliases;
        }

        public string ItemId { get; }
        public string QualifiedItemId { get; }
        public int UpgradeLevel { get; }
        public string DisplayNameFallback { get; }
        public string TextureAssetPath { get; }
        public string EmbeddedTextureName { get; }
        public string ClassName { get; }
        public string[] Aliases { get; }

        public IEnumerable<string> ItemRegistryIds
        {
            get
            {
                yield return QualifiedItemId;
                yield return ItemId;
                foreach (string alias in Aliases)
                    yield return alias;
            }
        }

        public bool MatchesText(string text)
        {
            if (text.Contains(ItemId, StringComparison.OrdinalIgnoreCase)
                || text.Contains(QualifiedItemId, StringComparison.OrdinalIgnoreCase)
                || text.Contains(ClassName, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (string alias in Aliases)
            {
                if (text.Contains(alias, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }

    private static bool TryGetNetDefinition(Tool tool, out NetDefinition definition)
    {
        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName, tool.GetType().FullName);
        foreach (NetDefinition candidate in NetDefinitions.OrderByDescending(candidateDefinition => candidateDefinition.UpgradeLevel))
        {
            if (candidate.MatchesText(text))
            {
                definition = candidate;
                return true;
            }
        }

        definition = NetDefinitions[0];
        return false;
    }

    private static NetDefinition GetDefinitionForItemId(string itemId)
    {
        foreach (NetDefinition definition in NetDefinitions)
        {
            if (definition.ItemId.Equals(itemId, StringComparison.OrdinalIgnoreCase))
                return definition;

            foreach (string alias in definition.Aliases)
            {
                if (alias.Equals(itemId, StringComparison.OrdinalIgnoreCase) || alias.Equals($"(T){itemId}", StringComparison.OrdinalIgnoreCase))
                    return definition;
            }
        }

        return NetDefinitions[0];
    }

    private bool StoreBestNatureNet(Farmer player, Tool tool)
    {
        NatureNetState incoming = NatureNetState.FromTool(tool);
        NatureNetState? current = GetStoredTool(player);
        if (current is not null && current.UpgradeLevel > incoming.UpgradeLevel)
            return false;

        SetStoredTool(player, incoming);
        return true;
    }

}
