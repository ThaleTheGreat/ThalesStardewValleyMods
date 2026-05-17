using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Collections.Generic;

#nullable enable
namespace ThaleTheGreat.DateChange;

internal sealed class SimpleDropdown
{
    private const int Scale = 4;
    private const int ListPad = 4;
    private const int ScrollbarWidth = 24;
    private const int ScrollbarGap = 8;
    private static readonly Rectangle MenuBoxSource = new(0, 256, 60, 60);
    private static readonly Rectangle ScrollbarRunnerSource = new(403, 383, 6, 6);
    private static readonly Rectangle ScrollbarThumbSource = new(435, 463, 6, 10);

    public Rectangle FieldBounds;
    public readonly List<string> Items;
    public int SelectedIndex;
    public bool IsOpen;
    public int ScrollIndex;
    public int VisibleRows = 8;
    public int RowHeight;

    private bool IsDraggingThumb;
    private int DragStartY;
    private int DragStartScrollIndex;

    public SimpleDropdown(Rectangle fieldBounds, List<string> items, int selectedIndex)
    {
        int vanillaHeight = OptionsDropDown.dropDownButtonSource.Height * Scale;
        this.FieldBounds = new Rectangle(fieldBounds.X, fieldBounds.Y, fieldBounds.Width, vanillaHeight);
        this.Items = items;
        this.SelectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, items.Count - 1));
        this.RowHeight = Math.Max(vanillaHeight, Game1.smallFont.LineSpacing + 12);
    }

    private int VisibleItemCount => Math.Min(this.VisibleRows, this.Items.Count);
    private int MaxScroll => Math.Max(0, this.Items.Count - this.VisibleItemCount);
    private int ButtonWidth => OptionsDropDown.dropDownButtonSource.Width * Scale;
    private int DropdownListGap => 4;


    public Rectangle GetListBounds()
    {
        int listHeight = this.VisibleItemCount * this.RowHeight + ListPad * 2;
        int listYBelow = this.FieldBounds.Bottom + this.DropdownListGap;
        int viewportBottom = Game1.uiViewport.Height - 16;
        int y = listYBelow + listHeight > viewportBottom
            ? Math.Max(16, this.FieldBounds.Y - this.DropdownListGap - listHeight)
            : listYBelow;

        int listWidth = Math.Max(96, this.FieldBounds.Width - this.ButtonWidth - ScrollbarGap);
        return new Rectangle(this.FieldBounds.X, y, listWidth, listHeight);
    }

    public void Close()
    {
        this.IsOpen = false;
        this.IsDraggingThumb = false;
    }

    public void Open()
    {
        this.IsOpen = true;
        this.ScrollIndex = Math.Clamp(this.SelectedIndex - 1, 0, this.MaxScroll);
    }

    public bool TryHandleLeftClick(int x, int y, out bool changed)
    {
        changed = false;
        if (this.FieldBounds.Contains(x, y))
        {
            this.IsOpen = !this.IsOpen;
            if (this.IsOpen)
                this.ScrollIndex = Math.Clamp(this.SelectedIndex - 1, 0, this.MaxScroll);
            return true;
        }

        if (!this.IsOpen)
            return false;

        Rectangle listBounds = this.GetListBounds();
        Rectangle track = this.GetScrollbarTrackRect(listBounds);
        if (!listBounds.Contains(x, y) && !(this.MaxScroll > 0 && track.Contains(x, y)))
        {
            this.Close();
            return true;
        }

        if (this.MaxScroll > 0 && track.Contains(x, y))
        {
            Rectangle thumb = this.GetScrollbarThumbRect(track);
            if (!thumb.Contains(x, y))
                this.ScrollIndex = this.ScrollFromTrackClick(track, y);
            this.BeginThumbDrag(y);
            return true;
        }

        Rectangle rowsRect = this.GetRowsRect(listBounds);
        if (!rowsRect.Contains(x, y))
            return true;

        int index = this.ScrollIndex + (y - rowsRect.Y) / this.RowHeight;
        if (index >= 0 && index < this.Items.Count)
        {
            this.SelectedIndex = index;
            changed = true;
        }

        this.Close();
        return true;
    }

    public bool TryStartDrag(int x, int y)
    {
        if (!this.IsOpen || this.MaxScroll <= 0)
            return false;

        Rectangle listBounds = this.GetListBounds();
        Rectangle track = this.GetScrollbarTrackRect(listBounds);
        Rectangle thumb = this.GetScrollbarThumbRect(track);
        if (!thumb.Contains(x, y))
            return false;

        this.BeginThumbDrag(y);
        return true;
    }

    private void BeginThumbDrag(int y)
    {
        this.IsDraggingThumb = true;
        this.DragStartY = y;
        this.DragStartScrollIndex = this.ScrollIndex;
    }

    public bool HandleDrag(int x, int y)
    {
        if (!this.IsOpen || !this.IsDraggingThumb || this.MaxScroll <= 0)
            return false;

        Rectangle track = this.GetScrollbarTrackRect(this.GetListBounds());
        Rectangle thumb = this.GetScrollbarThumbRect(track);
        int travel = Math.Max(1, track.Height - thumb.Height);
        float scrollPerPixel = this.MaxScroll / (float)travel;
        this.ScrollIndex = Math.Clamp(this.DragStartScrollIndex + (int)Math.Round((y - this.DragStartY) * scrollPerPixel), 0, this.MaxScroll);
        return true;
    }

    public void EndDrag() => this.IsDraggingThumb = false;

    public void MoveSelection(int delta)
    {
        if (this.Items.Count == 0)
            return;

        this.SelectedIndex = Math.Clamp(this.SelectedIndex + delta, 0, this.Items.Count - 1);
        this.EnsureSelectedItemVisible();
    }

    private void EnsureSelectedItemVisible()
    {
        if (!this.IsOpen)
            return;

        if (this.SelectedIndex < this.ScrollIndex)
            this.ScrollIndex = this.SelectedIndex;
        else if (this.SelectedIndex >= this.ScrollIndex + this.VisibleItemCount)
            this.ScrollIndex = this.SelectedIndex - this.VisibleItemCount + 1;

        this.ScrollIndex = Math.Clamp(this.ScrollIndex, 0, this.MaxScroll);
    }

    public bool TryHandleScrollWheel(int direction)
    {
        if (!this.IsOpen || this.MaxScroll <= 0)
            return false;

        Rectangle listBounds = this.GetListBounds();
        Rectangle track = this.GetScrollbarTrackRect(listBounds);
        int mouseX = Game1.getMouseX();
        int mouseY = Game1.getMouseY();
        if (!listBounds.Contains(mouseX, mouseY) && !track.Contains(mouseX, mouseY) && !this.FieldBounds.Contains(mouseX, mouseY))
            return false;

        this.ScrollIndex = Math.Clamp(this.ScrollIndex + (direction > 0 ? -1 : 1), 0, this.MaxScroll);
        return true;
    }

    public string SelectedValue => this.SelectedIndex < 0 || this.SelectedIndex >= this.Items.Count ? string.Empty : this.Items[this.SelectedIndex];

    public void DrawLabeledDropdown(SpriteBatch b, string label)
    {
        Utility.drawTextWithShadow(
            b,
            label,
            Game1.smallFont,
            new Vector2(this.FieldBounds.X + 8, this.FieldBounds.Y - Game1.smallFont.LineSpacing - 4),
            Game1.textColor);

        int buttonWidth = this.ButtonWidth;
        int buttonHeight = OptionsDropDown.dropDownButtonSource.Height * Scale;
        int fieldWidth = Math.Max(1, this.FieldBounds.Width - buttonWidth);

        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            OptionsDropDown.dropDownBGSource,
            this.FieldBounds.X,
            this.FieldBounds.Y,
            fieldWidth,
            buttonHeight,
            Color.White,
            Scale,
            false);

        b.Draw(
            Game1.mouseCursors,
            new Vector2(this.FieldBounds.X + fieldWidth, this.FieldBounds.Y),
            OptionsDropDown.dropDownButtonSource,
            Color.White,
            0f,
            Vector2.Zero,
            Scale,
            SpriteEffects.None,
            0f);

        DrawClippedText(
            b,
            this.SelectedValue,
            Game1.smallFont,
            new Vector2(this.FieldBounds.X + 12, this.FieldBounds.Y + (buttonHeight - Game1.smallFont.LineSpacing) / 2f + 2),
            Game1.textColor,
            fieldWidth - 24);
    }

    public void DrawOpenList(SpriteBatch b)
    {
        if (!this.IsOpen)
            return;

        Rectangle listBounds = this.GetListBounds();
        IClickableMenu.drawTextureBox(b, Game1.menuTexture, MenuBoxSource, listBounds.X, listBounds.Y, listBounds.Width, listBounds.Height, Color.White, 1f, false);

        Rectangle rowsRect = this.GetRowsRect(listBounds);
        int rows = this.VisibleItemCount;
        for (int row = 0; row < rows; row++)
        {
            int index = this.ScrollIndex + row;
            if (index >= this.Items.Count)
                break;

            Rectangle rowRect = new(rowsRect.X, rowsRect.Y + row * this.RowHeight, rowsRect.Width, this.RowHeight);
            if (rowRect.Contains(Game1.getMouseX(), Game1.getMouseY()))
                b.Draw(Game1.fadeToBlackRect, rowRect, Color.Black * 0.06f);

            DrawClippedText(
                b,
                this.Items[index],
                Game1.smallFont,
                new Vector2(rowRect.X + 10, rowRect.Y + (rowRect.Height - Game1.smallFont.LineSpacing) / 2f + 2),
                Game1.textColor,
                rowRect.Width - 20);
        }

        this.DrawScrollbar(b, listBounds);
    }

    private Rectangle GetRowsRect(Rectangle listBounds)
    {
        return new Rectangle(listBounds.X + ListPad, listBounds.Y + ListPad, listBounds.Width - ListPad * 2, listBounds.Height - ListPad * 2);
    }

    private Rectangle GetScrollbarTrackRect(Rectangle listBounds)
    {
        int arrowColumnX = this.FieldBounds.Right - this.ButtonWidth;
        int trackX = arrowColumnX + Math.Max(0, (this.ButtonWidth - ScrollbarWidth) / 2);
        return new Rectangle(trackX, listBounds.Y + ListPad, ScrollbarWidth, listBounds.Height - ListPad * 2);
    }

    private Rectangle GetScrollbarThumbRect(Rectangle track)
    {
        if (this.MaxScroll <= 0)
            return Rectangle.Empty;

        float visibleRatio = Math.Min(1f, this.VisibleItemCount / (float)Math.Max(1, this.Items.Count));
        int thumbHeight = Math.Max(44, Math.Min((int)Math.Round(track.Height * visibleRatio), track.Height));
        int travel = Math.Max(1, track.Height - thumbHeight);
        int y = track.Y + (int)Math.Round(travel * (this.ScrollIndex / (float)this.MaxScroll));
        return new Rectangle(track.X, y, track.Width, thumbHeight);
    }

    private int ScrollFromTrackClick(Rectangle track, int y)
    {
        Rectangle thumb = this.GetScrollbarThumbRect(track);
        int travel = Math.Max(1, track.Height - thumb.Height);
        int targetTop = Math.Clamp(y - thumb.Height / 2, track.Y, track.Bottom - thumb.Height);
        return Math.Clamp((int)Math.Round((targetTop - track.Y) / (float)travel * this.MaxScroll), 0, this.MaxScroll);
    }

    private void DrawScrollbar(SpriteBatch b, Rectangle listBounds)
    {
        if (this.MaxScroll <= 0)
            return;

        Rectangle track = this.GetScrollbarTrackRect(listBounds);
        IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ScrollbarRunnerSource, track.X, track.Y, track.Width, track.Height, Color.White, Scale, false);
        Rectangle thumb = this.GetScrollbarThumbRect(track);
        b.Draw(Game1.mouseCursors, thumb, ScrollbarThumbSource, Color.White);
    }

    private static void DrawClippedText(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, int maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
        {
            Utility.drawTextWithShadow(b, text, font, position, color);
            return;
        }

        const string ellipsis = "…";
        while (text.Length > 0 && font.MeasureString(text + ellipsis).X > maxWidth)
            text = text[..^1];

        Utility.drawTextWithShadow(b, text + ellipsis, font, position, color);
    }
}
