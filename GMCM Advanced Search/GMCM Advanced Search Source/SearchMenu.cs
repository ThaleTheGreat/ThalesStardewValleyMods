using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GMCMAdvancedSearch;

internal sealed class SearchMenu : IClickableMenu
{
    private static ITranslationHelper Translation = null!;
    private static readonly Rectangle MenuBoxSource = new(0, 256, 60, 60);

    private readonly string title;
    private readonly bool showResultDetails;
    private readonly bool showModTooltips;
    private readonly List<SearchResult> authorResults;
    private readonly List<SearchResult> modResults;
    private readonly List<SearchResult> optionResults;
    private readonly Dictionary<string, List<SearchResult>> optionResultsByMod;
    private readonly List<SearchResult> filtered = new();
    private readonly Func<IManifest, bool> openMod;
    private readonly SpriteFont mainFont = Game1.dialogueFont;
    private readonly SpriteFont detailFont = Game1.smallFont;
    private readonly GmcmVerticalScrollbar scrollbar = new();
    private readonly Stack<MenuState> history = new();

    private TextBox searchBox = null!;
    private Rectangle searchRowRect;
    private Rectangle searchBoxRect;
    private Rectangle resultsRect;
    private string lastSearch = "";
    private string? activeAuthor;
    private int selectedIndex;
    private int scrollOffset;
    private int hoveredIndex = -1;
    private readonly bool wasMouseVisibleOnOpen;
    private bool isClosing;
    private long nextBackInputAllowedMs;
    private long childBackInputHandledUntilMs;

    private bool titleInPosition = true;

    private const int OuterPad = 32;
    private const int SearchRowHeight = 72;
    private const int StatusHeight = 44;
    private const int ScrollbarWidth = GmcmVerticalScrollbar.WidthValue;
    private const int ScrollbarOutsideOffset = 6;

    private int RowHeight => 108 + (showResultDetails ? 22 : 0);
    private int MinimumVisibleRowHeight => 6 + mainFont.LineSpacing;
    private int ItemsPerPage => Math.Max(1, (resultsRect.Height + RowHeight - MinimumVisibleRowHeight) / RowHeight);
    private int MaxScroll => Math.Max(0, filtered.Count - ItemsPerPage);

