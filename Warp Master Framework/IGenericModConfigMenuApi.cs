using System;
using StardewModdingAPI;

namespace WarpMasterFramework
{
    /// <summary>
    /// Minimal subset of the Generic Mod Config Menu API used by this mod.
    /// Must be a public, top-level interface so SMAPI can map it.
    /// </summary>
    public interface IGenericModConfigMenuApi
    {
        void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);
        void AddBoolOption(IManifest mod, Func<bool> getValue, Action<bool> setValue, Func<string> name, Func<string> tooltip = null, string fieldId = null);
        void AddTextOption(IManifest mod, Func<string> getValue, Action<string> setValue, Func<string> name, Func<string> tooltip = null, string[] allowedValues = null, Func<string, string> formatAllowedValue = null, string fieldId = null);
        void AddNumberOption(IManifest mod, Func<int> getValue, Action<int> setValue, Func<string> name, Func<string> tooltip = null, int? min = null, int? max = null, int? interval = null, string fieldId = null);
        void AddParagraph(IManifest mod, Func<string> text);
        void AddSectionTitle(IManifest mod, Func<string> text, Func<string> tooltip = null);
        void AddPage(IManifest mod, string pageId, Func<string> pageTitle);
        void AddPageLink(IManifest mod, string pageId, Func<string> text, Func<string> tooltip = null);
    }
}
