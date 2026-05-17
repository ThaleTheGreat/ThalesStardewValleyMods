using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GMCMAdvancedSearch;

internal sealed class SearchMenu : IClickableMenu
{
    private static readonly Rectangle MenuBoxSource = new(0, 256, 60, 60);

    private readonly string title;
    private readonly bool showUniqueId;
    private readonly bool showResultDetails;
    private readonly bool showModTooltips;
    private readonly List<GmcmOptionRecord> all;
    private readonly List<GmcmOptionRecord> filtered = new();
    private readonly Func<IManifest, bool> openMod;
    private readonly SpriteFont optionFont = Game1.dialogueFont;
    private readonly SpriteFont detailFont = Game1.smallFont;
    private readonly GmcmVerticalScrollbar scrollbar = new();

    private TextBox searchBox = null!;
    private Rectangle searchRowRect;
    private Rectangle searchBoxRect;
    private Rectangle resultsRect;
    private string lastSearch = "";
    private int selectedIndex;
    private int scrollOffset;
    private int hoveredIndex = -1;
    private readonly bool wasMouseVisibleOnOpen;
    private bool isClosing;

    private bool titleInPosition = true;

    private const int OuterPad = 32;
    private const int SearchRowHeight = 72;
    private const int StatusHeight = 44;
    private const int ScrollbarWidth = GmcmVerticalScrollbar.WidthValue;
    private const int ScrollbarGap = 12;
    private const int ScrollbarOutsideOffset = 6;

    private int RowHeight => 78 + (showResultDetails ? 20 : 0) + (showUniqueId ? 20 : 0);
    private int ItemsPerPage => Math.Max(1, resultsRect.Height / RowHeight);
    private int MaxScroll => Math.Max(0, filtered.Count - ItemsPerPage);

