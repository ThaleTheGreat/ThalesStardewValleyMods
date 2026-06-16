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
