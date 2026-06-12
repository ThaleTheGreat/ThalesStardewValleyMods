using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Locations;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Diagnostics.CodeAnalysis;
using xTile;

namespace ThaleTheGreat.GemIslesSwimToGingerIsland
{
    public class ModEntry : Mod
    {
        internal static ModConfig Config = null!;
        internal static IMonitor SMonitor = null!;
        internal const string VanillaMapPath = "Maps/GemIslesSwimToGingerIsland/isles";

        private static int mapX;
        private static int mapY;

        private const string LocationPrefix = "GemIslesSwimToGingerIsland_";

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();
            SMonitor = Monitor;

            Utils.Initialize(Config, Monitor, Helper);

            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.GameLoop.DayEnding += OnDayEnding;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            Helper.GameContent.InvalidateCache("Data/Locations");
        }

        private void OnDayEnding(object? sender, DayEndingEventArgs e)
        {
            for (int i = Game1.locations.Count - 1; i >= 0; i--)
            {
                if (Game1.locations[i].Name.StartsWith(LocationPrefix, StringComparison.Ordinal))
                {
                    Game1.locations[i].characters.Clear();
                    Game1.locations.RemoveAt(i);
                }
            }
        }

        private enum EdgeDirection
        {
            North,
            South,
            East,
            West
        }

        private sealed class GemNode
        {
            public int X { get; }
            public int Y { get; }

            public GemNode(int x, int y)
            {
                X = x;
                Y = y;
            }
        }

        private sealed class VanillaDestination
        {
            public string LocationName { get; }
            public int X { get; }
            public int Y { get; }

            public VanillaDestination(string locationName, int x, int y)
            {
                LocationName = locationName;
                X = x;
                Y = y;
            }
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!Config.EnableMod || !IsPlayerFree() || !Game1.player.swimming.Value)
                return;

            GameLocation current = Game1.currentLocation;
            bool isGemIsles = IsGemIslesLocation(current.Name);
            bool isLinkedVanillaLocation = current.Name == "Beach"
                || current.Name == "IslandWest"
                || current.Name == "IslandSouth"
                || current.Name == "IslandSouthEast";

            if (!isGemIsles && !isLinkedVanillaLocation)
                return;

            int mapPixelWidth = current.map.DisplaySize.Width;
            int mapPixelHeight = current.map.DisplaySize.Height;

