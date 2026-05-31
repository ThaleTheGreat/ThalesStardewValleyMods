using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;
using System;

namespace WarpMasterFramework;

/// <summary>
/// Local vanilla-first UI adapter for Warp Master Framework.
/// Uses vanilla menu textures, OptionsDropDown textures/metrics, TextBox controls,
/// and mouseCursors scrollbars where practical.
/// </summary>
internal static class WarpMasterFrameworkGmcmUi
{
    internal static readonly Rectangle MenuBoxSource = new(0, 256, 60, 60);

    private static readonly Rectangle ScrollRunnerSource = new(403, 383, 6, 6);
    private static readonly Rectangle ScrollThumbSource = new(435, 463, 6, 10);

    internal static int RowHeight => Math.Max(Game1.smallFont.LineSpacing + 18, GetDropdownButtonHeight());
    internal const int ScrollbarWidth = 24;

    /// <summary>
    /// Use vanilla dropdown sizing instead of a magic row-height constant.
    /// OptionsDropDown's background source is a 9-slice texture scaled by 4;
    /// combined with the vanilla small-font line height this yields the same clean
    /// spacing used by GMCM/SpaceShared dropdown rows, while still adapting if the
    /// vanilla texture or font metrics change.
    /// </summary>
    internal static int DropdownRowHeight => Math.Max(Game1.smallFont.LineSpacing, GetDropdownButtonHeight());
    internal static int GetDropdownButtonHeight()
    {
        return OptionsDropDown.dropDownButtonSource.Height * 4;
    }
internal static void DrawMenuBox(SpriteBatch b, Rectangle rect)
    {
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            MenuBoxSource,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            Color.White,
            1f,
            false
        );
    }

    internal static void DrawTitleBox(SpriteBatch b, Rectangle rect, string title)
    {
        DrawMenuBox(b, rect);
        StardewValley.BellsAndWhistles.SpriteText.drawStringHorizontallyCenteredAt(
            b,
            title,
            rect.X + rect.Width / 2,
            rect.Y + 12,
            999999,
            -1,
            999999,
            1f,
            0.88f
        );
    }
