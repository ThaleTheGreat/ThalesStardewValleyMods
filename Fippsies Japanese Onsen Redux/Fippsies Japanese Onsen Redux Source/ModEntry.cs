using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.GameData.Buildings;
using StardewValley.Locations;
using xTile;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace ThaleTheGreat.FippsiesJapaneseOnsenRedux;

internal sealed class ModEntry : Mod
{
    private const string BigBuildingId = "Fippsie.Onsen";
    private const string SmallBuildingId = "Fippsie.SmallOnsen";

    private const string BigExteriorTexture = "Mods/ThaleTheGreat.FippsiesJapaneseOnsenRedux/BigOnsenExterior";
    private const string SmallExteriorTexture = "Mods/ThaleTheGreat.FippsiesJapaneseOnsenRedux/SmallOnsenExterior";

    private const string BigIndoorMapName = "FippsiesJapaneseOnsenRedux_BigOnsen";
    private const string SmallIndoorMapName = "FippsiesJapaneseOnsenRedux_SmallOnsen";
    private const string BigIndoorMapAsset = "Maps/" + BigIndoorMapName;
    private const string SmallIndoorMapAsset = "Maps/" + SmallIndoorMapName;

    private static readonly Point[] BigEntryTiles =
    {
        new(5, 16)
    };

    private static readonly Point[] SmallEntryTiles =
    {
        new(3, 10)
    };

    private int warpCooldownTicks;

    private Texture2D? onsenTilesTexture;
    private Texture2D? smallOnsenTilesTexture;

    private static readonly AnimatedTile[] BigExteriorAnimatedTiles =
    {
        new(12, 2, new[] { new AnimatedFrame(1148, 250), new AnimatedFrame(1065, 250), new AnimatedFrame(1066, 250), new AnimatedFrame(1067, 250) }),
        new(17, 9, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(13, 10, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(16, 11, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(13, 13, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(20, 13, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(12, 1, new[] { new AnimatedFrame(1121, 250), new AnimatedFrame(1038, 250), new AnimatedFrame(1039, 250), new AnimatedFrame(1040, 250) }),
        new(16, 10, new[] { new AnimatedFrame(772, 250), new AnimatedFrame(773, 250), new AnimatedFrame(774, 250), new AnimatedFrame(775, 250) }),
        new(13, 12, new[] { new AnimatedFrame(745, 300), new AnimatedFrame(746, 300), new AnimatedFrame(747, 300), new AnimatedFrame(748, 300) }),
        new(22, 13, new[] { new AnimatedFrame(772, 250), new AnimatedFrame(773, 250), new AnimatedFrame(774, 250), new AnimatedFrame(775, 250) }),
        new(11, 9, new[] { new AnimatedFrame(521, 200), new AnimatedFrame(548, 200), new AnimatedFrame(575, 200) }),
        new(12, 9, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(16, 9, new[] { new AnimatedFrame(371, 450), new AnimatedFrame(372, 450), new AnimatedFrame(373, 450), new AnimatedFrame(374, 450), new AnimatedFrame(375, 450), new AnimatedFrame(376, 450), new AnimatedFrame(377, 450), new AnimatedFrame(398, 450) }),
        new(12, 10, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) }),
        new(14, 11, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(19, 11, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(11, 12, new[] { new AnimatedFrame(521, 200), new AnimatedFrame(548, 200), new AnimatedFrame(575, 200) }),
        new(14, 12, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) }),
        new(19, 12, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) }),
        new(17, 13, new[] { new AnimatedFrame(371, 450), new AnimatedFrame(372, 450), new AnimatedFrame(373, 450), new AnimatedFrame(374, 450), new AnimatedFrame(375, 450), new AnimatedFrame(376, 450), new AnimatedFrame(377, 450), new AnimatedFrame(398, 450) }),
        new(21, 13, new[] { new AnimatedFrame(371, 450), new AnimatedFrame(372, 450), new AnimatedFrame(373, 450), new AnimatedFrame(374, 450), new AnimatedFrame(375, 450), new AnimatedFrame(376, 450), new AnimatedFrame(377, 450), new AnimatedFrame(398, 450) })
    };

    private static readonly AnimatedTile[] SmallExteriorAnimatedTiles =
    {
        new(10, 7, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(8, 8, new[] { new AnimatedFrame(138, 500), new AnimatedFrame(113, 500) }),
        new(9, 2, new[] { new AnimatedFrame(521, 200), new AnimatedFrame(548, 200), new AnimatedFrame(575, 200) }),
        new(9, 6, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(8, 7, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(9, 7, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) }),
        new(10, 7, new[] { new AnimatedFrame(266, 500), new AnimatedFrame(267, 500), new AnimatedFrame(268, 500), new AnimatedFrame(269, 500) }),
        new(8, 8, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) }),
        new(10, 8, new[] { new AnimatedFrame(293, 500), new AnimatedFrame(294, 500), new AnimatedFrame(295, 500), new AnimatedFrame(296, 500) })
    };



    public override void Entry(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (this.warpCooldownTicks > 0)
        {
            this.warpCooldownTicks--;
            return;
        }

        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null)
            return;

        Point[] playerTiles = GetPlayerTiles();

        if (Game1.currentLocation is Farm farm)
        {
            foreach (Building building in farm.buildings)
            {
                string buildingType = building.buildingType.Value;
                int buildingTileX = building.tileX.Value;
                int buildingTileY = building.tileY.Value;

                if (buildingType == BigBuildingId && IsOnEntryTile(playerTiles, buildingTileX, buildingTileY, BigEntryTiles))
                {
                    WarpIntoOnsen(building, 5, 16);
                    return;
                }

                if (buildingType == SmallBuildingId && IsOnEntryTile(playerTiles, buildingTileX, buildingTileY, SmallEntryTiles))
                {
                    WarpIntoOnsen(building, 3, 11);
                    return;
                }
            }

            return;
        }

        if (IsOnBigExitTile(playerTiles))
        {
            Building? building = FindParentOnsenBuilding(Game1.currentLocation, BigBuildingId);
            if (building is not null)
            {
                WarpOutOfOnsen(building, 5, 17);
                return;
            }
        }

        if (IsOnSmallExitTile(playerTiles))
        {
            Building? building = FindParentOnsenBuilding(Game1.currentLocation, SmallBuildingId);
            if (building is not null)
            {
                WarpOutOfOnsen(building, 3, 11);
                return;
            }
        }
    }

