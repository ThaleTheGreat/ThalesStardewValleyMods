using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Powers;
using StardewValley.Tools;

namespace ThaleTheGreat.WalletToolsForSwordAndSorcery;

public sealed class ModEntry : Mod
{
    private const string WalletToolsUniqueId = "ThaleTheGreat.WalletTools";
    private const string WalletPowerPrefix = "ThaleTheGreat.WalletTools_";
    private static ModEntry? Instance;

    private Harmony Harmony = null!;
    private ModConfig Config = new();
    private bool GmcmRegistered;
    private bool PatchesAttempted;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        Harmony = new Harmony(ModManifest.UniqueID);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        PatchWalletTools();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        InvalidatePowers();
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!Config.ModEnabled || !Context.IsWorldReady || !e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;
            ApplySwordAndSorceryPowerIcons(powers);
        }, AssetEditPriority.Late);
    }

    private void ApplySwordAndSorceryPowerIcons(IDictionary<string, PowersData> powers)
    {
        IDictionary? storedTools = GetWalletStoredTools(Game1.player);
        if (storedTools is null)
            return;

        foreach (DictionaryEntry entry in storedTools)
        {
            if (entry.Key is null || entry.Value is null)
                continue;

            string kindName = entry.Key.ToString() ?? string.Empty;
            string powerId = WalletPowerPrefix + kindName;
            if (!powers.TryGetValue(powerId, out PowersData? power) || power is null)
                continue;

            if (!SwordAndSorceryToolData.TryGetIconData(entry.Value, kindName, out string texturePath, out Point texturePosition))
                continue;

            power.TexturePath = texturePath;
            power.TexturePosition = texturePosition;
            DebugLog($"Applied Sword and Sorcery wallet icon for {kindName}: {texturePath} at {texturePosition.X},{texturePosition.Y}.");
        }
    }

    private static IDictionary? GetWalletStoredTools(Farmer player)
    {
        object? walletEntry = GetWalletToolsEntryInstance();
        if (walletEntry is null)
            return null;

        MethodInfo? method = AccessTools.Method(walletEntry.GetType(), "GetStoredTools", new[] { typeof(Farmer) });
        return method?.Invoke(walletEntry, new object[] { player }) as IDictionary;
    }

    private static object? GetWalletToolsEntryInstance()
    {
        Type? walletEntryType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ModEntry");
        return walletEntryType is null ? null : AccessTools.Field(walletEntryType, "Instance")?.GetValue(null);
    }

    private void PatchWalletTools()
    {
        if (PatchesAttempted)
            return;

        PatchesAttempted = true;

        if (!Helper.ModRegistry.IsLoaded(WalletToolsUniqueId))
        {
            Monitor.Log("Wallet Tools was not detected. Sword and Sorcery compatibility patches were not applied.", LogLevel.Warn);
            return;
        }

        Type? upgradeCompatType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ToolAndSprinklerUpgradesCompat");
        Type? walletEntryType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.ModEntry");
        Type? walletKindType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.WalletToolKind");
        Type? walletStateType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.WalletToolState");

        bool patchedAny = false;

        MethodInfo? isUpgradeTool = upgradeCompatType is null ? null : AccessTools.Method(upgradeCompatType, "IsUpgradeTool");
        MethodInfo? getUpgradeLevel = upgradeCompatType is null ? null : AccessTools.Method(upgradeCompatType, "GetUpgradeLevel");
        MethodInfo? replaceStoredTool = walletEntryType is null || walletKindType is null
            ? null
            : AccessTools.Method(walletEntryType, "TryReplaceStoredToolIfBetter", new[] { typeof(Farmer), walletKindType, typeof(Tool) });
        MethodInfo? getTexturePath = walletStateType is null ? null : AccessTools.Method(walletStateType, "GetTexturePath");
        MethodInfo? getTexturePosition = walletStateType is null ? null : AccessTools.Method(walletStateType, "GetTexturePosition");

        if (isUpgradeTool is not null)
        {
            Harmony.Patch(isUpgradeTool, postfix: new HarmonyMethod(typeof(WalletToolsPatches), nameof(WalletToolsPatches.IsUpgradeToolPostfix)));
            patchedAny = true;
        }
        else
        {
            Monitor.Log("Could not find Wallet Tools upgrade recognition method. Sword and Sorcery fallback type recognition was not patched.", LogLevel.Warn);
        }

        if (getUpgradeLevel is not null)
        {
            Harmony.Patch(getUpgradeLevel, postfix: new HarmonyMethod(typeof(WalletToolsPatches), nameof(WalletToolsPatches.GetUpgradeLevelPostfix)));
            patchedAny = true;
        }
        else
        {
            Monitor.Log("Could not find Wallet Tools upgrade level method. Sword and Sorcery fallback level recognition was not patched.", LogLevel.Warn);
        }

        if (replaceStoredTool is not null)
        {
            Harmony.Patch(replaceStoredTool, postfix: new HarmonyMethod(typeof(WalletToolsPatches), nameof(WalletToolsPatches.TryReplaceStoredToolIfBetterPostfix)));
            patchedAny = true;
        }
        else
        {
            Monitor.Log("Could not find Wallet Tools storage replacement method. Same-tier Sword and Sorcery replacement preference was not patched.", LogLevel.Warn);
        }

        if (getTexturePath is not null)
        {
            Harmony.Patch(getTexturePath, postfix: new HarmonyMethod(typeof(WalletToolsPatches), nameof(WalletToolsPatches.GetTexturePathPostfix)));
            patchedAny = true;
        }
        else
        {
            Monitor.Log("Could not find Wallet Tools texture path method. The direct Data/Powers icon edit will still run.", LogLevel.Warn);
        }

        if (getTexturePosition is not null)
        {
            Harmony.Patch(getTexturePosition, postfix: new HarmonyMethod(typeof(WalletToolsPatches), nameof(WalletToolsPatches.GetTexturePositionPostfix)));
            patchedAny = true;
        }
        else
        {
            Monitor.Log("Could not find Wallet Tools texture position method. The direct Data/Powers icon edit will still run.", LogLevel.Warn);
        }

        if (patchedAny)
            DebugLog("Applied Wallet Tools compatibility patches for Sword and Sorcery tools.");

        InvalidatePowers();
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Unregister(ModManifest);
        gmcm.Register(ModManifest, ResetConfig, SaveConfig);
        gmcm.AddBoolOption(ModManifest, () => Config.ModEnabled, value => SetModEnabled(value), () => "Mod Enabled", () => "Enables Wallet Tools support for Sword and Sorcery tools.", nameof(Config.ModEnabled));
        gmcm.AddBoolOption(ModManifest, () => Config.PreferSwordAndSorcerySameTierTools, value => Config.PreferSwordAndSorcerySameTierTools = value, () => "Prefer Same-Tier Sword and Sorcery Tools", () => "When a Sword and Sorcery tool has the same upgrade tier as the stored wallet tool, prefer the Sword and Sorcery version.", nameof(Config.PreferSwordAndSorcerySameTierTools));
        gmcm.AddBoolOption(ModManifest, () => Config.DebugLogging, value => Config.DebugLogging = value, () => "Debug Logging", () => "Enables extra compatibility diagnostics in the SMAPI console.", nameof(Config.DebugLogging));
        GmcmRegistered = true;
    }

    private void SetModEnabled(bool value)
    {
        Config.ModEnabled = value;
        InvalidatePowers();
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
        SaveConfig();
        InvalidatePowers();
    }

    private void SaveConfig()
    {
        Helper.WriteConfig(Config);
    }

    private void InvalidatePowers()
    {
        if (Context.IsWorldReady)
            Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private static bool IsEnabled()
    {
        return Instance?.Config.ModEnabled == true;
    }

    private static bool ShouldPreferSameTierTools()
    {
        return IsEnabled() && Instance?.Config.PreferSwordAndSorcerySameTierTools == true;
    }

    private static void DebugLog(string message)
    {
        if (Instance?.Config.DebugLogging == true)
            Instance.Monitor.Log(message, LogLevel.Debug);
    }

    private static class WalletToolsPatches
    {
        public static void IsUpgradeToolPostfix(Tool tool, string suffix, ref bool __result)
        {
            if (__result || !IsEnabled())
                return;

            __result = SwordAndSorceryToolData.IsTool(tool, suffix);
        }

        public static void GetUpgradeLevelPostfix(Tool tool, ref int __result)
        {
            if (!IsEnabled())
                return;

            int level = SwordAndSorceryToolData.GetUpgradeLevel(tool);
            if (level > __result)
                __result = level;
        }

        public static void TryReplaceStoredToolIfBetterPostfix(object __instance, Farmer player, object kind, Tool candidate, ref bool __result)
        {
            if (__result || !ShouldPreferSameTierTools() || !SwordAndSorceryToolData.IsTool(candidate))
                return;

            int candidateLevel = SwordAndSorceryToolData.GetUpgradeLevel(candidate);
            if (candidateLevel <= 0)
                return;

            IDictionary? storedTools = GetStoredTools(__instance, player);
            if (storedTools is null || !storedTools.Contains(kind))
                return;

            object? existingState = storedTools[kind];
            if (existingState is null || SwordAndSorceryToolData.IsState(existingState))
                return;

            int existingLevel = GetStateUpgradeLevel(existingState);
            if (existingLevel != candidateLevel)
                return;

            object? replacementState = CreateWalletToolState(kind, candidate);
            if (replacementState is null)
                return;

            storedTools[kind] = replacementState;
            __result = true;
            Instance?.InvalidatePowers();
            DebugLog($"Preferred same-tier Sword and Sorcery wallet tool '{candidate.DisplayName}' over existing level {existingLevel} tool.");
        }

        public static void GetTexturePathPostfix(object __instance, ref string __result)
        {
            if (!IsEnabled())
                return;

            if (SwordAndSorceryToolData.TryGetIconData(__instance, null, out string texturePath, out _))
                __result = texturePath;
        }

        public static void GetTexturePositionPostfix(object __instance, ref Point __result)
        {
            if (!IsEnabled())
                return;

            if (SwordAndSorceryToolData.TryGetIconData(__instance, null, out _, out Point texturePosition))
                __result = texturePosition;
        }

        private static IDictionary? GetStoredTools(object walletToolsEntry, Farmer player)
        {
            MethodInfo? method = AccessTools.Method(walletToolsEntry.GetType(), "GetStoredTools", new[] { typeof(Farmer) });
            return method?.Invoke(walletToolsEntry, new object[] { player }) as IDictionary;
        }

        private static object? CreateWalletToolState(object kind, Tool candidate)
        {
            Type? stateType = AccessTools.TypeByName("ThaleTheGreat.WalletTools.WalletToolState");
            MethodInfo? fromTool = stateType is null ? null : AccessTools.Method(stateType, "FromTool");
            return fromTool?.Invoke(null, new[] { kind, candidate });
        }

        private static int GetStateUpgradeLevel(object state)
        {
            object? value = AccessTools.Property(state.GetType(), "UpgradeLevel")?.GetValue(state)
                ?? AccessTools.Field(state.GetType(), "UpgradeLevel")?.GetValue(state);
            return value is int level ? level : 0;
        }
    }
}

