using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;

namespace ThaleTheGreat.ToolAndSprinklerUpgrades;

internal enum UpgradeIdentity
{
    Cobalt,
    Prismatic,
    Radioactive
}

internal static class ToolTierUtility
{
    public static bool IsCoreUpgradeableTool(Tool tool)
    {
        return tool is Axe or Pickaxe or Hoe or WateringCan;
    }

    public static UpgradeIdentity GetIdentity(int level)
    {
        return GetIdentity(level, ModEntry.Config.RadioactiveBeforePrismatic);
    }

    public static UpgradeIdentity GetIdentity(int level, bool radioactiveBeforePrismatic)
    {
        return level switch
        {
            Constants.CobaltLevel => UpgradeIdentity.Cobalt,
            Constants.MiddleCustomLevel => radioactiveBeforePrismatic ? UpgradeIdentity.Radioactive : UpgradeIdentity.Prismatic,
            Constants.HighestCustomLevel => radioactiveBeforePrismatic ? UpgradeIdentity.Prismatic : UpgradeIdentity.Radioactive,
            _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unsupported custom tool level.")
        };
    }

    public static int GetLevel(UpgradeIdentity identity)
    {
        return identity switch
        {
            UpgradeIdentity.Cobalt => Constants.CobaltLevel,
            UpgradeIdentity.Prismatic => ModEntry.Config.RadioactiveBeforePrismatic ? Constants.HighestCustomLevel : Constants.MiddleCustomLevel,
            UpgradeIdentity.Radioactive => ModEntry.Config.RadioactiveBeforePrismatic ? Constants.MiddleCustomLevel : Constants.HighestCustomLevel,
            _ => 0
        };
    }

    public static string GetTierName(int level)
    {
        return GetTierName(level, ModEntry.Config.RadioactiveBeforePrismatic);
    }

    public static string GetTierName(int level, bool radioactiveBeforePrismatic)
    {
        return level is >= Constants.CobaltLevel and <= Constants.HighestCustomLevel
            ? GetIdentity(level, radioactiveBeforePrismatic).ToString()
            : string.Empty;
    }

    public static string GetTierKey(int level)
    {
        return GetIdentity(level).ToString().ToLowerInvariant();
    }

    public static string GetTextureAsset(int level)
    {
        if (level is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
            return string.Empty;

        return GetIdentity(level) switch
        {
            UpgradeIdentity.Cobalt => Constants.CobaltTextureAsset,
            UpgradeIdentity.Prismatic => Constants.PrismaticTextureAsset,
            UpgradeIdentity.Radioactive => Constants.RadioactiveTextureAsset,
            _ => string.Empty
        };
    }

    public static string GetPanAnimationTextureAsset(int level)
    {
        if (level is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
            return string.Empty;

        return GetIdentity(level) switch
        {
            UpgradeIdentity.Cobalt => Constants.CobaltPanAnimationTextureAsset,
            UpgradeIdentity.Prismatic => Constants.PrismaticPanAnimationTextureAsset,
            UpgradeIdentity.Radioactive => Constants.RadioactivePanAnimationTextureAsset,
            _ => string.Empty
        };
    }

    public static string GetSprinklerRecipeName(int level)
    {
        return GetTierName(level) + " Sprinkler";
    }

    public static string? GetBarRecipeName(int level)
    {
        if (level is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
            return null;

        return GetIdentity(level) == UpgradeIdentity.Prismatic ? "Prismatic Bar" : null;
    }

    public static float GetStaminaRefundRatio(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => 0.10f,
            Constants.MiddleCustomLevel => 0.20f,
            Constants.HighestCustomLevel => 0.40f,
            _ => 0f
        };
    }

    public static int GetFishingRodLevelBonus(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => 1,
            Constants.MiddleCustomLevel => 2,
            Constants.HighestCustomLevel => 3,
            _ => 0
        };
    }

    public static int GetUpgradeCost(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => ModEntry.Config.CobaltUpgradeCost,
            Constants.MiddleCustomLevel => ModEntry.Config.PrismaticUpgradeCost,
            Constants.HighestCustomLevel => ModEntry.Config.RadioactiveUpgradeCost,
            _ => 0
        };
    }

    public static int GetRequiredBars(int level)
    {
        return level switch
        {
            Constants.CobaltLevel => ModEntry.Config.CobaltBarsRequired,
            Constants.MiddleCustomLevel => ModEntry.Config.PrismaticBarsRequired,
            Constants.HighestCustomLevel => ModEntry.Config.RadioactiveBarsRequired,
            _ => 0
        };
    }

    public static string GetRequiredBarId(int level)
    {
        if (level is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
            return string.Empty;

        return GetIdentity(level) switch
        {
            UpgradeIdentity.Cobalt => Constants.CobaltBarId,
            UpgradeIdentity.Prismatic => Constants.PrismaticBarId,
            UpgradeIdentity.Radioactive => Constants.VanillaRadioactiveBarId,
            _ => string.Empty
        };
    }

    public static string GetRequiredBarName(int level)
    {
        if (level is < Constants.CobaltLevel or > Constants.HighestCustomLevel)
            return "bars";

        return GetIdentity(level) switch
        {
            UpgradeIdentity.Cobalt => "Cobalt Bars",
            UpgradeIdentity.Prismatic => "Prismatic Bars",
            UpgradeIdentity.Radioactive => "Radioactive Bars",
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
            case Constants.MiddleCustomLevel:
                length = ModEntry.Config.PrismaticToolLength;
                width = ModEntry.Config.PrismaticToolWidth;
                break;
            case Constants.HighestCustomLevel:
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
