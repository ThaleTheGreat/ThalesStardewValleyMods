using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Delegates;
using StardewValley.GameData.Shops;
using StardewValley.Tools;

namespace ThaleTheGreat.WalletToolsForToolUpgradeBundles;

public sealed class ModEntry : Mod
{
    private const string WalletToolsUniqueId = "ThaleTheGreat.WalletTools";
    private const string ToolUpgradeBundlesCodeUniqueId = "Pixeltica.SMAPI.Tool_Bundles";
    private const string ToolUpgradeBundlesContentUniqueId = "Pixeltica.Tool_Bundles";
    private const string ToolUpgradeBundlesShopId = "Pixeltica.Tool_Bundles_ToolUpgradeShop";
    private const string ToolUpgradeHandlerTypeName = "ToolUpgradeBundles.ToolUpgradeHandler";
    private const string WalletToolsModEntryTypeName = "ThaleTheGreat.WalletTools.ModEntry";
    private const string WalletToolsMenuVirtualToolMarker = "ThaleTheGreat.WalletTools/MenuVirtualTool";
    private const string WalletToolsRuntimeToolMarker = "ThaleTheGreat.WalletTools/RuntimeTool";
    private const string WalletToolsOvernightExposureMarker = "ThaleTheGreat.WalletTools/OvernightExposure";
    private const string WalletToolQuery = "THALE_WALLET_TOOLS_FOR_TUB_HAS_TOOL";

    private static ModEntry? Instance;
    private Harmony Harmony = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Harmony = new Harmony(ModManifest.UniqueID);
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        if (!Helper.ModRegistry.IsLoaded(WalletToolsUniqueId) || !Helper.ModRegistry.IsLoaded(ToolUpgradeBundlesCodeUniqueId))
            return;

        RegisterWalletToolQuery();
        PatchToolUpgradeBundlesApplyAction();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, ShopData> shops = asset.AsDictionary<string, ShopData>().Data;
            if (!shops.TryGetValue(ToolUpgradeBundlesShopId, out ShopData? shop) || shop.Items is null)
                return;

