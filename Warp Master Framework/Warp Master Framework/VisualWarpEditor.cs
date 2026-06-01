using Microsoft.Xna.Framework;
using StardewValley;
using StardewModdingAPI;
using System;
using System.Collections.Generic;
using System.Linq;

namespace WarpMasterFramework
{
    public class VisualWarpEditor
    {
        private static string GetLocationDataMapAsset(object locationData)
        {
            // LocationData API differs between game/SMAPI versions and assemblies.
            // Use reflection to read the map asset path without hard dependency on a specific member name.
            if (locationData is null)
                return null;

            var t = locationData.GetType();

            // Common member names seen across versions/branches.
            foreach (var name in new[] { "Map", "MapPath", "MapAsset", "MapAssetPath" })
            {
                var prop = t.GetProperty(name);
                if (prop != null && prop.PropertyType == typeof(string))
                {
                    var val = prop.GetValue(locationData) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }

                var field = t.GetField(name);
                if (field != null && field.FieldType == typeof(string))
                {
                    var val = field.GetValue(locationData) as string;
                    if (!string.IsNullOrWhiteSpace(val))
                        return val;
                }
            }

            return null;
        }


        private List<WarpPointData> currentMapWarps;
        private IMonitor monitor;

        public bool EnableDebugLogging { get; set; } = false;

        /// <summary>
        /// Log only when debug logging is enabled, except errors/alerts which always log.
        /// </summary>
        private void SafeLog(string message, LogLevel level)
        {
            if (EnableDebugLogging || level >= LogLevel.Error)
                monitor?.Log(message, level);
        }

        // Cache detected (vanilla) warps per map to avoid expensive rescans and log spam.
        // Invalidated explicitly when the mod applies edits to that location.
        private readonly Dictionary<string, (int Hash, List<WarpPointData> Warps)> DetectedWarpCache =
            new(System.StringComparer.OrdinalIgnoreCase);
        
        public VisualWarpEditor(IMonitor monitor)
        {
            currentMapWarps = new List<WarpPointData>();
            this.monitor = monitor;
        }

        private void LogDebug(string message)
        {
            SafeLog(message, LogLevel.Debug);
        }

        private void LogTrace(string message)
        {
            SafeLog(message, LogLevel.Trace);
        }

        
        
        public void InvalidateDetectionCache(string mapName)
        {
            if (string.IsNullOrWhiteSpace(mapName))
                return;
            DetectedWarpCache.Remove(mapName);
        }

public void DetectWarpsOnCurrentMap()
        {
            if (Game1.currentLocation == null)
            {
                SafeLog("Cannot detect warps: currentLocation is null", LogLevel.Debug);
                currentMapWarps.Clear();
                return;
            }
            
            currentMapWarps = DetectWarpsForLocation(Game1.currentLocation);
        }
        
        
        /// <summary>
        /// Detect warps for a location by name, even if it isn't currently loaded.
        /// Uses the live GameLocation if available; otherwise loads the map asset from Data/Locations and scans tile properties.
        /// </summary>
        public List<WarpPointData> DetectWarpsForLocationName(string locationName)
        {
            if (string.IsNullOrWhiteSpace(locationName))
                return new List<WarpPointData>();

            // Prefer live location if present (supports farm variants, modded maps, etc.)
            var live = Game1.getLocationFromName(locationName);
            if (live != null)
                return DetectWarpsForLocation(live);

            // Fallback: load map asset path from Data/Locations and scan the map directly.
            try
            {
                var locationsData = Game1.content.Load<Dictionary<string, StardewValley.GameData.Locations.LocationData>>("Data/Locations");
                if (locationsData != null && locationsData.TryGetValue(locationName, out var locData))
                {
                    string mapPath = GetLocationDataMapAsset(locData);
                    if (!string.IsNullOrWhiteSpace(mapPath))
                        return DetectWarpsForMapAsset(locationName, mapPath);
                }
            }
            catch
            {
                // ignore; not all contexts can load Data/Locations
            }

            return new List<WarpPointData>();
        }

