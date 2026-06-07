using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Tools;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

public sealed class ModEntry : Mod
{
    internal static ModEntry Instance { get; private set; } = null!;
    internal static ModConfig Config { get; private set; } = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        NormalizeConfig(helper);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;

        if (Config.EnableDebugLogging)
        {
            helper.ConsoleCommands.Add("toolandsprinklerupgrades_setlevel", "Set a held axe, pickaxe, hoe, or watering can to a Tool and Sprinkler Upgrades upgrade level. Usage: toolandsprinklerupgrades_setlevel <5|6|7>", SetHeldToolLevelCommand);
            helper.ConsoleCommands.Add("toolandsprinklerupgrades_givebars", "Add Cobalt, Prismatic, and Radioactive Bars for testing.", GiveBarsCommand);
        }

        Harmony harmony = new(ModManifest.UniqueID);
        harmony.PatchAll(Assembly.GetExecutingAssembly());
        PatchToolStaminaUse(harmony);
        PatchCustomSprinklerRecognition(harmony);
    }


    private static void NormalizeConfig(IModHelper helper)
    {
        bool changed = false;

        if (Config.CobaltSprinklerRange <= 0)
        {
            Config.CobaltSprinklerRange = 3;
            changed = true;
        }

        if (Config.PrismaticSprinklerRange == 4 || Config.PrismaticSprinklerRange <= Config.CobaltSprinklerRange)
        {
            Config.PrismaticSprinklerRange = 5;
            changed = true;
        }

        if (Config.RadioactiveSprinklerRange == 5 || Config.RadioactiveSprinklerRange == 6 || Config.RadioactiveSprinklerRange <= Config.PrismaticSprinklerRange)
        {
            Config.RadioactiveSprinklerRange = 7;
            changed = true;
        }

        if (changed)
            helper.WriteConfig(Config);
    }

    private void PatchCustomSprinklerRecognition(Harmony harmony)
    {
        MethodInfo? isSprinkler = AccessTools.Method(typeof(SObject), "IsSprinkler", Type.EmptyTypes);
        MethodInfo? postfix = AccessTools.Method(typeof(CustomSprinklerRecognitionPatch), nameof(CustomSprinklerRecognitionPatch.AfterIsSprinkler));

        if (isSprinkler == null || postfix == null)
        {
            Monitor.Log("Custom sprinkler recognition patch was skipped because the expected Stardew Valley 1.6 IsSprinkler method was not found.", LogLevel.Warn);
            return;
        }

        harmony.Patch(isSprinkler, postfix: new HarmonyMethod(postfix));
    }

    private void PatchToolStaminaUse(Harmony harmony)
    {
        PatchToolStaminaUse<Axe>(harmony, nameof(ToolStaminaPatch.BeforeUseAxe), nameof(ToolStaminaPatch.AfterUseAxe));
        PatchToolStaminaUse<Pickaxe>(harmony, nameof(ToolStaminaPatch.BeforeUsePickaxe), nameof(ToolStaminaPatch.AfterUsePickaxe));
        PatchToolStaminaUse<Hoe>(harmony, nameof(ToolStaminaPatch.BeforeUseHoe), nameof(ToolStaminaPatch.AfterUseHoe));
        PatchToolStaminaUse<WateringCan>(harmony, nameof(ToolStaminaPatch.BeforeUseWateringCan), nameof(ToolStaminaPatch.AfterUseWateringCan));
    }

    private void PatchToolStaminaUse<TTool>(Harmony harmony, string prefixName, string postfixName) where TTool : Tool
    {
        MethodInfo? doFunction = AccessTools.Method(
            typeof(TTool),
            nameof(Tool.DoFunction),
            new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(int), typeof(Farmer) }
        );

        MethodInfo? prefix = AccessTools.Method(typeof(ToolStaminaPatch), prefixName);
        MethodInfo? postfix = AccessTools.Method(typeof(ToolStaminaPatch), postfixName);

        if (doFunction == null || prefix == null || postfix == null)
        {
            Monitor.Log($"Custom tool stamina patch was skipped for {typeof(TTool).Name} because the expected Stardew Valley 1.6 method was not found.", LogLevel.Warn);
            return;
        }

        harmony.Patch(doFunction, prefix: new HarmonyMethod(prefix), postfix: new HarmonyMethod(postfix));
    }


    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(Constants.CobaltTextureAsset))
        {
            e.LoadFromModFile<Texture2D>("assets/cobalt.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo(Constants.PrismaticTextureAsset))
        {
            e.LoadFromModFile<Texture2D>("assets/prismatic.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo(Constants.RadioactiveTextureAsset))
        {
            e.LoadFromModFile<Texture2D>("assets/radioactive.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ObjectData> data = asset.AsDictionary<string, ObjectData>().Data;
                data[Constants.CobaltBarId] = CreateObjectData("Cobalt Bar", "A bar of refined cobalt.", "Basic", -15, 500, Constants.CobaltTextureAsset, Constants.CobaltBarSpriteIndex);
                data[Constants.CobaltSprinklerId] = CreateObjectData("Cobalt Sprinkler", "Waters the 48 surrounding tiles every morning.", "Crafting", -9, 500, Constants.CobaltTextureAsset, Constants.CobaltSprinklerSpriteIndex, new List<string> { "sprinkler" });
                data[Constants.PrismaticBarId] = CreateObjectData("Prismatic Bar", "A bar pulsing with prismatic energy.", "Basic", -15, 1000, Constants.PrismaticTextureAsset, Constants.PrismaticBarSpriteIndex);
                data[Constants.PrismaticSprinklerId] = CreateObjectData("Prismatic Sprinkler", "Waters the 120 surrounding tiles every morning.", "Crafting", -9, 1000, Constants.PrismaticTextureAsset, Constants.PrismaticSprinklerSpriteIndex, new List<string> { "sprinkler" });
                data[Constants.RadioactiveSprinklerId] = CreateObjectData("Radioactive Sprinkler", "Waters the 224 surrounding tiles every morning.", "Crafting", -9, 2500, Constants.RadioactiveTextureAsset, Constants.RadioactiveSprinklerSpriteIndex, new List<string> { "sprinkler" });
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ToolData> data = asset.AsDictionary<string, ToolData>().Data;
                AddToolTierData(data);
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data["Cobalt Bar"] = $"337 2 338 5 382 5/Home/{Constants.CobaltBarId} 1/false/null";
                data["Prismatic Bar"] = $"{Constants.CobaltBarId} 2 74 1/Home/{Constants.PrismaticBarId} 1/false/null";
                data["Cobalt Sprinkler"] = $"{Constants.CobaltBarId} 1 645 1/Home/{Constants.CobaltSprinklerId} 1/false/null";
                data["Prismatic Sprinkler"] = $"{Constants.PrismaticBarId} 1 {Constants.CobaltSprinklerId} 1/Home/{Constants.PrismaticSprinklerId} 1/false/null";
                data["Radioactive Sprinkler"] = $"{Constants.VanillaRadioactiveBarId} 1 {Constants.PrismaticSprinklerId} 1/Home/{Constants.RadioactiveSprinklerId} 1/false/null";
            });
        }
    }

    private static ObjectData CreateObjectData(string name, string description, string type, int category, int price, string textureAsset, int spriteIndex, List<string>? contextTags = null)
    {
        return new ObjectData
        {
            Name = name,
            DisplayName = name,
            Description = description,
            Type = type,
            Category = category,
            Price = price,
            Texture = textureAsset,
            SpriteIndex = spriteIndex,
            ContextTags = contextTags
        };
    }

    private static void AddToolTierData(IDictionary<string, ToolData> data)
    {
        AddTool(data, "IridiumAxe", Constants.CobaltAxeId, "Cobalt Axe", "Used to chop wood.", Constants.CobaltLevel, "(T)IridiumAxe", Constants.CobaltBarId, Constants.CobaltTextureAsset, ModEntry.Config.CobaltUpgradeCost, ModEntry.Config.CobaltBarsRequired);
        AddTool(data, "IridiumPickaxe", Constants.CobaltPickaxeId, "Cobalt Pickaxe", "Used to break stones.", Constants.CobaltLevel, "(T)IridiumPickaxe", Constants.CobaltBarId, Constants.CobaltTextureAsset, ModEntry.Config.CobaltUpgradeCost, ModEntry.Config.CobaltBarsRequired);
        AddTool(data, "IridiumHoe", Constants.CobaltHoeId, "Cobalt Hoe", "Used to dig and till soil.", Constants.CobaltLevel, "(T)IridiumHoe", Constants.CobaltBarId, Constants.CobaltTextureAsset, ModEntry.Config.CobaltUpgradeCost, ModEntry.Config.CobaltBarsRequired);
        AddTool(data, "IridiumWateringCan", Constants.CobaltWateringCanId, "Cobalt Watering Can", "Used to water crops. It can be refilled at any water source.", Constants.CobaltLevel, "(T)IridiumWateringCan", Constants.CobaltBarId, Constants.CobaltTextureAsset, ModEntry.Config.CobaltUpgradeCost, ModEntry.Config.CobaltBarsRequired);

        AddTool(data, "IridiumAxe", Constants.PrismaticAxeId, "Prismatic Axe", "Used to chop wood.", Constants.PrismaticLevel, "(T)" + Constants.CobaltAxeId, Constants.PrismaticBarId, Constants.PrismaticTextureAsset, ModEntry.Config.PrismaticUpgradeCost, ModEntry.Config.PrismaticBarsRequired);
        AddTool(data, "IridiumPickaxe", Constants.PrismaticPickaxeId, "Prismatic Pickaxe", "Used to break stones.", Constants.PrismaticLevel, "(T)" + Constants.CobaltPickaxeId, Constants.PrismaticBarId, Constants.PrismaticTextureAsset, ModEntry.Config.PrismaticUpgradeCost, ModEntry.Config.PrismaticBarsRequired);
        AddTool(data, "IridiumHoe", Constants.PrismaticHoeId, "Prismatic Hoe", "Used to dig and till soil.", Constants.PrismaticLevel, "(T)" + Constants.CobaltHoeId, Constants.PrismaticBarId, Constants.PrismaticTextureAsset, ModEntry.Config.PrismaticUpgradeCost, ModEntry.Config.PrismaticBarsRequired);
        AddTool(data, "IridiumWateringCan", Constants.PrismaticWateringCanId, "Prismatic Watering Can", "Used to water crops. It can be refilled at any water source.", Constants.PrismaticLevel, "(T)" + Constants.CobaltWateringCanId, Constants.PrismaticBarId, Constants.PrismaticTextureAsset, ModEntry.Config.PrismaticUpgradeCost, ModEntry.Config.PrismaticBarsRequired);

        AddTool(data, "IridiumAxe", Constants.RadioactiveAxeId, "Radioactive Axe", "Used to chop wood.", Constants.RadioactiveLevel, "(T)" + Constants.PrismaticAxeId, Constants.VanillaRadioactiveBarId, Constants.RadioactiveTextureAsset, ModEntry.Config.RadioactiveUpgradeCost, ModEntry.Config.RadioactiveBarsRequired);
        AddTool(data, "IridiumPickaxe", Constants.RadioactivePickaxeId, "Radioactive Pickaxe", "Used to break stones.", Constants.RadioactiveLevel, "(T)" + Constants.PrismaticPickaxeId, Constants.VanillaRadioactiveBarId, Constants.RadioactiveTextureAsset, ModEntry.Config.RadioactiveUpgradeCost, ModEntry.Config.RadioactiveBarsRequired);
        AddTool(data, "IridiumHoe", Constants.RadioactiveHoeId, "Radioactive Hoe", "Used to dig and till soil.", Constants.RadioactiveLevel, "(T)" + Constants.PrismaticHoeId, Constants.VanillaRadioactiveBarId, Constants.RadioactiveTextureAsset, ModEntry.Config.RadioactiveUpgradeCost, ModEntry.Config.RadioactiveBarsRequired);
        AddTool(data, "IridiumWateringCan", Constants.RadioactiveWateringCanId, "Radioactive Watering Can", "Used to water crops. It can be refilled at any water source.", Constants.RadioactiveLevel, "(T)" + Constants.PrismaticWateringCanId, Constants.VanillaRadioactiveBarId, Constants.RadioactiveTextureAsset, ModEntry.Config.RadioactiveUpgradeCost, ModEntry.Config.RadioactiveBarsRequired);
    }


    private static int GetWorldSpriteIndexForToolId(string id, ToolData template)
    {
        if (id.EndsWith("Axe", StringComparison.Ordinal))
            return Constants.AxeSpriteIndex;
        if (id.EndsWith("Pickaxe", StringComparison.Ordinal))
            return Constants.PickaxeSpriteIndex;
        if (id.EndsWith("Hoe", StringComparison.Ordinal))
            return Constants.HoeSpriteIndex;
        if (id.EndsWith("WateringCan", StringComparison.Ordinal))
            return Constants.WateringCanSpriteIndex;

        return template.SpriteIndex;
    }

    private static int GetMenuSpriteIndexForToolId(string id, ToolData template)
    {
        if (id.EndsWith("Axe", StringComparison.Ordinal))
            return Constants.AxeMenuSpriteIndex;
        if (id.EndsWith("Pickaxe", StringComparison.Ordinal))
            return Constants.PickaxeMenuSpriteIndex;
        if (id.EndsWith("Hoe", StringComparison.Ordinal))
            return Constants.HoeMenuSpriteIndex;
        if (id.EndsWith("WateringCan", StringComparison.Ordinal))
            return Constants.WateringCanMenuSpriteIndex;

        return template.MenuSpriteIndex;
    }

    private static void AddTool(IDictionary<string, ToolData> data, string templateId, string id, string name, string description, int level, string requireToolId, string tradeItemId, string textureAsset, int price, int tradeAmount)
    {
        if (!data.TryGetValue(templateId, out ToolData? template))
            return;

        data[id] = new ToolData
        {
            ClassName = template.ClassName,
            Name = name,
            DisplayName = name,
            Description = description,
            Texture = textureAsset,
            SpriteIndex = GetWorldSpriteIndexForToolId(id, template),
            MenuSpriteIndex = GetMenuSpriteIndexForToolId(id, template),
            AttachmentSlots = template.AttachmentSlots,
            SalePrice = template.SalePrice,
            UpgradeLevel = level,
            UpgradeFrom = new List<ToolUpgradeData>
            {
                new()
                {
                    RequireToolId = requireToolId,
                    Price = price,
                    TradeItemId = "(O)" + tradeItemId,
                    TradeItemAmount = tradeAmount
                }
            },
            CanBeLostOnDeath = template.CanBeLostOnDeath,
            SetProperties = template.SetProperties,
            ModData = new Dictionary<string, string>
            {
                [Constants.ModId + "/Tier"] = ToolTierUtility.GetTierName(level),
                [Constants.ModId + "/UpgradeLevel"] = level.ToString()
            }
        };
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        UnlockKnownRecipes();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        UnlockKnownRecipes();
        foreach (GameLocation location in Game1.locations)
        {
            foreach (SObject obj in location.Objects.Values.ToArray())
            {
                int range = GetSprinklerRange(obj);
                if (range > 0)
                    WaterSprinklerArea(location, obj, range);
            }
        }
    }

    private static int GetSprinklerRange(SObject sprinkler)
    {
        int baseRange = GetBaseSprinklerRange(sprinkler.ItemId);
        if (baseRange <= 0)
            return 0;

        if (!IsPressureNozzle(sprinkler.heldObject.Value))
            return baseRange;

        int upgradedRange = baseRange + 1;
        if (!Config.PreventPressureNozzleFromMatchingNextBaseTier)
            return upgradedRange;

        string itemId = sprinkler.ItemId;
        if (itemId == Constants.CobaltSprinklerId)
            return Math.Min(upgradedRange, Config.PrismaticSprinklerRange - 1);
        if (itemId == Constants.PrismaticSprinklerId)
            return Math.Min(upgradedRange, Config.RadioactiveSprinklerRange - 1);

        return upgradedRange;
    }

    private static int GetBaseSprinklerRange(string itemId)
    {
        return itemId switch
        {
            Constants.CobaltSprinklerId => Config.CobaltSprinklerRange,
            Constants.PrismaticSprinklerId => Config.PrismaticSprinklerRange,
            Constants.RadioactiveSprinklerId => Config.RadioactiveSprinklerRange,
            _ => 0
        };
    }

    internal static bool IsCustomSprinkler(string itemId)
    {
        return GetBaseSprinklerRange(itemId) > 0;
    }

    private static void WaterSprinklerArea(GameLocation location, SObject sprinkler, int range)
    {
        SObject? enricher = IsEnricher(sprinkler.heldObject.Value) ? sprinkler.heldObject.Value : null;

        for (int x = (int)sprinkler.TileLocation.X - range; x <= sprinkler.TileLocation.X + range; x++)
        {
            for (int y = (int)sprinkler.TileLocation.Y - range; y <= sprinkler.TileLocation.Y + range; y++)
            {
                Vector2 tile = new(x, y);
                if (tile == sprinkler.TileLocation)
                    continue;

                if (location.terrainFeatures.TryGetValue(tile, out TerrainFeature feature) && feature is HoeDirt dirt)
                {
                    TryApplyEnricherFertilizer(dirt, enricher);
                    dirt.state.Value = HoeDirt.watered;
                }
            }
        }
    }

    private static bool IsPressureNozzle(SObject? obj)
    {
        return obj != null && IsObjectId(obj, Constants.PressureNozzleId);
    }

    private static bool IsEnricher(SObject? obj)
    {
        return obj != null && IsObjectId(obj, Constants.EnricherId);
    }

    private static bool IsObjectId(Item item, string id)
    {
        return item.ItemId == id || item.QualifiedItemId == "(O)" + id;
    }

    private static bool IsFertilizer(SObject obj)
    {
        return obj.Category == -19 || string.Equals(obj.Type, "Fertilizer", StringComparison.OrdinalIgnoreCase);
    }

    private static void TryApplyEnricherFertilizer(HoeDirt dirt, SObject? enricher)
    {
        if (enricher?.heldObject.Value is not SObject fertilizer || fertilizer.Stack <= 0 || !DirtHasNoFertilizer(dirt))
            return;

        if (!TrySetDirtFertilizer(dirt, fertilizer))
            return;

        fertilizer.Stack--;
        if (fertilizer.Stack <= 0)
            enricher.heldObject.Value = null;
    }

    private static bool DirtHasNoFertilizer(HoeDirt dirt)
    {
        string fertilizerId = dirt.fertilizer.Value;
        return string.IsNullOrWhiteSpace(fertilizerId) || fertilizerId == "0";
    }

    private static bool TrySetDirtFertilizer(HoeDirt dirt, SObject fertilizer)
    {
        if (string.IsNullOrWhiteSpace(fertilizer.ItemId))
            return false;

        dirt.fertilizer.Value = fertilizer.ItemId;
        return true;
    }

    private static void UnlockKnownRecipes()
    {
        Farmer player = Game1.player;
        int highestToolLevel = GetHighestCoreToolLevel(player);

        if (highestToolLevel >= 4 || HasItem(player, Constants.CobaltBarId))
        {
            AddCraftingRecipe(player, "Cobalt Bar");
            AddCraftingRecipe(player, "Cobalt Sprinkler");
        }

        if (highestToolLevel >= Constants.CobaltLevel || HasItem(player, Constants.PrismaticBarId))
        {
            AddCraftingRecipe(player, "Prismatic Bar");
            AddCraftingRecipe(player, "Prismatic Sprinkler");
        }

        if (highestToolLevel >= Constants.PrismaticLevel || HasItem(player, Constants.VanillaRadioactiveBarId))
            AddCraftingRecipe(player, "Radioactive Sprinkler");
    }

    private static int GetHighestCoreToolLevel(Farmer player)
    {
        int highest = 0;
        foreach (Item? item in player.Items)
        {
            if (item is Tool tool && ToolTierUtility.IsCoreUpgradeableTool(tool) && tool.UpgradeLevel > highest)
                highest = tool.UpgradeLevel;
        }

        if (player.toolBeingUpgraded.Value is Tool pendingTool && ToolTierUtility.IsCoreUpgradeableTool(pendingTool) && pendingTool.UpgradeLevel > highest)
            highest = pendingTool.UpgradeLevel;

        return highest;
    }

    private static bool HasItem(Farmer player, string itemId)
    {
        foreach (Item? item in player.Items)
        {
            if (item?.ItemId == itemId)
                return true;
        }

        return false;
    }

    private static void AddCraftingRecipe(Farmer player, string recipe)
    {
        if (!player.craftingRecipes.ContainsKey(recipe))
            player.craftingRecipes.Add(recipe, 0);
    }

    private void SetHeldToolLevelCommand(string command, string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out int level) || level is < Constants.CobaltLevel or > Constants.RadioactiveLevel)
        {
            Monitor.Log("Usage: toolandsprinklerupgrades_setlevel <5|6|7>", LogLevel.Info);
            return;
        }

        if (Game1.player.CurrentTool is not Tool tool || !ToolTierUtility.IsCoreUpgradeableTool(tool))
        {
            Monitor.Log("Hold an axe, pickaxe, hoe, or watering can first.", LogLevel.Info);
            return;
        }

        tool.UpgradeLevel = level;
        Monitor.Log($"Set held {tool.BaseName} to {ToolTierUtility.GetTierName(level)} level {level}.", LogLevel.Info);
    }

    private void GiveBarsCommand(string command, string[] args)
    {
        Game1.player.addItemByMenuIfNecessary(new SObject(Constants.CobaltBarId, 25));
        Game1.player.addItemByMenuIfNecessary(new SObject(Constants.PrismaticBarId, 25));
        Game1.player.addItemByMenuIfNecessary(new SObject(Constants.VanillaRadioactiveBarId, 25));
        Monitor.Log("Added Tool and Sprinkler Upgrades test bars.", LogLevel.Info);
    }

    internal void DebugLog(string message)
    {
        if (Config.EnableDebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }
}
