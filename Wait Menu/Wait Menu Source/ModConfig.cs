using StardewModdingAPI.Utilities;

namespace WaitMenu;

public sealed class ModConfig
{
    public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("LeftControl + W");

    public bool AllowDuringFestival { get; set; } = false;

    public bool AllowDuringEvents { get; set; } = false;

    public int MaxWaitMinutes { get; set; } = 240;

    public bool StabilizeAfterTeleport { get; set; } = true;
}