        private List<WarpPointData> DetectWarpsForMapAsset(string locationName, string mapAssetPath)
        {
            var warps = new List<WarpPointData>();
            if (string.IsNullOrWhiteSpace(mapAssetPath))
                return warps;

            try
            {
                var map = Game1.content.Load<xTile.Map>(mapAssetPath);
                if (map == null)
                    return warps;

                LogDebug($"Detecting warps from map asset: {locationName} ({mapAssetPath})");

                // Scan tile properties for Warp / Action Warp / TouchAction Warp / LockedDoorWarp / MagicWarp.
                ScanMapForTileWarps(map, locationName, warps);
            }
            catch
            {
                // ignore; asset may not exist in some modded setups
            }

            return warps;
        }

        private void ScanMapForTileWarps(xTile.Map map, string mapName, List<WarpPointData> warpsOut)
        {
            if (map == null || warpsOut == null)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddWarp(string warpType, int x, int y, string targetMap, int tx, int ty, string extra)
            {
                if (string.IsNullOrWhiteSpace(targetMap))
                    return;

                string key = $"{warpType}|{x}|{y}|{targetMap}|{tx}|{ty}";
                if (!seen.Add(key))
                    return;

                var warpData = new WarpPointData
                {
                    MapName = mapName,
                    IsAddedByMod = false,
                    TargetMap = targetMap,
                    OriginalPosition = new Point(x, y),
                    ModifiedPosition = new Point(x, y),
                    TargetPosition = new Point(tx, ty),
                    OriginalTargetMap = targetMap,
                    OriginalTargetPosition = new Point(tx, ty),
                    WarpType = warpType
                };

                // If WarpPointData has an Extra/Notes field in some branches, you can wire it here; otherwise ignore.
                warpsOut.Add(warpData);
            }

            var layers = new[] { "Buildings", "Back", "Front" };
            foreach (var layerName in layers)
            {
                var layer = map.GetLayer(layerName);
                if (layer == null)
                    continue;

                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    for (int y = 0; y < layer.LayerHeight; y++)
                    {
                        var tile = layer.Tiles[x, y];
                        if (tile?.Properties == null)
                            continue;

                        string raw = null;

                        bool TryGetProp(string key, out string value)
                        {
                            value = null;
                            if (tile.Properties.TryGetValue(key, out var v1))
                            {
                                value = v1?.ToString();
                                return true;
                            }
                            if (tile.TileIndexProperties != null && tile.TileIndexProperties.TryGetValue(key, out var v2))
                            {
                                value = v2?.ToString();
                                return true;
                            }
                            return false;
                        }

                        if (TryGetProp("Action", out var actionVal))
                            raw = actionVal;
                        else if (TryGetProp("TouchAction", out var touchVal))
                            raw = touchVal;
                        else if (TryGetProp("Warp", out var warpVal))
                            raw = warpVal;

                        if (string.IsNullOrWhiteSpace(raw))
                            continue;

                        raw = raw.Trim();
                        var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 1)
                            continue;

                        string targetMap = null;
                        int tx = 0;
                        int ty = 0;
                        string extra = "";

                        if (parts[0].Equals("Warp", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parts.Length < 4)
                                continue;

                            // TouchAction: Warp <area> <x> <y>
                            if (!int.TryParse(parts[1], out _) && int.TryParse(parts[2], out tx) && int.TryParse(parts[3], out ty))
                            {
                                targetMap = parts[1];
                                extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                            }
                            // Action: Warp <x> <y> <area>
                            else if (int.TryParse(parts[1], out tx) && int.TryParse(parts[2], out ty))
                            {
                                targetMap = parts[3];
                                extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                            }
                            else
                            {
                                continue;
                            }

                            AddWarp("Warp", x, y, targetMap, tx, ty, extra);
                        }
                        else if (parts[0].Equals("MagicWarp", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parts.Length < 4)
                                continue;

                            targetMap = parts[1];
                            if (!int.TryParse(parts[2], out tx) || !int.TryParse(parts[3], out ty))
                                continue;
                            extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";

                            AddWarp("MagicWarp", x, y, targetMap, tx, ty, extra);
                        }
                        else if (parts[0].Equals("LockedDoorWarp", StringComparison.OrdinalIgnoreCase))
                        {
                            if (parts.Length < 4)
                                continue;

                            if (!int.TryParse(parts[1], out tx) || !int.TryParse(parts[2], out ty))
                                continue;

                            targetMap = parts[3];
                            extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";

                            AddWarp("LockedDoorWarp", x, y, targetMap, tx, ty, extra);
                        }
                    }
                }
            }
        }

