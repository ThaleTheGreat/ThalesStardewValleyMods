namespace ThaleTheGreat.ModPatcher;

internal sealed class PatchContentFile
{
    public List<AssetPatchChange> Changes { get; set; } = new();
}

internal sealed class AssetPatchChange
{
    public string? LogName { get; set; }

    public string Action { get; set; } = "PatchMod";

    public string TargetMod { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string FromFile { get; set; } = "";
}

internal sealed class RegisteredPatch
{
    public string PackId { get; set; } = "";

    public string LogName { get; set; } = "";

    public string TargetMod { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string TargetFullPath { get; set; } = "";
}
