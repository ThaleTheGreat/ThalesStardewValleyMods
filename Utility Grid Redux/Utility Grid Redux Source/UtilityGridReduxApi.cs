using Microsoft.Xna.Framework;
using StardewValley;

namespace ThaleTheGreat.UtilityGridRedux;

public interface IUtilityGridReduxApi
{
    bool ShowingWaterGrid();
    bool ShowingPowerGrid();
    List<List<Vector2>> LocationWaterPipes(GameLocation location);
    List<List<Vector2>> LocationPowerPipes(GameLocation location);
    Vector2 TileGroupWaterVector(GameLocation location, int tileX, int tileY);
    Vector2 TileGroupPowerVector(GameLocation location, int tileX, int tileY);
    List<Vector2> TileGroupWaterObjects(GameLocation location, int tileX, int tileY);
    List<Vector2> TileGroupPowerObjects(GameLocation location, int tileX, int tileY);
}

public sealed class UtilityGridReduxApi : IUtilityGridReduxApi
{
    public bool ShowingWaterGrid() => ModEntry.ShowingGrid && ModEntry.CurrentGrid == GridKind.Water;
    public bool ShowingPowerGrid() => ModEntry.ShowingGrid && ModEntry.CurrentGrid == GridKind.Power;
    public List<List<Vector2>> LocationWaterPipes(GameLocation location) => ModEntry.GetLocationPipeGroups(location, GridKind.Water);
    public List<List<Vector2>> LocationPowerPipes(GameLocation location) => ModEntry.GetLocationPipeGroups(location, GridKind.Power);
    public Vector2 TileGroupWaterVector(GameLocation location, int tileX, int tileY) => ModEntry.GetTileGroupVector(location, new Vector2(tileX, tileY), GridKind.Water);
    public Vector2 TileGroupPowerVector(GameLocation location, int tileX, int tileY) => ModEntry.GetTileGroupVector(location, new Vector2(tileX, tileY), GridKind.Power);
    public List<Vector2> TileGroupWaterObjects(GameLocation location, int tileX, int tileY) => ModEntry.GetTileGroupObjects(location, new Vector2(tileX, tileY), GridKind.Water);
    public List<Vector2> TileGroupPowerObjects(GameLocation location, int tileX, int tileY) => ModEntry.GetTileGroupObjects(location, new Vector2(tileX, tileY), GridKind.Power);
}
