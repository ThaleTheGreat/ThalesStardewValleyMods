namespace ThaleTheGreat.MoonMisadventures;

internal sealed class SaveData
{
    public int SchemaVersion { get; set; } = 1;

    public bool EyeOfCthulhuDefeated { get; set; }
    public bool EyeOfCthulhuRewardClaimed { get; set; }
}
