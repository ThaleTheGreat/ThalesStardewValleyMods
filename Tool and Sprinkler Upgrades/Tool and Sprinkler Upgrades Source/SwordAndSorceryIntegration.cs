using StardewValley.GameData.Tools;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal static class SwordAndSorceryIntegration
{
    public const int AssetEditPriority = 100000;
    public const string LegacyAddonModId = "ThaleTheGreat.ToolAndSprinklerUpgradesForSwordAndSorcery";
    public const string ContentModId = "DN.SnS";
    public const string CodeModId = "KCC.SnS";

    private const string StygiumAxeToolId = "DN.SnS_StygiumAxe";
    private const string StygiumPickaxeToolId = "DN.SnS_StygiumPickaxe";
    private const string StygiumHoeToolId = "DN.SnS_StygiumHoe";
    private const string StygiumWateringCanToolId = "DN.SnS_StygiumWateringCan";

    private const string BlessedAxeToolId = "DN.SnS_BlessedAxe";
    private const string BlessedPickaxeToolId = "DN.SnS_BlessedPickaxe";
    private const string BlessedHoeToolId = "DN.SnS_BlessedHoe";
    private const string BlessedWateringCanToolId = "DN.SnS_BlessedWateringCan";

    private const string MiddleTierAxeToolId = Constants.PrismaticAxeId;
    private const string MiddleTierPickaxeToolId = Constants.PrismaticPickaxeId;
    private const string MiddleTierHoeToolId = Constants.PrismaticHoeId;
    private const string MiddleTierWateringCanToolId = Constants.PrismaticWateringCanId;

    private const string TopTierAxeToolId = Constants.RadioactiveAxeId;
    private const string TopTierPickaxeToolId = Constants.RadioactivePickaxeId;
    private const string TopTierHoeToolId = Constants.RadioactiveHoeId;
    private const string TopTierWateringCanToolId = Constants.RadioactiveWateringCanId;

    private const string MiddleTierAxeId = "(T)" + MiddleTierAxeToolId;
    private const string MiddleTierPickaxeId = "(T)" + MiddleTierPickaxeToolId;
    private const string MiddleTierHoeId = "(T)" + MiddleTierHoeToolId;
    private const string MiddleTierWateringCanId = "(T)" + MiddleTierWateringCanToolId;

    private const string BlessedAxeId = "(T)" + BlessedAxeToolId;
    private const string BlessedPickaxeId = "(T)" + BlessedPickaxeToolId;
    private const string BlessedHoeId = "(T)" + BlessedHoeToolId;
    private const string BlessedWateringCanId = "(T)" + BlessedWateringCanToolId;

    private static readonly PriceBridge[] PriceBridges =
    {
        new(StygiumAxeToolId, "(T)IridiumAxe", Constants.CobaltAxeId, "(T)IridiumAxe"),
        new(StygiumPickaxeToolId, "(T)IridiumPickaxe", Constants.CobaltPickaxeId, "(T)IridiumPickaxe"),
        new(StygiumHoeToolId, "(T)IridiumHoe", Constants.CobaltHoeId, "(T)IridiumHoe"),
        new(StygiumWateringCanToolId, "(T)IridiumWateringCan", Constants.CobaltWateringCanId, "(T)IridiumWateringCan"),

        new(BlessedAxeToolId, "(T)" + StygiumAxeToolId, MiddleTierAxeToolId, "(T)" + Constants.CobaltAxeId),
        new(BlessedPickaxeToolId, "(T)" + StygiumPickaxeToolId, MiddleTierPickaxeToolId, "(T)" + Constants.CobaltPickaxeId),
        new(BlessedHoeToolId, "(T)" + StygiumHoeToolId, MiddleTierHoeToolId, "(T)" + Constants.CobaltHoeId),
        new(BlessedWateringCanToolId, "(T)" + StygiumWateringCanToolId, MiddleTierWateringCanToolId, "(T)" + Constants.CobaltWateringCanId)
    };

    private static readonly UpgradeBridge[] UpgradeBridges =
    {
        new(TopTierAxeToolId, MiddleTierAxeId, BlessedAxeId),
        new(TopTierPickaxeToolId, MiddleTierPickaxeId, BlessedPickaxeId),
        new(TopTierHoeToolId, MiddleTierHoeId, BlessedHoeId),
        new(TopTierWateringCanToolId, MiddleTierWateringCanId, BlessedWateringCanId)
    };

    public static void Apply(IDictionary<string, ToolData> tools)
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

    private static void AddSwordAndSorceryTool(
        IDictionary<string, ToolData> tools,
        string id,
        string className,
        string name,
        string displayName,
        string description,
        string texture,
        int spriteIndex,
        int menuSpriteIndex,
        int upgradeLevel,
        string requireToolId,
        int price,
        string tradeItemId,
        int tradeItemAmount
    )
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

        if (targetUpgrade != null && templateUpgrade != null)
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
