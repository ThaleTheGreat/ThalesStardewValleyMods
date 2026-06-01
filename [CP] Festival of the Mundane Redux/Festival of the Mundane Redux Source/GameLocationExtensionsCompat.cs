using Microsoft.Xna.Framework;
using Netcode;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Objects;
using xTile.Dimensions;

namespace ShadowFestival;

internal static class GameLocationExtensionsCompat
{
  public static bool isTileOccupiedForPlacement(
    this GameLocation location,
    Vector2 tileLocation,
    StardewValley.Object toPlace = null)
  {
    return location.CanItemBePlacedHere(tileLocation, toPlace != null && toPlace.isPassable(), (CollisionMask) (int) byte.MaxValue, (CollisionMask) 223, false, false);
  }

  public static bool isTileOccupied(
    this GameLocation location,
    Vector2 tileLocation,
    string characterToIgnore = "",
    bool ignoreAllCharacters = false)
  {
    CollisionMask collisionMask = ignoreAllCharacters ? (CollisionMask) 249 : (CollisionMask) (int) byte.MaxValue;
    return location.IsTileOccupiedBy(tileLocation, collisionMask, (CollisionMask) 0, false);
  }

  public static bool isTileOccupiedIgnoreFloors(
    this GameLocation location,
    Vector2 tileLocation,
    string characterToIgnore = "")
  {
    return location.IsTileOccupiedBy(tileLocation, (CollisionMask) 115, (CollisionMask) 8, false);
  }

  public static bool isTileLocationOpenIgnoreFrontLayers(this GameLocation location, Location tile)
  {
    return FrameworkExtensions.RequireLayer(location.map, "Buildings").Tiles[tile.X, tile.Y] == null && !location.isWaterTile(tile.X, tile.Y);
  }

  public static bool isTileLocationTotallyClearAndPlaceable(
    this GameLocation location,
    int x,
    int y)
  {
    return location.isTileLocationTotallyClearAndPlaceable(new Vector2((float) x, (float) y));
  }

  public static bool isTileLocationTotallyClearAndPlaceableIgnoreFloors(
    this GameLocation location,
    Vector2 v)
  {
    return location.isTileOnMap(v) && !location.isTileOccupiedIgnoreFloors(v) && location.isTilePassable(new Location((int) v.X, (int) v.Y), Game1.viewport) && location.isTilePlaceable(v, false);
  }

  public static bool isTileLocationTotallyClearAndPlaceable(this GameLocation location, Vector2 v)
  {
    Vector2 vector2;
    vector2 = new Vector2((float) ((double) v.X * 64.0 + 32.0), (float) ((double) v.Y * 64.0 + 32.0));
    foreach (Furniture furniture in location.furniture)
    {
      int num;
      if (((NetFieldBase<int, NetInt>) furniture.furniture_type).Value != 12 && !((StardewValley.Object) furniture).isPassable())
      {
        Microsoft.Xna.Framework.Rectangle boundingBox = ((StardewValley.Object) furniture).GetBoundingBox();
        if (boundingBox.Contains((int) vector2.X, (int) vector2.Y))
        {
          num = !furniture.AllowPlacementOnThisTile((int) v.X, (int) v.Y) ? 1 : 0;
          goto label_6;
        }
      }
      num = 0;
label_6:
      if (num != 0)
        return false;
    }
    return location.isTileOnMap(v) && !location.isTileOccupied(v) && location.isTilePassable(new Location((int) v.X, (int) v.Y), Game1.viewport) && location.isTilePlaceable(v, false);
  }
}