internal static class SwordAndSorceryToolData
{
    private const string StygiumTexturePath = "Textures/DN.SnS/StygiumTools";
    private const string BlessedTexturePath = "Textures/DN.SnS/BlessedTools";

    private static readonly string[] ToolSuffixes =
    {
        "Axe",
        "Pickaxe",
        "Hoe",
        "WateringCan"
    };

    private static readonly Dictionary<string, Point> TexturePositionsByKind = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Hoe"] = new Point(80, 32),
        ["Pickaxe"] = new Point(80, 64),
        ["Axe"] = new Point(80, 96),
        ["WateringCan"] = new Point(48, 128)
    };

    public static bool IsTool(Tool tool)
    {
        return ToolSuffixes.Any(suffix => IsTool(tool, suffix));
    }

    public static bool IsTool(Tool tool, string suffix)
    {
        string normalizedSuffix = NormalizeSuffix(suffix);
        string text = GetToolIdentityText(tool);
        return ContainsToolIdentity(text, "Stygium", normalizedSuffix) || ContainsToolIdentity(text, "Blessed", normalizedSuffix);
    }

    public static int GetUpgradeLevel(Tool tool)
    {
        string text = GetToolIdentityText(tool);
        if (ContainsTierIdentity(text, "Blessed"))
            return 6;
        if (ContainsTierIdentity(text, "Stygium"))
            return 5;
        return 0;
    }

    public static bool IsState(object state)
    {
        return TryGetIconData(state, null, out _, out _);
    }

    public static bool TryGetIconData(object state, string? walletKindName, out string texturePath, out Point texturePosition)
    {
        texturePath = string.Empty;
        texturePosition = Point.Zero;

        string identity = GetStateIdentityText(state);
        string tier = GetTier(identity);
        if (string.IsNullOrWhiteSpace(tier))
            return false;

        string kindName = !string.IsNullOrWhiteSpace(walletKindName) ? walletKindName : GetKindName(identity);
        if (string.IsNullOrWhiteSpace(kindName) || !TexturePositionsByKind.TryGetValue(kindName, out texturePosition))
            return false;

        texturePath = tier.Equals("Blessed", StringComparison.OrdinalIgnoreCase) ? BlessedTexturePath : StygiumTexturePath;
        return true;
    }

    private static string GetTier(string identity)
    {
        if (ContainsTierIdentity(identity, "Blessed"))
            return "Blessed";
        if (ContainsTierIdentity(identity, "Stygium"))
            return "Stygium";
        return string.Empty;
    }

    private static string GetKindName(string identity)
    {
        string compact = identity.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        foreach (string suffix in ToolSuffixes)
        {
            if (compact.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                return suffix;
        }

        return string.Empty;
    }

    private static bool ContainsToolIdentity(string text, string tier, string suffix)
    {
        string compact = text.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
        return compact.Contains($"DN.SnS_{tier}{suffix}", StringComparison.OrdinalIgnoreCase)
            || compact.Contains($"{tier}{suffix}", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsTierIdentity(string text, string tier)
    {
        return text.Contains($"DN.SnS_{tier}", StringComparison.OrdinalIgnoreCase)
            || text.Contains(tier, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeSuffix(string suffix)
    {
        if (suffix.Equals("Watering Can", StringComparison.OrdinalIgnoreCase))
            return "WateringCan";

        return suffix.Replace(" ", string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetToolIdentityText(Tool tool)
    {
        return string.Join("\n", tool.GetType().FullName, tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
    }

    private static string GetStateIdentityText(object state)
    {
        return string.Join(
            "\n",
            GetStringMember(state, "QualifiedItemId"),
            GetStringMember(state, "Name"),
            GetStringMember(state, "DisplayName"),
            GetStringMember(state, "TexturePath")
        );
    }

    private static string GetStringMember(object instance, string memberName)
    {
        object? value = AccessTools.Property(instance.GetType(), memberName)?.GetValue(instance)
            ?? AccessTools.Field(instance.GetType(), memberName)?.GetValue(instance);
        return value as string ?? string.Empty;
    }
}
