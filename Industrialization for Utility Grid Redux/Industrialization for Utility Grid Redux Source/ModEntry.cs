using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;

namespace ThaleTheGreat.IndustrializationForUtilityGridRedux;

public sealed class ModEntry : Mod
{
    private const string ObjectTextureAsset = "Mods/ThaleTheGreat.IndustrializationForUtilityGridRedux/Objects";
    private const string BigCraftableTextureAsset = "Mods/ThaleTheGreat.IndustrializationForUtilityGridRedux/BigCraftables";
    private const int MinProducedAmount = 1;
    private const int MaxProducedAmount = 2500;
    private const int MinConsumedAmount = 1;
    private const int MaxConsumedAmount = 250;

    private ModConfig Config = new();
    private readonly Dictionary<string, object?> originalUtilityGridRules = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> managedUtilityGridRuleKeys = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object?> objectEntries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object?> bigCraftableEntries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object?> craftingRecipes = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object?> machineEntries = new(StringComparer.OrdinalIgnoreCase);
    private const string BetterCraftingModId = "leclair.bettercrafting";
    private const string BetterCraftingIndustrializationCategoryId = "industrialization-for-utility-grid-redux";

    private Dictionary<string, object?> utilityGridEntries = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, object?> shopEntries = new(StringComparer.OrdinalIgnoreCase);

