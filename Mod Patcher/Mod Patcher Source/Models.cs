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

    public string FromVanillaUi { get; set; } = "";

    public int OutputWidth { get; set; } = 64;

    public int OutputHeight { get; set; } = 64;

    public string Name { get; set; } = "";

    public List<string> RequireAnySource { get; set; } = new();

    public List<string> RequireAnyTarget { get; set; } = new();

    public List<BridgeEndpoint> Sources { get; set; } = new();

    public List<BridgeEndpoint> Targets { get; set; } = new();

    public BridgeSettings? Bridge { get; set; }
}

internal sealed class RegisteredPatch
{
    public string PackId { get; set; } = "";

    public string LogName { get; set; } = "";

    public string TargetMod { get; set; } = "";

    public string TargetPath { get; set; } = "";

    public string SourcePath { get; set; } = "";

    public string TargetFullPath { get; set; } = "";

    public string FromVanillaUi { get; set; } = "";

    public int OutputWidth { get; set; } = 64;

    public int OutputHeight { get; set; } = 64;

    public bool IsGenerated => !string.IsNullOrWhiteSpace(this.FromVanillaUi);
}


internal sealed class BridgeEndpoint
{
    public string ModID { get; set; } = "";

    public string Kind { get; set; } = "";
}

internal sealed class BridgeSettings
{
    public string Kind { get; set; } = "ReflectionProxyBridge";

    public string UseCase { get; set; } = "RuntimeProxy";

    public string SourceRole { get; set; } = "Provider";

    public string TargetRole { get; set; } = "Consumer";

    public string Payload { get; set; } = "Item";

    public string Operation { get; set; } = "TemporaryProxy";

    public string ProxyMode { get; set; } = "HiddenAppendedSlot";

    public bool SyncBack { get; set; } = true;

    public object? Cleanup { get; set; } = "AfterUse";
}

internal sealed class RegisteredBridgePatch
{
    public string PackId { get; set; } = "";

    public string Name { get; set; } = "";

    public List<string> RequireAnySource { get; set; } = new();

    public List<string> RequireAnyTarget { get; set; } = new();

    public BridgeSettings Bridge { get; set; } = new();
}
