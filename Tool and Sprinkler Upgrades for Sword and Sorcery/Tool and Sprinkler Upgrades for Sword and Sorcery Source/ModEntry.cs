using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.Tools;

namespace ThaleTheGreat.ToolAndSprinklerUpgradesForSwordAndSorcery;

internal sealed class ModEntry : Mod
{
    private const int BridgeAssetEditPriority = 100000;

    private const string CobaltAxeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltAxe";
    private const string CobaltPickaxeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltPickaxe";
    private const string CobaltHoeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltHoe";
    private const string CobaltWateringCanId = "ThaleTheGreat.ToolAndSprinklerUpgrades_CobaltWateringCan";

    private const string PrismaticAxeToolId = "ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticAxe";
    private const string PrismaticPickaxeToolId = "ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticPickaxe";
    private const string PrismaticHoeToolId = "ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticHoe";
    private const string PrismaticWateringCanToolId = "ThaleTheGreat.ToolAndSprinklerUpgrades_PrismaticWateringCan";

    private const string RadioactiveAxeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveAxe";
    private const string RadioactivePickaxeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactivePickaxe";
    private const string RadioactiveHoeId = "ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveHoe";
    private const string RadioactiveWateringCanId = "ThaleTheGreat.ToolAndSprinklerUpgrades_RadioactiveWateringCan";

    private const string StygiumAxeToolId = "DN.SnS_StygiumAxe";
    private const string StygiumPickaxeToolId = "DN.SnS_StygiumPickaxe";
    private const string StygiumHoeToolId = "DN.SnS_StygiumHoe";
    private const string StygiumWateringCanToolId = "DN.SnS_StygiumWateringCan";

    private const string BlessedAxeToolId = "DN.SnS_BlessedAxe";
    private const string BlessedPickaxeToolId = "DN.SnS_BlessedPickaxe";
    private const string BlessedHoeToolId = "DN.SnS_BlessedHoe";
    private const string BlessedWateringCanToolId = "DN.SnS_BlessedWateringCan";

    private const string PrismaticAxeId = "(T)" + PrismaticAxeToolId;
    private const string PrismaticPickaxeId = "(T)" + PrismaticPickaxeToolId;
    private const string PrismaticHoeId = "(T)" + PrismaticHoeToolId;
    private const string PrismaticWateringCanId = "(T)" + PrismaticWateringCanToolId;

    private const string BlessedAxeId = "(T)" + BlessedAxeToolId;
    private const string BlessedPickaxeId = "(T)" + BlessedPickaxeToolId;
    private const string BlessedHoeId = "(T)" + BlessedHoeToolId;
    private const string BlessedWateringCanId = "(T)" + BlessedWateringCanToolId;

    private static readonly PriceBridge[] PriceBridges =
    {
        new(StygiumAxeToolId, "(T)IridiumAxe", CobaltAxeId, "(T)IridiumAxe"),
        new(StygiumPickaxeToolId, "(T)IridiumPickaxe", CobaltPickaxeId, "(T)IridiumPickaxe"),
        new(StygiumHoeToolId, "(T)IridiumHoe", CobaltHoeId, "(T)IridiumHoe"),
        new(StygiumWateringCanToolId, "(T)IridiumWateringCan", CobaltWateringCanId, "(T)IridiumWateringCan"),

        new(BlessedAxeToolId, "(T)" + StygiumAxeToolId, PrismaticAxeToolId, "(T)" + CobaltAxeId),
        new(BlessedPickaxeToolId, "(T)" + StygiumPickaxeToolId, PrismaticPickaxeToolId, "(T)" + CobaltPickaxeId),
        new(BlessedHoeToolId, "(T)" + StygiumHoeToolId, PrismaticHoeToolId, "(T)" + CobaltHoeId),
        new(BlessedWateringCanToolId, "(T)" + StygiumWateringCanToolId, PrismaticWateringCanToolId, "(T)" + CobaltWateringCanId)
    };

