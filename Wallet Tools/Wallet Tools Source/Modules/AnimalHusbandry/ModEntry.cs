using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Powers;
using StardewValley.Locations;
using StardewValley.Tools;

using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletToolsForAnimalHusbandry;

internal sealed class AnimalHusbandryModule : WalletModule
{
    internal const string ModuleKey = "AnimalHusbandry";
    internal const string LegacyUniqueId = "ThaleTheGreat.WalletToolsForAnimalHusbandry";

    internal AnimalHusbandryModule(ThaleTheGreat.WalletTools.ModEntry host)
        : base(host, ModuleKey, "Animal Husbandry", LegacyUniqueId, "DIGUS.ANIMALHUSBANDRYMOD")
    {
    }
    private static AnimalHusbandryModule? Instance;
    private const string LegacySaveDataKey = "WalletToolsForAnimalHusbandry.MeatTool";
    private const string MeatToolItemId = "DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolQualifiedItemId = "(T)DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolModDataKey = "DIGUS.ANIMALHUSBANDRYMOD/MeatCleaver";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_AnimalHusbandryMeatTool";
    private const string HasMeatToolFlag = "ThaleTheGreat.WalletToolsForAnimalHusbandry/HasMeatTool";
    private const string RuntimeToolMarker = "ThaleTheGreat.WalletToolsForAnimalHusbandry/RuntimeTool";
    private const string RuntimeOwnerMarker = "ThaleTheGreat.WalletToolsForAnimalHusbandry/OwnerPlayerId";
    private const string OvernightExposureMarker = "ThaleTheGreat.WalletToolsForAnimalHusbandry/OvernightExposure";
    private const string ToolTexturePath = "Mods/DIGUS.ANIMALHUSBANDRYMOD/Tools";
    private const int MeatCleaverMenuSpriteIndex = 26;
    private const int MeatWandMenuSpriteIndex = 33;

