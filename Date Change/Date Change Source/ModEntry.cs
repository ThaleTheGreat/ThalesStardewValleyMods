using StardewModdingAPI;
using StardewModdingAPI.Events;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using System;
using System.IO;

#nullable enable
namespace ThaleTheGreat.DateChange;

public class ModEntry : Mod
{
  private const string SaveDataKey = "datechange.pending-world-changes";
  private const string LegacySaveDataKey = "pending-world-changes";
  internal ModConfig Config = null!;
  internal bool FreezeTime;
  private IMobilePhoneApi? MobilePhoneApi;
  private bool MobilePhoneOpenQueued;

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
    if (Game1.activeClickableMenu is DateChangeMenu menu)
    {
      if (e.Button == SButton.Escape || e.Button == SButton.ControllerB || this.Config.OpenMenuKey.JustPressed())
      {
        this.Helper.Input.Suppress(e.Button);
        Game1.playSound("cancel", new int?());
        menu.exitThisMenu(true);
        return;
      }

      if (!ModEntry.IsMouseInput(e.Button))
        this.Helper.Input.Suppress(e.Button);

      return;
    }

    if (!Context.IsWorldReady || !this.Config.OpenMenuKey.JustPressed())
      return;

    this.Helper.Input.Suppress(e.Button);
    Game1.activeClickableMenu = (IClickableMenu) new DateChangeMenu(this);
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
    this.DebugLog("Applying queued Date Change changes (next morning).");
    this.ApplyWorldChanges(pendingWorldChanges.Day, pendingWorldChanges.Season, pendingWorldChanges.Year, refreshVisuals: false);
    this.Helper.Data.WriteSaveData<PendingWorldChanges>(SaveDataKey, null);
    this.Helper.Data.WriteSaveData<PendingWorldChanges>(LegacySaveDataKey, null);
  }

  private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
  {
    this.RegisterMobilePhoneApp();

    IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
    if (api == null)
      return;
    api.Register(this.ModManifest, (Action) (() => this.Config = new ModConfig()), (Action) (() => this.Helper.WriteConfig<ModConfig>(this.Config)));
    api.AddKeybindList(this.ModManifest, (Func<KeybindList>) (() => this.Config.OpenMenuKey), (Action<KeybindList>) (value => this.Config.OpenMenuKey = value), (Func<string>) (() => "Open Menu Key"), (Func<string>) (() => "Keybind to open the Date Change menu."));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this.Config.ApplyImmediately), (Action<bool>) (value => this.Config.ApplyImmediately = value), (Func<string>) (() => "Apply Immediately"), (Func<string>) (() => "If enabled, Apply happens right now. If disabled (default), Apply queues changes for the next morning."));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this.Config.DebugLogging), (Action<bool>) (value => this.Config.DebugLogging = value), (Func<string>) (() => "Debug Logging"), (Func<string>) (() => "Show routine Date Change diagnostic messages in the SMAPI console."));
  }

  private void RegisterMobilePhoneApp()
  {
    this.MobilePhoneApi = this.Helper.ModRegistry.GetApi<IMobilePhoneApi>("aedenthorn.MobilePhone");
    if (this.MobilePhoneApi == null)
      return;

    try
    {
      Texture2D appIcon = this.Helper.ModContent.Load<Texture2D>(Path.Combine("assets", "MobilePhone.png"));
      bool success = this.MobilePhoneApi.AddApp(this.ModManifest.UniqueID, "(TTG) Date Change", this.OpenFromMobilePhone, appIcon);
      this.DebugLog($"Mobile Phone app registration success: {success}.");
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Failed to register the Date Change Mobile Phone app: {ex.Message}", LogLevel.Warn);
    }
  }

  private void OpenFromMobilePhone()
  {
    if (this.MobilePhoneOpenQueued)
      return;

    this.MobilePhoneOpenQueued = true;
    this.Helper.Events.GameLoop.UpdateTicked += this.OnOpenFromMobilePhoneTicked;
  }

  private void OnOpenFromMobilePhoneTicked(object? sender, UpdateTickedEventArgs e)
  {
    this.Helper.Events.GameLoop.UpdateTicked -= this.OnOpenFromMobilePhoneTicked;
    this.MobilePhoneOpenQueued = false;

    if (!Context.IsWorldReady || Game1.activeClickableMenu is DateChangeMenu)
      return;

    if (this.MobilePhoneApi != null)
    {
      this.MobilePhoneApi.SetAppRunning(false);
      this.MobilePhoneApi.SetRunningApp(string.Empty);
      this.MobilePhoneApi.SetPhoneOpened(false);
    }

    Game1.activeClickableMenu = new DateChangeMenu(this);
  }

  internal void ToggleFreezeTime()
  {
    if (!Context.IsMainPlayer)
      this.DebugLog("Only the main player can freeze/resume time in multiplayer.", LogLevel.Warn);
    else
      this.FreezeTime = !this.FreezeTime;
  }

  internal void ApplyFromMenu(int day, string season, int year)
  {
    if (!Context.IsWorldReady)
      return;
    if (!Context.IsMainPlayer)
      this.DebugLog("Only the main player can change world time/date in multiplayer.", LogLevel.Warn);
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
      this.DebugLog(error ?? "Couldn't queue changes.", LogLevel.Warn);
    }
    else
    {
      this.Helper.Data.WriteSaveData<PendingWorldChanges>(SaveDataKey, sanitized);
      this.DebugLog($"Queued changes for next morning: day={sanitized.Day}, season={sanitized.Season}, year={sanitized.Year}.");
    }
  }

  private void ApplyWorldChanges(int day, string season, int year, bool refreshVisuals)
  {
    string? error;
    PendingWorldChanges? sanitized = PendingWorldChanges.CreateSanitized(day, season, year, out error);
    if (sanitized == null)
    {
      this.DebugLog(error ?? "Couldn't apply changes.", LogLevel.Warn);
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
          this.DebugLog($"Date changed, but visual refresh failed: {ex.Message}", LogLevel.Trace);
        }
      }
      else
      {
        Game1.updateWeatherIcon();
      }
      this.DebugLog($"Applied changes: {sanitized.Season} {sanitized.Day}, Year {sanitized.Year}.");
    }
  }
  private static bool IsMouseInput(SButton button)
  {
    string name = button.ToString();
    return name.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase);
  }

  private void DebugLog(string message, LogLevel level = LogLevel.Debug)
  {
    if (this.Config.DebugLogging)
      this.Monitor.Log(message, level);
  }
}