    public SearchMenu(string title, bool showUniqueId, bool showResultDetails, bool showModTooltips, List<GmcmOptionRecord> records, Func<IManifest, bool> openMod)
        : base(0, 0, 0, 0, false)
    {
        this.title = title;
        this.showUniqueId = showUniqueId;
        this.showResultDetails = showResultDetails;
        this.showModTooltips = showModTooltips;
        this.openMod = openMod;
        wasMouseVisibleOnOpen = Game1.game1.IsMouseVisible;
        Game1.game1.IsMouseVisible = false;

        all = records
            .Where(r => r != null)
            .OrderBy(r => r.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.PrimaryText, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        filtered.AddRange(all);

        InitializeLayout();
        searchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites/textBox"), null, Game1.smallFont, Game1.textColor)
        {
            X = searchBoxRect.X,
            Y = searchBoxRect.Y,
            Width = searchBoxRect.Width,
            Text = ""
        };
        searchBox.SelectMe();
        SyncScrollbar();
    }

    public void CloseFromInput()
    {
        if (isClosing)
            return;

        isClosing = true;
        if (Game1.keyboardDispatcher.Subscriber == searchBox)
            Game1.keyboardDispatcher.Subscriber = null;
        searchBox.Selected = false;
        exitThisMenu();
    }

    protected override void cleanupBeforeExit()
    {
        if (Game1.keyboardDispatcher.Subscriber == searchBox)
            Game1.keyboardDispatcher.Subscriber = null;
        searchBox.Selected = false;
        Game1.game1.IsMouseVisible = wasMouseVisibleOnOpen;
        base.cleanupBeforeExit();
    }

    public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
    {
        base.gameWindowSizeChanged(oldBounds, newBounds);
        InitializeLayout();
        searchBox.X = searchBoxRect.X;
        searchBox.Y = searchBoxRect.Y;
        searchBox.Width = searchBoxRect.Width;
        ClampScroll();
        SyncScrollbar();
    }

    private void InitializeLayout()
    {
        int viewportWidth = Game1.uiViewport.Width;
        int viewportHeight = Game1.uiViewport.Height;
        width = Math.Min(1160, Math.Max(680, viewportWidth - 80));
        height = Math.Min(880, Math.Max(540, viewportHeight - 80));
        xPositionOnScreen = (viewportWidth - width) / 2;
        yPositionOnScreen = (viewportHeight - height) / 2;

        int innerX = xPositionOnScreen + OuterPad;
        int innerY = yPositionOnScreen + OuterPad;
        int innerWidth = width - OuterPad * 2;
        int innerHeight = height - OuterPad * 2;
        int titleHeight = SpriteText.getHeightOfString(title, 999999) + 18;

        searchRowRect = new Rectangle(innerX, innerY + titleHeight, innerWidth, SearchRowHeight);
        int labelWidth = 150;
        searchBoxRect = new Rectangle(searchRowRect.X + labelWidth, searchRowRect.Y + 12, searchRowRect.Width - labelWidth, 48);

        int statusY = searchRowRect.Bottom + 4;
        int resultsY = statusY + StatusHeight + 4;
        int resultsHeight = innerY + innerHeight - resultsY;
        int resultsRight = xPositionOnScreen + width - OuterPad;
        resultsRect = new Rectangle(innerX, resultsY, Math.Max(1, resultsRight - innerX), resultsHeight);

        int scrollbarX = xPositionOnScreen + width + ScrollbarOutsideOffset;
        Rectangle scrollbarBounds = new(scrollbarX, resultsRect.Y, ScrollbarWidth, Math.Max(1, resultsRect.Height));
        scrollbar.SetBounds(scrollbarBounds);
    }

    public override void update(GameTime time)
    {
        _ = titleInPosition;
        base.update(time);
        Game1.game1.IsMouseVisible = false;
        searchBox.Update();

        if (!isClosing && Game1.keyboardDispatcher.Subscriber != searchBox)
            Game1.keyboardDispatcher.Subscriber = searchBox;
        searchBox.Selected = !isClosing;

        string current = searchBox.Text ?? "";
        if (!string.Equals(current, lastSearch, StringComparison.Ordinal))
        {
            lastSearch = current;
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        string query = (searchBox.Text ?? "").Trim();
        filtered.Clear();

        if (string.IsNullOrWhiteSpace(query))
        {
            filtered.AddRange(all);
        }
        else
        {
            string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            filtered.AddRange(all.Where(r => r.Matches(terms)));
        }

        selectedIndex = 0;
        scrollOffset = 0;
        ClampScroll();
        SyncScrollbar();
    }

    public override void receiveKeyPress(Keys key)
    {
        if (key == Keys.Escape)
        {
            CloseFromInput();
            return;
        }

        if (Game1.options.doesInputListContain(Game1.options.menuButton, key) && searchBox.Selected)
            return;

        switch (key)
        {
            case Keys.Enter:
                OpenSelected();
                return;
            case Keys.Up:
                MoveSelection(-1);
                return;
            case Keys.Down:
                MoveSelection(1);
                return;
            case Keys.PageUp:
                MoveSelection(-ItemsPerPage);
                return;
            case Keys.PageDown:
                MoveSelection(ItemsPerPage);
                return;
            case Keys.Home:
                selectedIndex = 0;
                scrollOffset = 0;
                EnsureSelectionVisible();
                SyncScrollbar();
                return;
            case Keys.End:
                selectedIndex = Math.Max(0, filtered.Count - 1);
                scrollOffset = MaxScroll;
                EnsureSelectionVisible();
                SyncScrollbar();
                return;
        }
    }

    private void MoveSelection(int amount)
    {
        if (filtered.Count <= 0)
            return;
        selectedIndex = Math.Clamp(selectedIndex + amount, 0, filtered.Count - 1);
        EnsureSelectionVisible();
        SyncScrollbar();
    }

    public override void receiveScrollWheelAction(int direction)
    {
        base.receiveScrollWheelAction(direction);
        if (!resultsRect.Contains(Game1.getMouseX(), Game1.getMouseY()) && !scrollbar.ContainsPoint(Game1.getMouseX(), Game1.getMouseY()))
            return;

        scrollOffset = Math.Clamp(scrollOffset + (direction > 0 ? -1 : 1), 0, MaxScroll);
        SyncScrollbar();
    }

    public override void receiveLeftClick(int x, int y, bool playSound = true)
    {
        base.receiveLeftClick(x, y, playSound);
        searchBox.SelectMe();

        if (searchRowRect.Contains(x, y))
            return;

        if (scrollbar.ReceiveLeftClick(x, y, out int newOffset))
        {
            scrollOffset = newOffset;
            return;
        }

        if (resultsRect.Contains(x, y))
        {
            int clicked = scrollOffset + (y - resultsRect.Y) / RowHeight;
            if (clicked >= 0 && clicked < filtered.Count)
            {
                selectedIndex = clicked;
                OpenSelected();
            }
        }
    }

    public override void leftClickHeld(int x, int y)
    {
        base.leftClickHeld(x, y);
        if (scrollbar.LeftClickHeld(x, y, out int newOffset))
            scrollOffset = newOffset;
    }

    public override void releaseLeftClick(int x, int y)
    {
        base.releaseLeftClick(x, y);
        scrollbar.ReleaseLeftClick();
    }

    public override void performHoverAction(int x, int y)
    {
        base.performHoverAction(x, y);
        hoveredIndex = GetResultIndexAt(x, y);
    }

    private int GetResultIndexAt(int x, int y)
    {
        if (!resultsRect.Contains(x, y))
            return -1;

        int index = scrollOffset + (y - resultsRect.Y) / RowHeight;
        return index >= 0 && index < filtered.Count ? index : -1;
    }

    private void OpenSelected()
    {
        if (filtered.Count == 0)
            return;

        selectedIndex = Math.Clamp(selectedIndex, 0, filtered.Count - 1);
        GmcmOptionRecord target = filtered[selectedIndex];
        if (openMod(target.Mod))
            return;

        Game1.showRedMessage("That entry couldn't be opened in GMCM.");
        all.RemoveAll(r => r.UniqueId == target.UniqueId);
        filtered.RemoveAll(r => r.UniqueId == target.UniqueId);
        ClampScroll();
        SyncScrollbar();
    }

    private void ClampScroll()
    {
        scrollOffset = Math.Clamp(scrollOffset, 0, MaxScroll);
        selectedIndex = Math.Clamp(selectedIndex, 0, Math.Max(0, filtered.Count - 1));
        EnsureSelectionVisible();
    }

    private void EnsureSelectionVisible()
    {
        if (filtered.Count == 0)
            return;
        if (selectedIndex < scrollOffset)
            scrollOffset = selectedIndex;
        if (selectedIndex > scrollOffset + ItemsPerPage - 1)
            scrollOffset = Math.Min(MaxScroll, selectedIndex - ItemsPerPage + 1);
    }

    private void SyncScrollbar()
    {
        scrollbar.SetMetrics(filtered.Count, ItemsPerPage, scrollOffset);
    }

    public override void draw(SpriteBatch b)
    {
        b.Draw(Game1.fadeToBlackRect, Game1.graphics.GraphicsDevice.Viewport.Bounds, Color.Black * 0.75f);
        drawTextureBox(b, Game1.menuTexture, MenuBoxSource, xPositionOnScreen, yPositionOnScreen, width, height, Color.White, 1f, false);

        SpriteText.drawStringHorizontallyCenteredAt(b, title, xPositionOnScreen + width / 2, yPositionOnScreen + OuterPad - 4, 999999, -1, 999999, 1f, 0.88f);

        DrawSearchRow(b);
        DrawStatusLine(b);
        DrawRows(b);
        scrollbar.Draw(b);
        DrawHoveredModTooltip(b);
        drawMouse(b);
    }

    private void DrawSearchRow(SpriteBatch b)
    {
        Utility.drawTextWithShadow(b, "Search", detailFont, new Vector2(searchRowRect.X, searchRowRect.Y + 19), Game1.textColor);
        searchBox.Draw(b);
        if (string.IsNullOrEmpty(searchBox.Text) && !searchBox.Selected)
        {
            Utility.drawTextWithShadow(b, "Search options, tooltips, field IDs, and config keys…", Game1.smallFont,
                new Vector2(searchBoxRect.X + 10, searchBoxRect.Y + 14), new Color(120, 120, 120));
        }
    }

    private void DrawStatusLine(SpriteBatch b)
    {
        DrawBoldTextWithShadow(b, $"{filtered.Count} result(s). Click a result to open its mod config in GMCM.", optionFont,
            new Vector2(resultsRect.X, searchRowRect.Bottom + 5), Game1.textColor);
    }

    private static void DrawBoldTextWithShadow(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color)
    {
        Utility.drawTextWithShadow(b, text, font, position, color);
        Utility.drawTextWithShadow(b, text, font, position + new Vector2(1f, 0f), color);
        Utility.drawTextWithShadow(b, text, font, position + new Vector2(0f, 1f), color);
        Utility.drawTextWithShadow(b, text, font, position + new Vector2(1f, 1f), color);
    }

    private void DrawRows(SpriteBatch b)
    {
        int end = Math.Min(filtered.Count, scrollOffset + ItemsPerPage);
        for (int index = scrollOffset; index < end; index++)
        {
            int y = resultsRect.Y + (index - scrollOffset) * RowHeight;
            Rectangle row = new(resultsRect.X, y, resultsRect.Width, RowHeight);
            GmcmOptionRecord record = filtered[index];

            Vector2 modPos = new(row.X, row.Y + 8);
            Utility.drawTextWithShadow(b, record.ModName, detailFont, modPos, new Color(95, 66, 38));

            string primary = record.PrimaryText;
            Vector2 optionPos = new(row.X, row.Y + 28);
            DrawClippedText(b, primary, optionFont, optionPos, Game1.textColor, row.Width - 12);

            int nextDetailY = row.Y + 62;
            if (showResultDetails)
            {
                string secondary = record.SecondaryText;
                if (!string.IsNullOrWhiteSpace(secondary))
                    DrawClippedText(b, secondary, detailFont, new Vector2(row.X, nextDetailY), new Color(95, 95, 95), row.Width - 12);
                nextDetailY += 20;
            }

            if (showUniqueId)
                DrawClippedText(b, record.UniqueId, detailFont, new Vector2(row.X, nextDetailY), new Color(110, 110, 110), row.Width - 12);
        }
    }

    private void DrawHoveredModTooltip(SpriteBatch b)
    {
        if (!showModTooltips || hoveredIndex < 0 || hoveredIndex >= filtered.Count)
            return;

        GmcmOptionRecord record = filtered[hoveredIndex];
        string titleText = record.ModName;
        string description = record.Mod.Description ?? "";
        if (string.IsNullOrWhiteSpace(titleText) && string.IsNullOrWhiteSpace(description))
            return;

        DrawModTooltip(b, titleText, description);
    }

    private void DrawModTooltip(SpriteBatch b, string titleText, string description)
    {
        const int maxWidth = 520;
        const int padding = 16;
        const int gap = 8;
        const int lineGap = 2;

        string wrappedTitle = WrapText(titleText, optionFont, maxWidth - padding * 2);
        string wrappedDescription = WrapText(description, detailFont, maxWidth - padding * 2);
        string[] titleLines = SplitLines(wrappedTitle);
        string[] descriptionLines = SplitLines(wrappedDescription);

        int titleWidth = MeasureMaxWidth(optionFont, titleLines);
        int descriptionWidth = MeasureMaxWidth(detailFont, descriptionLines);
        int boxWidth = Math.Min(maxWidth, Math.Max(260, Math.Max(titleWidth, descriptionWidth) + padding * 2));
        int titleHeight = MeasureTextHeight(optionFont, titleLines, lineGap);
        int descriptionHeight = MeasureTextHeight(detailFont, descriptionLines, lineGap);
        int boxHeight = padding * 2 + titleHeight + (descriptionLines.Length > 0 ? gap + descriptionHeight : 0);

        int x = Game1.getMouseX() + 32;
        int y = Game1.getMouseY() + 32;
        Rectangle viewport = Game1.graphics.GraphicsDevice.Viewport.Bounds;
        if (x + boxWidth > viewport.Right - 16)
            x = Game1.getMouseX() - boxWidth - 32;
        if (y + boxHeight > viewport.Bottom - 16)
            y = viewport.Bottom - boxHeight - 16;
        x = Math.Max(16, x);
        y = Math.Max(16, y);

        drawTextureBox(b, Game1.menuTexture, MenuBoxSource, x, y, boxWidth, boxHeight, Color.White, 1f, false);

        Vector2 pos = new(x + padding, y + padding);
        foreach (string line in titleLines)
        {
            Utility.drawTextWithShadow(b, line, optionFont, pos, Game1.textColor);
            pos.Y += optionFont.LineSpacing + lineGap;
        }

        if (descriptionLines.Length == 0)
            return;

        pos.Y += gap;
        foreach (string line in descriptionLines)
        {
            Utility.drawTextWithShadow(b, line, detailFont, pos, Game1.textColor);
            pos.Y += detailFont.LineSpacing + lineGap;
        }
    }

    private static string WrapText(string text, SpriteFont font, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "";

        List<string> lines = new();
        foreach (string paragraph in text.Replace('\r', ' ').Split('\n'))
        {
            string current = "";
            foreach (string word in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string candidate = string.IsNullOrWhiteSpace(current) ? word : current + " " + word;
                if (font.MeasureString(candidate).X <= maxWidth || string.IsNullOrWhiteSpace(current))
                {
                    current = candidate;
                }
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }

            if (!string.IsNullOrWhiteSpace(current))
                lines.Add(current);
        }

        return string.Join("\n", lines);
    }

    private static string[] SplitLines(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? Array.Empty<string>()
            : text.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static int MeasureMaxWidth(SpriteFont font, string[] lines)
    {
        int width = 0;
        foreach (string line in lines)
            width = Math.Max(width, (int)Math.Ceiling(font.MeasureString(line).X));
        return width;
    }

    private static int MeasureTextHeight(SpriteFont font, string[] lines, int lineGap)
    {
        return lines.Length == 0 ? 0 : lines.Length * font.LineSpacing + Math.Max(0, lines.Length - 1) * lineGap;
    }

    private static void DrawClippedText(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, int maxWidth)
    {
        if (font.MeasureString(text).X <= maxWidth)
        {
            Utility.drawTextWithShadow(b, text, font, position, color);
            return;
        }

        string ellipsis = "…";
        while (text.Length > 0 && font.MeasureString(text + ellipsis).X > maxWidth)
            text = text[..^1];
        Utility.drawTextWithShadow(b, text + ellipsis, font, position, color);
    }

    private sealed class GmcmVerticalScrollbar
    {
        private static readonly Rectangle RunnerSource = new(403, 383, 6, 6);
        private static readonly Rectangle ThumbSource = new(435, 463, 6, 10);
    
        public const int WidthValue = 24;
    
        private Rectangle bounds;
        private int totalItems;
        private int visibleItems;
        private int offset;
        private bool dragging;
        private int dragGrabOffsetY;
    
        public bool IsNeeded => MaxOffset > 0;
    
        private int MaxOffset => Math.Max(0, totalItems - Math.Max(1, visibleItems));
    
        public void SetBounds(Rectangle bounds)
        {
            this.bounds = bounds;
            ClampOffset();
        }
    
        public void SetMetrics(int totalItems, int visibleItems, int offset)
        {
            this.totalItems = Math.Max(0, totalItems);
            this.visibleItems = Math.Max(1, visibleItems);
            this.offset = offset;
            ClampOffset();
        }
    
        public bool ContainsPoint(int x, int y)
        {
            return IsNeeded && bounds.Contains(x, y);
        }
    
        public bool ReceiveLeftClick(int x, int y, out int newOffset)
        {
            newOffset = offset;
            if (!ContainsPoint(x, y))
                return false;
    
            Rectangle thumb = GetThumbRect();
            if (thumb.Contains(x, y))
            {
                dragging = true;
                dragGrabOffsetY = y - thumb.Y;
            }
            else
            {
                SetOffsetFromThumbTop(y - thumb.Height / 2);
                newOffset = offset;
            }
    
            return true;
        }
    
        public bool LeftClickHeld(int x, int y, out int newOffset)
        {
            newOffset = offset;
            if (!dragging)
                return false;
    
            SetOffsetFromThumbTop(y - dragGrabOffsetY);
            newOffset = offset;
            return true;
        }
    
        public void ReleaseLeftClick()
        {
            dragging = false;
        }
    
        public void Draw(SpriteBatch b)
        {
            if (!IsNeeded)
                return;
    
            IClickableMenu.drawTextureBox(
                b,
                Game1.mouseCursors,
                RunnerSource,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                Color.White,
                4f,
                false);
    
            Rectangle thumb = GetThumbRect();
            b.Draw(Game1.mouseCursors, thumb, ThumbSource, Color.White);
        }
    
        private Rectangle GetThumbRect()
        {
            int height = Math.Max(1, bounds.Height);
            if (totalItems > 0)
            {
                float visibleRatio = Math.Min(1f, visibleItems / (float)Math.Max(1, totalItems));
                int minThumbHeight = Math.Min(bounds.Height, Math.Max(ThumbSource.Height * 4, bounds.Width * 2));
                height = Math.Max(minThumbHeight, Math.Min((int)Math.Round(bounds.Height * visibleRatio), bounds.Height));
            }

            int minY = bounds.Y;
            int maxY = bounds.Bottom - height;
            float scrollRatio = MaxOffset == 0 ? 0f : offset / (float)MaxOffset;
            int y = (int)Math.Round(minY + (maxY - minY) * scrollRatio);
            return new Rectangle(bounds.X, y, bounds.Width, height);
        }
    
        private void SetOffsetFromThumbTop(int thumbTopY)
        {
            Rectangle thumb = GetThumbRect();
            int minY = bounds.Y;
            int maxY = bounds.Bottom - thumb.Height;
            int y = Math.Clamp(thumbTopY, minY, maxY);
            offset = (int)Math.Round((maxY == minY ? 0.0 : (double)(y - minY) / (maxY - minY)) * MaxOffset);
            ClampOffset();
        }
    
        private void ClampOffset()
        {
            offset = Math.Clamp(offset, 0, MaxOffset);
        }
    }
}
