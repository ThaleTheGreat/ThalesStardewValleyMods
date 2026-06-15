using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Tools;

namespace ThaleTheGreat.InstantToolUpgradeRedux;

public sealed class ModEntry : Mod
{
    private ModConfig Config = new();
    private bool GmcmRegistered;
    private readonly PerScreen<bool> PendingVanillaReturnScreen = new(() => false);
    private readonly PerScreen<int> VanillaReturnDelayTicksScreen = new(() => 0);
    private readonly PerScreen<long> PendingVanillaReturnPlayerIdScreen = new(() => 0L);

    private bool PendingVanillaReturn
    {
        get => PendingVanillaReturnScreen.Value;
        set => PendingVanillaReturnScreen.Value = value;
    }

    private int VanillaReturnDelayTicks
    {
        get => VanillaReturnDelayTicksScreen.Value;
        set => VanillaReturnDelayTicksScreen.Value = value;
    }

    private long PendingVanillaReturnPlayerId
    {
        get => PendingVanillaReturnPlayerIdScreen.Value;
        set => PendingVanillaReturnPlayerIdScreen.Value = value;
    }

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        NormalizeConfig();

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Content.AssetRequested += OnAssetRequested;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        try
        {
            gmcm.Unregister(ModManifest);
            gmcm.Register(ModManifest, ResetConfig, SaveConfig);

            gmcm.AddSectionTitle(
                ModManifest,
                text: () => Helper.Translation.Get("gmcm.section.upgrades"),
                tooltip: () => Helper.Translation.Get("gmcm.section.upgrades.tooltip"));

            gmcm.AddNumberOption(
                ModManifest,
                getValue: () => Config.PriceMultiplier,
                setValue: value =>
                {
                    Config.PriceMultiplier = value;
                    NormalizeConfig();
                    InvalidateToolUpgradeData();
                },
                name: () => Helper.Translation.Get("gmcm.price-multiplier.name"),
                tooltip: () => Helper.Translation.Get("gmcm.price-multiplier.tooltip"),
                min: 0.10f,
                max: 5f,
                interval: 0.10f,
                formatValue: FormatPriceMultiplier,
                fieldId: nameof(ModConfig.PriceMultiplier));

            gmcm.AddNumberOption(
                ModManifest,
                getValue: () => Config.BarsRequired,
                setValue: value =>
                {
                    Config.BarsRequired = value;
                    NormalizeConfig();
                    InvalidateToolUpgradeData();
                },
                name: () => Helper.Translation.Get("gmcm.bars-required.name"),
                tooltip: () => Helper.Translation.Get("gmcm.bars-required.tooltip"),
                min: 0,
                max: 25,
                interval: 5,
                formatValue: FormatBarsRequired,
                fieldId: nameof(ModConfig.BarsRequired));

            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Instant Tool Upgrade Redux with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        ClearPendingVanillaReturn();
        MakeLocalToolUpgradeReady(Game1.player, queueVanillaReturn: false);
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        ClearPendingVanillaReturn();
        MakeLocalToolUpgradeReady(Game1.player, queueVanillaReturn: false);
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        if (e.IsMultipleOf(15))
            MakeLocalToolUpgradeReady(Game1.player, queueVanillaReturn: true);

        TryTriggerPendingVanillaReturn();
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.IsLocalPlayer)
            return;

