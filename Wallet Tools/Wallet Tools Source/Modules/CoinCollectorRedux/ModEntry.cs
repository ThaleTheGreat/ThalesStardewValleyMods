using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Powers;
using Object = StardewValley.Object;

using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletToolsForCoinCollectorRedux;

internal sealed class CoinCollectorReduxModule : WalletModule
{
    internal const string ModuleKey = "CoinCollectorRedux";
    internal const string LegacyUniqueId = "ThaleTheGreat.WalletToolsForCoinCollectorRedux";

    internal CoinCollectorReduxModule(ThaleTheGreat.WalletTools.ModEntry host)
        : base(host, ModuleKey, "Coin Collector Redux", LegacyUniqueId, "ThaleTheGreat.CoinCollectorRedux")
    {
    }
    private const string DetectorId = "ThaleTheGreat.CoinCollectorRedux_MetalDetector";
    private const string DetectorQualifiedId = "(O)ThaleTheGreat.CoinCollectorRedux_MetalDetector";
    private const string DetectorWeaponId = "ThaleTheGreat.CoinCollectorRedux_MetalDetectorWeapon";
    private const string DetectorWeaponQualifiedId = "(W)ThaleTheGreat.CoinCollectorRedux_MetalDetectorWeapon";
    private const string DetectorModDataKey = "ThaleTheGreat.CoinCollectorRedux/MetalDetectorItem";
    private const string DetectorTexturePath = "ThaleTheGreat.CoinCollectorRedux/MetalDetector";
    private const string HasDetectorFlag = "ThaleTheGreat.WalletToolsForCoinCollectorRedux/HasMetalDetector";
    private const string StoredDetectorStateKey = "ThaleTheGreat.WalletToolsForCoinCollectorRedux/MetalDetectorState";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_CoinCollectorReduxMetalDetector";

    private static CoinCollectorReduxModule? Instance;

    private ModConfig Config = new();
    private Harmony Harmony = null!;
    private bool GmcmRegistered;
    private bool SuppressInventoryConversion;
    private bool CoinCollectorPatchAttempted;
    private bool CoinCollectorPatchApplied;
    private MethodInfo? CoinCollectorDoBlipMethod;
    private Type? CoinCollectorEntryType;

    internal override void Initialize()
    {
        IModHelper helper = Helper;
        Instance = this;
        Config = Host.ReadModuleConfig<ModConfig>(ModuleKey);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;

        Harmony = new Harmony(LegacyUniqueId);
        TryPatchCoinCollectorRedux();
    }

    internal override void OnGameLaunched()
    {
        RegisterGmcm();
        TryPatchCoinCollectorRedux();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers") || !Context.IsWorldReady || !Config.ModEnabled || !HasStoredDetector(Game1.player))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            StoredDetectorState state = GetStoredDetector(Game1.player) ?? StoredDetectorState.CreatePlaceholder(Monitor);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = state.GetDisplayName(Monitor),
                Description = state.GetDescription(Monitor),
                TexturePath = DetectorTexturePath,
                TexturePosition = Point.Zero,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {HasDetectorFlag} true",
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
            AddBool(gmcm, nameof(Config.ModEnabled), () => Config.ModEnabled, SetModEnabled, "Mod Enabled", "Enables Wallet Tools for Coin Collector Redux.");
            AddBool(gmcm, nameof(Config.EnablePassiveWalletDetection), () => Config.EnablePassiveWalletDetection, value => Config.EnablePassiveWalletDetection = value, "Enable Passive Wallet Detection", "Lets Coin Collector Redux's passive polling use the Metal Detector stored in the wallet. This does not affect Coin Collector Redux's swing-only mode.");
            AddBool(gmcm, nameof(Config.EnableManualUseHotkey), () => Config.EnableManualUseHotkey, value => Config.EnableManualUseHotkey = value, "Enable Manual Use Hotkey", "Allows the configured hotkey to ping the wallet Metal Detector.");
            AddBool(gmcm, nameof(Config.PlayToolSwapSound), () => Config.PlayToolSwapSound, value => Config.PlayToolSwapSound = value, "Play Tool Swap Sound", "Play the Wallet Tools swap sound when the wallet Metal Detector is used manually.");
            AddBool(gmcm, nameof(Config.ShowHudMessageWhenStored), () => Config.ShowHudMessageWhenStored, value => Config.ShowHudMessageWhenStored = value, "Show Stored Message", "Show a HUD message when the Metal Detector is moved to the wallet.");
            AddKeybind(gmcm, nameof(Config.UseMetalDetectorHotkey), () => Config.UseMetalDetectorHotkey, value => Config.UseMetalDetectorHotkey = value, "Use Metal Detector", "Ping the Metal Detector stored in the wallet.");
            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools for Coin Collector Redux options with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
    }

