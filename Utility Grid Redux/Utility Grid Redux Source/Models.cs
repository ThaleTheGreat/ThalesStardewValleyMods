using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace ThaleTheGreat.UtilityGridRedux;

internal enum GridKind
{
    Water,
    Power
}

internal sealed class GridPipe
{
    public int Index { get; set; }
    public int Rotation { get; set; }
}

internal sealed class PipeGroup
{
    public List<Vector2> Pipes { get; } = new();
    public HashSet<Vector2> PipeSet { get; } = new();
    public Vector2 PowerVector { get; set; }
    public Vector2 StorageVector { get; set; }

    public void AddPipe(Vector2 tile)
    {
        if (PipeSet.Add(tile))
            Pipes.Add(tile);
    }
}

internal sealed class UtilityObjectRule
{
    public float Water { get; set; }
    public float Power { get; set; }
    public bool MustBeOn { get; set; }
    public bool OnlyMorning { get; set; }
    public bool OnlyDay { get; set; }
    public bool OnlyNight { get; set; }
    public bool MustBeFull { get; set; }
    public bool MustNeedOther { get; set; }
    public string? MustContain { get; set; }
    public bool MustBeWorking { get; set; }
    public bool OnlyInWater { get; set; }
    public bool MustHaveSun { get; set; }
    public bool MustHaveRain { get; set; }
    public bool MustHaveLightning { get; set; }
    public int WaterChargeCapacity { get; set; }
    public int PowerChargeCapacity { get; set; }
    public float WaterChargeRate { get; set; }
    public float PowerChargeRate { get; set; }
    public float WaterDischargeRate { get; set; }
    public float PowerDischargeRate { get; set; }
    public bool FillWaterFromRain { get; set; }
}

internal sealed class UtilityGridContentPackData
{
    public List<UtilityGridContentPackRule> MachineRules { get; set; } = new();
}

internal sealed class UtilityGridContentPackRule
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public List<string> Keys { get; set; } = new();
    public int? WaterProduced { get; set; }
    public int? WaterConsumed { get; set; }
    public int? PowerProduced { get; set; }
    public int? PowerConsumed { get; set; }
    public int WaterChargeCapacity { get; set; }
    public int PowerChargeCapacity { get; set; }
    public int? WaterChargeConsumed { get; set; }
    public int? PowerChargeConsumed { get; set; }
    public int? WaterDischargeProduced { get; set; }
    public int? PowerDischargeProduced { get; set; }
    public bool MustBeOn { get; set; }
    public bool OnlyMorning { get; set; }
    public bool OnlyDay { get; set; }
    public bool OnlyNight { get; set; }
    public bool MustBeFull { get; set; }
    public bool MustNeedOther { get; set; }
    public string? MustContain { get; set; }
    public bool MustBeWorking { get; set; }
    public bool OnlyInWater { get; set; }
    public bool MustHaveSun { get; set; }
    public bool MustHaveRain { get; set; }
    public bool MustHaveLightning { get; set; }
    public bool FillWaterFromRain { get; set; }
    public bool AddTooltip { get; set; } = true;
}

internal sealed class LoadedContentPackRule
{
    public string SourceName { get; set; } = string.Empty;
    public string SourceUniqueId { get; set; } = string.Empty;
    public string ConfigKey { get; set; } = string.Empty;
    public UtilityGridContentPackRule Rule { get; set; } = new();
}

internal sealed class ContentPackRuleConfig
{
    public bool Enabled { get; set; } = true;
    public int? WaterProduced { get; set; }
    public int? WaterConsumed { get; set; }
    public int? PowerProduced { get; set; }
    public int? PowerConsumed { get; set; }
}

internal sealed class UtilityObjectInstance
{
    public UtilityObjectInstance(UtilityObjectRule template, StardewValley.Object worldObject)
    {
        Template = template;
        WorldObject = worldObject;
    }

    public PipeGroup? Group { get; set; }
    public UtilityObjectRule Template { get; }
    public StardewValley.Object WorldObject { get; }
}

internal sealed class UtilitySystem
{
    public Dictionary<Vector2, GridPipe> Pipes { get; } = new();
    public List<PipeGroup> Groups { get; } = new();
    public Dictionary<Vector2, UtilityObjectInstance> Objects { get; } = new();
    public bool GroupsDirty { get; set; } = true;
    public long PowerCacheVersion { get; set; } = -1;
}


internal sealed class UtilitySystemSaveData
{
    public Dictionary<string, LocationGridSaveData> Locations { get; set; } = new();
}

internal sealed class LocationGridSaveData
{
    public List<int[]> WaterPipes { get; set; } = new();
    public List<int[]> PowerPipes { get; set; } = new();

    public LocationGridSaveData()
    {
    }

