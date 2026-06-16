using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Machines;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.IndustrializationForUtilityGridRedux;

public static class MachineLogic
{
    private static readonly Dictionary<string, MachineOutputRule> Rules = new(StringComparer.OrdinalIgnoreCase);
    private static IMonitor? Monitor;

    public static void Initialize(string modDirectory, IMonitor monitor)
    {
        Monitor = monitor;
        Rules.Clear();

        string path = Path.Combine(modDirectory, "assets", "Data", "machine-output-rules.json");
        if (!File.Exists(path))
            return;

        Dictionary<string, MachineOutputRule>? data = JsonSerializer.Deserialize<Dictionary<string, MachineOutputRule>>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (data is null)
            return;

        foreach ((string key, MachineOutputRule value) in data)
            Rules[key] = value;
    }

    public static Item? GetOutput(SObject machine, Item? inputItem, bool probe, MachineItemOutput outputData, Farmer player, out int? overrideMinutesUntilReady)
    {
        overrideMinutesUntilReady = null;

        if (outputData.CustomData is null || !outputData.CustomData.TryGetValue("RuleId", out string? ruleId) || ruleId is null)
            return null;

        if (!Rules.TryGetValue(ruleId, out MachineOutputRule? rule))
            return null;

        MachineOutputChoice? choice = ChooseOutput(rule, inputItem);
        if (choice is null || string.IsNullOrWhiteSpace(choice.OutputIdentifier))
            return null;

        int stack = choice.OutputStack;
        if (choice.OutputMaxStack > stack)
            stack = Game1.random.Next(stack, choice.OutputMaxStack + 1);

        int quality = choice.KeepInputQuality && inputItem is not null ? GetQuality(inputItem) : choice.OutputQuality;
        Item? item = ItemRegistry.Create(choice.OutputIdentifier, Math.Max(1, stack), quality, allowNull: true);
        if (item is null)
        {
            if (!probe)
                Monitor?.Log($"Could not create machine output '{choice.OutputIdentifier}'.", LogLevel.Warn);
            return null;
        }

        if (choice.MinutesUntilReady.HasValue)
            overrideMinutesUntilReady = choice.MinutesUntilReady.Value;

        if (inputItem is not null)
        {
            ApplyPreserveData(item, inputItem, choice.PreserveType);
            ApplyPriceData(item, inputItem, choice.OutputPriceMultiplier, choice.OutputPriceIncrement);
        }

        return item;
    }

    private static MachineOutputChoice? ChooseOutput(MachineOutputRule rule, Item? inputItem)
    {
        List<MachineOutputChoice> choices = new();

        foreach (MachineOutputChoice extra in rule.AdditionalOutputs)
        {
            MachineOutputChoice? choice = CreateChoice(rule, extra, inputItem);
            if (choice is not null)
                choices.Add(choice);
        }

        if (choices.Count > 0)
            return ChooseWeightedOutput(choices);

        if (string.IsNullOrWhiteSpace(rule.OutputIdentifier))
            return null;

        return new MachineOutputChoice
        {
            OutputIdentifier = rule.OutputIdentifier,
            OutputStack = rule.OutputStack,
            OutputMaxStack = rule.OutputMaxStack,
            OutputQuality = rule.OutputQuality,
            KeepInputQuality = rule.KeepInputQuality,
            PreserveType = rule.PreserveType,
            OutputPriceMultiplier = rule.OutputPriceMultiplier,
            OutputPriceIncrement = rule.OutputPriceIncrement
        };
    }

    private static MachineOutputChoice? CreateChoice(MachineOutputRule rule, MachineOutputChoice extra, Item? inputItem)
    {
        if (inputItem is null)
        {
            if (extra.RequiredInputQuality is { Count: > 0 } || extra.RequiredInputParentIdentifier is { Count: > 0 })
                return null;

            return extra.InheritFrom(rule);
        }

        if (extra.RequiredInputQuality is { Count: > 0 } && !extra.RequiredInputQuality.Contains(GetQuality(inputItem)))
            return null;

        if (extra.RequiredInputParentIdentifier is { Count: > 0 })
        {
            string inputId = GetPreservedParentId(inputItem) ?? inputItem.ItemId;
            if (!extra.RequiredInputParentIdentifier.Contains(inputId, StringComparer.OrdinalIgnoreCase))
                return null;
        }

        MachineOutputChoice choice = extra.InheritFrom(rule);

        QualitySpecificOutput? qualityOutput = GetQuality(inputItem) switch
        {
            1 => extra.SilverQualityInput,
            2 => extra.GoldQualityInput,
            4 => extra.IridiumQualityInput,
            _ => null
        };

        if (qualityOutput is not null)
        {
            choice.OutputProbability = qualityOutput.Probability ?? choice.OutputProbability;
            choice.OutputStack = qualityOutput.OutputStack ?? choice.OutputStack;
            choice.OutputMaxStack = qualityOutput.OutputMaxStack ?? choice.OutputMaxStack;
        }

        return choice;
    }

    private static MachineOutputChoice ChooseWeightedOutput(List<MachineOutputChoice> choices)
    {
        bool hasExplicitWeight = choices.Any(choice => choice.OutputProbability.HasValue);
        if (!hasExplicitWeight)
            return choices[Game1.random.Next(choices.Count)];

        double totalWeight = choices.Sum(choice => Math.Max(0d, choice.OutputProbability ?? 1d));
        if (totalWeight <= 0d)
            return choices[Game1.random.Next(choices.Count)];

        double roll = Game1.random.NextDouble() * totalWeight;
        foreach (MachineOutputChoice choice in choices)
        {
            roll -= Math.Max(0d, choice.OutputProbability ?? 1d);
            if (roll <= 0d)
                return choice;
        }

        return choices[^1];
    }

