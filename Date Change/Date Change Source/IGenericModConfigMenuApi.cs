using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using System;

#nullable enable
namespace ThaleTheGreat.DateChange;

public interface IGenericModConfigMenuApi
{
  void Register(IManifest mod, Action reset, Action save, bool titleScreenOnly = false);

  void AddBoolOption(
    IManifest mod,
    Func<bool> getValue,
    Action<bool> setValue,
    Func<string> name,
    Func<string>? tooltip = null,
    string? fieldId = null);

  void AddKeybindList(
    IManifest mod,
    Func<KeybindList> getValue,
    Action<KeybindList> setValue,
    Func<string> name,
    Func<string>? tooltip = null,
    string? fieldId = null);
}
