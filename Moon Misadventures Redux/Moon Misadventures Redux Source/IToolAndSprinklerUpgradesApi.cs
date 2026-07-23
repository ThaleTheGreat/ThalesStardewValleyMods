namespace ThaleTheGreat.MoonMisadventures;

public interface IToolAndSprinklerUpgradesApi
{
    bool IsPrismaticHighest { get; }

    bool RegisterAlternativeLevelFiveCoreTools(
        string ownerModId,
        string tierId,
        string textureAsset,
        string barItemId,
        string axeId,
        string axeDisplayName,
        string pickaxeId,
        string pickaxeDisplayName,
        string hoeId,
        string hoeDisplayName,
        string wateringCanId,
        string wateringCanDisplayName
    );

    bool RegisterAlternativeLevelSixCoreTools(
        string ownerModId,
        string tierId,
        string textureAsset,
        string barItemId,
        string axeId,
        string axeDisplayName,
        string pickaxeId,
        string pickaxeDisplayName,
        string hoeId,
        string hoeDisplayName,
        string wateringCanId,
        string wateringCanDisplayName
    );

    bool SetAlternativeTierBarTransmutationEnabled(
        string ownerModId,
        string tierId,
        bool enabled
    );
}
