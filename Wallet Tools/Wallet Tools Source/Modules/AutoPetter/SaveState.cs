namespace ThaleTheGreat.WalletAutoPetter;

internal sealed class SaveState
{
    public bool HasAutoPetter { get; set; }
    public int AutoPetterCount { get; set; }
    public bool SuppressAutoStoreUntilInventoryAutoPetterLeaves { get; set; }
}