    public override void Entry(IModHelper helper)
    {
        this.objectEntries = this.ReadDictionary("assets/Data/objects.json");
        this.bigCraftableEntries = this.ReadDictionary("assets/Data/big-craftables.json");
        this.craftingRecipes = this.ReadDictionary("assets/Data/crafting-recipes.json");
        this.machineEntries = this.ReadDictionary("assets/Data/machines.json");
        this.shopEntries = this.ReadDictionary("assets/Data/shops.json");
        this.utilityGridEntries = this.ReadDictionary("assets/UtilityGrid/utility-grid-object-entries.json");
        this.Config = helper.ReadConfig<ModConfig>();
        this.EnsureConfigDefaults();
        this.NormalizeConfig();
        this.ApplyUtilityGridTooltips();

        MachineLogic.Initialize(helper.DirectoryPath, this.Monitor);

        helper.Events.Content.AssetRequested += this.OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(ObjectTextureAsset))
        {
            e.LoadFromModFile<Texture2D>("assets/Generated/Objects.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo(BigCraftableTextureAsset))
        {
            e.LoadFromModFile<Texture2D>("assets/Generated/BigCraftables.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            e.Edit(asset => ApplyEntries(asset.Data, this.objectEntries));
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
            e.Edit(asset => ApplyEntries(asset.Data, this.bigCraftableEntries));
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            e.Edit(asset => ApplyEntries(asset.Data, this.craftingRecipes));
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            e.Edit(asset => this.ApplyMachineEntries(asset.Data), (AssetEditPriority)((int)AssetEditPriority.Late + 1000));
        else if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
            e.Edit(asset => this.ApplyShopEntries(asset.Data));
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterUtilityGridRules();
        this.RegisterBetterCraftingCategory();
        this.RegisterGmcm();
    }

    private void RegisterBetterCraftingCategory()
    {
        Leclair.Stardew.BetterCrafting.IBetterCrafting? betterCrafting = this.Helper.ModRegistry.GetApi<Leclair.Stardew.BetterCrafting.IBetterCrafting>(BetterCraftingModId);
        if (betterCrafting is null)
            return;

        string[] recipeNames = this.craftingRecipes.Keys
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (recipeNames.Length == 0)
            return;

        string iconRecipe = recipeNames.Contains("ThaleTheGreat.IndustrializationForUtilityGridRedux_ElectricFurnace", StringComparer.OrdinalIgnoreCase)
            ? "ThaleTheGreat.IndustrializationForUtilityGridRedux_ElectricFurnace"
            : recipeNames[0];

        this.ExcludeBetterCraftingMachineRuleRecipes(recipeNames);

        betterCrafting.CreateDefaultCategory(false, BetterCraftingIndustrializationCategoryId, () => "Industrialization", recipeNames, iconRecipe, false, null);
        betterCrafting.AddRecipesToDefaultCategory(false, BetterCraftingIndustrializationCategoryId, recipeNames);
        betterCrafting.RemoveRecipesFromDefaultCategory(false, "machines", recipeNames);
    }

    private void ExcludeBetterCraftingMachineRuleRecipes(IEnumerable<string> recipeNames)
    {
        Assembly? utilityGridAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "UtilityGridRedux", StringComparison.OrdinalIgnoreCase));
        Type? modEntryType = utilityGridAssembly?.GetType("ThaleTheGreat.UtilityGridRedux.ModEntry");
        MethodInfo? method = modEntryType?.GetMethod("ExcludeBetterCraftingMachineRecipes", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (method is null)
            return;

        try
        {
            method.Invoke(null, new object?[] { recipeNames.ToArray() });
        }
        catch (Exception)
        {
        }
    }

    private void ApplyUtilityGridTooltips()
    {
        foreach ((string displayName, object? rawRule) in this.utilityGridEntries)
        {
            if (rawRule is not Dictionary<string, object?> rule)
                continue;

            string[] keys = this.GetUtilityGridRuleKeys(displayName).ToArray();
            if (this.IsUtilityGridMachineDisabled(keys))
                continue;

            Dictionary<string, object?>? configuredRule = this.GetConfiguredRuleData(displayName, rule);
            string tooltipText = configuredRule is null ? string.Empty : FormatUtilityGridTooltip(configuredRule);

            Dictionary<string, object?>? bigCraftable = this.GetBigCraftableByDisplayName(displayName);
            if (bigCraftable is null)
                continue;

            string description = Convert.ToString(bigCraftable.GetValueOrDefault("Description"), CultureInfo.InvariantCulture) ?? string.Empty;
            description = RemoveUtilityGridTooltip(description);

            bigCraftable["Description"] = string.IsNullOrWhiteSpace(tooltipText)
                ? description
                : string.IsNullOrWhiteSpace(description)
                    ? tooltipText
                    : EnsureSentenceTerminator(description.TrimEnd()) + " " + tooltipText;
        }
    }

    private void RegisterUtilityGridRules()
    {
        Assembly? utilityGridAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "UtilityGridRedux", StringComparison.OrdinalIgnoreCase));
        if (utilityGridAssembly is null)
        {
            this.Monitor.Log("Utility Grid Redux assembly was not found, so Industrialization machines could not be registered with the utility grid.", LogLevel.Warn);
            return;
        }

        Type? modEntryType = utilityGridAssembly.GetType("ThaleTheGreat.UtilityGridRedux.ModEntry");
        Type? ruleType = utilityGridAssembly.GetType("ThaleTheGreat.UtilityGridRedux.UtilityObjectRule");
        FieldInfo? rulesField = modEntryType?.GetField("ObjectRules", BindingFlags.Static | BindingFlags.NonPublic);
        if (modEntryType is null || ruleType is null || rulesField?.GetValue(null) is not IDictionary objectRules)
        {
            this.Monitor.Log("Utility Grid Redux rule table was not available, so Industrialization machines could not be registered with the utility grid.", LogLevel.Warn);
            return;
        }

        this.RestoreManagedUtilityGridRules(objectRules);

        int added = 0;
        foreach ((string displayName, object? rawRule) in this.utilityGridEntries)
        {
            if (rawRule is not Dictionary<string, object?> ruleData)
                continue;

            string[] keys = this.GetUtilityGridRuleKeys(displayName).ToArray();
            if (this.IsUtilityGridMachineDisabled(keys))
                continue;

            Dictionary<string, object?>? configuredRuleData = this.GetConfiguredRuleData(displayName, ruleData);
            if (configuredRuleData is null)
                continue;

            object? rule = Activator.CreateInstance(ruleType, nonPublic: true);
            if (rule is null)
                continue;

            foreach ((string propertyName, object? value) in configuredRuleData)
                TrySetMember(rule, propertyName, value);

            foreach (string key in keys)
            {
                this.TrackOriginalUtilityGridRule(objectRules, key);
                objectRules[key] = rule;
                added++;
            }
        }

        if (added == 0 && this.utilityGridEntries.Count == 0)
            this.Monitor.Log("No Industrialization Utility Grid rules were registered.", LogLevel.Warn);
    }

