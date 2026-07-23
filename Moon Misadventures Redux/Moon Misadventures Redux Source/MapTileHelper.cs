using System;
using StardewValley;
using xTile.Layers;
using xTile.Tiles;

namespace ThaleTheGreat.MoonMisadventures
{
    public static class MapTileHelper
    {
        public static void SetMapTile(GameLocation location, int tileX, int tileY, int index, string layerName, string? action, int tileSheetIndex = 0)
        {
            Layer layer = location.Map.GetLayer(layerName) ?? throw new InvalidOperationException($"Map layer '{layerName}' was not found.");
            var tile = new StaticTile(layer, location.Map.TileSheets[tileSheetIndex], BlendMode.Alpha, index);
            layer.Tiles[tileX, tileY] = tile;
            if (action is not null && layerName == "Buildings")
                tile.Properties.Add("Action", action);
        }

        public static void SetMapTileIndex(GameLocation location, int tileX, int tileY, int index, string layerName, int tileSheetIndex = 0)
        {
            if (location.Map is null)
                return;

            try
            {
                Layer layer = location.Map.GetLayer(layerName) ?? throw new InvalidOperationException($"Map layer '{layerName}' was not found.");
                if (layer.Tiles[tileX, tileY] is not null)
                {
                    if (index == -1)
                        layer.Tiles[tileX, tileY] = null;
                    else
                        layer.Tiles[tileX, tileY].TileIndex = index;
                }
                else if (index != -1)
                    layer.Tiles[tileX, tileY] = new StaticTile(layer, location.Map.TileSheets[tileSheetIndex], BlendMode.Alpha, index);
            }
            catch
            {
            }
        }
    }
}
