namespace ThaleTheGreat.AssetPatcher;

public sealed class ModConfig
{
    public bool EnableAssetPatching { get; set; } = true;
    public bool CreateBackups { get; set; } = true;
    public bool ReapplyWhenTargetChanged { get; set; } = true;
    public bool DebugLogging { get; set; } = false;
    public string SelectedBackup { get; set; } = string.Empty;
}
