using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;

#nullable enable
namespace CalendarMaster;

public class CalendarMasterMenu : IClickableMenu
{
  private readonly ModEntry Mod;
  private ClickableComponent freezeToggleButton = null!;
  private ClickableComponent dayFieldComponent = null!;
  private ClickableComponent seasonFieldComponent = null!;
  private ClickableComponent yearFieldComponent = null!;
  private ClickableComponent applyButton = null!;
  private ClickableComponent cancelButton = null!;
  private SimpleDropdown dayDropdown = null!;
  private SimpleDropdown seasonDropdown = null!;
  private SimpleDropdown yearDropdown = null!;
  private int tempDay;
  private string tempSeason;
  private int tempYear;
  private bool tempFreeze;

  public CalendarMasterMenu(ModEntry mod)
    : base(Game1.viewport.Width / 2 - 300, Game1.viewport.Height / 2 - 270, 600, 540, false)
  {
    this.Mod = mod;
    this.tempDay = Game1.dayOfMonth;
    this.tempSeason = Game1.currentSeason;
    this.tempYear = Game1.year;
    this.tempFreeze = mod.FreezeTime;
    this.SetupComponents();
    base.populateClickableComponentList();
    if (!Game1.options.gamepadControls)
      return;
    if (!Game1.options.snappyMenus)
      Game1.options.snappyMenus = true;
    this.setUpForGamePadMode();
    base.snapToDefaultClickableComponent();
  }

  public override void populateClickableComponentList()
  {
    this.allClickableComponents = new List<ClickableComponent>()
    {
      this.freezeToggleButton,
      this.dayFieldComponent,
      this.seasonFieldComponent,
      this.yearFieldComponent,
      this.applyButton,
      this.cancelButton
    };
  }

  private void SetupComponents()
  {
    int num1 = this.xPositionOnScreen + this.width / 2;
    int num2 = this.yPositionOnScreen + 130;
    this.freezeToggleButton = new ClickableComponent(new Rectangle(num1 - 200, this.yPositionOnScreen - 56, 400, 48 /*0x30*/), "freeze");
    int num3 = 360;
    int num4 = 44;
    int num5 = num1 - num3 / 2;
    int num6 = num2;
    this.dayDropdown = new SimpleDropdown(new Rectangle(num5, num6, num3, num4), CalendarMasterMenu.BuildDayItems(), Math.Clamp(this.tempDay - 1, 0, 27))
    {
      VisibleRows = 8,
      RowHeight = 34
    };
    this.dayFieldComponent = new ClickableComponent(this.dayDropdown.FieldBounds, "day")
    {
      myID = 100,
      upNeighborID = 104,
      downNeighborID = 101,
      leftNeighborID = 104,
      rightNeighborID = 105
    };
    int num7 = num2 + 110;
    Rectangle fieldBounds = new Rectangle(num5, num7, num3, num4);
    List<string> items = new List<string>();
    items.Add("Spring");
    items.Add("Summer");
    items.Add("Fall");
    items.Add("Winter");
    int index = CalendarMasterMenu.SeasonToIndex(this.tempSeason);
    this.seasonDropdown = new SimpleDropdown(fieldBounds, items, index)
    {
      VisibleRows = 4,
      RowHeight = 34
    };
    this.seasonFieldComponent = new ClickableComponent(this.seasonDropdown.FieldBounds, "season")
    {
      myID = 101,
      upNeighborID = 100,
      downNeighborID = 102,
      leftNeighborID = 104,
      rightNeighborID = 105
    };
    int num8 = num2 + 220;
    this.yearDropdown = new SimpleDropdown(new Rectangle(num5, num8, num3, num4), CalendarMasterMenu.BuildYearItems(), Math.Max(0, this.tempYear - 1))
    {
      VisibleRows = 8,
      RowHeight = 34
    };
    this.yearFieldComponent = new ClickableComponent(this.yearDropdown.FieldBounds, "year")
    {
      myID = 102,
      upNeighborID = 101,
      downNeighborID = 103,
      leftNeighborID = 104,
      rightNeighborID = 105
    };
    int num9 = this.yPositionOnScreen + this.height - 108;
    this.applyButton = new ClickableComponent(new Rectangle(num1 - 150, num9, 120, 64 /*0x40*/), "apply");
    this.cancelButton = new ClickableComponent(new Rectangle(num1 + 30, num9, 120, 64 /*0x40*/), "cancel");
    this.applyButton.myID = 103;
    this.applyButton.upNeighborID = 102;
    this.applyButton.rightNeighborID = 105;
    this.cancelButton.myID = 105;
    this.cancelButton.upNeighborID = 102;
    this.cancelButton.leftNeighborID = 103;
    this.freezeToggleButton.myID = 104;
    this.freezeToggleButton.downNeighborID = 100;
    this.freezeToggleButton.leftNeighborID = 103;
    this.freezeToggleButton.rightNeighborID = 105;
  }

  public override void snapToDefaultClickableComponent()
  {
    this.currentlySnappedComponent = this.freezeToggleButton;
    this.snapCursorToCurrentSnappedComponent();
  }

