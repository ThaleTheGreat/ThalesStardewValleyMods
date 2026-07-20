using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;
using StardewValley;

namespace NonDestructiveNPCsRedux;

public sealed class ModEntry : Mod
{
    public override void Entry(IModHelper helper)
    {
        Harmony harmony = new(this.ModManifest.UniqueID);

        MethodInfo[] methods = AccessTools
            .GetDeclaredMethods(typeof(GameLocation))
            .Where(method => method.Name == "characterDestroyObjectWithinRectangle")
            .ToArray();

        if (methods.Length == 0)
        {
            this.Monitor.Log("No method named 'GameLocation.characterDestroyObjectWithinRectangle' was found. The mod will do nothing.", LogLevel.Warn);
            return;
        }

        foreach (MethodInfo method in methods)
        {
            try
            {
                if (method.ReturnType == typeof(bool))
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModEntry), nameof(NeverDestroyObjectBool)));
                }
                else if (method.ReturnType == typeof(void))
                {
                    harmony.Patch(method, prefix: new HarmonyMethod(typeof(ModEntry), nameof(NeverDestroyObjectVoid)));
                }
                else
                {
                    this.Monitor.Log($"Skipping patch for unexpected overload signature: {method}", LogLevel.Warn);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"Failed to patch {method}: {ex}", LogLevel.Warn);
            }
        }
    }

    private static bool NeverDestroyObjectBool(ref bool __result)
    {
        __result = false;
        return false;
    }

    private static bool NeverDestroyObjectVoid()
    {
        return false;
    }
}
