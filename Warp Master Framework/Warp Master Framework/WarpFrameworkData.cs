using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace WarpMasterFramework
{
    public sealed class WarpFrameworkOverrideAsset
    {
        public string Format { get; set; } = "1.0.0";
        public List<WarpFrameworkOverride> Overrides { get; set; } = new();
    }

    public sealed class WarpFrameworkOverride
    {
        public string Id { get; set; } = "";
        public string SourceMap { get; set; } = "";
        public string WarpType { get; set; } = "Warp";
        public int OriginalX { get; set; }
        public int OriginalY { get; set; }
        public string OriginalTargetMap { get; set; } = "";
        public int? OriginalTargetX { get; set; }
        public int? OriginalTargetY { get; set; }
        public int? NewX { get; set; }
        public int? NewY { get; set; }
        public string TargetMap { get; set; } = "";
        public int? TargetX { get; set; }
        public int? TargetY { get; set; }
        public string DoorLayerName { get; set; } = "";
        public string DoorPropertyName { get; set; } = "";
        public string DoorTokenOrder { get; set; } = "";
        public string DoorCommand { get; set; } = "Warp";
        public string DoorExtraTokens { get; set; } = "";
        public bool Delete { get; set; }

        public WarpPointData ToWarpPointData(GameLocationSnapshotFallback fallback)
        {
            string originalTargetMap = !string.IsNullOrWhiteSpace(OriginalTargetMap) ? OriginalTargetMap : fallback.TargetMap;
            Point originalTarget = OriginalTargetX.HasValue && OriginalTargetY.HasValue ? new Point(OriginalTargetX.Value, OriginalTargetY.Value) : fallback.TargetTile;
            string targetMap = !string.IsNullOrWhiteSpace(TargetMap) ? TargetMap : originalTargetMap;
            Point target = TargetX.HasValue && TargetY.HasValue ? new Point(TargetX.Value, TargetY.Value) : originalTarget;
            Point original = new(OriginalX, OriginalY);
            Point modified = new(NewX ?? OriginalX, NewY ?? OriginalY);

            return new WarpPointData
            {
                MapName = SourceMap ?? "",
                WarpType = string.IsNullOrWhiteSpace(WarpType) ? "Warp" : WarpType,
                OriginalPosition = original,
                TrueOriginalPosition = original,
                ModifiedPosition = modified,
                OriginalTargetMap = originalTargetMap,
                OriginalTargetPosition = originalTarget,
                TargetMap = targetMap,
                TargetPosition = target,
                DoorLayerName = DoorLayerName ?? "",
                DoorPropertyName = DoorPropertyName ?? "",
                DoorTokenOrder = DoorTokenOrder ?? "",
                DoorCommand = string.IsNullOrWhiteSpace(DoorCommand) ? "Warp" : DoorCommand,
                DoorExtraTokens = DoorExtraTokens ?? "",
                IsDeleted = Delete
            };
        }
    }

    public readonly struct GameLocationSnapshotFallback
    {
        public GameLocationSnapshotFallback(string targetMap, Point targetTile)
        {
            TargetMap = targetMap ?? "";
            TargetTile = targetTile;
        }

        public string TargetMap { get; }
        public Point TargetTile { get; }
    }

    public sealed class WarpFrameworkOriginalExport
    {
        public string Format { get; set; } = "1.0.0";
        public string FrameworkUniqueId { get; set; } = "ThaleTheGreat.WarpMasterFramework";
        public List<WarpFrameworkModDetail> Mods { get; set; } = new();
        public Dictionary<string, List<WarpFrameworkOriginalWarp>> Maps { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class WarpFrameworkModDetail
    {
        public string Name { get; set; } = "";
        public string UniqueID { get; set; } = "";
        public string Author { get; set; } = "";
        public string Version { get; set; } = "";
        public bool IsContentPack { get; set; }
    }

    public sealed class WarpFrameworkOriginalWarp
    {
        public string SourceMap { get; set; } = "";
        public string WarpType { get; set; } = "Warp";
        public int X { get; set; }
        public int Y { get; set; }
        public string TargetMap { get; set; } = "";
        public int TargetX { get; set; }
        public int TargetY { get; set; }
        public string DoorLayerName { get; set; } = "";
        public string DoorPropertyName { get; set; } = "";
        public string DoorCommand { get; set; } = "Warp";
        public string DoorExtraTokens { get; set; } = "";
    }
}
