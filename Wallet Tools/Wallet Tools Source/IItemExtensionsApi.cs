namespace ThaleTheGreat.WalletTools;

public interface IItemExtensionsApi
{
    bool GetBreakingTool(string id, bool isClump, out string tool);
}
