using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace ThaleTheGreat.AssetPatcher;

public sealed class AssetPatcherContentFile
{
    public string Format { get; set; } = "1.0.0";
    public List<AssetPatchRule> Replacements { get; set; } = new();
    public List<AssetPatchRule>? Changes { get; set; }
}

public sealed class AssetPatchRule
{
    public string? LogName { get; set; }
    public string Action { get; set; } = "ReplaceFile";
    public string TargetMod { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string FromFile { get; set; } = string.Empty;
    public string BackupSuffix { get; set; } = ".original";
    public bool? CreateBackup { get; set; }
    public bool? ReapplyWhenTargetChanged { get; set; }
    public AssetPatchConditions? When { get; set; }
}

public sealed class AssetPatchConditions
{
    [JsonConverter(typeof(StringOrStringArrayConverter))]
    public string[]? HasMod { get; set; }

    [JsonConverter(typeof(StringOrStringArrayConverter))]
    public string[]? NotHasMod { get; set; }
}

public sealed class BackupRegistry
{
    public List<BackupRecord> Backups { get; set; } = new();
}

public sealed class BackupRecord
{
    public string TargetMod { get; set; } = string.Empty;
    public string TargetPath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string BackupSuffix { get; set; } = ".original";
}
