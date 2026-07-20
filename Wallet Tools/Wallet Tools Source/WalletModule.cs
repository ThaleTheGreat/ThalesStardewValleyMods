using StardewModdingAPI;

namespace ThaleTheGreat.WalletTools;

internal abstract class WalletModule
{
    protected ModEntry Host { get; }
    protected IModHelper Helper => Host.ModHelper;
    protected IMonitor Monitor => Host.ModMonitor;
    protected IManifest ModManifest => Host.Manifest;

    internal string Key { get; }
    internal string DisplayName { get; }
    internal string LegacyStandaloneUniqueId { get; }
    internal bool IsActive { get; private set; }
    internal virtual bool HasConfigPage => true;
    private string[] RequiredModIds { get; }

    protected WalletModule(ModEntry host, string key, string displayName, string legacyUniqueId, params string[] requiredModIds)
    {
        Host = host;
        Key = key;
        DisplayName = displayName;
        LegacyStandaloneUniqueId = legacyUniqueId;
        RequiredModIds = requiredModIds;
    }

    internal void InitializeSafely()
    {
        string? conflictingModId = GetConflictingStandaloneModId();
        if (!string.IsNullOrWhiteSpace(conflictingModId))
        {
            Monitor.Log($"The standalone mod '{conflictingModId}' is installed, so Wallet Tools' integrated {DisplayName} module was disabled to prevent duplicate patches and save handling. Remove the standalone mod only after returning any wallet-held item it owns to the inventory.", LogLevel.Warn);
            return;
        }

        foreach (string requiredModId in RequiredModIds)
        {
            if (!Helper.ModRegistry.IsLoaded(requiredModId))
                return;
        }

        try
        {
            Initialize();
            IsActive = true;
        }
        catch (System.Exception ex)
        {
            Monitor.Log($"The integrated {DisplayName} module could not initialize and was disabled: {ex}", LogLevel.Error);
        }
    }


    internal virtual string? GetConflictingStandaloneModId()
    {
        return !string.IsNullOrWhiteSpace(LegacyStandaloneUniqueId) && Helper.ModRegistry.IsLoaded(LegacyStandaloneUniqueId)
            ? LegacyStandaloneUniqueId
            : null;
    }

    internal abstract void Initialize();

    internal void OnGameLaunchedSafely()
    {
        if (!IsActive)
            return;

        try
        {
            OnGameLaunched();
        }
        catch (System.Exception ex)
        {
            Monitor.Log($"The integrated {DisplayName} module failed during game launch and was disabled: {ex}", LogLevel.Error);
            IsActive = false;
        }
    }

    internal virtual void OnGameLaunched()
    {
    }
}
