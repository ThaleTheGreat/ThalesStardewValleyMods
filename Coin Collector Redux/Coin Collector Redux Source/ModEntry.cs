using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Newtonsoft.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.GameData.Weapons;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ThaleTheGreat.CoinCollectorRedux
{
    public partial class ModEntry : Mod
    {
        public const string DetectorId = "ThaleTheGreat.CoinCollectorRedux_MetalDetector";
        public const string DetectorQualifiedId = "(O)ThaleTheGreat.CoinCollectorRedux_MetalDetector";
        public const string DetectorWeaponId = "ThaleTheGreat.CoinCollectorRedux_MetalDetectorWeapon";
        public const string DetectorWeaponQualifiedId = "(W)ThaleTheGreat.CoinCollectorRedux_MetalDetectorWeapon";

        private const string NewDictionaryPath = "ThaleTheGreat.CoinCollectorRedux/Coins";
        private const string DetectorTexturePath = "ThaleTheGreat.CoinCollectorRedux/MetalDetector";
        private const string CoinTexturePath = "ThaleTheGreat.CoinCollectorRedux/CoinsTexture";
        private const string LocationCoinsModDataKey = "ThaleTheGreat.CoinCollectorRedux/Coins";
        private const string DetectorModDataKey = "ThaleTheGreat.CoinCollectorRedux/MetalDetectorItem";
        private const int DetectorShopPrice = 2500;

        public static IMonitor PMonitor = null!;
        public static IModHelper PHelper = null!;
        public static ModEntry Instance = null!;
        public static ModConfig Config = null!;

        public static Dictionary<string, CoinData> CoinDataDict = new(StringComparer.OrdinalIgnoreCase);
        public static Dictionary<string, Dictionary<Vector2, string>> CoinLocationDict = new(StringComparer.OrdinalIgnoreCase);

        private static int deltaTime;
        private static SoundEffect? blipEffectCenter;
        private static SoundEffect? blipEffectLeft;
        private static SoundEffect? blipEffectRight;
        private static bool coinDataLoaded;
        private static Texture2D? detectorTexture;

        public override void Entry(IModHelper helper)
        {
            Instance = this;
            PMonitor = Monitor;
            PHelper = helper;
            Config = Helper.ReadConfig<ModConfig>();
            CoinDataDict = GetDefaultCoinData();

            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;

            if (!Config.ModEnabled)
                return;

            new Harmony(ModManifest.UniqueID).PatchAll();

            Helper.Events.Content.AssetRequested += OnAssetRequested;
            Helper.Events.Content.AssetReady += OnAssetReady;
            Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            Helper.Events.GameLoop.DayStarted += OnDayStarted;
            Helper.Events.GameLoop.OneSecondUpdateTicked += OnOneSecondUpdateTicked;
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(NewDictionaryPath))
            {
                e.LoadFrom(GetDefaultCoinData, AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(DetectorTexturePath))
            {
                e.LoadFromModFile<Texture2D>("assets/metaldetector.png", AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo(CoinTexturePath))
            {
                e.LoadFromModFile<Texture2D>("assets/coins.png", AssetLoadPriority.Medium);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit(EditObjects);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Weapons"))
            {
                e.Edit(EditWeapons);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            {
                e.Edit(EditShops);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(EditCraftingRecipes);
            }
        }

        private void OnAssetReady(object? sender, AssetReadyEventArgs e)
        {
            if (e.Name.IsEquivalentTo(DetectorTexturePath))
                detectorTexture = null;

            if (!e.Name.IsEquivalentTo(NewDictionaryPath))
                return;

            ReloadCoinData();
            Helper.GameContent.InvalidateCache("Data/Objects");
            if (StardewModdingAPI.Context.IsWorldReady)
                RefreshCoinLocationCache();
        }

        private void EditObjects(IAssetData asset)
        {
            EnsureCoinDataLoaded();
            IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;

            data[DetectorId] = new ObjectData
            {
                Name = "MetalDetector",
                DisplayName = Helper.Translation.Get("name").ToString(),
                Description = Helper.Translation.Get("description").ToString(),
                Type = "Basic",
                Category = -98,
                Price = 2500,
                Edibility = -300,
                Texture = DetectorTexturePath,
                SpriteIndex = 0,
                ContextTags = new List<string>
                {
                    "item_metal_detector",
                    "item_tool"
                }
            };

            foreach ((_, CoinData coin) in CoinDataDict)
            {
                if (!coin.CreateObject)
                    continue;

                data[coin.UnqualifiedObjectId()] = new ObjectData
                {
                    Name = coin.Name,
                    DisplayName = coin.DisplayName,
                    Description = coin.Description,
                    Type = string.IsNullOrWhiteSpace(coin.Type) ? "Minerals" : coin.Type,
                    Category = coin.Category,
                    Price = coin.Price,
                    Edibility = -300,
                    Texture = coin.TexturePath,
                    SpriteIndex = coin.SpriteIndex,
                    ContextTags = BuildCoinContextTags(coin)
                };
            }
        }


        private void EditWeapons(IAssetData asset)
        {
            IDictionary<string, WeaponData> data = asset.AsDictionary<string, WeaponData>().Data;

            data[DetectorWeaponId] = new WeaponData
            {
                Name = "MetalDetector",
                DisplayName = Helper.Translation.Get("name").ToString(),
                Description = Helper.Translation.Get("description").ToString(),
                Type = 2,
                Texture = DetectorTexturePath,
                SpriteIndex = 0,
                MinDamage = 5,
                MaxDamage = 15,
                Knockback = 1.5f,
                Speed = -16,
                Precision = 0,
                Defense = 0,
                AreaOfEffect = 0,
                CritChance = 0.02f,
                CritMultiplier = 3f,
                MineBaseLevel = -1,
                MineMinLevel = -1,
                CanBeLostOnDeath = false
            };
        }

        private void EditShops(IAssetData asset)
        {
            IDictionary<string, ShopData> shops = asset.AsDictionary<string, ShopData>().Data;
            if (!shops.TryGetValue("SeedShop", out ShopData? seedShop))
                return;

            seedShop.Items ??= new List<ShopItemData>();

            seedShop.Items.RemoveAll(item =>
                item.Id == DetectorId
                || item.Id == DetectorWeaponId
                || item.ItemId == DetectorQualifiedId
                || item.ItemId == DetectorWeaponQualifiedId
            );

            seedShop.Items.Add(new ShopItemData
            {
                Id = DetectorWeaponId,
                ItemId = DetectorWeaponQualifiedId,
                Price = DetectorShopPrice,
                AvailableStock = 1,
                AvoidRepeat = true,
                IsRecipe = false
            });
        }

        private void EditCraftingRecipes(IAssetData asset)
        {
            IDictionary<string, string> recipes = asset.AsDictionary<string, string>().Data;
            recipes["Metal Detector"] = $"{Config.CraftingRequirements}/Home/{DetectorId}/false/default/{Helper.Translation.Get("name")}";
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            ReloadCoinData();
            Helper.GameContent.InvalidateCache("Data/Objects");
            RefreshCoinLocationCache();

            EnsureMetalDetectorRecipeKnown();
            ConvertKnownObjectDetectorsToWeapon();
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (!Config.ModEnabled || CoinDataDict.Count == 0)
                return;

            if (StardewModdingAPI.Context.IsMainPlayer)
                GenerateDailyCoins();

            RefreshCoinLocationCache();
        }

        private void OnOneSecondUpdateTicked(object? sender, OneSecondUpdateTickedEventArgs e)
        {
            if (!Config.ModEnabled || Config.RequireMetalDetectorSwing || !StardewModdingAPI.Context.IsPlayerFree || ++deltaTime < Math.Max(1, Config.SecondsPerPoll))
                return;
            if (Config.RequireMetalDetector && !IsMetalDetectorItem(Game1.player.CurrentItem))
                return;

            DoBlip();
        }


        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            RegisterConfigMenu();
            ReloadSounds();
        }

        private void RegisterConfigMenu()
        {
            IGenericModConfigMenuApi? api = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (api == null)
                return;

            api.Register(
                ModManifest,
                reset: () => Config = new ModConfig(),
                save: () =>
                {
                    Helper.WriteConfig(Config);
                    ReloadSounds();
                    Helper.GameContent.InvalidateCache("Data/Objects");
                    Helper.GameContent.InvalidateCache("Data/Weapons");
                    Helper.GameContent.InvalidateCache("Data/Shops");
                    Helper.GameContent.InvalidateCache("Data/CraftingRecipes");
                }
            );

            api.AddSectionTitle(ModManifest, () => "General");
            api.AddBoolOption(ModManifest, () => Config.ModEnabled, value => Config.ModEnabled = value, () => "Mod Enabled", () => "Enable Coin Collector Redux. Restart the game after changing this option.");
            api.AddBoolOption(ModManifest, () => Config.DebugLogging, value => Config.DebugLogging = value, () => "Debug Logging", () => "Log extra diagnostic messages for troubleshooting.");

            api.AddSectionTitle(ModManifest, () => "Metal Detector");
            api.AddBoolOption(ModManifest, () => Config.RequireMetalDetector, value => Config.RequireMetalDetector = value, () => "Require Metal Detector", () => "Only play detector blips while holding a recognized metal detector.");
            api.AddBoolOption(ModManifest, () => Config.RequireMetalDetectorSwing, value => Config.RequireMetalDetectorSwing = value, () => "Require Metal Detector Swing", () => "Only ping when using the held detector instead of polling automatically.");

            api.AddSectionTitle(ModManifest, () => "Audio");
            api.AddNumberOption(ModManifest, () => Config.BlipAudioVolume, value => Config.BlipAudioVolume = value, () => "Blip Audio Volume", () => "Maximum blip volume.", min: 0f, max: 1f, interval: 0.05f);
            api.AddBoolOption(ModManifest, () => Config.BlipAudioIncreasePitch, value => Config.BlipAudioIncreasePitch = value, () => "Increase Pitch Near Coins", () => "Increase blip pitch as the player gets closer to a coin.");

            api.AddSectionTitle(ModManifest, () => "Coin Spawns");
            api.AddNumberOption(ModManifest, () => Config.SecondsPerPoll, value => Config.SecondsPerPoll = value, () => "Seconds Per Poll", () => "Seconds between automatic detector checks.", min: 1, interval: 1);
            api.AddNumberOption(ModManifest, () => Config.MapHasCoinsChance, value => Config.MapHasCoinsChance = value, () => "Map Has Coins Chance", () => "Daily chance each outdoor map receives coin spots.", min: 0f, max: 1f, interval: 0.05f);
            api.AddNumberOption(ModManifest, () => Config.MinCoinsPerMap, value => Config.MinCoinsPerMap = value, () => "Minimum Coins Per Map", () => "Minimum generated coin spots on a valid map.", min: 0, interval: 1);
            api.AddNumberOption(ModManifest, () => Config.MaxCoinsPerMap, value => Config.MaxCoinsPerMap = value, () => "Maximum Coins Per Map", () => "Maximum generated coin spots on a valid map before luck bonus.", min: 0, interval: 1);
            api.AddNumberOption(ModManifest, () => Config.LuckFactor, value => Config.LuckFactor = value, () => "Luck Factor", () => "How much player luck can increase maximum generated coins.", min: 0f, interval: 0.05f);
            api.AddNumberOption(ModManifest, () => Config.MaxPixelPingDistance, value => Config.MaxPixelPingDistance = value, () => "Max Pixel Ping Distance", () => "Maximum pixel distance for detector pings. Use 0 for unlimited.", min: 0f, interval: 50f);

            api.AddSectionTitle(ModManifest, () => "Indicator");
            api.AddBoolOption(ModManifest, () => Config.EnableIndicator, value => Config.EnableIndicator = value, () => "Enable Indicator", () => "Show a directional projectile indicator toward the nearest coin.");
            api.AddNumberOption(ModManifest, () => Config.IndicatorSprite, value => Config.IndicatorSprite = value, () => "Indicator Sprite", () => "Projectile sprite index used by the detector indicator.", min: 0, interval: 1);
            api.AddNumberOption(ModManifest, () => Config.IndicatorLength, value => Config.IndicatorLength = value, () => "Indicator Length", () => "Maximum indicator travel distance in tiles.", min: 0f, interval: 0.25f);
            api.AddNumberOption(ModManifest, () => Config.IndicatorSpeed, value => Config.IndicatorSpeed = value, () => "Indicator Speed", () => "Speed multiplier for the directional indicator.", min: 0f, interval: 0.5f);
        }

        private void ReloadSounds()
        {
            blipEffectCenter = LoadSound(Config.BlipAudioPath);
            blipEffectLeft = LoadSound(Config.BlipAudioPathLeft) ?? blipEffectCenter;
            blipEffectRight = LoadSound(Config.BlipAudioPathRight) ?? blipEffectCenter;
        }

        private static void EnsureMetalDetectorRecipeKnown()
        {
            if (Game1.player != null && !Game1.player.craftingRecipes.ContainsKey("Metal Detector"))
                Game1.player.craftingRecipes.Add("Metal Detector", 0);
        }

        private static void ConvertKnownObjectDetectorsToWeapon()
        {
            if (Game1.player == null)
                return;

            for (int i = 0; i < Game1.player.Items.Count; i++)
            {
                if (IsLegacyObjectDetector(Game1.player.Items[i]))
                    Game1.player.Items[i] = CreateDetectorWeapon();
            }
        }

        private static Item CreateDetectorWeapon()
        {
            Item item = ItemRegistry.Create(DetectorWeaponQualifiedId);
            item.modData[DetectorModDataKey] = "true";
            return item;
        }

        private static bool IsLegacyObjectDetector(Item? item)
        {
            if (item is not StardewValley.Object)
                return false;

            if (item.modData.ContainsKey(DetectorModDataKey))
                return true;

            return ItemRegistry.HasItemId(item, DetectorQualifiedId)
                || ItemRegistry.HasItemId(item, DetectorId)
                || item.ItemId.Equals(DetectorId, StringComparison.OrdinalIgnoreCase)
                || item.Name.Equals("MetalDetector", StringComparison.OrdinalIgnoreCase);
        }

        private static Texture2D? GetDetectorTexture()
        {
            if (detectorTexture != null)
                return detectorTexture;

            try
            {
                detectorTexture = PHelper.GameContent.Load<Texture2D>(DetectorTexturePath);
                return detectorTexture;
            }
            catch (Exception ex)
            {
                PMonitor.Log($"Failed loading metal detector texture: {ex}", LogLevel.Error);
                return null;
            }
        }

        private SoundEffect? LoadSound(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return null;

            try
            {
                string path = Path.Combine(Helper.DirectoryPath, relativePath);
                using FileStream stream = File.OpenRead(path);
                return SoundEffect.FromStream(stream);
            }
            catch (Exception ex)
            {
                DebugLog($"Couldn't load sound '{relativePath}': {ex.Message}");
                return null;
            }
        }

        private void EnsureCoinDataLoaded()
        {
            if (!coinDataLoaded)
                ReloadCoinData();
        }

        private void ReloadCoinData()
        {
            Dictionary<string, CoinData> merged = new(StringComparer.OrdinalIgnoreCase);

            MergeCoinData(merged, SafeLoadCoinData(NewDictionaryPath));

            foreach ((string key, CoinData coin) in merged.ToArray())
            {
                NormalizeCoinData(key, coin);
                if (coin.Rarity <= 0 || string.IsNullOrWhiteSpace(coin.Id))
                    merged.Remove(key);
            }

            CoinDataDict = merged;
            coinDataLoaded = true;
            DebugLog($"Loaded {CoinDataDict.Count} coin entries.");
        }

        private Dictionary<string, CoinData> SafeLoadCoinData(string assetName)
        {
            try
            {
                return PHelper.GameContent.Load<Dictionary<string, CoinData>>(assetName) ?? new Dictionary<string, CoinData>(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed loading coin data asset '{assetName}': {ex}", LogLevel.Error);
                return new Dictionary<string, CoinData>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void MergeCoinData(IDictionary<string, CoinData> target, IDictionary<string, CoinData> source)
        {
            foreach ((string key, CoinData value) in source)
                target[key] = value;
        }

        private void GenerateDailyCoins()
        {
            EnsureCoinDataLoaded();

            foreach (GameLocation location in Game1.locations)
            {
                Dictionary<Vector2, string> generated = new();

                if (location.IsOutdoors && Game1.random.NextDouble() < Config.MapHasCoinsChance)
                {
                    List<Vector2> diggableTiles = GetDiggableTiles(location);
                    List<KeyValuePair<string, CoinData>> locationCoins = CoinDataDict
                        .Where(pair => CoinCanSpawnInLocation(pair.Value, location))
                        .ToList();

                    if (diggableTiles.Count > 0 && locationCoins.Count > 0)
                    {
                        int maxCoins = Math.Max(Config.MinCoinsPerMap, Config.MaxCoinsPerMap + (int)Math.Round(Game1.player.LuckLevel * Config.LuckFactor));
                        int coinCount = Game1.random.Next(Math.Max(0, Config.MinCoinsPerMap), Math.Max(0, maxCoins) + 1);

                        for (int i = 0; i < coinCount && diggableTiles.Count > 0; i++)
                        {
                            KeyValuePair<string, CoinData>? chosen = PickWeightedCoin(locationCoins);
                            if (chosen == null)
                                break;

                            int tileIndex = Game1.random.Next(diggableTiles.Count);
                            generated[diggableTiles[tileIndex]] = chosen.Value.Key;
                            diggableTiles.RemoveAt(tileIndex);
                        }
                    }
                }

                SaveLocationCoins(location, generated);
                DebugLog($"Generated {generated.Count} coin spots for {location.Name}.");
            }
        }

        private static string LocationCacheKey(GameLocation location)
        {
            return location.NameOrUniqueName;
        }

        private static bool CoinCanSpawnInLocation(CoinData coin, GameLocation location)
        {
            if (coin.Locations.Count == 0)
                return true;

            return coin.Locations.Contains(location.Name, StringComparer.OrdinalIgnoreCase)
                || coin.Locations.Contains(location.NameOrUniqueName, StringComparer.OrdinalIgnoreCase);
        }

        private static List<Vector2> GetDiggableTiles(GameLocation location)
        {
            List<Vector2> tiles = new();
            xTile.Layers.Layer backLayer = location.map.GetLayer("Back");

            for (int x = 0; x < backLayer.LayerWidth; x++)
            {
                for (int y = 0; y < backLayer.LayerHeight; y++)
                {
                    Vector2 tile = new(x, y);
                    if (location.doesTileHaveProperty(x, y, "Diggable", "Back") != null && !IsTileOccupied(location, tile))
                        tiles.Add(tile);
                }
            }

            return tiles;
        }

        private static bool IsTileOccupied(GameLocation location, Vector2 tile)
        {
            return location.IsTileOccupiedBy(tile, CollisionMask.All);
        }

        private static KeyValuePair<string, CoinData>? PickWeightedCoin(IReadOnlyList<KeyValuePair<string, CoinData>> coins)
        {
            float totalRarity = coins.Sum(pair => Math.Max(0, pair.Value.Rarity));
            if (totalRarity <= 0)
                return null;

            double target = Game1.random.NextDouble() * totalRarity;
            float current = 0;

            foreach (KeyValuePair<string, CoinData> coin in coins)
            {
                current += Math.Max(0, coin.Value.Rarity);
                if (target <= current)
                    return coin;
            }

            return coins[^1];
        }

        private static void RefreshCoinLocationCache()
        {
            CoinLocationDict.Clear();
            foreach (GameLocation location in Game1.locations)
            {
                Dictionary<Vector2, string> coins = LoadLocationCoins(location);
                if (coins.Count > 0)
                    CoinLocationDict[LocationCacheKey(location)] = coins;
            }
        }

        public static bool TryDigCoin(GameLocation location, Vector2 tileLocation, bool ignoreChecks)
        {
            Dictionary<Vector2, string> locationCoins = LoadLocationCoins(location);
            if (!locationCoins.TryGetValue(tileLocation, out string? coinKey) || string.IsNullOrWhiteSpace(coinKey))
                return false;
            if (!ignoreChecks && (location.doesTileHaveProperty((int)tileLocation.X, (int)tileLocation.Y, "Diggable", "Back") == null || IsTileOccupied(location, tileLocation)))
                return false;
            if (!CoinDataDict.TryGetValue(coinKey, out CoinData? coin))
                return false;

            try
            {
                Item item = ItemRegistry.Create(coin.QualifiedItemId());
                Game1.createItemDebris(item, tileLocation * Game1.tileSize, -1, location);
                locationCoins.Remove(tileLocation);
                SaveLocationCoins(location, locationCoins);
                RefreshLocationCache(location, locationCoins);
                DebugLog($"Dug up {coin.Id} at {location.Name} {tileLocation}.");
                return true;
            }
            catch (Exception ex)
            {
                PMonitor.Log($"Failed creating coin item '{coin.Id}': {ex}", LogLevel.Error);
                return false;
            }
        }

        private static void RefreshLocationCache(GameLocation location, Dictionary<Vector2, string> coins)
        {
            if (coins.Count > 0)
                CoinLocationDict[LocationCacheKey(location)] = new Dictionary<Vector2, string>(coins);
            else
                CoinLocationDict.Remove(LocationCacheKey(location));
        }

        private static Dictionary<Vector2, string> LoadLocationCoins(GameLocation location)
        {
            Dictionary<Vector2, string> result = new();
            if (!location.modData.TryGetValue(LocationCoinsModDataKey, out string raw) || string.IsNullOrWhiteSpace(raw))
                return result;

            try
            {
                Dictionary<string, string>? stored = JsonConvert.DeserializeObject<Dictionary<string, string>>(raw);
                if (stored == null)
                    return result;

                foreach ((string tileKey, string coinKey) in stored)
                {
                    if (TryParseTileKey(tileKey, out Vector2 tile) && CoinDataDict.ContainsKey(coinKey))
                        result[tile] = coinKey;
                }
            }
            catch (Exception ex)
            {
                PMonitor.Log($"Failed reading coin locations for {location.Name}: {ex}", LogLevel.Error);
            }

            return result;
        }

        private static void SaveLocationCoins(GameLocation location, Dictionary<Vector2, string> coins)
        {
            if (coins.Count == 0)
            {
                location.modData.Remove(LocationCoinsModDataKey);
                return;
            }

            Dictionary<string, string> stored = coins.ToDictionary(pair => TileKey(pair.Key), pair => pair.Value);
            location.modData[LocationCoinsModDataKey] = JsonConvert.SerializeObject(stored);
        }

        private static string TileKey(Vector2 tile)
        {
            return $"{(int)tile.X},{(int)tile.Y}";
        }

        private static bool TryParseTileKey(string key, out Vector2 tile)
        {
            tile = Vector2.Zero;
            string[] parts = key.Split(',');
            if (parts.Length != 2 || !int.TryParse(parts[0], out int x) || !int.TryParse(parts[1], out int y))
                return false;

            tile = new Vector2(x, y);
            return true;
        }

        private static void DoBlip()
        {
            deltaTime = 0;
            if (!CoinLocationDict.TryGetValue(LocationCacheKey(Game1.player.currentLocation), out Dictionary<Vector2, string>? locationCoins) || locationCoins.Count == 0)
                return;

            Vector2 playerLocation = Game1.player.Position + new Vector2(0, -Game1.tileSize / 2f);
            Vector2 nearest = locationCoins.Keys.OrderBy(tile => Vector2.Distance(playerLocation, tile * Game1.tileSize)).FirstOrDefault(-Vector2.One);

            if (nearest.X < 0)
                return;

            float distance = Vector2.Distance(playerLocation, nearest * Game1.tileSize);
            if (Config.MaxPixelPingDistance > 0 && distance > Config.MaxPixelPingDistance)
                return;

            float pan = nearest.X * Game1.tileSize > playerLocation.X ? 1f : nearest.X * Game1.tileSize < playerLocation.X ? -1f : 0f;
            float range = Config.MaxPixelPingDistance > 0 ? Config.MaxPixelPingDistance : Game1.viewport.Width;
            float volume = Math.Clamp((1 - distance / range) * Config.BlipAudioVolume, 0, 1);
            float pitch = Config.BlipAudioIncreasePitch ? volume * 2 - 1 : 0;

            SoundEffect? blipEffect = pan == 0 ? blipEffectCenter : pan > 0 ? blipEffectRight : blipEffectLeft;
            blipEffect?.Play(volume, pitch, pan);

            if (!Config.EnableIndicator)
                return;

            Vector2 velocity = nearest * Game1.tileSize - playerLocation;
            if (velocity == Vector2.Zero)
                return;

            velocity.Normalize();
            IndicatorProjectile projectile = new(0, Config.IndicatorSprite, 0, 32, 0, velocity.X * Config.IndicatorSpeed, velocity.Y * Config.IndicatorSpeed, playerLocation, "", "", "", false, false, Game1.player.currentLocation, Game1.player, null);
            projectile.maxTravelDistance.Value = (int)Math.Min(Math.Round(Game1.tileSize * Config.IndicatorLength), distance);
            Instance.Helper.Reflection.GetField<Netcode.NetBool>(projectile, "damagesMonsters").GetValue().Value = false;
            Instance.Helper.Reflection.GetField<Netcode.NetBool>(projectile, "ignoreLocationCollision").GetValue().Value = true;
            Instance.Helper.Reflection.GetField<Netcode.NetBool>(projectile, "ignoreMeleeAttacks").GetValue().Value = true;
            Game1.player.currentLocation.projectiles.Add(projectile);
        }

        private static bool IsMetalDetectorItem(Item? item)
        {
            if (item == null)
                return false;

            if (item.modData.ContainsKey(DetectorModDataKey))
                return true;

            string configured = Config.MetalDetectorID?.Trim() ?? "";
            if (ItemRegistry.HasItemId(item, DetectorWeaponQualifiedId) || ItemRegistry.HasItemId(item, DetectorWeaponId))
                return true;
            if (ItemRegistry.HasItemId(item, DetectorQualifiedId) || ItemRegistry.HasItemId(item, DetectorId))
                return true;
            if (!string.IsNullOrWhiteSpace(configured) && ItemRegistry.HasItemId(item, configured))
                return true;

            return item.ItemId.Equals(DetectorWeaponId, StringComparison.OrdinalIgnoreCase)
                || item.ItemId.Equals(DetectorId, StringComparison.OrdinalIgnoreCase)
                || item.Name.Equals("MetalDetector", StringComparison.OrdinalIgnoreCase)
                || item.Name.Equals(configured, StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> BuildCoinContextTags(CoinData coin)
        {
            List<string> tags = new()
            {
                "category_gem",
                "item_coin",
                "color_yellow"
            };

            if (!string.IsNullOrWhiteSpace(coin.SetName))
                tags.Add($"coin_set_{SanitizeContextTag(coin.SetName)}");

            foreach (string tag in coin.ContextTags.Where(tag => !string.IsNullOrWhiteSpace(tag)))
            {
                if (!tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
                    tags.Add(tag);
            }

            return tags;
        }

        private static string SanitizeContextTag(string value)
        {
            return new string(value.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
        }

        private static void NormalizeCoinData(string key, CoinData coin)
        {
            if (string.IsNullOrWhiteSpace(coin.Id))
                coin.Id = GetDefaultCoinId(key);
            if (string.IsNullOrWhiteSpace(coin.Name))
                coin.Name = key;
            if (string.IsNullOrWhiteSpace(coin.DisplayName))
                coin.DisplayName = coin.Name;
            if (string.IsNullOrWhiteSpace(coin.Description))
                coin.Description = "A rare and valuable coin.";
            if (coin.CreateObject && string.IsNullOrWhiteSpace(coin.TexturePath))
                coin.TexturePath = CoinTexturePath;
            coin.Id = coin.UnqualifiedObjectId();
            coin.Locations ??= new List<string>();
            coin.ContextTags ??= new List<string>();
        }

        private static string GetDefaultCoinId(string key)
        {
            string suffix = new string(key.Where(char.IsLetterOrDigit).ToArray());
            if (string.IsNullOrWhiteSpace(suffix))
                suffix = "Coin";
            return $"ThaleTheGreat.CoinCollectorRedux_{suffix}";
        }

        private static Dictionary<string, CoinData> GetDefaultCoinData()
        {
            return new Dictionary<string, CoinData>(StringComparer.OrdinalIgnoreCase)
            {
                ["YobaCopperCoin"] = new CoinData
                {
                    Id = "ThaleTheGreat.CoinCollectorRedux_YobaCopperCoin",
                    Name = "YobaCopperCoin",
                    SetName = "Yoba",
                    DisplayName = "Yoba Copper Coin",
                    Description = "A rare copper coin stamped with Yoba's mark.",
                    TexturePath = CoinTexturePath,
                    SpriteIndex = 3,
                    Rarity = 5f,
                    Price = 250
                },
                ["YobaSilverCoin"] = new CoinData
                {
                    Id = "ThaleTheGreat.CoinCollectorRedux_YobaSilverCoin",
                    Name = "YobaSilverCoin",
                    SetName = "Yoba",
                    DisplayName = "Yoba Silver Coin",
                    Description = "A rare silver coin stamped with Yoba's mark.",
                    TexturePath = CoinTexturePath,
                    SpriteIndex = 2,
                    Rarity = 3f,
                    Price = 500
                },
                ["YobaGoldCoin"] = new CoinData
                {
                    Id = "ThaleTheGreat.CoinCollectorRedux_YobaGoldCoin",
                    Name = "YobaGoldCoin",
                    SetName = "Yoba",
                    DisplayName = "Yoba Gold Coin",
                    Description = "A rare gold coin stamped with Yoba's mark.",
                    TexturePath = CoinTexturePath,
                    SpriteIndex = 0,
                    Rarity = 1.5f,
                    Price = 1000
                },
                ["YobaIridiumCoin"] = new CoinData
                {
                    Id = "ThaleTheGreat.CoinCollectorRedux_YobaIridiumCoin",
                    Name = "YobaIridiumCoin",
                    SetName = "Yoba",
                    DisplayName = "Yoba Iridium Coin",
                    Description = "A rare iridium coin stamped with Yoba's mark.",
                    TexturePath = CoinTexturePath,
                    SpriteIndex = 1,
                    Rarity = 0.5f,
                    Price = 2500
                }
            };
        }

        private static void DebugLog(string message)
        {
            if (Config.DebugLogging)
                PMonitor.Log(message, LogLevel.Debug);
        }
    }
}
