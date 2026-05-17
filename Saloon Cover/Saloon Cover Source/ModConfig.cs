namespace ThaleTheGreat.SaloonCover;

internal sealed class ModConfig
{
    public int CoverCharge { get; set; } = 15;
    public int CoverStartsAt { get; set; } = 2000;
    public bool PayOncePerDay { get; set; } = true;
    public int BouncerTileX { get; set; } = 14;
    public int BouncerTileY { get; set; } = 23;
    public int EjectTileX { get; set; } = 45;
    public int EjectTileY { get; set; } = 57;
    public bool DebugLogging { get; set; } = false;
}