    public SearchMenu(string title, bool showResultDetails, bool showModTooltips, List<GmcmOptionRecord> records, Func<IManifest, bool> openMod, ITranslationHelper translation)
        : base(0, 0, 0, 0, false)
    {
        Translation = translation;
        this.title = title;
        this.showResultDetails = showResultDetails;
        this.showModTooltips = showModTooltips;
        this.openMod = openMod;
        wasMouseVisibleOnOpen = Game1.game1.IsMouseVisible;
        Game1.game1.IsMouseVisible = false;

        List<ModSearchGroup> mods = records
            .Where(r => r != null)
            .GroupBy(r => r.UniqueId, StringComparer.OrdinalIgnoreCase)
            .Select(g => ModSearchGroup.Create(g.ToList()))
            .Where(g => g.Manifest != null)
            .OrderBy(g => g.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(g => g.UniqueId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        authorResults = mods
            .GroupBy(g => NormalizeAuthor(g.Author), StringComparer.CurrentCultureIgnoreCase)
            .Select(g => SearchResult.ForAuthor(g.Key, g.Count()))
            .OrderBy(r => r.Author, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        modResults = mods
            .Select(SearchResult.ForMod)
            .OrderBy(r => r.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.UniqueId, StringComparer.OrdinalIgnoreCase)
            .ToList();

        optionResults = records
            .Where(r => r != null)
            .OrderBy(r => r.ModName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.Section, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(r => r.PrimaryText, StringComparer.CurrentCultureIgnoreCase)
            .Select(SearchResult.ForOption)
            .ToList();

        optionResultsByMod = optionResults
            .GroupBy(r => r.UniqueId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(r => r.Option?.PrimaryText ?? "", StringComparer.CurrentCultureIgnoreCase).ToList(),
                StringComparer.OrdinalIgnoreCase);

        InitializeLayout();
        searchBox = new TextBox(Game1.content.Load<Texture2D>("LooseSprites/textBox"), null, Game1.smallFont, Game1.textColor)
        {
            X = searchBoxRect.X,
            Y = searchBoxRect.Y,
            Width = searchBoxRect.Width,
            Text = ""
        };
        searchBox.SelectMe();
        ApplyFilter();
    }

    public bool LetChildMenuHandleBackInput()
    {
        if (!HasOpenChildMenu())
            return false;

        childBackInputHandledUntilMs = Environment.TickCount64 + 250;
        return true;
    }

    public void BackOrCloseFromInput()
    {
        long now = Environment.TickCount64;
        if (now < childBackInputHandledUntilMs || now < nextBackInputAllowedMs)
            return;

        nextBackInputAllowedMs = now + 100;

        if (GoBack())
            return;

        Game1.playSound("bigDeSelect");
        CloseFromInput();
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

    private bool HasOpenChildMenu()
    {
        try
        {
            for (Type? type = GetType(); type != null; type = type.BaseType)
            {
                foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (field.IsStatic)
                        continue;

                    if (ValueContainsChildMenu(field.GetValue(this)))
                        return true;
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private bool ValueContainsChildMenu(object? value)
    {
        if (value == null || ReferenceEquals(value, this))
            return false;

        if (value is IClickableMenu)
            return true;

        if (value is string || value is not IEnumerable enumerable)
            return false;

        foreach (object? item in enumerable)
        {
            if (item is IClickableMenu menu && !ReferenceEquals(menu, this))
                return true;
        }

        return false;
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
        width = Math.Min(1392, Math.Max(680, viewportWidth - 80));
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
        int contentBottom = yPositionOnScreen + height - 12;
        int resultsHeight = Math.Max(1, contentBottom - resultsY);
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
        string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        filtered.Clear();

        if (terms.Length == 0)
        {
            if (!string.IsNullOrWhiteSpace(activeAuthor))
            {
                filtered.AddRange(modResults
                    .Where(r => string.Equals(r.Author, activeAuthor, StringComparison.OrdinalIgnoreCase)));
            }
            else
            {
                filtered.AddRange(authorResults);
            }
        }
        else
        {
            filtered.AddRange(authorResults.Where(r => r.MatchesAuthor(terms)));

            foreach (SearchResult modResult in modResults)
            {
                bool modNameMatches = modResult.MatchesMod(terms);
                List<SearchResult> matchingOptions = optionResultsByMod.TryGetValue(modResult.UniqueId, out List<SearchResult>? options)
                    ? options.Where(r => r.MatchesOption(terms)).ToList()
                    : new List<SearchResult>();

                if (!modNameMatches && matchingOptions.Count == 0)
                    continue;

                filtered.Add(modResult.WithRelevantOptions(matchingOptions));
            }
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
            if (LetChildMenuHandleBackInput())
                return;

            BackOrCloseFromInput();
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

    public override void receiveRightClick(int x, int y, bool playSound = true)
    {
        base.receiveRightClick(x, y, playSound);

        if (!searchBoxRect.Contains(x, y))
            return;

        searchBox.SelectMe();
        if (string.IsNullOrEmpty(searchBox.Text))
            return;

        searchBox.Text = "";
        lastSearch = "";
        ApplyFilter();
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
        SearchResult target = filtered[selectedIndex];

        if (target.Kind == SearchResultKind.Author)
        {
            PushState();
            activeAuthor = target.Author;
            searchBox.Text = "";
            lastSearch = "";
            ApplyFilter();
            Game1.playSound("shiny4");
            return;
        }

        if (target.Mod == null)
            return;

        if (openMod(target.Mod))
            return;

        Game1.showRedMessage(T("message.open-failed"));
        string failedUniqueId = target.UniqueId;
        modResults.RemoveAll(r => string.Equals(r.UniqueId, failedUniqueId, StringComparison.OrdinalIgnoreCase));
        optionResults.RemoveAll(r => string.Equals(r.UniqueId, failedUniqueId, StringComparison.OrdinalIgnoreCase));
        filtered.RemoveAll(r => string.Equals(r.UniqueId, failedUniqueId, StringComparison.OrdinalIgnoreCase));
        ClampScroll();
        SyncScrollbar();
    }


    private bool GoBack()
    {
        if (!string.IsNullOrWhiteSpace(searchBox.Text))
        {
            searchBox.Text = "";
            lastSearch = "";
            ApplyFilter();
            Game1.playSound("smallSelect");
            return true;
        }

        if (history.Count > 0)
        {
            RestoreState(history.Pop());
            Game1.playSound("smallSelect");
            return true;
        }

        return false;
    }

    private void PushState()
    {
        history.Push(new MenuState(activeAuthor, searchBox.Text ?? "", selectedIndex, scrollOffset));
    }

    private void RestoreState(MenuState state)
    {
        activeAuthor = state.ActiveAuthor;
        searchBox.Text = state.SearchText;
        lastSearch = state.SearchText;
        ApplyFilter();
        selectedIndex = state.SelectedIndex;
        scrollOffset = state.ScrollOffset;
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
        DrawHoveredTooltip(b);
        drawMouse(b);
    }

    private void DrawSearchRow(SpriteBatch b)
    {
        Utility.drawTextWithShadow(b, T("menu.search-label"), detailFont, new Vector2(searchRowRect.X, searchRowRect.Y + 19), Game1.textColor);
        searchBox.Draw(b);
        if (string.IsNullOrEmpty(searchBox.Text) && !searchBox.Selected)
        {
            Utility.drawTextWithShadow(b, T("menu.search-placeholder"), Game1.smallFont,
                new Vector2(searchBoxRect.X + 10, searchBoxRect.Y + 14), new Color(120, 120, 120));
        }
    }

    private void DrawStatusLine(SpriteBatch b)
    {
        string status = !string.IsNullOrWhiteSpace(searchBox.Text)
            ? T("status.search-results", new { count = filtered.Count })
            : string.IsNullOrWhiteSpace(activeAuthor)
                ? T("status.authors", new { count = filtered.Count })
                : T("status.author-mods", new { count = filtered.Count, author = activeAuthor });

        DrawBoldTextWithShadow(b, status, mainFont, new Vector2(resultsRect.X, searchRowRect.Bottom + 5), Game1.textColor);
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
            DrawResultRow(b, filtered[index], row, resultsRect.Bottom);
        }
    }

    private void DrawResultRow(SpriteBatch b, SearchResult record, Rectangle row, int visibleBottom)
    {
        Vector2 mainPos = new(row.X, row.Y + 6);
        Vector2 detailPos = new(row.X, row.Y + 48);
        Vector2 optionPos = new(row.X, row.Y + 70);
        int textWidth = row.Width - 12;

        DrawClippedTextIfVisible(b, record.MainText, mainFont, mainPos, Game1.textColor, textWidth, visibleBottom);

        string detailText = GetVisibleDetailText(record);
        if (!string.IsNullOrWhiteSpace(detailText))
            DrawClippedTextIfVisible(b, detailText, detailFont, detailPos, new Color(95, 66, 38), textWidth, visibleBottom);

        string optionSummary = record.OptionSummaryText;
        if (!string.IsNullOrWhiteSpace(optionSummary))
            DrawClippedTextIfVisible(b, optionSummary, detailFont, optionPos, new Color(110, 86, 55), textWidth, visibleBottom);

        int nextDetailY = row.Y + 92;
        if (showResultDetails)
        {
            string advanced = record.AdvancedText;
            if (!string.IsNullOrWhiteSpace(advanced))
                DrawClippedTextIfVisible(b, advanced, detailFont, new Vector2(row.X, nextDetailY), new Color(95, 95, 95), textWidth, visibleBottom);
        }
    }

    private string GetVisibleDetailText(SearchResult record)
    {
        if (record.Kind != SearchResultKind.Mod)
            return record.DetailText;

        List<string> pieces = new();
        if (string.IsNullOrWhiteSpace(searchBox.Text)
            && !string.IsNullOrWhiteSpace(activeAuthor)
            && string.Equals(record.Author, activeAuthor, StringComparison.OrdinalIgnoreCase))
        {
            AddIfNotBlank(pieces, record.UniqueId);
        }
        else
        {
            AddIfNotBlank(pieces, record.Author);
            AddIfNotBlank(pieces, record.UniqueId);
        }

        if (!string.IsNullOrWhiteSpace(record.NexusId))
            pieces.Add($"Nexus:{record.NexusId}");

        return string.Join(" • ", pieces);
    }

    private static void AddIfNotBlank(List<string> pieces, string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            pieces.Add(value);
    }

    private void DrawHoveredTooltip(SpriteBatch b)
    {
        if (!showModTooltips || hoveredIndex < 0 || hoveredIndex >= filtered.Count)
            return;

        SearchResult record = filtered[hoveredIndex];
        string titleText = record.TooltipTitle;
        string description = record.TooltipDescription;
        if (string.IsNullOrWhiteSpace(titleText) && string.IsNullOrWhiteSpace(description))
            return;

        DrawTooltip(b, titleText, description);
    }

    private void DrawTooltip(SpriteBatch b, string titleText, string description)
    {
        const int maxWidth = 520;
        const int padding = 16;
        const int gap = 8;
        const int lineGap = 2;

        string wrappedTitle = WrapText(titleText, mainFont, maxWidth - padding * 2);
        string wrappedDescription = WrapText(description, detailFont, maxWidth - padding * 2);
        string[] titleLines = SplitLines(wrappedTitle);
        string[] descriptionLines = SplitLines(wrappedDescription);

        int titleWidth = MeasureMaxWidth(mainFont, titleLines);
        int descriptionWidth = MeasureMaxWidth(detailFont, descriptionLines);
        int boxWidth = Math.Min(maxWidth, Math.Max(260, Math.Max(titleWidth, descriptionWidth) + padding * 2));
        int titleHeight = MeasureTextHeight(mainFont, titleLines, lineGap);
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
            Utility.drawTextWithShadow(b, line, mainFont, pos, Game1.textColor);
            pos.Y += mainFont.LineSpacing + lineGap;
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

    private static void DrawClippedTextIfVisible(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, int maxWidth, int visibleBottom)
    {
        if (position.Y + font.LineSpacing > visibleBottom)
            return;

        DrawClippedText(b, text, font, position, color, maxWidth);
    }

    private static void DrawClippedText(SpriteBatch b, string text, SpriteFont font, Vector2 position, Color color, int maxWidth)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

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

    private static string T(string key)
    {
        return Translation.Get(key).ToString();
    }

    private static string T(string key, object tokens)
    {
        return Translation.Get(key, tokens).ToString();
    }

    private sealed record MenuState(string? ActiveAuthor, string SearchText, int SelectedIndex, int ScrollOffset);

    private enum SearchResultKind
    {
        Author,
        Mod,
        Option
    }

    private sealed class SearchResult
    {
        public SearchResultKind Kind { get; init; }
        public IManifest? Mod { get; init; }
        public GmcmOptionRecord? Option { get; init; }
        public string Author { get; init; } = "";
        public string ModName { get; init; } = "";
        public string UniqueId { get; init; } = "";
        public string NexusId { get; init; } = "";
        public int ModCount { get; init; }
        public int OptionCount { get; init; }
        public IReadOnlyList<GmcmOptionRecord> RelevantOptions { get; init; } = Array.Empty<GmcmOptionRecord>();

        public string MainText => Kind switch
        {
            SearchResultKind.Author => Author,
            SearchResultKind.Mod => ModName,
            _ => ModName
        };

        public string DetailText => Kind switch
        {
            SearchResultKind.Author => T(ModCount == 1 ? "result.author-detail.one" : "result.author-detail.many", new { count = ModCount }),
            SearchResultKind.Mod => UniqueId,
            _ => Option?.PrimaryText ?? ""
        };

        public string OptionSummaryText => Kind == SearchResultKind.Mod && RelevantOptions.Count > 0
            ? string.Join("  •  ", RelevantOptions.Select(o => o.PrimaryText).Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase))
            : "";

        public string AdvancedText => Kind switch
        {
            SearchResultKind.Author => T("result.author-advanced"),
            SearchResultKind.Mod => Mod?.Description ?? "",
            _ => Option?.SecondaryText ?? ""
        };

        public string TooltipTitle => Kind switch
        {
            SearchResultKind.Author => Author,
            _ => ModName
        };

        public string TooltipDescription => Kind switch
        {
            SearchResultKind.Author => T(ModCount == 1 ? "result.author-tooltip.one" : "result.author-tooltip.many", new { count = ModCount }),
            SearchResultKind.Mod => Mod?.Description ?? "",
            _ => Mod?.Description ?? ""
        };

        public SearchResult WithRelevantOptions(IEnumerable<SearchResult> options)
        {
            return new SearchResult
            {
                Kind = Kind,
                Mod = Mod,
                Option = Option,
                Author = Author,
                ModName = ModName,
                UniqueId = UniqueId,
                NexusId = NexusId,
                ModCount = ModCount,
                OptionCount = OptionCount,
                RelevantOptions = options.Select(o => o.Option).Where(o => o != null).Cast<GmcmOptionRecord>().ToList()
            };
        }

        public bool MatchesAuthor(string[] terms)
        {
            return TermsMatch(Author, terms);
        }

        public bool MatchesMod(string[] terms)
        {
            return TermsMatch(ModName, terms)
                || TermsMatchSearchableUniqueId(UniqueId, Author, terms)
                || TermsMatchNexusId(NexusId, terms);
        }

        public bool MatchesOption(string[] terms)
        {
            return Option?.Matches(terms, Author, ModName, UniqueId) == true;
        }

        public static SearchResult ForAuthor(string author, int modCount)
        {
            return new SearchResult
            {
                Kind = SearchResultKind.Author,
                Author = NormalizeAuthor(author),
                ModCount = modCount
            };
        }

        public static SearchResult ForMod(ModSearchGroup group)
        {
            return new SearchResult
            {
                Kind = SearchResultKind.Mod,
                Mod = group.Manifest,
                Author = group.Author,
                ModName = group.ModName,
                UniqueId = group.UniqueId,
                NexusId = group.NexusId,
                OptionCount = group.OptionCount
            };
        }

        public static SearchResult ForOption(GmcmOptionRecord option)
        {
            return new SearchResult
            {
                Kind = SearchResultKind.Option,
                Mod = option.Mod,
                Option = option,
                Author = NormalizeAuthor(option.Mod.Author),
                ModName = option.ModName,
                UniqueId = option.UniqueId,
                NexusId = ExtractNexusId(option.Mod),
                OptionCount = 1
            };
        }

        private static bool TermsMatch(string haystack, string[] terms)
        {
            return terms.All(term => haystack.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static bool TermsMatchNexusId(string nexusId, string[] terms)
        {
            if (string.IsNullOrWhiteSpace(nexusId))
                return false;

            string[] searchableTerms = terms
                .Where(term => !term.Equals("Nexus", StringComparison.OrdinalIgnoreCase)
                    && !term.Equals("Nexus:", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            if (searchableTerms.Length == 0)
                return false;

            string canonical = $"Nexus:{nexusId}";
            return searchableTerms.All(term =>
            {
                if (string.IsNullOrWhiteSpace(term))
                    return true;

                if (term.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase))
                    return canonical.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

                return nexusId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
            });
        }

        private static bool TermsMatchSearchableUniqueId(string uniqueId, string author, string[] terms)
        {
            if (string.IsNullOrWhiteSpace(uniqueId))
                return false;

            string searchableUniqueId = GetSearchableUniqueId(uniqueId, author);
            return terms.All(term => MatchesUniqueIdTerm(uniqueId, searchableUniqueId, term));
        }

        private static bool MatchesUniqueIdTerm(string fullUniqueId, string searchableUniqueId, string term)
        {
            if (string.IsNullOrWhiteSpace(term))
                return true;

            if (term.Contains('.', StringComparison.Ordinal))
                return fullUniqueId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

            return searchableUniqueId.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSearchableUniqueId(string uniqueId, string author)
        {
            string searchable = uniqueId.Trim();
            string authorPrefix = string.IsNullOrWhiteSpace(author) ? "" : author.Trim() + ".";
            if (!string.IsNullOrWhiteSpace(authorPrefix) && searchable.StartsWith(authorPrefix, StringComparison.OrdinalIgnoreCase))
                return searchable[authorPrefix.Length..];

            int dotIndex = searchable.IndexOf('.');
            return dotIndex >= 0 && dotIndex + 1 < searchable.Length ? searchable[(dotIndex + 1)..] : searchable;
        }
    }

    private sealed class ModSearchGroup
    {
        public IManifest Manifest { get; init; } = null!;
        public string Author { get; init; } = "";
        public string ModName { get; init; } = "";
        public string UniqueId { get; init; } = "";
        public string NexusId { get; init; } = "";
        public int OptionCount { get; init; }

        public static ModSearchGroup Create(List<GmcmOptionRecord> records)
        {
            GmcmOptionRecord first = records[0];
            return new ModSearchGroup
            {
                Manifest = first.Mod,
                Author = NormalizeAuthor(first.Mod.Author),
                ModName = first.ModName,
                UniqueId = first.UniqueId,
                NexusId = ExtractNexusId(first.Mod),
                OptionCount = records.Count
            };
        }
    }

    private static string ExtractNexusId(IManifest? manifest)
    {
        if (manifest?.UpdateKeys == null)
            return "";

        foreach (string updateKey in manifest.UpdateKeys)
        {
            string id = ExtractNexusId(updateKey);
            if (!string.IsNullOrWhiteSpace(id))
                return id;
        }

        return "";
    }

    private static string ExtractNexusId(string? updateKey)
    {
        if (string.IsNullOrWhiteSpace(updateKey))
            return "";

        string value = updateKey.Trim();
        if (value.StartsWith("Nexus:", StringComparison.OrdinalIgnoreCase))
            value = value["Nexus:".Length..].Trim();
        else if (!value.All(char.IsDigit))
            return "";

        string id = new(value.TakeWhile(char.IsDigit).ToArray());
        return id;
    }

    private static string NormalizeAuthor(string? author)
    {
        return string.IsNullOrWhiteSpace(author) ? T("result.unknown-author") : author.Trim();
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
