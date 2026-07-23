using System;
using System.Collections.Generic;
using System.Linq;

using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Spacechase.Shared.Patching;


using SpaceShared;

using StardewModdingAPI;
using StardewModdingAPI.Events;

using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.Projectiles;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

using ThaleTheGreat.TheftOfTheWinterStar.Framework;
using ThaleTheGreat.TheftOfTheWinterStar.Patches;

using xTile;
using xTile.Layers;
using xTile.Tiles;

using SObject = StardewValley.Object;

namespace ThaleTheGreat.TheftOfTheWinterStar
{
    /// <summary>The mod entry class.</summary>
    public class Mod : StardewModdingAPI.Mod
    {
        /*********
        ** Fields
        *********/
        /// <summary>The ID for the introductory event with Lewis.</summary>
        private const string EventId = "ThaleTheGreat.TheftOfTheWinterStar_Intro";
        private const string SaveDataKey = "ThaleTheGreat.TheftOfTheWinterStar.SaveData";
        public const string FestiveBigKeyAId = "ThaleTheGreat.TheftOfTheWinterStar_FestiveBigKeyA";
        public const string FestiveBigKeyBId = "ThaleTheGreat.TheftOfTheWinterStar_FestiveBigKeyB";
        public const string FestiveKeyId = "ThaleTheGreat.TheftOfTheWinterStar_FestiveKey";
        public const string FrostyStardropPieceId = "ThaleTheGreat.TheftOfTheWinterStar_FrostyStardropPiece";
        public const string TempusGlobeId = "ThaleTheGreat.TheftOfTheWinterStar_TempusGlobe";
        public const string FestiveScepterId = "ThaleTheGreat.TheftOfTheWinterStar_FestiveScepter";
        private const string LockFlagPrefix = "ThaleTheGreat.TheftOfTheWinterStar_Lock.";
        private const string LockedDoorAction = "ThaleTheGreat.TheftOfTheWinterStar_LockedDoor";
        private const string ActivateArenaAction = "ThaleTheGreat.TheftOfTheWinterStar_ActivateArena";
        private const string ItemPuzzleAction = "ThaleTheGreat.TheftOfTheWinterStar_ItemPuzzle";
        private const string BossKeyHalfAction = "ThaleTheGreat.TheftOfTheWinterStar_BossKeyHalf";
        private const string MovableAction = "ThaleTheGreat.TheftOfTheWinterStar_Movable";

        /// <summary>The number of boss keys used on the boss door.</summary>
        private static int BossKeysUsed;
        private bool ShouldPopulateDungeonLoot;

        /// <summary>The saved player progress in the dungeons.</summary>
        private SaveData SaveData = new();

        /// <summary>The boss's health bar background.</summary>
        private Texture2D BossBarBg = null!;

        /// <summary>The boss's health bar foreground.</summary>
        private Texture2D BossBarFg = null!;

        /// <summary>Whether the player has started the boss fight.</summary>
        private bool StartedBoss;

        /// <summary>The projectiles fired by the boss which are still active.</summary>
        private List<Projectile>? PrevProjectiles;


        /// <summary>The names of the custom locations to load.</summary>
        private readonly string[] LocationNames = {
            "Entrance",
            "Arena",
            "Branch1",
            "ItemPuzzle",
            "Bonus1",
            "Bonus2",
            "WeaponRoom",
            "KeyRoom",
            "Branch2",
            "PushPuzzle",
            "Bonus3",
            "Maze",
            "Bonus4",
            "Boss"
        };

        /// <summary>The locations and tiles on which to drop decorations.</summary>
        private readonly IDictionary<string, Vector2[]> DecoSpots = new Dictionary<string, Vector2[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["BusStop"] = new Vector2[] { new(5, 8), new(9, 10), new(10, 14) },
            ["Backwoods"] = new Vector2[] { new(40, 30), new(32, 31), new(25, 29) },
            ["Tunnel"] = new Vector2[] { new(33, 10), new(23, 9), new(10, 8) }
        };


        /*********
        ** Accessors
        *********/
        public static Mod Instance { get; private set; } = null!;


