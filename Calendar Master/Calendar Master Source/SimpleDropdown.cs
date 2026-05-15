using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;

#nullable enable
namespace CalendarMaster;

internal sealed class SimpleDropdown
{
  public Rectangle FieldBounds;
  public List<string> Items;
  public int SelectedIndex;
  public bool IsOpen;
  public int ScrollIndex;
  private bool IsDragging;
  private int DragStartY;
  private int DragStartScrollIndex;
  public int VisibleRows = 8;
  public int RowHeight = 36;

  public SimpleDropdown(Rectangle fieldBounds, List<string> items, int selectedIndex)
  {
    this.FieldBounds = fieldBounds;
    this.Items = items;
    this.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
  }

  public Rectangle GetListBounds()
  {
    int num1 = Math.Min(this.VisibleRows, this.Items.Count) * this.RowHeight + 8;
    int num2 = this.FieldBounds.Bottom + 4;
    int num3 = Game1.uiViewport.Height - 16 /*0x10*/;
    return num2 + num1 > num3 ? new Rectangle(this.FieldBounds.X, Math.Max(16 /*0x10*/, this.FieldBounds.Y - 4 - num1), this.FieldBounds.Width, num1) : new Rectangle(this.FieldBounds.X, num2, this.FieldBounds.Width, num1);
  }

  public void Close() => this.IsOpen = false;

  public void Open()
  {
    this.IsOpen = true;
    this.ScrollIndex = Math.Clamp(this.SelectedIndex - 1, 0, Math.Max(0, this.Items.Count - this.VisibleRows));
  }

  public bool TryHandleLeftClick(int x, int y, out bool changed)
  {
    changed = false;
    if (this.FieldBounds.Contains(x, y))
    {
      this.IsOpen = !this.IsOpen;
      if (this.IsOpen)
        this.ScrollIndex = Math.Clamp(this.SelectedIndex - 1, 0, Math.Max(0, this.Items.Count - this.VisibleRows));
      return true;
    }
    if (!this.IsOpen)
      return false;
    Rectangle listBounds = this.GetListBounds();
    if (!listBounds.Contains(x, y))
    {
      this.IsOpen = false;
      this.IsDragging = false;
      return true;
    }
    int num = this.ScrollIndex + (y - (listBounds.Y + 4)) / this.RowHeight;
    if (num >= 0 && num < this.Items.Count)
    {
      this.SelectedIndex = num;
      changed = true;
    }
    this.IsOpen = false;
    this.IsDragging = false;
    return true;
  }

  public bool TryStartDrag(int x, int y)
  {
    if (!this.IsOpen)
      return false;
    Rectangle listBounds = this.GetListBounds();
    if (!listBounds.Contains(x, y))
      return false;
    if (!this.IsDragging)
    {
      this.IsDragging = true;
      this.DragStartY = y;
      this.DragStartScrollIndex = this.ScrollIndex;
    }
    return true;
  }

  public bool HandleDrag(int x, int y)
  {
    if (!this.IsOpen || !this.IsDragging || this.Items.Count <= this.VisibleRows)
      return false;
    int num = Math.Max(0, this.Items.Count - this.VisibleRows);
    this.ScrollIndex = Math.Clamp(this.DragStartScrollIndex + (y - this.DragStartY) / this.RowHeight, 0, num);
    return true;
  }

  public void EndDrag() => this.IsDragging = false;

  public void MoveSelection(int delta)
  {
    if (this.Items.Count == 0)
      return;
    this.SelectedIndex = Math.Clamp(this.SelectedIndex + delta, 0, this.Items.Count - 1);
    this.EnsureVisible();
  }

  private void EnsureVisible()
  {
    if (!this.IsOpen)
      return;
    int num = Math.Max(0, this.Items.Count - this.VisibleRows);
    if (this.SelectedIndex < this.ScrollIndex)
      this.ScrollIndex = this.SelectedIndex;
    else if (this.SelectedIndex >= this.ScrollIndex + this.VisibleRows)
      this.ScrollIndex = this.SelectedIndex - this.VisibleRows + 1;
    this.ScrollIndex = Math.Clamp(this.ScrollIndex, 0, num);
  }

