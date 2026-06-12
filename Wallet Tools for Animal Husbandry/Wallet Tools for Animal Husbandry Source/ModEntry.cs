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
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Powers;
using StardewValley.Locations;
using StardewValley.Tools;

namespace ThaleTheGreat.WalletToolsForAnimalHusbandry;

public sealed class ModEntry : Mod
{
    private static ModEntry? Instance;
    private const string LegacySaveDataKey = "WalletToolsForAnimalHusbandry.MeatTool";
    private const string MeatToolItemId = "DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolQualifiedItemId = "(T)DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolModDataKey = "DIGUS.ANIMALHUSBANDRYMOD/MeatCleaver";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_AnimalHusbandryMeatTool";
    private const string HasMeatToolFlag = "ThaleTheGreat.WalletToolsForAnimalHusbandry/HasMeatTool";
    private const string RuntimeToolMarker = "ThaleTheGreat.WalletToolsForAnimalHusbandry/RuntimeTool";
    private const string OvernightExposureMarker = "ThaleTheGreat.WalletToolsForAnimalHusbandry/OvernightExposure";
    private const string ToolTexturePath = "Mods/DIGUS.ANIMALHUSBANDRYMOD/Tools";

    private ModConfig Config = new();
    private Harmony Harmony = null!;
    private bool GmcmRegistered;
    private bool GmcmMissingLogged;
    private MeatToolState? StoredTool;
    private TemporaryToolUse? PendingToolUse;
    private SButton? PendingManualUseHotkey;
    private bool SuppressInventoryConversion;
    private bool PendingInventoryConversion;

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
        Harmony.PatchAll(Assembly.GetExecutingAssembly());
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
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
            MeatToolState state = StoredTool ?? MeatToolState.CreatePlaceholder(Monitor);
            MeatToolIconData icon = state.GetIconData(Monitor);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = state.GetDisplayName(Monitor),
                Description = state.GetDescription(Monitor),
                TexturePath = icon.TexturePath,
                TexturePosition = icon.TexturePosition,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {HasMeatToolFlag} true"
            };
        });
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
        {
            if (!GmcmMissingLogged)
            {
                Monitor.Log("Generic Mod Config Menu was not detected. Wallet Tools for Animal Husbandry settings remain available through config.json.", LogLevel.Warn);
                GmcmMissingLogged = true;
            }
            return;
        }

        try
        {
            gmcm.Unregister(ModManifest);
            gmcm.Register(ModManifest, ResetConfig, SaveConfig);
            AddBool(gmcm, nameof(Config.ModEnabled), () => Config.ModEnabled, SetModEnabled, "Mod Enabled", "Enables Wallet Tools for Animal Husbandry.");
            AddBool(gmcm, nameof(Config.MeatToolEnabled), () => Config.MeatToolEnabled, SetMeatToolEnabled, "Wallet Meat Cleaver / Meat Wand", "Store Animal Husbandry's Meat Cleaver or Meat Wand in the wallet.");
            AddBool(gmcm, nameof(Config.AutoUseEnabled), () => Config.AutoUseEnabled, value => SetAutoUseMasterEnabled(value, false), "Auto Use Enabled", "Master toggle for automatic Meat Cleaver / Meat Wand wallet logic. Manual hotkeys still work.");
            AddBool(gmcm, nameof(Config.MeatToolAutoUseEnabled), () => Config.MeatToolAutoUseEnabled, value => Config.MeatToolAutoUseEnabled = value, "Auto Meat Tool", "Automatically supplies the stored Meat Cleaver or Meat Wand when using it on a nearby farm animal.");
            AddBool(gmcm, nameof(Config.RequireLeftShiftForAutoUse), () => Config.RequireLeftShiftForAutoUse, value => Config.RequireLeftShiftForAutoUse = value, "Require Left Shift + Click", "Require Left Shift to be held while clicking an animal before the wallet Meat Cleaver or Meat Wand is supplied automatically.");
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
        Helper.WriteConfig(Config);
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

    private void SetMeatToolEnabled(bool enabled)
    {
        if (Config.MeatToolEnabled == enabled)
            return;

        Config.MeatToolEnabled = enabled;
        if (!Context.IsWorldReady)
            return;

        if (enabled && Config.ModEnabled)
        {
            CollectOvernightExposedTool(Game1.player);
            ReconcileLoadedToolState(Game1.player);
            ConvertInventoryTools(Game1.player);
        }
        else
        {
            if (PendingToolUse is not null)
                RestoreTemporaryTool(Game1.player);

            if (MaterializeStoredToolToInventory(Game1.player, true))
                Game1.addHUDMessage(new HUDMessage("Animal Husbandry tool returned to inventory.", HUDMessage.newQuest_type));

            RemoveRuntimeToolCopies(Game1.player);
            ClearWalletMarkersFromInventory(Game1.player);
            UpdateWalletFlag(Game1.player);
            InvalidatePowers();
        }
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

        Helper.WriteConfig(Config);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ClearVolatileState(false);

        MeatToolState? legacyState = Helper.Data.ReadSaveData<MeatToolState>(LegacySaveDataKey);
        if (legacyState is not null)
            StoredTool = legacyState;

        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);

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
        StoredTool = null;
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
        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(Game1.player);

        RemoveRuntimeToolCopies(Game1.player);
        NormalizePlayerItemsAfterCleanup(Game1.player);
        ExposeStoredToolForOvernight(Game1.player);
        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
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
        if (!IsMeatToolEnabled() || StoredTool is null)
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
        return Config.ModEnabled && Config.MeatToolEnabled;
    }

    private bool IsAutoUseEnabled()
    {
        return IsMeatToolEnabled() && Config.AutoUseEnabled && Config.MeatToolAutoUseEnabled;
    }


    private bool TryPrepareWalletToolUse(Farmer player)
    {
        if (PendingToolUse is not null || !IsAutoUseEnabled() || Game1.fadeToBlack || !Context.CanPlayerMove || Game1.activeClickableMenu is not null)
            return false;

        if (IsMeatTool(player.CurrentItem))
            return false;

        if (Config.RequireLeftShiftForAutoUse && !Helper.Input.IsDown(SButton.LeftShift))
            return false;

        if (!IsAnimalInToolReach(player))
            return false;

        return TrySetTemporaryWalletTool(player);
    }

    private bool IsAutoUseTrigger(SButton button)
    {
        if (!Config.RequireLeftShiftForAutoUse)
            return button is SButton.MouseLeft or SButton.ControllerA;

        return button == SButton.MouseLeft && Helper.Input.IsDown(SButton.LeftShift);
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
                if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || !IsMeatTool(tool))
                    continue;

                StoredTool = MeatToolState.FromTool(tool);
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
        RefreshWalletStateAfterStoredToolChange();
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
            RemoveRuntimeToolCopies(player);

            if (!Config.MeatToolEnabled)
            {
                changed = MaterializeStoredToolToInventory(player, false) || changed;
            }
            else if (StoredTool is not null)
            {
                if (RemoveInventoryCopiesOfMeatTool(player))
                    changed = true;
                if (CollectLostFoundMeatTool())
                    changed = true;
            }
            else
            {
                if (TryTakeInventoryMeatTool(player, out Tool? inventoryTool))
                {
                    StoredTool = MeatToolState.FromTool(inventoryTool);
                    changed = true;
                }

                if (CollectLostFoundMeatTool())
                    changed = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
            RefreshWalletStateAfterStoredToolChange();
        else
        {
            UpdateWalletFlag(player);
            InvalidatePowers();
        }
    }

    private bool MaterializeStoredToolToInventory(Farmer player, bool useOverflowMenu)
    {
        if (StoredTool is null)
            return false;

        Tool? tool = StoredTool.CreateTool(Monitor);
        StoredTool = null;

        if (tool is null || MeatToolState.IsErrorTool(tool))
        {
            RefreshWalletStateAfterStoredToolChange();
            return false;
        }

        SuppressInventoryConversion = true;
        try
        {
            ClearWalletMarkers(tool);
            if (useOverflowMenu)
                AddToolToInventoryOrOverflowMenu(player, tool);
            else
                AddToolToInventoryOrAppend(player, tool);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        RefreshWalletStateAfterStoredToolChange();
        return true;
    }

    private void DisableWalletBehavior(Farmer player, bool showMessage)
    {
        if (PendingToolUse is not null)
            RestoreTemporaryTool(player);

        bool returnedTool = MaterializeStoredToolToInventory(player, true);
        RemoveRuntimeToolCopies(player);
        ClearWalletMarkersFromInventory(player);
        StoredTool = null;
        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
        UpdateWalletFlag(player);
        InvalidatePowers();

        if (showMessage && returnedTool)
            Game1.addHUDMessage(new HUDMessage("Animal Husbandry tool returned to inventory.", HUDMessage.newQuest_type));
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

        StoredTool = null;
        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
        if (Context.IsWorldReady)
            UpdateWalletFlag(Game1.player);
        InvalidatePowers();
    }

    private void ExposeStoredToolForOvernight(Farmer player)
    {
        if (!IsMeatToolEnabled() || StoredTool is null || HasOvernightExposure(player))
            return;

        Tool? tool = StoredTool.CreateTool(Monitor);
        if (tool is null || MeatToolState.IsErrorTool(tool))
            return;

        SuppressInventoryConversion = true;
        try
        {
            MarkOvernightExposure(tool);
            AddToolToInventoryOrAppend(player, tool);
            StoredTool = null;
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        UpdateWalletFlag(player);
        InvalidatePowers();
    }

    private void CollectOvernightExposedTool(Farmer player)
    {
        if (!Config.ModEnabled)
            return;

        bool changed = false;
        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                if (player.Items[i] is not Tool tool || !IsOvernightExposure(tool))
                    continue;

                ClearWalletMarkers(tool);
                if (Config.MeatToolEnabled && IsMeatTool(tool))
                {
                    StoredTool = MeatToolState.FromTool(tool);
                    player.Items[i] = null;
                }

                changed = true;
            }

            if (changed)
                NormalizePlayerItemsAfterCleanup(player);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (changed)
            RefreshWalletStateAfterStoredToolChange();
    }

    private bool TrySetTemporaryWalletTool(Farmer player)
    {
        if (PendingToolUse is not null || !IsMeatToolEnabled() || StoredTool is null)
            return false;

        Tool? walletTool = StoredTool.CreateTool(Monitor);
        if (walletTool is null)
            return false;

        int previousToolIndex = player.CurrentToolIndex;
        int maxItems = GetPlayerMaxItemCount(player);
        if (previousToolIndex < 0 || previousToolIndex >= maxItems)
            return false;

        SuppressInventoryConversion = true;
        try
        {
            while (player.Items.Count <= previousToolIndex && player.Items.Count < maxItems)
                player.Items.Add(null);

            if (previousToolIndex >= player.Items.Count)
                return false;

            Item? previousItem = player.Items[previousToolIndex];
            UpdateToolEnchantmentsForSelectedSlot(player, previousItem as Tool, walletTool);
            MarkRuntimeTool(walletTool);
            player.Items[previousToolIndex] = walletTool;
            player.CurrentToolIndex = previousToolIndex;
            PendingToolUse = new TemporaryToolUse(previousToolIndex, walletTool, previousItem);
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
            Tool? usedTool = FindTemporaryTool(player, pending);
            if (usedTool is not null)
            {
                ClearWalletMarkers(usedTool);
                StoredTool = MeatToolState.FromTool(usedTool);
                RefreshWalletStateAfterStoredToolChange(false);
            }
            else
                RemoveRuntimeToolCopies(player);

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

    private static void RestoreTemporaryToolSlot(Farmer player, TemporaryToolUse pending)
    {
        if (pending.Slot < 0 || pending.Slot >= player.Items.Count)
            return;

        if (ReferenceEquals(player.Items[pending.Slot], pending.TemporaryTool) || player.Items[pending.Slot] is null)
        {
            player.Items[pending.Slot] = pending.PreviousItem;
            return;
        }

        if (pending.PreviousItem is not null)
            player.addItemByMenuIfNecessary(pending.PreviousItem);
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
            if (player.Items[i] is Tool candidate && IsMeatTool(candidate) && !IsRuntimeTool(candidate) && !IsOvernightExposure(candidate))
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
            if (player.Items[i] is not Tool tool || IsRuntimeTool(tool) || IsOvernightExposure(tool) || !IsMeatTool(tool))
                continue;

            ClearWalletMarkers(tool);
            StoredTool = MeatToolState.FromTool(tool);
            player.Items[i] = null;
            removed = true;
        }

        if (removed)
            NormalizePlayerItemsAfterCleanup(player);

        return removed;
    }

    private bool CollectLostFoundMeatTool()
    {
        bool changed = false;
        for (int i = 0; i < 16; i++)
        {
            if (!TryTakeLostFoundMeatTool(out Tool? recoveredTool))
                break;

            StoredTool = MeatToolState.FromTool(recoveredTool);
            changed = true;
        }

        return changed;
    }

    private void ScanLostFoundForMeatTool()
    {
        if (!IsMeatToolEnabled() || !Context.IsWorldReady)
            return;

        if (CollectLostFoundMeatTool())
            RefreshWalletStateAfterStoredToolChange();
    }

    private static bool TryTakeLostFoundMeatTool(out Tool tool)
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
            if (TryTakeMeatToolFromValue(value, out tool))
                return true;
        }

        return false;
    }

    private static bool TryTakeMeatToolFromValue(object? value, out Tool tool)
    {
        tool = null!;
        if (value is null || value is string)
            return false;

        if (TryTakeMeatToolFromWrappedValue(value, out tool))
            return true;

        object? unwrapped = TryGetNetValue(value);
        if (!ReferenceEquals(unwrapped, value) && unwrapped is not Tool && TryTakeMeatToolFromValue(unwrapped, out tool))
            return true;

        if (value is Tool directTool && IsMeatTool(directTool))
        {
            ClearWalletMarkers(directTool);
            tool = directTool;
            return true;
        }

        if (value is IList list)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] is Tool candidate && IsMeatTool(candidate))
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
                if (dictionaryValue is Tool candidate && IsMeatTool(candidate))
                {
                    ClearWalletMarkers(candidate);
                    dictionary.Remove(key);
                    tool = candidate;
                    return true;
                }

                if (TryTakeMeatToolFromValue(dictionaryValue, out tool))
                    return true;
            }
        }

        return false;
    }

    private static bool TryTakeMeatToolFromWrappedValue(object value, out Tool tool)
    {
        tool = null!;
        try
        {
            PropertyInfo? property = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is null || property.GetIndexParameters().Length != 0 || !property.CanRead || !property.CanWrite)
                return false;

            object? inner = property.GetValue(value);
            if (inner is not Tool candidate || !IsMeatTool(candidate))
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

    private static void MarkRuntimeTool(Tool tool)
    {
        EnsureMeatToolInstanceId(tool);
        tool.modData[RuntimeToolMarker] = "true";
    }

    private static bool IsRuntimeTool(Item? item)
    {
        return item is Tool tool && tool.modData.ContainsKey(RuntimeToolMarker);
    }

    private static void MarkOvernightExposure(Tool tool)
    {
        EnsureMeatToolInstanceId(tool);
        tool.modData[OvernightExposureMarker] = "true";
    }

    private static bool IsOvernightExposure(Item? item)
    {
        return item is Tool tool && tool.modData.ContainsKey(OvernightExposureMarker);
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
    }

    private static void RemoveRuntimeToolCopies(Farmer player)
    {
        bool removed = false;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (IsRuntimeTool(player.Items[i]))
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
            if (item is Tool tool)
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

    private void RefreshWalletStateAfterStoredToolChange(bool invalidatePowers = true)
    {
        Helper.Data.WriteSaveData<MeatToolState>(LegacySaveDataKey, null);
        UpdateWalletFlag(Game1.player);
        if (invalidatePowers)
            InvalidatePowers();
    }

    private void UpdateWalletFlag(Farmer player)
    {
        if (IsMeatToolEnabled() && StoredTool is not null)
            player.modData[HasMeatToolFlag] = "true";
        else
            player.modData.Remove(HasMeatToolFlag);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }


    [HarmonyPatch(typeof(Game1), nameof(Game1.pressUseToolButton))]
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
            return tool is not null && !string.IsNullOrWhiteSpace(tool.DisplayName) ? tool.DisplayName : "Meat Cleaver";
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
                : GetTexturePositionFromMenuIndex(ToolTexturePath, 33, monitor);

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
                catch (Exception ex)
                {
                    monitor.Log($"Could not create Animal Husbandry meat tool from '{id}': {ex.Message}", LogLevel.Trace);
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
            catch (Exception ex)
            {
                monitor.Log($"Could not read tool texture width for '{texturePath}': {ex.Message}", LogLevel.Trace);
            }

            return texturePath.Equals(ToolTexturePath, StringComparison.OrdinalIgnoreCase) ? 336 : 336;
        }
    }
}
