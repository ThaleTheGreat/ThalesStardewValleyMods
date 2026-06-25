using System.Collections;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.GameData.Tools;
using StardewValley.Objects;
using StardewValley.Tools;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

public sealed class ModEntry : Mod
{
    internal static ModEntry Instance { get; private set; } = null!;
    internal static ModConfig Config { get; private set; } = null!;

    private bool suppressExistingSaveSpriteRefresh;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        NormalizeConfig(helper);

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
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


    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm == null)
            return;

        gmcm.Register(
            ModManifest,
            () => Config = new ModConfig(),
            () =>
            {
                NormalizeConfig(Helper);
                Helper.WriteConfig(Config);
            }
        );

        gmcm.AddBoolOption(
            ModManifest,
            () => Config.RadioactiveSprinklerActsAsScarecrow,
            value => Config.RadioactiveSprinklerActsAsScarecrow = value,
            () => "Radioactive Sprinkler Scarecrow",
            () => "Treat placed Radioactive Sprinklers like a Scarecrow."
        );
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
                data[Constants.RadioactiveSprinklerId] = CreateObjectData("Radioactive Sprinkler", "Waters the 224 surrounding tiles every morning. It can also protect crops like a  Scarecrow.", "Crafting", -9, 2500, Constants.RadioactiveTextureAsset, Constants.RadioactiveSprinklerSpriteIndex, new List<string> { "sprinkler" });
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

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ShopData> data = asset.AsDictionary<string, ShopData>().Data;
                AddWillyFishingRodShopItems(data);
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
        AddTool(data, "IridiumPan", Constants.CobaltPanId, "Cobalt Pan", "Use this to gather ore from streams.", Constants.CobaltLevel, "(T)IridiumPan", Constants.CobaltBarId, Constants.CobaltTextureAsset, ModEntry.Config.CobaltUpgradeCost, ModEntry.Config.CobaltBarsRequired);
        AddTool(data, "IridiumPan", Constants.PrismaticPanId, "Prismatic Pan", "Use this to gather ore from streams.", Constants.PrismaticLevel, "(T)" + Constants.CobaltPanId, Constants.PrismaticBarId, Constants.PrismaticTextureAsset, ModEntry.Config.PrismaticUpgradeCost, ModEntry.Config.PrismaticBarsRequired);
        AddTool(data, "IridiumPan", Constants.RadioactivePanId, "Radioactive Pan", "Use this to gather ore from streams.", Constants.RadioactiveLevel, "(T)" + Constants.PrismaticPanId, Constants.VanillaRadioactiveBarId, Constants.RadioactiveTextureAsset, ModEntry.Config.RadioactiveUpgradeCost, ModEntry.Config.RadioactiveBarsRequired);

        AddShopTool(data, Constants.AdvancedIridiumRodId, Constants.CobaltFishingRodId, "Cobalt Rod", "Use in the water to catch fish.", Constants.CobaltLevel, Constants.CobaltTextureAsset);
        AddShopTool(data, Constants.AdvancedIridiumRodId, Constants.PrismaticFishingRodId, "Prismatic Rod", "Use in the water to catch fish.", Constants.PrismaticLevel, Constants.PrismaticTextureAsset);
        AddShopTool(data, Constants.AdvancedIridiumRodId, Constants.RadioactiveFishingRodId, "Radioactive Rod", "Use in the water to catch fish.", Constants.RadioactiveLevel, Constants.RadioactiveTextureAsset);
    }


    private static void AddWillyFishingRodShopItems(IDictionary<string, ShopData> shops)
    {
        if (!shops.TryGetValue("FishShop", out ShopData? fishShop))
            return;

        fishShop.Items ??= new List<ShopItemData>();
        AddPurchaseMailAction(fishShop.Items, Constants.IridiumRodId, Constants.IridiumRodPurchasedMailId);
        AddPurchaseMailAction(fishShop.Items, Constants.AdvancedIridiumRodId, Constants.IridiumRodPurchasedMailId);

        string masteryCondition = GetVanillaAdvancedIridiumRodCondition(fishShop.Items);
        AddFishingRodShopItem(fishShop.Items, Constants.CobaltFishingRodId, ModEntry.Config.CobaltUpgradeCost, GetCobaltFishingRodCondition(masteryCondition), Constants.CobaltFishingRodPurchasedMailId);
        AddFishingRodShopItem(fishShop.Items, Constants.PrismaticFishingRodId, ModEntry.Config.PrismaticUpgradeCost, GetPrismaticFishingRodCondition(masteryCondition), Constants.PrismaticFishingRodPurchasedMailId);
        AddFishingRodShopItem(fishShop.Items, Constants.RadioactiveFishingRodId, ModEntry.Config.RadioactiveUpgradeCost, GetRadioactiveFishingRodCondition(masteryCondition), Constants.RadioactiveFishingRodPurchasedMailId);
    }

    private static void AddPurchaseMailAction(List<ShopItemData> items, string itemId, string purchaseMailId)
    {
        ShopItemData? item = items.FirstOrDefault(entry => entry.ItemId == "(T)" + itemId);
        if (item == null)
            return;

        item.ActionsOnPurchase ??= new List<string>();
        string action = "AddMail Current " + purchaseMailId + " received";
        if (!item.ActionsOnPurchase.Contains(action))
            item.ActionsOnPurchase.Add(action);
    }

    private static void AddFishingRodShopItem(List<ShopItemData> items, string itemId, int price, string condition, string purchaseMailId)
    {
        if (items.Any(item => item.Id == itemId))
            return;

        items.Add(new ShopItemData
        {
            Id = itemId,
            ItemId = "(T)" + itemId,
            Price = price,
            Condition = condition,
            ActionsOnPurchase = new List<string>
            {
                "AddMail Current " + purchaseMailId + " received"
            }
        });
    }

    private static string GetVanillaAdvancedIridiumRodCondition(List<ShopItemData> items)
    {
        string? condition = items.FirstOrDefault(item => item.ItemId == "(T)" + Constants.AdvancedIridiumRodId)?.Condition;
        if (!string.IsNullOrWhiteSpace(condition))
            return condition;

        return "ANY \"PLAYER_HAS_MAIL Current " + Constants.FishingMasteryMailId + " Received\" \"PLAYER_HAS_MAIL Current " + Constants.FishingMasteryAlternateMailId + " Received\"";
    }

    private static string GetCobaltFishingRodCondition(string masteryCondition)
    {
        return masteryCondition + ", PLAYER_HAS_MAIL Current " + Constants.IridiumRodPurchasedMailId + " Received, PLAYER_HAS_CRAFTING_RECIPE Current Cobalt Bar";
    }

    private static string GetPrismaticFishingRodCondition(string masteryCondition)
    {
        return masteryCondition + ", PLAYER_HAS_MAIL Current " + Constants.CobaltFishingRodPurchasedMailId + " Received, PLAYER_HAS_CRAFTING_RECIPE Current Prismatic Bar";
    }

    private static string GetRadioactiveFishingRodCondition(string masteryCondition)
    {
        return masteryCondition + ", PLAYER_HAS_MAIL Current " + Constants.PrismaticFishingRodPurchasedMailId + " Received, PLAYER_HAS_ITEM Current (O)" + Constants.VanillaRadioactiveBarId;
    }

    private static void AddShopTool(IDictionary<string, ToolData> data, string templateId, string id, string name, string description, int level, string textureAsset)
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
            SpriteIndex = template.SpriteIndex,
            MenuSpriteIndex = template.MenuSpriteIndex >= 0 ? template.MenuSpriteIndex : template.SpriteIndex,
            AttachmentSlots = template.AttachmentSlots,
            SalePrice = template.SalePrice,
            UpgradeLevel = level,
            CanBeLostOnDeath = template.CanBeLostOnDeath,
            SetProperties = template.SetProperties,
            ModData = new Dictionary<string, string>
            {
                [Constants.ModId + "/Tier"] = ToolTierUtility.GetTierName(level),
                [Constants.ModId + "/UpgradeLevel"] = level.ToString()
            }
        };
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
            SpriteIndex = template.SpriteIndex,
            MenuSpriteIndex = template.MenuSpriteIndex >= 0 ? template.MenuSpriteIndex : template.SpriteIndex,
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
        RefreshExistingCustomToolSprites();
        MarkKnownRodPurchases();
        UnlockKnownRecipes();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        MarkKnownRodPurchases();
        UnlockKnownRecipes();
    }

    internal static int GetSprinklerRange(SObject sprinkler)
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

    internal static int GetBaseSprinklerRange(string itemId)
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

    internal static bool IsRadioactiveSprinklerScarecrow(SObject obj)
    {
        return Config.RadioactiveSprinklerActsAsScarecrow && IsObjectId(obj, Constants.RadioactiveSprinklerId);
    }

    private static bool IsPressureNozzle(SObject? obj)
    {
        return obj != null && IsObjectId(obj, Constants.PressureNozzleId);
    }

    private static bool IsObjectId(Item item, string id)
    {
        return item.ItemId == id || item.QualifiedItemId == "(O)" + id;
    }

    private const string SpriteRefreshVersion = "1.2.0";

    private void RefreshExistingCustomToolSprites()
    {
        if (!Context.IsWorldReady || suppressExistingSaveSpriteRefresh)
            return;

        suppressExistingSaveSpriteRefresh = true;
        int fixedCount = 0;
        HashSet<Item> seen = new(ReferenceEqualityComparer.Instance);

        try
        {
            try
            {
                Utility.ForEachItem(item =>
                {
                    if (item is Tool tool && seen.Add(item) && TryGetCustomToolDataForItem(tool, out string itemId, out CustomToolSpriteData spriteData) && !ToolInstanceMatchesCurrentData(tool, itemId, spriteData))
                    {
                        ApplyCurrentCustomToolData(tool, itemId, spriteData);
                        fixedCount++;
                    }

                    return true;
                });
            }
            catch (Exception ex)
            {
                DebugLog("Utility.ForEachItem custom tool refresh failed; falling back to direct farmer/chest scan: " + ex.Message);
                RefreshFarmerCustomToolSprites(Game1.player, seen, ref fixedCount);
                foreach (GameLocation location in Game1.locations.ToArray())
                    RefreshLocationChestToolSprites(location, seen, ref fixedCount);
            }

            fixedCount += RefreshToolBeingUpgraded(Game1.player, seen);
            fixedCount += RefreshWalletToolsStoredSprites();
        }
        finally
        {
            suppressExistingSaveSpriteRefresh = false;
        }

        if (fixedCount > 0)
            Monitor.Log($"Refreshed {fixedCount} existing custom tool instance(s) to current Data/Tools sprite indexes.", LogLevel.Info);
    }

    private static int RefreshToolBeingUpgraded(Farmer? farmer, HashSet<Item> seen)
    {
        if (farmer?.toolBeingUpgraded.Value is null)
            return 0;

        if (!seen.Add(farmer.toolBeingUpgraded.Value))
            return 0;

        if (!TryCreateRefreshedCustomTool(farmer.toolBeingUpgraded.Value, out Tool? refreshedTool))
            return 0;

        farmer.toolBeingUpgraded.Value = refreshedTool;
        return 1;
    }

    private static void RefreshFarmerCustomToolSprites(Farmer? farmer, HashSet<Item> seen, ref int fixedCount)
    {
        if (farmer == null)
            return;

        for (int i = 0; i < farmer.Items.Count; i++)
        {
            Item? item = farmer.Items[i];
            if (item != null && seen.Add(item) && TryCreateRefreshedCustomTool(item, out Tool? refreshedTool))
            {
                farmer.Items[i] = refreshedTool;
                fixedCount++;
            }
        }
    }

    private static void RefreshLocationChestToolSprites(GameLocation location, HashSet<Item> seen, ref int fixedCount)
    {
        foreach (SObject obj in location.Objects.Values.ToArray())
        {
            if (obj is not Chest chest)
                continue;

            for (int i = 0; i < chest.Items.Count; i++)
            {
                Item? item = chest.Items[i];
                if (item != null && seen.Add(item) && TryCreateRefreshedCustomTool(item, out Tool? refreshedTool))
                {
                    chest.Items[i] = refreshedTool;
                    fixedCount++;
                }
            }
        }
    }

    private static bool TryCreateRefreshedCustomTool(Item? item, out Tool? refreshedTool)
    {
        refreshedTool = null;
        if (item is not Tool oldTool || !TryGetCustomToolDataForItem(oldTool, out string itemId, out CustomToolSpriteData spriteData))
            return false;

        if (ToolInstanceMatchesCurrentData(oldTool, itemId, spriteData))
            return false;

        refreshedTool = CreateCurrentCustomTool(itemId, spriteData, oldTool);
        return refreshedTool != null;
    }

    private static bool ToolInstanceMatchesCurrentData(Tool tool, string itemId, CustomToolSpriteData spriteData)
    {
        return string.Equals(NormalizeToolItemId(tool.ItemId), itemId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(NormalizeToolItemId(tool.QualifiedItemId), itemId, StringComparison.OrdinalIgnoreCase)
            && tool.UpgradeLevel == spriteData.Level
            && tool.CurrentParentTileIndex == spriteData.SpriteIndex
            && tool.IndexOfMenuItemView == spriteData.MenuSpriteIndex
            && tool.modData.TryGetValue(Constants.ModId + "/SpriteRefreshVersion", out string? refreshVersion)
            && string.Equals(refreshVersion, SpriteRefreshVersion, StringComparison.Ordinal);
    }

    private static Tool? CreateCurrentCustomTool(string itemId, CustomToolSpriteData spriteData, Tool? oldTool)
    {
        try
        {
            Tool newTool = ItemRegistry.Create<Tool>("(T)" + itemId);
            CopyPersistentToolState(oldTool, newTool);
            ApplyCurrentCustomToolData(newTool, itemId, spriteData);
            return newTool;
        }
        catch
        {
            return null;
        }
    }

    private static void CopyPersistentToolState(Tool? oldTool, Tool newTool)
    {
        if (oldTool == null)
            return;

        foreach (KeyValuePair<string, string> pair in oldTool.modData.Pairs)
            newTool.modData[pair.Key] = pair.Value;

        int attachmentCount = Math.Min(oldTool.attachments.Length, newTool.attachments.Length);
        for (int i = 0; i < attachmentCount; i++)
        {
            SObject? attachment = oldTool.attachments[i];
            newTool.attachments[i] = attachment?.getOne() as SObject ?? attachment;
        }

        newTool.enchantments.Clear();
        foreach (var enchantment in oldTool.enchantments)
            newTool.enchantments.Add(enchantment);

        CopyMatchingMemberValue(oldTool, newTool, "waterLeft");
        CopyMatchingMemberValue(oldTool, newTool, "WaterLeft");
        CopyMatchingMemberValue(oldTool, newTool, "lastUser");
        CopyMatchingMemberValue(oldTool, newTool, "LastUser");
    }

    private static void ApplyCurrentCustomToolData(Tool tool, string itemId, CustomToolSpriteData spriteData)
    {
        tool.UpgradeLevel = spriteData.Level;
        tool.CurrentParentTileIndex = spriteData.SpriteIndex;
        tool.IndexOfMenuItemView = spriteData.MenuSpriteIndex;
        tool.modData[Constants.ModId + "/Tier"] = ToolTierUtility.GetTierName(spriteData.Level);
        tool.modData[Constants.ModId + "/UpgradeLevel"] = spriteData.Level.ToString();
        tool.modData[Constants.ModId + "/SpriteRefreshVersion"] = SpriteRefreshVersion;

        SetStringMember(tool, "itemId", itemId);
        SetStringMember(tool, "qualifiedItemId", "(T)" + itemId);
        SetStringMember(tool, "Name", spriteData.Name);
        SetStringMember(tool, "name", spriteData.Name);
        SetStringMember(tool, "DisplayName", spriteData.DisplayName);
        SetStringMember(tool, "displayName", spriteData.DisplayName);
        SetStringMember(tool, "Texture", spriteData.Texture);
        SetStringMember(tool, "TexturePath", spriteData.Texture);
        SetStringMember(tool, "texturePath", spriteData.Texture);
        SetStringMember(tool, "textureName", spriteData.Texture);
        SetIntMember(tool, "SpriteIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "spriteIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "CurrentParentTileIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "currentParentTileIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "InitialParentTileIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "initialParentTileIndex", spriteData.SpriteIndex);
        SetIntMember(tool, "MenuSpriteIndex", spriteData.MenuSpriteIndex);
        SetIntMember(tool, "menuSpriteIndex", spriteData.MenuSpriteIndex);
        SetIntMember(tool, "IndexOfMenuItemView", spriteData.MenuSpriteIndex);
        SetIntMember(tool, "indexOfMenuItemView", spriteData.MenuSpriteIndex);
    }

    private int RefreshWalletToolsStoredSprites()
    {
        int fixedCount = 0;

        IWalletToolsApi? api = null;
        try
        {
            api = Helper.ModRegistry.GetApi<IWalletToolsApi>("ThaleTheGreat.WalletTools");
        }
        catch (Exception ex)
        {
            DebugLog("Wallet Tools public API check was skipped: " + ex.Message);
        }

        if (api != null)
            fixedCount += RefreshWalletToolsViaPublicApi(api);

        object? walletMod = GetWalletToolsModInstance(api);
        if (walletMod != null)
            fixedCount += RefreshWalletToolsStoredStateBySourceShape(walletMod);

        return fixedCount;
    }

    private object? GetWalletToolsModInstance(IWalletToolsApi? api)
    {
        object? modInfo = Helper.ModRegistry.Get("ThaleTheGreat.WalletTools");
        object? mod = modInfo != null ? GetMemberValue(modInfo, "Mod") : null;
        if (mod != null)
            return mod;

        return api != null ? GetMemberValue(api, "Mod") : null;
    }

    private int RefreshWalletToolsViaPublicApi(IWalletToolsApi api)
    {
        string[] kinds;
        try
        {
            kinds = api.GetStoredToolKinds() ?? Array.Empty<string>();
        }
        catch (Exception ex)
        {
            DebugLog("Wallet Tools stored tool list check was skipped: " + ex.Message);
            return 0;
        }

        int recognized = 0;
        foreach (string kind in kinds)
        {
            try
            {
                if (!api.TryGetStoredTool(kind, out string qualifiedItemId, out _, out string displayName))
                    continue;

                if (TryGetCustomToolData(qualifiedItemId, out _) || TryGetCustomToolData(displayName, out _))
                    recognized++;
            }
            catch (Exception ex)
            {
                DebugLog("Wallet Tools stored tool check skipped one entry: " + ex.Message);
            }
        }

        if (recognized > 0)
            Helper.GameContent.InvalidateCache("Data/Powers");

        return 0;
    }

    private int RefreshWalletToolsStoredStateBySourceShape(object walletMod)
    {
        object? storedToolsByPlayer = GetMemberValue(walletMod, "StoredToolsByPlayer");
        if (storedToolsByPlayer is not IDictionary byPlayer)
            return 0;

        int fixedCount = 0;
        foreach (DictionaryEntry playerEntry in byPlayer)
        {
            if (playerEntry.Value is not IDictionary storedTools)
                continue;

            foreach (DictionaryEntry toolEntry in storedTools)
            {
                object? state = toolEntry.Value;
                if (state == null || !TryGetWalletStateCustomToolData(state, out string itemId, out CustomToolSpriteData spriteData))
                    continue;

                if (ApplyWalletToolStateData(state, itemId, spriteData))
                    fixedCount++;
            }
        }

        if (fixedCount > 0)
        {
            InvokeWalletToolsRefresh(walletMod);
            Helper.GameContent.InvalidateCache("Data/Powers");
        }

        return fixedCount;
    }

    private static bool TryGetWalletStateCustomToolData(object state, out string itemId, out CustomToolSpriteData spriteData)
    {
        foreach (string candidate in GetWalletStateIdentityCandidates(state))
        {
            if (TryGetCustomToolData(candidate, out spriteData))
            {
                itemId = NormalizeToolItemId(candidate);
                return true;
            }
        }

        itemId = string.Empty;
        spriteData = default;
        return false;
    }

    private static IEnumerable<string> GetWalletStateIdentityCandidates(object state)
    {
        yield return GetStringMember(state, "QualifiedItemId");
        yield return GetStringMember(state, "Name");
        yield return GetStringMember(state, "DisplayName");

        if (GetMemberValue(state, "LiveTool") is Tool liveTool)
        {
            foreach (string candidate in GetToolIdentityCandidates(liveTool))
                yield return candidate;
        }

        if (GetMemberValue(state, "ModData") is IDictionary modData)
        {
            foreach (object? value in modData.Values)
            {
                if (value is string text)
                    yield return text;
            }
        }
    }

    private static bool ApplyWalletToolStateData(object state, string itemId, CustomToolSpriteData spriteData)
    {
        bool changed = false;
        string qualifiedItemId = "(T)" + itemId;
        Tool? liveTool = CreateCurrentCustomTool(itemId, spriteData, GetMemberValue(state, "LiveTool") as Tool);

        changed |= SetStringMember(state, "QualifiedItemId", qualifiedItemId);
        changed |= SetStringMember(state, "Name", spriteData.Name);
        changed |= SetStringMember(state, "DisplayName", spriteData.DisplayName);
        if (liveTool != null)
            changed |= SetStringMember(state, "Description", liveTool.getDescription());
        changed |= SetIntMember(state, "UpgradeLevel", spriteData.Level);
        changed |= SetIntMember(state, "MenuSpriteIndex", spriteData.MenuSpriteIndex);
        changed |= SetStringMember(state, "TexturePath", spriteData.Texture);

        if (GetMemberValue(state, "ModData") is IDictionary modData)
        {
            SetDictionaryStringValue(modData, Constants.ModId + "/Tier", ToolTierUtility.GetTierName(spriteData.Level), ref changed);
            SetDictionaryStringValue(modData, Constants.ModId + "/UpgradeLevel", spriteData.Level.ToString(), ref changed);
            SetDictionaryStringValue(modData, Constants.ModId + "/SpriteRefreshVersion", SpriteRefreshVersion, ref changed);
        }

        if (liveTool != null)
            changed |= SetObjectMember(state, "LiveTool", liveTool);

        return changed;
    }

    private static void InvokeWalletToolsRefresh(object walletMod)
    {
        try
        {
            MethodInfo? method = walletMod.GetType().GetMethod(
                "RefreshWalletStateAfterStoredToolChange",
                BindingFlags.Instance | BindingFlags.NonPublic,
                null,
                new[] { typeof(Farmer), typeof(bool) },
                null
            );
            method?.Invoke(walletMod, new object[] { Game1.player, true });
        }
        catch
        {
        }
    }

    private static bool TryGetCustomToolDataForItem(Tool tool, out string itemId, out CustomToolSpriteData spriteData)
    {
        foreach (string candidate in GetToolIdentityCandidates(tool))
        {
            if (TryGetCustomToolData(candidate, out spriteData))
            {
                itemId = NormalizeToolItemId(candidate);
                return true;
            }
        }

        itemId = string.Empty;
        spriteData = default;
        return false;
    }

    private static IEnumerable<string> GetToolIdentityCandidates(Tool tool)
    {
        yield return tool.ItemId;
        yield return tool.QualifiedItemId;
        yield return tool.Name;
        yield return tool.DisplayName;
        yield return GetStringMember(tool, "BaseName");
        yield return GetStringMember(tool, "baseName");
        yield return GetStringMember(tool, "itemId");
        yield return GetStringMember(tool, "qualifiedItemId");
        yield return GetStringMember(tool, "Name");
        yield return GetStringMember(tool, "name");
        yield return GetStringMember(tool, "displayName");
    }

    private static string NormalizeToolItemId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return string.Empty;

        string itemId = rawId.Trim();
        if (itemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase))
            itemId = itemId[3..];

        return itemId switch
        {
            "Cobalt Axe" => Constants.CobaltAxeId,
            "Cobalt Pickaxe" => Constants.CobaltPickaxeId,
            "Cobalt Hoe" => Constants.CobaltHoeId,
            "Cobalt Watering Can" => Constants.CobaltWateringCanId,
            "Cobalt Pan" => Constants.CobaltPanId,
            "Cobalt Rod" => Constants.CobaltFishingRodId,
            "Prismatic Axe" => Constants.PrismaticAxeId,
            "Prismatic Pickaxe" => Constants.PrismaticPickaxeId,
            "Prismatic Hoe" => Constants.PrismaticHoeId,
            "Prismatic Watering Can" => Constants.PrismaticWateringCanId,
            "Prismatic Pan" => Constants.PrismaticPanId,
            "Prismatic Rod" => Constants.PrismaticFishingRodId,
            "Radioactive Axe" => Constants.RadioactiveAxeId,
            "Radioactive Pickaxe" => Constants.RadioactivePickaxeId,
            "Radioactive Hoe" => Constants.RadioactiveHoeId,
            "Radioactive Watering Can" => Constants.RadioactiveWateringCanId,
            "Radioactive Pan" => Constants.RadioactivePanId,
            "Radioactive Rod" => Constants.RadioactiveFishingRodId,
            _ => itemId
        };
    }

    private static bool TryGetCustomToolData(string rawItemId, out CustomToolSpriteData spriteData)
    {
        spriteData = default;
        string itemId = NormalizeToolItemId(rawItemId);
        if (string.IsNullOrWhiteSpace(itemId) || !TryGetVanillaTemplateForCustomToolId(itemId, out string templateId, out int level))
            return false;

        IDictionary<string, ToolData> data = Game1.content.Load<Dictionary<string, ToolData>>("Data/Tools");
        if (!data.TryGetValue(itemId, out ToolData? customTool) || !data.TryGetValue(templateId, out ToolData? template))
            return false;

        int menuSpriteIndex = template.MenuSpriteIndex >= 0 ? template.MenuSpriteIndex : template.SpriteIndex;
        spriteData = new CustomToolSpriteData(
            level,
            customTool.Name,
            customTool.DisplayName,
            customTool.Texture,
            template.SpriteIndex,
            menuSpriteIndex
        );
        return true;
    }

    private static bool TryGetVanillaTemplateForCustomToolId(string itemId, out string templateId, out int level)
    {
        templateId = string.Empty;
        level = 0;

        if (itemId is Constants.CobaltAxeId or Constants.CobaltPickaxeId or Constants.CobaltHoeId or Constants.CobaltWateringCanId or Constants.CobaltPanId or Constants.CobaltFishingRodId)
            level = Constants.CobaltLevel;
        else if (itemId is Constants.PrismaticAxeId or Constants.PrismaticPickaxeId or Constants.PrismaticHoeId or Constants.PrismaticWateringCanId or Constants.PrismaticPanId or Constants.PrismaticFishingRodId)
            level = Constants.PrismaticLevel;
        else if (itemId is Constants.RadioactiveAxeId or Constants.RadioactivePickaxeId or Constants.RadioactiveHoeId or Constants.RadioactiveWateringCanId or Constants.RadioactivePanId or Constants.RadioactiveFishingRodId)
            level = Constants.RadioactiveLevel;
        else
            return false;

        if (itemId.EndsWith("Axe", StringComparison.Ordinal))
            templateId = "IridiumAxe";
        else if (itemId.EndsWith("Pickaxe", StringComparison.Ordinal))
            templateId = "IridiumPickaxe";
        else if (itemId.EndsWith("Hoe", StringComparison.Ordinal))
            templateId = "IridiumHoe";
        else if (itemId.EndsWith("WateringCan", StringComparison.Ordinal))
            templateId = "IridiumWateringCan";
        else if (itemId.EndsWith("Pan", StringComparison.Ordinal))
            templateId = "IridiumPan";
        else if (itemId.EndsWith("FishingRod", StringComparison.Ordinal))
            templateId = Constants.AdvancedIridiumRodId;
        else
            return false;

        return true;
    }

    private static object? GetMemberValue(object target, string memberName)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(memberName, flags);
            if (field != null)
                return field.GetValue(target);

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
                return property.GetValue(target);
        }

        return null;
    }

    private static string GetStringMember(object target, string memberName)
    {
        object? value = GetMemberValue(target, memberName);
        if (value is string text)
            return text;

        PropertyInfo? valueProperty = value?.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (valueProperty != null && valueProperty.CanRead && valueProperty.GetValue(value) is string netText)
            return netText;

        return string.Empty;
    }

    private static bool SetIntMember(object target, string memberName, int value)
    {
        return SetTypedMemberValue(target, memberName, value);
    }

    private static bool SetStringMember(object target, string memberName, string value)
    {
        return SetTypedMemberValue(target, memberName, value);
    }

    private static bool SetObjectMember(object target, string memberName, object value)
    {
        return SetTypedMemberValue(target, memberName, value);
    }

    private static bool SetTypedMemberValue<TValue>(object target, string memberName, TValue value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        for (Type? type = target.GetType(); type != null; type = type.BaseType)
        {
            FieldInfo? field = type.GetField(memberName, flags);
            if (field != null)
            {
                object? current = field.GetValue(target);
                if (current == null && value != null && !field.IsInitOnly && field.FieldType.IsAssignableFrom(value.GetType()))
                {
                    field.SetValue(target, value);
                    return true;
                }

                return SetReflectedValue(current, value, newValue =>
                {
                    if (!field.IsInitOnly)
                        field.SetValue(target, newValue);
                });
            }

            PropertyInfo? property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
            {
                object? current = property.GetValue(target);
                if (current == null && value != null && property.CanWrite && property.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    property.SetValue(target, value);
                    return true;
                }

                return SetReflectedValue(current, value, newValue =>
                {
                    if (property.CanWrite)
                        property.SetValue(target, newValue);
                });
            }
        }

        return false;
    }

    private static bool SetReflectedValue<TValue>(object? current, TValue value, Action<TValue> assignDirect)
    {
        if (current is TValue currentValue)
        {
            if (EqualityComparer<TValue>.Default.Equals(currentValue, value))
                return false;

            assignDirect(value);
            return true;
        }

        if (current != null)
        {
            PropertyInfo? valueProperty = current.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (valueProperty != null && valueProperty.CanRead && valueProperty.CanWrite)
            {
                object? oldValue = valueProperty.GetValue(current);
                if (oldValue is TValue typedOldValue && EqualityComparer<TValue>.Default.Equals(typedOldValue, value))
                    return false;

                valueProperty.SetValue(current, value);
                return true;
            }
        }

        return false;
    }

    private static void CopyMatchingMemberValue(object source, object target, string memberName)
    {
        object? sourceValue = GetMemberValue(source, memberName);
        object? targetValue = GetMemberValue(target, memberName);
        if (sourceValue == null || targetValue == null)
            return;

        PropertyInfo? sourceValueProperty = sourceValue.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        PropertyInfo? targetValueProperty = targetValue.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (sourceValueProperty != null && targetValueProperty != null && sourceValueProperty.CanRead && targetValueProperty.CanWrite)
        {
            object? copiedValue = sourceValueProperty.GetValue(sourceValue);
            if (copiedValue != null && targetValueProperty.PropertyType.IsAssignableFrom(copiedValue.GetType()))
                targetValueProperty.SetValue(targetValue, copiedValue);
            return;
        }

        SetObjectMember(target, memberName, sourceValue);
    }

    private static void SetDictionaryStringValue(IDictionary dictionary, string key, string value, ref bool changed)
    {
        if (dictionary.Contains(key) && dictionary[key] is string oldValue && oldValue == value)
            return;

        dictionary[key] = value;
        changed = true;
    }

    private readonly record struct CustomToolSpriteData(int Level, string Name, string DisplayName, string Texture, int SpriteIndex, int MenuSpriteIndex);


    private static void MarkKnownRodPurchases()
    {
        Farmer player = Game1.player;
        if (HasItem(player, Constants.IridiumRodId) || HasItem(player, Constants.AdvancedIridiumRodId))
            player.mailReceived.Add(Constants.IridiumRodPurchasedMailId);
        if (HasItem(player, Constants.CobaltFishingRodId))
            player.mailReceived.Add(Constants.CobaltFishingRodPurchasedMailId);
        if (HasItem(player, Constants.PrismaticFishingRodId))
            player.mailReceived.Add(Constants.PrismaticFishingRodPurchasedMailId);
        if (HasItem(player, Constants.RadioactiveFishingRodId))
            player.mailReceived.Add(Constants.RadioactiveFishingRodPurchasedMailId);
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
