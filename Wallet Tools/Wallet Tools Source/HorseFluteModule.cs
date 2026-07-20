using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Powers;
using StardewValley.ItemTypeDefinitions;

namespace ThaleTheGreat.WalletTools;

internal sealed class HorseFluteModule : WalletModule
{
    internal const string ModuleKey = "HorseFlute";
    private const string HorseFluteQualifiedItemId = "(O)911";
    private const string WalletFlagKey = "ThaleTheGreat.WalletTools/HasHorseFlute";
    private const string WalletPowerId = "ThaleTheGreat.WalletTools_HorseFlute";
    private const string PowerIconAssetPath = "Mods/ThaleTheGreat.WalletTools/HorseFlutePower";

    private HorseFluteConfig Config = new();
    private bool SuppressInventoryConversion;

    internal HorseFluteModule(ModEntry host)
        : base(host, ModuleKey, "Horse Flute", string.Empty)
    {
    }

    internal override string? GetConflictingStandaloneModId()
    {
        IModInfo? conflict = Helper.ModRegistry.GetAll().FirstOrDefault(mod =>
            string.Equals(mod.Manifest.Name, "Wallet Horse Flute", StringComparison.OrdinalIgnoreCase)
            && string.Equals(mod.Manifest.Author, "HeavyStarRuler", StringComparison.OrdinalIgnoreCase));

        return conflict?.Manifest.UniqueID;
    }

    internal override void Initialize()
    {
        Config = Host.ReadModuleConfig<HorseFluteConfig>(ModuleKey);
        Helper.Events.Content.AssetRequested += OnAssetRequested;
        Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        Helper.Events.Player.InventoryChanged += OnInventoryChanged;
        Helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    internal override void OnGameLaunched()
    {
        IGenericModConfigMenuApi? gmcm = Host.GetDirectModuleGmcmApi(ModuleKey);
        if (gmcm is not null)
        {
            Host.RegisterModuleConfigCallbacks(ModuleKey, ResetConfig, SaveConfig);
            gmcm.AddPage(ModManifest, Host.GetModulePageId(ModuleKey), () => "Horse Flute");
            gmcm.AddBoolOption(ModManifest, () => Config.Enabled, value => SetEnabled(value), () => "Enabled", () => "Stores the vanilla Horse Flute in the wallet and lets you use it with a hotkey.", $"{ModuleKey}.Enabled");
            gmcm.AddBoolOption(ModManifest, () => Config.AutoStoreFromInventory, SetAutoStoreFromInventory, () => "Auto Store From Inventory", () => "Moves a vanilla Horse Flute into the wallet when it enters your inventory.", $"{ModuleKey}.AutoStoreFromInventory");
            gmcm.AddKeybindList(ModManifest, () => Config.UseHorseFluteKey, value => Config.UseHorseFluteKey = value, () => "Use Horse Flute", () => "Uses the real vanilla Horse Flute from the wallet.", $"{ModuleKey}.UseHorseFluteKey");
        }
    }


    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(PowerIconAssetPath))
        {
            e.LoadFrom(CreatePowerIconTexture, AssetLoadPriority.Exclusive);
            return;
        }

        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            if (!Context.IsWorldReady || !Config.Enabled || !HasHorseFlute(Game1.player))
            {
                powers.Remove(WalletPowerId);
                return;
            }