    private void AddBool(IGenericModConfigMenuApi gmcm, string fieldId, Func<bool> getValue, Action<bool> setValue, string name, string tooltip)
    {
        gmcm.AddBoolOption(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void AddKeybind(IGenericModConfigMenuApi gmcm, string fieldId, Func<KeybindList> getValue, Action<KeybindList> setValue, string name, string tooltip)
    {
        gmcm.AddKeybindList(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
        if (Context.IsWorldReady)
        {
            ConvertInventoryDetector(Game1.player);
            UpdateWalletFlag(Game1.player);
            InvalidatePowers();
        }
    }

    private void SaveConfig()
    {
        Host.WriteModuleConfig(ModuleKey, Config);
        if (!Context.IsWorldReady)
            return;

        if (!Config.ModEnabled)
            MaterializeStoredDetectorToInventory(Game1.player, true);
        else
            ConvertInventoryDetector(Game1.player);

        UpdateWalletFlag(Game1.player);
        InvalidatePowers();
    }

    private void SetModEnabled(bool enabled)
    {
        if (Config.ModEnabled == enabled)
            return;

        Config.ModEnabled = enabled;
        if (!Context.IsWorldReady)
            return;

        if (!enabled)
            MaterializeStoredDetectorToInventory(Game1.player, true);
        else
            ConvertInventoryDetector(Game1.player);

        UpdateWalletFlag(Game1.player);
        InvalidatePowers();
        Host.WriteModuleConfig(ModuleKey, Config);
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        UpdateWalletFlag(Game1.player);

        if (!Config.ModEnabled)
        {
            MaterializeStoredDetectorToInventory(Game1.player, true);
            return;
        }

        ConvertInventoryDetector(Game1.player);
        UpdateWalletFlag(Game1.player);
        InvalidatePowers();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        SuppressInventoryConversion = false;
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Config.ModEnabled)
            return;

        ConvertInventoryDetector(Game1.player);
        UpdateWalletFlag(Game1.player);
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Config.ModEnabled)
            MaterializeStoredDetectorToInventory(Game1.player, false);
        else
            UpdateWalletFlag(Game1.player);
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (SuppressInventoryConversion || !Context.IsWorldReady || !Config.ModEnabled || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            return;

        ConvertInventoryDetector(e.Player);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.ModEnabled || !Config.EnableManualUseHotkey || Game1.activeClickableMenu is not null || !Context.IsPlayerFree)
            return;

        if (!Config.UseMetalDetectorHotkey.JustPressed())
            return;

        Helper.Input.Suppress(e.Button);
        TryUseWalletDetector(Game1.player);
    }

    private void ConvertInventoryDetector(Farmer player)
    {
        if (HasStoredDetector(player))
            return;

        SuppressInventoryConversion = true;
        try
        {
            for (int i = 0; i < player.Items.Count; i++)
            {
                Item? detector = player.Items[i];
                if (!IsMetalDetectorItem(detector))
                    continue;

                SetStoredDetector(player, StoredDetectorState.FromItem(detector!));
                RemoveOneFromSlot(player, i, detector!);
                UpdateWalletFlag(player);
                InvalidatePowers();

                if (Config.ShowHudMessageWhenStored)
                    Game1.addHUDMessage(new HUDMessage("Metal Detector moved to wallet.", HUDMessage.newQuest_type));

                return;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }
    }

    private bool MaterializeStoredDetectorToInventory(Farmer player, bool useOverflowMenu)
    {
        StoredDetectorState? state = GetStoredDetector(player);
        if (state is null)
            return false;

        Item? detector = state.CreateItem(Monitor);
        if (detector is null)
            return false;

        SetStoredDetector(player, null);
        AddDetectorToInventory(player, detector, useOverflowMenu);
        UpdateWalletFlag(player);
        InvalidatePowers();
        return true;
    }

    private void TryUseWalletDetector(Farmer player)
    {
        if (!HasStoredDetector(player) || !CoinCollectorIsEnabled())
            return;

        if (Config.PlayToolSwapSound)
            Game1.playSound("toolSwap");

        TryInvokeCoinCollectorBlip();
    }

    private static void RemoveOneFromSlot(Farmer player, int slot, Item item)
    {
        if (item.Stack > 1)
        {
            item.Stack--;
            return;
        }

        player.Items[slot] = null;
        NormalizePlayerItemsAfterCleanup(player);
    }

    private static void AddDetectorToInventory(Farmer player, Item detector, bool useOverflowMenu)
    {
        int maxItems = GetPlayerMaxItemCount(player);
        int searchCount = Math.Min(player.Items.Count, maxItems);
        for (int i = 0; i < searchCount; i++)
        {
            if (player.Items[i] is null)
            {
                player.Items[i] = detector;
                NormalizePlayerItemsAfterCleanup(player);
                return;
            }
        }

        if (player.Items.Count < maxItems)
        {
            player.Items.Add(detector);
            NormalizePlayerItemsAfterCleanup(player);
            return;
        }

        if (useOverflowMenu)
            player.addItemByMenuIfNecessary(detector);
        else
            player.Items.Add(detector);

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

    private bool HasStoredDetector(Farmer player)
    {
        return GetStoredDetector(player) is not null;
    }

    private StoredDetectorState? GetStoredDetector(Farmer player)
    {
        if (!player.modData.TryGetValue(StoredDetectorStateKey, out string? raw) || string.IsNullOrWhiteSpace(raw))
            return null;

        try
        {
            StoredDetectorState? state = JsonConvert.DeserializeObject<StoredDetectorState>(raw);
            if (state is null)
                return null;

            state.ModData ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(state.QualifiedItemId)
                || state.QualifiedItemId.Equals(DetectorQualifiedId, StringComparison.OrdinalIgnoreCase)
                || state.QualifiedItemId.Equals(DetectorId, StringComparison.OrdinalIgnoreCase))
            {
                state.QualifiedItemId = DetectorWeaponQualifiedId;
                state.ModData[DetectorModDataKey] = "true";
                SetStoredDetector(player, state);
            }

            return state;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed reading wallet Metal Detector state: {ex}", LogLevel.Error);
            player.modData.Remove(StoredDetectorStateKey);
            return null;
        }
    }

    private void SetStoredDetector(Farmer player, StoredDetectorState? state)
    {
        if (state is null)
        {
            player.modData.Remove(StoredDetectorStateKey);
            player.modData.Remove(HasDetectorFlag);
            return;
        }

        player.modData[StoredDetectorStateKey] = JsonConvert.SerializeObject(state);
        player.modData[HasDetectorFlag] = "true";
    }

    private void UpdateWalletFlag(Farmer player)
    {
        if (Config.ModEnabled && HasStoredDetector(player))
            player.modData[HasDetectorFlag] = "true";
        else
            player.modData.Remove(HasDetectorFlag);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private static bool IsMetalDetectorItem(Item? item)
    {
        if (item is null)
            return false;

        if (item.modData.ContainsKey(DetectorModDataKey))
            return true;

        if (ItemRegistry.HasItemId(item, DetectorWeaponQualifiedId)
            || ItemRegistry.HasItemId(item, DetectorWeaponId)
            || ItemRegistry.HasItemId(item, DetectorQualifiedId)
            || ItemRegistry.HasItemId(item, DetectorId))
        {
            return true;
        }

        return item.ItemId.Equals(DetectorWeaponId, StringComparison.OrdinalIgnoreCase)
            || item.QualifiedItemId.Equals(DetectorWeaponQualifiedId, StringComparison.OrdinalIgnoreCase)
            || item.ItemId.Equals(DetectorId, StringComparison.OrdinalIgnoreCase)
            || item.QualifiedItemId.Equals(DetectorQualifiedId, StringComparison.OrdinalIgnoreCase)
            || item.Name.Equals("MetalDetector", StringComparison.OrdinalIgnoreCase)
            || item.DisplayName.Equals("Metal Detector", StringComparison.OrdinalIgnoreCase);
    }

    private void TryPatchCoinCollectorRedux()
    {
        if (CoinCollectorPatchAttempted && CoinCollectorPatchApplied)
            return;

        CoinCollectorPatchAttempted = true;
        try
        {
            CoinCollectorEntryType = AccessTools.TypeByName("ThaleTheGreat.CoinCollectorRedux.ModEntry");
            if (CoinCollectorEntryType is null)
            {
                Monitor.Log("Could not find Coin Collector Redux's ModEntry type. Wallet detector passive support will be unavailable.", LogLevel.Warn);
                return;
            }

            CoinCollectorDoBlipMethod = AccessTools.Method(CoinCollectorEntryType, "DoBlip");
            MethodInfo? passivePoll = AccessTools.Method(CoinCollectorEntryType, "OnOneSecondUpdateTicked");
            MethodInfo? prefix = AccessTools.Method(typeof(CoinCollectorPassivePollPatch), nameof(CoinCollectorPassivePollPatch.Prefix));
            MethodInfo? postfix = AccessTools.Method(typeof(CoinCollectorPassivePollPatch), nameof(CoinCollectorPassivePollPatch.Postfix));
            MethodInfo? finalizer = AccessTools.Method(typeof(CoinCollectorPassivePollPatch), nameof(CoinCollectorPassivePollPatch.Finalizer));

            if (passivePoll is null || prefix is null || postfix is null || finalizer is null)
            {
                Monitor.Log("Could not find Coin Collector Redux's passive detector polling path. Wallet detector passive support will be unavailable.", LogLevel.Warn);
                return;
            }

            Harmony.Patch(
                passivePoll,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix),
                finalizer: new HarmonyMethod(finalizer)
            );
            CoinCollectorPatchApplied = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed patching Coin Collector Redux passive detector polling: {ex}", LogLevel.Error);
        }
    }

    private bool TryInvokeCoinCollectorBlip()
    {
        TryPatchCoinCollectorRedux();
        if (CoinCollectorDoBlipMethod is null)
        {
            Monitor.Log("Could not find Coin Collector Redux's detector ping method.", LogLevel.Warn);
            return false;
        }

        try
        {
            CoinCollectorDoBlipMethod.Invoke(null, null);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            Monitor.Log($"Coin Collector Redux detector ping failed: {ex.InnerException ?? ex}", LogLevel.Error);
            return false;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Coin Collector Redux detector ping failed: {ex}", LogLevel.Error);
            return false;
        }
    }

    private bool CoinCollectorIsEnabled()
    {
        object? config = GetCoinCollectorConfig();
        if (config is null)
            return true;

        return GetBoolProperty(config, "ModEnabled", true);
    }

    private bool CoinCollectorRequiresSwing()
    {
        object? config = GetCoinCollectorConfig();
        if (config is null)
            return true;

        return GetBoolProperty(config, "RequireMetalDetectorSwing", true);
    }

    private object? GetCoinCollectorConfig()
    {
        TryPatchCoinCollectorRedux();
        if (CoinCollectorEntryType is null)
            return null;

        try
        {
            FieldInfo? field = AccessTools.Field(CoinCollectorEntryType, "Config");
            return field?.GetValue(null);
        }
        catch
        {
            return null;
        }
    }

    private static bool GetBoolProperty(object instance, string propertyName, bool fallback)
    {
        try
        {
            PropertyInfo? property = instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(instance) is bool value)
                return value;

            FieldInfo? field = instance.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(instance) is bool fieldValue)
                return fieldValue;
        }
        catch
        {
        }

        return fallback;
    }