  public override void applyMovementKey(int direction)
  {
    if (this.dayDropdown.IsOpen || this.seasonDropdown.IsOpen || this.yearDropdown.IsOpen)
    {
      SimpleDropdown simpleDropdown = this.dayDropdown.IsOpen ? this.dayDropdown : (this.seasonDropdown.IsOpen ? this.seasonDropdown : this.yearDropdown);
      if (direction == 0)
      {
        simpleDropdown.MoveSelection(-1);
      }
      else
      {
        if (direction != 2)
          return;
        simpleDropdown.MoveSelection(1);
      }
    }
    else
      base.applyMovementKey(direction);
  }

  public override void receiveGamePadButton(Buttons b)
  {
    if (this.dayDropdown.IsOpen || this.seasonDropdown.IsOpen || this.yearDropdown.IsOpen)
    {
      if (b == Buttons.A)
      {
        this.dayDropdown.Close();
        this.seasonDropdown.Close();
        this.yearDropdown.Close();
        Game1.playSound("bigDeSelect", new int?());
      }
      else
      {
        if (b != Buttons.B)
          return;
        this.dayDropdown.Close();
        this.seasonDropdown.Close();
        this.yearDropdown.Close();
        Game1.playSound("cancel", new int?());
      }
    }
    else
    {
      if (b == Buttons.A)
      {
        if (this.currentlySnappedComponent == null)
          return;
        if (this.currentlySnappedComponent.myID == this.freezeToggleButton.myID)
        {
          this.Mod.ToggleFreezeTime();
          this.tempFreeze = this.Mod.FreezeTime;
          Game1.playSound("drumkit6", new int?());
        }
        else if (this.currentlySnappedComponent.myID == this.dayFieldComponent.myID)
        {
          this.dayDropdown.Open();
          this.seasonDropdown.Close();
          this.yearDropdown.Close();
          Game1.playSound("shwip", new int?());
        }
        else if (this.currentlySnappedComponent.myID == this.seasonFieldComponent.myID)
        {
          this.seasonDropdown.Open();
          this.dayDropdown.Close();
          this.yearDropdown.Close();
          Game1.playSound("shwip", new int?());
        }
        else if (this.currentlySnappedComponent.myID == this.yearFieldComponent.myID)
        {
          this.yearDropdown.Open();
          this.dayDropdown.Close();
          this.seasonDropdown.Close();
          Game1.playSound("shwip", new int?());
        }
        else if (this.currentlySnappedComponent.myID == this.applyButton.myID)
        {
          this.Mod.ApplyFromMenu(this.tempDay, this.tempSeason, this.tempYear);
          Game1.playSound("coin", new int?());
          this.exitThisMenu(true);
        }
        else if (this.currentlySnappedComponent.myID == this.cancelButton.myID)
        {
          Game1.playSound("cancel", new int?());
          this.exitThisMenu(true);
        }
      }
      else if (b == Buttons.B)
      {
        Game1.playSound("cancel", new int?());
        this.exitThisMenu(true);
      }
      base.receiveGamePadButton(b);
    }
  }

  private static List<string> BuildDayItems()
  {
    List<string> stringList = new List<string>(28);
    for (int index = 1; index <= 28; ++index)
      stringList.Add(index.ToString());
    return stringList;
  }

  private static List<string> BuildYearItems()
  {
    List<string> stringList = new List<string>(50);
    for (int index = 1; index <= 50; ++index)
      stringList.Add(index.ToString());
    return stringList;
  }

  private static int SeasonToIndex(string season)
  {
    int index;
    switch (season)
    {
      case "spring":
        index = 0;
        break;
      case "summer":
        index = 1;
        break;
      case "fall":
        index = 2;
        break;
      case "winter":
        index = 3;
        break;
      default:
        index = 0;
        break;
    }
    return index;
  }

  private static string IndexToSeason(int idx)
  {
    string season;
    switch (idx)
    {
      case 0:
        season = "spring";
        break;
      case 1:
        season = "summer";
        break;
      case 2:
        season = "fall";
        break;
      case 3:
        season = "winter";
        break;
      default:
        season = "spring";
        break;
    }
    return season;
  }

  public override void receiveLeftClick(int x, int y, bool playSound = true)
  {
    base.receiveLeftClick(x, y, playSound);
    if ((this.dayDropdown.IsOpen || this.seasonDropdown.IsOpen || this.yearDropdown.IsOpen) && this.TryHandleDropdownClicks(x, y))
      return;
    if (this.freezeToggleButton.containsPoint(x, y))
    {
      this.Mod.ToggleFreezeTime();
      this.tempFreeze = this.Mod.FreezeTime;
      Game1.playSound("drumkit6", new int?());
    }
    else
    {
      if (this.TryHandleDropdownClicks(x, y))
        return;
      if (this.applyButton.containsPoint(x, y))
      {
        this.Mod.ApplyFromMenu(this.tempDay, this.tempSeason, this.tempYear);
        Game1.playSound("coin", new int?());
        this.exitThisMenu(true);
      }
      else
      {
        if (!this.cancelButton.containsPoint(x, y))
          return;
        Game1.playSound("cancel", new int?());
        this.exitThisMenu(true);
      }
    }
  }

