namespace ThaleTheGreat.SaloonCover;

internal sealed class ModConfig
{
    public int CoverCharge { get; set; } = 15;
    public int CoverStartsAt { get; set; } = 2000;
    public bool CoverOnSunday { get; set; } = true;
    public bool CoverOnMonday { get; set; } = true;
    public bool CoverOnTuesday { get; set; } = true;
    public bool CoverOnWednesday { get; set; } = true;
    public bool CoverOnThursday { get; set; } = true;
    public bool CoverOnFriday { get; set; } = true;
    public bool CoverOnSaturday { get; set; } = true;
    public bool PayOncePerDay { get; set; } = true;
    public int BouncerTileX { get; set; } = 14;
    public int BouncerTileY { get; set; } = 23;
    public int EjectTileX { get; set; } = 45;
    public int EjectTileY { get; set; } = 57;
    public bool DebugLogging { get; set; } = false;
}