    private void RestoreManagedUtilityGridRules(IDictionary objectRules)
    {
        foreach (string key in this.managedUtilityGridRuleKeys)
        {
            if (this.originalUtilityGridRules.TryGetValue(key, out object? original) && original is not null)
                objectRules[key] = original;
            else
                objectRules.Remove(key);
        }
        this.managedUtilityGridRuleKeys.Clear();
    }

    private void TrackOriginalUtilityGridRule(IDictionary objectRules, string key)
    {
        if (!this.originalUtilityGridRules.ContainsKey(key))
            this.originalUtilityGridRules[key] = objectRules.Contains(key) ? objectRules[key] : null;
        this.managedUtilityGridRuleKeys.Add(key);
    }

    private IEnumerable<string> GetUtilityGridRuleKeys(string displayName)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            yield return displayName;

        Dictionary<string, object?>? bigCraftable = this.GetBigCraftableByDisplayName(displayName);
        if (bigCraftable is null)
            yield break;

        string? itemId = Convert.ToString(bigCraftable.GetValueOrDefault("Name"), CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(itemId))
            yield break;

        yield return itemId;
        yield return "(BC)" + itemId;
    }

    private Dictionary<string, object?>? GetBigCraftableByDisplayName(string displayName)
    {
        foreach (object? rawEntry in this.bigCraftableEntries.Values)
        {
            if (rawEntry is not Dictionary<string, object?> entry)
                continue;

            string? entryDisplayName = Convert.ToString(entry.GetValueOrDefault("DisplayName"), CultureInfo.InvariantCulture);
            if (string.Equals(entryDisplayName, displayName, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }

    private static string FormatUtilityGridTooltip(Dictionary<string, object?> rule)
    {
        List<string> parts = new();
        AddStorageAmount(parts, "water", GetRuleFloat(rule, "waterChargeCapacity"), GetRuleFloat(rule, "waterDischargeRate"));
        AddStorageAmount(parts, "power", GetRuleFloat(rule, "powerChargeCapacity"), GetRuleFloat(rule, "powerDischargeRate"));
        AddUtilityAmount(parts, "water", GetRuleFloat(rule, "water"));
        AddUtilityAmount(parts, "power", GetRuleFloat(rule, "power"));
        return string.Join(" ", parts);
    }

    private static string RemoveUtilityGridTooltip(string description)
    {
        if (string.IsNullOrWhiteSpace(description))
            return string.Empty;

        string oldPrefixPattern = @"\s*Utility" + " Grid:" + @"\s*";
        description = Regex.Replace(
            description,
            oldPrefixPattern + @"(?:consumes|produces)\s+[0-9.]+\s+(?:energy|water|power)(?:;\s*(?:consumes|produces)\s+[0-9.]+\s+(?:energy|water|power))*\.?$",
            string.Empty,
            RegexOptions.IgnoreCase);

        description = Regex.Replace(
            description,
            @"(?:\s*(?:Consumes|Produces)\s+[0-9.]+\s+(?:water|power)\.)+$",
            string.Empty,
            RegexOptions.IgnoreCase);

        description = Regex.Replace(
            description,
            @"(?:\s*Stores\s+[0-9.]+\s+(?:water|power)\.\s*Outputs\s+[0-9.]+\s+(?:water|power)\.)+$",
            string.Empty,
            RegexOptions.IgnoreCase);

        return description.TrimEnd();
    }


    private static string EnsureSentenceTerminator(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        char last = text[^1];
        return last is '.' or '!' or '?' ? text : text + ".";
    }

    private static void AddStorageAmount(List<string> parts, string label, float capacity, float output)
    {
        if (capacity > 0 && output > 0)
            parts.Add($"Stores {FormatRuleFloat(capacity)} {label}. Outputs {FormatRuleFloat(output)} {label}.");
    }

    private static void AddUtilityAmount(List<string> parts, string label, float amount)
    {
        if (amount > 0)
            parts.Add($"Produces {FormatRuleFloat(amount)} {label}.");
        else if (amount < 0)
            parts.Add($"Consumes {FormatRuleFloat(-amount)} {label}.");
    }

    private static float GetRuleFloat(Dictionary<string, object?> rule, string key)
    {
        if (!rule.TryGetValue(key, out object? value) || value is null)
            return 0f;

        return Convert.ToSingle(value, CultureInfo.InvariantCulture);
    }

    private static string FormatRuleFloat(float value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(
            this.ModManifest,
            () =>
            {
                this.Config = new ModConfig();
                this.ReloadConfigRules();
            },
            () =>
            {
                this.ReloadConfigRules();
                this.Helper.WriteConfig(this.Config);
            });

        gmcm.AddSectionTitle(
            this.ModManifest,
            () => "Utility Grid Machine Rules",
            () => "Produced amounts allow 1-2500. Consumed amounts allow 1-250. Only resources already used by each machine are shown.");

        foreach ((string displayName, object? rawRule) in this.utilityGridEntries.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (rawRule is not Dictionary<string, object?> rule)
                continue;

            string machineName = displayName;
            this.GetOrCreateMachineConfig(machineName, rule);
            this.AddMachineRuleToggle(gmcm, () => this.GetOrCreateMachineConfig(machineName, rule).Enabled, value => this.GetOrCreateMachineConfig(machineName, rule).Enabled = value, machineName, () => this.GetMachineRuleTooltip(machineName, rule));

            float power = GetRuleFloat(rule, "power");
            if (power > 0)
                this.AddProducedOption(gmcm, () => this.GetOrCreateMachineConfig(machineName, rule).PowerProduced ?? NormalizeProducedAmount((int)Math.Round(power)), value => this.GetOrCreateMachineConfig(machineName, rule).PowerProduced = NormalizeProducedAmount(value), "Power");
            else if (power < 0)
                this.AddConsumedOption(gmcm, () => this.GetOrCreateMachineConfig(machineName, rule).PowerConsumed ?? NormalizeConsumedAmount((int)Math.Round(-power)), value => this.GetOrCreateMachineConfig(machineName, rule).PowerConsumed = NormalizeConsumedAmount(value), "Power");

            float water = GetRuleFloat(rule, "water");
            if (water > 0)
                this.AddProducedOption(gmcm, () => this.GetOrCreateMachineConfig(machineName, rule).WaterProduced ?? NormalizeProducedAmount((int)Math.Round(water)), value => this.GetOrCreateMachineConfig(machineName, rule).WaterProduced = NormalizeProducedAmount(value), "Water");
            else if (water < 0)
                this.AddConsumedOption(gmcm, () => this.GetOrCreateMachineConfig(machineName, rule).WaterConsumed ?? NormalizeConsumedAmount((int)Math.Round(-water)), value => this.GetOrCreateMachineConfig(machineName, rule).WaterConsumed = NormalizeConsumedAmount(value), "Water");
        }
    }

    private string GetMachineRuleTooltip(string displayName, Dictionary<string, object?> rule)
    {
        MachineRuleConfig config = this.GetOrCreateMachineConfig(displayName, rule);
        string description = this.GetMachineBaseDescription(displayName);

        if (!config.Enabled)
            return description;

        Dictionary<string, object?>? configuredRule = this.GetConfiguredRuleData(displayName, rule, requireEnabled: false);
        string utilityText = configuredRule is null ? string.Empty : FormatUtilityGridTooltip(configuredRule);

        if (string.IsNullOrWhiteSpace(description))
            return utilityText;

        if (string.IsNullOrWhiteSpace(utilityText))
            return description;

        return EnsureSentenceTerminator(description.TrimEnd()) + " " + utilityText;
    }

    private string GetMachineBaseDescription(string displayName)
    {
        Dictionary<string, object?>? bigCraftable = this.GetBigCraftableByDisplayName(displayName);
        if (bigCraftable is null)
            return string.Empty;

        string description = Convert.ToString(bigCraftable.GetValueOrDefault("Description"), CultureInfo.InvariantCulture) ?? string.Empty;
        return RemoveUtilityGridTooltip(description);
    }

    private void AddMachineRuleToggle(IGenericModConfigMenuApi gmcm, Func<bool> getValue, Action<bool> setValue, string machineName, Func<string>? tooltip = null)
    {
        gmcm.AddBoolOption(
            this.ModManifest,
            getValue,
            setValue,
            () => machineName,
            tooltip ?? (() => "Turn this off to exclude this machine from Utility Grid management."));
    }

    private void AddProducedOption(IGenericModConfigMenuApi gmcm, Func<int> getValue, Action<int> setValue, string resourceName)
    {
        gmcm.AddTextOption(
            this.ModManifest,
            () => FormatAmountText(getValue()),
            value => setValue(ParseProducedAmountText(value, getValue())),
            () => $"  {resourceName.ToLowerInvariant()} produced",
            () => $"Amount of {resourceName.ToLowerInvariant()} this machine adds to the grid. Type a value from {MinProducedAmount} to {MaxProducedAmount}.");
    }

    private void AddConsumedOption(IGenericModConfigMenuApi gmcm, Func<int> getValue, Action<int> setValue, string resourceName)
    {
        gmcm.AddTextOption(
            this.ModManifest,
            () => FormatAmountText(getValue()),
            value => setValue(ParseConsumedAmountText(value, getValue())),
            () => $"  {resourceName.ToLowerInvariant()} consumed",
            () => $"Amount of {resourceName.ToLowerInvariant()} this machine removes from the grid. Type a value from {MinConsumedAmount} to {MaxConsumedAmount}.");
    }

    private static string FormatAmountText(int value)
    {
        return value.ToString(CultureInfo.InvariantCulture);
    }

    private static int ParseProducedAmountText(string value, int fallback)
    {
        return NormalizeProducedAmount(ParseAmountText(value, fallback));
    }

    private static int ParseConsumedAmountText(string value, int fallback)
    {
        return NormalizeConsumedAmount(ParseAmountText(value, fallback));
    }

    private static int ParseAmountText(string value, int fallback)
    {
        return int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? parsed
            : fallback;
    }

    private void ReloadConfigRules()
    {
        this.EnsureConfigDefaults();
        this.NormalizeConfig();
        this.ApplyUtilityGridTooltips();
        this.RegisterUtilityGridRules();
        this.Helper.GameContent.InvalidateCache("Data/BigCraftables");
    }

    private void EnsureConfigDefaults()
    {
        this.Config.MachineRules ??= new Dictionary<string, MachineRuleConfig>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, MachineRuleConfig> rules = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string displayName, object? rawRule) in this.utilityGridEntries)
        {
            if (rawRule is not Dictionary<string, object?> rule)
                continue;

            MachineRuleConfig config = this.Config.MachineRules.TryGetValue(displayName, out MachineRuleConfig? existing)
                ? existing
                : new MachineRuleConfig();

            float power = GetRuleFloat(rule, "power");
            if (power > 0)
                config.PowerProduced ??= NormalizeProducedAmount((int)Math.Round(power));
            else if (power < 0)
                config.PowerConsumed ??= NormalizeConsumedAmount((int)Math.Round(-power));

            float water = GetRuleFloat(rule, "water");
            if (water > 0)
                config.WaterProduced ??= NormalizeProducedAmount((int)Math.Round(water));
            else if (water < 0)
                config.WaterConsumed ??= NormalizeConsumedAmount((int)Math.Round(-water));

            rules[displayName] = config;
        }
        this.Config.MachineRules = rules;
    }

    private void NormalizeConfig()
    {
        foreach ((string displayName, object? rawRule) in this.utilityGridEntries)
        {
            if (rawRule is not Dictionary<string, object?> rule)
                continue;

            MachineRuleConfig config = this.GetOrCreateMachineConfig(displayName, rule);
            float power = GetRuleFloat(rule, "power");
            if (power > 0)
            {
                config.PowerProduced = NormalizeProducedAmount(config.PowerProduced ?? (int)Math.Round(power));
                config.PowerConsumed = null;
            }
            else if (power < 0)
            {
                config.PowerConsumed = NormalizeConsumedAmount(config.PowerConsumed ?? (int)Math.Round(-power));
                config.PowerProduced = null;
            }
            else
            {
                config.PowerProduced = null;
                config.PowerConsumed = null;
            }

            float water = GetRuleFloat(rule, "water");
            if (water > 0)
            {
                config.WaterProduced = NormalizeProducedAmount(config.WaterProduced ?? (int)Math.Round(water));
                config.WaterConsumed = null;
            }
            else if (water < 0)
            {
                config.WaterConsumed = NormalizeConsumedAmount(config.WaterConsumed ?? (int)Math.Round(-water));
                config.WaterProduced = null;
            }
            else
            {
                config.WaterProduced = null;
                config.WaterConsumed = null;
            }
        }
    }

    private MachineRuleConfig GetOrCreateMachineConfig(string displayName, Dictionary<string, object?> rule)
    {
        if (!this.Config.MachineRules.TryGetValue(displayName, out MachineRuleConfig? config))
        {
            config = new MachineRuleConfig();
            this.Config.MachineRules[displayName] = config;
        }

        float power = GetRuleFloat(rule, "power");
        if (power > 0)
            config.PowerProduced ??= NormalizeProducedAmount((int)Math.Round(power));
        else if (power < 0)
            config.PowerConsumed ??= NormalizeConsumedAmount((int)Math.Round(-power));

        float water = GetRuleFloat(rule, "water");
        if (water > 0)
            config.WaterProduced ??= NormalizeProducedAmount((int)Math.Round(water));
        else if (water < 0)
            config.WaterConsumed ??= NormalizeConsumedAmount((int)Math.Round(-water));

        return config;
    }

    private Dictionary<string, object?>? GetConfiguredRuleData(string displayName, Dictionary<string, object?> ruleData, bool requireEnabled = true)
    {
        MachineRuleConfig config = this.GetOrCreateMachineConfig(displayName, ruleData);
        if (requireEnabled && !config.Enabled)
            return null;

        Dictionary<string, object?> result = new(ruleData, StringComparer.OrdinalIgnoreCase);
        float power = GetRuleFloat(ruleData, "power");
        if (power > 0)
            result["power"] = NormalizeProducedAmount(config.PowerProduced ?? (int)Math.Round(power));
        else if (power < 0)
            result["power"] = -NormalizeConsumedAmount(config.PowerConsumed ?? (int)Math.Round(-power));

        float water = GetRuleFloat(ruleData, "water");
        if (water > 0)
            result["water"] = NormalizeProducedAmount(config.WaterProduced ?? (int)Math.Round(water));
        else if (water < 0)
            result["water"] = -NormalizeConsumedAmount(config.WaterConsumed ?? (int)Math.Round(-water));

        return result;
    }

    private static int NormalizeProducedAmount(int value)
    {
        return Math.Clamp(value, MinProducedAmount, MaxProducedAmount);
    }

    private static int NormalizeConsumedAmount(int value)
    {
        return Math.Clamp(value, MinConsumedAmount, MaxConsumedAmount);
    }

    private Dictionary<string, object?> ReadDictionary(string relativePath)
    {
        string path = Path.Combine(this.Helper.DirectoryPath, relativePath);
        if (!File.Exists(path))
            return new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        using FileStream stream = File.OpenRead(path);
        using JsonDocument document = JsonDocument.Parse(stream);
        object? converted = ConvertJson(document.RootElement);
        return converted as Dictionary<string, object?> ?? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
    }

    private void ApplyMachineEntries(object? assetData)
    {
        if (assetData is not IDictionary dictionary)
            return;

        Type valueType = GetDictionaryValueType(dictionary.GetType()) ?? typeof(object);
        foreach ((string key, object? value) in this.machineEntries)
        {
            string[] keys = ExpandMachineKeys(key).ToArray();
            if (this.IsUtilityGridMachineDisabled(keys))
                continue;

            object? converted = ConvertToTargetType(value, valueType);
            if (dictionary.Contains(key) && dictionary[key] is not null && converted is not null)
                MergeMachineEntry(dictionary[key]!, converted);
            else
                dictionary[key] = converted;
        }
    }

    private static void ApplyEntries(object? assetData, IReadOnlyDictionary<string, object?> entries)
    {
        if (assetData is not IDictionary dictionary)
            return;

        Type valueType = GetDictionaryValueType(dictionary.GetType()) ?? typeof(object);
        foreach ((string key, object? value) in entries)
            dictionary[key] = ConvertToTargetType(value, valueType);
    }

    private static void MergeMachineEntry(object existingEntry, object incomingEntry)
    {
        AppendListMemberById(existingEntry, incomingEntry, "OutputRules");
        AppendListMemberById(existingEntry, incomingEntry, "LoadEffects");
        AppendListMemberById(existingEntry, incomingEntry, "WorkingEffects");
    }

    private static void AppendListMemberById(object existingEntry, object incomingEntry, string memberName)
    {
        if (GetMemberValue(existingEntry, memberName) is not IList existingList || GetMemberValue(incomingEntry, memberName) is not IEnumerable incomingList)
            return;

        HashSet<string> existingIds = new(StringComparer.OrdinalIgnoreCase);
        foreach (object? entry in existingList)
        {
            string? id = Convert.ToString(entry is null ? null : GetMemberValue(entry, "Id"), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(id))
                existingIds.Add(id);
        }

        foreach (object? entry in incomingList)
        {
            if (entry is null)
                continue;

            string? id = Convert.ToString(GetMemberValue(entry, "Id"), CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(id) && !existingIds.Add(id))
                continue;

            existingList.Add(entry);
        }
    }

    private bool IsUtilityGridMachineDisabled(IEnumerable<string> keys)
    {
        MethodInfo? method = GetUtilityGridDisabledMethod();
        if (method is null)
            return false;

        foreach (string key in keys)
        {
            foreach (string expanded in ExpandMachineKeys(key))
            {
                if (method.Invoke(null, new object?[] { expanded }) is bool disabled && disabled)
                    return true;
            }
        }

        return false;
    }

    private static MethodInfo? GetUtilityGridDisabledMethod()
    {
        Assembly? utilityGridAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "UtilityGridRedux", StringComparison.OrdinalIgnoreCase));
        Type? modEntryType = utilityGridAssembly?.GetType("ThaleTheGreat.UtilityGridRedux.ModEntry");
        return modEntryType?.GetMethod("IsObjectRuleKeyDisabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
    }

    private static IEnumerable<string> ExpandMachineKeys(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            yield break;

        string trimmed = key.Trim();
        yield return trimmed;

        int qualifierEnd = trimmed.IndexOf(')');
        if (trimmed.StartsWith("(", StringComparison.Ordinal) && qualifierEnd >= 0 && qualifierEnd + 1 < trimmed.Length)
            yield return trimmed[(qualifierEnd + 1)..];
    }

    private void ApplyShopEntries(object? assetData)
    {
        if (assetData is not IDictionary shops)
            return;

        foreach ((string shopId, object? rawItems) in this.shopEntries)
        {
            if (!shops.Contains(shopId) || shops[shopId] is null || rawItems is not List<object?> itemsToAdd)
                continue;

            object shop = shops[shopId]!;
            object? items = GetMemberValue(shop, "Items");
            if (items is not IList list)
                continue;

            Type itemType = GetListElementType(list.GetType()) ?? typeof(object);
            foreach (object? item in itemsToAdd)
                list.Add(ConvertToTargetType(item, itemType));
        }
    }

    internal static object? ConvertToTargetType(object? value, Type targetType)
    {
        if (value is null)
            return null;

        Type actualType = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (actualType == typeof(object))
            return value;
        if (actualType.IsInstanceOfType(value))
            return value;
        if (actualType == typeof(string))
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        if (actualType.IsEnum && value is string enumText)
            return Enum.Parse(actualType, enumText, ignoreCase: true);
        if (actualType == typeof(bool) || actualType == typeof(int) || actualType == typeof(float) || actualType == typeof(double) || actualType == typeof(decimal))
            return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);

        if (value is List<object?> listValue)
        {
            if (actualType.IsArray)
            {
                Type arrayElementType = actualType.GetElementType() ?? typeof(object);
                Array array = Array.CreateInstance(arrayElementType, listValue.Count);
                for (int i = 0; i < listValue.Count; i++)
                    array.SetValue(ConvertToTargetType(listValue[i], arrayElementType), i);
                return array;
            }

            Type elementType = GetListElementType(actualType) ?? typeof(object);
            IList list = (IList)(Activator.CreateInstance(typeof(List<>).MakeGenericType(elementType)) ?? new ArrayList());
            foreach (object? entry in listValue)
                list.Add(ConvertToTargetType(entry, elementType));
            return list;
        }

        if (value is Dictionary<string, object?> dictionaryValue)
        {
            Type? dictionaryValueType = GetDictionaryValueType(actualType);
            if (dictionaryValueType is not null)
            {
                Type dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), dictionaryValueType);
                IDictionary dictionary = (IDictionary)(Activator.CreateInstance(dictionaryType) ?? new Hashtable());
                foreach ((string key, object? entry) in dictionaryValue)
                    dictionary[key] = ConvertToTargetType(entry, dictionaryValueType);
                return dictionary;
            }

            object? instance = Activator.CreateInstance(actualType);
            if (instance is null)
                return value;

            foreach ((string name, object? entry) in dictionaryValue)
                TrySetMember(instance, name, entry);
            return instance;
        }

        return Convert.ChangeType(value, actualType, CultureInfo.InvariantCulture);
    }

    private static object? ConvertJson(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJson(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJson).ToList(),
            JsonValueKind.String => element.GetString(),
            JsonValueKind.Number => element.TryGetInt32(out int i) ? i : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static void TrySetMember(object instance, string name, object? value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        Type type = instance.GetType();

        PropertyInfo? property = type.GetProperty(name, flags);
        if (property is not null && property.CanWrite)
        {
            property.SetValue(instance, ConvertToTargetType(value, property.PropertyType));
            return;
        }

        FieldInfo? field = type.GetField(name, flags);
        if (field is not null)
            field.SetValue(instance, ConvertToTargetType(value, field.FieldType));
    }

    private static object? GetMemberValue(object instance, string name)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.IgnoreCase;
        Type type = instance.GetType();
        return type.GetProperty(name, flags)?.GetValue(instance) ?? type.GetField(name, flags)?.GetValue(instance);
    }

    private static Type? GetDictionaryValueType(Type type)
    {
        IEnumerable<Type> candidates = type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : type.GetInterfaces().Prepend(type);
        foreach (Type candidate in candidates)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IDictionary<,>) && candidate.GetGenericArguments()[0] == typeof(string))
                return candidate.GetGenericArguments()[1];
        }
        return null;
    }

    private static Type? GetListElementType(Type type)
    {
        if (type.IsArray)
            return type.GetElementType();
        IEnumerable<Type> candidates = type.IsInterface ? new[] { type }.Concat(type.GetInterfaces()) : type.GetInterfaces().Prepend(type);
        foreach (Type candidate in candidates)
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IList<>))
                return candidate.GetGenericArguments()[0];
        }
        return null;
    }
}
