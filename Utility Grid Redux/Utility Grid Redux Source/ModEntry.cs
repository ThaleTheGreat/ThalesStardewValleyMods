using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using System.Globalization;
using System.Runtime.CompilerServices;
using Object = StardewValley.Object;

namespace ThaleTheGreat.UtilityGridRedux;

public sealed class ModEntry : Mod
{
    internal const string ModId = "ThaleTheGreat.UtilityGridRedux";
    private const string SaveKey = "utility-grid-redux-data";
    private const string WaterChargeKey = ModId + "/waterCharge";
    private const string PowerChargeKey = ModId + "/powerCharge";
    private const string MachinePauseKey = ModId + "/PausedMinutesUntilReady";
    private const string WaterPumpTexturePath = "assets/water_pump.png";
    private const string StorageTexturePath = "assets/storage.png";
    private const string PipesTexturePath = "assets/pipes.png";
    private const string DropTexturePath = "assets/drop.png";
    private const int GridEditViewportPanSpeed = 16;
    private const int GridEditViewportEdgeSize = 48;

    private static readonly Vector2[] AdjacentTiles =
    {
        new(0, 1),
        new(1, 0),
        new(-1, 0),
        new(0, -1)
    };

    private static readonly Vector2[] WateringOffsets =
    {
        new(-1, -1),
        new(-1, 0),
        new(-1, 1),
        new(0, -1),
        new(0, 0),
        new(0, 1),
        new(1, -1),
        new(1, 0),
        new(1, 1)
    };

    private static readonly int[][] IntakeArray =
    {
        new[] { 1, 0, 0, 0 },
        new[] { 0, 1, 0, 1 },
        new[] { 1, 0, 0, 1 },
        new[] { 0, 1, 1, 1 },
        new[] { 1, 1, 1, 1 }
    };

    internal static ModEntry Instance = null!;
    internal static IMonitor SMonitor = null!;
    internal static IModHelper SHelper = null!;
    internal static ModConfig Config = new();
    internal static bool ShowingGrid { get; private set; }
    internal static bool EditingGrid { get; private set; }
    internal static GridKind CurrentGrid { get; private set; } = GridKind.Water;
    internal static int CurrentTile { get; private set; }
    internal static int CurrentRotation { get; private set; }

    private static readonly Dictionary<string, Dictionary<GridKind, UtilitySystem>> Systems = new();
    private static readonly Dictionary<string, UtilityObjectRule> ObjectRules = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Vector2>> WateringTiles = new();
    private static readonly Dictionary<string, List<Vector2>> WateringPipes = new();
    private static long PowerCacheVersion;

    private readonly Harmony harmony = new(ModId);
    private Texture2D? pipeTexture;
    private Texture2D? dropTexture;
    private Vector2? dropCenterOffset;
    private Vector2 lastCursorTile = new(-1, -1);
    private PipeActionKind? lastDragAction;
    private bool gridPlaceHeld;
    private bool gridDestroyHeld;
    private readonly List<IClickableMenu> hiddenGridEditMenus = new();
    private xTile.Dimensions.Rectangle? gridEditViewport;
    private string? gridEditStatus;
    private int gridEditStatusTicks;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        SMonitor = Monitor;
        SHelper = helper;
        Config = helper.ReadConfig<ModConfig>();
        NormalizeConfig();
        LoadBuiltInRules();

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.TimeChanged += OnTimeChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Input.ButtonReleased += OnButtonReleased;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Multiplayer.ModMessageReceived += OnModMessageReceived;
        helper.Events.Display.RenderedWorld += OnRenderedWorld;
        helper.Events.Display.RenderedHud += OnRenderedHud;
        helper.Events.World.ObjectListChanged += OnObjectListChanged;
        helper.Events.Player.Warped += OnWarped;