            foreach (ShopItemData item in shop.Items)
            {
                string? oldToolId = GetOldToolIdFromPurchaseActions(item.ActionsOnPurchase);
                if (oldToolId is null || !IsWalletSupportedUpgradeToolId(oldToolId))
                    continue;

                item.Condition = RewriteToolRequirementCondition(item.Condition, oldToolId);
            }
        }, AssetEditPriority.Late);
    }

    private void RegisterWalletToolQuery()
    {
        try
        {
            GameStateQuery.Register(WalletToolQuery, HasUpgradeToolQuery);
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not register Tool Upgrade Bundles wallet-tool game-state query: {ex}", LogLevel.Warn);
        }
    }

    private void PatchToolUpgradeBundlesApplyAction()
    {
        Type? handlerType = AccessTools.TypeByName(ToolUpgradeHandlerTypeName);
        if (handlerType is null)
        {
            Monitor.Log("Could not find Tool Upgrade Bundles tool handler.", LogLevel.Warn);
            return;
        }

        MethodInfo? target = AccessTools.Method(handlerType, "UpgradeRegularTool");
        MethodInfo? prefix = AccessTools.Method(typeof(ModEntry), nameof(UpgradeRegularToolPrefix));
        if (target is null || prefix is null)
        {
            Monitor.Log("Could not patch Tool Upgrade Bundles regular tool upgrade flow.", LogLevel.Warn);
            return;
        }

        Harmony.Patch(target, prefix: new HarmonyMethod(prefix));
    }

    private static bool HasUpgradeToolQuery(string[] query, GameStateQueryContext context)
    {
        if (query.Length < 3)
            return false;

        string requiredToolId = query[2];
        if (string.IsNullOrWhiteSpace(requiredToolId) || !IsWalletSupportedUpgradeToolId(requiredToolId))
            return false;

        Farmer player = Game1.player;
        return PlayerInventoryContainsExactNonTemporaryQualifiedTool(player, requiredToolId)
            || TryFindStoredWalletTool(player, requiredToolId, out _, out _);
    }

    private static string? GetOldToolIdFromPurchaseActions(List<string>? actions)
    {
        if (actions is null)
            return null;

        foreach (string action in actions)
        {
            List<string> tokens = Tokenize(action);
            if (tokens.Count >= 3 && tokens[0].Equals("Pixeltica_ApplyToolUpgrade", StringComparison.OrdinalIgnoreCase))
                return tokens[1];
        }

        return null;
    }

    private static string RewriteToolRequirementCondition(string? condition, string oldToolId)
    {
        string walletRequirement = $"{WalletToolQuery} Current {oldToolId}";
        if (string.IsNullOrWhiteSpace(condition))
            return walletRequirement;

        if (condition.Contains(WalletToolQuery, StringComparison.OrdinalIgnoreCase))
            return condition;

        string pattern = $@"\bPLAYER_HAS_ITEM\s+Current\s+{Regex.Escape(oldToolId)}(\s+\d+)?\b";
        string rewritten = Regex.Replace(condition, pattern, walletRequirement, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!rewritten.Equals(condition, StringComparison.Ordinal))
            return rewritten;

        return walletRequirement + ", " + condition;
    }

    private static bool UpgradeRegularToolPrefix(string oldToolId, string newToolId, ref bool __result, ref string? error)
    {
        if (string.IsNullOrWhiteSpace(oldToolId) || string.IsNullOrWhiteSpace(newToolId) || !IsWalletSupportedUpgradeToolId(oldToolId))
            return true;

        if (PlayerInventoryContainsExactNonTemporaryQualifiedTool(Game1.player, oldToolId))
            return true;

        if (!TryFindStoredWalletTool(Game1.player, oldToolId, out StoredWalletTool storedTool, out string? lookupError))
        {
            if (!string.IsNullOrWhiteSpace(lookupError))
            {
                error = lookupError;
                __result = false;
                return false;
            }

            return true;
        }

        if (newToolId.Contains("Bamboo", StringComparison.OrdinalIgnoreCase))
            newToolId = newToolId.Replace("Rod", "Pole", StringComparison.OrdinalIgnoreCase);

        try
        {
            Tool newTool = ItemRegistry.Create<Tool>(newToolId);
            newTool.UpgradeFrom(storedTool.Tool);

            object? newState = CreateWalletToolState(storedTool.State, storedTool.Kind, newTool);
            if (newState is null)
            {
                error = $"Wallet Tools could not store upgraded tool '{newToolId}'.";
                __result = false;
                return false;
            }

            storedTool.StoredTools[storedTool.Kind] = newState;
            RefreshWalletToolsState(Game1.player);

            string tempObjectId = $"(O)Pixeltica_UpgradeShopStock_{NormalizeToolId(newToolId)}";
            Game1.player.removeFirstOfThisItemFromInventory(tempObjectId);

            Game1.exitActiveMenu();
            Game1.player.holdUpItemThenMessage(newTool);

            error = null;
            __result = true;
            return false;
        }
        catch (Exception ex)
        {
            Instance?.Monitor.Log($"Failed to apply Tool Upgrade Bundles upgrade to Wallet Tools stored tool '{oldToolId}' -> '{newToolId}': {ex}", LogLevel.Error);
            error = $"Wallet Tools compatibility failed while upgrading '{oldToolId}'.";
            __result = false;
            return false;
        }
    }

    private static bool PlayerInventoryContainsExactNonTemporaryQualifiedTool(Farmer player, string requiredQualifiedItemId)
    {
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool
                && !IsTemporaryWalletTool(tool)
                && ToolMatchesRequiredId(tool, requiredQualifiedItemId))
                return true;
        }

        return false;
    }

    private static bool IsTemporaryWalletTool(Tool tool)
    {
        return tool.modData.ContainsKey(WalletToolsMenuVirtualToolMarker)
            || tool.modData.ContainsKey(WalletToolsRuntimeToolMarker)
            || tool.modData.ContainsKey(WalletToolsOvernightExposureMarker);
    }

    private static bool TryFindStoredWalletTool(Farmer player, string oldToolId, out StoredWalletTool storedTool, out string? error)
    {
        storedTool = null!;
        error = null;

        object? walletEntry = GetWalletToolsModEntry();
        if (walletEntry is null)
            return false;

        IDictionary? storedTools = GetStoredTools(walletEntry, player);
        if (storedTools is null)
        {
            error = "Wallet Tools compatibility could not read stored wallet tools.";
            return false;
        }

        foreach (DictionaryEntry entry in storedTools)
        {
            if (entry.Key is null || entry.Value is null)
                continue;

            Tool? tool = CreateToolFromWalletState(entry.Value);
            if (tool is null || WalletToolStateIsErrorTool(tool))
                continue;

            if (!ToolMatchesRequiredId(tool, oldToolId))
                continue;

            storedTool = new StoredWalletTool(storedTools, entry.Key, entry.Value, tool);
            return true;
        }

        return false;
    }

    private static object? GetWalletToolsModEntry()
    {
        Type? walletType = AccessTools.TypeByName(WalletToolsModEntryTypeName);
        if (walletType is null)
            return null;

        FieldInfo? instanceField = AccessTools.Field(walletType, "Instance");
        return instanceField?.GetValue(null);
    }

    private static IDictionary? GetStoredTools(object walletEntry, Farmer player)
    {
        MethodInfo? getStoredTools = AccessTools.Method(walletEntry.GetType(), "GetStoredTools", new[] { typeof(Farmer) });
        return getStoredTools?.Invoke(walletEntry, new object[] { player }) as IDictionary;
    }

    private static Tool? CreateToolFromWalletState(object state)
    {
        MethodInfo? createTool = AccessTools.Method(state.GetType(), "CreateTool", new[] { typeof(IMonitor) });
        if (createTool is null)
            return null;

        return createTool.Invoke(state, new object?[] { Instance?.Monitor }) as Tool;
    }

    private static object? CreateWalletToolState(object oldState, object kind, Tool tool)
    {
        MethodInfo? fromTool = AccessTools.Method(oldState.GetType(), "FromTool");
        return fromTool?.Invoke(null, new[] { kind, tool });
    }

    private static void RefreshWalletToolsState(Farmer player)
    {
        object? walletEntry = GetWalletToolsModEntry();
        if (walletEntry is null)
            return;

        MethodInfo? refresh = AccessTools.Method(walletEntry.GetType(), "RefreshWalletStateAfterStoredToolChange", new[] { typeof(Farmer), typeof(bool) });
        refresh?.Invoke(walletEntry, new object[] { player, true });
    }

    private static bool ToolMatchesRequiredId(Tool tool, string requiredId)
    {
        foreach (string value in GetToolIdentityValues(tool))
        {
            if (string.Equals(value, requiredId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (string.Equals(NormalizeToolId(value), NormalizeToolId(requiredId), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static IEnumerable<string> GetToolIdentityValues(Tool tool)
    {
        if (!string.IsNullOrWhiteSpace(tool.QualifiedItemId))
            yield return tool.QualifiedItemId;
        if (!string.IsNullOrWhiteSpace(tool.ItemId))
            yield return tool.ItemId;
        if (!string.IsNullOrWhiteSpace(tool.ItemId))
            yield return "(T)" + tool.ItemId;
        if (!string.IsNullOrWhiteSpace(tool.Name))
            yield return tool.Name;
        if (!string.IsNullOrWhiteSpace(tool.DisplayName))
            yield return tool.DisplayName;
    }

    private static bool IsWalletSupportedUpgradeToolId(string itemId)
    {
        string normalized = NormalizeToolId(itemId);
        return normalized.Contains("Axe", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Pickaxe", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Hoe", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("WateringCan", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("Pan", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeToolId(string value)
    {
        string normalized = value.Trim();
        if (normalized.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
            normalized = normalized[3..];

        int slash = normalized.LastIndexOf('/');
        if (slash >= 0 && slash < normalized.Length - 1)
            normalized = normalized[(slash + 1)..];

        return normalized.Replace(" ", string.Empty, StringComparison.Ordinal).Trim();
    }

    private static bool WalletToolStateIsErrorTool(Tool tool)
    {
        string name = string.Join("\n", tool.Name, tool.DisplayName, tool.ItemId, tool.QualifiedItemId);
        return name.Contains("Error Item", StringComparison.OrdinalIgnoreCase)
            || name.Contains("ErrorItem", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> Tokenize(string value)
    {
        List<string> tokens = new();
        bool inQuote = false;
        char quote = '\0';
        int start = -1;

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (inQuote)
            {
                if (c == quote)
                {
                    tokens.Add(value.Substring(start, i - start));
                    start = -1;
                    inQuote = false;
                }
                continue;
            }

            if (c == '"' || c == '\'')
            {
                inQuote = true;
                quote = c;
                start = i + 1;
                continue;
            }

            if (char.IsWhiteSpace(c))
            {
                if (start >= 0)
                {
                    tokens.Add(value.Substring(start, i - start));
                    start = -1;
                }
                continue;
            }

            if (start < 0)
                start = i;
        }

        if (start >= 0)
            tokens.Add(value.Substring(start));

        return tokens;
    }

    private sealed class StoredWalletTool
    {
        public StoredWalletTool(IDictionary storedTools, object kind, object state, Tool tool)
        {
            StoredTools = storedTools;
            Kind = kind;
            State = state;
            Tool = tool;
        }

        public IDictionary StoredTools { get; }
        public object Kind { get; }
        public object State { get; }
        public Tool Tool { get; }
    }
}