    private static Point[] GetPlayerTiles()
    {
        Rectangle boundingBox = Game1.player.GetBoundingBox();
        return new[]
        {
            new Point(boundingBox.Center.X / Game1.tileSize, (boundingBox.Bottom - 1) / Game1.tileSize),
            new Point(boundingBox.Center.X / Game1.tileSize, boundingBox.Center.Y / Game1.tileSize),
            new Point((int)(Game1.player.Position.X / Game1.tileSize), (int)(Game1.player.Position.Y / Game1.tileSize))
        };
    }

    private static bool IsOnBigExitTile(IEnumerable<Point> playerTiles)
    {
        return ContainsTile(playerTiles, 5, 18) || ContainsTile(playerTiles, 5, 19);
    }

    private static bool IsOnSmallExitTile(IEnumerable<Point> playerTiles)
    {
        return ContainsTile(playerTiles, 3, 12) || ContainsTile(playerTiles, 3, 13);
    }

    private static bool ContainsTile(IEnumerable<Point> tiles, int x, int y)
    {
        foreach (Point tile in tiles)
        {
            if (tile.X == x && tile.Y == y)
                return true;
        }

        return false;
    }

    private static Building? FindParentOnsenBuilding(GameLocation location, string buildingId)
    {
        foreach (Building building in Game1.getFarm().buildings)
        {
            if (building.buildingType.Value != buildingId)
                continue;

            if (building.GetIndoors() == location)
                return building;

            string? indoorsName = building.GetIndoorsName();
            if (!string.IsNullOrWhiteSpace(indoorsName) && indoorsName == location.NameOrUniqueName)
                return building;
        }

        return null;
    }

    private static bool IsOnEntryTile(IEnumerable<Point> playerTiles, int buildingTileX, int buildingTileY, IEnumerable<Point> entryTiles)
    {
        foreach (Point playerTile in playerTiles)
        {
            foreach (Point entryTile in entryTiles)
            {
                if (playerTile.X == buildingTileX + entryTile.X && playerTile.Y == buildingTileY + entryTile.Y)
                    return true;
            }
        }

        return false;
    }

    private void WarpIntoOnsen(Building building, int targetX, int targetY)
    {
        GameLocation? indoors = building.GetIndoors();
        string? indoorsName = building.GetIndoorsName();
        if (indoors is null || string.IsNullOrWhiteSpace(indoorsName))
            return;

        this.warpCooldownTicks = 120;
        LocationRequest request = new(indoorsName, true, indoors);
        Game1.warpFarmer(request, targetX, targetY, Game1.player.FacingDirection);
    }