    public LocationGridSaveData(Dictionary<Vector2, GridPipe> waterPipes, Dictionary<Vector2, GridPipe> powerPipes)
    {
        foreach ((Vector2 tile, GridPipe pipe) in waterPipes)
            WaterPipes.Add(new int[] { (int)tile.X, (int)tile.Y, pipe.Index, pipe.Rotation });

        foreach ((Vector2 tile, GridPipe pipe) in powerPipes)
            PowerPipes.Add(new int[] { (int)tile.X, (int)tile.Y, pipe.Index, pipe.Rotation });
    }
}

internal sealed class ModConfig
{
    public bool EnableMod { get; set; } = true;
    public bool EnablePowerRules { get; set; } = true;
    public bool EnableBronzeWaterPumpRule { get; set; } = true;
    public bool EnableSteelWaterPumpRule { get; set; } = true;
    public bool EnableGoldWaterPumpRule { get; set; } = true;
    public bool EnableIridiumWaterPumpRule { get; set; } = true;
    public bool EnableUtilityGridBatteryRule { get; set; } = true;
    public bool EnableUtilityGridAdvancedBatteryRule { get; set; } = true;
    public bool EnableUtilityGridWaterTankRule { get; set; } = true;
    public bool EnableSprinklerRule { get; set; } = true;
    public bool EnableQualitySprinklerRule { get; set; } = true;
    public bool EnableIridiumSprinklerRule { get; set; } = true;
    public bool EnableCobaltSprinklerRule { get; set; } = true;
    public bool EnablePrismaticSprinklerRule { get; set; } = true;
    public bool EnableRadioactiveSprinklerRule { get; set; } = true;
    public bool EnableFurnaceRule { get; set; } = true;
    public bool EnableCharcoalKilnRule { get; set; } = true;
    public bool EnableSolarPanelRule { get; set; } = true;
    public bool EnableCrystalariumRule { get; set; } = true;
    public bool EnableRecyclingMachineRule { get; set; } = true;
    public bool EnableSlimeIncubatorRule { get; set; } = true;
    public bool EnableWoodChipperRule { get; set; } = true;
    public bool EnableOstrichIncubatorRule { get; set; } = true;
    public bool EnableDeconstructorRule { get; set; } = true;
    public bool EnableSodaMachineRule { get; set; } = true;
    public bool EnableCoffeeMakerRule { get; set; } = true;
    public bool EnableHeavyFurnaceRule { get; set; } = true;
    public bool EnableGeodeCrusherRule { get; set; } = true;
    public bool EnableMiniForgeRule { get; set; } = true;
    public bool EnableBaitMakerRule { get; set; } = true;
    public bool EnableBoneMillRule { get; set; } = true;
    public bool EnableSlimeEggPressRule { get; set; } = true;
    public bool EnableSeedMakerRule { get; set; } = true;
    public bool EnableDehydratorRule { get; set; } = true;
    public bool EnableFishSmokerRule { get; set; } = true;
    public bool EnableOilMakerRule { get; set; } = true;
    public bool EnableLoomRule { get; set; } = true;
    public bool EnableMayonnaiseMachineRule { get; set; } = true;
    public bool EnableCheesePressRule { get; set; } = true;
    public bool EnableHopperRule { get; set; } = true;
    public bool EnableFarmComputerRule { get; set; } = true;
    public bool EnableTelephoneRule { get; set; } = true;
    public bool EnableSewingMachineRule { get; set; } = true;
    public bool EnableMiniJukeboxRule { get; set; } = true;
    public int BronzeWaterPumpWaterProduced { get; set; } = 10;
    public int BronzeWaterPumpPowerConsumed { get; set; } = 2;
    public int SteelWaterPumpWaterProduced { get; set; } = 25;
    public int SteelWaterPumpPowerConsumed { get; set; } = 4;
    public int GoldWaterPumpWaterProduced { get; set; } = 80;
    public int GoldWaterPumpPowerConsumed { get; set; } = 8;
    public int IridiumWaterPumpWaterProduced { get; set; } = 200;
    public int IridiumWaterPumpPowerConsumed { get; set; } = 16;
    public int UtilityGridBatteryPowerProduced { get; set; } = 2;
    public int UtilityGridBatteryPowerConsumed { get; set; } = 2;
    public int UtilityGridAdvancedBatteryPowerProduced { get; set; } = 5;
    public int UtilityGridAdvancedBatteryPowerConsumed { get; set; } = 5;
    public int UtilityGridWaterTankWaterProduced { get; set; } = 2;
    public int UtilityGridWaterTankWaterConsumed { get; set; } = 2;
    public int SprinklerWaterConsumed { get; set; } = 1;
    public int QualitySprinklerWaterConsumed { get; set; } = 2;
    public int IridiumSprinklerWaterConsumed { get; set; } = 6;
    public int IridiumSprinklerPowerConsumed { get; set; } = 2;
    public int CobaltSprinklerWaterConsumed { get; set; } = 10;
    public int CobaltSprinklerPowerConsumed { get; set; } = 3;
    public int PrismaticSprinklerWaterConsumed { get; set; } = 25;
    public int PrismaticSprinklerPowerConsumed { get; set; } = 6;
    public int RadioactiveSprinklerWaterConsumed { get; set; } = 50;
    public int RadioactiveSprinklerPowerConsumed { get; set; } = 10;
    public int FurnacePowerProduced { get; set; } = 10;
    public int CharcoalKilnPowerProduced { get; set; } = 10;
    public int SolarPanelPowerProduced { get; set; } = 10;
    public int CrystalariumPowerConsumed { get; set; } = 3;
    public int RecyclingMachinePowerConsumed { get; set; } = 1;
    public int SlimeIncubatorPowerConsumed { get; set; } = 2;
    public int WoodChipperPowerConsumed { get; set; } = 2;
    public int OstrichIncubatorPowerConsumed { get; set; } = 2;
    public int DeconstructorPowerConsumed { get; set; } = 2;
    public int SodaMachinePowerConsumed { get; set; } = 1;
    public int CoffeeMakerPowerConsumed { get; set; } = 1;
    public int HeavyFurnacePowerConsumed { get; set; } = 4;
    public int GeodeCrusherPowerConsumed { get; set; } = 2;
    public int MiniForgePowerConsumed { get; set; } = 3;
    public int BaitMakerPowerConsumed { get; set; } = 1;
    public int BoneMillPowerConsumed { get; set; } = 2;
    public int SlimeEggPressPowerConsumed { get; set; } = 2;
    public int SeedMakerPowerConsumed { get; set; } = 1;
    public int DehydratorPowerConsumed { get; set; } = 2;
    public int FishSmokerPowerConsumed { get; set; } = 2;
    public int OilMakerPowerConsumed { get; set; } = 2;
    public int LoomPowerConsumed { get; set; } = 1;
    public int MayonnaiseMachinePowerConsumed { get; set; } = 1;
    public int CheesePressPowerConsumed { get; set; } = 1;
    public int HopperPowerConsumed { get; set; } = 1;
    public int FarmComputerPowerConsumed { get; set; } = 1;
    public int TelephonePowerConsumed { get; set; } = 1;
    public int SewingMachinePowerConsumed { get; set; } = 1;
    public int MiniJukeboxPowerConsumed { get; set; } = 1;
    public Dictionary<string, ContentPackRuleConfig> ContentPackMachineRules { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public bool EnablePipeIrrigation { get; set; } = false;
    public bool WaterSurroundingTiles { get; set; } = true;
    public bool ShowSprinklerAnimations { get; set; } = true;
    public bool ShowWateredTileMarkers { get; set; } = true;
    public bool DebugLogging { get; set; }
    public KeybindList ToggleGridOverlay { get; set; } = new(SButton.Back);
    public KeybindList ToggleGrid { get; set; } = new(SButton.Home);
    public KeybindList SwitchGrid { get; set; } = new(SButton.Delete);
    public KeybindList SwitchTile { get; set; } = new(SButton.PageUp);
    public KeybindList RotateTile { get; set; } = new(SButton.PageDown);
    public KeybindList PlaceTile { get; set; } = new(SButton.MouseLeft);
    public KeybindList DestroyTile { get; set; } = new(SButton.MouseRight);
    public Color WaterColor { get; set; } = Color.Aqua;
    public Color UnpoweredGridColor { get; set; } = Color.White;
    public Color PowerColor { get; set; } = Color.Yellow;
    public Color InsufficientColor { get; set; } = Color.Red;
    public Color IdleColor { get; set; } = Color.LightGray;
    public Color ShadowColor { get; set; } = Color.Black;
    public int PipeCostGold { get; set; } = 100;
    public int PipeDestroyGold { get; set; } = 50;
    public string PipeCostItems { get; set; } = "(O)378:2";
    public string PipeDestroyItems { get; set; } = "(O)378:1";
    public string PipeSound { get; set; } = "dirtyHit";
    public string DestroySound { get; set; } = "axe";
    public float PercentWaterPerTile { get; set; } = 0.25f;
}

internal enum PipeActionKind
{
    Place,
    Destroy
}

internal sealed class PipeActionRequest
{
    public string LocationName { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public GridKind Grid { get; set; }
    public int Index { get; set; }
    public int Rotation { get; set; }
    public PipeActionKind Action { get; set; }
    public bool FromGridEditMode { get; set; }
}

internal sealed class PipeActionBroadcast
{
    public string LocationName { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public GridKind Grid { get; set; }
    public int Index { get; set; }
    public int Rotation { get; set; }
    public PipeActionKind Action { get; set; }
}

internal sealed class GridSyncRequest
{
}

internal sealed class PipeActionRejection
{
    public string Message { get; set; } = string.Empty;
}

internal sealed class GridSyncMessage
{
    public UtilitySystemSaveData? Data { get; set; }
}