    private static int GetQuality(Item item)
    {
        PropertyInfo? property = item.GetType().GetProperty("Quality", BindingFlags.Instance | BindingFlags.Public);
        if (property?.GetValue(item) is int quality)
            return quality;
        return item is SObject obj ? obj.Quality : 0;
    }

    private static string? GetPreservedParentId(Item item)
    {
        if (item is not SObject obj)
            return null;

        object? field = obj.GetType().GetField("preservedParentSheetIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj);
        object? value = field?.GetType().GetProperty("Value")?.GetValue(field);
        return value?.ToString();
    }

    private static void ApplyPreserveData(Item item, Item inputItem, string? preserveType)
    {
        if (string.IsNullOrWhiteSpace(preserveType) || item is not SObject obj)
            return;

        FieldInfo? preserveField = obj.GetType().GetField("preserve", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? preserveNetField = preserveField?.GetValue(obj);
        PropertyInfo? valueProperty = preserveNetField?.GetType().GetProperty("Value");
        if (valueProperty is not null)
        {
            Type enumType = valueProperty.PropertyType;
            if (enumType.IsEnum)
                valueProperty.SetValue(preserveNetField, Enum.Parse(enumType, preserveType, ignoreCase: true));
        }

        FieldInfo? parentField = obj.GetType().GetField("preservedParentSheetIndex", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? parentNetField = parentField?.GetValue(obj);
        PropertyInfo? parentValueProperty = parentNetField?.GetType().GetProperty("Value");
        parentValueProperty?.SetValue(parentNetField, GetPreservedParentId(inputItem) ?? inputItem.ItemId);
    }

    private static void ApplyPriceData(Item item, Item inputItem, double multiplier, int increment)
    {
        if (item is not SObject obj || multiplier == 1d && increment == 0)
            return;

        int inputPrice = inputItem is SObject inputObject ? inputObject.Price : Math.Max(0, inputItem.salePrice());
        int price = Math.Max(0, (int)Math.Round(inputPrice * multiplier + increment));

        PropertyInfo? priceProperty = obj.GetType().GetProperty("Price", BindingFlags.Instance | BindingFlags.Public);
        if (priceProperty?.CanWrite == true)
        {
            priceProperty.SetValue(obj, price);
            return;
        }

        FieldInfo? priceField = obj.GetType().GetField("price", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        object? priceNetField = priceField?.GetValue(obj);
        priceNetField?.GetType().GetProperty("Value")?.SetValue(priceNetField, price);
    }
}

public sealed class MachineOutputRule
{
    public string? OutputIdentifier { get; set; }
    public int OutputStack { get; set; } = 1;
    public int OutputMaxStack { get; set; } = 1;
    public int OutputQuality { get; set; }
    public bool KeepInputQuality { get; set; }
    public string? PreserveType { get; set; }
    public int OutputPriceIncrement { get; set; }
    public double OutputPriceMultiplier { get; set; } = 1d;
    public List<MachineOutputChoice> AdditionalOutputs { get; set; } = new();
}

public sealed class MachineOutputChoice
{
    public string? OutputIdentifier { get; set; }
    public int OutputStack { get; set; } = 1;
    public int OutputMaxStack { get; set; } = 1;
    public int OutputQuality { get; set; }
    public bool KeepInputQuality { get; set; }
    public string? PreserveType { get; set; }
    public int OutputPriceIncrement { get; set; }
    public double OutputPriceMultiplier { get; set; } = 1d;
    public double? OutputProbability { get; set; }
    public int? MinutesUntilReady { get; set; }
    public List<int> RequiredInputQuality { get; set; } = new();
    public List<string> RequiredInputParentIdentifier { get; set; } = new();
    public QualitySpecificOutput? SilverQualityInput { get; set; }
    public QualitySpecificOutput? GoldQualityInput { get; set; }
    public QualitySpecificOutput? IridiumQualityInput { get; set; }

    public MachineOutputChoice InheritFrom(MachineOutputRule rule)
    {
        return new MachineOutputChoice
        {
            OutputIdentifier = this.OutputIdentifier,
            OutputStack = this.OutputStack > 0 ? this.OutputStack : rule.OutputStack,
            OutputMaxStack = this.OutputMaxStack > 0 ? this.OutputMaxStack : Math.Max(this.OutputStack, rule.OutputMaxStack),
            OutputQuality = this.OutputQuality,
            KeepInputQuality = this.KeepInputQuality || rule.KeepInputQuality,
            PreserveType = this.PreserveType ?? rule.PreserveType,
            OutputPriceIncrement = this.OutputPriceIncrement != 0 ? this.OutputPriceIncrement : rule.OutputPriceIncrement,
            OutputPriceMultiplier = this.OutputPriceMultiplier != 1d ? this.OutputPriceMultiplier : rule.OutputPriceMultiplier,
            OutputProbability = this.OutputProbability,
            MinutesUntilReady = this.MinutesUntilReady,
            RequiredInputQuality = this.RequiredInputQuality,
            RequiredInputParentIdentifier = this.RequiredInputParentIdentifier,
            SilverQualityInput = this.SilverQualityInput,
            GoldQualityInput = this.GoldQualityInput,
            IridiumQualityInput = this.IridiumQualityInput
        };
    }
}

public sealed class QualitySpecificOutput
{
    public double? Probability { get; set; }
    public int? OutputStack { get; set; }
    public int? OutputMaxStack { get; set; }
}
