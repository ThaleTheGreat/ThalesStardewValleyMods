using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Powers;
using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletToolsForFashionSense;

internal sealed class FashionSenseMirrorModule : WalletModule
{
    internal const string ModuleKey = "FashionSenseMirror";
    private const string FashionSenseUniqueId = "PeacefulEnd.FashionSense";
    private const string HandMirrorQualifiedItemId = "(T)PeacefulEnd.FashionSense_HandMirror";
    private const string HandMirrorModDataKey = "FashionSense.Tools.HandMirror";
    private const string HandMirrorTexturePath = "FashionSense/Textures/HandMirror";
    private const string WalletFlagKey = "ThaleTheGreat.WalletTools/HasFashionSenseMirror";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_FashionSenseMirror";

    private FashionSenseMirrorConfig Config = new();
    private bool SuppressInventoryConversion;

    internal FashionSenseMirrorModule(ModEntry host)
        : base(host, ModuleKey, "Fashion Sense Mirror", string.Empty, FashionSenseUniqueId)
    {
    }

    internal override void Initialize()
    {
        Config = Host.ReadModuleConfig<FashionSenseMirrorConfig>(ModuleKey);
        Helper.Events.Content.AssetRequested += OnAssetRequested;
        Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        Helper.Events.Player.InventoryChanged += OnInventoryChanged;
        Helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    internal override void OnGameLaunched()
    {
        IGenericModConfigMenuApi? gmcm = Host.GetDirectModuleGmcmApi(ModuleKey);
        if (gmcm is null)
            return;

        Host.RegisterModuleConfigCallbacks(ModuleKey, ResetConfig, SaveConfig);
        gmcm.AddPage(ModManifest, Host.GetModulePageId(ModuleKey), () => "Fashion Sense Mirror");
        gmcm.AddBoolOption(ModManifest, () => Config.Enabled, SetEnabled, () => "Enabled", () => "Stores the Fashion Sense Hand Mirror in the wallet and lets you use it with a hotkey.", $"{ModuleKey}.Enabled");
        gmcm.AddBoolOption(ModManifest, () => Config.AutoStoreFromInventory, SetAutoStoreFromInventory, () => "Auto Store From Inventory", () => "Moves a Fashion Sense Hand Mirror into the wallet when it enters your inventory.", $"{ModuleKey}.AutoStoreFromInventory");
        gmcm.AddKeybindList(ModManifest, () => Config.UseHandMirrorKey, value => Config.UseHandMirrorKey = value, () => "Use Hand Mirror", () => "Uses the real Fashion Sense Hand Mirror from the wallet.", $"{ModuleKey}.UseHandMirrorKey");
        gmcm.AddKeybindList(ModManifest, () => Config.ReturnToInventoryKey, value => Config.ReturnToInventoryKey = value, () => "Return To Inventory", () => "Returns the wallet Hand Mirror to your inventory.", $"{ModuleKey}.ReturnToInventoryKey");
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            if (!Context.IsWorldReady || !Config.Enabled || !HasHandMirror(Game1.player))
            {
                powers.Remove(WalletPowerId);
                return;
            }

            Item item = ItemRegistry.Create(HandMirrorQualifiedItemId);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = item.DisplayName,
                Description = item.getDescription(),
                TexturePath = HandMirrorTexturePath,
                TexturePosition = Point.Zero,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {WalletFlagKey} true",
                CustomFields = Host.GetWalletPowerCustomFields()
            };
        });
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (Config.Enabled && Config.AutoStoreFromInventory)
            ConvertInventoryMirrors(Game1.player);
        else if (!Config.Enabled)
            ReturnMirrorToInventory(Game1.player, showMessage: false);

        InvalidatePowers();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || SuppressInventoryConversion || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            return;

        if (Config.Enabled && Config.AutoStoreFromInventory)
            ConvertInventoryMirrors(e.Player);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || Game1.activeClickableMenu is not null)
            return;

        if (Config.ReturnToInventoryKey.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            ReturnMirrorToInventory(Game1.player, showMessage: true);
            return;
        }

        if (!Config.UseHandMirrorKey.JustPressed())
            return;

        Helper.Input.Suppress(e.Button);
        if (HasHandMirror(Game1.player))
            UseRealHandMirrorPath(Game1.player);
    }

    private void ConvertInventoryMirrors(Farmer player)
    {
        if (HasHandMirror(player))
            return;

        int index = -1;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (!IsHandMirror(player.Items[i]))
                continue;

            index = i;
            break;
        }

        if (index < 0)
            return;

        SuppressInventoryConversion = true;
        try
        {
            player.Items[index] = null;
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        player.modData[WalletFlagKey] = "true";
        InvalidatePowers();
        Game1.addHUDMessage(new HUDMessage("Hand Mirror added to wallet.", HUDMessage.newQuest_type));
    }

    private bool ReturnMirrorToInventory(Farmer player, bool showMessage)
    {
        if (!HasHandMirror(player))
            return false;

        Item item = ItemRegistry.Create(HandMirrorQualifiedItemId);
        SuppressInventoryConversion = true;
        try
        {
            if (!player.addItemToInventoryBool(item))
                Game1.createItemDebris(item, player.getStandingPosition(), player.FacingDirection, player.currentLocation);
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        player.modData.Remove(WalletFlagKey);
        InvalidatePowers();
        if (showMessage)
            Game1.addHUDMessage(new HUDMessage("Hand Mirror returned to inventory.", HUDMessage.newQuest_type));
        return true;
    }

    private bool UseRealHandMirrorPath(Farmer player)
    {
        try
        {
            Item item = ItemRegistry.Create(HandMirrorQualifiedItemId);
            if (item is not Tool tool)
            {
                Monitor.Log($"The Fashion Sense Hand Mirror resolved to an unexpected item type '{item.GetType().FullName}'.", LogLevel.Error);
                return false;
            }

            Vector2 target = !Game1.wasMouseVisibleThisFrame
                ? player.GetToolLocation(false)
                : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);
            target = player.GetToolLocation(target, false);
            return tool.beginUsing(player.currentLocation, (int)target.X, (int)target.Y, player);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to activate the Fashion Sense Hand Mirror from the wallet: {ex}", LogLevel.Error);
            return false;
        }
    }

    private void SetEnabled(bool value)
    {
        Config.Enabled = value;
        if (!Context.IsWorldReady)
            return;

        if (value && Config.AutoStoreFromInventory)
            ConvertInventoryMirrors(Game1.player);
        else if (!value)
            ReturnMirrorToInventory(Game1.player, showMessage: true);
    }

    private void SetAutoStoreFromInventory(bool value)
    {
        Config.AutoStoreFromInventory = value;
        if (value && Config.Enabled && Context.IsWorldReady)
            ConvertInventoryMirrors(Game1.player);
    }

    private void ResetConfig()
    {
        Config = new FashionSenseMirrorConfig();
        SaveConfig();
    }

    private void SaveConfig()
    {
        Host.WriteModuleConfig(ModuleKey, Config);
        InvalidatePowers();
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private static bool HasHandMirror(Farmer player)
    {
        return player.modData.TryGetValue(WalletFlagKey, out string? value)
            && bool.TryParse(value, out bool parsed)
            && parsed;
    }

    private static bool IsHandMirror(Item? item)
    {
        return string.Equals(item?.QualifiedItemId, HandMirrorQualifiedItemId, StringComparison.OrdinalIgnoreCase)
            || item?.modData.ContainsKey(HandMirrorModDataKey) == true;
    }
}

internal sealed class FashionSenseMirrorConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoStoreFromInventory { get; set; } = true;
    public KeybindList UseHandMirrorKey { get; set; } = KeybindList.Parse("LeftControl + F");
    public KeybindList ReturnToInventoryKey { get; set; } = KeybindList.Parse("LeftShift + MouseRight");
}
