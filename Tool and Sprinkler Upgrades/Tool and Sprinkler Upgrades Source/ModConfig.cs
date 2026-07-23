namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal sealed class ModConfig
{
    public bool RadioactiveBeforePrismatic { get; set; } = true;
    public int CobaltUpgradeCost { get; set; } = 75000;
    public int PrismaticUpgradeCost { get; set; } = 150000;
    public int RadioactiveUpgradeCost { get; set; } = 250000;
    public int CobaltBarsRequired { get; set; } = 5;
    public int PrismaticBarsRequired { get; set; } = 5;
    public int RadioactiveBarsRequired { get; set; } = 5;
    public int CobaltToolLength { get; set; } = 5;
    public int CobaltToolWidth { get; set; } = 2;
    public int PrismaticToolLength { get; set; } = 6;
    public int PrismaticToolWidth { get; set; } = 2;
    public int RadioactiveToolLength { get; set; } = 7;
    public int RadioactiveToolWidth { get; set; } = 2;
    public int CobaltSprinklerRange { get; set; } = 3;
    public int PrismaticSprinklerRange { get; set; } = 5;
    public int RadioactiveSprinklerRange { get; set; } = 7;
    public bool RadioactiveSprinklerActsAsScarecrow { get; set; } = true;
    public bool PreventPressureNozzleFromMatchingNextBaseTier { get; set; } = true;
    public bool EnableDebugLogging { get; set; } = false;
}
