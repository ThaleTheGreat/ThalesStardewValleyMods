using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using xTile;

#nullable enable
namespace CalendarMaster;

public class ModEntry : Mod
{
  private const string SaveDataKey = "calendarmaster.pending-world-changes";
  private const string LegacySaveDataKey = "pending-world-changes";
  internal ModConfig Config = null!;
  internal bool FreezeTime;

  public override void Entry(IModHelper helper)
  {
    this.Config = helper.ReadConfig<ModConfig>();
    helper.Events.Input.ButtonPressed += new EventHandler<ButtonPressedEventArgs>(this.OnButtonPressed);
    helper.Events.GameLoop.UpdateTicking += new EventHandler<UpdateTickingEventArgs>(this.OnUpdateTicking);
    helper.Events.GameLoop.DayStarted += new EventHandler<DayStartedEventArgs>(this.OnDayStarted);
    helper.Events.GameLoop.GameLaunched += new EventHandler<GameLaunchedEventArgs>(this.OnGameLaunched);
  }

  private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
  {
    if (!Context.IsWorldReady || !this.Config.OpenMenuKey.JustPressed())
      return;
    Game1.activeClickableMenu = (IClickableMenu) new CalendarMasterMenu(this);
  }

  private void OnUpdateTicking(object? sender, UpdateTickingEventArgs e)
  {
    if (!Context.IsWorldReady || !Context.IsMainPlayer || !this.FreezeTime)
      return;
    Game1.gameTimeInterval = 0;
  }

  private void OnDayStarted(object? sender, DayStartedEventArgs e)
  {
    if (!Context.IsWorldReady || !Context.IsMainPlayer)
      return;
    PendingWorldChanges? pendingWorldChanges = this.Helper.Data.ReadSaveData<PendingWorldChanges>(SaveDataKey) ?? this.Helper.Data.ReadSaveData<PendingWorldChanges>(LegacySaveDataKey);
    if (pendingWorldChanges == null || !pendingWorldChanges.HasValue)
      return;
    this.Monitor.Log("Applying queued Calendar Master changes (next morning).", LogLevel.Info);
    this.ApplyWorldChanges(pendingWorldChanges.Day, pendingWorldChanges.Season, pendingWorldChanges.Year, refreshVisuals: false);
    this.Helper.Data.WriteSaveData<PendingWorldChanges>(SaveDataKey, null);
    this.Helper.Data.WriteSaveData<PendingWorldChanges>(LegacySaveDataKey, null);
  }

  private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
  {
    IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
    if (api == null)
      return;
    api.Register(this.ModManifest, (Action) (() => this.Config = new ModConfig()), (Action) (() => this.Helper.WriteConfig<ModConfig>(this.Config)));
    api.AddKeybindList(this.ModManifest, (Func<KeybindList>) (() => this.Config.OpenMenuKey), (Action<KeybindList>) (value => this.Config.OpenMenuKey = value), (Func<string>) (() => "Open Menu Key"), (Func<string>) (() => "Keybind to open the Calendar Master menu."));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this.Config.ApplyImmediately), (Action<bool>) (value => this.Config.ApplyImmediately = value), (Func<string>) (() => "Apply Immediately"), (Func<string>) (() => "If enabled, Apply happens right now. If disabled (default), Apply queues changes for the next morning."));
  }

  internal void ToggleFreezeTime()
  {
    if (!Context.IsMainPlayer)
      this.Monitor.Log("Only the main player can freeze/resume time in multiplayer.", LogLevel.Warn);
    else
      this.FreezeTime = !this.FreezeTime;
  }

  internal void ApplyFromMenu(int day, string season, int year)
  {
    if (!Context.IsWorldReady)
      return;
    if (!Context.IsMainPlayer)
      this.Monitor.Log("Only the main player can change world time/date in multiplayer.", LogLevel.Warn);
    else if (this.Config.ApplyImmediately)
    {
      this.ApplyWorldChanges(day, season, year, refreshVisuals: true);
      Game1.playSound("reward", new int?());
    }
    else
    {
      this.QueueWorldChanges(day, season, year);
      Game1.playSound("Ship", new int?());
    }
  }

  private void QueueWorldChanges(int day, string season, int year)
  {
    string? error;
    PendingWorldChanges? sanitized = PendingWorldChanges.CreateSanitized(day, season, year, out error);
    if (sanitized == null)
    {
      this.Monitor.Log(error ?? "Couldn't queue changes.", LogLevel.Warn);
    }
    else
    {
      this.Helper.Data.WriteSaveData<PendingWorldChanges>(SaveDataKey, sanitized);
      this.Monitor.Log($"Queued changes for next morning: day={sanitized.Day}, season={sanitized.Season}, year={sanitized.Year}.", LogLevel.Info);
    }
  }

  private void ApplyWorldChanges(int day, string season, int year, bool refreshVisuals)
  {
    string? error;
    PendingWorldChanges? sanitized = PendingWorldChanges.CreateSanitized(day, season, year, out error);
    if (sanitized == null)
    {
      this.Monitor.Log(error ?? "Couldn't apply changes.", LogLevel.Warn);
    }
    else
    {
      Game1.dayOfMonth = sanitized.Day;
      Game1.currentSeason = sanitized.Season;
      Game1.year = sanitized.Year;
      try
      {
        int seasonIndex = PendingWorldChanges.GetSeasonIndex(sanitized.Season);
        int num = (sanitized.Year - 1) * 112 /*0x70*/ + seasonIndex * 28 + (sanitized.Day - 1);
        if (Game1.stats != null)
          Game1.stats.DaysPlayed = (uint) Math.Max(0, num);
      }
      catch
      {
      }
      if (refreshVisuals && !Game1.game1.takingMapScreenshot)
      {
        try
        {
          Game1.setGraphicsForSeason(false);
          Game1.updateWeatherIcon();
        }
        catch (Exception ex)
        {
          this.Monitor.Log($"Date changed, but visual refresh failed: {ex.Message}", LogLevel.Trace);
        }
      }
      else
      {
        Game1.updateWeatherIcon();
      }
      this.Monitor.Log($"Applied changes: {sanitized.Season} {sanitized.Day}, Year {sanitized.Year}.", LogLevel.Info);
    }
  }
}
