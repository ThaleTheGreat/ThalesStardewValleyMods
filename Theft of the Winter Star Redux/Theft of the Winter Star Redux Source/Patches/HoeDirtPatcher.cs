using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Spacechase.Shared.Patching;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.TheftOfTheWinterStar.Patches
{
    /// <summary>Applies the Tempus Globe planting and season behavior.</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "The parameters are named for Harmony's patch contract.")]
    internal sealed class HoeDirtPatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: this.RequireMethod<HoeDirt>(nameof(HoeDirt.canPlantThisSeedHere), new[] { typeof(string), typeof(bool) }),
                prefix: this.GetHarmonyMethod(nameof(Before_CanPlantThisSeedHere))
            );

            harmony.Patch(
                original: this.RequireMethod<HoeDirt>(nameof(HoeDirt.plant), new[] { typeof(string), typeof(Farmer), typeof(bool) }),
                prefix: this.GetHarmonyMethod(nameof(Before_Plant))
            );

            harmony.Patch(
                original: this.RequireMethod<Crop>(nameof(Crop.IsInSeason), new[] { typeof(GameLocation) }),
                postfix: this.GetHarmonyMethod(nameof(After_IsInSeason))
            );
        }

        private static bool Before_CanPlantThisSeedHere(HoeDirt __instance, string itemId, bool isFertilizer, ref bool __result)
        {
            if (isFertilizer || !IsNearTempusGlobe(__instance.Location, __instance.Tile))
                return true;

            if (__instance.crop is not null)
            {
                __result = false;
                return false;
            }

            itemId = Crop.ResolveSeedId(itemId, __instance.Location);
            if (!Crop.TryGetData(itemId, out var cropData) || cropData.Seasons.Count == 0)
            {
                __result = false;
                return false;
            }

            __result = !cropData.IsRaised || !Utility.doesRectangleIntersectTile(
                Game1.player.GetBoundingBox(),
                (int)__instance.Tile.X,
                (int)__instance.Tile.Y
            );
            return false;
        }

        private static bool Before_Plant(HoeDirt __instance, string itemId, Farmer who, bool isFertilizer, ref bool __result)
        {
            GameLocation location = __instance.Location;
            if (isFertilizer || !IsNearTempusGlobe(location, __instance.Tile))
                return true;

            Point tile = Utility.Vector2ToPoint(__instance.Tile);
            itemId = Crop.ResolveSeedId(itemId, location);
            if (!Crop.TryGetData(itemId, out var cropData) || cropData.Seasons.Count == 0)
            {
                __result = false;
                return false;
            }

            bool isGardenPot = location.objects.TryGetValue(__instance.Tile, out SObject? obj) && obj is IndoorPot;
            bool isIndoorPot = isGardenPot && !location.IsOutdoors;

            if (!who.currentLocation.CheckItemPlantRules(
                    itemId,
                    isGardenPot,
                    isIndoorPot || (location.GetData()?.CanPlantHere ?? location.IsFarm),
                    out string? deniedMessage
                ))
            {
                ShowPlantingError(location, itemId, isGardenPot, deniedMessage);
                __result = false;
                return false;
            }

            if (!isIndoorPot && !who.currentLocation.CanPlantSeedsHere(itemId, tile.X, tile.Y, isGardenPot, out deniedMessage))
            {
                if (Game1.didPlayerJustClickAtAll(ignoreNonMouseHeldInput: true))
                    Game1.showRedMessage(deniedMessage ?? Game1.content.LoadString("Strings\\StringsFromCSFiles:HoeDirt.cs.13925"));

                __result = false;
                return false;
            }

            __instance.crop = new Crop(itemId, tile.X, tile.Y, location);
            if (__instance.crop.raisedSeeds.Value)
                location.playSound("stoneStep");

            location.playSound("dirtyHit");
            Game1.stats.SeedsSown++;
            __instance.applySpeedIncreases(who);
            __instance.nearWaterForPaddy.Value = -1;
            if (__instance.hasPaddyCrop() && __instance.paddyWaterCheck())
            {
                __instance.state.Value = HoeDirt.watered;
                __instance.updateNeighbors();
            }

            __result = true;
            return false;
        }

        private static void After_IsInSeason(Crop __instance, GameLocation location, ref bool __result)
        {
            if (!__result && IsNearTempusGlobe(location, __instance.Dirt?.Tile ?? __instance.tilePosition))
                __result = true;
        }

        private static bool IsNearTempusGlobe(GameLocation? location, Vector2 tile)
        {
            if (location is null)
                return false;

            string qualifiedId = $"(BC){Mod.TempusGlobeId}";
            for (int x = -2; x <= 2; x++)
            {
                for (int y = -2; y <= 2; y++)
                {
                    Vector2 nearbyTile = tile + new Vector2(x, y);
                    if (location.objects.TryGetValue(nearbyTile, out SObject? obj) && obj.QualifiedItemId == qualifiedId)
                        return true;
                }
            }

            return false;
        }

        private static void ShowPlantingError(GameLocation location, string itemId, bool isGardenPot, string? deniedMessage)
        {
            if (!Game1.didPlayerJustClickAtAll(ignoreNonMouseHeldInput: true))
                return;

            if (deniedMessage is null && location.NameOrUniqueName != "Farm")
            {
                Farm farm = Game1.getFarm();
                if (farm.CheckItemPlantRules(itemId, isGardenPot, farm.GetData()?.CanPlantHere ?? true, out _))
                    deniedMessage = Game1.content.LoadString("Strings\\StringsFromCSFiles:HoeDirt.cs.13919");
            }

            Game1.showRedMessage(deniedMessage ?? Game1.content.LoadString("Strings\\StringsFromCSFiles:HoeDirt.cs.13925"));
        }
    }
}