        /*********
        ** Public methods
        *********/
        /// <inheritdoc />
        public override void Entry(IModHelper helper)
        {
            I18n.Init(helper.Translation);
            Mod.Instance = this;
            Log.Monitor = this.Monitor;

            this.BossBarBg = this.Helper.ModContent.Load<Texture2D>("assets/bossbar-bg.png");
            this.BossBarFg = this.Helper.ModContent.Load<Texture2D>("assets/bossbar-fg.png");

            helper.Events.GameLoop.SaveCreated += this.OnSaveCreated;
            helper.Events.GameLoop.SaveLoaded += this.OnSaveLoaded;
            helper.Events.GameLoop.Saving += this.OnSaving;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.DayEnding += this.OnDayEnding;
            helper.Events.Player.Warped += this.OnWarped;
            helper.Events.Player.InventoryChanged += this.OnInventoryChanged;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.RenderedHud += this.OnRenderedHud;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            GameLocation.RegisterTileAction(LockedDoorAction, this.OnTileAction);
            GameLocation.RegisterTileAction(ActivateArenaAction, this.OnTileAction);
            GameLocation.RegisterTileAction(ItemPuzzleAction, this.OnTileAction);
            GameLocation.RegisterTileAction(BossKeyHalfAction, this.OnTileAction);
            GameLocation.RegisterTileAction(MovableAction, this.OnTileAction);

            HarmonyPatcher.Apply(this,
                new GamePatcher(),
                new HoeDirtPatcher()
            );
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            // scatter decorations
            if (this.IsPossibleDecoSpotsMap(e.NameWithoutLocale))
                e.Edit(asset =>
                {
                    if (this.TryGetDecoSpots(asset, out Vector2[] decoSpots) && asset.Data is Map map)
                        this.ScatterDecorationsIfNeeded(map, decoSpots);
                });

            // add map strings
            else if (e.NameWithoutLocale.IsEquivalentTo("Strings/StringsFromMaps"))
            {
                e.Edit(static asset =>
                {
                    var dict = asset.AsDictionary<string, string>().Data;
                    dict.Add("FrostDungeon.LockedEntrance", I18n.MapMessages_LockedEntrance());
                    dict.Add("FrostDungeon.Locked", I18n.MapMessages_LockedDoor());
                    dict.Add("FrostDungeon.LockedBoss", I18n.MapMessages_LockedBoss());
                    dict.Add("FrostDungeon.Unlock", I18n.MapMessages_Unlocked());
                    dict.Add("FrostDungeon.ItemPuzzle", I18n.MapMessages_ItemPuzzle());
                    dict.Add("FrostDungeon.Target", I18n.MapMessages_Target());
                    dict.Add("FrostDungeon.Trail0", I18n.MapMessages_TrailLights());
                    dict.Add("FrostDungeon.Trail1", I18n.MapMessages_TrailCandyCane());
                    dict.Add("FrostDungeon.Trail2", I18n.MapMessages_TrailOrnaments());
                    dict.Add("FrostDungeon.Trail3", I18n.MapMessages_TrailTree());
                });
            }

            // edit tunnel map
            if (e.NameWithoutLocale.IsEquivalentTo("Maps/Tunnel"))
            {
                e.Edit(asset =>
                {
                    var overlay = Game1.currentSeason == "winter" && Game1.dayOfMonth < 25
                        ? this.Helper.ModContent.Load<Map>("assets/OverlayPortal.tmx")
                        : this.Helper.ModContent.Load<Map>("assets/OverlayPortalLocked.tmx");

                    asset
                        .AsMap()
                        .PatchMap(overlay, targetArea: new Rectangle(7, 4, 3, 3));
                });
            }
        }

        /*********
        ** Private methods
        *********/
        /// <inheritdoc cref="IGameLoopEvents.GameLaunched"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        /// <inheritdoc cref="IGameLoopEvents.SaveCreated"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnSaveCreated(object? sender, SaveCreatedEventArgs e)
        {
            this.SaveData = new SaveData();
            this.ShouldPopulateDungeonLoot = true;
        }

        /// <inheritdoc cref="IGameLoopEvents.SaveLoaded"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            this.SaveData = this.Helper.Data.ReadSaveData<SaveData>(SaveDataKey) ?? new SaveData();
            this.ShouldPopulateDungeonLoot = true;
        }

