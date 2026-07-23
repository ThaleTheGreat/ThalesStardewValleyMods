namespace ThaleTheGreat.MoonMisadventures;

public interface IWalletToolsApi
{
    int GetHighestStoredCoreToolLevel();
    int GetStoredToolLevel(string toolKind);
    bool TryGetStoredToolQualifiedItemId(string toolKind, out string qualifiedItemId);
    bool TryGetStoredTool(string toolKind, out string qualifiedItemId, out int upgradeLevel, out string displayName);
    bool IsToolStored(string toolKind);
    bool IsToolAutoUseEnabled(string toolKind);
    string[] GetStoredToolKinds();
}