    private ModConfig Config = new();
    private Harmony Harmony = null!;
    private bool GmcmRegistered;
    private readonly Dictionary<long, MeatToolState?> StoredToolByPlayer = new();
    private MeatToolState? StoredTool
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
    }

    private static long GetWalletOwnerId(Farmer? player)
    {
        return player?.UniqueMultiplayerID ?? 0L;
    }

    private MeatToolState? GetStoredTool(Farmer? player)
    {
        StoredToolByPlayer.TryGetValue(GetWalletOwnerId(player), out MeatToolState? state);
        return state;
    }

    private void SetStoredTool(Farmer? player, MeatToolState? state)
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
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers") || !Context.IsWorldReady || !IsMeatToolEnabled())
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            MeatToolState state = GetStoredTool(Game1.player) ?? MeatToolState.CreatePlaceholder(Monitor);
            MeatToolIconData icon = state.GetIconData(Monitor);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = state.GetDisplayName(Monitor),
                Description = state.GetDescription(Monitor),
                TexturePath = icon.TexturePath,
                TexturePosition = icon.TexturePosition,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {HasMeatToolFlag} true",
                CustomFields = Host.GetWalletPowerCustomFields()
            };
        });
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
            AddBool(gmcm, nameof(Config.ModEnabled), () => Config.ModEnabled, SetModEnabled, "Mod Enabled", "Enables Wallet Tools for Animal Husbandry.");
            AddBool(gmcm, nameof(Config.AutoUseEnabled), () => Config.AutoUseEnabled, value => SetAutoUseMasterEnabled(value, false), "Enable Auto Use", "Automatically supplies the stored Meat Cleaver or Meat Wand when using it on a nearby farm animal. Manual hotkey use still works when this is disabled.");
            AddBool(gmcm, nameof(Config.RequireLeftControlForAutoUse), () => Config.RequireLeftControlForAutoUse, value => Config.RequireLeftControlForAutoUse = value, "Require Left Ctrl + Click", "Require Left Ctrl to be held while clicking an animal before the wallet Meat Cleaver or Meat Wand is supplied automatically.");
            AddBool(gmcm, nameof(Config.PlayToolSwapSound), () => Config.PlayToolSwapSound, value => Config.PlayToolSwapSound = value, "Play Tool Swap Sound", "Play the Wallet Tools swap sound when the meat tool is readied.");
            AddBool(gmcm, nameof(Config.ShowHudMessageWhenStored), () => Config.ShowHudMessageWhenStored, value => Config.ShowHudMessageWhenStored = value, "Show Stored Message", "Show a HUD message when the Meat Cleaver or Meat Wand is moved to the wallet.");
            AddKeybind(gmcm, nameof(Config.ToggleAutoUseHotkey), () => Config.ToggleAutoUseHotkey, value => Config.ToggleAutoUseHotkey = value, "Toggle Auto Use", "Hotkey to toggle automatic Animal Husbandry wallet use on or off.");
            AddKeybind(gmcm, nameof(Config.UseMeatToolHotkey), () => Config.UseMeatToolHotkey, value => Config.UseMeatToolHotkey = value, "Use Meat Tool", "Immediately use the wallet Meat Cleaver or Meat Wand.");
            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools for Animal Husbandry options with Generic Mod Config Menu: {ex}", LogLevel.Error);
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

        if (!IsMeatToolEnabled())
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
                Game1.addHUDMessage(new HUDMessage(enabled ? "Animal Husbandry wallet auto use enabled." : "Animal Husbandry wallet auto use disabled.", HUDMessage.newQuest_type));
        }

        Host.WriteModuleConfig(ModuleKey, Config);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ClearVolatileState(false);

        if (Context.IsMainPlayer)
        {
            MeatToolState? legacyState = Host.ReadSaveDataWithLegacy<MeatToolState>(LegacySaveDataKey, LegacyUniqueId);
            if (legacyState is not null)
                SetStoredTool(Game1.player, legacyState);

            Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
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
            ScanLostFoundForMeatTool();
    }

    private void UpdatePendingTemporaryToolUse()
    {
        if (PendingToolUse is null)
            return;

        if (PendingManualUseHotkey.HasValue)
        {
            if (IsManualHotkeyPhysicallyHeld())
                UpdateManualWalletToolCharge(Game1.player);
            else
                FinishManualWalletToolHotkeyUse(Game1.player);
        }
        else if (!IsPlayerUsingTool(Game1.player))
            RestoreTemporaryTool(Game1.player);
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

        if (Config.UseMeatToolHotkey.JustPressed())
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
        if (!Context.IsWorldReady || PendingManualUseHotkey != e.Button)
            return;

        if (IsManualHotkeyPhysicallyHeld())
            return;

        FinishManualWalletToolHotkeyUse(Game1.player);
    }

    private void TryUseWalletToolHotkey(Farmer player, SButton pressedButton)
    {
        if (!IsMeatToolEnabled() || GetStoredTool(player) is null)
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
            if (player.UsingTool)
                player.EndUsingTool();
        }

        if (PendingToolUse is not null && !IsPlayerUsingTool(player))
            RestoreTemporaryTool(player);
    }

    private bool IsManualHotkeyPhysicallyHeld()
    {
        return PendingManualUseHotkey.HasValue
            && (Helper.Input.IsDown(PendingManualUseHotkey.Value) || Helper.Input.IsSuppressed(PendingManualUseHotkey.Value));
    }

    private bool IsMeatToolEnabled()
    {
        return Config.ModEnabled;
    }

    private bool IsAutoUseEnabled()
    {
        return IsMeatToolEnabled() && Config.AutoUseEnabled;
    }


    private bool TryPrepareWalletToolUse(Farmer player)
    {
        if (PendingToolUse is not null || !IsAutoUseEnabled() || Game1.fadeToBlack || !Context.CanPlayerMove || Game1.activeClickableMenu is not null)
            return false;

        if (IsMeatTool(player.CurrentItem))
            return false;

        if (Config.RequireLeftControlForAutoUse && !Helper.Input.IsDown(SButton.LeftControl))
            return false;

        if (!IsAnimalInToolReach(player))
            return false;

        return TrySetTemporaryWalletTool(player);
    }

    private bool IsAutoUseTrigger(SButton button)
    {
        if (!Config.RequireLeftControlForAutoUse)
            return button is SButton.MouseLeft or SButton.ControllerA;

        return button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftControl);
    }

    private bool CanConvertInventoryTools(Farmer player)
    {
        return IsMeatToolEnabled() && PendingToolUse is null && !IsPlayerUsingTool(player);
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
                if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || IsCurrentlySelectedItem(player, i, tool) || !IsMeatTool(tool))
                    continue;

                SetStoredTool(player, MeatToolState.FromTool(tool));
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
            Game1.addHUDMessage(new HUDMessage("Animal Husbandry tool moved to wallet.", HUDMessage.newQuest_type));
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
                if (RemoveInventoryCopiesOfMeatTool(player))
                    changed = true;
                if (CollectLostFoundMeatTool(player))
                    changed = true;
            }
            else
            {
                if (TryTakeInventoryMeatTool(player, out Tool? inventoryTool))
                {
                    SetStoredTool(player, MeatToolState.FromTool(inventoryTool));
                    changed = true;
                }

                if (CollectLostFoundMeatTool(player))
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
        MeatToolState? storedTool = GetStoredTool(player);
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
            Game1.addHUDMessage(new HUDMessage("Animal Husbandry tool returned to inventory.", HUDMessage.newQuest_type));
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
        MeatToolState? storedTool = GetStoredTool(player);
        if (!IsMeatToolEnabled() || storedTool is null || HasOvernightExposure(player))
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
                if (player.Items[i] is Tool tool && IsOvernightExposureForPlayer(tool, player) && IsMeatTool(tool))
                {
                    ClearWalletMarkers(tool);
                    SetStoredTool(player, MeatToolState.FromTool(tool));
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
        MeatToolState? storedTool = GetStoredTool(player);
        if (PendingToolUse is not null || !IsMeatToolEnabled() || storedTool is null)
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
            PendingToolUse = new TemporaryToolUse(previousToolIndex, walletTool, previousItem);
        }
        catch
        {
            if (leasedFromWallet)
            {
                ClearWalletMarkers(walletTool);
                SetStoredTool(player, MeatToolState.FromTool(walletTool));
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

            if (usedTool is not null)
            {
                if (temporarySlot >= 0 && temporarySlot < player.Items.Count)
                    player.Items[temporarySlot] = null;

                ClearWalletMarkers(usedTool);
                SetStoredTool(player, MeatToolState.FromTool(usedTool));
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
        return item is Tool tool && IsRuntimeToolForPlayer(tool, player) && IsMeatTool(tool);
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
            Monitor.Log($"Could not update vanilla enchantment equip state for Animal Husbandry wallet tool: {ex.Message}", LogLevel.Warn);
        }
    }

    private bool TryTakeInventoryMeatTool(Farmer player, out Tool tool)
    {
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is Tool candidate && IsMeatTool(candidate) && !IsRuntimeTool(candidate) && !IsOvernightExposure(candidate) && !IsCurrentlySelectedItem(player, i, candidate))
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

    private bool RemoveInventoryCopiesOfMeatTool(Farmer player)
    {
        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || IsCurrentlySelectedItem(player, i, tool) || !IsMeatTool(tool))
                continue;

            ClearWalletMarkers(tool);
            SetStoredTool(player, MeatToolState.FromTool(tool));
            player.Items[i] = null;
            removed = true;
        }

        if (removed)
            NormalizePlayerItemsAfterCleanup(player);

        return removed;
    }

    private bool CollectLostFoundMeatTool(Farmer player)
    {
        bool changed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundMeatTool(player, out Tool? recoveredTool))
                break;

            SetStoredTool(player, MeatToolState.FromTool(recoveredTool));
            changed = true;
        }

        return changed;
    }

    private void ScanLostFoundForMeatTool()
    {
        if (!IsMeatToolEnabled() || !Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        if (CollectLostFoundMeatTool(player))
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

    private static bool TryTakeLostFoundMeatTool(Farmer player, out Tool tool)
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

            if (TryTakeMeatToolFromValue(value, out tool, player, requireOwner))
                return true;
        }

        return false;
    }

    private static bool TryTakeMeatToolFromValue(object? value, out Tool tool, Farmer? player = null, bool requireOwner = false)
    {
        tool = null!;
        if (value is null || value is string)
            return false;

        if (TryTakeMeatToolFromWrappedValue(value, out tool, player, requireOwner))
            return true;

        object? unwrapped = TryGetNetValue(value);
        if (!ReferenceEquals(unwrapped, value) && unwrapped is not Tool && TryTakeMeatToolFromValue(unwrapped, out tool, player, requireOwner))
            return true;

        if (value is Tool directTool && IsMeatTool(directTool) && ToolIsEligibleForOwner(directTool, player, requireOwner))
        {
            ClearWalletMarkers(directTool);
            tool = directTool;
            return true;
        }

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Tool candidate && IsMeatTool(candidate) && ToolIsEligibleForOwner(candidate, player, requireOwner))
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
                if (dictionaryValue is Tool candidate && IsMeatTool(candidate) && ToolIsEligibleForOwner(candidate, player, requireOwner))
                {
                    ClearWalletMarkers(candidate);
                    dictionary.Remove(key);
                    tool = candidate;
                    return true;
                }

                if (TryTakeMeatToolFromValue(dictionaryValue, out tool, player, requireOwner))
                    return true;
            }
        }

        return false;
    }

    private static bool TryTakeMeatToolFromWrappedValue(object value, out Tool tool, Farmer? player = null, bool requireOwner = false)
    {
        tool = null!;
        try
        {
            PropertyInfo? property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null || property.GetIndexParameters().Length != 0 || !property.CanRead)
                return false;

            object? inner = property.GetValue(value);
            if (inner is not Tool candidate || !IsMeatTool(candidate))
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

    private static bool IsAnimalInToolReach(Farmer player)
    {
        GameLocation location = player.currentLocation;
        if (location is not (Farm or AnimalHouse))
            return false;

        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        tile = new Vector2((int)tile.X, (int)tile.Y);

        foreach (FarmAnimal animal in location.getAllFarmAnimals())
        {
            if (animal.currentLocation == location && Vector2.Distance(tile, animal.Tile) <= 1f)
                return true;
        }

        return false;
    }

    private static bool IsMeatTool(Item? item)
    {
        if (item is not Tool tool || MeatToolState.IsErrorTool(tool))
            return false;

        if (tool.modData.ContainsKey(MeatToolModDataKey))
            return true;

        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        return text.Contains(MeatToolItemId, StringComparison.OrdinalIgnoreCase)
            || text.Contains(MeatToolQualifiedItemId, StringComparison.OrdinalIgnoreCase)
            || text.Contains("MeatCleaver", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Meat Cleaver", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Meat Wand", StringComparison.OrdinalIgnoreCase);
    }

    private static void MarkRuntimeTool(Tool tool, Farmer player)
    {
        EnsureMeatToolInstanceId(tool);
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
        EnsureMeatToolInstanceId(tool);
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
            if (IsOvernightExposure(item) && IsMeatTool(item))
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

    private void RestorePendingToolUseBeforeDestructiveCleanup(Farmer player)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);
    }

    private void RestorePendingToolUseToInventoryBeforeVolatileClear(Farmer player)
    {
        if (PendingToolUse is null)
            return;

        MeatToolState? storedTool = GetStoredTool(player);
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

        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
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
        return player.UsingTool;
    }

    private static void EnsureMeatToolInstanceId(Tool tool)
    {
        if (!tool.modData.ContainsKey(MeatToolModDataKey) || string.IsNullOrWhiteSpace(tool.modData[MeatToolModDataKey]))
            tool.modData[MeatToolModDataKey] = Game1.random.Next().ToString();
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
        if (IsMeatToolEnabled() && GetStoredTool(player) is not null)
            player.modData[HasMeatToolFlag] = "true";
        else
            player.modData.Remove(HasMeatToolFlag);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
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
        public TemporaryToolUse(int slot, Tool temporaryTool, Item? previousItem)
        {
            Slot = slot;
            TemporaryTool = temporaryTool;
            PreviousItem = previousItem;
        }

        public int Slot { get; }
        public Tool TemporaryTool { get; }
        public Item? PreviousItem { get; }
    }

    private sealed class MeatToolIconData
    {
        public MeatToolIconData(string texturePath, Point texturePosition)
        {
            TexturePath = texturePath;
            TexturePosition = texturePosition;
        }

        public string TexturePath { get; }
        public Point TexturePosition { get; }
    }

    private sealed class MeatToolState
    {
        public Dictionary<string, string> ModData { get; set; } = new();

        public static MeatToolState CreatePlaceholder(IMonitor monitor)
        {
            Tool? tool = CreateFreshTool(monitor);
            return tool is null ? new MeatToolState() : FromTool(tool);
        }

        public static MeatToolState FromTool(Tool tool)
        {
            EnsureMeatToolInstanceId(tool);
            MeatToolState state = new();
            foreach (KeyValuePair<string, string> pair in tool.modData.Pairs)
            {
                if (pair.Key == RuntimeToolMarker || pair.Key == OvernightExposureMarker)
                    continue;

                state.ModData[pair.Key] = pair.Value;
            }

            return state;
        }

        public string GetDisplayName(IMonitor monitor)
        {
            Tool? tool = CreateTool(monitor);
            int menuSpriteIndex = GetToolDataInt(tool, "MenuSpriteIndex") ?? GetToolMenuSpriteIndex(tool);
            return menuSpriteIndex switch
            {
                MeatWandMenuSpriteIndex => "Meat Wand",
                MeatCleaverMenuSpriteIndex => "Meat Cleaver",
                _ when tool is not null && !string.IsNullOrWhiteSpace(tool.DisplayName) => tool.DisplayName,
                _ => "Meat Cleaver"
            };
        }

        public string GetDescription(IMonitor monitor)
        {
            Tool? tool = CreateTool(monitor);
            return tool?.getDescription() ?? string.Empty;
        }

        public MeatToolIconData GetIconData(IMonitor monitor)
        {
            Tool? tool = CreateTool(monitor);
            string texturePath = GetToolDataString(tool, "Texture") ?? ToolTexturePath;
            int menuSpriteIndex = GetToolDataInt(tool, "MenuSpriteIndex") ?? GetToolMenuSpriteIndex(tool);
            Point texturePosition = menuSpriteIndex >= 0
                ? GetTexturePositionFromMenuIndex(texturePath, menuSpriteIndex, monitor)
                : GetTexturePositionFromMenuIndex(ToolTexturePath, MeatWandMenuSpriteIndex, monitor);

            return new MeatToolIconData(texturePath, texturePosition);
        }

        public Tool? CreateTool(IMonitor monitor)
        {
            Tool? tool = CreateFreshTool(monitor);
            if (tool is null)
                return null;

            foreach (KeyValuePair<string, string> pair in ModData)
            {
                if (pair.Key == RuntimeToolMarker || pair.Key == OvernightExposureMarker)
                    continue;

                tool.modData[pair.Key] = pair.Value;
            }

            EnsureMeatToolInstanceId(tool);
            return tool;
        }

        private static Tool? CreateFreshTool(IMonitor monitor)
        {
            foreach (string id in new[] { MeatToolItemId, MeatToolQualifiedItemId })
            {
                try
                {
                    Tool tool = ItemRegistry.Create<Tool>(id);
                    EnsureMeatToolInstanceId(tool);
                    if (!IsErrorTool(tool))
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
            string text = string.Join("\n", tool.Name, tool.DisplayName, tool.ItemId, tool.QualifiedItemId);
            return text.Contains("Error Item", StringComparison.OrdinalIgnoreCase) || text.Contains("ErrorItem", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetToolMenuSpriteIndex(Tool? tool)
        {
            if (tool is null)
                return -1;

            foreach (string memberName in new[] { "IndexOfMenuItemView", "indexOfMenuItemView" })
            {
                try
                {
                    PropertyInfo? property = tool.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property?.GetValue(tool) is int propertyValue && propertyValue >= 0)
                        return propertyValue;

                    FieldInfo? field = tool.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field?.GetValue(tool) is int fieldValue && fieldValue >= 0)
                        return fieldValue;
                }
                catch
                {
                }
            }

            return -1;
        }

        private static string? GetToolDataString(Tool? tool, string propertyName)
        {
            object? data = GetToolData(tool);
            if (data is null)
                return null;

            try
            {
                PropertyInfo? property = data.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(data) is string value && !string.IsNullOrWhiteSpace(value))
                    return value;

                FieldInfo? field = data.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(data) is string fieldValue && !string.IsNullOrWhiteSpace(fieldValue))
                    return fieldValue;
            }
            catch
            {
            }

            return null;
        }

        private static int? GetToolDataInt(Tool? tool, string propertyName)
        {
            object? data = GetToolData(tool);
            if (data is null)
                return null;

            try
            {
                PropertyInfo? property = data.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property?.GetValue(data) is int value && value >= 0)
                    return value;

                FieldInfo? field = data.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field?.GetValue(data) is int fieldValue && fieldValue >= 0)
                    return fieldValue;
            }
            catch
            {
            }

            return null;
        }

        private static object? GetToolData(Tool? tool)
        {
            if (tool is null)
                return null;

            try
            {
                MethodInfo? method = typeof(Tool).GetMethod("GetToolData", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                return method?.Invoke(tool, null);
            }
            catch
            {
                return null;
            }
        }

        private static Point GetTexturePositionFromMenuIndex(string texturePath, int menuSpriteIndex, IMonitor monitor)
        {
            int textureWidth = GetTextureWidth(texturePath, monitor);
            int columns = Math.Max(1, textureWidth / 16);
            int x = Math.Max(0, menuSpriteIndex % columns) * 16;
            int y = Math.Max(0, menuSpriteIndex / columns) * 16;
            return new Point(x, y);
        }

        private static int GetTextureWidth(string texturePath, IMonitor monitor)
        {
            try
            {
                Texture2D texture = Game1.content.Load<Texture2D>(texturePath);
                if (texture.Width > 0)
                    return texture.Width;
            }
            catch
            {
            }

            return texturePath.Equals(ToolTexturePath, StringComparison.OrdinalIgnoreCase) ? 336 : 336;
        }
    }
}
