namespace TheAmazingDeathicornRedux;

public sealed class ModConfig
{
    public bool EnableHornLasers { get; set; } = true;
    public bool EnableDeathicornGlow { get; set; } = true;
    public float GlowRadius { get; set; } = 5f;
    public LaserColor GlowColor { get; set; } = LaserColor.Prismatic;
    public float LaserIntervalSeconds { get; set; } = 2f;
    public int RangeTiles { get; set; } = 5;
    public LaserColor BoltCoreColor { get; set; } = LaserColor.Prismatic;
    public LaserColor BoltGlowColor { get; set; } = LaserColor.Prismatic;
    public LaserColor ImpactSplashColor { get; set; } = LaserColor.Prismatic;
}
