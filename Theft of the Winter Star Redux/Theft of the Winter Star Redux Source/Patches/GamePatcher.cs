using System;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Spacechase.Shared.Patching;
using StardewModdingAPI;
using StardewValley;

namespace ThaleTheGreat.TheftOfTheWinterStar.Patches
{
    internal sealed class GamePatcher : BasePatcher
    {
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: this.RequireMethod<Game1>(nameof(Game1.loadForNewGame), new[] { typeof(bool) }),
                postfix: this.GetHarmonyMethod(nameof(After_LoadForNewGame))
            );

            harmony.Patch(
                original: this.RequireMethod<GameLocation>(nameof(GameLocation.explode), new[]
                {
                    typeof(Vector2),
                    typeof(int),
                    typeof(Farmer),
                    typeof(bool),
                    typeof(int),
                    typeof(bool)
                }),
                postfix: this.GetHarmonyMethod(nameof(After_Explode))
            );
        }

        private static void After_LoadForNewGame()
        {
            Mod.Instance.AddFrostDungeonLocations();
        }

        private static void After_Explode(GameLocation __instance, Vector2 tileLocation, int radius, Farmer who)
        {
            Mod.Instance.HandleBombExploded(__instance, tileLocation, radius, who);
        }
    }
}