  public override void leftClickHeld(int x, int y)
  {
    base.leftClickHeld(x, y);
    if (this.dayDropdown.TryStartDrag(x, y) || this.seasonDropdown.TryStartDrag(x, y) || this.yearDropdown.TryStartDrag(x, y))
      return;
    this.dayDropdown.HandleDrag(x, y);
    this.seasonDropdown.HandleDrag(x, y);
    this.yearDropdown.HandleDrag(x, y);
  }

  public override void releaseLeftClick(int x, int y)
  {
    base.releaseLeftClick(x, y);
    this.dayDropdown.EndDrag();
    this.seasonDropdown.EndDrag();
    this.yearDropdown.EndDrag();
  }

  private bool TryHandleDropdownClicks(int x, int y)
  {
    bool changed;
    if (this.dayDropdown.TryHandleLeftClick(x, y, out changed))
    {
      if (changed)
      {
        this.tempDay = this.dayDropdown.SelectedIndex + 1;
        Game1.playSound("drumkit6", new int?());
      }
      this.seasonDropdown.Close();
      this.yearDropdown.Close();
      return true;
    }
    if (this.seasonDropdown.TryHandleLeftClick(x, y, out changed))
    {
      if (changed)
      {
        this.tempSeason = CalendarMasterMenu.IndexToSeason(this.seasonDropdown.SelectedIndex);
        Game1.playSound("drumkit6", new int?());
      }
      this.dayDropdown.Close();
      this.yearDropdown.Close();
      return true;
    }
    if (!this.yearDropdown.TryHandleLeftClick(x, y, out changed))
      return false;
    if (changed)
    {
      this.tempYear = this.yearDropdown.SelectedIndex + 1;
      Game1.playSound("drumkit6", new int?());
    }
    this.dayDropdown.Close();
    this.seasonDropdown.Close();
    return true;
  }

  public override void receiveScrollWheelAction(int direction)
  {
    base.receiveScrollWheelAction(direction);
    if (!this.dayDropdown.TryHandleScrollWheel(direction) && !this.seasonDropdown.TryHandleScrollWheel(direction) && !this.yearDropdown.TryHandleScrollWheel(direction))
      return;
    Game1.playSound("shiny4", new int?());
  }

  public override void draw(SpriteBatch b)
  {
    SpriteBatch spriteBatch = b;
    Texture2D fadeToBlackRect = Game1.fadeToBlackRect;
    Viewport viewport = Game1.graphics.GraphicsDevice.Viewport;
    Rectangle bounds = viewport.Bounds;
    Color color = Color.Black * 0.75f;
    spriteBatch.Draw(fadeToBlackRect, bounds, color);
    Game1.drawDialogueBox(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, false, true, null!, false, true, -1, -1, -1);
    this.DrawWideToggle(b, this.freezeToggleButton, "Freeze Clock: " + (this.tempFreeze ? "ON" : "OFF"));
    this.dayDropdown.SelectedIndex = Math.Clamp(this.tempDay - 1, 0, 27);
    this.seasonDropdown.SelectedIndex = CalendarMasterMenu.SeasonToIndex(this.tempSeason);
    this.yearDropdown.SelectedIndex = Math.Clamp(this.tempYear - 1, 0, this.yearDropdown.Items.Count - 1);
    this.dayDropdown.DrawField(b, "Day");
    this.seasonDropdown.DrawField(b, "Season");
    this.yearDropdown.DrawField(b, "Year");
    this.DrawButton(b, this.applyButton, "Apply");
    this.DrawButton(b, this.cancelButton, "Cancel");
    if (this.dayDropdown.IsOpen)
      this.dayDropdown.DrawOpenList(b);
    else if (this.seasonDropdown.IsOpen)
      this.seasonDropdown.DrawOpenList(b);
    else if (this.yearDropdown.IsOpen)
      this.yearDropdown.DrawOpenList(b);
    this.drawMouse(b, false, -1);
  }

  private void DrawButton(SpriteBatch b, ClickableComponent button, string text)
  {
    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, button.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.Wheat : Color.White, 4f, false, -1f);
    Vector2 vector2 = Game1.smallFont.MeasureString(text);
    Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2((float) (button.bounds.X + button.bounds.Width / 2) - vector2.X / 2f, (float) (button.bounds.Y + button.bounds.Height / 2) - vector2.Y / 2f), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
  }

  private void DrawWideToggle(SpriteBatch b, ClickableComponent button, string text)
  {
    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), button.bounds.X, button.bounds.Y, button.bounds.Width, button.bounds.Height, button.containsPoint(Game1.getMouseX(), Game1.getMouseY()) ? Color.Wheat : Color.White, 4f, false, -1f);
    Vector2 vector2 = Game1.smallFont.MeasureString(text);
    Utility.drawTextWithShadow(b, text, Game1.smallFont, new Vector2((float) (button.bounds.X + button.bounds.Width / 2) - vector2.X / 2f, (float) (button.bounds.Y + button.bounds.Height / 2) - vector2.Y / 2f), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
  }
}