        /// <inheritdoc cref="IGameLoopEvents.Saving"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (Game1.IsMasterGame)
                this.Helper.Data.WriteSaveData(SaveDataKey, this.SaveData);
        }

        /// <inheritdoc cref="IGameLoopEvents.UpdateTicked"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;

            if (this.ShouldPopulateDungeonLoot)
            {
                this.ShouldPopulateDungeonLoot = false;
                this.PopulateDungeonLoot();
            }

            GameLocation location = Game1.currentLocation;

            switch (location?.Name)
            {
                case "FrostDungeon.Arena":
                    if ((this.SaveData.ArenaStage is ArenaStage.Stage1 or ArenaStage.Stage2) && !location.characters.Any(npc => npc is Monster))
                    {
                        Game1.playSound("questcomplete");
                        switch (this.SaveData.ArenaStage)
                        {
                            case ArenaStage.Stage1:
                            {
                                this.SaveData.ArenaStage = ArenaStage.Finished1;
                                string key = FestiveKeyId;
                                var pos = new Vector2(6, 13);
                                var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){key}") }, pos);
                                location.overlayObjects[pos] = chest;
                                Game1.playSound("questcomplete");
                            }
                            break;

                            case ArenaStage.Stage2:
                            {
                                this.SaveData.ArenaStage = ArenaStage.Finished2;
                                string stardropPiece = FrostyStardropPieceId;
                                var pos = new Vector2(13, 13);
                                var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){stardropPiece}") }, pos);
                                location.overlayObjects[pos] = chest;
                                Game1.playSound("questcomplete");
                            }
                            break;
                        }
                    }
                    break;

                case "FrostDungeon.Bonus4":
                    if (!this.SaveData.DidProjectilePuzzle)
                    {
                        var projectiles = location.projectiles.ToList();
                        if (this.PrevProjectiles != null)
                        {
                            foreach (var projectile in projectiles)
                            {
                                if (this.PrevProjectiles.Contains(projectile))
                                    this.PrevProjectiles.Remove(projectile);
                            }

                            foreach (var projectile in this.PrevProjectiles)
                            {
                                if (projectile.getBoundingBox().Intersects(new Rectangle((int)(8.5 * Game1.tileSize), (int)(8.5 * Game1.tileSize), Game1.tileSize * 2, Game1.tileSize * 2)))
                                {
                                    string stardropPiece = FrostyStardropPieceId;
                                    var pos = new Vector2(9, 13);
                                    var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){stardropPiece}") }, pos);
                                    location.overlayObjects[pos] = chest;
                                    this.SaveData.DidProjectilePuzzle = true;
                                    break;
                                }
                            }
                        }
                        this.PrevProjectiles = projectiles;
                    }
                    break;

                case "FrostDungeon.Boss":
                    if (this.StartedBoss & !this.SaveData.BeatBoss)
                    {
                        if (location.characters.Count(npc => npc is Monster) <= 0)
                        {
                            this.StartedBoss = false;
                            this.SaveData.BeatBoss = true;
                            Game1.playSound("achievement");

                            foreach (var player in Game1.getAllFarmers())
                            {
                                if (!player.knowsRecipe("Tempus Globe"))
                                    player.craftingRecipes.Add("Tempus Globe", 0);
                                foreach (NPC npc in Utility.getAllCharacters())
                                    player.changeFriendship(250, npc);
                            }

                            Game1.drawObjectDialogue(I18n.FinalBoss_VictoryMessage());
                        }
                    }
                    break;
            }
        }

        /// <inheritdoc cref="IGameLoopEvents.DayStarted"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            // update maps
            this.Helper.GameContent.InvalidateCache("Maps/Tunnel");
            foreach (string mapName in this.DecoSpots.Keys)
                this.Helper.GameContent.InvalidateCache($"Maps/{mapName}");

            // apply Tempus Globe logic
            string seasonalDelimiterId = $"(BC){TempusGlobeId}";
            Utility.ForEachLocation(loc =>
            {
                if (!this.IsFarm(loc))
                    return true;
                foreach (var pair in loc.Objects.Pairs)
                {
                    var obj = pair.Value;
                    if (obj.QualifiedItemId == seasonalDelimiterId)
                    {
                        for (int ix = -2; ix <= 2; ++ix)
                        {
                            for (int iy = -2; iy <= 2; ++iy)
                            {
                                var key = new Vector2(pair.Key.X + ix, pair.Key.Y + iy);
                                if (!loc.terrainFeatures.TryGetValue(key, out TerrainFeature feature))
                                    continue;
                                if (feature is HoeDirt dirt)
                                {
                                    dirt.state.Value = HoeDirt.watered;
                                    dirt.updateNeighbors();
                                }
                            }
                        }

                        loc.temporarySprites.Add(new TemporaryAnimatedSprite("TileSheets\\animations", new Rectangle(0, 2176, 320, 320), 60f, 4, 100, pair.Key * 64f + new Vector2(sbyte.MinValue, sbyte.MinValue), false, false)
                        {
                            color = Color.White * 0.4f,
                            delayBeforeAnimationStart = Game1.random.Next(1000),
                            id = (int)(pair.Key.X * 4000f + pair.Key.Y)
                        });
                    }
                }

                return true;
            });
        }

        /// <inheritdoc cref="IGameLoopEvents.DayEnding"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            // save dungeon progress
            if (this.SaveData != null)
            {
                this.SaveData.ArenaStage = this.SaveData.ArenaStage switch
                {
                    ArenaStage.Stage1 => ArenaStage.NotTriggered,
                    ArenaStage.Stage2 => ArenaStage.Finished1,
                    _ => this.SaveData.ArenaStage
                };
            }

            // clear custom data from dungeon
            {
                var arena = Game1.getLocationFromName("FrostDungeon.Arena");
                arena.characters.Clear();
                var bossArea = Game1.getLocationFromName("FrostDungeon.Boss");
                if (this.SaveData?.BeatBoss != true)
                {
                    bossArea.characters.Clear();
                    bossArea.netObjects.Clear();
                    this.StartedBoss = false;
                }
            }

        }

        /// <summary>Get whether a location can be farmed.</summary>
        /// <param name="location">The location to check.</param>
        private bool IsFarm(GameLocation location)
        {
            return location.IsFarm || location.IsGreenhouse || location is Farm or IslandWest;
        }

        /// <summary>Populate the dungeon reward chests with native Stardew Valley 1.6 items.</summary>
        private void PopulateDungeonLoot()
        {
            Log.Debug("Adding frost dungeon loot");

            foreach (string locName in this.LocationNames)
            {
                GameLocation loc = Game1.getLocationFromName("FrostDungeon." + locName);
                if (loc == null)
                    continue;

                switch (locName)
                {
                    case "Entrance":
                        break;

                    case "Bonus1" or "Bonus2" or "Bonus3":
                    {
                        var pos = new Vector2(locName == "Bonus2" ? 13 : 9, 9);
                        var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){FrostyStardropPieceId}") }, pos);
                        loc.overlayObjects[pos] = chest;
                    }
                    break;

                    case "WeaponRoom":
                    {
                        var pos = new Vector2(13, 9);
                        var chest = new Chest(new List<Item> { ItemRegistry.Create<MeleeWeapon>($"(W){FestiveScepterId}") }, pos);
                        loc.overlayObjects[pos] = chest;
                    }
                    break;

                    case "KeyRoom":
                    {
                        var pos = new Vector2(13, 9);
                        var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){FestiveKeyId}") }, pos);
                        loc.overlayObjects[pos] = chest;
                    }
                    break;

                    case "Maze":
                    {
                        var pos = new Vector2(20, 26);
                        var chest = new Chest(new List<Item> { ItemRegistry.Create<SObject>($"(O){FestiveBigKeyAId}") }, pos);
                        loc.overlayObjects[pos] = chest;
                    }
                    break;

                    case "Branch2":
                        break;
                }
            }
        }

        /// <inheritdoc cref="IPlayerEvents.Warped"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (!e.IsLocalPlayer)
                return;

            if (e.NewLocation.Name.StartsWith("FrostDungeon."))
            {
                for (int ix = 0; ix < e.NewLocation.Map.Layers[0].LayerWidth; ++ix)
                {
                    for (int iy = 0; iy < e.NewLocation.Map.Layers[0].LayerHeight; ++iy)
                    {
                        string prop = e.NewLocation.doesTileHaveProperty(ix, iy, "UnlockId", "Buildings");

                        if (!string.IsNullOrEmpty(prop) && e.Player.mailReceived.Contains(LockFlagPrefix + prop))
                        {
                            e.NewLocation.setTileProperty(ix, iy, "Buildings", "Action", LockedDoorAction);
                            SetMapTileIndex(e.NewLocation, ix, iy - 2, 48, "Buildings");
                        }
                    }
                }
            }

            switch (e.NewLocation.Name)
            {
                case "Farm":
                    if (!Game1.player.eventsSeen.Contains(Mod.EventId) && Game1.currentSeason == "winter" && Game1.dayOfMonth < 25)
                    {
                        string eventStr = $"continue/64 15/farmer 64 16 2 Lewis 64 18 0/skippable/pause 1500/speak Lewis \"{I18n.Event_LewisSpeech()}\"/pause 500/end";
                        e.NewLocation.currentEvent = new Event(eventStr, this.ModManifest.UniqueID, Mod.EventId, e.Player);
                        Game1.eventUp = true;
                        Game1.displayHUD = false;
                        Game1.player.CanMove = false;
                        Game1.player.showNotCarrying();

                        Game1.player.eventsSeen.Add(Mod.EventId);
                    }
                    break;

                case "FrostDungeon.Boss":
                    if (!this.StartedBoss && !this.SaveData.BeatBoss)
                    {
                        var witch = new Witch();
                        e.NewLocation.characters.Add(witch);

                        var dummySpeaker = new NPC(new AnimatedSprite("Characters\\Penny"), new Vector2(-1, -1), "", 0, "Witch", false, witch.Portrait);
                        var dialogue = new Dialogue(dummySpeaker, string.Empty, I18n.FinalBoss_Speech());
                        var dialogueBox = new DialogueBox(dialogue);

                        Game1.activeClickableMenu = dialogueBox;
                        Game1.dialogueUp = true;
                        Game1.player.Halt();
                        Game1.player.CanMove = false;
                        Game1.currentSpeaker = dummySpeaker;

                        this.StartedBoss = true;
                    }
                    break;
            }
        }

        private bool IsPossibleDecoSpotsMap(IAssetName assetName)
            => assetName.StartsWith("Maps/") && this.DecoSpots.ContainsKey(Path.GetFileNameWithoutExtension(assetName.BaseName));

        /// <summary>Get the <see cref="DecoSpots"/> for a map asset, if any.</summary>
        /// <param name="asset">The map asset being edited.</param>
        /// <param name="decoSpots">The tiles on which to drop decorations.</param>
        private bool TryGetDecoSpots(IAssetInfo asset, out Vector2[] decoSpots)
        {
            // make sure it's a map asset
            if (!typeof(Map).IsAssignableFrom(asset.DataType))
            {
                decoSpots = Array.Empty<Vector2>();
                return false;
            }

            // check for deco spots
            string mapName = Path.GetFileNameWithoutExtension(asset.NameWithoutLocale.BaseName);
            if (this.DecoSpots.TryGetValue(mapName, out Vector2[]? found) && found.Length > 0)
            {
                decoSpots = found;
                return true;
            }

            decoSpots = Array.Empty<Vector2>();
            return false;
        }

        /// <summary>Drop decorations on the given tiles.</summary>
        /// <param name="map">The map to edit.</param>
        /// <param name="spots">The tiles on which to drop decorations.</param>
        private void ScatterDecorationsIfNeeded(Map map, Vector2[] spots)
        {
            if (Game1.currentSeason == "winter" && Game1.dayOfMonth < 25 && !this.SaveData.BeatBoss)
            {
                TileSheet? tilesheet = map.TileSheets.FirstOrDefault(p => p.ImageSource.Contains("trail-decorations"));
                if (tilesheet == null)
                {
                    // AddTileSheet sorts the tilesheets by ID after adding them.
                    // The game sometimes refers to tilesheets by their index (such as in Beach.fixBridge)
                    // Prepending this to the ID should ensure that this tilesheet is added to the end,
                    // which preserves the normal indices of the tilesheets.
                    char comeLast = '\u03a9'; // Omega

                    tilesheet = new TileSheet(map, this.Helper.ModContent.GetInternalAssetName("assets/trail-decorations.png").BaseName, new xTile.Dimensions.Size(2, 2), new xTile.Dimensions.Size(16, 16));
                    tilesheet.Id = comeLast + tilesheet.Id;
                    map.AddTileSheet(tilesheet);
                    map.LoadTileSheets(Game1.mapDisplayDevice);

                    Random r = new Random((int)Game1.uniqueIDForThisGame + map.assetPath.GetHashCode());
                    var buildingsLayer = map.GetLayer("Buildings");
                    foreach (var spot in spots)
                    {
                        int tile = r.Next(4);
                        buildingsLayer.Tiles[(int)spot.X, (int)spot.Y] = new StaticTile(buildingsLayer, tilesheet, BlendMode.Alpha, tile)
                        {
                            Properties = { ["Action"] = $"Message \"FrostDungeon.Trail{tile}\"" }
                        };
                    }
                }
            }
            else
            {
                var layer = map.GetLayer("Buildings");
                foreach (Vector2 spot in spots)
                    layer.Tiles[(int)spot.X, (int)spot.Y] = null;
            }
        }

        /// <inheritdoc cref="IPlayerEvents.InventoryChanged"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
        {
            if (!e.Player.knowsRecipe("Frosty Stardrop"))
            {
                foreach (var item in e.Added)
                {
                    if (item is SObject obj && obj.QualifiedItemId == $"(O){FrostyStardropPieceId}")
                    {
                        e.Player.craftingRecipes.Add("Frosty Stardrop", 0);
                        this.Helper.Events.Player.InventoryChanged -= OnInventoryChanged;
                    }
                }
            }
            else
            {
                this.Helper.Events.Player.InventoryChanged -= OnInventoryChanged;
            }
        }

        private bool PerformUnlockedDoorAction(GameLocation location, Farmer farmer, Point position)
        {
            string action = location.doesTileHaveProperty(position.X, position.Y, "UnlockAction", "Buildings");
            if (string.IsNullOrWhiteSpace(action))
            {
                Log.Error($"The unlocked door at {location.NameOrUniqueName} ({position.X}, {position.Y}) has no UnlockAction property.");
                return false;
            }

            return location.performAction(action, farmer, new xTile.Dimensions.Location(position.X, position.Y));
        }

        internal void AddFrostDungeonLocations()
        {
            Log.Debug("Adding frost dungeon");

            foreach (string locName in this.LocationNames)
            {
                string locationName = $"FrostDungeon.{locName}";
                if (Game1.getLocationFromName(locationName) is null)
                {
                    GameLocation location = new(this.Helper.ModContent.GetInternalAssetName($"assets/{locName}.tmx").BaseName, locationName);
                    Game1.locations.Add(location);
                }
            }

            this.ShouldPopulateDungeonLoot = true;
        }

        private bool OnTileAction(GameLocation location, string[] args, Farmer farmer, Point position)
        {
            switch (args[0])
            {
                case LockedDoorAction:
                {
                    string unlockId = location.doesTileHaveProperty(position.X, position.Y, "UnlockId", "Buildings");
                    if (string.IsNullOrWhiteSpace(unlockId))
                    {
                        Log.Error($"The locked door at {location.NameOrUniqueName} ({position.X}, {position.Y}) has no UnlockId property.");
                        return false;
                    }

                    string unlockFlag = LockFlagPrefix + unlockId;
                    if (farmer.mailReceived.Contains(unlockFlag))
                        return this.PerformUnlockedDoorAction(location, farmer, position);

                    if (farmer.ActiveObject?.QualifiedItemId == $"(O){FestiveKeyId}")
                    {
                        farmer.Items.ReduceId($"(O){FestiveKeyId}", 1);
                        farmer.mailReceived.Add(unlockFlag);
                        location.setTileProperty(position.X, position.Y, "Buildings", "Action", LockedDoorAction);
                        SetMapTileIndex(location, position.X, position.Y - 2, 48, "Buildings");

                        Game1.drawDialogueNoTyping(Game1.content.LoadString("Strings\\StringsFromMaps:FrostDungeon.Unlock"));
                        Game1.playSound("crystal");
                    }
                    else
                        Game1.drawDialogueNoTyping(Game1.content.LoadString("Strings\\StringsFromMaps:FrostDungeon.Locked"));

                    return true;
                }

                case ActivateArenaAction:
                {
                    if (location.Name != "FrostDungeon.Arena")
                        return true;

                    Log.Trace("Activate arena: Stage " + this.SaveData.ArenaStage);
                    Game1.playSound("batScreech");
                    Game1.playSound("rockGolemSpawn");
                    switch (this.SaveData.ArenaStage)
                    {
                        case ArenaStage.NotTriggered:
                            this.SaveData.ArenaStage = ArenaStage.Stage1;
                            for (int i = 0; i < 9; ++i)
                            {
                                int centerX = position.X;
                                int centerY = position.Y;
                                int offsetX = (int)(Math.Cos(Math.PI * 2 / 9 * i) * 5);
                                int offsetY = (int)(Math.Sin(Math.PI * 2 / 9 * i) * 5);
                                int x = (centerX + offsetX) * Game1.tileSize;
                                int y = (centerY + offsetY) * Game1.tileSize;

                                Monster? monster = (i % 3) switch
                                {
                                    0 => new Ghost(new Vector2(x, y)),
                                    1 => new Skeleton(new Vector2(x, y)),
                                    2 => new DustSpirit(new Vector2(x, y)),
                                    _ => null
                                };

                                if (monster != null)
                                    location.addCharacter(monster);
                            }
                            break;

                        case ArenaStage.Finished1:
                            this.SaveData.ArenaStage = ArenaStage.Stage2;
                            for (int i = 0; i < 3; ++i)
                            {
                                int centerX = position.X;
                                int centerY = position.Y;
                                int offsetX = (int)(Math.Cos(Math.PI * 2 / 3 * i) * 4);
                                int offsetY = (int)(Math.Sin(Math.PI * 2 / 3 * i) * 4);
                                int x = (centerX + offsetX) * Game1.tileSize;
                                int y = (centerY + offsetY) * Game1.tileSize;

                                if (i % 2 == 0)
                                    location.addCharacter(new Bat(new Vector2(x, y), 77377));
                            }
                            location.addCharacter(new DinoMonster(new Vector2(9 * Game1.tileSize, 8 * Game1.tileSize)));
                            break;
                    }

                    return true;
                }

                case ItemPuzzleAction:
                {
                    int itemId = int.Parse(args[1]);
                    if (farmer.ActiveObject?.QualifiedItemId == $"(O){itemId}")
                    {
                        farmer.Items.ReduceId($"(O){itemId}", 1);
                        location.removeTileProperty(position.X, position.Y, "Buildings", "Action");

                        int warpIndex = location.Map.GetLayer("Buildings").Tiles[position.X, position.Y].TileIndex - 32;
                        Layer back = location.Map.GetLayer("Back");
                        back.Tiles[position.X, position.Y + 3] = new StaticTile(back, location.Map.TileSheets[0], BlendMode.Additive, warpIndex);

                        location.warps.Add(new Warp(position.X, position.Y + 3, args[2], 7, 9, false));
                        Game1.playSound("secret1");
                    }
                    else
                        Game1.drawDialogueNoTyping(Game1.content.LoadString("Strings\\StringsFromMaps:FrostDungeon.ItemPuzzle"));

                    return true;
                }

                case BossKeyHalfAction:
                {
                    string key = args[1] == "A" ? FestiveBigKeyAId : FestiveBigKeyBId;
                    if (farmer.ActiveObject?.QualifiedItemId == $"(O){key}")
                    {
                        farmer.Items.ReduceId($"(O){key}", 1);
                        location.removeTile(position.X, position.Y - 1, "Front");
                        location.removeTile(position.X, position.Y, "Buildings");
                        Game1.playSound("secret1");

                        if (++Mod.BossKeysUsed >= 2)
                        {
                            Layer buildings = location.Map.GetLayer("Buildings");
                            const int baseX = 9;
                            const int baseY = 4;
                            for (int i = 0; i < 4; ++i)
                            {
                                int x = baseX + i % 2;
                                int y = baseY + i / 2;
                                buildings.Tiles[x, y].TileIndex += 2;

                                string action = location.doesTileHaveProperty(x, y, "UnlockAction", "Buildings");
                                if (!string.IsNullOrEmpty(action))
                                    location.setTileProperty(x, y, "Buildings", "Action", action);
                            }
                        }
                    }

                    return true;
                }

                case MovableAction:
                {
                    int offsetX = 0;
                    int offsetY = 0;
                    switch (farmer.FacingDirection)
                    {
                        case Game1.down: offsetY = 1; break;
                        case Game1.up: offsetY = -1; break;
                        case Game1.left: offsetX = -1; break;
                        case Game1.right: offsetX = 1; break;
                    }

                    int[] validPuzzleTiles =
                    {
                        240, 241, 242, 243,
                        256, 257, 258, 259, 260,
                        272, 273, 274, 275, 276
                    };
                    const int target = 243;

                    int targetX = position.X;
                    int targetY = position.Y;
                    while (true)
                    {
                        targetX += offsetX;
                        targetY += offsetY;
                        if (!validPuzzleTiles.Contains(location.getTileIndexAt(targetX, targetY, "Back"))
                            || location.doesTileHaveProperty(targetX, targetY, "Action", "Buildings") == MovableAction)
                        {
                            targetX -= offsetX;
                            targetY -= offsetY;
                            break;
                        }
                    }

                    int tileIndex = location.getTileIndexAt(position.X, position.Y, "Buildings");
                    location.removeTile(position.X, position.Y, "Buildings");
                    Layer buildings = location.Map.GetLayer("Buildings");
                    buildings.Tiles[targetX, targetY] = new StaticTile(buildings, location.Map.TileSheets[0], BlendMode.Additive, tileIndex);
                    location.setTileProperty(targetX, targetY, "Buildings", "Action", MovableAction);
                    Game1.playSound("throw");

                    if (location.getTileIndexAt(targetX, targetY, "Back") == target)
                    {
                        Layer back = location.Map.GetLayer("Back");
                        back.Tiles[targetX, targetY] = new StaticTile(back, location.Map.TileSheets[0], BlendMode.Additive, 257);
                        var chestPosition = new Vector2(14, 13);
                        location.overlayObjects[chestPosition] = new Chest(new List<Item>
                        {
                            ItemRegistry.Create<SObject>($"(O){FestiveBigKeyBId}")
                        }, chestPosition);
                        Game1.playSound("secret1");

                        for (int x = 0; x < back.LayerWidth; ++x)
                        {
                            for (int y = 0; y < back.LayerHeight; ++y)
                            {
                                if (location.doesTileHaveProperty(x, y, "Action", "Buildings") == MovableAction)
                                    location.removeTile(x, y, "Buildings");
                            }
                        }
                    }

                    return true;
                }
            }

            return false;
        }

        private static void SetMapTileIndex(GameLocation location, int tileX, int tileY, int index, string layerName, int tileSheetIndex = 0)
        {
            Layer layer = location.Map.GetLayer(layerName)
                ?? throw new InvalidOperationException($"Map '{location.NameOrUniqueName}' has no '{layerName}' layer.");

            if (layer.Tiles[tileX, tileY] is Tile tile)
                tile.TileIndex = index;
            else
                layer.Tiles[tileX, tileY] = new StaticTile(layer, location.Map.TileSheets[tileSheetIndex], BlendMode.Alpha, index);
        }

        /// <inheritdoc cref="IInputEvents.ButtonPressed"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (e.Button.IsActionButton() && Context.IsPlayerFree)
            {
                if (Game1.player.CurrentTool is MeleeWeapon weapon && weapon.QualifiedItemId == $"(W){FestiveScepterId}")
                {
                    if (MeleeWeapon.defenseCooldown > 0)
                        return;

                    _ = new Beam(Game1.player, e.Cursor.AbsolutePixels);
                }
            }
        }

        /// <inheritdoc cref="IDisplayEvents.RenderedHud"/>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event arguments.</param>
        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            var b = e.SpriteBatch;

            if (Game1.currentLocation.characters.SingleOrDefault(npc => npc is Witch) is Witch witch)
            {
                int posX = (Game1.viewport.Width - this.BossBarBg.Width * 4) / 2;
                b.Draw(this.BossBarBg, new Vector2(posX, 5), null, Color.White, 0, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 1);

                float percent = (float)witch.Health / Witch.WitchHealth;
                Rectangle sourceRect = new Rectangle(0, 0, (int)(this.BossBarFg.Width * percent), this.BossBarFg.Height);
                if (sourceRect.Width > 0)
                {
                    b.Draw(this.BossBarFg, new Vector2(posX, 5), sourceRect, Color.Green, 0, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, 1);
                }
            }
        }

        internal void HandleBombExploded(GameLocation location, Vector2 position, int bombRadius, Farmer who)
        {
            if (!location.Name.StartsWith("FrostDungeon."))
                return;

            int radius = bombRadius + 2;
            bool[,] circleOutlineGrid2 = Game1.getCircleOutlineGrid(radius);

            bool flag = false;
            Vector2 index1 = new Vector2((int)(position.X - (double)radius), (int)(position.Y - (double)radius));
            for (int index2 = 0; index2 < radius * 2 + 1; ++index2)
            {
                for (int index3 = 0; index3 < radius * 2 + 1; ++index3)
                {
                    if (index2 == 0 || index3 == 0 || (index2 == radius * 2 || index3 == radius * 2))
                        flag = circleOutlineGrid2[index2, index3];
                    else if (circleOutlineGrid2[index2, index3])
                    {
                        flag = !flag;
                        if (!flag)
                        {
                            this.DoBombableCheck(location, index1);
                        }
                    }
                    if (flag)
                    {
                        this.DoBombableCheck(location, index1);
                    }
                    ++index1.Y;
                    index1.Y = Math.Min(location.map.Layers[0].LayerHeight - 1, Math.Max(0.0f, index1.Y));
                }
                ++index1.X;
                index1.Y = Math.Min(location.map.Layers[0].LayerWidth - 1, Math.Max(0.0f, index1.X));
                index1.Y = position.Y - radius;
                index1.Y = Math.Min(location.map.Layers[0].LayerHeight - 1, Math.Max(0.0f, index1.Y));
            }
        }

        private void DoBombableCheck(GameLocation location, Vector2 tile)
        {
            string propVal = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Bombable", "Buildings");
            if (string.IsNullOrEmpty(propVal))
                return;

            string[] bombActions = propVal.Split(' ');
            foreach (string actStr in bombActions)
            {
                int eqIndex = actStr.IndexOf('=');
                string action = actStr.Substring(0, eqIndex);
                string arguments = actStr.Substring(eqIndex + 1);

                switch (action)
                {
                    case "Buildings":
                    {
                        int index = int.Parse(arguments);
                        var buildings = location.Map.GetLayer("Buildings");
                        var existingTile = buildings.Tiles[(int)tile.X, (int)tile.Y];
                        buildings.Tiles[(int)tile.X, (int)tile.Y] = (index == -1) ? null : new StaticTile(buildings, existingTile.TileSheet, BlendMode.Additive, index);
                    }
                    break;

                    case "Warp":
                    {
                        string[] tokens = arguments.Split(',');
                        var warp = new Warp((int)tile.X, (int)tile.Y, tokens[2], int.Parse(tokens[0]), int.Parse(tokens[1]), false);
                        location.warps.Add(warp);
                    }
                    break;
                }
            }

            location.removeTileProperty((int)tile.X, (int)tile.Y, "Buildings", "Bombable");
            this.DoBombableCheck(location, new Vector2(tile.X + 1, tile.Y));
            this.DoBombableCheck(location, new Vector2(tile.X - 1, tile.Y));
            this.DoBombableCheck(location, new Vector2(tile.X, tile.Y + 1));
            this.DoBombableCheck(location, new Vector2(tile.X, tile.Y - 1));
        }
    }
}