            Item item = ItemRegistry.Create(HorseFluteQualifiedItemId);
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = item.DisplayName,
                Description = item.getDescription(),
                TexturePath = PowerIconAssetPath,
                TexturePosition = Point.Zero,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {WalletFlagKey} true",
                CustomFields = Host.GetWalletPowerCustomFields()
            };
        });
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        if (Config.Enabled && Config.AutoStoreFromInventory)
            ConvertInventoryFlutes(Game1.player);
        else if (!Config.Enabled)
            ReturnFluteToInventory(Game1.player, showMessage: false);

        InvalidatePowers();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || SuppressInventoryConversion || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            return;

        if (Config.Enabled && Config.AutoStoreFromInventory)
            ConvertInventoryFlutes(e.Player);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || Game1.activeClickableMenu is not null)
            return;

        if (!Config.UseHorseFluteKey.JustPressed())
            return;

        Helper.Input.Suppress(e.Button);
        if (!HasHorseFlute(Game1.player))
            return;

        UseRealHorseFlutePath();
    }

    private void ConvertInventoryFlutes(Farmer player)
    {
        if (HasHorseFlute(player))
            return;

        int index = -1;
        for (int i = 0; i < player.Items.Count; i++)
        {
            if (!IsHorseFlute(player.Items[i]))
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
        Game1.addHUDMessage(new HUDMessage("Horse Flute added to wallet.", HUDMessage.newQuest_type));
    }

    private bool ReturnFluteToInventory(Farmer player, bool showMessage)
    {
        if (!HasHorseFlute(player))
            return false;

        Item item = ItemRegistry.Create(HorseFluteQualifiedItemId);
        SuppressInventoryConversion = true;
        try
        {
            if (!player.addItemToInventoryBool(item))
            {
                Game1.createItemDebris(item, player.getStandingPosition(), player.FacingDirection, player.currentLocation);
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        player.modData.Remove(WalletFlagKey);
        InvalidatePowers();
        if (showMessage)
            Game1.addHUDMessage(new HUDMessage("Horse Flute returned to inventory.", HUDMessage.newQuest_type));
        return true;
    }

    private bool UseRealHorseFlutePath()
    {
        try
        {
            Item item = ItemRegistry.Create(HorseFluteQualifiedItemId);
            if (item is StardewValley.Object obj)
                return obj.performUseAction(Game1.currentLocation);

            Monitor.Log($"The vanilla Horse Flute resolved to an unexpected item type '{item.GetType().FullName}'.", LogLevel.Error);
            return false;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to activate the vanilla Horse Flute from the wallet: {ex}", LogLevel.Error);
            return false;
        }
    }

    private Texture2D CreatePowerIconTexture()
    {
        const int renderSize = 64;
        const int iconSize = 16;
        GraphicsDevice graphicsDevice = Game1.graphics.GraphicsDevice;
        RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();
        RenderTarget2D inventoryRender = new(graphicsDevice, renderSize, renderSize, false, SurfaceFormat.Color, DepthFormat.None);
        RenderTarget2D powerIcon = new(graphicsDevice, iconSize, iconSize, false, SurfaceFormat.Color, DepthFormat.None);

        try
        {
            Item item = ItemRegistry.Create(HorseFluteQualifiedItemId);
            graphicsDevice.SetRenderTarget(inventoryRender);
            graphicsDevice.Clear(Color.Transparent);
            using (SpriteBatch spriteBatch = new(graphicsDevice))
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                item.drawInMenu(spriteBatch, Vector2.Zero, 1f, 1f, 0.89f, StackDrawType.Hide, Color.White, drawShadow: true);
                spriteBatch.End();
            }

            graphicsDevice.SetRenderTarget(powerIcon);
            graphicsDevice.Clear(Color.Transparent);
            using (SpriteBatch spriteBatch = new(graphicsDevice))
            {
                spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                spriteBatch.Draw(inventoryRender, new Rectangle(0, 0, iconSize, iconSize), new Rectangle(0, 0, renderSize, renderSize), Color.White);
                spriteBatch.End();
            }

            return powerIcon;
        }
        finally
        {
            graphicsDevice.SetRenderTargets(previousTargets);
            inventoryRender.Dispose();
        }
    }

    private void SetEnabled(bool value)
    {
        Config.Enabled = value;
        if (!Context.IsWorldReady)
            return;

        if (value && Config.AutoStoreFromInventory)
            ConvertInventoryFlutes(Game1.player);
        else if (!value)
            ReturnFluteToInventory(Game1.player, showMessage: true);
    }

    private void SetAutoStoreFromInventory(bool value)
    {
        Config.AutoStoreFromInventory = value;
        if (value && Config.Enabled && Context.IsWorldReady)
            ConvertInventoryFlutes(Game1.player);
    }

    private void ResetConfig()
    {
        Config = new HorseFluteConfig();
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

    private static bool HasHorseFlute(Farmer player)
    {
        return player.modData.TryGetValue(WalletFlagKey, out string? value)
            && bool.TryParse(value, out bool parsed)
            && parsed;
    }

    private static bool IsHorseFlute(Item? item)
    {
        return string.Equals(item?.QualifiedItemId, HorseFluteQualifiedItemId, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed class HorseFluteConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoStoreFromInventory { get; set; } = true;
    public KeybindList UseHorseFluteKey { get; set; } = KeybindList.Parse("LeftControl + H");
}
