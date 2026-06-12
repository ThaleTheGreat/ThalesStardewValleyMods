using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Reflection;
using xTile;
using xTile.Format;
using xTile.Layers;
using xTile.Tiles;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.GemIslesSwimToGingerIsland
{
    internal class Utils
    {
        private static IModHelper Helper = null!;
        private static IMonitor Monitor = null!;
        private static ModConfig Config = null!;

        public const int MapWidth = 128;
        public const int MapHeight = 72;

        private static readonly string[] VanillaGemAndMineralIds =
        {
            "60", "62", "64", "66", "68", "70", "72", "74", "80", "82", "84", "86"
        };

        internal static void Initialize(ModConfig config, IMonitor monitor, IModHelper helper)
        {
            Helper = helper;
            Monitor = monitor;
            Config = config;
        }

        internal static Map LoadEmbeddedIslesMap()
        {
            string tempDir = Path.Combine(Path.GetTempPath(), "GemIslesSwimToGingerIsland");
            Directory.CreateDirectory(tempDir);
            string tempPath = Path.Combine(tempDir, "isles.tbin");
            File.WriteAllBytes(tempPath, EmbeddedIslesMapData.Bytes);
            return FormatManager.Instance.LoadMap(tempPath);
        }

        internal static void CreateIslesMap(GameLocation location)
        {
            _ = Helper;
            _ = Monitor;

            location.loadMap(ModEntry.VanillaMapPath, true);
            Layer backLayer = RequireLayer(location.map, "Back");
            Layer buildingsLayer = RequireLayer(location.map, "Buildings");
            Layer? frontLayer = GetLayerOrNull(location.map, "Front");
            Layer? alwaysFrontLayer = GetLayerOrNull(location.map, "AlwaysFront");
            TileSheet sheet = RequireTileSheet(location.map);

            int width = backLayer.LayerWidth;
            int height = backLayer.LayerHeight;

            ClearLayer(buildingsLayer);
            ClearLayer(frontLayer);
            ClearLayer(alwaysFrontLayer);
            FillBackLayerWithWater(backLayer, sheet);

            List<Rectangle> isleBoxes = BuildIslandBoxes(width, height);
            foreach (Rectangle isleBox in isleBoxes)
            {
                bool[] landTiles = BuildIslandShape(isleBox);
                PaintIsland(backLayer, buildingsLayer, sheet, isleBox, landTiles);
            }

            RebuildWaterTiles(location, width, height);
            PopulateIslands(location, isleBoxes, width, height);
        }

        private static void ClearLayer(Layer? layer)
        {
            if (layer == null)
                return;

            for (int x = 0; x < layer.LayerWidth; x++)
            {
                for (int y = 0; y < layer.LayerHeight; y++)
                    layer.Tiles[x, y] = null;
            }
        }

        private static Layer RequireLayer(Map map, string name)
        {
            return GetLayerOrNull(map, name)
                ?? throw new InvalidOperationException($"Gem Isles template map is missing required '{name}' layer.");
        }

        private static Layer? GetLayerOrNull(Map map, string name)
        {
            try
            {
                return map.GetLayer(name);
            }
            catch
            {
                return null;
            }
        }

        private static TileSheet RequireTileSheet(Map map)
        {
            return map.TileSheets.Count > 0
                ? map.TileSheets[0]
                : throw new InvalidOperationException("Gem Isles template map is missing its beach tilesheet.");
        }

        private static void FillBackLayerWithWater(Layer backLayer, TileSheet sheet)
        {
            for (int x = 0; x < backLayer.LayerWidth; x++)
            {
                for (int y = 0; y < backLayer.LayerHeight; y++)
                    backLayer.Tiles[x, y] = new StaticTile(backLayer, sheet, BlendMode.Alpha, GetRandomOceanTile());
            }
        }

        private static List<Rectangle> BuildIslandBoxes(int mapWidth, int mapHeight)
        {
            int isles = Game1.random.Next(1, Math.Max(1, Config.MaxIsles) + 1);
            double coeff = Math.Sqrt(mapWidth * mapHeight / (float)isles / 144f);
            Rectangle usableBounds = new Rectangle(
                Math.Max(2, (int)(coeff * 16 / 8f)),
                Math.Max(2, (int)(coeff * 9 / 8f)),
                Math.Max(1, mapWidth - (int)(coeff * 16 / 4f)),
                Math.Max(1, mapHeight - (int)(coeff * 9 / 4f))
            );

            List<Point> points = new List<Point>();
            for (int x = usableBounds.Left; x < usableBounds.Right; x++)
            {
                for (int y = usableBounds.Top; y < usableBounds.Bottom; y++)
                    points.Add(new Point(x, y));
            }

            List<Rectangle> boxes = new List<Rectangle>();
            for (int i = 0; i < isles; i++)
            {
                int minWidth = Math.Max(8, (int)(coeff * 16 / 2f));
                int maxWidth = Math.Max(minWidth + 1, (int)(coeff * 16 * 3 / 4f));
                int minHeight = Math.Max(6, (int)(coeff * 9 / 2f));
                int maxHeight = Math.Max(minHeight + 1, (int)(coeff * 9 * 3 / 4f));

                int islandWidth = Math.Min(Game1.random.Next(minWidth, maxWidth), mapWidth - 4);
                int islandHeight = Math.Min(Game1.random.Next(minHeight, maxHeight), mapHeight - 4);
                List<Point> freePoints = new List<Point>();

                foreach (Point point in points)
                {
                    Rectangle testRect = new Rectangle(point.X - islandWidth / 2, point.Y - islandHeight / 2, islandWidth, islandHeight);
                    if (testRect.Left < 1 || testRect.Top < 1 || testRect.Right >= mapWidth - 1 || testRect.Bottom >= mapHeight - 1)
                        continue;

                    if (boxes.All(box => !box.Intersects(testRect)))
                        freePoints.Add(point);
                }

                if (freePoints.Count == 0)
                    continue;

                Point center = freePoints[Game1.random.Next(freePoints.Count)];
                boxes.Add(new Rectangle(center.X - islandWidth / 2, center.Y - islandHeight / 2, islandWidth, islandHeight));
            }

            DebugLog($"Generated {boxes.Count} island box(es).");
            return boxes;
        }

        private static void PaintIsland(Layer backLayer, Layer buildingsLayer, TileSheet sheet, Rectangle isleBox, bool[] landTiles)
        {
            for (int x = 0; x < isleBox.Width; x++)
            {
                for (int y = 0; y < isleBox.Height; y++)
                {
                    int index = y * isleBox.Width + x;
                    bool[] surround = GetSurroundingTiles(isleBox, landTiles, index);
                    int mapX = isleBox.X + x;
                    int mapY = isleBox.Y + y;

                    if (landTiles[index])
                    {
                        if (surround.Count(value => value) == 8)
                        {
                            backLayer.Tiles[mapX, mapY] = new StaticTile(backLayer, sheet, BlendMode.Alpha, GetRandomLandTile());
                            continue;
                        }

                        if (!surround[1])
                        {
                            if (!surround[3])
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 51);
                            else if (!surround[4])
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 54);
                            else if (buildingsLayer.Tiles[mapX - 1, mapY]?.TileIndex == 52)
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 53);
                            else
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 52);
                        }
                        else if (!surround[6])
                        {
                            if (!surround[3])
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 51);
                            else if (!surround[4])
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 54);
                            else if (buildingsLayer.Tiles[mapX - 1, mapY]?.TileIndex == 52)
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 53);
                            else
                                buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 52);

                            buildingsLayer.Tiles[mapX, mapY].Properties.Add("@Flip", 2);
                        }
                        else if (!surround[3])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 68);
                        else if (!surround[4])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 71);
                        else if (!surround[0])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 70);
                        else if (!surround[2])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 69);
                        else if (!surround[5])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 87);
                        else if (!surround[7])
                            buildingsLayer.Tiles[mapX, mapY] = new StaticTile(buildingsLayer, sheet, BlendMode.Alpha, 86);
                    }
                    else if (surround.Any(value => value))
                    {
                        Tile? edgeTile = null;
                        if (surround[1])
                        {
                            if (surround[3])
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 226);
                            else if (surround[4])
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 175);
                            else if (mapX > 0 && buildingsLayer.Tiles[mapX - 1, mapY]?.TileIndex == 141)
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 158);
                            else
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 141);
                        }
                        else if (surround[6])
                        {
                            if (surround[3])
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 226);
                            else if (surround[4])
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 175);
                            else if (mapX > 0 && buildingsLayer.Tiles[mapX - 1, mapY]?.TileIndex == 141)
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 158);
                            else
                                edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 141);

                            edgeTile.Properties.Add("@Flip", 2);
                        }
                        else if (surround[3])
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 260);
                        else if (surround[4])
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 209);
                        else if (surround[0])
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 243);
                        else if (surround[2])
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 192);
                        else if (surround[5])
                        {
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 243);
                            edgeTile.Properties.Add("@Flip", 2);
                        }
                        else if (surround[7])
                        {
                            edgeTile = CreateAnimatedTile(buildingsLayer, sheet, 192);
                            edgeTile.Properties.Add("@Flip", 2);
                        }

                        if (edgeTile != null)
                        {
                            edgeTile.Properties["Passable"] = "T";
                            buildingsLayer.Tiles[mapX, mapY] = edgeTile;
                        }
                    }
                }
            }
        }

        private static bool[] BuildIslandShape(Rectangle isleBox)
        {
            bool[] landTiles = Enumerable.Repeat(true, isleBox.Width * isleBox.Height).ToArray();

            for (int x = 0; x < isleBox.Width; x++)
            {
                for (int y = 0; y < isleBox.Height; y++)
                {
                    int index = y * isleBox.Width + x;
                    if (x == 0 || x == isleBox.Width - 1 || y == 0 || y == isleBox.Height - 1)
                    {
                        landTiles[index] = false;
                        continue;
                    }

                    float widthOffset = Math.Abs(isleBox.Width / 2f - x) / (isleBox.Width / 2f);
                    float heightOffset = Math.Abs(isleBox.Height / 2f - y) / (isleBox.Height / 2f);
                    double distance = Math.Sqrt(Math.Pow(widthOffset, 2) + Math.Pow(heightOffset, 2));
                    double land = 1 - distance;

                    foreach (bool nearbyLand in GetSurroundingTiles(isleBox, landTiles, index))
                    {
                        if (!nearbyLand)
                            land -= 0.025;
                    }

                    landTiles[index] = Game1.random.NextDouble() < land;
                }
            }

            SmoothIslandShape(isleBox, landTiles);
            return landTiles;
        }

        private static void SmoothIslandShape(Rectangle isleBox, bool[] landTiles)
        {
            for (int pass = 0; pass < 8; pass++)
            {
                bool changed = false;
                for (int x = 1; x < isleBox.Width - 1; x++)
                {
                    for (int y = 1; y < isleBox.Height - 1; y++)
                    {
                        int index = y * isleBox.Width + x;
                        bool[] surround = GetSurroundingTiles(isleBox, landTiles, index);
                        int landCount = surround.Count(value => value);

                        if (!landTiles[index] && landCount >= 5 && Game1.random.NextDouble() < 0.85)
                        {
                            landTiles[index] = true;
                            changed = true;
                        }
                        else if (landTiles[index] && landCount <= 2 && Game1.random.NextDouble() < 0.25)
                        {
                            landTiles[index] = false;
                            changed = true;
                        }
                    }
                }

                if (!changed)
                    break;
            }
        }

        private static void RebuildWaterTiles(GameLocation location, int width, int height)
        {
            bool[,] waterTiles = new bool[width, height];
            location.waterTiles = new WaterTiles(waterTiles);

            for (int x = 0; x < width; x++)
            {
                for (int y = 0; y < height; y++)
                {
                    if (location.doesTileHaveProperty(x, y, "Water", "Back") != null)
                        location.waterTiles[x, y] = true;
                }
            }
        }

        private static void PopulateIslands(GameLocation location, List<Rectangle> isleBoxes, int width, int height)
        {
            foreach (Rectangle isleBox in isleBoxes)
            {
                List<Vector2> freeSpots = GetFreeSpots(location, isleBox, width, height);
                if (freeSpots.Count == 0)
                    continue;

                Shuffle(freeSpots);
                int taken = 0;

                taken = TryPlaceTreasure(location, freeSpots, taken);
                taken = TryPlaceTrees(location, freeSpots, taken);
                taken = TryPlaceArtifacts(location, freeSpots, taken);
                taken = TryPlaceMonsters(location, freeSpots, taken);
                TryPlaceGrass(location, freeSpots, taken);
            }
        }

        private static List<Vector2> GetFreeSpots(GameLocation location, Rectangle isleBox, int width, int height)
        {
            List<Vector2> spots = new List<Vector2>();
            for (int x = Math.Max(1, isleBox.Left); x < Math.Min(width - 1, isleBox.Right); x++)
            {
                for (int y = Math.Max(1, isleBox.Top); y < Math.Min(height - 1, isleBox.Bottom); y++)
                {
                    if (location.doesTileHaveProperty(x, y, "Water", "Back") == null)
                        spots.Add(new Vector2(x, y));
                }
            }

            return spots;
        }

        private static int TryPlaceTreasure(GameLocation location, List<Vector2> spots, int taken)
        {
            if (taken < spots.Count && Game1.random.NextDouble() < Config.TreasureChance)
            {
                PlaceTreasureChest(location, spots[taken]);
                taken++;
            }

            return taken;
        }

        private static int TryPlaceTrees(GameLocation location, List<Vector2> spots, int taken)
        {
            if (Game1.random.NextDouble() >= Config.TreesChance)
                return taken;

            int trees = Math.Min(spots.Count - taken, (int)(spots.Count * Math.Min(1, Config.TreesPortion)));
            for (int i = 0; i < trees; i++)
                location.terrainFeatures[spots[i + taken]] = new Tree("6", 5);

            return taken + trees;
        }

        private static int TryPlaceArtifacts(GameLocation location, List<Vector2> spots, int taken)
        {
            if (Game1.random.NextDouble() >= Config.ArtifactChance)
                return taken;

            int count = Math.Min(spots.Count - taken, (int)(spots.Count * Math.Min(1, Config.ArtifactPortion)));
            for (int i = 0; i < count; i++)
                PlaceArtifactSpot(location, spots[i + taken]);

            return taken + count;
        }

        private static int TryPlaceMonsters(GameLocation location, List<Vector2> spots, int taken)
        {
            if (Game1.random.NextDouble() >= Config.MonsterChance)
                return taken;

            int count = Math.Min(spots.Count - taken, (int)(spots.Count * Math.Min(1, Config.MonsterPortion)));
            double type = Game1.random.NextDouble();

            for (int i = 0; i < count; i++)
            {
                Vector2 position = spots[i + taken] * Game1.tileSize;
                if (type < 0.2)
                    location.characters.Add(new Skeleton(position));
                else if (type < 0.3)
                    location.characters.Add(new DinoMonster(position));
                else if (type < 0.5)
                    location.characters.Add(new RockGolem(position, 10));
                else
                    location.characters.Add(new RockCrab(position));
            }

            return taken + count;
        }

        private static int TryPlaceGrass(GameLocation location, List<Vector2> spots, int taken)
        {
            if (Game1.random.NextDouble() >= Config.GrassChance)
                return taken;

            int count = Math.Min(spots.Count - taken, (int)(spots.Count * Math.Min(1, Config.GrassPortion)));
            for (int i = 0; i < count; i++)
                location.terrainFeatures[spots[i + taken]] = new Grass(Game1.random.Next(2, 5), Game1.random.Next(1, 3));

            return taken + count;
        }

        private static void PlaceTreasureChest(GameLocation location, Vector2 tile)
        {
            Chest chest = new Chest(true)
            {
                TileLocation = tile
            };
            chest.Items.Add(MineShaft.getTreasureRoomItem());
            location.overlayObjects[tile] = chest;
        }

        private static void PlaceArtifactSpot(GameLocation location, Vector2 tile)
        {
            SObject obj = CreatePlacedObject("590", tile, 1);
            location.objects[tile] = obj;
        }

        private static SObject CreatePlacedObject(string itemId, Vector2 tile, int stack)
        {
            if (ItemRegistry.Create($"(O){itemId}", stack, allowNull: true) is SObject obj)
            {
                obj.TileLocation = tile;
                return obj;
            }

            SObject fallback = new SObject(itemId, stack)
            {
                TileLocation = tile
            };
            return fallback;
        }

        internal static IEnumerable<string> GetTropicalForageObjectIds()
        {
            return GetObjectIds(IsTropicalForage, "88");
        }

        internal static IEnumerable<string> GetBeachForageObjectIds()
        {
            return GetObjectIds(IsBeachForage, "393", "397", "152");
        }

        internal static IEnumerable<string> GetGemAndMineralObjectIds()
        {
            return GetObjectIds(IsGemOrMineralPickup, VanillaGemAndMineralIds);
        }

        private static IEnumerable<string> GetObjectIds(Func<ObjectData, bool> predicate, params string[] fallbackIds)
        {
            foreach (string fallbackId in fallbackIds)
                yield return fallbackId;

            if (!Config.UseModdedObjectData)
                yield break;

            foreach (KeyValuePair<string, ObjectData> pair in Game1.objectData)
            {
                if (pair.Value != null && predicate(pair.Value) && ItemRegistry.Exists($"(O){pair.Key}"))
                    yield return pair.Key;
            }
        }

        internal static void SpawnVanillaForage(GameLocation location)
        {
            MethodInfo? spawnObjects = typeof(GameLocation).GetMethod("spawnObjects", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
            if (spawnObjects == null)
                return;

            try
            {
                spawnObjects.Invoke(location, null);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Gem Isles Swim to Ginger Island could not run vanilla forage spawning for {location.Name}: {ex.GetBaseException().Message}", LogLevel.Warn);
            }
        }

        private static bool IsTropicalForage(ObjectData data)
        {
            return HasContextTag(data, "item_coconut")
                || HasContextTag(data, "category_fruits")
                || data.Category == SObject.FruitsCategory;
        }

        private static bool IsBeachForage(ObjectData data)
        {
            return HasContextTag(data, "location_beach")
                || data.Category == SObject.sellAtFishShopCategory;
        }

        private static bool IsGemOrMineralPickup(ObjectData data)
        {
            return data.Category == SObject.mineralsCategory
                || data.Category == SObject.GemCategory
                || HasContextTag(data, "category_minerals")
                || HasContextTag(data, "category_gem");
        }

        private static bool HasContextTag(ObjectData data, string tag)
        {
            return data.ContextTags?.Any(t => string.Equals(t, tag, StringComparison.OrdinalIgnoreCase)) == true;
        }

        private static AnimatedTile CreateAnimatedTile(Layer layer, TileSheet sheet, int firstTileIndex)
        {
            StaticTile[] frames =
            {
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 1),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 2),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 3),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 3),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 4),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 5),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex + 6),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex),
                new StaticTile(layer, sheet, BlendMode.Alpha, firstTileIndex)
            };

            return new AnimatedTile(layer, frames, 250);
        }

        private static int GetRandomOceanTile()
        {
            double roll = Game1.random.NextDouble();
            int[] detailTiles = { 458, 185, 475, 130 };
            const double chance = 0.02;

            for (int i = 0; i < detailTiles.Length; i++)
            {
                if (roll < chance * (i + 1))
                    return detailTiles[i];
            }

            return 75;
        }

        private static int GetRandomLandTile()
        {
            double roll = Game1.random.NextDouble();
            int[] detailTiles = { 18, 168, 25, 43 };
            const double chance = 0.02;

            for (int i = 0; i < detailTiles.Length; i++)
            {
                if (roll < chance * (i + 1))
                    return detailTiles[i];
            }

            return 42;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            int n = list.Count;
            while (n > 1)
            {
                n--;
                int k = Game1.random.Next(n + 1);
                (list[k], list[n]) = (list[n], list[k]);
            }
        }

        private static bool[] GetSurroundingTiles(Rectangle isleBox, bool[] landTiles, int index)
        {
            bool[] list = new bool[8];
            if (index >= isleBox.Width)
            {
                if (index % isleBox.Width > 0)
                    list[0] = landTiles[index - 1 - isleBox.Width];
                list[1] = landTiles[index - isleBox.Width];
                if (index % isleBox.Width < isleBox.Width - 1)
                    list[2] = landTiles[index + 1 - isleBox.Width];
            }
            if (index % isleBox.Width > 0)
                list[3] = landTiles[index - 1];
            if (index % isleBox.Width < isleBox.Width - 1)
                list[4] = landTiles[index + 1];
            if (index < landTiles.Length - isleBox.Width)
            {
                if (index % isleBox.Width > 0)
                    list[5] = landTiles[index - 1 + isleBox.Width];
                list[6] = landTiles[index + isleBox.Width];
                if (index % isleBox.Width < isleBox.Width - 1)
                    list[7] = landTiles[index + 1 + isleBox.Width];
            }
            return list;
        }

        private static void DebugLog(string message)
        {
            ModEntry.DebugLog(message);
        }

    }
}
