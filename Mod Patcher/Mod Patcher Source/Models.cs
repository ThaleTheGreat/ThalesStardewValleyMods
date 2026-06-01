using StardewModdingAPI;

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

    public string Type { get; set; } = "";

    public string Method { get; set; } = "";

    public bool PatchAllOverloads { get; set; }

    public RuntimePrefix? Prefix { get; set; }

    public List<string> RequireAnyTarget { get; set; } = new();

    public List<BridgeEndpoint> Sources { get; set; } = new();

    public List<BridgeEndpoint> Targets { get; set; } = new();

    public BridgeSettings? Bridge { get; set; }

    public InteractionWhen? When { get; set; }

    public InteractionLimit? Limit { get; set; }

    public List<InteractionEffect> Effects { get; set; } = new();

    public List<InteractionConfigOption> Config { get; set; } = new();
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
    public string UniqueID { get; set; } = "";

    public string Type { get; set; } = "";

    public string Method { get; set; } = "";

    public string Field { get; set; } = "";

    public string Property { get; set; } = "";
}

internal sealed class BridgeSettings
{
    public string ProxyMode { get; set; } = "HiddenAppendedSlot";

    public bool PreferMainInventory { get; set; } = true;

    public bool SyncBack { get; set; } = true;

    public object? Cleanup { get; set; } = "AfterUse";
}

internal sealed class RuntimePrefix
{
    public object? Return { get; set; }

    public bool SkipOriginal { get; set; }
}

internal sealed class RegisteredRuntimeMethodPatch
{
    public string PackId { get; set; } = "";

    public string Name { get; set; } = "";

    public string Type { get; set; } = "";

    public string Method { get; set; } = "";

    public bool PatchAllOverloads { get; set; }

    public RuntimePrefix Prefix { get; set; } = new();
}

internal sealed class RegisteredRuntimeProxyPatch
{
    public string PackId { get; set; } = "";

    public string Name { get; set; } = "";

    public List<string> RequireAnySource { get; set; } = new();

    public List<string> RequireAnyTarget { get; set; } = new();

    public BridgeSettings Bridge { get; set; } = new();
}

internal sealed class InteractionWhen
{
    public string Input { get; set; } = "UseToolButton";

    public string Event { get; set; } = "ButtonPressed";

    public string Location { get; set; } = "";

    public string TimeAtOrAfter { get; set; } = "";

    public Dictionary<string, string> DayOfWeekConfig { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public int DelayTicks { get; set; }

    public string HeldToolType { get; set; } = "";

    public InteractionTarget Target { get; set; } = new();
}

internal sealed class InteractionTarget
{
    public string Type { get; set; } = "NPC";

    public List<string> Names { get; set; } = new();

    public float MaxTileDistance { get; set; } = 1.5f;
}

internal sealed class InteractionLimit
{
    public bool OncePerDay { get; set; }

    public string Key { get; set; } = "";

    public string Config { get; set; } = "";
}

internal sealed class InteractionEffect
{
    public string Type { get; set; } = "";

    public string Target { get; set; } = "MatchedNPC";

    public int Amount { get; set; }

    public string AmountFromConfig { get; set; } = "";

    public string Text { get; set; } = "";

    public string Config { get; set; } = "";

    public string Key { get; set; } = "";

    public string Location { get; set; } = "";

    public string TileX { get; set; } = "";

    public string TileY { get; set; } = "";

    public int Facing { get; set; } = 2;

    public string Texture { get; set; } = "";

    public int SourceX { get; set; }

    public int SourceY { get; set; }

    public int SourceWidth { get; set; } = 16;

    public int SourceHeight { get; set; } = 16;

    public bool PixelZoom { get; set; } = true;

    public string PayKey { get; set; } = "Pay";

    public string LeaveKey { get; set; } = "Leave";

    public string PayText { get; set; } = "Pay";

    public string LeaveText { get; set; } = "Leave";

    public List<InteractionEffect> WhenTrue { get; set; } = new();

    public List<InteractionEffect> WhenFalse { get; set; } = new();

    public List<InteractionEffect> OnPaid { get; set; } = new();

    public List<InteractionEffect> OnCannotPay { get; set; } = new();

    public List<InteractionEffect> OnDeclined { get; set; } = new();
}

internal sealed class InteractionConfigOption
{
    public string Key { get; set; } = "";

    public string Type { get; set; } = "Bool";

    public bool Default { get; set; }

    public int DefaultNumber { get; set; }

    public int Min { get; set; }

    public int Max { get; set; } = 100;

    public int Interval { get; set; } = 1;

    public string Name { get; set; } = "";

    public string Tooltip { get; set; } = "";
}

internal sealed class RegisteredInteractionPatch
{
    public string PackId { get; set; } = "";

    public string Name { get; set; } = "";

    public IContentPack Pack { get; set; } = null!;

    public InteractionWhen When { get; set; } = new();

    public InteractionLimit Limit { get; set; } = new();

    public List<InteractionEffect> Effects { get; set; } = new();

    public List<InteractionConfigOption> Config { get; set; } = new();
}

internal sealed class PendingInteraction
{
    public RegisteredInteractionPatch Patch { get; set; } = null!;

    public int Ticks { get; set; }
}

internal sealed class InteractionConfigValue
{
    public bool Bool { get; set; }

    public int Number { get; set; }
}
