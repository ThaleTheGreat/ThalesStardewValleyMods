using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletScepter;

internal sealed class ModConfig
{
    public KeybindList UseReturnScepterKey { get; set; } = KeybindList.Parse("LeftControl + S");

    public bool EnableAutomaticReturnHomeAt2500 { get; set; } = true;

    public bool AskBeforeAutomaticReturnHomeAt2500 { get; set; } = true;

    public bool RequireWalletUnlock { get; set; } = true;

    public bool ShowHudMessageWhenMissing { get; set; } = true;

}