        harmony.Patch(
            original: AccessTools.Method(typeof(Utility), nameof(Utility.playerCanPlaceItemHere)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(PlayerCanPlaceItemHerePrefix))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Object), nameof(Object.minutesElapsed)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ObjectMinutesElapsedPrefix)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(ObjectMethodPostfix))
        );
        harmony.Patch(
            original: AccessTools.Method(typeof(Object), nameof(Object.DayUpdate)),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(ObjectDayUpdatePrefix)),
            postfix: new HarmonyMethod(typeof(ModEntry), nameof(ObjectMethodPostfix))
        );
    }

    public override object GetApi()
    {
        return new UtilityGridReduxApi();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, BigCraftableData> data = asset.AsDictionary<string, BigCraftableData>().Data;
                string pumpTexture = Helper.ModContent.GetInternalAssetName(WaterPumpTexturePath).Name;
                string storageTexture = Helper.ModContent.GetInternalAssetName(StorageTexturePath).Name;
                AddBigCraftable(data, "BronzeWaterPump", "Bronze Water Pump", "Produces 10 water. Consumes 2 power.", 100, pumpTexture, 0);
                AddBigCraftable(data, "SteelWaterPump", "Steel Water Pump", "Produces 25 water. Consumes 4 power.", 1000, pumpTexture, 1);
                AddBigCraftable(data, "GoldWaterPump", "Gold Water Pump", "Produces 80 water. Consumes 8 power.", 10000, pumpTexture, 2);
                AddBigCraftable(data, "IridiumWaterPump", "Iridium Water Pump", "Produces 200 water. Consumes 16 power.", 100000, pumpTexture, 3);
                AddBigCraftable(data, "UtilityGridBattery", "Utility Grid Battery", "Stores 50 power. Outputs 2 power.", 1000, storageTexture, 0);
                AddBigCraftable(data, "UtilityGridWaterTank", "Utility Grid Water Tank", "Stores 50 water. Outputs 2 water.", 1000, storageTexture, 1);
                AddBigCraftable(data, "UtilityGridAdvancedBattery", "Utility Grid Advanced Battery", "Stores 100 power. Outputs 5 power.", 5000, storageTexture, 2);
                ApplyUtilityGridTooltips(data);
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
                ApplyToolAndSprinklerUpgradeTooltips(data);
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data["Bronze Water Pump"] = "334 5 335 5/Utilities/" + ItemId("BronzeWaterPump") + "/true/s Farming 2/";
                data["Steel Water Pump"] = "335 5 336 5/Utilities/" + ItemId("SteelWaterPump") + "/true/s Farming 4/";
                data["Gold Water Pump"] = "336 5 338 5/Utilities/" + ItemId("GoldWaterPump") + "/true/s Farming 6/";
                data["Iridium Water Pump"] = "336 5 337 5 787 5/Utilities/" + ItemId("IridiumWaterPump") + "/true/s Farming 8/";
                data["Utility Grid Battery"] = "787 4 334 2 335 1/Utilities/" + ItemId("UtilityGridBattery") + "/true/s Farming 2/";
                data["Utility Grid Water Tank"] = "787 4 334 2 335 1/Utilities/" + ItemId("UtilityGridWaterTank") + "/true/s Farming 2/";
                data["Utility Grid Advanced Battery"] = "787 8 336 4 337 2/Utilities/" + ItemId("UtilityGridAdvancedBattery") + "/true/s Farming 4/";
            });
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ShopData> data = asset.AsDictionary<string, ShopData>().Data;
                if (!data.TryGetValue("Carpenter", out ShopData? shop))
                    return;

                AddShopItem(shop, "BronzeWaterPump", 500, 10);
                AddShopItem(shop, "SteelWaterPump", 2500, 10);
                AddShopItem(shop, "GoldWaterPump", 10000, 10);
                AddShopItem(shop, "IridiumWaterPump", 25000, 10);
                AddShopItem(shop, "UtilityGridBattery", 5000, 10);
                AddShopItem(shop, "UtilityGridWaterTank", 5000, 10);
                AddShopItem(shop, "UtilityGridAdvancedBattery", 10000, 10);
            });
        }
    }

    private static void AddBigCraftable(IDictionary<string, BigCraftableData> data, string key, string displayName, string description, int price, string texture, int spriteIndex)
    {
        string itemId = ItemId(key);
        data[itemId] = new BigCraftableData
        {
            Name = itemId,
            DisplayName = displayName,
            Description = description,
            Price = price,
            Texture = texture,
            SpriteIndex = spriteIndex,
            Fragility = 0
        };
    }

    private static void ApplyUtilityGridTooltips(IDictionary<string, BigCraftableData> data)
    {
        Dictionary<string, string> notes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Sprinkler"] = "Consumes 1 water.",
            ["Quality Sprinkler"] = "Consumes 2 water.",
            ["Iridium Sprinkler"] = "Consumes 6 water. Consumes 2 power.",
            ["Furnace"] = "Produces 10 power.",
            ["Charcoal Kiln"] = "Produces 10 power.",
            ["Solar Panel"] = "Produces 10 power.",
            ["Crystalarium"] = "Consumes 3 power.",
            ["Recycling Machine"] = "Consumes 1 power.",
            ["Slime Incubator"] = "Consumes 2 power.",
            ["Wood Chipper"] = "Consumes 2 power.",
            ["Ostrich Incubator"] = "Consumes 2 power.",
            ["Deconstructor"] = "Consumes 2 power.",
            ["Soda Machine"] = "Consumes 1 power.",
            ["Coffee Maker"] = "Consumes 1 power.",
            ["Heavy Furnace"] = "Consumes 4 power.",
            ["Geode Crusher"] = "Consumes 2 power.",
            ["Mini-Forge"] = "Consumes 3 power.",
            ["Bait Maker"] = "Consumes 1 power.",
            ["Bone Mill"] = "Consumes 2 power.",
            ["Slime Egg-Press"] = "Consumes 2 power.",
            ["Seed Maker"] = "Consumes 1 power.",
            ["Dehydrator"] = "Consumes 2 power.",
            ["Fish Smoker"] = "Consumes 2 power.",
            ["Oil Maker"] = "Consumes 2 power.",
            ["Loom"] = "Consumes 1 power.",
            ["Mayonnaise Machine"] = "Consumes 1 power.",
            ["Cheese Press"] = "Consumes 1 power.",
            ["Hopper"] = "Consumes 1 power.",
            ["Farm Computer"] = "Consumes 1 power.",
            ["Telephone"] = "Consumes 1 power.",
            ["Sewing Machine"] = "Consumes 1 power.",
            ["Mini-Jukebox"] = "Consumes 1 power.",};

        foreach (BigCraftableData craftable in data.Values)
        {
            if (!TryGetUtilityTooltipNote(craftable, notes, out string? note) || string.IsNullOrWhiteSpace(note))
                continue;

            string description = craftable.Description ?? string.Empty;
            if (!description.Contains(note, StringComparison.OrdinalIgnoreCase))
                craftable.Description = string.IsNullOrWhiteSpace(description) ? note : description.TrimEnd() + " " + note;
        }
    }

    private static void ApplyToolAndSprinklerUpgradeTooltips(IDictionary<string, ObjectData> data)
    {
        Dictionary<string, string> notes = new(StringComparer.OrdinalIgnoreCase)
        {
            ["ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltSprinkler"] = "Consumes 10 water. Consumes 3 power.",
            ["ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticSprinkler"] = "Consumes 25 water. Consumes 6 power.",
            ["ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveSprinkler"] = "Consumes 50 water. Consumes 10 power."
        };

        foreach ((string itemId, string note) in notes)
        {
            if (!data.TryGetValue(itemId, out ObjectData? obj) || string.IsNullOrWhiteSpace(note))
                continue;

            string description = obj.Description ?? string.Empty;
            if (!description.Contains(note, StringComparison.OrdinalIgnoreCase))
                obj.Description = string.IsNullOrWhiteSpace(description) ? note : description.TrimEnd() + " " + note;
        }
    }

    private static bool TryGetUtilityTooltipNote(BigCraftableData craftable, Dictionary<string, string> notes, out string? note)
    {
        if (!string.IsNullOrWhiteSpace(craftable.DisplayName) && notes.TryGetValue(craftable.DisplayName, out note))
            return true;

        if (!string.IsNullOrWhiteSpace(craftable.Name) && notes.TryGetValue(craftable.Name, out note))
            return true;

        note = null;
        return false;
    }

    private static void AddShopItem(ShopData shop, string key, int price, int stock)
    {
        string qualifiedId = "(BC)" + ItemId(key);
        shop.Items.RemoveAll(item => item.Id == ModId + "/" + key);
        shop.Items.Add(new ShopItemData
        {
            Id = ModId + "/" + key,
            ItemId = qualifiedId,
            Price = price,
            AvailableStock = stock
        });
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        pipeTexture = Helper.ModContent.Load<Texture2D>(PipesTexturePath);
        dropTexture = Helper.ModContent.Load<Texture2D>(DropTexturePath);
        dropCenterOffset = null;
        RegisterGmcm();
        RegisterBetterCrafting();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        Systems.Clear();
        WateringTiles.Clear();
        WateringPipes.Clear();
        ShowingGrid = false;
        EditingGrid = false;
        CurrentGrid = GridKind.Water;
        CurrentRotation = 0;
        CurrentTile = 0;

        UtilitySystemSaveData saveData = Helper.Data.ReadSaveData<UtilitySystemSaveData>(SaveKey) ?? new UtilitySystemSaveData();
        ApplySaveData(saveData);

        if (Context.IsMultiplayer && !Context.IsMainPlayer)
            Helper.Multiplayer.SendMessage(new GridSyncRequest(), nameof(GridSyncRequest), new[] { ModId });
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (Context.IsMultiplayer && !Context.IsMainPlayer)
            return;

        Helper.Data.WriteSaveData(SaveKey, CreateSaveData());
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Config.EnableMod || !Config.EnablePipeIrrigation || (Context.IsMultiplayer && !Context.IsMainPlayer))
            return;

        foreach (GameLocation location in Game1.locations)
        {
            if (CanPipeIrrigateLocation(location))
                WaterLocationFromPipes(location, true);
        }
    }

    private void OnTimeChanged(object? sender, TimeChangedEventArgs e)
    {
        if (!Config.EnableMod || (Context.IsMultiplayer && !Context.IsMainPlayer))
            return;

        float hours = GetElapsedHours(e.OldTime, e.NewTime);
        PowerCacheVersion++;
        foreach (string locationName in Systems.Keys.ToArray())
        {
            EnsureAllGroupsClean(locationName);
            RecalculateLocationPower(locationName);
            Dictionary<GridKind, UtilitySystem> grids = Systems[locationName];
            foreach (GridKind grid in Enum.GetValues<GridKind>())
            {
                foreach (PipeGroup group in grids[grid].Groups)
                    ChangeStorageObjects(locationName, group, grid, hours);
            }
        }

        GameLocation current = Game1.player.currentLocation;
        if (Config.EnablePipeIrrigation && CanPipeIrrigateLocation(current))
            RefreshWateringTiles(current, false);
    }

    private void CycleGridOverlay(GameLocation location)
    {
        EditingGrid = false;
        RestoreHiddenGridEditMenus();

        if (!ShowingGrid)
        {
            ShowingGrid = true;
            CurrentGrid = GridKind.Power;
            return;
        }

        if (CurrentGrid == GridKind.Power)
        {
            CurrentGrid = GridKind.Water;
            if (Config.EnablePipeIrrigation && CanPipeIrrigateLocation(location))
                RefreshWateringTiles(location, false);
            return;
        }

        ShowingGrid = false;
        CurrentGrid = GridKind.Power;
    }

    private bool PreparePlayerForGridEditing()
    {
        if (Game1.activeClickableMenu is not null)
            Game1.exitActiveMenu();

        Farmer player = Game1.player;
        if (!TryStowCursorItem(player))
            return false;

        player.Halt();
        player.completelyStopAnimatingOrDoingAction();
        CaptureGridEditViewport();
        HideGridEditHudMenus();
        return true;
    }

    private static bool TryStowCursorItem(Farmer player)
    {
        if (player.CursorSlotItem is null)
            return true;

        Item heldItem = player.CursorSlotItem;
        player.CursorSlotItem = null;
        Item? leftover = player.addItemToInventory(heldItem);
        if (leftover is null)
            return true;

        player.CursorSlotItem = leftover;
        Game1.showRedMessage(Game1.content.LoadString("Strings\\UI:ItemGrab_FullInventory"));
        return false;
    }

    private void ResetPipeDragState()
    {
        lastCursorTile = new Vector2(-1, -1);
        lastDragAction = null;
        gridPlaceHeld = false;
        gridDestroyHeld = false;
    }

    private void ResetPipeDragPath()
    {
        lastCursorTile = new Vector2(-1, -1);
        lastDragAction = null;
    }

    private void CaptureGridEditViewport()
    {
        gridEditViewport = Game1.viewport;
    }

    private void RestoreHiddenGridEditMenus()
    {
        if (hiddenGridEditMenus.Count == 0)
            return;

        foreach (IClickableMenu menu in hiddenGridEditMenus)
        {
            if (!Game1.onScreenMenus.Contains(menu))
                Game1.onScreenMenus.Add(menu);
        }

        hiddenGridEditMenus.Clear();
    }

    private void HideGridEditHudMenus()
    {
        for (int i = Game1.onScreenMenus.Count - 1; i >= 0; i--)
        {
            if (Game1.onScreenMenus[i] is Toolbar toolbar)
            {
                hiddenGridEditMenus.Add(toolbar);
                Game1.onScreenMenus.RemoveAt(i);
            }
        }
    }

    private void LeaveGridEditing()
    {
        EditingGrid = false;
        gridEditViewport = null;
        ResetPipeDragState();
        RestoreHiddenGridEditMenus();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Config.EnableMod || !Context.IsWorldReady)
            return;

        if (EditingGrid && e.Button == SButton.Escape)
        {
            Helper.Input.Suppress(e.Button);
            LeaveGridEditing();
            return;
        }

        if (Config.ToggleGrid.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            EditingGrid = !EditingGrid;
            if (EditingGrid)
            {
                if (!PreparePlayerForGridEditing())
                {
                    LeaveGridEditing();
                    return;
                }

                ShowingGrid = true;
                ResetPipeDragState();
                if (Config.EnablePipeIrrigation && CurrentGrid == GridKind.Water && CanPipeIrrigateLocation(Game1.player.currentLocation))
                    RefreshWateringTiles(Game1.player.currentLocation, false);
            }
            else
                LeaveGridEditing();
            return;
        }

        if (Game1.activeClickableMenu != null)
            return;

        string locationName = Game1.player.currentLocation.NameOrUniqueName;
        EnsureLocation(locationName);

        if (Config.ToggleGridOverlay.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CycleGridOverlay(Game1.player.currentLocation);
            ResetPipeDragState();
            return;
        }

        if (!ShowingGrid)
            return;

        if (Config.SwitchGrid.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CurrentGrid = CurrentGrid == GridKind.Water ? GridKind.Power : GridKind.Water;
            ResetPipeDragState();
            if (Config.EnablePipeIrrigation && CurrentGrid == GridKind.Water && CanPipeIrrigateLocation(Game1.player.currentLocation))
                RefreshWateringTiles(Game1.player.currentLocation, false);
            return;
        }

        if (!EditingGrid)
            return;

        if (MatchesKeybind(Config.PlaceTile, e.Button))
        {
            Helper.Input.Suppress(e.Button);
            gridPlaceHeld = true;
            gridDestroyHeld = false;
            if (Game1.player?.currentLocation is not null)
                ProcessGridEditAction(Game1.player.currentLocation, PipeActionKind.Place);
            return;
        }

        if (MatchesKeybind(Config.DestroyTile, e.Button))
        {
            Helper.Input.Suppress(e.Button);
            gridDestroyHeld = true;
            gridPlaceHeld = false;
            if (Game1.player?.currentLocation is not null)
                ProcessGridEditAction(Game1.player.currentLocation, PipeActionKind.Destroy);
            return;
        }

        if (Config.SwitchTile.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CurrentTile = (CurrentTile + 1) % 5;
            CurrentRotation = 0;
            ResetPipeDragState();
        }
        else if (Config.RotateTile.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CurrentRotation++;
            if (CurrentTile == 1)
                CurrentRotation %= 2;
            else if (CurrentTile == 4)
                CurrentRotation = 0;
            else
                CurrentRotation %= 4;
            ResetPipeDragState();
        }
    }


    internal void HandleGridEditViewportPan()
    {
        GameLocation? location = Game1.player?.currentLocation;
        if (location is null)
            return;

        int dx = 0;
        int dy = 0;

        Microsoft.Xna.Framework.Input.KeyboardState keyboard = Microsoft.Xna.Framework.Input.Keyboard.GetState();
        if (keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.A) || keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Left))
            dx -= GridEditViewportPanSpeed;
        if (keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.D) || keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Right))
            dx += GridEditViewportPanSpeed;
        if (keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.W) || keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Up))
            dy -= GridEditViewportPanSpeed;
        if (keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.S) || keyboard.IsKeyDown(Microsoft.Xna.Framework.Input.Keys.Down))
            dy += GridEditViewportPanSpeed;

        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        if (mouseX <= GridEditViewportEdgeSize)
            dx -= GridEditViewportPanSpeed;
        else if (mouseX >= Game1.uiViewport.Width - GridEditViewportEdgeSize)
            dx += GridEditViewportPanSpeed;

        if (mouseY <= GridEditViewportEdgeSize)
            dy -= GridEditViewportPanSpeed;
        else if (mouseY >= Game1.uiViewport.Height - GridEditViewportEdgeSize)
            dy += GridEditViewportPanSpeed;

        if (dx == 0 && dy == 0)
            return;

        xTile.Dimensions.Rectangle viewport = gridEditViewport ?? Game1.viewport;
        int maxX = Math.Max(0, location.map.DisplayWidth - viewport.Width);
        int maxY = Math.Max(0, location.map.DisplayHeight - viewport.Height);
        viewport.X = Math.Clamp(viewport.X + dx, 0, maxX);
        viewport.Y = Math.Clamp(viewport.Y + dy, 0, maxY);
        gridEditViewport = viewport;
        Game1.viewport = viewport;
    }

    private void ApplyGridEditViewport()
    {
        if (gridEditViewport.HasValue)
            Game1.viewport = gridEditViewport.Value;
    }

    private xTile.Dimensions.Rectangle GetGridEditViewport()
    {
        return gridEditViewport ?? Game1.viewport;
    }

    private Vector2 GetGridEditCursorTile()
    {
        xTile.Dimensions.Rectangle viewport = GetGridEditViewport();
        int x = (Game1.getMouseX() + viewport.X) / Game1.tileSize;
        int y = (Game1.getMouseY() + viewport.Y) / Game1.tileSize;
        return new Vector2(x, y);
    }

    private void SuppressGridEditInputs()
    {
        Helper.Input.Suppress(SButton.W);
        Helper.Input.Suppress(SButton.A);
        Helper.Input.Suppress(SButton.S);
        Helper.Input.Suppress(SButton.D);
        Helper.Input.Suppress(SButton.Up);
        Helper.Input.Suppress(SButton.Down);
        Helper.Input.Suppress(SButton.Left);
        Helper.Input.Suppress(SButton.Right);
        Helper.Input.Suppress(SButton.MouseLeft);
        Helper.Input.Suppress(SButton.MouseRight);
        SuppressKeybind(Config.PlaceTile);
        SuppressKeybind(Config.DestroyTile);
    }

    private void SuppressKeybind(KeybindList keybindList)
    {
        foreach (Keybind keybind in keybindList.Keybinds)
        {
            foreach (SButton button in keybind.Buttons)
                Helper.Input.Suppress(button);
        }
    }

    private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
    {
        if (!Config.EnableMod || !Context.IsWorldReady || !EditingGrid)
            return;

        bool changed = false;
        if (MatchesKeybind(Config.PlaceTile, e.Button))
        {
            gridPlaceHeld = false;
            changed = true;
        }

        if (MatchesKeybind(Config.DestroyTile, e.Button))
        {
            gridDestroyHeld = false;
            changed = true;
        }

        if (changed)
        {
            ResetPipeDragPath();
            Helper.Input.Suppress(e.Button);
        }
    }

    private static bool MatchesKeybind(KeybindList keybindList, SButton button)
    {
        foreach (Keybind keybind in keybindList.Keybinds)
        {
            if (keybind.Buttons.Contains(button))
                return true;
        }

        return false;
    }

    private bool IsGridEditKeybindHeld(KeybindList keybindList)
    {
        foreach (Keybind keybind in keybindList.Keybinds)
        {
            if (keybind.Buttons.Length == 0)
                continue;

            bool held = true;
            foreach (SButton button in keybind.Buttons)
            {
                if (!IsGridEditButtonHeld(button))
                {
                    held = false;
                    break;
                }
            }

            if (held)
                return true;
        }

        return false;
    }

    private bool IsGridEditButtonHeld(SButton button)
    {
        Microsoft.Xna.Framework.Input.MouseState mouse = Microsoft.Xna.Framework.Input.Mouse.GetState();
        return button switch
        {
            SButton.MouseLeft => mouse.LeftButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
            SButton.MouseRight => mouse.RightButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
            SButton.MouseMiddle => mouse.MiddleButton == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
            SButton.MouseX1 => mouse.XButton1 == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
            SButton.MouseX2 => mouse.XButton2 == Microsoft.Xna.Framework.Input.ButtonState.Pressed,
            _ => Helper.Input.IsDown(button)
        };
    }

    private void RefreshGridEditHeldActions()
    {
        bool destroyHeld = IsGridEditKeybindHeld(Config.DestroyTile);
        bool placeHeld = !destroyHeld && IsGridEditKeybindHeld(Config.PlaceTile);

        if (!placeHeld && !destroyHeld)
        {
            gridPlaceHeld = false;
            gridDestroyHeld = false;
            ResetPipeDragPath();
            return;
        }

        PipeActionKind? oldAction = gridDestroyHeld ? PipeActionKind.Destroy : gridPlaceHeld ? PipeActionKind.Place : null;
        PipeActionKind newAction = destroyHeld ? PipeActionKind.Destroy : PipeActionKind.Place;
        if (oldAction != newAction)
            ResetPipeDragPath();

        gridPlaceHeld = placeHeld;
        gridDestroyHeld = destroyHeld;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        Farmer? player = Game1.player;
        GameLocation? location = player?.currentLocation;

        if (Config.EnableMod && Context.IsWorldReady && EditingGrid && player is not null)
        {
            if (Game1.activeClickableMenu is not null)
                Game1.exitActiveMenu();

            if (!TryStowCursorItem(player))
                LeaveGridEditing();
            else
            {
                HideGridEditHudMenus();
                HandleGridEditViewportPan();
                ApplyGridEditViewport();
                player.Halt();
                player.completelyStopAnimatingOrDoingAction();
                RefreshGridEditHeldActions();

                if (location is not null)
                    ProcessGridDrag(location);
                else
                    ResetPipeDragState();

                SuppressGridEditInputs();
                if (gridEditStatusTicks > 0)
                    gridEditStatusTicks--;
                else
                    gridEditStatus = null;
            }
        }
        else if (!EditingGrid)
            RestoreHiddenGridEditMenus();

        if (Config.EnableMod && Context.IsWorldReady && ShowingGrid && location is not null)
        {
            string visibleLocationName = location.NameOrUniqueName;
            EnsureAllGroupsClean(visibleLocationName);
            EnsurePowerCache(visibleLocationName);
        }
    }

    private void ProcessGridDrag(GameLocation location)
    {
        if (!Config.EnableMod || !Context.IsWorldReady || !EditingGrid)
        {
            ResetPipeDragState();
            return;
        }

        PipeActionKind? action = gridDestroyHeld ? PipeActionKind.Destroy : gridPlaceHeld ? PipeActionKind.Place : null;
        if (action is null)
        {
            ResetPipeDragPath();
            return;
        }

        ProcessGridEditAction(location, action.Value);
    }

    private void ProcessGridEditAction(GameLocation location, PipeActionKind action)
    {
        Vector2 tile = GetGridEditCursorTile();
        if (lastDragAction != action)
        {
            ResetPipeDragPath();
            lastDragAction = action;
        }

        if (lastCursorTile == tile)
            return;

        bool anyApplied = false;
        foreach (Vector2 dragTile in GetDragTiles(lastCursorTile, tile))
            anyApplied |= QueuePipeAction(location.NameOrUniqueName, dragTile, CurrentGrid, CurrentTile, CurrentRotation, action);

        lastCursorTile = tile;
    }

    private static IEnumerable<Vector2> GetDragTiles(Vector2 from, Vector2 to)
    {
        int startX = (int)from.X;
        int startY = (int)from.Y;
        int endX = (int)to.X;
        int endY = (int)to.Y;

        if (startX < 0 || startY < 0)
        {
            yield return new Vector2(endX, endY);
            yield break;
        }

        int dx = Math.Abs(endX - startX);
        int dy = Math.Abs(endY - startY);
        int stepX = startX < endX ? 1 : -1;
        int stepY = startY < endY ? 1 : -1;
        int err = dx - dy;
        int x = startX;
        int y = startY;
        bool first = true;

        while (true)
        {
            if (!first)
                yield return new Vector2(x, y);
            first = false;

            if (x == endX && y == endY)
                yield break;

            int doubledError = err * 2;
            if (doubledError > -dy)
            {
                err -= dy;
                x += stepX;
            }
            if (doubledError < dx)
            {
                err += dx;
                y += stepY;
            }
        }
    }

    private bool QueuePipeAction(string locationName, Vector2 tile, GridKind grid, int index, int rotation, PipeActionKind action)
    {
        PipeActionRequest request = new()
        {
            LocationName = locationName,
            X = (int)tile.X,
            Y = (int)tile.Y,
            Grid = grid,
            Index = index,
            Rotation = rotation,
            Action = action,
            FromGridEditMode = EditingGrid
        };

        if (Context.IsMultiplayer && !Context.IsMainPlayer)
        {
            Helper.Multiplayer.SendMessage(request, nameof(PipeActionRequest), new[] { ModId });
            return true;
        }

        Farmer player = Game1.player;
        if (TryApplyPipeAction(request, player, chargePlayer: true, playFeedback: true, out PipeActionBroadcast broadcast))
        {
            BroadcastPipeAction(broadcast);
            return true;
        }

        SetGridEditStatus($"Could not {(action == PipeActionKind.Place ? "place" : "delete")} line at {(int)tile.X}, {(int)tile.Y}.");
        return false;
    }

    private void OnModMessageReceived(object? sender, ModMessageReceivedEventArgs e)
    {
        if (e.FromModID != ModId)
            return;

        if (e.Type == nameof(PipeActionRequest))
        {
            if (!Context.IsMainPlayer)
                return;

            PipeActionRequest request = e.ReadAs<PipeActionRequest>();
            Farmer? farmer = GetOnlineFarmer(e.FromPlayerID);
            if (farmer is null)
                return;

            if (TryApplyPipeAction(request, farmer, chargePlayer: true, playFeedback: false, out PipeActionBroadcast broadcast))
                BroadcastPipeAction(broadcast);
            else
                Helper.Multiplayer.SendMessage(new PipeActionRejection { Message = Helper.Translation.Get("pipe-action-rejected") }, nameof(PipeActionRejection), new[] { ModId }, new[] { e.FromPlayerID });
            return;
        }

        if (e.Type == nameof(PipeActionBroadcast))
        {
            PipeActionBroadcast broadcast = e.ReadAs<PipeActionBroadcast>();
            if (Context.IsMainPlayer)
                return;

            ApplyPipeBroadcast(broadcast, playFeedback: false);
            return;
        }

        if (e.Type == nameof(GridSyncRequest))
        {
            if (!Context.IsMainPlayer)
                return;

            Helper.Multiplayer.SendMessage(new GridSyncMessage { Data = CreateSaveData() }, nameof(GridSyncMessage), new[] { ModId }, new[] { e.FromPlayerID });
            return;
        }

        if (e.Type == nameof(GridSyncMessage))
        {
            if (Context.IsMainPlayer)
                return;

            GridSyncMessage message = e.ReadAs<GridSyncMessage>();
            ApplySaveData(message.Data ?? new UtilitySystemSaveData());
            return;
        }

        if (e.Type == nameof(PipeActionRejection))
        {
            if (Context.IsMainPlayer)
                return;

            PipeActionRejection rejection = e.ReadAs<PipeActionRejection>();
            if (!string.IsNullOrWhiteSpace(rejection.Message))
                Game1.showRedMessage(rejection.Message);
        }
    }

    private static Farmer? GetOnlineFarmer(long multiplayerId)
    {
        if (Game1.player.UniqueMultiplayerID == multiplayerId)
            return Game1.player;

        foreach (Farmer farmer in Game1.getOnlineFarmers())
        {
            if (farmer.UniqueMultiplayerID == multiplayerId)
                return farmer;
        }
        return null;
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Config.EnableMod || !ShowingGrid || Game1.activeClickableMenu != null || pipeTexture is null)
            return;

        GameLocation location = Game1.player.currentLocation;
        string locationName = location.NameOrUniqueName;
        if (!Systems.ContainsKey(locationName))
            return;

        Dictionary<Vector2, GridPipe> pipes = Systems[locationName][CurrentGrid].Pipes;
        List<PipeGroup> groups = Systems[locationName][CurrentGrid].Groups;
        Dictionary<Vector2, UtilityObjectInstance> objects = Systems[locationName][CurrentGrid].Objects;
        Color color = CurrentGrid == GridKind.Power ? Config.PowerColor : Config.WaterColor;

        foreach (PipeGroup group in groups)
        {
            Vector2 power = group.PowerVector;
            Vector2 storage = group.StorageVector;
            bool powered = power.X > 0 && power.X + power.Y + storage.X >= 0;
            foreach (Vector2 pipe in group.Pipes)
            {
                if (!pipes.TryGetValue(pipe, out GridPipe? gridPipe))
                    continue;
                if (Utility.isOnScreen(pipe * Game1.tileSize + new Vector2(Game1.tileSize / 2f), Game1.tileSize / 2))
                {
                    GridPipe drawPipe = gridPipe.Index == 4 ? new GridPipe { Index = 1, Rotation = 2 } : gridPipe;
                    DrawPipe(e.SpriteBatch, pipe, drawPipe, powered ? color : Config.UnpoweredGridColor);
                }
            }
        }

        foreach (KeyValuePair<Vector2, UtilityObjectInstance> obj in objects)
        {
            DrawAmount(e.SpriteBatch, location, obj, color);
            DrawCharge(e.SpriteBatch, obj, color);
        }

        if (Config.ShowWateredTileMarkers && Config.EnablePipeIrrigation && CurrentGrid == GridKind.Water && dropTexture is not null && WateringTiles.TryGetValue(locationName, out List<Vector2>? wateredTiles))
        {
            foreach (Vector2 tile in wateredTiles)
            {
                Rectangle source = new(0, 0, dropTexture.Width, dropTexture.Height);
                dropCenterOffset ??= GetCenteredTextureOffset(dropTexture, source);
                e.SpriteBatch.Draw(dropTexture, Game1.GlobalToLocal(Game1.viewport, tile * Game1.tileSize + dropCenterOffset.Value), Color.White);
            }
        }

        if (EditingGrid)
            DrawPlacementPreview(e.SpriteBatch, locationName, color);
    }


    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Config.EnableMod || !Context.IsWorldReady || !EditingGrid)
            return;

        DrawGridEditHud(e.SpriteBatch);
    }

    private void SetGridEditStatus(string status)
    {
        gridEditStatus = status;
        gridEditStatusTicks = 180;
    }

    private void DrawGridEditHud(SpriteBatch b)
    {
        string text = $"Place: {Config.PlaceTile}   Delete: {Config.DestroyTile}   |   Switch grid: {Config.SwitchGrid}   Shape: {Config.SwitchTile}   Rotate: {Config.RotateTile}   |   Pan: WASD/arrows/edge   Exit: Esc";
        int padX = 16;
        int padY = 10;
        int maxWidth = Math.Max(320, Game1.uiViewport.Width - 64);
        Vector2 textSize = Game1.smallFont.MeasureString(text);
        float scale = Math.Min(1f, (maxWidth - padX * 2) / Math.Max(1f, textSize.X));
        int boxWidth = Math.Min(maxWidth, (int)Math.Ceiling(textSize.X * scale) + padX * 2);
        int boxHeight = (int)Math.Ceiling(Game1.smallFont.LineSpacing * scale) + padY * 2;
        int boxX = (Game1.uiViewport.Width - boxWidth) / 2;
        int boxY = Game1.uiViewport.Height - boxHeight - 24;

        Rectangle box = new(boxX, boxY, boxWidth, boxHeight);
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), box.X, box.Y, box.Width, box.Height, Color.White, 1f, false);

        Vector2 pos = new(box.X + padX, box.Y + padY);
        b.DrawString(Game1.smallFont, text, pos + new Vector2(2f, 2f), Color.Black * 0.35f, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        b.DrawString(Game1.smallFont, text, pos, Game1.textColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
    }

    private void OnObjectListChanged(object? sender, ObjectListChangedEventArgs e)
    {
        if (!Config.EnableMod)
            return;

        string locationName = e.Location.NameOrUniqueName;
        if (!Systems.ContainsKey(locationName))
            return;

        MarkAllGroupsDirty(locationName);
        EnsureAllGroupsClean(locationName);
        RecalculateLocationPower(locationName);
        if (Config.EnablePipeIrrigation && CanPipeIrrigateLocation(e.Location))
            RefreshWateringTiles(e.Location, false);
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!Config.EnableMod || !e.IsLocalPlayer)
            return;

        EnsureLocation(e.NewLocation.NameOrUniqueName);
        EnsureAllGroupsClean(e.NewLocation.NameOrUniqueName);
        RecalculateLocationPower(e.NewLocation.NameOrUniqueName);
        if (Config.EnablePipeIrrigation && CanPipeIrrigateLocation(e.NewLocation))
            RefreshWateringTiles(e.NewLocation, false);
    }

    private void DrawPlacementPreview(SpriteBatch b, string locationName, Color color)
    {
        if (pipeTexture is null)
            return;

        GridPipe preview = CurrentTile == 4 ? new GridPipe { Index = 1, Rotation = 2 } : new GridPipe { Index = CurrentTile, Rotation = CurrentRotation };
        Vector2 cursor = GetGridEditCursorTile();
        DrawPipe(b, cursor + new Vector2(-2, 2) / Game1.tileSize, preview, Config.ShadowColor);
        DrawPipe(b, cursor + new Vector2(-2, -2) / Game1.tileSize, preview, Config.ShadowColor);
        DrawPipe(b, cursor + new Vector2(2, -2) / Game1.tileSize, preview, Config.ShadowColor);
        DrawPipe(b, cursor + new Vector2(2, 2) / Game1.tileSize, preview, Config.ShadowColor);
        DrawPipe(b, cursor, preview, color);
    }

    private bool TryApplyPipeAction(PipeActionRequest request, Farmer player, bool chargePlayer, bool playFeedback, out PipeActionBroadcast broadcast)
    {
        broadcast = new PipeActionBroadcast
        {
            LocationName = request.LocationName,
            X = request.X,
            Y = request.Y,
            Grid = request.Grid,
            Index = request.Index,
            Rotation = request.Rotation,
            Action = request.Action
        };

        if (!ValidatePipeActionRequest(request, player))
            return false;

        EnsureLocation(request.LocationName);
        Dictionary<Vector2, GridPipe> pipes = Systems[request.LocationName][request.Grid].Pipes;
        Vector2 tile = new(request.X, request.Y);

        if (request.Action == PipeActionKind.Destroy)
        {
            if (!pipes.ContainsKey(tile))
                return false;

            pipes.Remove(tile);
            if (chargePlayer)
                GivePipeRefund(player);
            AfterPipeGridChanged(request.LocationName, request.Grid, playFeedback ? Config.DestroySound : null);
            return true;
        }

        if (request.Action != PipeActionKind.Place)
            return false;

        bool replacing = pipes.ContainsKey(tile);
        if (chargePlayer && !CanPayForPipe(player))
            return false;

        if (chargePlayer)
        {
            ChargeForPipe(player);
            if (replacing)
                GivePipeRefund(player);
        }

        pipes[tile] = new GridPipe { Index = request.Index, Rotation = request.Rotation };
        AfterPipeGridChanged(request.LocationName, request.Grid, playFeedback ? Config.PipeSound : null);
        return true;
    }

    private void BroadcastPipeAction(PipeActionBroadcast broadcast)
    {
        if (Context.IsMultiplayer && Context.IsMainPlayer)
            Helper.Multiplayer.SendMessage(broadcast, nameof(PipeActionBroadcast), new[] { ModId });
    }

    private void ApplyPipeBroadcast(PipeActionBroadcast broadcast, bool playFeedback)
    {
        EnsureLocation(broadcast.LocationName);
        Dictionary<Vector2, GridPipe> pipes = Systems[broadcast.LocationName][broadcast.Grid].Pipes;
        Vector2 tile = new(broadcast.X, broadcast.Y);
        if (broadcast.Action == PipeActionKind.Destroy)
            pipes.Remove(tile);
        else if (broadcast.Action == PipeActionKind.Place)
            pipes[tile] = new GridPipe { Index = broadcast.Index, Rotation = broadcast.Rotation };

        AfterPipeGridChanged(broadcast.LocationName, broadcast.Grid, playFeedback ? broadcast.Action == PipeActionKind.Destroy ? Config.DestroySound : Config.PipeSound : null);
    }

    private void AfterPipeGridChanged(string locationName, GridKind grid, string? sound)
    {
        MarkGroupsDirty(locationName, grid);
        EnsureGroupsClean(locationName, grid);
        RecalculateLocationPower(locationName);
        GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(locationName, true);
        if (location is not null && Config.EnablePipeIrrigation && CanPipeIrrigateLocation(location))
            RefreshWateringTiles(location, false);
        if (!string.IsNullOrWhiteSpace(sound))
            PlayLocationSound(sound);
    }

    private void DrawPipe(SpriteBatch b, Vector2 tile, GridPipe pipe, Color color)
    {
        if (pipeTexture is null)
            return;

        float layerDepth = (tile.Y * Game1.tileSize + Game1.tileSize) / 10000f;
        Rectangle source = new(pipe.Rotation * Game1.tileSize, pipe.Index * Game1.tileSize, Game1.tileSize, Game1.tileSize);
        b.Draw(pipeTexture, Game1.GlobalToLocal(Game1.viewport, tile * Game1.tileSize), source, color, 0f, Vector2.Zero, 1f, SpriteEffects.None, layerDepth);
    }

    private static Vector2 GetCenteredTextureOffset(Texture2D texture, Rectangle source)
    {
        Color[] pixels = new Color[source.Width * source.Height];
        texture.GetData(0, source, pixels, 0, pixels.Length);

        int minX = source.Width;
        int minY = source.Height;
        int maxX = -1;
        int maxY = -1;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (pixels[y * source.Width + x].A == 0)
                    continue;

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        if (maxX < minX || maxY < minY)
            return Vector2.Zero;

        float width = maxX - minX + 1;
        float height = maxY - minY + 1;
        return new Vector2((source.Width - width) / 2f - minX, (source.Height - height) / 2f - minY);
    }

    private void DrawAmount(SpriteBatch b, GameLocation location, KeyValuePair<Vector2, UtilityObjectInstance> pair, Color color)
    {
        float amount = CurrentGrid == GridKind.Power ? pair.Value.Template.Power : pair.Value.Template.Water;
        if (amount == 0)
            return;

        bool enough = IsObjectPowered(location.NameOrUniqueName, pair.Key, pair.Value.Template);
        bool active = IsObjectWorking(location, pair.Value) && (amount > 0 || !pair.Value.Template.MustNeedOther || IsObjectNeeded(location, pair.Value, CurrentGrid));
        Color drawColor = active ? enough ? color : Config.InsufficientColor : Config.IdleColor;
        string text = Math.Round(amount).ToString(CultureInfo.InvariantCulture);
        Vector2 pos = Game1.GlobalToLocal(Game1.viewport, pair.Key * Game1.tileSize);
        DrawOutlinedText(b, text, Game1.dialogueFont, pos, drawColor, 1f);
    }

    private static void DrawCharge(SpriteBatch b, KeyValuePair<Vector2, UtilityObjectInstance> pair, Color color)
    {
        int capacity = CurrentGrid == GridKind.Power ? pair.Value.Template.PowerChargeCapacity : pair.Value.Template.WaterChargeCapacity;
        if (capacity <= 0)
            return;

        string chargeKey = CurrentGrid == GridKind.Power ? PowerChargeKey : WaterChargeKey;
        float charge = GetFloat(pair.Value.WorldObject.modData.TryGetValue(chargeKey, out string? raw) ? raw : null);
        string text = Math.Round(charge, 1).ToString(CultureInfo.InvariantCulture) + "\n" + capacity;
        Vector2 pos = Game1.GlobalToLocal(Game1.viewport, pair.Key * Game1.tileSize) + new Vector2(16, -16);
        DrawOutlinedText(b, text, Game1.dialogueFont, pos, color, 0.5f);
    }

    private static void DrawOutlinedText(SpriteBatch b, string text, SpriteFont font, Vector2 pos, Color color, float scale)
    {
        b.DrawString(font, text, pos + new Vector2(-1, 1), Config.ShadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.999999f);
        b.DrawString(font, text, pos + new Vector2(1, -1), Config.ShadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.999999f);
        b.DrawString(font, text, pos + new Vector2(-1, -1), Config.ShadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.999999f);
        b.DrawString(font, text, pos + new Vector2(1, 1), Config.ShadowColor, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.999999f);
        b.DrawString(font, text, pos, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0.9999999f);
    }

    private static UtilitySystemSaveData CreateSaveData()
    {
        UtilitySystemSaveData saveData = new();
        foreach ((string locationName, Dictionary<GridKind, UtilitySystem> grids) in Systems)
        {
            if (grids[GridKind.Water].Pipes.Count == 0 && grids[GridKind.Power].Pipes.Count == 0)
                continue;

            saveData.Locations[locationName] = new LocationGridSaveData(grids[GridKind.Water].Pipes, grids[GridKind.Power].Pipes);
        }
        return saveData;
    }

    private static void ApplySaveData(UtilitySystemSaveData saveData)
    {
        Systems.Clear();

        foreach ((string locationName, LocationGridSaveData locationData) in saveData.Locations)
        {
            EnsureLocation(locationName);
            foreach (int[] pipe in locationData.WaterPipes)
            {
                if (pipe.Length >= 4)
                    Systems[locationName][GridKind.Water].Pipes[new Vector2(pipe[0], pipe[1])] = new GridPipe { Index = pipe[2], Rotation = pipe[3] };
            }
            foreach (int[] pipe in locationData.PowerPipes)
            {
                if (pipe.Length >= 4)
                    Systems[locationName][GridKind.Power].Pipes[new Vector2(pipe[0], pipe[1])] = new GridPipe { Index = pipe[2], Rotation = pipe[3] };
            }
            MarkAllGroupsDirty(locationName);
            EnsureAllGroupsClean(locationName);
            RecalculateLocationPower(locationName);
        }
    }

    private static bool CanPipeIrrigateLocation(GameLocation location)
    {
        return location.terrainFeatures.Pairs.Any(pair => pair.Value is HoeDirt);
    }

    private static void EnsureLocation(string locationName)
    {
        if (Systems.ContainsKey(locationName))
            return;

        Systems[locationName] = new Dictionary<GridKind, UtilitySystem>
        {
            [GridKind.Water] = new(),
            [GridKind.Power] = new()
        };
    }

    private static void MarkAllGroupsDirty(string locationName)
    {
        EnsureLocation(locationName);
        foreach (GridKind grid in Enum.GetValues<GridKind>())
            MarkGroupsDirty(locationName, grid);
    }

    private static void MarkGroupsDirty(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        Systems[locationName][grid].GroupsDirty = true;
        Systems[locationName][grid].PowerCacheVersion = -1;
    }

    private static void MarkPowerCacheDirty(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        Systems[locationName][grid].PowerCacheVersion = -1;
    }

    private static void EnsureAllGroupsClean(string locationName)
    {
        EnsureLocation(locationName);
        foreach (GridKind grid in Enum.GetValues<GridKind>())
            EnsureGroupsClean(locationName, grid);
    }

    private static void EnsureGroupsClean(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        UtilitySystem system = Systems[locationName][grid];
        if (!system.GroupsDirty)
            return;

        RebuildGroups(locationName, grid);
        system.GroupsDirty = false;
        system.PowerCacheVersion = -1;
    }

    private static void AddObjectsToGrid(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        UtilitySystem system = Systems[locationName][grid];
        system.Objects.Clear();

        GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(locationName, true);
        if (location is null)
            return;

        foreach (KeyValuePair<Vector2, Object> pair in location.Objects.Pairs)
        {
            if (!ObjectRules.TryGetValue(GetRuleKey(pair.Value), out UtilityObjectRule? rule))
                continue;

            PipeGroup? group = system.Groups.FirstOrDefault(candidate => candidate.PipeSet.Contains(pair.Key));
            if (group is null)
                continue;

            UtilityObjectInstance instance = new(rule, pair.Value)
            {
                Group = group
            };
            system.Objects[pair.Key] = instance;
        }
    }

    private static void RebuildGroups(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        UtilitySystem system = Systems[locationName][grid];
        HashSet<Vector2> remaining = new(system.Pipes.Keys);
        system.Groups.Clear();

        while (remaining.Count > 0)
        {
            Vector2 start = remaining.First();
            remaining.Remove(start);
            PipeGroup group = new();
            Stack<Vector2> stack = new();
            stack.Push(start);

            while (stack.Count > 0)
            {
                Vector2 tile = stack.Pop();
                group.AddPipe(tile);

                foreach (Vector2 adjacent in AdjacentTiles.Select(offset => tile + offset))
                {
                    if (!remaining.Contains(adjacent) || !PipesAreJoined(locationName, tile, adjacent, grid))
                        continue;

                    remaining.Remove(adjacent);
                    stack.Push(adjacent);
                }
            }

            system.Groups.Add(group);
        }

        AddObjectsToGrid(locationName, grid);
    }

    private static void RecalculateLocationPower(string locationName)
    {
        EnsureLocation(locationName);
        EnsurePowerCache(locationName);
    }

    private static void EnsurePowerCache(string locationName)
    {
        EnsureLocation(locationName);
        EnsureAllGroupsClean(locationName);
        foreach (GridKind grid in Enum.GetValues<GridKind>())
            EnsurePowerCache(locationName, grid);
    }

    private static void EnsurePowerCache(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        UtilitySystem system = Systems[locationName][grid];
        if (system.PowerCacheVersion == PowerCacheVersion)
            return;

        foreach (PipeGroup group in system.Groups)
        {
            group.PowerVector = CalculateGroupPower(locationName, group, grid, new HashSet<(string LocationName, GridKind Grid, PipeGroup Group)>());
            group.StorageVector = CalculateGroupStoragePower(locationName, group, grid);
        }
        system.PowerCacheVersion = PowerCacheVersion;
    }

    private static bool ValidatePipeActionRequest(PipeActionRequest request, Farmer player)
    {
        if (!Config.EnableMod || player is null || request.LocationName is null)
            return false;

        if (!Enum.IsDefined(typeof(GridKind), request.Grid) || !Enum.IsDefined(typeof(PipeActionKind), request.Action))
            return false;

        if (request.Index < 0 || request.Index >= IntakeArray.Length)
            return false;

        int maxRotation = request.Index == 1 ? 1 : request.Index == 4 ? 0 : 3;
        if (request.Rotation < 0 || request.Rotation > maxRotation)
            return false;

        GameLocation? requestedLocation = Game1.getLocationFromName(request.LocationName) ?? Game1.getLocationFromName(request.LocationName, true);
        if (requestedLocation is null || player.currentLocation is null || !string.Equals(player.currentLocation.NameOrUniqueName, request.LocationName, StringComparison.Ordinal))
            return false;

        Vector2 tile = new(request.X, request.Y);
        if (!IsTileWithinMap(requestedLocation, tile))
            return false;

        if (!request.FromGridEditMode)
        {
            Vector2 playerTile = player.Tile;
            if (Vector2.Distance(playerTile, tile) > 16f)
                return false;
        }

        EnsureLocation(request.LocationName);
        Dictionary<Vector2, GridPipe> pipes = Systems[request.LocationName][request.Grid].Pipes;
        if (request.Action == PipeActionKind.Destroy && !pipes.ContainsKey(tile))
            return false;

        return true;
    }

    private static bool IsTileWithinMap(GameLocation location, Vector2 tile)
    {
        if (tile.X < 0 || tile.Y < 0)
            return false;

        try
        {
            return tile.X < location.Map.Layers[0].LayerWidth && tile.Y < location.Map.Layers[0].LayerHeight;
        }
        catch
        {
            return false;
        }
    }

    private static bool PipesAreJoined(string locationName, Vector2 tile, Vector2 other, GridKind grid)
    {
        if (!Systems.TryGetValue(locationName, out Dictionary<GridKind, UtilitySystem>? grids))
            return false;

        Dictionary<Vector2, GridPipe> pipes = grids[grid].Pipes;
        if (!pipes.ContainsKey(tile) || !pipes.ContainsKey(other))
            return false;

        if (other.X == tile.X)
        {
            if (tile.Y == other.Y + 1)
                return HasIntake(pipes[tile], 0) && HasIntake(pipes[other], 2);
            if (tile.Y == other.Y - 1)
                return HasIntake(pipes[tile], 2) && HasIntake(pipes[other], 0);
        }
        else if (other.Y == tile.Y)
        {
            if (tile.X == other.X + 1)
                return HasIntake(pipes[tile], 3) && HasIntake(pipes[other], 1);
            if (tile.X == other.X - 1)
                return HasIntake(pipes[tile], 1) && HasIntake(pipes[other], 3);
        }

        return false;
    }

    private static bool HasIntake(GridPipe pipe, int direction)
    {
        int index = Math.Clamp(pipe.Index, 0, IntakeArray.Length - 1);
        return IntakeArray[index][(direction + pipe.Rotation) % 4] == 1;
    }

    internal static List<List<Vector2>> GetLocationPipeGroups(GameLocation location, GridKind grid)
    {
        string locationName = location.NameOrUniqueName;
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        return Systems[locationName][grid].Groups.Select(group => group.Pipes.ToList()).ToList();
    }

    internal static Vector2 GetTileGroupVector(GameLocation location, Vector2 tile, GridKind grid)
    {
        string locationName = location.NameOrUniqueName;
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        EnsurePowerCache(locationName, grid);
        foreach (PipeGroup group in Systems[locationName][grid].Groups)
        {
            if (group.PipeSet.Contains(tile))
                return group.PowerVector + new Vector2(group.StorageVector.X, 0f);
        }
        return Vector2.Zero;
    }

    internal static List<Vector2> GetTileGroupObjects(GameLocation location, Vector2 tile, GridKind grid)
    {
        string locationName = location.NameOrUniqueName;
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        foreach (PipeGroup group in Systems[locationName][grid].Groups)
        {
            if (!group.PipeSet.Contains(tile))
                continue;
            return Systems[locationName][grid].Objects.Keys.Where(group.PipeSet.Contains).ToList();
        }
        return new List<Vector2>();
    }

    private static Vector2 GetTilePower(string locationName, Vector2 tile, GridKind grid)
    {
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        EnsurePowerCache(locationName, grid);
        UtilitySystem system = Systems[locationName][grid];
        if (!system.Objects.TryGetValue(tile, out UtilityObjectInstance? obj) || obj.Group is null)
            return Vector2.Zero;

        Vector2 power = obj.Group.PowerVector;
        power.X += obj.Group.StorageVector.X;
        return power;
    }

    private static Vector2 CalculateGroupPower(string locationName, PipeGroup group, GridKind grid, HashSet<(string LocationName, GridKind Grid, PipeGroup Group)> visiting)
    {
        if (!visiting.Add((locationName, grid, group)))
            return Vector2.Zero;

        Vector2 power = Vector2.Zero;
        foreach (Vector2 tile in group.Pipes)
        {
            if (!Systems[locationName][grid].Objects.TryGetValue(tile, out UtilityObjectInstance? obj))
                continue;

            UtilityObjectRule rule = obj.Template;
            if (grid == GridKind.Water && rule.Power < 0)
            {
                Vector2 powerGridPower = GetCachedTilePower(locationName, tile, GridKind.Power, visiting);
                if (powerGridPower.X == 0 || powerGridPower.X + powerGridPower.Y < 0)
                    continue;
            }
            power += GetPowerVector(locationName, obj, grid == GridKind.Water ? rule.Water : rule.Power);
        }

        foreach ((string linkedLocationName, PipeGroup linkedGroup) in GetLinkedBuildingGroups(locationName, group, grid))
            power += CalculateGroupPower(linkedLocationName, linkedGroup, grid, visiting);

        visiting.Remove((locationName, grid, group));
        return power;
    }

    private static Vector2 GetCachedTilePower(string locationName, Vector2 tile, GridKind grid, HashSet<(string LocationName, GridKind Grid, PipeGroup Group)> visiting)
    {
        EnsureGroupsClean(locationName, grid);
        UtilitySystem system = Systems[locationName][grid];
        if (!system.Objects.TryGetValue(tile, out UtilityObjectInstance? obj) || obj.Group is null)
            return Vector2.Zero;

        if (system.PowerCacheVersion != PowerCacheVersion)
        {
            obj.Group.PowerVector = CalculateGroupPower(locationName, obj.Group, grid, visiting);
            obj.Group.StorageVector = CalculateGroupStoragePower(locationName, obj.Group, grid);
        }

        Vector2 power = obj.Group.PowerVector;
        power.X += obj.Group.StorageVector.X;
        return power;
    }

    private static Vector2 CalculateGroupStoragePower(string locationName, PipeGroup group, GridKind grid)
    {
        return CalculateGroupStoragePower(locationName, group, grid, new HashSet<(string LocationName, GridKind Grid, PipeGroup Group)>());
    }

    private static Vector2 CalculateGroupStoragePower(string locationName, PipeGroup group, GridKind grid, HashSet<(string LocationName, GridKind Grid, PipeGroup Group)> visiting)
    {
        if (!visiting.Add((locationName, grid, group)))
            return Vector2.Zero;

        string chargeKey = grid == GridKind.Water ? WaterChargeKey : PowerChargeKey;
        Vector2 power = Vector2.Zero;
        foreach (Vector2 tile in Systems[locationName][grid].Objects.Keys.ToArray())
        {
            if (!group.PipeSet.Contains(tile))
                continue;

            UtilityObjectInstance obj = Systems[locationName][grid].Objects[tile];
            UtilityObjectRule rule = obj.Template;
            int capacity = grid == GridKind.Water ? rule.WaterChargeCapacity : rule.PowerChargeCapacity;
            if (capacity == 0)
                continue;

            float charge = GetFloat(obj.WorldObject.modData.TryGetValue(chargeKey, out string? raw) ? raw : null);
            power.X += Math.Min(charge, grid == GridKind.Water ? rule.WaterDischargeRate : rule.PowerDischargeRate);
            power.Y -= Math.Min(capacity - charge, grid == GridKind.Water ? rule.WaterChargeRate : rule.PowerChargeRate);
        }

        foreach ((string linkedLocationName, PipeGroup linkedGroup) in GetLinkedBuildingGroups(locationName, group, grid))
            power += CalculateGroupStoragePower(linkedLocationName, linkedGroup, grid, visiting);

        visiting.Remove((locationName, grid, group));
        return power;
    }

    private static void ChangeStorageObjects(string locationName, PipeGroup group, GridKind grid, float hours)
    {
        float netPower = group.PowerVector.X + group.PowerVector.Y;
        if (group.StorageVector.X + netPower < 0)
            return;

        GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(locationName, true);
        if (location is null)
            return;

        string chargeKey = grid == GridKind.Water ? WaterChargeKey : PowerChargeKey;
        bool changed = false;
        Dictionary<Vector2, float> changeObjects = new();
        foreach (Vector2 tile in Systems[locationName][grid].Objects.Keys.ToArray())
        {
            if (!group.PipeSet.Contains(tile))
                continue;

            UtilityObjectInstance obj = Systems[locationName][grid].Objects[tile];
            UtilityObjectRule rule = obj.Template;
            int capacity = grid == GridKind.Water ? rule.WaterChargeCapacity : rule.PowerChargeCapacity;
            if (capacity == 0)
                continue;

            float charge = GetFloat(obj.WorldObject.modData.TryGetValue(chargeKey, out string? raw) ? raw : null);
            if (grid == GridKind.Water && hours > 0 && rule.FillWaterFromRain && location.IsOutdoors && IsRainingHere(location))
            {
                charge = Math.Min(charge + rule.WaterChargeRate * hours, rule.WaterChargeCapacity);
                obj.WorldObject.modData[chargeKey] = FormatFloat(charge);
                changed = true;
            }

            if (netPower > 0)
                changeObjects[tile] = Math.Min(capacity - charge, grid == GridKind.Water ? rule.WaterChargeRate : rule.PowerChargeRate);
            else if (netPower < 0)
                changeObjects[tile] = Math.Min(charge, grid == GridKind.Water ? rule.WaterDischargeRate : rule.PowerDischargeRate);
        }

        while (changeObjects.Count > 0 && netPower != 0)
        {
            float eachPower = netPower / changeObjects.Count;
            foreach (Vector2 tile in changeObjects.Keys.ToArray())
            {
                Object obj = Systems[locationName][grid].Objects[tile].WorldObject;
                UtilityObjectRule rule = Systems[locationName][grid].Objects[tile].Template;
                float currentCharge = GetFloat(obj.modData.TryGetValue(chargeKey, out string? raw) ? raw : null);
                float diff = changeObjects[tile];
                if (eachPower > 0)
                {
                    int capacity = grid == GridKind.Water ? rule.WaterChargeCapacity : rule.PowerChargeCapacity;
                    float add = Math.Min(capacity - currentCharge, Math.Min(diff, eachPower));
                    if (hours > 0)
                    {
                        obj.modData[chargeKey] = FormatFloat(Math.Min(capacity, currentCharge + add * hours));
                        changed = true;
                    }
                    changeObjects[tile] -= add;
                    if (add != eachPower)
                        changeObjects.Remove(tile);
                }
                else
                {
                    float subtract = Math.Min(currentCharge, Math.Min(diff, -eachPower));
                    if (hours > 0)
                    {
                        obj.modData[chargeKey] = FormatFloat(Math.Max(0, currentCharge - subtract * hours));
                        changed = true;
                    }
                    changeObjects[tile] -= subtract;
                    if (subtract != -eachPower)
                        changeObjects.Remove(tile);
                }
            }
        }

        if (changed)
            MarkPowerCacheDirty(locationName, grid);
    }

    private static Vector2 GetPowerVector(string locationName, UtilityObjectInstance obj, float amount)
    {
        GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(locationName, true);
        if (location is null || amount == 0 || !IsObjectWorking(location, obj))
            return Vector2.Zero;

        return amount > 0 ? new Vector2(amount, 0) : new Vector2(0, amount);
    }

    private static bool IsRainingHere(GameLocation location)
    {
        return Game1.IsRainingHere(location);
    }

    private static bool IsLightningHere(GameLocation location)
    {
        return Game1.IsLightningHere(location);
    }

    private static bool IsObjectWorking(GameLocation location, UtilityObjectInstance obj)
    {
        UtilityObjectRule rule = obj.Template;
        Object worldObject = obj.WorldObject;
        return ConditionsAllowObject(location, worldObject, rule)
            && (!worldObject.IsSprinkler() || Game1.timeOfDay == 600)
            && (!rule.MustBeFull || worldObject.heldObject.Value != null)
            && (!rule.MustBeWorking || worldObject.MinutesUntilReady > 0);
    }

    private static bool IsObjectNeeded(GameLocation location, UtilityObjectInstance obj, GridKind checkGrid)
    {
        GridKind otherGrid = checkGrid == GridKind.Water ? GridKind.Power : GridKind.Water;
        string locationName = location.NameOrUniqueName;
        EnsureLocation(locationName);
        foreach (PipeGroup group in Systems[locationName][otherGrid].Groups)
        {
            if (group.PipeSet.Contains(obj.WorldObject.TileLocation))
                return group.PowerVector.Y < 0 || group.StorageVector.Y < 0;
        }
        return false;
    }

    private static bool IsObjectPowered(string locationName, Vector2 tile, UtilityObjectRule rule)
    {
        Vector2 waterPower = GetTilePower(locationName, tile, GridKind.Water);
        Vector2 powerGridPower = GetTilePower(locationName, tile, GridKind.Power);
        if (rule.Water < 0 && (waterPower == Vector2.Zero || waterPower.X + waterPower.Y < 0))
            return false;
        if (rule.Power < 0 && (powerGridPower == Vector2.Zero || powerGridPower.X + powerGridPower.Y < 0))
            return false;
        return true;
    }

    private static bool ObjectNeedsPower(UtilityObjectRule rule)
    {
        return rule.Water < 0 || rule.Power < 0;
    }

    private static UtilityObjectInstance? GetUtilityObjectAtTile(GameLocation location, Vector2 tile)
    {
        if (!location.Objects.TryGetValue(tile, out Object? obj))
            return null;

        string key = GetRuleKey(obj);
        if (!ObjectRules.TryGetValue(key, out UtilityObjectRule? rule))
            return null;

        return new UtilityObjectInstance(rule, obj);
    }

    private static string GetRuleKey(Object obj)
    {
        string qualifiedId = obj.QualifiedItemId;
        if (qualifiedId.StartsWith("(BC)", StringComparison.Ordinal))
        {
            string itemId = qualifiedId.Substring(4);
            if (ObjectRules.ContainsKey(itemId))
                return itemId;
        }

        if (ObjectRules.ContainsKey(qualifiedId))
            return qualifiedId;

        return obj.Name;
    }

    private static void RefreshWateringTiles(GameLocation location, bool use)
    {
        string locationName = location.NameOrUniqueName;
        WateringPipes[locationName] = new List<Vector2>();
        WateringTiles[locationName] = new List<Vector2>();
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, GridKind.Water);
        EnsurePowerCache(locationName, GridKind.Water);

        foreach (PipeGroup group in Systems[locationName][GridKind.Water].Groups)
        {
            if (group.Pipes.Count == 0)
                continue;

            Vector2 power = group.PowerVector;
            power.X += group.StorageVector.X;
            float netExcess = power.X + power.Y;
            List<Vector2> pipeList = new();
            List<Vector2> hoeDirtList = new();

            foreach (Vector2 pipe in group.Pipes)
            {
                if (Config.WaterSurroundingTiles)
                {
                    foreach (Vector2 offset in WateringOffsets)
                    {
                        Vector2 tile = pipe + offset;
                        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) && feature is HoeDirt && !hoeDirtList.Contains(tile))
                        {
                            if (!pipeList.Contains(pipe))
                                pipeList.Add(pipe);
                            hoeDirtList.Add(tile);
                        }
                    }
                }
                else if (location.terrainFeatures.TryGetValue(pipe, out TerrainFeature? feature) && feature is HoeDirt && !hoeDirtList.Contains(pipe))
                {
                    pipeList.Add(pipe);
                    hoeDirtList.Add(pipe);
                }
            }

            bool enough = hoeDirtList.Count * Config.PercentWaterPerTile <= netExcess;
            if (!enough)
            {
                float lacking = hoeDirtList.Count * Config.PercentWaterPerTile - netExcess;
                foreach (Vector2 objTile in Systems[locationName][GridKind.Water].Objects.Keys.Where(group.PipeSet.Contains).ToArray())
                {
                    if (!location.Objects.TryGetValue(objTile, out Object? obj) || !obj.modData.ContainsKey(WaterChargeKey))
                        continue;

                    float charge = GetFloat(obj.modData[WaterChargeKey]);
                    float required = Math.Min(charge, lacking);
                    lacking -= required;
                    if (use)
                    {
                        obj.modData[WaterChargeKey] = FormatFloat(charge - required);
                        MarkPowerCacheDirty(locationName, GridKind.Water);
                    }
                    if (lacking <= 0)
                    {
                        enough = true;
                        break;
                    }
                }
            }

            if (enough)
            {
                WateringPipes[locationName].AddRange(pipeList);
                WateringTiles[locationName].AddRange(hoeDirtList);
            }
        }
    }

    private static void WaterLocationFromPipes(GameLocation location, bool use)
    {
        RefreshWateringTiles(location, use);
        string locationName = location.NameOrUniqueName;
        if (!WateringPipes.TryGetValue(locationName, out List<Vector2>? pipes))
            return;

        foreach (Vector2 pipe in pipes)
        {
            if (Config.WaterSurroundingTiles)
            {
                foreach (Vector2 offset in WateringOffsets)
                    WaterTile(location, pipe + offset);

                if (Config.ShowSprinklerAnimations)
                {
                    location.temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 1984, 192, 192), 60f, 3, 100, pipe * Game1.tileSize + new Vector2(-64f, -64f), false, false)
                    {
                        color = Color.White * 0.4f,
                        delayBeforeAnimationStart = Game1.random.Next(1000),
                        id = (int)(pipe.X * 4000f + pipe.Y)
                    });
                }
            }
            else
            {
                WaterTile(location, pipe);
                if (Config.ShowSprinklerAnimations)
                {
                    location.temporarySprites.Add(new TemporaryAnimatedSprite(29, pipe * Game1.tileSize + new Vector2(0f, -48f), Color.White * 0.5f, 4, false, 60f, 100, -1, -1f, -1, 0)
                    {
                        delayBeforeAnimationStart = Game1.random.Next(1000),
                        id = (int)(pipe.X * 4000f + pipe.Y)
                    });
                }
            }
        }
    }

    private static void WaterTile(GameLocation location, Vector2 tile)
    {
        if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature? feature) && feature is HoeDirt dirt)
        {
            dirt.Pot?.Water();
            dirt.state.Value = HoeDirt.watered;
        }
    }

    private bool CanPayForPipe(Farmer player)
    {
        if (Config.PipeCostGold > 0 && player.Money < Config.PipeCostGold)
        {
            if (player.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                Game1.showRedMessage(Helper.Translation.Get("not-enough-money"));
            return false;
        }

        foreach ((string itemId, int amount) in ParseItemCosts(Config.PipeCostItems))
        {
            if (player.Items.Sum(item => item?.QualifiedItemId == itemId ? item.Stack : 0) < amount)
            {
                if (player.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
                    Game1.showRedMessage(Helper.Translation.Get("not-enough-materials"));
                return false;
            }
        }
        return true;
    }

    private static void ChargeForPipe(Farmer player)
    {
        player.Money -= Config.PipeCostGold;
        foreach ((string itemId, int amount) in ParseItemCosts(Config.PipeCostItems))
            RemoveItems(player, itemId, amount);
    }

    private static void GivePipeRefund(Farmer player)
    {
        if (Config.PipeDestroyGold > 0)
            player.Money += Config.PipeDestroyGold;

        foreach ((string itemId, int amount) in ParseItemCosts(Config.PipeDestroyItems))
        {
            Item item = ItemRegistry.Create(itemId, amount);
            Item? leftover = player.addItemToInventory(item);
            if (leftover is not null)
                Game1.createItemDebris(leftover, player.Position, player.FacingDirection);
        }
    }

    private static List<(string ItemId, int Amount)> ParseItemCosts(string raw)
    {
        List<(string ItemId, int Amount)> costs = new();
        foreach (string entry in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = entry.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !int.TryParse(parts[1], out int amount) || amount <= 0)
                continue;

            string itemId = parts[0];
            if (!itemId.StartsWith("(", StringComparison.Ordinal))
                itemId = "(O)" + itemId;
            costs.Add((itemId, amount));
        }
        return costs;
    }

    private static void RemoveItems(Farmer player, string itemId, int amount)
    {
        for (int i = 0; i < player.Items.Count && amount > 0; i++)
        {
            Item? item = player.Items[i];
            if (item?.QualifiedItemId != itemId)
                continue;

            int remove = Math.Min(item.Stack, amount);
            item.Stack -= remove;
            amount -= remove;
            if (item.Stack <= 0)
                player.Items[i] = null;
        }
    }

    private static float GetElapsedHours(int oldTime, int newTime)
    {
        int oldMinutes = StardewTimeToMinutes(oldTime);
        int newMinutes = StardewTimeToMinutes(newTime);
        if (newMinutes < oldMinutes)
            newMinutes += 24 * 60;
        return Math.Max(0, newMinutes - oldMinutes) / 60f;
    }

    private static int StardewTimeToMinutes(int time)
    {
        return time / 100 * 60 + time % 100;
    }

    private void PlayLocationSound(string sound)
    {
        if (!string.IsNullOrWhiteSpace(sound))
            Game1.player.currentLocation.playSound(sound);
    }

    private static float GetFloat(string? raw)
    {
        return float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : 0f;
    }

    private static string FormatFloat(float value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static string ItemId(string key)
    {
        return ModId + "_" + key;
    }


    private static bool ConditionsAllowObject(GameLocation location, Object worldObject, UtilityObjectRule rule)
    {
        return (!rule.MustBeOn || worldObject.IsOn)
            && (!rule.OnlyDay || Game1.timeOfDay < 1800)
            && (!rule.OnlyNight || Game1.timeOfDay >= 1800)
            && (!rule.OnlyMorning || Game1.timeOfDay == 600)
            && (string.IsNullOrWhiteSpace(rule.MustContain) || worldObject.heldObject.Value?.Name == rule.MustContain)
            && (!rule.MustHaveSun || ((location.IsOutdoors || location.IsGreenhouse) && !IsRainingHere(location)))
            && (!rule.MustHaveRain || (location.IsOutdoors && IsRainingHere(location)))
            && (!rule.MustHaveLightning || (location.IsOutdoors && IsLightningHere(location)));
    }

    private static bool PlayerCanPlaceItemHerePrefix(GameLocation location, Item item, int x, int y, ref bool __result)
    {
        if (!Config.EnableMod || item is not Object obj)
            return true;

        Vector2 tile = new(x / Game1.tileSize, y / Game1.tileSize);

        string key = GetRuleKey(obj);
        if (!ObjectRules.TryGetValue(key, out UtilityObjectRule? rule) || !rule.OnlyInWater || location.Objects.ContainsKey(tile))
            return true;

        __result = location.isWaterTile(x / Game1.tileSize, y / Game1.tileSize);
        return false;
    }

    private static void ObjectMinutesElapsedPrefix(Object __instance, int minutes, ref bool __state)
    {
        GameLocation? location = FindObjectLocation(__instance);
        __state = location is not null && PauseUnpoweredMachine(__instance, location);
    }


    private static GameLocation? FindObjectLocation(Object obj)
    {
        foreach (GameLocation location in Game1.locations)
        {
            if (location.Objects.TryGetValue(obj.TileLocation, out Object? candidate) && ReferenceEquals(candidate, obj))
                return location;
        }

        return null;
    }

    private static bool ObjectDayUpdatePrefix(Object __instance, ref bool __state)
    {
        __state = false;
        GameLocation? location = FindObjectLocation(__instance);
        if (location is null)
            return true;

        if (__instance.IsSprinkler() && ShouldBlockUnpoweredObject(__instance, location))
            return false;

        __state = PauseUnpoweredMachine(__instance, location);
        return true;
    }

    private static void ObjectMethodPostfix(Object __instance, bool __state)
    {
        if (!__state || !__instance.modData.TryGetValue(MachinePauseKey, out string? raw) || !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int minutes))
            return;

        __instance.MinutesUntilReady = minutes;
        __instance.modData.Remove(MachinePauseKey);
    }

    private static bool PauseUnpoweredMachine(Object obj, GameLocation location)
    {
        if (!ShouldBlockUnpoweredObject(obj, location))
            return false;

        obj.modData[MachinePauseKey] = obj.MinutesUntilReady.ToString(CultureInfo.InvariantCulture);
        obj.MinutesUntilReady = 9999;
        return true;
    }

    private static bool ShouldBlockUnpoweredObject(Object obj, GameLocation location)
    {
        if (!Config.EnableMod || !Config.EnablePowerRules || location is null || (Context.IsMultiplayer && !Context.IsMainPlayer))
            return false;

        string key = GetRuleKey(obj);
        if (!ObjectRules.TryGetValue(key, out UtilityObjectRule? rule) || !ObjectNeedsPower(rule))
            return false;

        string locationName = location.NameOrUniqueName;
        EnsureLocation(locationName);
        EnsureAllGroupsClean(locationName);
        EnsurePowerCache(locationName);
        return !IsObjectPowered(locationName, obj.TileLocation, rule);
    }


    private void RegisterBetterCrafting()
    {
        Leclair.Stardew.BetterCrafting.IBetterCrafting? betterCrafting = Helper.ModRegistry.GetApi<Leclair.Stardew.BetterCrafting.IBetterCrafting>("leclair.bettercrafting");
        if (betterCrafting is null)
            return;

        string[] recipes =
        {
            "Bronze Water Pump",
            "Steel Water Pump",
            "Gold Water Pump",
            "Iridium Water Pump",
            "Utility Grid Battery",
            "Utility Grid Water Tank",
            "Utility Grid Advanced Battery"
        };

        const string categoryId = "utility-grid-redux-utilities";
        betterCrafting.CreateDefaultCategory(false, categoryId, () => "Utilities", recipes, "Bronze Water Pump", false, null);
        betterCrafting.AddRecipesToDefaultCategory(false, categoryId, recipes);
    }

    private static void InvokeBetterCraftingMethod(object api, string methodName, params object?[] arguments)
    {
        foreach (System.Reflection.MethodInfo method in api.GetType().GetMethods().Where(method => method.Name == methodName && method.GetParameters().Length == arguments.Length))
        {
            method.Invoke(api, arguments);
            return;
        }
    }

    private static IEnumerable<(string LocationName, PipeGroup Group)> GetLinkedBuildingGroups(string locationName, PipeGroup group, GridKind grid)
    {
        GameLocation? location = Game1.getLocationFromName(locationName) ?? Game1.getLocationFromName(locationName, true);
        if (location is null)
            yield break;

        HashSet<string> yielded = new(StringComparer.Ordinal);
        if (location is Farm farm)
        {
            foreach (object building in farm.buildings)
            {
                if (!TryGetBuildingBounds(building, out Rectangle bounds)
                    || !TryGetBuildingInteriorName(building, out string? interiorName)
                    || string.IsNullOrWhiteSpace(interiorName)
                    || !group.Pipes.Any(tile => IsTileAttachedToBuilding(tile, bounds)))
                {
                    continue;
                }

                foreach (PipeGroup linkedGroup in GetLocationGroups(interiorName, grid))
                {
                    string key = interiorName + ":" + RuntimeHelpers.GetHashCode(linkedGroup).ToString(CultureInfo.InvariantCulture);
                    if (yielded.Add(key))
                        yield return (interiorName, linkedGroup);
                }
            }
            yield break;
        }

        foreach ((string exteriorLocationName, Rectangle bounds) in GetExteriorBuildingBoundsForInterior(locationName))
        {
            foreach (PipeGroup linkedGroup in GetLocationGroups(exteriorLocationName, grid))
            {
                if (!linkedGroup.Pipes.Any(tile => IsTileAttachedToBuilding(tile, bounds)))
                    continue;

                string key = exteriorLocationName + ":" + RuntimeHelpers.GetHashCode(linkedGroup).ToString(CultureInfo.InvariantCulture);
                if (yielded.Add(key))
                    yield return (exteriorLocationName, linkedGroup);
            }
        }
    }

    private static IEnumerable<PipeGroup> GetLocationGroups(string locationName, GridKind grid)
    {
        EnsureLocation(locationName);
        EnsureGroupsClean(locationName, grid);
        return Systems[locationName][grid].Groups;
    }

    private static IEnumerable<(string LocationName, Rectangle Bounds)> GetExteriorBuildingBoundsForInterior(string interiorName)
    {
        foreach (GameLocation candidate in Game1.locations)
        {
            if (candidate is not Farm farm)
                continue;

            string exteriorLocationName = candidate.NameOrUniqueName;
            foreach (object building in farm.buildings)
            {
                if (!TryGetBuildingBounds(building, out Rectangle bounds)
                    || !TryGetBuildingInteriorName(building, out string? buildingInteriorName)
                    || !string.Equals(buildingInteriorName, interiorName, StringComparison.Ordinal))
                {
                    continue;
                }

                yield return (exteriorLocationName, bounds);
            }
        }
    }

    private static bool IsTileAttachedToBuilding(Vector2 tile, Rectangle bounds)
    {
        int x = (int)tile.X;
        int y = (int)tile.Y;
        return x >= bounds.X - 1
            && x <= bounds.X + bounds.Width
            && y >= bounds.Y - 1
            && y <= bounds.Y + bounds.Height;
    }

    private static bool TryGetBuildingBounds(object building, out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        if (!TryGetIntMember(building, "tileX", out int x)
            || !TryGetIntMember(building, "tileY", out int y)
            || !TryGetIntMember(building, "tilesWide", out int width)
            || !TryGetIntMember(building, "tilesHigh", out int height)
            || width <= 0
            || height <= 0)
        {
            return false;
        }

        bounds = new Rectangle(x, y, width, height);
        return true;
    }

    private static bool TryGetBuildingInteriorName(object building, out string? interiorName)
    {
        interiorName = null;
        object? value = GetMemberValue(building, "indoors");
        value = UnwrapNetValue(value);
        if (value is GameLocation location)
        {
            interiorName = location.NameOrUniqueName;
            return true;
        }

        if (value is string raw && !string.IsNullOrWhiteSpace(raw))
        {
            interiorName = raw;
            return true;
        }

        return false;
    }

    private static bool TryGetIntMember(object instance, string name, out int value)
    {
        value = 0;
        object? raw = UnwrapNetValue(GetMemberValue(instance, name));
        if (raw is int intValue)
        {
            value = intValue;
            return true;
        }

        if (raw is IConvertible convertible)
        {
            try
            {
                value = convertible.ToInt32(CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        return false;
    }

    private static object? GetMemberValue(object instance, string name)
    {
        Type type = instance.GetType();
        return type.GetField(name)?.GetValue(instance)
            ?? type.GetProperty(name)?.GetValue(instance);
    }

    private static object? UnwrapNetValue(object? value)
    {
        if (value is null)
            return null;

        return value.GetType().GetProperty("Value")?.GetValue(value) ?? value;
    }


    private static void NormalizeConfig()
    {
        Config.PercentWaterPerTile = NormalizeWaterPerTile(Config.PercentWaterPerTile);
    }

    private static float NormalizeWaterPerTile(float value)
    {
        if (value > 1f)
            value /= 100f;

        value = Math.Clamp(value, 0f, 1f);
        return (float)Math.Round(value / 0.05f, MidpointRounding.AwayFromZero) * 0.05f;
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(ModManifest, () => Config = new ModConfig(), () => { NormalizeConfig(); Helper.WriteConfig(Config); });
        gmcm.AddSectionTitle(ModManifest, () => "General");
        gmcm.AddBoolOption(ModManifest, () => Config.EnableMod, value => Config.EnableMod = value, () => "Enable Mod");
        gmcm.AddBoolOption(ModManifest, () => Config.EnablePowerRules, value => Config.EnablePowerRules = value, () => "Enable Power Rules");
        gmcm.AddBoolOption(ModManifest, () => Config.EnablePipeIrrigation, value => Config.EnablePipeIrrigation = value, () => "Enable Pipe Irrigation", () => "Off by default. Lets water pipes directly water nearby tilled tiles like the legacy behavior.");
        gmcm.AddBoolOption(ModManifest, () => Config.WaterSurroundingTiles, value => Config.WaterSurroundingTiles = value, () => "Water Surrounding Tiles");
        gmcm.AddBoolOption(ModManifest, () => Config.ShowWateredTileMarkers, value => Config.ShowWateredTileMarkers = value, () => "Show Watered Tile Markers");
        gmcm.AddBoolOption(ModManifest, () => Config.ShowSprinklerAnimations, value => Config.ShowSprinklerAnimations = value, () => "Show Sprinkler Animations");
        gmcm.AddBoolOption(ModManifest, () => Config.DebugLogging, value => Config.DebugLogging = value, () => "Debug Logging");
        gmcm.AddNumberOption(ModManifest, () => Config.PercentWaterPerTile, value => Config.PercentWaterPerTile = NormalizeWaterPerTile(value), () => "Water Per Tile", () => "Amount of water consumed per irrigated tile. 1 means each watered tile uses 1 water.", 0f, 1f, 0.05f);

        gmcm.AddSectionTitle(ModManifest, () => "Pipe Costs");
        gmcm.AddNumberOption(ModManifest, () => Config.PipeCostGold, value => Config.PipeCostGold = value, () => "Pipe Gold Cost", null, 0, 100000, 1);
        gmcm.AddNumberOption(ModManifest, () => Config.PipeDestroyGold, value => Config.PipeDestroyGold = value, () => "Pipe Gold Refund", null, 0, 100000, 1);
        gmcm.AddTextOption(ModManifest, () => Config.PipeCostItems, value => Config.PipeCostItems = value, () => "Pipe Item Cost", () => "Comma-separated QualifiedItemId:amount pairs. Bare numbers are treated as object IDs.");
        gmcm.AddTextOption(ModManifest, () => Config.PipeDestroyItems, value => Config.PipeDestroyItems = value, () => "Pipe Item Refund", () => "Comma-separated QualifiedItemId:amount pairs. Bare numbers are treated as object IDs.");
        gmcm.AddTextOption(ModManifest, () => Config.PipeSound, value => Config.PipeSound = value, () => "Pipe Create Sound");
        gmcm.AddTextOption(ModManifest, () => Config.DestroySound, value => Config.DestroySound = value, () => "Pipe Destroy Sound");

        gmcm.AddSectionTitle(ModManifest, () => "Controls");
        gmcm.AddKeybindList(ModManifest, () => Config.ToggleGridOverlay, value => Config.ToggleGridOverlay = value, () => "Toggle Grid Overlay", () => "Cycles the display-only overlay: off, power, water, off. Does not enable pipe editing.");
        gmcm.AddKeybindList(ModManifest, () => Config.ToggleGrid, value => Config.ToggleGrid = value, () => "Toggle Grid Editing", () => "Enables pipe editing and shows the grid overlay while editing.");
        gmcm.AddKeybindList(ModManifest, () => Config.SwitchGrid, value => Config.SwitchGrid = value, () => "Switch Grid");
        gmcm.AddKeybindList(ModManifest, () => Config.SwitchTile, value => Config.SwitchTile = value, () => "Change Pipe Shape");
        gmcm.AddKeybindList(ModManifest, () => Config.RotateTile, value => Config.RotateTile = value, () => "Rotate Pipe Shape");
        gmcm.AddKeybindList(ModManifest, () => Config.PlaceTile, value => Config.PlaceTile = value, () => "Place Pipe");
        gmcm.AddKeybindList(ModManifest, () => Config.DestroyTile, value => Config.DestroyTile = value, () => "Destroy Pipe");
    }

    private static void AddObjectRule(UtilityObjectRule rule, params string[] keys)
    {
        foreach (string key in keys.Where(key => !string.IsNullOrWhiteSpace(key)))
            ObjectRules[key] = rule;
    }

    private static void LoadBuiltInRules()
    {
        ObjectRules.Clear();
        AddObjectRule(new UtilityObjectRule { Water = 10, Power = -2, OnlyInWater = true }, ItemId("BronzeWaterPump"));
        AddObjectRule(new UtilityObjectRule { Water = 25, Power = -4, OnlyInWater = true }, ItemId("SteelWaterPump"));
        AddObjectRule(new UtilityObjectRule { Water = 80, Power = -8 }, ItemId("GoldWaterPump"));
        AddObjectRule(new UtilityObjectRule { Water = 200, Power = -16 }, ItemId("IridiumWaterPump"));
        AddObjectRule(new UtilityObjectRule { PowerChargeCapacity = 50, PowerChargeRate = 2, PowerDischargeRate = 2 }, ItemId("UtilityGridBattery"));
        AddObjectRule(new UtilityObjectRule { PowerChargeCapacity = 100, PowerChargeRate = 5, PowerDischargeRate = 5 }, ItemId("UtilityGridAdvancedBattery"));
        AddObjectRule(new UtilityObjectRule { WaterChargeCapacity = 50, WaterChargeRate = 2, WaterDischargeRate = 2, FillWaterFromRain = true }, ItemId("UtilityGridWaterTank"));
        AddObjectRule(new UtilityObjectRule { Water = -1 }, "(BC)599", "599", "Sprinkler");
        AddObjectRule(new UtilityObjectRule { Water = -2 }, "(BC)621", "621", "Quality Sprinkler");
        AddObjectRule(new UtilityObjectRule { Water = -6, Power = -2 }, "(BC)645", "645", "Iridium Sprinkler");
        AddObjectRule(new UtilityObjectRule { Water = -10, Power = -3 }, "(O)ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltSprinkler", "ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltSprinkler", "Cobalt Sprinkler");
        AddObjectRule(new UtilityObjectRule { Water = -25, Power = -6 }, "(O)ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticSprinkler", "ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticSprinkler", "Prismatic Sprinkler");
        AddObjectRule(new UtilityObjectRule { Water = -50, Power = -10 }, "(O)ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveSprinkler", "ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveSprinkler", "Radioactive Sprinkler");
        AddObjectRule(new UtilityObjectRule { Power = 10, MustBeWorking = true, MustBeFull = true }, "(BC)13", "13", "Furnace");
        AddObjectRule(new UtilityObjectRule { Power = 10, MustBeWorking = true, MustBeFull = true }, "(BC)114", "114", "Charcoal Kiln");
        AddObjectRule(new UtilityObjectRule { Power = 10, MustHaveSun = true }, "(BC)231", "231", "Solar Panel");
        AddObjectRule(new UtilityObjectRule { Power = -3, MustBeWorking = true, MustBeFull = true }, "(BC)21", "21", "Crystalarium");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "(BC)20", "20", "Recycling Machine");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "(BC)156", "156", "Slime Incubator");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "(BC)211", "211", "Wood Chipper");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "(BC)254", "254", "Ostrich Incubator");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "(BC)265", "265", "Deconstructor");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "(BC)117", "117", "Soda Machine");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "(BC)246", "246", "Coffee Maker");
        AddObjectRule(new UtilityObjectRule { Power = -4, MustBeWorking = true, MustBeFull = true }, "Heavy Furnace");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Geode Crusher");
        AddObjectRule(new UtilityObjectRule { Power = -3, MustBeWorking = true, MustBeFull = true }, "Mini-Forge");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Bait Maker");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Bone Mill");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Slime Egg-Press");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Seed Maker");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Dehydrator");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Fish Smoker");
        AddObjectRule(new UtilityObjectRule { Power = -2, MustBeWorking = true, MustBeFull = true }, "Oil Maker");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Loom");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Mayonnaise Machine");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Cheese Press");
        AddObjectRule(new UtilityObjectRule { Power = -1 }, "Hopper");
        AddObjectRule(new UtilityObjectRule { Power = -1 }, "Farm Computer");
        AddObjectRule(new UtilityObjectRule { Power = -1 }, "Telephone");
        AddObjectRule(new UtilityObjectRule { Power = -1, MustBeWorking = true, MustBeFull = true }, "Sewing Machine");
        AddObjectRule(new UtilityObjectRule { Power = -1 }, "Mini-Jukebox");

    }

    private static void DebugLog(string message)
    {
        if (Config.DebugLogging)
            SMonitor.Log(message, LogLevel.Debug);
    }

}
