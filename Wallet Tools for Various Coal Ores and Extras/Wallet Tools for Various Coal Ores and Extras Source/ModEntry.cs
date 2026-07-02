using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;
using Object = StardewValley.Object;

namespace ThaleTheGreat.WalletToolsForVariousCoalOresAndExtras;

public sealed class ModEntry : Mod
{
    private const string VariousCoalNodePrefix = "6135.VariousCoalOresExtras_";

    private static ModEntry? Instance;
    private Harmony Harmony = null!;
    private IItemExtensionsApi? ItemExtensionsApi;
    private FieldInfo? WalletToolsInstanceField;
    private MethodInfo? WalletToolsSupplyMethod;
    private bool ReflectionWarningLogged;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Harmony = new Harmony(ModManifest.UniqueID);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        PatchUseToolButton();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        ItemExtensionsApi = Helper.ModRegistry.GetApi<IItemExtensionsApi>("mistyspring.ItemExtensions");
        CacheWalletToolsReflection();
    }

    private void PatchUseToolButton()
    {
        MethodInfo? target = AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton));
        MethodInfo? prefix = AccessTools.Method(typeof(PressUseToolButtonPatch), nameof(PressUseToolButtonPatch.Prefix));

        if (target is null || prefix is null)
        {
            Monitor.Log("Could not patch Game1.pressUseToolButton for Various Coal Ores and Extras wallet pickaxe support.", LogLevel.Error);
            return;
        }

        Harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private bool TryPrepareWalletToolUse(Farmer player)
    {
        if (!Context.IsWorldReady || Game1.fadeToBlack || !Context.CanPlayerMove || Game1.activeClickableMenu is not null)
            return false;

        if (!TryGetTargetVariousCoalNode(player, out Object node))
            return false;

        if (!TryGetBreakingToolType(node, out Type toolType))
            return false;

        if (toolType.IsInstanceOfType(player.CurrentItem))
            return false;

        return TrySupplyWalletTool(player, toolType);
    }

    private static bool TryGetTargetVariousCoalNode(Farmer player, out Object node)
    {
        node = null!;
        GameLocation location = player.currentLocation;
        if (location is null)
            return false;

        Vector2 position = !Game1.wasMouseVisibleThisFrame
            ? player.GetToolLocation(false)
            : new Vector2(Game1.getOldMouseX() + Game1.viewport.X, Game1.getOldMouseY() + Game1.viewport.Y);

        Vector2 tile = player.GetToolLocation(position, false) / 64f;
        tile = new Vector2((int)tile.X, (int)tile.Y);

        return location.objects.TryGetValue(tile, out node) && IsVariousCoalNode(node);
    }

    private bool TryGetBreakingToolType(Object node, out Type toolType)
    {
        foreach (string id in GetCandidateIds(node))
        {
            if (TryGetBreakingToolTypeFromItemExtensions(id, out toolType))
                return true;
        }

        toolType = typeof(Pickaxe);
        return IsVariousCoalNode(node);
    }

    private bool TryGetBreakingToolTypeFromItemExtensions(string id, out Type toolType)
    {
        toolType = typeof(Pickaxe);
        IItemExtensionsApi? api = ItemExtensionsApi ??= Helper.ModRegistry.GetApi<IItemExtensionsApi>("mistyspring.ItemExtensions");
        if (api is null)
            return false;

        try
        {
            if (!api.GetBreakingTool(id, false, out string toolName) && !api.IsResource(id, out _, out _))
                return false;

            if (string.IsNullOrWhiteSpace(toolName) && !api.GetBreakingTool(id, false, out toolName))
                return false;

            return TryConvertToolName(toolName, out toolType);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed checking Item Extensions resource '{id}': {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private static bool TryConvertToolName(string toolName, out Type toolType)
    {
        string normalized = toolName.Replace(" ", string.Empty).Trim();
        if (normalized.Equals("Pick", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Pickaxe", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(Pickaxe);
            return true;
        }

        if (normalized.Equals("Axe", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(Axe);
            return true;
        }

        if (normalized.Equals("Hoe", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(Hoe);
            return true;
        }

        if (normalized.Equals("WateringCan", StringComparison.OrdinalIgnoreCase))
        {
            toolType = typeof(WateringCan);
            return true;
        }

        toolType = typeof(Pickaxe);
        return false;
    }

    private bool TrySupplyWalletTool(Farmer player, Type toolType)
    {
        CacheWalletToolsReflection();
        object? walletTools = WalletToolsInstanceField?.GetValue(null);
        if (walletTools is null || WalletToolsSupplyMethod is null)
        {
            LogReflectionWarning();
            return false;
        }

        try
        {
            return WalletToolsSupplyMethod.Invoke(walletTools, new object[] { player, toolType, false }) is true;
        }
        catch (TargetInvocationException ex)
        {
            Monitor.Log($"Wallet Tools rejected the requested {toolType.Name}: {ex.InnerException?.Message ?? ex.Message}", LogLevel.Warn);
            return false;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to supply the requested {toolType.Name} from Wallet Tools: {ex.Message}", LogLevel.Warn);
            return false;
        }
    }

    private void CacheWalletToolsReflection()
    {
        if (WalletToolsInstanceField is not null && WalletToolsSupplyMethod is not null)
            return;

        Type? walletToolsEntry = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ModEntry");
        if (walletToolsEntry is null)
            return;

        WalletToolsInstanceField ??= AccessTools.Field(walletToolsEntry, "Instance");
        WalletToolsSupplyMethod ??= AccessTools.Method(
            walletToolsEntry,
            "TrySupplyRequestedWalletTool",
            new[] { typeof(Farmer), typeof(Type), typeof(bool) }
        );
    }

    private void LogReflectionWarning()
    {
        if (ReflectionWarningLogged)
            return;

        ReflectionWarningLogged = true;
        Monitor.Log("Could not access Wallet Tools' tool supply method. Various Coal Ores and Extras custom nodes will not auto-use wallet tools.", LogLevel.Warn);
    }

    private static bool IsVariousCoalNode(Object node)
    {
        foreach (string id in GetCandidateIds(node))
        {
            string normalized = NormalizeItemId(id);
            if (normalized.StartsWith(VariousCoalNodePrefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetCandidateIds(Object node)
    {
        HashSet<string> ids = new(StringComparer.OrdinalIgnoreCase);
        AddId(ids, node.QualifiedItemId);
        AddId(ids, node.ItemId);
        AddId(ids, node.Name);

        foreach (KeyValuePair<string, string> pair in node.modData.Pairs)
        {
            AddId(ids, pair.Key);
            AddId(ids, pair.Value);
        }

        return ids;
    }

    private static void AddId(HashSet<string> ids, string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return;

        ids.Add(id);
        string normalized = NormalizeItemId(id);
        if (!string.IsNullOrWhiteSpace(normalized))
        {
            ids.Add(normalized);
            ids.Add($"(O){normalized}");
        }
    }

    private static string NormalizeItemId(string id)
    {
        string normalized = id.Trim();
        if (normalized.StartsWith("(O)", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];

        int slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash < normalized.Length - 1)
            normalized = normalized[(slash + 1)..];

        return normalized.Trim();
    }

    private static class PressUseToolButtonPatch
    {
        public static void Prefix()
        {
            if (Instance is not null && Context.IsWorldReady)
                Instance.TryPrepareWalletToolUse(Game1.player);
        }
    }
}

public interface IItemExtensionsApi
{
    bool IsResource(string id, out int? health, out string itemDropped);
    bool GetBreakingTool(string id, bool isClump, out string tool);
}
