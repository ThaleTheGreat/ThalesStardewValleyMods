using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using TileLocation = xTile.Dimensions.Location;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.Powers;
using StardewValley.Tools;

using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletScepter;

internal sealed class WalletScepterModule : WalletModule
{
    internal const string ModuleKey = "Scepter";
    internal const string LegacyUniqueId = "ThaleTheGreat.WalletScepter";

    internal WalletScepterModule(ThaleTheGreat.WalletTools.ModEntry host)
        : base(host, ModuleKey, "Wallet Scepter", LegacyUniqueId)
    {
    }
    private const string ReturnScepterQualifiedItemId = "(T)ReturnScepter";
    private const string WalletFlagKey = "ThaleTheGreat.WalletScepter/HasReturnScepter";
    private const string LegacyWalletFlagKey = "ThaleTheGreat.ReturnScepterWallet/HasReturnScepter";
    private const string WalletPowerId = "ThaleTheGreat.WalletScepter_ReturnScepter";
    private const string SaveMaterializedMarker = "ThaleTheGreat.WalletScepter/SaveMaterialized";
    private const string SaveMaterializedOwnerMarker = "ThaleTheGreat.WalletScepter/OwnerPlayerId";
    private const string DefaultToolTexturePath = "TileSheets/tools";
    private const string DefaultHomeLocationName = "FarmHouse";
    private const int DefaultHomeWarpTileX = 9;
    private const int DefaultHomeWarpTileY = 11;
    private const int MaxPendingSleepAttempts = 90;
    private const string NightChoiceReturnHome = "ReturnHome";
    private const string NightChoiceStayOut = "StayOut";
    private const string NightChoiceGoToBed = "GoToBed";

    private ModConfig Config = new();
    private readonly PerScreen<bool> AutoReturnHandledTodayScreen = new();
    private bool AutoReturnHandledToday
    {
        get => AutoReturnHandledTodayScreen.Value;
        set => AutoReturnHandledTodayScreen.Value = value;
    }

    private readonly PerScreen<bool> PendingSleepAfterHomeWarpScreen = new();
    private bool PendingSleepAfterHomeWarp
    {
        get => PendingSleepAfterHomeWarpScreen.Value;
        set => PendingSleepAfterHomeWarpScreen.Value = value;
    }

    private readonly PerScreen<string> PendingSleepHomeLocationNameScreen = new(() => DefaultHomeLocationName);
    private string PendingSleepHomeLocationName
    {
        get => PendingSleepHomeLocationNameScreen.Value;
        set => PendingSleepHomeLocationNameScreen.Value = value;
    }

    private readonly PerScreen<int> PendingSleepAttemptsScreen = new();
    private int PendingSleepAttempts
    {
        get => PendingSleepAttemptsScreen.Value;
        set => PendingSleepAttemptsScreen.Value = value;
    }
    private bool GmcmRegistered;
    private bool SuppressInventoryConversion;
    private IGenericModConfigMenuApi? GmcmApi;
    private IMobilePhoneApi? MobilePhoneApi;

    internal override void Initialize()
    {
        IModHelper helper = Helper;
        Config = Host.ReadModuleConfig<ModConfig>(ModuleKey);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.DayEnding += OnDayEnding;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.Saved += OnSaved;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
    }



    private static bool IsOwnedSaveMaterializedScepter(Item item, Farmer player)
    {
        if (!item.modData.TryGetValue(SaveMaterializedOwnerMarker, out string? rawOwnerId))
            return !Context.IsMultiplayer;

        return long.TryParse(rawOwnerId, out long ownerId) && ownerId == player.UniqueMultiplayerID;
    }