  public bool TryHandleScrollWheel(int direction)
  {
    if (!this.IsOpen || this.Items.Count <= this.VisibleRows)
      return false;
    int num = Math.Max(0, this.Items.Count - this.VisibleRows);
    this.ScrollIndex = Math.Clamp(this.ScrollIndex - Math.Sign(direction), 0, num);
    return true;
  }

  public string SelectedValue
  {
    get
    {
      return this.SelectedIndex < 0 || this.SelectedIndex >= this.Items.Count ? string.Empty : this.Items[this.SelectedIndex];
    }
  }

  public void DrawField(SpriteBatch b, string label)
  {
    Utility.drawTextWithShadow(b, label, Game1.smallFont, new Vector2((float) (this.FieldBounds.X + 8), (float) (this.FieldBounds.Y - 28)), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
    Color color = this.FieldBounds.Contains(Game1.getMouseX(), Game1.getMouseY()) ? Color.Wheat : Color.White;
    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), this.FieldBounds.X, this.FieldBounds.Y, this.FieldBounds.Width, this.FieldBounds.Height, color, 4f, false, -1f);
    string selectedValue = this.SelectedValue;
    Vector2 vector2_1 = Game1.smallFont.MeasureString(selectedValue);
    Utility.drawTextWithShadow(b, selectedValue, Game1.smallFont, new Vector2((float) (this.FieldBounds.X + 12), (float) (this.FieldBounds.Y + this.FieldBounds.Height / 2) - vector2_1.Y / 2f), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
    Rectangle rectangle = new Rectangle(this.FieldBounds.Right - 42, this.FieldBounds.Y + 6, 36, this.FieldBounds.Height - 12);
    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height, Color.White, 3f, false, -1f);
    string str = this.IsOpen ? "▲" : "▼";
    Vector2 vector2_2 = Game1.smallFont.MeasureString(str);
    Utility.drawTextWithShadow(b, str, Game1.smallFont, new Vector2((float) (rectangle.X + rectangle.Width / 2) - vector2_2.X / 2f, (float) (rectangle.Y + rectangle.Height / 2) - vector2_2.Y / 2f), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
  }

  public void DrawOpenList(SpriteBatch b)
  {
    if (!this.IsOpen)
      return;
    this.DrawList(b);
  }

  private void DrawList(SpriteBatch b)
  {
    Rectangle listBounds = this.GetListBounds();
    IClickableMenu.drawTextureBox(b, Game1.mouseCursors, new Rectangle(432, 439, 9, 9), listBounds.X, listBounds.Y, listBounds.Width, listBounds.Height, Color.White, 4f, false, -1f);
    int num = Math.Min(this.VisibleRows, this.Items.Count);
    for (int index1 = 0; index1 < num; ++index1)
    {
      int index2 = this.ScrollIndex + index1;
      if (index2 >= this.Items.Count)
        break;
      Rectangle rectangle = new Rectangle(listBounds.X + 4, listBounds.Y + 4 + index1 * this.RowHeight, listBounds.Width - 8, this.RowHeight);
      bool flag1 = rectangle.Contains(Game1.getMouseX(), Game1.getMouseY());
      bool flag2 = index2 == this.SelectedIndex;
      if (flag1)
        b.Draw(Game1.staminaRect, rectangle, Color.Black * 0.08f);
      if (flag2)
        b.Draw(Game1.staminaRect, rectangle, Color.Green * 0.12f);
      string str = this.Items[index2];
      Vector2 vector2 = Game1.smallFont.MeasureString(str);
      Utility.drawTextWithShadow(b, str, Game1.smallFont, new Vector2((float) (rectangle.X + 10), (float) (rectangle.Y + rectangle.Height / 2) - vector2.Y / 2f), Game1.textColor, 1f, -1f, -1, -1, 1f, 3);
    }
  }
}
