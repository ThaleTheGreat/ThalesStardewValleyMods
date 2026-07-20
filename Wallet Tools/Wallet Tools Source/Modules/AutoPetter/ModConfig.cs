namespace ThaleTheGreat.WalletAutoPetter;

internal sealed class ModConfig
{
    public bool Enabled { get; set; } = true;
    public bool AutoStoreFromInventory { get; set; } = true;
    public bool ApplyFriendshipGain { get; set; } = true;
    public int FriendshipPointsPerDay { get; set; } = 7;
    public bool ShowStoredMessage { get; set; } = true;
    public bool ShowWalletIcon { get; set; } = true;
}
