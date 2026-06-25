using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletToolsForNooksAndCrannies;

public interface IGenericModConfigMenuApi
{
    void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
    void Unregister(IManifest mod);
    void OpenModMenu(IManifest mod);
    void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
    void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null);
}
