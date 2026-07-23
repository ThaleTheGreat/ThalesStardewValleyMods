using System;
using System.Reflection;
using HarmonyLib;
using StardewModdingAPI;

namespace Spacechase.Shared.Patching
{
    public interface IPatcher
    {
        void Apply(Harmony harmony, IMonitor monitor);
    }

    public abstract class BasePatcher : IPatcher
    {
        public abstract void Apply(Harmony harmony, IMonitor monitor);

        protected MethodInfo RequireMethod<T>(string name, Type[]? argumentTypes = null)
        {
            return PatchHelper.RequireMethod<T>(name, argumentTypes);
        }

        protected HarmonyMethod GetHarmonyMethod(string name)
        {
            MethodInfo? method = AccessTools.Method(this.GetType(), name);
            return method is null
                ? throw new InvalidOperationException($"Could not find Harmony patch method {this.GetType().FullName}.{name}.")
                : new HarmonyMethod(method);
        }
    }

    public static class PatchHelper
    {
        public static MethodInfo RequireMethod<T>(string name, Type[]? argumentTypes = null)
        {
            MethodInfo? method = argumentTypes is null
                ? AccessTools.Method(typeof(T), name)
                : AccessTools.Method(typeof(T), name, argumentTypes);
            return method ?? throw new InvalidOperationException($"Could not find method {typeof(T).FullName}.{name}.");
        }
    }

    public static class HarmonyPatcher
    {
        public static void Apply(Mod mod, params IPatcher[] patchers)
        {
            var harmony = new Harmony(mod.ModManifest.UniqueID);
            foreach (IPatcher patcher in patchers)
            {
                try
                {
                    patcher.Apply(harmony, mod.Monitor);
                }
                catch (Exception ex)
                {
                    mod.Monitor.Log($"Failed applying Harmony patcher {patcher.GetType().FullName}: {ex}", LogLevel.Error);
                    throw;
                }
            }
        }
    }
}
