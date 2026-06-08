using System;

namespace ThaleTheGreat.WalletTools;

public sealed class WalletToolsApi : IWalletToolsApi
{
    private readonly ModEntry Mod;

    public WalletToolsApi(ModEntry mod)
    {
        Mod = mod;
    }

    public int GetHighestStoredCoreToolLevel()
    {
        return Mod.GetHighestStoredCoreToolLevelForApi();
    }

    public int GetStoredToolLevel(string toolKind)
    {
        return Mod.TryGetStoredToolForApi(toolKind, out WalletToolState state) ? state.GetPowerScore() : 0;
    }

    public bool TryGetStoredToolQualifiedItemId(string toolKind, out string qualifiedItemId)
    {
        qualifiedItemId = string.Empty;
        if (!Mod.TryGetStoredToolForApi(toolKind, out WalletToolState state))
            return false;

        qualifiedItemId = state.QualifiedItemId;
        return !string.IsNullOrWhiteSpace(qualifiedItemId);
    }

    public bool TryGetStoredTool(string toolKind, out string qualifiedItemId, out int upgradeLevel, out string displayName)
    {
        qualifiedItemId = string.Empty;
        upgradeLevel = 0;
        displayName = string.Empty;

        if (!Mod.TryGetStoredToolForApi(toolKind, out WalletToolState state))
            return false;

        qualifiedItemId = state.QualifiedItemId;
        upgradeLevel = state.GetPowerScore();
        displayName = state.GetDisplayName();
        return true;
    }
}
