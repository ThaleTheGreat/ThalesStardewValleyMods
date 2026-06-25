using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using Object = StardewValley.Object;

namespace ThaleTheGreat.WalletToolsForNooksAndCrannies;

public sealed class ModEntry : Mod
{
    private static ModEntry? Instance;
    private const string ItemExtensionsUniqueId = "mistyspring.ItemExtensions";
    private const string NooksResourcePrefix = "Wildflour.NooksCrannies_";

    private static readonly HashSet<string> NooksResourceIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Wildflour.NooksCrannies_WildMushStump",
        "Wildflour.NooksCrannies_WildMushLog",
        "Wildflour.NooksCrannies_HerbStump",
        "Wildflour.NooksCrannies_OvergrownFruitStump",
        "Wildflour.NooksCrannies_FlowerStump",
        "Wildflour.NooksCrannies_MagicDeadStump",
        "Wildflour.NooksCrannies_MagicOvergrownStump",
        "Wildflour.NooksCrannies_MagicBloomingStump",
        "Wildflour.NooksCrannies_Flower_Seed_Node",
        "Wildflour.NooksCrannies_Herb_Seed_Node",
        "Wildflour.NooksCrannies_Berry_Node",
        "Wildflour.NooksCrannies_Mush_Node"
    };

    private ModConfig Config = new();
    private Harmony Harmony = null!;
    private IItemExtensionsApi? ItemExtensionsApi;
    private bool GmcmRegistered;
    private bool WalletToolsPatched;
    private MethodInfo? WalletToolsSupplyMethod;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        Harmony = new Harmony(ModManifest.UniqueID);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        ItemExtensionsApi = Helper.ModRegistry.GetApi<IItemExtensionsApi>(ItemExtensionsUniqueId);
        PatchWalletToolsPrepareUse();
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        try
        {
            gmcm.Unregister(ModManifest);
            gmcm.Register(ModManifest, ResetConfig, SaveConfig);
            gmcm.AddBoolOption(ModManifest, () => Config.ModEnabled, value => Config.ModEnabled = value, () => "Mod Enabled", () => "Allows Wallet Tools to recognize Forager's Nooks and Crannies custom resources.", nameof(Config.ModEnabled));
            gmcm.AddBoolOption(ModManifest, () => Config.EnableDebugLogging, value => Config.EnableDebugLogging = value, () => "Debug Logging", () => "Logs which Nooks and Crannies custom resources are detected for Wallet Tools auto use.", nameof(Config.EnableDebugLogging));
            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register GMCM options: {ex}", LogLevel.Error);
        }
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
        Helper.WriteConfig(Config);
    }

    private void SaveConfig()
    {
        Helper.WriteConfig(Config);
    }

    private void PatchWalletToolsPrepareUse()
    {
        if (WalletToolsPatched)
            return;

        Type? walletToolsModEntry = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ModEntry");
        MethodInfo? target = walletToolsModEntry is null
            ? null
            : AccessTools.Method(walletToolsModEntry, "TryPrepareWalletToolUse", new[] { typeof(Farmer) });
        WalletToolsSupplyMethod = walletToolsModEntry is null
            ? null
            : AccessTools.Method(walletToolsModEntry, "TrySupplyRequestedWalletTool", new[] { typeof(Farmer), typeof(Type), typeof(bool) });
        MethodInfo? prefix = AccessTools.Method(typeof(WalletToolsPrepareToolUsePatch), nameof(WalletToolsPrepareToolUsePatch.Prefix));

        if (target is null || WalletToolsSupplyMethod is null || prefix is null)
        {
            Monitor.Log("Could not patch Wallet Tools tool-use preparation. The required Wallet Tools methods were not found.", LogLevel.Error);
            return;
        }

        Harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        WalletToolsPatched = true;
        DebugLog("Patched Wallet Tools tool-use preparation for Nooks and Crannies custom resources.");
    }

    private bool TryGetNooksResourceToolRequest(Farmer player, out Type toolType, out bool anyTool)
    {
        toolType = typeof(Axe);
        anyTool = false;

        if (!Config.ModEnabled || ItemExtensionsApi is null || player.currentLocation is null)
            return false;

        Vector2 tile = GetTargetTile(player);
        GameLocation location = player.currentLocation;

        if (location.objects.TryGetValue(tile, out Object obj) && TryGetObjectToolRequest(obj, out toolType, out anyTool))
            return true;

        Rectangle tileRect = new((int)tile.X * 64, (int)tile.Y * 64, 64, 64);
        foreach (ResourceClump clump in location.resourceClumps)
        {
            if (clump.getBoundingBox().Intersects(tileRect) && TryGetClumpToolRequest(clump, out toolType, out anyTool))
                return true;
        }

        return false;
    }

    private static Vector2 GetTargetTile(Farmer player)
    {
        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        return new Vector2((int)tile.X, (int)tile.Y);
    }

    private bool TryGetObjectToolRequest(Object obj, out Type toolType, out bool anyTool)
    {
        toolType = typeof(Axe);
        anyTool = false;

        string id = NormalizeItemId(obj.QualifiedItemId);
        if (!IsNooksResourceId(id) || !TryGetBreakingTool(obj.QualifiedItemId, isClump: false, out string tool))
            return false;

        if (!TryMapBreakingTool(tool, out toolType, out anyTool))
            return false;

        DebugLog($"Detected Nooks resource object '{id}' requiring '{tool}'.");
        return true;
    }

    private bool TryGetClumpToolRequest(ResourceClump clump, out Type toolType, out bool anyTool)
    {
        toolType = typeof(Axe);
        anyTool = false;

        foreach (string id in GetClumpResourceIds(clump))
        {
            if (!IsNooksResourceId(id) || !TryGetBreakingTool(id, isClump: true, out string tool))
                continue;

            if (!TryMapBreakingTool(tool, out toolType, out anyTool))
                return false;

            DebugLog($"Detected Nooks resource clump '{id}' requiring '{tool}'.");
            return true;
        }

        return false;
    }

    private bool TryGetBreakingTool(string itemId, bool isClump, out string tool)
    {
        tool = string.Empty;
        if (ItemExtensionsApi is null)
            return false;

        foreach (string candidate in GetItemIdCandidates(itemId))
        {
            if (ItemExtensionsApi.GetBreakingTool(candidate, isClump, out tool) && !string.IsNullOrWhiteSpace(tool))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetClumpResourceIds(ResourceClump clump)
    {
        foreach (KeyValuePair<string, string> pair in clump.modData.Pairs)
        {
            string normalizedValue = NormalizeItemId(pair.Value);
            if (IsNooksResourceId(normalizedValue))
                yield return normalizedValue;
        }
    }

    private static IEnumerable<string> GetItemIdCandidates(string itemId)
    {
        string normalized = NormalizeItemId(itemId);
        if (string.IsNullOrWhiteSpace(normalized))
            yield break;

        yield return itemId;
        yield return normalized;
        yield return $"(O){normalized}";
    }

    private static string NormalizeItemId(string? itemId)
    {
        string value = itemId?.Trim() ?? string.Empty;
        if (value.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
            value = value[3..];

        return value.Trim();
    }

    private static bool IsNooksResourceId(string itemId)
    {
        return itemId.StartsWith(NooksResourcePrefix, StringComparison.OrdinalIgnoreCase)
            && NooksResourceIds.Contains(itemId);
    }

    private static bool TryMapBreakingTool(string tool, out Type toolType, out bool anyTool)
    {
        string normalized = tool.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase).Replace("_", string.Empty, StringComparison.OrdinalIgnoreCase);
        anyTool = false;

        switch (normalized)
        {
            case "Axe":
                toolType = typeof(Axe);
                return true;
            case "Pickaxe":
            case "Pick":
                toolType = typeof(Pickaxe);
                return true;
            case "Hoe":
                toolType = typeof(Hoe);
                return true;
            case "WateringCan":
                toolType = typeof(WateringCan);
                return true;
            case "Any":
            case "AnyTool":
            case "AnyExceptWateringCan":
                toolType = typeof(MeleeWeapon);
                anyTool = true;
                return true;
            default:
                toolType = typeof(Axe);
                return false;
        }
    }


    private bool TrySupplyWalletTool(object walletToolsInstance, Farmer player, Type toolType, bool anyTool, out bool supplied)
    {
        supplied = false;
        if (WalletToolsSupplyMethod is null)
            return false;

        try
        {
            object? result = WalletToolsSupplyMethod.Invoke(walletToolsInstance, new object[] { player, toolType, anyTool });
            if (result is not bool value)
                return false;

            supplied = value;
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to ask Wallet Tools to supply a Nooks and Crannies resource tool: {ex.Message}", LogLevel.Error);
            return false;
        }
    }

    private void DebugLog(string message)
    {
        if (Config.EnableDebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }

    private static class WalletToolsPrepareToolUsePatch
    {
        public static bool Prefix(object __instance, Farmer player, ref bool __result)
        {
            ModEntry? instance = Instance;
            if (instance is null || !Context.IsWorldReady || Game1.fadeToBlack || !Context.CanPlayerMove)
                return true;

            if (!instance.TryGetNooksResourceToolRequest(player, out Type detectedToolType, out bool detectedAnyTool))
                return true;

            if (!instance.TrySupplyWalletTool(__instance, player, detectedToolType, detectedAnyTool, out bool supplied))
                return true;

            __result = supplied;
            return false;
        }
    }
}