        MakeLocalToolUpgradeReady(e.Player, queueVanillaReturn: true);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Strings/StringsFromCSFiles"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data["ShopMenu.cs.11474"] = Helper.Translation.Get("crafting-window");
                data["Tool.cs.14317"] = Helper.Translation.Get("post-purchase-dialogue");
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
        {
            e.Edit(asset => EditToolUpgradeData(asset.AsDictionary<string, ToolData>().Data), AssetEditPriority.Late);
        }
    }

    private void EditToolUpgradeData(IDictionary<string, ToolData> tools)
    {
        foreach (ToolData tool in tools.Values)
        {
            ApplyConventionalUpgradeOverride(tool);

            if (tool.UpgradeFrom is null)
                continue;

            foreach (ToolUpgradeData upgrade in tool.UpgradeFrom)
                ApplyConfiguredUpgradeCost(upgrade);
        }
    }

    private void ApplyConventionalUpgradeOverride(ToolData tool)
    {
        if (string.IsNullOrWhiteSpace(tool.ConventionalUpgradeFrom))
            return;

        int basePrice = GetConventionalUpgradePrice(tool.UpgradeLevel);
        string? tradeItemId = GetConventionalUpgradeTradeItemId(tool.UpgradeLevel);
        if (basePrice <= 0 || string.IsNullOrWhiteSpace(tradeItemId))
            return;

        ToolUpgradeData configuredUpgrade = new()
        {
            RequireToolId = tool.ConventionalUpgradeFrom,
            Price = basePrice,
            TradeItemId = Config.BarsRequired > 0 ? tradeItemId : null,
            TradeItemAmount = Config.BarsRequired > 0 ? Config.BarsRequired : 0
        };

        tool.ConventionalUpgradeFrom = null;
        tool.UpgradeFrom ??= new List<ToolUpgradeData>();
        tool.UpgradeFrom.RemoveAll(upgrade => string.Equals(upgrade.RequireToolId, configuredUpgrade.RequireToolId, StringComparison.OrdinalIgnoreCase));
        tool.UpgradeFrom.Insert(0, configuredUpgrade);
    }

    private void ApplyConfiguredUpgradeCost(ToolUpgradeData upgrade)
    {
        if (upgrade.Price > 0)
            upgrade.Price = GetConfiguredPrice(upgrade.Price);

        if (Config.BarsRequired <= 0)
        {
            upgrade.TradeItemId = null;
            upgrade.TradeItemAmount = 0;
        }
        else if (!string.IsNullOrWhiteSpace(upgrade.TradeItemId))
        {
            upgrade.TradeItemAmount = Config.BarsRequired;
        }
    }

    private int GetConfiguredPrice(int basePrice)
    {
        return (int)Math.Round(Math.Max(0, basePrice) * Config.PriceMultiplier, MidpointRounding.AwayFromZero);
    }

    private static int GetConventionalUpgradePrice(int upgradeLevel)
    {
        return upgradeLevel switch
        {
            1 => 2000,
            2 => 5000,
            3 => 10000,
            4 => 25000,
            _ => 0
        };
    }

    private static string? GetConventionalUpgradeTradeItemId(int upgradeLevel)
    {
        return upgradeLevel switch
        {
            1 => "(O)334",
            2 => "(O)335",
            3 => "(O)336",
            4 => "(O)337",
            _ => null
        };
    }

    private bool MakeLocalToolUpgradeReady(Farmer? player, bool queueVanillaReturn)
    {
        if (player is null || !IsCurrentScreenPlayer(player))
            return false;

        if (player.toolBeingUpgraded.Value is null || player.daysLeftForToolUpgrade.Value <= 0)
            return false;

        player.daysLeftForToolUpgrade.Value = 0;

        if (queueVanillaReturn)
            QueueVanillaReturn(player);

        return true;
    }

    private void QueueVanillaReturn(Farmer player)
    {
        PendingVanillaReturn = true;
        PendingVanillaReturnPlayerId = player.UniqueMultiplayerID;
        VanillaReturnDelayTicks = Math.Max(VanillaReturnDelayTicks, 3);
    }

    private void ClearPendingVanillaReturn()
    {
        PendingVanillaReturn = false;
        PendingVanillaReturnPlayerId = 0L;
        VanillaReturnDelayTicks = 0;
    }

    private void TryTriggerPendingVanillaReturn()
    {
        if (!PendingVanillaReturn)
            return;

        Farmer player = Game1.player;
        if (!IsCurrentScreenPlayer(player) || PendingVanillaReturnPlayerId != player.UniqueMultiplayerID)
        {
            ClearPendingVanillaReturn();
            return;
        }

        if (player.toolBeingUpgraded.Value is null)
        {
            ClearPendingVanillaReturn();
            return;
        }

        if (player.daysLeftForToolUpgrade.Value > 0)
        {
            player.daysLeftForToolUpgrade.Value = 0;
            VanillaReturnDelayTicks = Math.Max(VanillaReturnDelayTicks, 3);
            return;
        }

        if (VanillaReturnDelayTicks > 0)
        {
            VanillaReturnDelayTicks--;
            return;
        }

        if (Game1.activeClickableMenu is not null || Game1.dialogueUp || Game1.eventUp)
            return;

        if (!IsToolUpgradeLocation(player.currentLocation ?? Game1.currentLocation))
            return;

        if (TryRunVanillaBlacksmithAction(player) || TryRunVanillaClintCheckAction(player))
            ClearPendingVanillaReturn();
    }

    private bool TryRunVanillaBlacksmithAction(Farmer player)
    {
        GameLocation? location = player.currentLocation ?? Game1.currentLocation;
        if (!IsToolUpgradeLocation(location))
            return false;

        foreach (MethodInfo method in typeof(GameLocation).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(method => method.Name == "performAction"))
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length == 0 || parameters[0].ParameterType != typeof(string[]))
                continue;

            object?[] args = new object?[parameters.Length];
            bool supported = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (!TryCreatePerformActionArgument(parameters[i].ParameterType, player, i == 0, out object? arg))
                {
                    supported = false;
                    break;
                }

                args[i] = arg;
            }

            if (!supported)
                continue;

            try
            {
                object? result = method.Invoke(location, args);
                if (method.ReturnType == typeof(bool) && result is bool handled)
                    return handled;

                return true;
            }
            catch (TargetInvocationException ex)
            {
                Monitor.Log($"Failed to trigger the vanilla Blacksmith action for instant tool pickup: {ex.InnerException ?? ex}", LogLevel.Error);
                return false;
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to trigger the vanilla Blacksmith action for instant tool pickup: {ex}", LogLevel.Error);
                return false;
            }
        }

        return false;
    }

    private static bool TryCreatePerformActionArgument(Type type, Farmer player, bool firstParameter, out object? value)
    {
        if (firstParameter && type == typeof(string[]))
        {
            value = new[] { "Blacksmith" };
            return true;
        }

        if (type == typeof(Farmer))
        {
            value = player;
            return true;
        }

        int tileX = (int)(player.Position.X / Game1.tileSize);
        int tileY = (int)(player.Position.Y / Game1.tileSize);

        if (type.FullName == "xTile.Dimensions.Location")
        {
            value = Activator.CreateInstance(type, tileX, tileY);
            return value is not null;
        }

        if (type.FullName == "Microsoft.Xna.Framework.Vector2")
        {
            value = Activator.CreateInstance(type, (float)tileX, (float)tileY);
            return value is not null;
        }

        if (type.FullName == "Microsoft.Xna.Framework.Point")
        {
            value = Activator.CreateInstance(type, tileX, tileY);
            return value is not null;
        }

        if (type.IsValueType)
        {
            value = Activator.CreateInstance(type);
            return true;
        }

        if (!type.IsValueType)
        {
            value = null;
            return true;
        }

        value = null;
        return false;
    }

    private bool TryRunVanillaClintCheckAction(Farmer player)
    {
        GameLocation? location = player.currentLocation ?? Game1.currentLocation;
        if (!IsToolUpgradeLocation(location))
            return false;

        NPC? clint = location?.characters.FirstOrDefault(npc => npc?.Name.Equals("Clint", StringComparison.OrdinalIgnoreCase) == true);
        if (clint is null)
            return false;

        clint.checkAction(player, location);
        return true;
    }

    private static bool IsCurrentScreenPlayer(Farmer? player)
    {
        return player is not null && Game1.player is not null && player.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID;
    }

    private static bool IsToolUpgradeLocation(GameLocation? location)
    {
        if (location is null)
            return false;

        string name = location.NameOrUniqueName;
        return name.Equals("Blacksmith", StringComparison.OrdinalIgnoreCase) || name.Equals("Clint", StringComparison.OrdinalIgnoreCase);
    }

    private string FormatPriceMultiplier(float value)
    {
        return Helper.Translation.Get(
            "gmcm.price-multiplier.format",
            new
            {
                multiplier = value.ToString("0.00")
            });
    }

    private string FormatBarsRequired(int value)
    {
        return Helper.Translation.Get(
            "gmcm.bars-required.format",
            new
            {
                bars = value
            });
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
        InvalidateToolUpgradeData();
    }

    private void SaveConfig()
    {
        NormalizeConfig();
        Helper.WriteConfig(Config);
        InvalidateToolUpgradeData();
    }

    private void InvalidateToolUpgradeData()
    {
        Helper.GameContent.InvalidateCache("Data/Tools");
    }

    private void NormalizeConfig()
    {
        Config.PriceMultiplier = Math.Clamp(Config.PriceMultiplier, 0.10f, 5f);
        Config.BarsRequired = Math.Clamp((int)Math.Round(Config.BarsRequired / 5f, MidpointRounding.AwayFromZero) * 5, 0, 25);
    }
}