            if (Game1.player.position.Y > mapPixelHeight - 20)
            {
                Game1.player.position.Value = new Vector2(Game1.player.position.X, mapPixelHeight - 21);
                HandleEdgeTransition(EdgeDirection.South, mapPixelWidth, mapPixelHeight);
            }
            else if (Game1.player.position.Y < -12)
            {
                Game1.player.position.Value = new Vector2(Game1.player.position.X, -11);
                HandleEdgeTransition(EdgeDirection.North, mapPixelWidth, mapPixelHeight);
            }
            else if (Game1.player.position.X > mapPixelWidth - 36)
            {
                Game1.player.position.Value = new Vector2(mapPixelWidth - 37, Game1.player.position.Y);
                HandleEdgeTransition(EdgeDirection.East, mapPixelWidth, mapPixelHeight);
            }
            else if (Game1.player.position.X < -28)
            {
                Game1.player.position.Value = new Vector2(-27, Game1.player.position.Y);
                HandleEdgeTransition(EdgeDirection.West, mapPixelWidth, mapPixelHeight);
            }
        }

        private void HandleEdgeTransition(EdgeDirection direction, int sourceMapPixelWidth, int sourceMapPixelHeight)
        {
            GameLocation current = Game1.currentLocation;
            Point sourceTile = new Point(
                (int)(Game1.player.position.X / Game1.tileSize),
                (int)(Game1.player.position.Y / Game1.tileSize)
            );

            if (!IsGemIslesLocation(current.Name))
            {
                HandleVanillaEdgeTransition(current.Name, direction, sourceTile, sourceMapPixelWidth, sourceMapPixelHeight);
                return;
            }

            if (TryGetVanillaDestination(mapX, mapY, direction, out VanillaDestination? vanilla))
            {
                DebugLog($"Warping from Gem Isles {mapX},{mapY} {direction} to {vanilla.LocationName}.");
                Game1.warpFarmer(vanilla.LocationName, vanilla.X, vanilla.Y, false);
                return;
            }

            if (TryGetGemDestination(mapX, mapY, direction, out GemNode? gem))
            {
                DebugLog($"Warping from Gem Isles {mapX},{mapY} {direction} to Gem Isles {gem.X},{gem.Y}.");
                int targetX = direction switch
                {
                    EdgeDirection.East => 0,
                    EdgeDirection.West => Utils.MapWidth - 1,
                    _ => Math.Clamp(sourceTile.X, 0, Utils.MapWidth - 1)
                };
                int targetY = direction switch
                {
                    EdgeDirection.North => Utils.MapHeight - 1,
                    EdgeDirection.South => 0,
                    _ => Math.Clamp(sourceTile.Y, 0, Utils.MapHeight - 1)
                };

                WarpToGemIsles(gem.X, gem.Y, targetX, targetY);
                return;
            }

            DebugLog($"Blocked Gem Isles {mapX},{mapY} {direction}; no transition exists there.");
        }

        private void HandleVanillaEdgeTransition(string locationName, EdgeDirection direction, Point sourceTile, int sourceMapPixelWidth, int sourceMapPixelHeight)
        {
            if (locationName == "Beach" && direction == EdgeDirection.South)
            {
                DebugLog("Warping south from Beach to first Gem Isles screen.");
                int targetX = Math.Clamp(sourceTile.X * Utils.MapWidth / Math.Max(1, sourceMapPixelWidth / Game1.tileSize), 0, Utils.MapWidth - 1);
                WarpToGemIsles(0, 0, targetX, 0);
                return;
            }

            if (locationName == "IslandWest" && direction == EdgeDirection.West)
            {
                DebugLog("Warping west from IslandWest to Gem Isles 0,2.");
                WarpToGemIsles(0, 2, Utils.MapWidth - 1, 40);
                return;
            }

            if (locationName == "IslandWest" && direction == EdgeDirection.South)
            {
                DebugLog("Warping south from IslandWest to Gem Isles 1,3.");
                WarpToGemIsles(1, 3, 70, 0);
                return;
            }

            if (locationName == "IslandSouth" && direction == EdgeDirection.South)
            {
                DebugLog("Warping south from IslandSouth to Gem Isles 2,3.");
                WarpToGemIsles(2, 3, 21, 0);
                return;
            }

            if (locationName == "IslandSouthEast" && direction == EdgeDirection.South)
            {
                DebugLog("Warping south from IslandSouthEast to Gem Isles 3,3.");
                WarpToGemIsles(3, 3, 23, 0);
                return;
            }

            DebugLog($"Blocked {locationName} {direction}; Gem Isles has no transition there.");
        }

        private static bool TryGetGemDestination(int x, int y, EdgeDirection direction, [NotNullWhen(true)] out GemNode? destination)
        {
            destination = null;

            switch ((x, y, direction))
            {
                case (0, 0, EdgeDirection.South): destination = new GemNode(0, 1); return true;

                case (0, 1, EdgeDirection.North): destination = new GemNode(0, 0); return true;
                case (0, 1, EdgeDirection.South): destination = new GemNode(0, 2); return true;
                case (0, 1, EdgeDirection.East): destination = new GemNode(1, 1); return true;
                case (1, 1, EdgeDirection.West): destination = new GemNode(0, 1); return true;
                case (1, 1, EdgeDirection.East): destination = new GemNode(2, 1); return true;
                case (2, 1, EdgeDirection.West): destination = new GemNode(1, 1); return true;
                case (2, 1, EdgeDirection.East): destination = new GemNode(3, 1); return true;
                case (3, 1, EdgeDirection.West): destination = new GemNode(2, 1); return true;
                case (3, 1, EdgeDirection.East): destination = new GemNode(4, 1); return true;
                case (4, 1, EdgeDirection.West): destination = new GemNode(3, 1); return true;
                case (4, 1, EdgeDirection.South): destination = new GemNode(4, 2); return true;

                case (0, 2, EdgeDirection.North): destination = new GemNode(0, 1); return true;
                case (0, 2, EdgeDirection.South): destination = new GemNode(0, 3); return true;
                case (4, 2, EdgeDirection.North): destination = new GemNode(4, 1); return true;
                case (4, 2, EdgeDirection.South): destination = new GemNode(4, 3); return true;

                case (0, 3, EdgeDirection.North): destination = new GemNode(0, 2); return true;
                case (0, 3, EdgeDirection.East): destination = new GemNode(1, 3); return true;
                case (1, 3, EdgeDirection.West): destination = new GemNode(0, 3); return true;
                case (1, 3, EdgeDirection.East): destination = new GemNode(2, 3); return true;
                case (2, 3, EdgeDirection.West): destination = new GemNode(1, 3); return true;
                case (2, 3, EdgeDirection.East): destination = new GemNode(3, 3); return true;
                case (3, 3, EdgeDirection.West): destination = new GemNode(2, 3); return true;
                case (3, 3, EdgeDirection.East): destination = new GemNode(4, 3); return true;
                case (4, 3, EdgeDirection.West): destination = new GemNode(3, 3); return true;
                case (4, 3, EdgeDirection.North): destination = new GemNode(4, 2); return true;
            }

            return false;
        }

        private static bool TryGetVanillaDestination(int x, int y, EdgeDirection direction, [NotNullWhen(true)] out VanillaDestination? destination)
        {
            destination = null;

            switch ((x, y, direction))
            {
                case (0, 0, EdgeDirection.North): destination = new VanillaDestination("Beach", 60, 40); return true;
                case (0, 2, EdgeDirection.East): destination = new VanillaDestination("IslandWest", 5, 40); return true;
                case (1, 3, EdgeDirection.North): destination = new VanillaDestination("IslandWest", 70, 100); return true;
                case (2, 3, EdgeDirection.North): destination = new VanillaDestination("IslandSouth", 21, 56); return true;
                case (3, 3, EdgeDirection.North): destination = new VanillaDestination("IslandSouthEast", 23, 60); return true;
            }

            return false;
        }

        private static bool IsGemIslesLocation(string locationName)
        {
            return locationName.StartsWith(LocationPrefix, StringComparison.Ordinal);
        }

        private void WarpToGemIsles(int destinationMapX, int destinationMapY, int x, int y)
        {
            if (Game1.eventUp)
                return;

            mapX = destinationMapX;
            mapY = destinationMapY;

            string name = $"{LocationPrefix}{mapX}_{mapY}";
            if (Game1.getLocationFromName(name) == null)
            {
                GameLocation location = new GameLocation(VanillaMapPath, name)
                {
                    IsOutdoors = true,
                    IsFarm = false
                };

                Game1.locations.Add(location);
                Helper.GameContent.InvalidateCache("Data/Locations");
                Utils.CreateIslesMap(location);
                Utils.SpawnVanillaForage(location);
            }

            x = Math.Clamp(x, 0, Utils.MapWidth - 1);
            y = Math.Clamp(y, 0, Utils.MapHeight - 1);
            Game1.warpFarmer(name, x, y, false);
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (!Config.EnableMod)
                return;

            if (e.NameWithoutLocale.IsEquivalentTo(VanillaMapPath))
            {
                e.LoadFrom(() => Utils.LoadEmbeddedIslesMap(), AssetLoadPriority.Exclusive);
                return;
            }

            if (TryGetVanillaBeachTilesheetPath(e.NameWithoutLocale.Name, out string? vanillaTilesheetPath))
            {
                string assetPath = vanillaTilesheetPath;
                e.LoadFrom(() => Helper.GameContent.Load<Texture2D>(assetPath), AssetLoadPriority.Exclusive);
                return;
            }

            if (!e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
                return;

            e.Edit(asset =>
            {
                IDictionary<string, LocationData> data = asset.AsDictionary<string, LocationData>().Data;
                LocationData template = data.TryGetValue("Beach", out LocationData? beachData) ? beachData : new LocationData();

                foreach (string isle in Game1.locations.Where(l => l.Name.StartsWith(LocationPrefix, StringComparison.Ordinal)).Select(l => l.Name))
                {
                    if (data.ContainsKey(isle))
                        continue;

                    LocationData isleData = CopyLocationData(template);
                    TrySetMember(isleData, "DisplayName", "Gem Isles");
                    TrySetMember(isleData, "MapPath", VanillaMapPath);
                    TrySetMember(isleData, "CreateOnLoad", null);
                    ConfigureGemIslesLocationData(isleData);
                    data[isle] = isleData;
                }
            });
        }

        private static bool TryGetVanillaBeachTilesheetPath(string assetName, [NotNullWhen(true)] out string? path)
        {
            string normalized = assetName.Replace('\\', '/');
            string? fileName = normalized.Split('/').LastOrDefault();
            path = fileName switch
            {
                "spring_beach" or "spring_beach.png" => "Maps/spring_beach",
                "summer_beach" or "summer_beach.png" => "Maps/summer_beach",
                "fall_beach" or "fall_beach.png" => "Maps/fall_beach",
                "winter_beach" or "winter_beach.png" => "Maps/winter_beach",
                _ => null
            };

            return path != null && (normalized.StartsWith("Maps/GemIslesSwimToGingerIsland/", StringComparison.OrdinalIgnoreCase)
                || normalized.StartsWith("assets/", StringComparison.OrdinalIgnoreCase)
                || !normalized.Contains('/'));
        }

        private static void ConfigureGemIslesLocationData(LocationData data)
        {
            ReplaceSpawnList(data, "Forage", BuildForageEntries);
        }

        private static void ReplaceSpawnList(object data, string memberName, Func<Type, object[]> buildEntries)
        {
            Type? listType = GetMemberType(data.GetType(), memberName);
            Type? entryType = listType?.IsGenericType == true ? listType.GetGenericArguments()[0] : null;
            if (listType == null || entryType == null)
                return;

            object? list = Activator.CreateInstance(listType);
            if (list is not System.Collections.IList targetList)
                return;

            foreach (object entry in buildEntries(entryType))
                targetList.Add(entry);

            TrySetMember(data, memberName, targetList);
        }

        private static object[] BuildForageEntries(Type entryType)
        {
            List<object> entries = new List<object>();
            int maxSpawned = Math.Max(6, (int)Math.Round(6 + Config.MineralPortion * 100 + Config.FaunaPortion * 100 + Config.CoconutPortion * 100));
            int maxDaily = Math.Clamp(maxSpawned / 2, 4, 24);

            AddForageEntries(entries, entryType, Utils.GetTropicalForageObjectIds(), Config.CoconutChance, "coconut", maxDaily, maxSpawned);
            AddForageEntries(entries, entryType, Utils.GetBeachForageObjectIds(), Config.FaunaChance, "beach", maxDaily, maxSpawned);
            AddForageEntries(entries, entryType, Utils.GetGemAndMineralObjectIds(), Config.MineralChance, "mineral", maxDaily, maxSpawned);

            return entries.ToArray();
        }

        private static void AddForageEntries(List<object> entries, Type entryType, IEnumerable<string> itemIds, float chance, string group, int maxDaily, int maxSpawned)
        {
            int index = 0;
            foreach (string itemId in itemIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                object? entry = Activator.CreateInstance(entryType);
                if (entry == null)
                    continue;

                TrySetMember(entry, "Id", $"GemIslesSwimToGingerIsland_{group}_{index++}_{SanitizeId(itemId)}");
                TrySetMember(entry, "ItemId", itemId.StartsWith("(", StringComparison.Ordinal) ? itemId : $"(O){itemId}");
                TrySetMember(entry, "Chance", Math.Clamp(chance, 0f, 1f));
                TrySetMember(entry, "MinDailyForageSpawn", 1);
                TrySetMember(entry, "MaxDailyForageSpawn", maxDaily);
                TrySetMember(entry, "MaxSpawnedForageAtOnce", maxSpawned);
                entries.Add(entry);
            }
        }

        private static string SanitizeId(string itemId)
        {
            return new string(itemId.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        }

        private static Type? GetMemberType(Type type, string name)
        {
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
                return property.PropertyType;

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            return field?.FieldType;
        }

        private static LocationData CopyLocationData(LocationData source)
        {
            LocationData copy = new LocationData();
            Type type = typeof(LocationData);

            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.CanRead && property.CanWrite)
                    property.SetValue(copy, property.GetValue(source));
            }

            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
                field.SetValue(copy, field.GetValue(source));

            return copy;
        }

        private static void TrySetMember(object target, string name, object? value)
        {
            Type type = target.GetType();
            PropertyInfo? property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property?.CanWrite == true)
            {
                property.SetValue(target, ConvertValue(value, property.PropertyType));
                return;
            }

            FieldInfo? field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public);
            if (field != null)
                field.SetValue(target, ConvertValue(value, field.FieldType));
        }

        private static object? ConvertValue(object? value, Type targetType)
        {
            if (value == null)
                return null;

            Type realTargetType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (realTargetType.IsInstanceOfType(value))
                return value;

            if (realTargetType.IsEnum)
                return Enum.ToObject(realTargetType, value);

            return Convert.ChangeType(value, realTargetType);
        }

        private static bool IsPlayerFree()
        {
            return global::StardewModdingAPI.Context.IsWorldReady
                && Game1.player != null
                && Game1.currentLocation != null
                && !Game1.eventUp
                && Game1.activeClickableMenu == null
                && Game1.player.CanMove;
        }

        internal static void DebugLog(string message)
        {
            if (Config.EnableDebugLogging)
                SMonitor.Log(message, LogLevel.Debug);
        }
    }
}
