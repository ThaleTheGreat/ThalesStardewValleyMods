using Microsoft.Xna.Framework;

namespace WarpMasterFramework
{
    public class WarpPointData
    {
        public string MapName { get; set; }

        /// <summary>Whether this warp was created by Warp Master Framework (i.e., it didn't exist in the map's detected warps).</summary>
        public bool IsAddedByMod { get; set; }
        public bool IsDeleted { get; set; }

        // Editable destination
        public string TargetMap { get; set; }
        public Point TargetPosition { get; set; }

        // Immutable destination identity for matching across reloads.
        // These should remain stable even if TargetMap/TargetPosition are edited.
        public string OriginalTargetMap { get; set; }
        public Point OriginalTargetPosition { get; set; }

        public Point OriginalPosition { get; set; }      // Position when first detected (before any mods)
        public Point ModifiedPosition { get; set; }      // Current modified position
        public Point TrueOriginalPosition { get; set; }  // The very first position ever (persists across saves)
        public string WarpType { get; set; } // "Warp" or "Door"

        // Door warp backing tile property (for WarpType == "Door")
        public string DoorLayerName { get; set; }
        public string DoorPropertyName { get; set; }
        /// <summary>For door warps, the token order used by the backing tile property ("x y map" or "map x y").</summary>
        public string DoorTokenOrder { get; set; }

        /// <summary>The tile command name for door/tile transitions (e.g. "Warp", "MagicWarp", "LockedDoorWarp").</summary>
        public string DoorCommand { get; set; }

        public string DoorExtraTokens { get; set; }

        /// <summary>
        /// The last state that was actually applied to the live location's warp list.
        /// This allows us to remove/replace an already-applied warp when the user edits it again
        /// (otherwise we can end up with duplicates in <c>GameLocation.warps</c>).
        ///
        /// These fields are runtime-friendly and may be persisted, but they do not affect the
        /// intended "commit only on overnight save" model.
        /// </summary>
        public Point LastAppliedPosition { get; set; }

        /// <summary>Last applied destination map name.</summary>
        public string LastAppliedTargetMap { get; set; }

        /// <summary>Last applied destination tile.</summary>
        public Point LastAppliedTargetPosition { get; set; }

        public WarpPointData()
        {
            MapName = "";
            IsAddedByMod = false;
            IsDeleted = false;
            TargetMap = "";
            TargetPosition = Point.Zero;

            OriginalTargetMap = "";
            OriginalTargetPosition = Point.Zero;

            OriginalPosition = Point.Zero;
            ModifiedPosition = Point.Zero;
            TrueOriginalPosition = Point.Zero;
            WarpType = "Warp";

            DoorLayerName = "";
            DoorPropertyName = "";
            DoorTokenOrder = "";
            DoorCommand = "Warp";
            DoorExtraTokens = "";

            LastAppliedPosition = Point.Zero;
            LastAppliedTargetMap = "";
            LastAppliedTargetPosition = Point.Zero;
        }

        public string GetOriginalTargetMapFallback()
        {
            return string.IsNullOrEmpty(OriginalTargetMap) ? (TargetMap ?? "") : OriginalTargetMap;
        }

        public Point GetOriginalTargetPosFallback()
        {
            return OriginalTargetPosition != Point.Zero ? OriginalTargetPosition : TargetPosition;
        }

        public string GetDisplayName()
        {
            string origMap = GetOriginalTargetMapFallback();
            Point origPos = GetOriginalTargetPosFallback();

            return $"{MapName} [{TrueOriginalPosition.X},{TrueOriginalPosition.Y}] -> {TargetMap} [{TargetPosition.X},{TargetPosition.Y}] (orig: {origMap} [{origPos.X},{origPos.Y}])";
        }
    }
}