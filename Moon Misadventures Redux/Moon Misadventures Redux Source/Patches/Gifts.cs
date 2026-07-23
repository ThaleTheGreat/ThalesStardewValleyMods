using HarmonyLib;
using StardewValley;

namespace ThaleTheGreat.MoonMisadventures.Patches
{
    [HarmonyPatch(typeof(NPC), nameof(NPC.receiveGift))]
    public static class NpcReceiveGiftPatch
    {
        public static void Postfix(NPC __instance, StardewValley.Object o, Farmer giver)
        {
            Mod.HandleGiftGiven(giver, __instance, o);
        }
    }
}
