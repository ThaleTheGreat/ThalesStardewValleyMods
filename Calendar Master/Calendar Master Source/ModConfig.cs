using StardewModdingAPI.Utilities;

#nullable enable
namespace CalendarMaster;

public class ModConfig
{
  public KeybindList OpenMenuKey { get; set; } = KeybindList.Parse("LeftControl + C");

  public bool ApplyImmediately { get; set; } = false;
}
