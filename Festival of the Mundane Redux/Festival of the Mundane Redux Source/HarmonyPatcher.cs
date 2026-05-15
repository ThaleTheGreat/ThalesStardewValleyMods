using HarmonyLib;
using Netcode;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Objects;
using System;
using System.Reflection;

namespace ShadowFestival;

internal static class HarmonyPatcher
{
  private static IMonitor Monitor;

  public static void Hook(Harmony harmony, IMonitor monitor)
  {
    HarmonyPatcher.Monitor = monitor;
    harmony.Patch(
      original: AccessTools.Method(typeof(Farmer), nameof(Farmer.takeDamage)),
      prefix: new HarmonyMethod(typeof(HarmonyPatcher), nameof(Prefix_takeDamage))
    );
  }

  private static bool Prefix_takeDamage(
    Farmer __instance,
    int damage,
    bool overrideParry,
    Monster damager)
  {
    if (damager == null)
      return true;
    if (ModEntry.IsFestivalSewerActive() && ModEntry.IsSlimeMonster(damager))
    {
      HarmonyPatcher.Monitor?.Log("Festival sewer slime damage will be nullified.", (LogLevel) 0);
      return false;
    }

    string name = ((Item) ((NetFieldBase<Hat, NetRef<Hat>>) __instance.hat).Value)?.Name;
    if (name == null || ModEntry.Data == null || !ModEntry.Data.CalmingHats.Contains(name))
      return true;

    int num;
    switch (damager)
    {
      case ShadowBrute _:
      case ShadowShaman _:
      case ShadowGuy _:
        num = 1;
        break;
      default:
        num = damager is ShadowGirl ? 1 : 0;
        break;
    }
    if (num == 0)
      return true;
    HarmonyPatcher.Monitor?.Log($"Farmer wearing hat '{name}' and shadow damage will be nullified.", (LogLevel) 0);
    return false;
  }
}
