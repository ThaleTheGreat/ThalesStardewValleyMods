using System;

#nullable enable
namespace ThaleTheGreat.DateChange;

internal sealed class PendingWorldChanges
{
  public int Day { get; set; }

  public string Season { get; set; } = "spring";

  public int Year { get; set; }

  public bool HasValue { get; set; } = true;

  public static PendingWorldChanges? CreateSanitized(
    int day,
    string season,
    int year,
    out string? error)
  {
    error = null;
    day = Math.Clamp(day, 1, 28);
    year = Math.Max(1, year);
    season = PendingWorldChanges.NormalizeSeason(season);
    if (!PendingWorldChanges.IsValidSeason(season))
    {
      error = "Invalid season. Use spring, summer, fall, winter.";
      return null;
    }
    return new PendingWorldChanges()
    {
      Day = day,
      Season = season,
      Year = year,
      HasValue = true
    };
  }

  private static string NormalizeSeason(string raw)
  {
    string value = raw.Trim().ToLowerInvariant();
    return value == "autumn" ? "fall" : value;
  }

  private static bool IsValidSeason(string s)
  {
    return s == "spring" || s == "summer" || s == "fall" || s == "winter";
  }

  public static int GetSeasonIndex(string season)
  {
    int seasonIndex;
    switch (season)
    {
      case "spring":
        seasonIndex = 0;
        break;
      case "summer":
        seasonIndex = 1;
        break;
      case "fall":
        seasonIndex = 2;
        break;
      case "winter":
        seasonIndex = 3;
        break;
      default:
        seasonIndex = 0;
        break;
    }
    return seasonIndex;
  }
}