    private static void ClearSaveMaterializedScepterMarkers(Item item)
    {
        item.modData.Remove(SaveMaterializedMarker);
        item.modData.Remove(SaveMaterializedOwnerMarker);
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;

            if (!Context.IsWorldReady || !HasWalletScepter(Game1.player))
            {
                powers.Remove(WalletPowerId);
                return;
            }

            (string texturePath, Point texturePosition) = GetReturnScepterPowerTexture();
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = GetReturnScepterDisplayName(),
                Description = "The golden handle quivers with raw potential. Press the Wallet Scepter hotkey to return home at will.",
                TexturePath = texturePath,
                TexturePosition = texturePosition,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {WalletFlagKey} true",
                CustomFields = Host.GetWalletPowerCustomFields()
            };
        });
    }


    private static (string TexturePath, Point TexturePosition) GetReturnScepterPowerTexture()
    {
        return (DefaultToolTexturePath, new Point(32, 0));
    }

    private Texture2D CreateMobilePhoneScepterIcon()
    {
        (string texturePath, Point texturePosition) = GetReturnScepterPowerTexture();
        Texture2D sourceTexture = Helper.GameContent.Load<Texture2D>(texturePath);

        const int iconSize = 16;
        Color[] pixels = new Color[iconSize * iconSize];
        sourceTexture.GetData(0, new Rectangle(texturePosition.X, texturePosition.Y, iconSize, iconSize), pixels, 0, pixels.Length);

        Texture2D icon = new(Game1.graphics.GraphicsDevice, iconSize, iconSize);
        icon.SetData(pixels);
        return icon;
    }

    internal override void OnGameLaunched()
    {
        RegisterGmcm();
        RegisterMobilePhoneApp();
    }

    private void RegisterMobilePhoneApp()
    {
        MobilePhoneApi = Helper.ModRegistry.GetApi<IMobilePhoneApi>("aedenthorn.MobilePhone");
        if (MobilePhoneApi is null)
            return;

        try
        {
            Texture2D icon = CreateMobilePhoneScepterIcon();
            MobilePhoneApi.AddApp(LegacyUniqueId, "Wallet Scepter", ActivateScepterFromMobilePhone, icon);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Scepter Mobile Phone app: {ex.Message}", LogLevel.Warn);
        }
    }


    private void ActivateScepterFromMobilePhone()
    {
        MobilePhoneApi?.SetPhoneOpened(false);
        MobilePhoneApi?.SetAppRunning(false);

        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null)
            return;

        if (Config.RequireWalletUnlock && !HasWalletScepter(Game1.player))
        {
            if (Config.ShowHudMessageWhenMissing)
                Game1.addHUDMessage(new HUDMessage("You do not have the Return Scepter in your wallet.", HUDMessage.error_type));

            return;
        }

        UseRealReturnScepterPath();
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        GmcmApi = Host.GetGmcmAdapter<IGenericModConfigMenuApi>(ModuleKey);
        if (GmcmApi is null)
            return;

        try
        {
            GmcmApi.Unregister(ModManifest);

            GmcmApi.Register(
                ModManifest,
                reset: () => Config = new ModConfig(),
                save: () => Host.WriteModuleConfig(ModuleKey, Config)
            );

            GmcmApi.AddKeybindList(
                ModManifest,
                getValue: () => Config.UseReturnScepterKey,
                setValue: value => Config.UseReturnScepterKey = value,
                name: () => "Use Wallet Scepter",
                tooltip: () => "Activates the wallet Return Scepter through the real in-game Return Scepter use path. Default: Left Control + S.",
                fieldId: nameof(Config.UseReturnScepterKey)
            );

            GmcmApi.AddBoolOption(
                ModManifest,
                getValue: () => Config.EnableAutomaticReturnHomeAt2500,
                setValue: value => Config.EnableAutomaticReturnHomeAt2500 = value,
                name: () => "Auto Return Home at 25:00",
                tooltip: () => "After your Return Scepter is wallet-unlocked, offers or performs a late-night farm return at 25:00.",
                fieldId: nameof(Config.EnableAutomaticReturnHomeAt2500)
            );

            GmcmApi.AddBoolOption(
                ModManifest,
                getValue: () => Config.AskBeforeAutomaticReturnHomeAt2500,
                setValue: value => Config.AskBeforeAutomaticReturnHomeAt2500 = value,
                name: () => "Ask Before Auto Return",
                tooltip: () => "When enabled, 25:00 shows choices to return home, stay out, or go to bed. When disabled, 25:00 immediately returns you to the farm.",
                fieldId: nameof(Config.AskBeforeAutomaticReturnHomeAt2500)
            );

            GmcmApi.AddBoolOption(
                ModManifest,
                getValue: () => Config.RequireWalletUnlock,
                setValue: value => Config.RequireWalletUnlock = value,
                name: () => "Require Wallet Unlock",
                tooltip: () => "When enabled, the hotkey only works after this mod has converted a real Return Scepter into a wallet-style unlock.",
                fieldId: nameof(Config.RequireWalletUnlock)
            );

            GmcmApi.AddBoolOption(
                ModManifest,
                getValue: () => Config.ShowHudMessageWhenMissing,
                setValue: value => Config.ShowHudMessageWhenMissing = value,
                name: () => "Show Missing Scepter Message",
                tooltip: () => "Shows a HUD message when the hotkey is pressed before the Return Scepter has been unlocked.",
                fieldId: nameof(Config.ShowHudMessageWhenMissing)
            );


            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Scepter options with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        AutoReturnHandledToday = false;
        PendingSleepAfterHomeWarp = false;
        CollectSaveMaterializedScepters(Game1.player, "save loaded");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        MigrateLegacyWalletFlag(Game1.player);
        ConvertInventoryScepters(Game1.player);
        CleanupLostAndFoundScepterDuplicates(Game1.player, "save loaded");
        InvalidatePowers();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        AutoReturnHandledToday = false;
        PendingSleepAfterHomeWarp = false;
        CollectSaveMaterializedScepters(Game1.player, "day start");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        MigrateLegacyWalletFlag(Game1.player);
        ConvertInventoryScepters(Game1.player);
        CleanupLostAndFoundScepterDuplicates(Game1.player, "day start");
        InvalidatePowers();
    }

    private void OnDayEnding(object? sender, DayEndingEventArgs e)
    {
        CollectSaveMaterializedScepters(Game1.player, "day ending cleanup");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        CleanupLostAndFoundScepterDuplicates(Game1.player, "day ending");
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        CollectSaveMaterializedScepters(Game1.player, "pre-save cleanup");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        CleanupLostAndFoundScepterDuplicates(Game1.player, "saving");
    }

    private void OnSaved(object? sender, SavedEventArgs e)
    {
        CollectSaveMaterializedScepters(Game1.player, "post-save cleanup");
        NormalizePlayerItemsAfterWalletCleanup(Game1.player);
        ConvertInventoryScepters(Game1.player);
        CleanupLostAndFoundScepterDuplicates(Game1.player, "saved");
        InvalidatePowers();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!GmcmRegistered)
            RegisterGmcm();

        if (!Context.IsWorldReady)
            return;

        if (PendingSleepAfterHomeWarp)
        {
            HandlePendingSleepAfterHomeWarp();
            return;
        }

        if (AutoReturnHandledToday || !Config.EnableAutomaticReturnHomeAt2500)
            return;

        if (Game1.timeOfDay < 2500 || Game1.activeClickableMenu is not null || !HasWalletScepter(Game1.player))
            return;

        AutoReturnHandledToday = true;

        if (Config.AskBeforeAutomaticReturnHomeAt2500)
            ShowNightReturnDialogue();
        else if (ForceReturnHomeForNight())
            Game1.addHUDMessage(new HUDMessage("Return Scepter brought you home for the night.", HUDMessage.newQuest_type));
    }

    private void ShowNightReturnDialogue()
    {
        Response[] responses =
        {
            new Response(NightChoiceReturnHome, "Yes"),
            new Response(NightChoiceStayOut, "No"),
            new Response(NightChoiceGoToBed, "Go to bed")
        };

        Game1.currentLocation.createQuestionDialogue(
            "You feel the subtle pull of the Scepter. Let it bring you home?",
            responses,
            OnNightReturnDialogueAnswered
        );

    }

    private void OnNightReturnDialogueAnswered(Farmer who, string answer)
    {
        switch (answer)
        {
            case NightChoiceReturnHome:
                if (ForceReturnHomeForNight())
                    Game1.addHUDMessage(new HUDMessage("Return Scepter brought you home for the night.", HUDMessage.newQuest_type));
                break;

            case NightChoiceGoToBed:
                StartNormalSleepFlow();
                break;

            case NightChoiceStayOut:
                break;
        }
    }

    private void StartNormalSleepFlow()
    {
        string homeName = GetPlayerHomeLocationName(Game1.player) ?? DefaultHomeLocationName;

        try
        {
            Game1.warpFarmer(homeName, DefaultHomeWarpTileX, DefaultHomeWarpTileY, false);
            PendingSleepAfterHomeWarp = true;
            PendingSleepHomeLocationName = homeName;
            PendingSleepAttempts = 0;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to warp home for the Go to bed response: {ex}", LogLevel.Error);
        }
    }

    private void HandlePendingSleepAfterHomeWarp()
    {
        PendingSleepAttempts++;

        if (Game1.activeClickableMenu is not null || Game1.currentLocation is null)
            return;

        if (!string.Equals(Game1.currentLocation.NameOrUniqueName, PendingSleepHomeLocationName, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(Game1.currentLocation.Name, PendingSleepHomeLocationName, StringComparison.OrdinalIgnoreCase))
        {
            if (PendingSleepAttempts < MaxPendingSleepAttempts)
                return;

            Monitor.Log($"Timed out waiting to reach home location '{PendingSleepHomeLocationName}' for the Go to bed response.", LogLevel.Warn);
        }

        PendingSleepAfterHomeWarp = false;
        StartSleepAtCurrentLocation();
    }

    private void StartSleepAtCurrentLocation()
    {
        try
        {
            Point? sleepTile = FindSleepTile(Game1.currentLocation);
            if (sleepTile.HasValue)
            {
                Game1.player.Position = new Vector2(sleepTile.Value.X * Game1.tileSize, sleepTile.Value.Y * Game1.tileSize);
                Game1.player.faceDirection(2);
                TrySetPlayerInBedFlag();
            }

            if (TryInvokeStartSleep(Game1.currentLocation))
                return;

            if (sleepTile.HasValue && TryPerformSleepTileAction(Game1.currentLocation, sleepTile.Value))
                return;

            Monitor.Log($"Could not trigger the normal sleep flow from '{Game1.currentLocation.NameOrUniqueName}' for the Go to bed response.", LogLevel.Error);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to start the normal sleep flow: {ex}", LogLevel.Error);
        }
    }

    private static void TrySetPlayerInBedFlag()
    {
        try
        {
            object? isInBed = typeof(Farmer).GetField("isInBed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Game1.player)
                ?? typeof(Farmer).GetProperty("isInBed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(Game1.player);

            if (isInBed is null)
                return;

            PropertyInfo? valueProperty = isInBed.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueProperty is not null && valueProperty.CanWrite)
            {
                valueProperty.SetValue(isInBed, true);
                return;
            }

            if (isInBed is bool)
            {
                FieldInfo? field = typeof(Farmer).GetField("isInBed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                field?.SetValue(Game1.player, true);
            }
        }
        catch
        {
        }
    }

    private bool TryInvokeStartSleep(GameLocation location)
    {
        foreach (MethodInfo method in FindStartSleepMethods(location))
        {
            if (!TryBuildArguments(method, out object?[] arguments))
                continue;

            try
            {
                method.Invoke(location, arguments);
                return true;
            }
            catch (TargetInvocationException)
            {
            }
            catch (Exception)
            {
            }
        }

        return false;
    }

    private static MethodInfo[] FindStartSleepMethods(GameLocation location)
    {
        List<MethodInfo> methods = new();

        for (Type? type = location.GetType(); type is not null; type = type.BaseType)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (string.Equals(method.Name, "startSleep", StringComparison.OrdinalIgnoreCase))
                    methods.Add(method);
            }
        }

        return methods
            .OrderBy(method => method.GetParameters().Length)
            .ToArray();
    }

    private static bool TryBuildArguments(MethodInfo method, out object?[] arguments)
    {
        ParameterInfo[] parameters = method.GetParameters();
        arguments = new object?[parameters.Length];

        for (int i = 0; i < parameters.Length; i++)
        {
            Type parameterType = parameters[i].ParameterType;

            if (parameterType.IsAssignableFrom(typeof(Farmer)))
            {
                arguments[i] = Game1.player;
                continue;
            }

            if (parameterType == typeof(bool))
            {
                arguments[i] = true;
                continue;
            }

            if (parameterType == typeof(string))
            {
                arguments[i] = null;
                continue;
            }

            if (parameterType.IsValueType)
            {
                arguments[i] = Activator.CreateInstance(parameterType);
                continue;
            }

            arguments[i] = null;
        }

        return true;
    }

    private bool TryPerformSleepTileAction(GameLocation location, Point sleepTile)
    {
        TileLocation tileLocation = new(sleepTile.X, sleepTile.Y);

        foreach (MethodInfo method in FindPerformActionMethods(location))
        {
            object?[]? arguments = BuildPerformActionArguments(method, tileLocation);
            if (arguments is null)
                continue;

            try
            {
                method.Invoke(location, arguments);
                return true;
            }
            catch (TargetInvocationException)
            {
            }
            catch (Exception)
            {
            }
        }

        return false;
    }

    private static MethodInfo[] FindPerformActionMethods(GameLocation location)
    {
        List<MethodInfo> methods = new();

        for (Type? type = location.GetType(); type is not null; type = type.BaseType)
        {
            foreach (MethodInfo method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (string.Equals(method.Name, "performAction", StringComparison.OrdinalIgnoreCase))
                    methods.Add(method);
            }
        }

        return methods.ToArray();
    }

    private static object?[]? BuildPerformActionArguments(MethodInfo method, TileLocation tileLocation)
    {
        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length != 3)
            return null;

        object? first;
        if (parameters[0].ParameterType == typeof(string[]))
            first = new[] { "Sleep" };
        else if (parameters[0].ParameterType == typeof(string))
            first = "Sleep";
        else
            return null;

        if (!parameters[1].ParameterType.IsAssignableFrom(typeof(Farmer)))
            return null;

        if (!parameters[2].ParameterType.IsAssignableFrom(typeof(TileLocation)))
            return null;

        return new object?[] { first, Game1.player, tileLocation };
    }

    private static Point? FindSleepTile(GameLocation location)
    {
        try
        {
            if (location.Map is null)
                return null;

            foreach (xTile.Layers.Layer layer in location.Map.Layers)
            {
                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    for (int y = 0; y < layer.LayerHeight; y++)
                    {
                        xTile.Tiles.Tile? tile = layer.Tiles[x, y];
                        if (tile is null)
                            continue;

                        foreach (KeyValuePair<string, xTile.ObjectModel.PropertyValue> entry in tile.Properties)
                        {
                            string key = entry.Key ?? string.Empty;
                            string value = entry.Value?.ToString() ?? string.Empty;

                            if ((string.Equals(key, "Action", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(key, "TouchAction", StringComparison.OrdinalIgnoreCase))
                                && value.IndexOf("Sleep", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                return new Point(x, y);
                            }
                        }
                    }
                }
            }
        }
        catch
        {
            return null;
        }

        return null;
    }

    private static string? GetPlayerHomeLocationName(Farmer player)
    {
        try
        {
            object? homeLocation = typeof(Farmer).GetField("homeLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(player)
                ?? typeof(Farmer).GetProperty("homeLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(player);

            if (homeLocation is null)
                return null;

            object? value = homeLocation.GetType().GetProperty("Value")?.GetValue(homeLocation);
            return value as string ?? homeLocation.ToString();
        }
        catch
        {
            return null;
        }
    }

    private bool ForceReturnHomeForNight()
    {
        try
        {
            Game1.warpFarmer("Farm", 64, 15, false);
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to force 25:00 farm return: {ex}", LogLevel.Error);
            return false;
        }
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || e.Player.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
            return;

        ConvertInventoryScepters(e.Player);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.UseReturnScepterKey.JustPressed())
            return;

        Helper.Input.Suppress(e.Button);

        if (Game1.activeClickableMenu is not null)
            return;

        if (Config.RequireWalletUnlock && !HasWalletScepter(Game1.player))
        {
            if (Config.ShowHudMessageWhenMissing)
                Game1.addHUDMessage(new HUDMessage("You do not have the Return Scepter in your wallet.", HUDMessage.error_type));

            return;
        }

        UseRealReturnScepterPath();
    }

    private void ConvertInventoryScepters(Farmer player)
    {
        if (SuppressInventoryConversion)
        {
            return;
        }

        bool converted = false;

        for (int index = player.Items.Count - 1; index >= 0; index--)
        {
            Item? item = player.Items[index];
            if (!IsReturnScepter(item) || IsSaveMaterializedScepter(item))
                continue;

            player.Items[index] = null;
            converted = true;
        }

        if (!converted)
            return;

        player.modData[WalletFlagKey] = "true";
        NormalizePlayerItemsAfterWalletCleanup(player);
        InvalidatePowers();
        Game1.addHUDMessage(new HUDMessage("Return Scepter added to wallet.", HUDMessage.newQuest_type));
    }

    private bool UseRealReturnScepterPath()
    {
        Item item;

        try
        {
            item = ItemRegistry.Create(ReturnScepterQualifiedItemId);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to create a real Return Scepter item instance: {ex}", LogLevel.Error);
            return false;
        }

        try
        {
            if (item is StardewValley.Object obj)
            {
                bool result = obj.performUseAction(Game1.currentLocation);
                return result;
            }

            if (item is Tool tool)
            {
                tool.DoFunction(
                    Game1.currentLocation,
                    (int)Game1.player.Position.X,
                    (int)Game1.player.Position.Y,
                    0,
                    Game1.player
                );
                return true;
            }

            Monitor.Log($"Created Return Scepter item is not an Object or Tool and cannot be activated directly. {DescribeItemIdentity(item)}", LogLevel.Warn);
            return false;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed while activating the real Return Scepter use path: {ex}", LogLevel.Error);
            return false;
        }
    }


    private void CollectSaveMaterializedScepters(Farmer player, string reason)
    {
        bool collected = false;

        SuppressInventoryConversion = true;
        try
        {
            for (int index = player.Items.Count - 1; index >= 0; index--)
            {
                Item? item = player.Items[index];
                if (!IsSaveMaterializedScepter(item) || item is null || !IsOwnedSaveMaterializedScepter(item, player))
                    continue;

                player.Items[index] = null;
                ClearSaveMaterializedScepterMarkers(item);
                player.modData[WalletFlagKey] = "true";
                collected = true;
            }
        }
        finally
        {
            SuppressInventoryConversion = false;
        }

        if (collected)
            NormalizePlayerItemsAfterWalletCleanup(player);
    }



    private static void NormalizePlayerItemsAfterWalletCleanup(Farmer player)
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

    private static int GetPlayerMaxItemCount(Farmer player)
    {
        int maxItems = GetIntMember(player, "MaxItems", "maxItems");
        return Math.Max(12, maxItems <= 0 ? 36 : maxItems);
    }

    private static int GetIntMember(object instance, params string[] memberNames)
    {
        Type type = instance.GetType();
        foreach (string memberName in memberNames)
        {
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property?.GetValue(instance) is int propertyValue)
                return propertyValue;

            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field?.GetValue(instance) is int fieldValue)
                return fieldValue;
        }

        return 0;
    }

    private string GetReturnScepterDisplayName()
    {
        try
        {
            return ItemRegistry.Create(ReturnScepterQualifiedItemId).DisplayName;
        }
        catch (Exception)
        {
            return "Return Scepter";
        }
    }

    private void CleanupLostAndFoundScepterDuplicates(Farmer player, string reason)
    {
        if (!HasWalletScepter(player))
            return;

        if (!TryGetLostAndFoundItems(player, out IList? lostAndFoundItems) || lostAndFoundItems is null)
            return;

        for (int index = lostAndFoundItems.Count - 1; index >= 0; index--)
        {
            if (lostAndFoundItems[index] is not Item item || !IsReturnScepter(item))
                continue;

            if (Context.IsMultiplayer && !IsOwnedSaveMaterializedScepter(item, player))
                continue;

            lostAndFoundItems.RemoveAt(index);
            player.modData[WalletFlagKey] = "true";
        }
    }

    private static bool TryGetLostAndFoundItems(Farmer player, out IList? lostAndFoundItems)
    {
        lostAndFoundItems = null;

        object? team = GetMemberValue(player, "team", "Team");
        if (team is null)
            return false;

        object? list = GetMemberValue(team, "lostAndFoundItems", "LostAndFoundItems");
        if (list is IList itemList)
        {
            lostAndFoundItems = itemList;
            return true;
        }

        return false;
    }

    private static object? GetMemberValue(object instance, params string[] memberNames)
    {
        Type type = instance.GetType();
        foreach (string memberName in memberNames)
        {
            PropertyInfo? property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property is not null)
                return property.GetValue(instance);

            FieldInfo? field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
                return field.GetValue(instance);
        }

        return null;
    }

    private static string DescribeItemIdentity(Item item)
    {
        return $"type='{item.GetType().FullName}', qualifiedId='{item.QualifiedItemId}', itemId='{item.ItemId}', name='{item.Name}', displayName='{item.DisplayName}', saveMaterialized='{IsSaveMaterializedScepter(item)}'";
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private static void MigrateLegacyWalletFlag(Farmer player)
    {
        if (player.modData.ContainsKey(WalletFlagKey))
            return;

        if (player.modData.TryGetValue(LegacyWalletFlagKey, out string? value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase))
        {
            player.modData[WalletFlagKey] = "true";
        }
    }

    private static bool HasWalletScepter(Farmer player)
    {
        return HasWalletFlag(player, WalletFlagKey) || HasWalletFlag(player, LegacyWalletFlagKey);
    }

    private static bool HasWalletFlag(Farmer player, string key)
    {
        return player.modData.TryGetValue(key, out string? value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSaveMaterializedScepter(Item? item)
    {
        return IsReturnScepter(item)
            && item!.modData.TryGetValue(SaveMaterializedMarker, out string? value)
            && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsReturnScepter(Item? item)
    {
        if (item is null)
            return false;

        return string.Equals(item.QualifiedItemId, ReturnScepterQualifiedItemId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(item.ItemId, "ReturnScepter", StringComparison.OrdinalIgnoreCase);
    }

}
