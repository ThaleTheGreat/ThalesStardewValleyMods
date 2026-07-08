using HarmonyLib;
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Projectiles;
using Object = StardewValley.Object;
using Rectangle = Microsoft.Xna.Framework.Rectangle;

namespace ThaleTheGreat.CoinCollectorRedux
{
    public partial class ModEntry
    {
        [HarmonyPatch(typeof(CraftingRecipe), nameof(CraftingRecipe.createItem))]
        private static class CraftingRecipeCreateItemPatch
        {
            public static void Postfix(CraftingRecipe __instance, ref Item __result)
            {
                if (!Config.ModEnabled || __result == null || __instance.name != "Metal Detector")
                    return;

                __result = CreateDetectorWeapon();
            }
        }

        [HarmonyPatch(typeof(Object), nameof(Object.drawWhenHeld))]
        private static class ObjectDrawWhenHeldPatch
        {
            public static bool Prefix(Object __instance, SpriteBatch spriteBatch, Vector2 objectPosition, Farmer f)
            {
                if (!Config.ModEnabled || !IsMetalDetectorItem(__instance))
                    return true;

                Texture2D? texture = GetDetectorTexture();
                if (texture == null)
                    return true;

                spriteBatch.Draw(texture, objectPosition + new Vector2(0, 92), new Rectangle(0, 0, 16, 16), Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, Math.Max(0f, (f.StandingPixel.Y + 3) / 10000f));
                return false;
            }
        }

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
