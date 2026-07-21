using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using System;
using System.Collections.Generic;

#nullable enable
namespace ThaleTheGreat.DateChange;

public class DateChangeMenu : IClickableMenu
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
  private readonly bool previousMouseVisible;
  private bool titleInPosition = true;

  public DateChangeMenu(ModEntry mod)
    : base((Game1.uiViewport.Width - 720) / 2, (Game1.uiViewport.Height - 560) / 2, 720, 560, false)
  {
    this.Mod = mod;
    this.tempDay = Game1.dayOfMonth;
    this.tempSeason = Game1.currentSeason;
    this.tempYear = Game1.year;
    this.tempFreeze = mod.FreezeTime;
    this.previousMouseVisible = Game1.game1.IsMouseVisible;
    Game1.game1.IsMouseVisible = false;
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
    int centerX = this.xPositionOnScreen + this.width / 2;
    int contentTop = this.yPositionOnScreen + 132;
    int fieldWidth = 360;
    int fieldHeight = OptionsDropDown.dropDownButtonSource.Height * 4;
    int fieldX = centerX - fieldWidth / 2;

    this.freezeToggleButton = new ClickableComponent(new Rectangle(centerX - 170, this.yPositionOnScreen - 58, 340, 52), "freeze");

    this.dayDropdown = new SimpleDropdown(new Rectangle(fieldX, contentTop, fieldWidth, fieldHeight), DateChangeMenu.BuildDayItems(), Math.Clamp(this.tempDay - 1, 0, 27))
    {
      VisibleRows = 8
    };
    this.dayFieldComponent = new ClickableComponent(this.dayDropdown.FieldBounds, "day")
    {
      myID = 100,
      upNeighborID = 104,
      downNeighborID = 101,
      leftNeighborID = 104,
      rightNeighborID = 105
    };

    Rectangle seasonBounds = new Rectangle(fieldX, contentTop + 108, fieldWidth, fieldHeight);
    List<string> items = new List<string>();
    items.Add(this.Mod.T("season.spring"));
    items.Add(this.Mod.T("season.summer"));
    items.Add(this.Mod.T("season.fall"));
    items.Add(this.Mod.T("season.winter"));
    this.seasonDropdown = new SimpleDropdown(seasonBounds, items, DateChangeMenu.SeasonToIndex(this.tempSeason))
    {
      VisibleRows = 4
    };
    this.seasonFieldComponent = new ClickableComponent(this.seasonDropdown.FieldBounds, "season")
    {
      myID = 101,
      upNeighborID = 100,
      downNeighborID = 102,
      leftNeighborID = 104,
      rightNeighborID = 105
    };

    this.yearDropdown = new SimpleDropdown(new Rectangle(fieldX, contentTop + 216, fieldWidth, fieldHeight), DateChangeMenu.BuildYearItems(), Math.Max(0, this.tempYear - 1))
    {
      VisibleRows = 8
    };
    this.yearFieldComponent = new ClickableComponent(this.yearDropdown.FieldBounds, "year")
    {
      myID = 102,
      upNeighborID = 101,
      downNeighborID = 103,
      leftNeighborID = 104,
      rightNeighborID = 105
    };

    int buttonY = this.yearDropdown.FieldBounds.Bottom + 44;
    this.applyButton = new ClickableComponent(new Rectangle(centerX - 150, buttonY, 120, 64), "apply");
    this.cancelButton = new ClickableComponent(new Rectangle(centerX + 30, buttonY, 120, 64), "cancel");
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
        this.tempSeason = DateChangeMenu.IndexToSeason(this.seasonDropdown.SelectedIndex);
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
    _ = this.titleInPosition;

    b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.72f);
    IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height, Color.White, 1f, false);

    SpriteText.drawStringHorizontallyCenteredAt(
      b,
      this.Mod.T("menu.title"),
      this.xPositionOnScreen + this.width / 2,
      this.yPositionOnScreen + 28,
      999999,
      -1,
      999999,
      1f,
      0.88f);

    this.DrawVanillaButton(b, this.freezeToggleButton.bounds, this.Mod.T("menu.freeze-clock", new { state = this.tempFreeze ? this.Mod.T("state.on") : this.Mod.T("state.off") }));
    this.dayDropdown.SelectedIndex = Math.Clamp(this.tempDay - 1, 0, 27);
    this.seasonDropdown.SelectedIndex = DateChangeMenu.SeasonToIndex(this.tempSeason);
    this.yearDropdown.SelectedIndex = Math.Clamp(this.tempYear - 1, 0, this.yearDropdown.Items.Count - 1);
    this.dayDropdown.DrawLabeledDropdown(b, this.Mod.T("menu.day"));
    this.seasonDropdown.DrawLabeledDropdown(b, this.Mod.T("menu.season"));
    this.yearDropdown.DrawLabeledDropdown(b, this.Mod.T("menu.year"));
    this.DrawVanillaButton(b, this.applyButton.bounds, this.Mod.T("menu.ok"));
    this.DrawVanillaButton(b, this.cancelButton.bounds, this.Mod.T("menu.cancel"));

    if (this.dayDropdown.IsOpen)
      this.dayDropdown.DrawOpenList(b);
    else if (this.seasonDropdown.IsOpen)
      this.seasonDropdown.DrawOpenList(b);
    else if (this.yearDropdown.IsOpen)
      this.yearDropdown.DrawOpenList(b);

    this.drawMouse(b, false, -1);
  }


  private void DrawVanillaButton(SpriteBatch b, Rectangle bounds, string text)
  {
    IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), bounds.X, bounds.Y, bounds.Width, bounds.Height, Color.White, 1f, false);
    Vector2 textSize = Game1.smallFont.MeasureString(text);
    b.DrawString(Game1.smallFont, text, new Vector2(bounds.X + (bounds.Width - textSize.X) / 2f, bounds.Y + (bounds.Height - textSize.Y) / 2f), Game1.textColor);
  }

  protected override void cleanupBeforeExit()
  {
    Game1.game1.IsMouseVisible = this.previousMouseVisible;
    base.cleanupBeforeExit();
  }

}