    private PassiveDetectorRequirementOverride? BeginPassiveDetectorRequirementOverride()
    {
        if (!Context.IsWorldReady
            || !Config.ModEnabled
            || !Config.EnablePassiveWalletDetection
            || !HasStoredDetector(Game1.player)
            || CoinCollectorRequiresSwing())
        {
            return null;
        }

        object? config = GetCoinCollectorConfig();
        if (config is null || !GetBoolProperty(config, "RequireMetalDetector", true))
            return null;

        return TrySetBoolMember(config, "RequireMetalDetector", false)
            ? new PassiveDetectorRequirementOverride(config, true)
            : null;
    }

    private static void RestorePassiveDetectorRequirement(PassiveDetectorRequirementOverride? state)
    {
        if (state is not null)
            TrySetBoolMember(state.Config, "RequireMetalDetector", state.OriginalValue);
    }

    private static bool TrySetBoolMember(object instance, string memberName, bool value)
    {
        try
        {
            PropertyInfo? property = instance.GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.CanWrite == true && property.PropertyType == typeof(bool))
            {
                property.SetValue(instance, value);
                return true;
            }

            FieldInfo? field = instance.GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null && field.FieldType == typeof(bool))
            {
                field.SetValue(instance, value);
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private static class CoinCollectorPassivePollPatch
    {
        public static void Prefix(ref PassiveDetectorRequirementOverride? __state)
        {
            __state = Instance?.BeginPassiveDetectorRequirementOverride();
        }

        public static void Postfix(PassiveDetectorRequirementOverride? __state)
        {
            RestorePassiveDetectorRequirement(__state);
        }

        public static Exception? Finalizer(Exception? __exception, PassiveDetectorRequirementOverride? __state)
        {
            RestorePassiveDetectorRequirement(__state);
            return __exception;
        }
    }

    private sealed class PassiveDetectorRequirementOverride
    {
        public PassiveDetectorRequirementOverride(object config, bool originalValue)
        {
            Config = config;
            OriginalValue = originalValue;
        }

        public object Config { get; }
        public bool OriginalValue { get; }
    }

    private sealed class StoredDetectorState
    {
        public string QualifiedItemId { get; set; } = string.Empty;
        public int Stack { get; set; } = 1;
        public int Quality { get; set; }
        public Dictionary<string, string> ModData { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static StoredDetectorState CreatePlaceholder(IMonitor monitor)
        {
            Item? detector = CreateFreshItem(DetectorWeaponQualifiedId);
            return detector is null ? new StoredDetectorState { QualifiedItemId = DetectorWeaponQualifiedId } : FromItem(detector);
        }

        public static StoredDetectorState FromItem(Item detector)
        {
            StoredDetectorState state = new()
            {
                QualifiedItemId = detector.QualifiedItemId,
                Stack = Math.Max(1, detector.Stack),
                Quality = detector is Object obj ? Math.Max(0, obj.Quality) : 0
            };

            foreach (KeyValuePair<string, string> pair in detector.modData.Pairs)
                state.ModData[pair.Key] = pair.Value;

            return state;
        }

        public string GetDisplayName(IMonitor monitor)
        {
            Item? detector = CreateItem(monitor);
            return detector is not null && !string.IsNullOrWhiteSpace(detector.DisplayName) ? detector.DisplayName : "Metal Detector";
        }

        public string GetDescription(IMonitor monitor)
        {
            Item? detector = CreateItem(monitor);
            return detector?.getDescription() ?? "Detects buried coins.";
        }

        public Item? CreateItem(IMonitor monitor)
        {
            Item? detector = CreateFreshItem(QualifiedItemId);
            if (detector is null)
                return null;

            detector.Stack = Math.Max(1, Stack);
            if (detector is Object obj)
                obj.Quality = Math.Max(0, Quality);

            foreach (KeyValuePair<string, string> pair in ModData)
                detector.modData[pair.Key] = pair.Value;

            detector.modData[DetectorModDataKey] = "true";
            return detector;
        }

        private static Item? CreateFreshItem(string? preferredQualifiedItemId)
        {
            IEnumerable<string> ids = string.IsNullOrWhiteSpace(preferredQualifiedItemId)
                ? new[] { DetectorQualifiedId, DetectorWeaponQualifiedId, DetectorId, DetectorWeaponId }
                : new[] { preferredQualifiedItemId, DetectorWeaponQualifiedId, DetectorQualifiedId, DetectorWeaponId, DetectorId };

            foreach (string id in ids.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    Item detector = ItemRegistry.Create(id);
                    if (!IsErrorItem(detector) && IsMetalDetectorItem(detector))
                        return detector;
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsErrorItem(Item item)
        {
            string text = string.Join("\n", item.Name, item.DisplayName, item.ItemId, item.QualifiedItemId);
            return text.Contains("Error Item", StringComparison.OrdinalIgnoreCase) || text.Contains("ErrorItem", StringComparison.OrdinalIgnoreCase);
        }
    }
}
