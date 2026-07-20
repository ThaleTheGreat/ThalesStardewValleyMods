using System;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace ThaleTheGreat.WalletTools;

internal sealed class UnifiedGmcmApiAdapter :
    ThaleTheGreat.WalletAutoPetter.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletScepter.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletToolsForAnimalHusbandry.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletToolsForCoinCollectorRedux.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletToolsForNatureInTheValley.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletToolsForSwordAndSorcery.IGenericModConfigMenuApi,
    ThaleTheGreat.WalletToolsForTractorMod.IGenericModConfigMenuApi
{
    private readonly ModEntry Host;
    private readonly IGenericModConfigMenuApi Api;
    private readonly WalletModule Module;
    private bool PageSelected;

    internal UnifiedGmcmApiAdapter(ModEntry host, IGenericModConfigMenuApi api, WalletModule module)
    {
        Host = host;
        Api = api;
        Module = module;
    }

    public void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false)
    {
        Host.RegisterModuleConfigCallbacks(Module.Key, reset, save);
    }

    public void Unregister(IManifest mod)
    {
    }

    public void OpenModMenu(IManifest mod)
    {
        Api.OpenModMenu(Host.Manifest);
    }

    public void AddSectionTitle(IManifest mod, Func<string> text, Func<string>? tooltip = null)
    {
        SelectPage();
        Api.AddSectionTitle(Host.Manifest, text, tooltip);
    }

    public void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null)
    {
        SelectPage();
        Api.AddBoolOption(Host.Manifest, getValue, setValue, name, tooltip, QualifyFieldId(fieldId));
    }

    public void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string>? tooltip = null, int? min = null, int? max = null, int? interval = null, Func<int, string>? formatValue = null, string? fieldId = null)
    {
        SelectPage();
        Api.AddNumberOption(Host.Manifest, getValue, setValue, name, tooltip, min, max, interval, formatValue, QualifyFieldId(fieldId));
    }

    public void AddKeybindList(IManifest mod, Func<KeybindList> getValue, Action<KeybindList> setValue, Func<string> name, Func<string>? tooltip = null, string? fieldId = null)
    {
        SelectPage();
        Api.AddKeybindList(Host.Manifest, getValue, setValue, name, tooltip, QualifyFieldId(fieldId));
    }

    public void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string>? tooltip = null, string[]? allowedValues = null, Func<string, string>? formatAllowedValue = null, string? fieldId = null)
    {
        SelectPage();
        Api.AddTextOption(Host.Manifest, getValue, setValue, name, tooltip, allowedValues, formatAllowedValue, QualifyFieldId(fieldId));
    }

    private void SelectPage()
    {
        if (PageSelected)
            return;

        Api.AddPage(Host.Manifest, Host.GetModulePageId(Module.Key), () => Module.DisplayName);
        PageSelected = true;
    }

    private string? QualifyFieldId(string? fieldId)
    {
        return string.IsNullOrWhiteSpace(fieldId) ? null : $"{Module.Key}.{fieldId}";
    }
}