internal static void DrawLabel(SpriteBatch b, string text, Rectangle row, Color color)
    {
        Utility.drawTextWithShadow(
            b,
            text ?? "",
            Game1.smallFont,
            new Vector2(row.X, row.Y + (row.Height - Game1.smallFont.LineSpacing) / 2f),
            color
        );
    }

    internal static void DrawDropdownField(SpriteBatch b, Rectangle rect, string text, SpriteFont font, Color textColor, bool open)
    {
        // OptionsDropDown-backed: use the vanilla dropdown background/button source rectangles.
        int buttonWidth = GetDropdownButtonWidth();
        int buttonHeight = GetDropdownButtonHeight();
        int fieldWidth = Math.Max(1, rect.Width - buttonWidth);
        int y = rect.Y + Math.Max(0, (rect.Height - buttonHeight) / 2);

        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            OptionsDropDown.dropDownBGSource,
            rect.X,
            y,
            fieldWidth,
            buttonHeight,
            Color.White,
            4f,
            false
        );

        DrawClippedText(
            b,
            font,
            text ?? "",
            new Vector2(rect.X + 4, y + (buttonHeight - font.LineSpacing) / 2f),
            Math.Max(1, fieldWidth - 12),
            textColor
        );

        b.Draw(
            Game1.mouseCursors,
            new Vector2(rect.X + fieldWidth, y),
            OptionsDropDown.dropDownButtonSource,
            Color.White,
            0f,
            Vector2.Zero,
            4f,
            SpriteEffects.None,
            0f
        );
    }

    internal static void DrawListPanel(SpriteBatch b, Rectangle rect)
    {
        // Expanded vanilla/GMCM dropdown list body. No custom purple blocks.
        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            OptionsDropDown.dropDownBGSource,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            Color.White,
            4f,
            false
        );
    }

    internal static void DrawHoverOverlay(SpriteBatch b, Rectangle rect)
    {
        b.Draw(Game1.fadeToBlackRect, rect, Color.Black * 0.06f);
    }

    internal static void DrawFlatButton(SpriteBatch b, Rectangle rect, string text, Color textColor)
    {
        IClickableMenu.drawTextureBox(
            b,
            Game1.menuTexture,
            MenuBoxSource,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height,
            Color.White,
            1f,
            false
        );

        Vector2 textSize = Game1.smallFont.MeasureString(text);
        b.DrawString(
            Game1.smallFont,
            text,
            new Vector2(rect.X + (rect.Width - textSize.X) / 2f, rect.Y + (rect.Height - textSize.Y) / 2f),
            textColor
        );
    }


    internal static void DrawScrollbar(SpriteBatch b, Rectangle bounds, int totalItems, int visibleItems, int offset)
    {
        int maxOffset = Math.Max(0, totalItems - Math.Max(1, visibleItems));
        if (maxOffset <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
            return;

        IClickableMenu.drawTextureBox(
            b,
            Game1.mouseCursors,
            ScrollRunnerSource,
            bounds.X,
            bounds.Y,
            bounds.Width,
            bounds.Height,
            Color.White,
            4f,
            false
        );

        Rectangle thumb = GetThumbRect(bounds, totalItems, visibleItems, offset);
        b.Draw(Game1.mouseCursors, thumb, ScrollThumbSource, Color.White);
    }

    internal static Rectangle GetThumbRect(Rectangle bounds, int totalItems, int visibleItems, int offset)
    {
        int maxOffset = Math.Max(0, totalItems - Math.Max(1, visibleItems));
        int height = Math.Min(GetDropdownButtonHeight(), Math.Max(1, bounds.Height));
        int minY = bounds.Y;
        int maxY = bounds.Bottom - height;
        float scrollRatio = maxOffset == 0 ? 0f : offset / (float)maxOffset;
        int y = (int)Math.Round(minY + (maxY - minY) * scrollRatio);
        return new Rectangle(bounds.X, y, bounds.Width, height);
    }

    internal static void DrawClippedText(SpriteBatch b, SpriteFont font, string text, Vector2 position, float maxWidth, Color color)
    {
        text ??= "";
        if (font.MeasureString(text).X <= maxWidth)
        {
            Utility.drawTextWithShadow(b, text, font, position, color);
            return;
        }

        const string ellipsis = "...";
        while (text.Length > 0 && font.MeasureString(text + ellipsis).X > maxWidth)
            text = text[..^1];

        Utility.drawTextWithShadow(b, text + ellipsis, font, position, color);
    }

    internal struct VanillaDropdownLayout
    {
        public Rectangle ListRect;
        public Rectangle ItemsRect;
        public Rectangle ScrollbarRect;
        public int VisibleItems;
        public int TotalItems;
        public int MaxScroll;
    }

    internal static int GetDropdownButtonWidth()
    {
        return OptionsDropDown.dropDownButtonSource.Width * 4;
    }

    internal static VanillaDropdownLayout CreateDropdownLayout(Rectangle fieldRect, int totalItems, int itemHeight, int maxVisibleItems, bool listExcludesButton = true)
    {
        int buttonWidth = GetDropdownButtonWidth();
        int visible = Math.Max(1, Math.Min(Math.Max(1, maxVisibleItems), Math.Max(1, totalItems)));

        // Keep the expanded list aligned with the whole dropdown field.
        // The scrollbar track sits under the arrow button column, matching GMCM's visual alignment.
        int listWidth = fieldRect.Width;
        int pad = Math.Max(4, OptionsDropDown.dropDownBGSource.Height * 2);

        Rectangle listRect = new(fieldRect.X, fieldRect.Bottom + 4, listWidth, visible * itemHeight + (pad * 2));
        Rectangle inner = new(listRect.X + pad, listRect.Y + pad, Math.Max(1, listRect.Width - (pad * 2)), Math.Max(1, listRect.Height - (pad * 2)));

        bool needsScrollbar = totalItems > visible;
        int scrollbarWidth = needsScrollbar ? Math.Max(ScrollbarWidth, buttonWidth - pad) : 0;
        Rectangle scrollbarRect = needsScrollbar
            ? new Rectangle(Math.Max(inner.X, fieldRect.Right - buttonWidth), inner.Y, scrollbarWidth, inner.Height)
            : Rectangle.Empty;

        Rectangle itemsRect = needsScrollbar
            ? new Rectangle(inner.X, inner.Y, Math.Max(1, scrollbarRect.X - pad - inner.X), inner.Height)
            : inner;

        return new VanillaDropdownLayout
        {
            ListRect = listRect,
            ItemsRect = itemsRect,
            ScrollbarRect = scrollbarRect,
            VisibleItems = visible,
            TotalItems = totalItems,
            MaxScroll = Math.Max(0, totalItems - visible)
        };
    }

    internal static int CenterOnSelection(int selectedIndex, int totalItems, int visibleItems)
    {
        int maxScroll = Math.Max(0, totalItems - Math.Max(1, visibleItems));
        if (selectedIndex < 0)
            return 0;

        return Math.Clamp(selectedIndex - (visibleItems / 2), 0, maxScroll);
    }

    internal static int ScrollOffset(int currentScroll, int delta, int totalItems, int visibleItems)
    {
        int maxScroll = Math.Max(0, totalItems - Math.Max(1, visibleItems));
        if (delta > 0)
            currentScroll--;
        else if (delta < 0)
            currentScroll++;

        return Math.Clamp(currentScroll, 0, maxScroll);
    }

    internal static int GetIndexAtPoint(Point mouse, VanillaDropdownLayout layout, int scrollIndex, int itemHeight)
    {
        if (!layout.ItemsRect.Contains(mouse))
            return -1;

        int row = (mouse.Y - layout.ItemsRect.Y) / itemHeight;
        if (row < 0 || row >= layout.VisibleItems)
            return -1;

        int idx = scrollIndex + row;
        return idx >= 0 && idx < layout.TotalItems ? idx : -1;
    }

    internal static bool TryBeginScrollbarDrag(Point mouse, VanillaDropdownLayout layout, int scrollIndex, out int newScroll, out int dragOffsetY)
    {
        newScroll = scrollIndex;
        dragOffsetY = 0;

        if (layout.TotalItems <= layout.VisibleItems || layout.MaxScroll <= 0 || !layout.ScrollbarRect.Contains(mouse))
            return false;

        Rectangle thumb = GetThumbRect(layout.ScrollbarRect, layout.TotalItems, layout.VisibleItems, scrollIndex);
        if (thumb.Contains(mouse))
        {
            dragOffsetY = mouse.Y - thumb.Y;
            return true;
        }

        int desiredThumbTop = mouse.Y - (thumb.Height / 2);
        desiredThumbTop = Math.Clamp(desiredThumbTop, layout.ScrollbarRect.Y, layout.ScrollbarRect.Bottom - thumb.Height);

        int trackSpan = layout.ScrollbarRect.Height - thumb.Height;
        if (trackSpan > 0)
        {
            float t = (desiredThumbTop - layout.ScrollbarRect.Y) / (float)trackSpan;
            newScroll = Math.Clamp((int)Math.Round(t * layout.MaxScroll), 0, layout.MaxScroll);
            dragOffsetY = thumb.Height / 2;
        }

        return true;
    }

    internal static int UpdateScrollbarDrag(int mouseY, int dragOffsetY, VanillaDropdownLayout layout)
    {
        if (layout.TotalItems <= layout.VisibleItems || layout.MaxScroll <= 0)
            return 0;

        Rectangle thumb = GetThumbRect(layout.ScrollbarRect, layout.TotalItems, layout.VisibleItems, 0);
        int trackSpan = layout.ScrollbarRect.Height - thumb.Height;
        if (trackSpan <= 0)
            return 0;

        int newThumbTop = mouseY - dragOffsetY;
        newThumbTop = Math.Clamp(newThumbTop, layout.ScrollbarRect.Y, layout.ScrollbarRect.Bottom - thumb.Height);

        float t = (newThumbTop - layout.ScrollbarRect.Y) / (float)trackSpan;
        return Math.Clamp((int)Math.Round(t * layout.MaxScroll), 0, layout.MaxScroll);
    }

}
