// Decompiled with JetBrains decompiler
// Type: TheAmazingDeathicornRedux.IGenericModConfigMenuApi
// Assembly: TheAmazingDeathicornRedux, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: D6C3522D-3198-4403-9719-55CB68169A5E
// Assembly location: C:\Users\Thale\Downloads\The Amazing Deathicorn Redux\TheAmazingDeathicornRedux.dll

using StardewModdingAPI;
using System;

#nullable enable
namespace TheAmazingDeathicornRedux;

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

  void AddNumberOption(
    IManifest mod,
    Func<int> getValue,
    Action<int> setValue,
    Func<string> name,
    Func<string>? tooltip = null,
    int? min = null,
    int? max = null,
    int? interval = null,
    Func<int, string>? formatValue = null,
    string? fieldId = null);

  void AddNumberOption(
    IManifest mod,
    Func<float> getValue,
    Action<float> setValue,
    Func<string> name,
    Func<string>? tooltip = null,
    float? min = null,
    float? max = null,
    float? interval = null,
    Func<float, string>? formatValue = null,
    string? fieldId = null);

  void AddTextOption(
    IManifest mod,
    Func<string> getValue,
    Action<string> setValue,
    Func<string> name,
    Func<string>? tooltip = null,
    string[]? allowedValues = null,
    Func<string, string>? formatAllowedValue = null,
    string? fieldId = null);
}