public List<WarpPointData> DetectWarpsForLocation(GameLocation location)
        {
            var warps = new List<WarpPointData>();
            
            if (location == null)
            {
                SafeLog("Cannot detect warps: location is null", LogLevel.Debug);
                return warps;
            }
            
            string mapName = location.Name;

            // Fast path: reuse cached detection if it matches the current runtime warp list.
            // Door warps are derived from the map asset and are stable for a given location unless the mod edits them;
            // we invalidate the cache when applying edits.
            int runtimeHash = ComputeRuntimeWarpHash(location);
            if (DetectedWarpCache.TryGetValue(mapName, out var cached) && cached.Hash == runtimeHash)
            {
                return new List<WarpPointData>(cached.Warps);
            }

            LogDebug($"Detecting warps on map: {mapName}");
            
            // Detect regular warps
            if (location.warps.Count > 0)
            {
                LogDebug($"Found {location.warps.Count} regular warps");
                foreach (var warp in location.warps)
                {
                    var warpData = new WarpPointData
                    {
                        MapName = mapName,
                        IsAddedByMod = false,
                        TargetMap = warp.TargetName,
                        OriginalPosition = new Point(warp.X, warp.Y),
                        ModifiedPosition = new Point(warp.X, warp.Y),
                        TargetPosition = new Point(warp.TargetX, warp.TargetY),
                        OriginalTargetMap = warp.TargetName,
                        OriginalTargetPosition = new Point(warp.TargetX, warp.TargetY),
                        WarpType = "Warp"
                    };
                    warps.Add(warpData);
                    LogTrace($"  Added warp: ({warp.X},{warp.Y}) -> {warp.TargetName}");
                }
            }
            else
            {
                LogDebug("No regular warps found on this map");
            }
            
            // Detect door warps (tile property Action/TouchAction starting with 'Warp ').
            try
            {
                var map = location.Map;
                if (map != null)
                {
                    var layers = new[] { "Buildings", "Back", "Front" };
                    foreach (var layerName in layers)
                    {
                        var layer = map.GetLayer(layerName);
                        if (layer == null)
                            continue;

                        for (int x = 0; x < layer.LayerWidth; x++)
                        {
                            for (int y = 0; y < layer.LayerHeight; y++)
                            {
                                var tile = layer.Tiles[x, y];
                                if (tile?.Properties == null)
                                    continue;

                                // Prefer Action, then TouchAction.
                                // Door warps can be defined either as tile properties on the placed tile,
                                // or as tile index properties on the tilesheet.
                                // We check both so we don't miss vanilla door warps.
                                string propName = null;
                                string raw = null;

                                bool TryGetProp(string key, out string value)
                                {
                                    value = null;
                                    if (tile.Properties.TryGetValue(key, out var v1))
                                    {
                                        value = v1?.ToString();
                                        return true;
                                    }
                                    if (tile.TileIndexProperties != null && tile.TileIndexProperties.TryGetValue(key, out var v2))
                                    {
                                        value = v2?.ToString();
                                        return true;
                                    }
                                    return false;
                                }

                                if (TryGetProp("Action", out var actionVal))
                                {
                                    propName = "Action";
                                    raw = actionVal;
                                }
                                else if (TryGetProp("TouchAction", out var touchVal))
                                {
                                    propName = "TouchAction";
                                    raw = touchVal;
                                }
                                else if (TryGetProp("Warp", out var warpVal))
                                {
                                    propName = "Warp";
                                    raw = warpVal;
                                }

                                if (string.IsNullOrEmpty(raw))
                                    continue;

                                raw = raw.Trim();
                                var parts = raw.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length < 1)
                                    continue;

                                string targetMap = null;
                                int tx = 0;
                                int ty = 0;
                                string extra = "";

                                // Supported formats (see wiki):
                                //   TouchAction Warp <area> <x> <y> [prereq]
                                //   Action Warp <x> <y> <area>
                                //   Action LockedDoorWarp <toX> <toY> <toArea> <openTime> <closeTime> ...
                                if (parts[0].Equals("Warp", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    if (parts.Length < 4)
                                        continue;

                                    // Try TouchAction form first: Warp <area> <x> <y>
                                    if (!int.TryParse(parts[1], out _) && int.TryParse(parts[2], out tx) && int.TryParse(parts[3], out ty))
                                    {
                                        targetMap = parts[1];
                                        extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                                    }
                                    // Then Action form: Warp <x> <y> <area>
                                    else if (int.TryParse(parts[1], out tx) && int.TryParse(parts[2], out ty))
                                    {
                                        targetMap = parts[3];
                                        extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                                    }
                                    else
                                    {
                                        continue;
                                    }
                                }
                                else if (parts[0].Equals("MagicWarp", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    if (parts.Length < 4)
                                        continue;

                                    // TouchAction MagicWarp <area> <x> <y> [prereq]
                                    targetMap = parts[1];
                                    if (!int.TryParse(parts[2], out tx) || !int.TryParse(parts[3], out ty))
                                        continue;
                                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                                }
                                else if (parts[0].Equals("LockedDoorWarp", System.StringComparison.OrdinalIgnoreCase))
                                {
                                    if (parts.Length < 4)
                                        continue;
                                    if (!int.TryParse(parts[1], out tx) || !int.TryParse(parts[2], out ty))
                                        continue;
                                    targetMap = parts[3];
                                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                                }
                                else
                                {
                                    // Ignore other Action/TouchAction values.
                                    continue;
                                }

                                if (string.IsNullOrWhiteSpace(targetMap))
                                    continue;

                                // Avoid duplicates if a regular warp exists at this tile.
                                if (location.warps.Any(w => w != null && w.X == x && w.Y == y))
                                    continue;

                                warps.Add(new WarpPointData
                                {
                                    MapName = mapName,
                                    IsAddedByMod = false,
                                    TargetMap = targetMap,
                                    TargetPosition = new Point(tx, ty),
                                    OriginalTargetMap = targetMap,
                                    OriginalTargetPosition = new Point(tx, ty),
                                    OriginalPosition = new Point(x, y),
                                    ModifiedPosition = new Point(x, y),
                                    TrueOriginalPosition = new Point(x, y),
                                    WarpType = "Door",
                                    DoorLayerName = layerName,
                                    DoorPropertyName = propName,
                                    DoorExtraTokens = extra,
                                    DoorCommand = parts[0]
                                });
                            }
                        }
                    }
                }
            }
            catch (System.Exception ex)
            {
                SafeLog($"Error detecting door warps on {mapName}: {ex}", LogLevel.Debug);
            }

            SafeLog($"Total warps detected: {warps.Count}", LogLevel.Debug);

            DetectedWarpCache[mapName] = (runtimeHash, new List<WarpPointData>(warps));
            return warps;
        }

        private static int ComputeRuntimeWarpHash(GameLocation location)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + (location?.NameOrUniqueName?.GetHashCode() ?? 0);
                if (location == null)
                {
                    hash = hash * 31;
                    return hash;
                }

                var warps = location.warps;
                hash = hash * 31 + warps.Count;
                for (int i = 0; i < warps.Count; i++)
                {
                    var w = warps[i];
                    if (w == null)
                        continue;

                    hash = hash * 31 + w.X;
                    hash = hash * 31 + w.Y;
                    hash = hash * 31 + w.TargetX;
                    hash = hash * 31 + w.TargetY;
                    hash = hash * 31 + (w.TargetName?.GetHashCode() ?? 0);
                }

                return hash;
            }
        }


        public List<WarpPointData> GetCurrentWarps()
        {
            return currentMapWarps;
        }
        
        public void UpdateWarps(List<WarpPointData> warps)
        {
            currentMapWarps = warps;
        }
    }
}