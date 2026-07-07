using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Projectiles;

namespace ThaleTheGreat.CoinCollectorRedux
{
    public partial class ModEntry
    {
        [HarmonyPatch(typeof(Projectile), nameof(Projectile.isColliding))]
        private static class ProjectileIsCollidingPatch
        {
            public static bool Prefix(Projectile __instance, ref bool __result)
            {
                if (!Config.ModEnabled || __instance is not IndicatorProjectile)
                    return true;

                __result = false;
                return false;
            }
        }

        [HarmonyPatch(typeof(GameLocation), nameof(GameLocation.makeHoeDirt))]
        private static class GameLocationMakeHoeDirtPatch
        {
            public static void Prefix(GameLocation __instance, Vector2 tileLocation, bool ignoreChecks)
            {
                if (Config.ModEnabled)
                    TryDigCoin(__instance, tileLocation, ignoreChecks);
            }
        }
    }
}
