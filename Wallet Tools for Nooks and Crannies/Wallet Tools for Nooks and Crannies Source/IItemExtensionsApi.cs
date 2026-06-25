namespace ThaleTheGreat.WalletToolsForNooksAndCrannies;

public interface IItemExtensionsApi
{
    bool GetBreakingTool(string id, bool isClump, out string tool);
}