    private void WarpOutOfOnsen(Building building, int relativeX, int relativeY)
    {
        this.warpCooldownTicks = 120;
        Farm farm = Game1.getFarm();
        LocationRequest request = new("Farm", false, farm);
        Game1.warpFarmer(request, building.tileX.Value + relativeX, building.tileY.Value + relativeY, Game1.player.FacingDirection);
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || Game1.currentLocation is not Farm farm)
            return;

        this.onsenTilesTexture ??= this.Helper.ModContent.Load<Texture2D>("assets/OnsenTiles.png");
        this.smallOnsenTilesTexture ??= this.Helper.ModContent.Load<Texture2D>("assets/SmallOnsenTiles.png");

        foreach (Building building in farm.buildings)
        {
            string buildingType = building.buildingType.Value;
            if (buildingType == BigBuildingId)
                DrawAnimatedExteriorTiles(e.SpriteBatch, building, this.onsenTilesTexture, BigExteriorAnimatedTiles);
            else if (buildingType == SmallBuildingId)
                DrawAnimatedExteriorTiles(e.SpriteBatch, building, this.smallOnsenTilesTexture, SmallExteriorAnimatedTiles);
        }
    }

    private static void DrawAnimatedExteriorTiles(SpriteBatch b, Building building, Texture2D tilesheet, IEnumerable<AnimatedTile> tiles)
    {
        foreach (AnimatedTile tile in tiles)
        {
            int tileId = GetCurrentAnimatedTileId(tile.Frames);
            Rectangle source = new((tileId % 27) * 16, (tileId / 27) * 16, 16, 16);
            Vector2 position = new(
                (building.tileX.Value + tile.X) * Game1.tileSize - Game1.viewport.X,
                (building.tileY.Value + tile.Y) * Game1.tileSize - Game1.viewport.Y
            );

            float layerDepth = Math.Max(0f, ((building.tileY.Value + building.tilesHigh.Value - 8f) * Game1.tileSize) / 10000f);
            b.Draw(tilesheet, position, source, Color.White, 0f, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, layerDepth);
        }
    }

    private static int GetCurrentAnimatedTileId(IReadOnlyList<AnimatedFrame> frames)
    {
        int totalDuration = 0;
        foreach (AnimatedFrame frame in frames)
            totalDuration += frame.Duration;

        int time = (int)(Game1.currentGameTime.TotalGameTime.TotalMilliseconds % totalDuration);
        foreach (AnimatedFrame frame in frames)
        {
            if (time < frame.Duration)
                return frame.TileId;

            time -= frame.Duration;
        }

        return frames[0].TileId;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(BigExteriorTexture))
            e.LoadFromModFile<Texture2D>("assets/BigOnsenExterior.png", AssetLoadPriority.Medium);
        else if (e.NameWithoutLocale.IsEquivalentTo(SmallExteriorTexture))
            e.LoadFromModFile<Texture2D>("assets/SmallOnsenExterior.png", AssetLoadPriority.Medium);
        else if (e.NameWithoutLocale.IsEquivalentTo(BigIndoorMapAsset))
        {
            e.LoadFromModFile<Map>("assets/Onsen.tmx", AssetLoadPriority.Medium);
            e.Edit(asset => ConfigureBigOnsenMap(asset.AsMap().Data));
        }
        else if (e.NameWithoutLocale.IsEquivalentTo(SmallIndoorMapAsset))
        {
            e.LoadFromModFile<Map>("assets/SmallOnsen.tmx", AssetLoadPriority.Medium);
            e.Edit(asset => ConfigureSmallOnsenMap(asset.AsMap().Data));
        }
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, BuildingData> data = asset.AsDictionary<string, BuildingData>().Data;
                data[BigBuildingId] = CreateBigOnsenData();
                data[SmallBuildingId] = CreateSmallOnsenData();
            });
        }
    }

    private BuildingData CreateBigOnsenData()
    {
        BuildingData data = new()
        {
            Name = Helper.Translation.Get("building.big.name").ToString(),
            Description = Helper.Translation.Get("building.big.description").ToString(),
            Texture = BigExteriorTexture,
            SourceRect = new Rectangle(0, 16, 400, 272),
            Builder = "Robin",
            BuildCost = 45000,
            BuildDays = 3,
            Size = new Point(25, 17),
            CollisionMap = @"OOOOOOOOOOOOOOOOOOOOOOOOO
OOOOOOOOOOOOOOOOOOOOOOOOO
XXXXXXXXXXXXXXXXXXXXXXXXX
XXXXXXXXXXXXXXXXXXXXXXXXX
XXXXXXXXXXXXXXXXXXXXXXXXX
XXXXXXXXXXXXXXXXXXXXXXXXX
XXXXXXXXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX
XXXXOOOXXXXXXXXXXXXXXXXXX",
            SortTileOffset = 8f,
            AllowsFlooringUnderneath = false
        };
        SetMember(data, "BuildMaterials", new List<BuildingMaterial>
        {
            new() { ItemId = "390", Amount = 750 },
            new() { ItemId = "709", Amount = 300 },
            new() { ItemId = "330", Amount = 100 },
            new() { ItemId = "338", Amount = 30 }
        });
        SetMember(data, "IndoorMap", BigIndoorMapName);
        SetMember(data, "IndoorMapType", "StardewValley.Locations.DecoratableLocation");
        return data;
    }

    private BuildingData CreateSmallOnsenData()
    {
        BuildingData data = new()
        {
            Name = Helper.Translation.Get("building.small.name").ToString(),
            Description = Helper.Translation.Get("building.small.description").ToString(),
            Texture = SmallExteriorTexture,
            SourceRect = new Rectangle(0, 16, 208, 176),
            Builder = "Robin",
            BuildCost = 15000,
            BuildDays = 2,
            Size = new Point(13, 11),
            CollisionMap = @"OOOOOOOOOOOOO
OOOOOOOOOOOOO
XXXXXXXXXXXXX
XXXXXXXXXXXXX
XXXXXXXXXXXXX
XXXXXXXXXXXXX
XXOOOXXXXXXXX
XXOOOXXXXXXXX
XXOOOXXXXXXXX
XXOOOXXXXXXXX
XXOOOXXXXXXXX",
            SortTileOffset = 5f,
            AllowsFlooringUnderneath = false
        };
        SetMember(data, "BuildMaterials", new List<BuildingMaterial>
        {
            new() { ItemId = "390", Amount = 250 },
            new() { ItemId = "709", Amount = 100 },
            new() { ItemId = "330", Amount = 35 },
            new() { ItemId = "338", Amount = 10 }
        });
        SetMember(data, "IndoorMap", SmallIndoorMapName);
        SetMember(data, "IndoorMapType", "StardewValley.Locations.DecoratableLocation");
        return data;
    }

    private static void ConfigureBigOnsenMap(Map map)
    {
        SetBrightInteriorLighting(map);
        SetTouchAction(map, 18, 6, "ChangeOutOfSwimsuit");
        SetTouchAction(map, 19, 6, "ChangeIntoSwimsuit");
        SetTouchAction(map, 21, 12, "PoolEntrance");
    }

    private static void ConfigureSmallOnsenMap(Map map)
    {
        SetBrightInteriorLighting(map);
        SetTouchAction(map, 3, 5, "ChangeOutOfSwimsuit");
        SetTouchAction(map, 4, 5, "ChangeIntoSwimsuit");
        SetTouchAction(map, 9, 7, "PoolEntrance");
    }

    private static void SetBrightInteriorLighting(Map map)
    {
        map.Properties["AmbientLight"] = new PropertyValue("0 0 0");
        map.Properties["AmbientNightLight"] = new PropertyValue("0 0 0");
    }


    private static void SetTouchAction(Map map, int x, int y, string action)
    {
        Layer layer = map.GetLayer("Back") ?? throw new InvalidOperationException("Onsen map has no Back layer.");
        Tile tile = layer.Tiles[x, y] ?? throw new InvalidOperationException($"Onsen map has no Back tile at {x}, {y}.");
        tile.Properties["TouchAction"] = new PropertyValue(action);
    }

    private static void SetMember(object target, string name, object? value)
    {
        Type type = target.GetType();
        System.Reflection.PropertyInfo? property = type.GetProperty(name);
        if (property is not null)
        {
            property.SetValue(target, value);
            return;
        }

        System.Reflection.FieldInfo? field = type.GetField(name);
        if (field is not null)
        {
            field.SetValue(target, value);
            return;
        }

        throw new InvalidOperationException($"{type.FullName} has no public {name} property or field.");
    }

    private readonly record struct AnimatedFrame(int TileId, int Duration);

    private readonly record struct AnimatedTile(int X, int Y, AnimatedFrame[] Frames);

}