    private static readonly UpgradeBridge[] UpgradeBridges =
    {
        new(RadioactiveAxeId, PrismaticAxeId, BlessedAxeId),
        new(RadioactivePickaxeId, PrismaticPickaxeId, BlessedPickaxeId),
        new(RadioactiveHoeId, PrismaticHoeId, BlessedHoeId),
        new(RadioactiveWateringCanId, PrismaticWateringCanId, BlessedWateringCanId)
    };

    public override void Entry(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.Content.AssetReady += OnAssetReady;
    }

    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            return;

        e.Edit(asset => ApplyBridge(asset.AsDictionary<string, ToolData>().Data), (AssetEditPriority)BridgeAssetEditPriority);
    }

    private void OnAssetReady(object? sender, AssetReadyEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            return;

        ApplyBridge(Helper.GameContent.Load<Dictionary<string, ToolData>>("Data/Tools"));
    }

    private static void ApplyBridge(IDictionary<string, ToolData> tools)
    {
        EnsureSwordAndSorceryTools(tools);

        foreach (PriceBridge bridge in PriceBridges)
            CopyUpgradePrice(tools, bridge);

        foreach (UpgradeBridge bridge in UpgradeBridges)
            AddUpgradeSource(tools, bridge);
    }

    private static void EnsureSwordAndSorceryTools(IDictionary<string, ToolData> tools)
    {
        AddSwordAndSorceryTool(tools, StygiumHoeToolId, "Hoe", "Stygium Hoe", "Stygium Hoe", "[LocalizedText Strings\\Tools:Hoe_Description] Curses on touch.", "Textures/DN.SnS/StygiumTools", 6, 17, 5, "(T)IridiumHoe", 35000, "(O)DN.SnS_StygiumBar", 5);
        AddSwordAndSorceryTool(tools, StygiumPickaxeToolId, "Pickaxe", "Stygium Pickaxe", "Stygium Pickaxe", "[LocalizedText Strings\\Tools:Pickaxe_Description] Curses on touch.", "Textures/DN.SnS/StygiumTools", 18, 29, 5, "(T)IridiumPickaxe", 35000, "(O)DN.SnS_StygiumBar", 5);
        AddSwordAndSorceryTool(tools, StygiumAxeToolId, "Axe", "Stygium Axe", "Stygium Axe", "[LocalizedText Strings\\Tools:Axe_Description] Curses on touch.", "Textures/DN.SnS/StygiumTools", 30, 41, 5, "(T)IridiumAxe", 35000, "(O)DN.SnS_StygiumBar", 5);
        AddSwordAndSorceryTool(tools, StygiumWateringCanToolId, "WateringCan", "Stygium Watering Can", "Stygium Watering Can", "[LocalizedText Strings\\Tools:WateringCan_Description] Curses on touch.", "Textures/DN.SnS/StygiumTools", 42, 50, 5, "(T)IridiumWateringCan", 35000, "(O)DN.SnS_StygiumBar", 5);

        AddSwordAndSorceryTool(tools, BlessedHoeToolId, "Hoe", "Blessed Hoe", "Elysium Hoe", "[LocalizedText Strings\\Tools:Hoe_Description]", "Textures/DN.SnS/BlessedTools", 6, 17, 6, "(T)" + StygiumHoeToolId, 50000, "(O)DN.SnS_DuskspireHeart", 1);
        AddSwordAndSorceryTool(tools, BlessedPickaxeToolId, "Pickaxe", "Blessed Pickaxe", "Elysium Pickaxe", "[LocalizedText Strings\\Tools:Pickaxe_Description]", "Textures/DN.SnS/BlessedTools", 18, 29, 6, "(T)" + StygiumPickaxeToolId, 50000, "(O)DN.SnS_DuskspireHeart", 1);
        AddSwordAndSorceryTool(tools, BlessedAxeToolId, "Axe", "Blessed Axe", "Elysium Axe", "[LocalizedText Strings\\Tools:Axe_Description]", "Textures/DN.SnS/BlessedTools", 30, 41, 6, "(T)" + StygiumAxeToolId, 50000, "(O)DN.SnS_DuskspireHeart", 1);
        AddSwordAndSorceryTool(tools, BlessedWateringCanToolId, "WateringCan", "Blessed Watering Can", "Elysium Watering Can", "[LocalizedText Strings\\Tools:WateringCan_Description]", "Textures/DN.SnS/BlessedTools", 42, 50, 6, "(T)" + StygiumWateringCanToolId, 50000, "(O)DN.SnS_DuskspireHeart", 1);
    }

    private static void AddSwordAndSorceryTool(IDictionary<string, ToolData> tools, string id, string className, string name, string displayName, string description, string texture, int spriteIndex, int menuSpriteIndex, int upgradeLevel, string requireToolId, int price, string tradeItemId, int tradeItemAmount)
    {
        if (tools.ContainsKey(id))
            return;

        tools[id] = new ToolData
        {
            ClassName = className,
            Name = name,
            DisplayName = displayName,
            Description = description,
            Texture = texture,
            SpriteIndex = spriteIndex,
            MenuSpriteIndex = menuSpriteIndex,
            SalePrice = price,
            UpgradeLevel = upgradeLevel,
            UpgradeFrom = new List<ToolUpgradeData>
            {
                new()
                {
                    RequireToolId = requireToolId,
                    Price = price,
                    TradeItemId = tradeItemId,
                    TradeItemAmount = tradeItemAmount
                }
            }
        };
    }

    private static void CopyUpgradePrice(IDictionary<string, ToolData> tools, PriceBridge bridge)
    {
        ToolUpgradeData? targetUpgrade = GetUpgradeEntry(tools, bridge.TargetToolId, bridge.TargetRequireToolId);
        ToolUpgradeData? templateUpgrade = GetUpgradeEntry(tools, bridge.TemplateToolId, bridge.TemplateRequireToolId);

        if (targetUpgrade == null || templateUpgrade == null)
            return;

        targetUpgrade.Price = templateUpgrade.Price;
    }

    private static ToolUpgradeData? GetUpgradeEntry(IDictionary<string, ToolData> tools, string toolId, string requireToolId)
    {
        return tools.TryGetValue(toolId, out ToolData? tool)
            ? tool.UpgradeFrom?.FirstOrDefault(entry => string.Equals(entry.RequireToolId, requireToolId, StringComparison.OrdinalIgnoreCase))
            : null;
    }

    private static void AddUpgradeSource(IDictionary<string, ToolData> tools, UpgradeBridge bridge)
    {
        if (!tools.TryGetValue(bridge.TargetToolId, out ToolData? targetTool))
            return;

        string sourceToolKey = StripQualifiedToolPrefix(bridge.AlternateRequireToolId);
        if (!tools.ContainsKey(sourceToolKey))
            return;

        targetTool.UpgradeFrom ??= new List<ToolUpgradeData>();

        if (targetTool.UpgradeFrom.Any(entry => string.Equals(entry.RequireToolId, bridge.AlternateRequireToolId, StringComparison.OrdinalIgnoreCase)))
            return;

        ToolUpgradeData? template = targetTool.UpgradeFrom.FirstOrDefault(entry => string.Equals(entry.RequireToolId, bridge.TemplateRequireToolId, StringComparison.OrdinalIgnoreCase))
            ?? targetTool.UpgradeFrom.FirstOrDefault();

        if (template == null)
            return;

        targetTool.UpgradeFrom.Add(new ToolUpgradeData
        {
            RequireToolId = bridge.AlternateRequireToolId,
            Price = template.Price,
            TradeItemId = template.TradeItemId,
            TradeItemAmount = template.TradeItemAmount
        });
    }

    private static string StripQualifiedToolPrefix(string itemId)
    {
        return itemId.StartsWith("(T)", StringComparison.OrdinalIgnoreCase)
            ? itemId[3..]
            : itemId;
    }

    private sealed record PriceBridge(string TargetToolId, string TargetRequireToolId, string TemplateToolId, string TemplateRequireToolId);

    private sealed record UpgradeBridge(string TargetToolId, string TemplateRequireToolId, string AlternateRequireToolId);
}
