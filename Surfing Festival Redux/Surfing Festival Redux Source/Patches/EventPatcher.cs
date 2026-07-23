using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Microsoft.Xna.Framework.Graphics;
using Spacechase.Shared.Patching;
using SpaceShared;
using StardewModdingAPI;
using StardewValley;

namespace ThaleTheGreat.SurfingFestival.Patches
{
    /// <summary>Applies Harmony patches to <see cref="Event"/>.</summary>
    [SuppressMessage("ReSharper", "InconsistentNaming", Justification = "The parameter is named for Harmony's patch contract.")]
    internal class EventPatcher : BasePatcher
    {
        /*********
        ** Public methods
        *********/
        /// <inheritdoc />
        public override void Apply(Harmony harmony, IMonitor monitor)
        {
            harmony.Patch(
                original: this.RequireMethod<Event>(nameof(Event.setUpPlayerControlSequence)),
                postfix: this.GetHarmonyMethod(nameof(After_SetUpPlayerControlSequence))
            );

            harmony.Patch(
                original: this.RequireMethod<Event>(nameof(Event.setUpFestivalMainEvent)),
                postfix: this.GetHarmonyMethod(nameof(After_SetUpFestivalMainEvent))
            );

            harmony.Patch(
                original: this.RequireMethod<Event>(nameof(Event.draw)),
                prefix: this.GetHarmonyMethod(nameof(Before_Draw)),
                postfix: this.GetHarmonyMethod(nameof(After_Draw))
            );
        }


        /*********
        ** Private methods
        *********/
        /// <summary>The method to call after <see cref="Event.setUpPlayerControlSequence"/>.</summary>
        private static void After_SetUpPlayerControlSequence(Event __instance, string id)
        {
            if (id == "surfing")
            {
                Mod.Instance.Helper.Reflection.GetField<NPC>(__instance, "festivalHost").SetValue(__instance.getActorByName("Lewis"));
                Mod.Instance.Helper.Reflection.GetField<string>(__instance, "hostMessageKey").SetValue(Mod.HostMessageKey);
            }
        }

        /// <summary>The method to call after <see cref="Event.setUpFestivalMainEvent"/>.</summary>
        private static void After_SetUpFestivalMainEvent(Event __instance)
        {
            if (!__instance.isSpecificFestival("summer5"))
                return;

            // ...
        }

        /// <summary>The method to call before <see cref="Event.draw"/>.</summary>
        private static void Before_Draw(Event __instance, SpriteBatch b)
        {
            if (!__instance.isSpecificFestival("summer5") || !Mod.RaceCourseActive)
                return;

            foreach (string racerName in Mod.Racers)
            {
                Character? racer = __instance.getCharacterByName(racerName);
                if (racer is not null)
                    Mod.DrawSurfboard(racer, b);
            }
        }

        /// <summary>The method to call after <see cref="Event.draw"/>.</summary>
        private static void After_Draw(Event __instance, SpriteBatch b)
        {
            if (!__instance.isSpecificFestival("summer5") || __instance.playerControlSequenceID != "surfingRace")
                return;

            foreach (string racerName in Mod.Racers)
            {
                Character? racer = __instance.getCharacterByName(racerName);
                if (racer is not null)
                    Mod.DrawSurfingStatuses(racer, b);
            }

            Mod.Instance.DrawObstacles(b);
        }
    }
}
