using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal static class ToolTierUtility
{
    public static bool IsCoreUpgradeableTool(Tool tool)
    {
        return tool is Axe or Pickaxe or Hoe or WateringCan;
    }

    public static string GetTierName(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => "Cobalt",
            Constants.PrismaticLevel => "Prismatic",
            Constants.RadioactiveLevel => "Radioactive",
            _ => string.Empty
        };
    }

    public static float GetStaminaRefundRatio(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => 0.10f,
            Constants.PrismaticLevel => 0.20f,
            Constants.RadioactiveLevel => 0.40f,
            _ => 0f
        };
    }

    public static int GetFishingRodLevelBonus(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => 1,
            Constants.PrismaticLevel => 2,
            Constants.RadioactiveLevel => 3,
            _ => 0
        };
    }

    public static int GetUpgradeCost(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => ModEntry.Config.CobaltUpgradeCost,
            Constants.PrismaticLevel => ModEntry.Config.PrismaticUpgradeCost,
            Constants.RadioactiveLevel => ModEntry.Config.RadioactiveUpgradeCost,
            _ => 0
        };
    }

    public static int GetRequiredBars(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => ModEntry.Config.CobaltBarsRequired,
            Constants.PrismaticLevel => ModEntry.Config.PrismaticBarsRequired,
            Constants.RadioactiveLevel => ModEntry.Config.RadioactiveBarsRequired,
            _ => 0
        };
    }

    public static string GetRequiredBarId(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => Constants.CobaltBarId,
            Constants.PrismaticLevel => Constants.PrismaticBarId,
            Constants.RadioactiveLevel => Constants.VanillaRadioactiveBarId,
            _ => string.Empty
        };
    }

    public static string GetRequiredBarName(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => "Cobalt Bars",
            Constants.PrismaticLevel => "Prismatic Bars",
            Constants.RadioactiveLevel => "Radioactive Bars",
            _ => "bars"
        };
    }

    public static string GetDisplayName(Tool tool)
    {
        string tier = GetTierName(tool.UpgradeLevel);
        if (string.IsNullOrWhiteSpace(tier))
            return tool.BaseName;

        return tool switch
        {
            WateringCan => tier + " Watering Can",
            Axe => tier + " Axe",
            Pickaxe => tier + " Pickaxe",
            Hoe => tier + " Hoe",
            Pan => tier + " Pan",
            FishingRod => tier + " Rod",
            _ => tier + " " + tool.BaseName
        };
    }

    public static void GetAreaShape(int level, out int length, out int width)
    {
        switch (level)
        {
            case Constants.CobaltLevel:
                length = ModEntry.Config.CobaltToolLength;
                width = ModEntry.Config.CobaltToolWidth;
                break;
            case Constants.PrismaticLevel:
                length = ModEntry.Config.PrismaticToolLength;
                width = ModEntry.Config.PrismaticToolWidth;
                break;
            case Constants.RadioactiveLevel:
                length = ModEntry.Config.RadioactiveToolLength;
                width = ModEntry.Config.RadioactiveToolWidth;
                break;
            default:
                length = 0;
                width = 0;
                break;
        }
    }

    public static void AddRectangleTiles(List<Vector2> result, Vector2 tileLocation, Farmer who, int length, int width)
    {
        Vector2 direction;
        Vector2 orthogonal;
        switch (who.FacingDirection)
        {
            case 0:
                direction = new Vector2(0, -1);
                orthogonal = new Vector2(1, 0);
                break;
            case 1:
                direction = new Vector2(1, 0);
                orthogonal = new Vector2(0, 1);
                break;
            case 2:
                direction = new Vector2(0, 1);
                orthogonal = new Vector2(-1, 0);
                break;
            case 3:
                direction = new Vector2(-1, 0);
                orthogonal = new Vector2(0, -1);
                break;
            default:
                direction = Vector2.Zero;
                orthogonal = Vector2.Zero;
                break;
        }

        for (int i = 0; i < length; i++)
        {
            result.Add(tileLocation + direction * i);
            for (int j = 1; j <= width; j++)
            {
                result.Add(tileLocation + direction * i + orthogonal * j);
                result.Add(tileLocation + direction * i - orthogonal * j);
            }
        }
    }
}
