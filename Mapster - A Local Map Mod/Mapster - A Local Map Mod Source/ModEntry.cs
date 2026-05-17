using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Mapster
{
    public sealed class ModEntry : Mod
    {
        private const int DropdownRowsVisible = 8;
        private static readonly Rectangle ScrollRunnerSource = new(403, 383, 6, 6);
        private static readonly Rectangle ScrollThumbSource = new(435, 463, 6, 10);

        private ModConfig Config = null!;
        private Texture2D? previewTexture;
        private GameLocation? viewedLocation;
        private bool showingMap;
        private bool dropdownOpen;
        private int dropdownScroll;
        private Rectangle mapRect;
        private Rectangle dropdownRect;
        private Rectangle previewWorldRect;
        private List<GameLocation> locations = new();
        private List<GameLocation> filteredLocations = new();
        private TextBox? searchBox;
        private string lastSearchText = string.Empty;
        private bool isCapturingPreview;
        private bool dropdownDraggingThumb;
        private int dropdownDragYOffset;
        private int frozenToolIndex = -1;
        private int previewRefreshCountdown;
        private bool previewRefreshFailed;
        private bool previousMouseVisible;
        private bool searchFocused;
        private float mapZoom = 1f;
        private Vector2 mapPanPixels = Vector2.Zero;
        private bool mapRightDragging;
        private Point lastRightDragPoint;

        public override void Entry(IModHelper helper)
        {
            Config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Display.MenuChanged += OnMenuChanged;
            helper.Events.Display.WindowResized += OnWindowResized;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            helper.Events.Input.ButtonsChanged += OnButtonsChanged;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.ButtonReleased += OnButtonReleased;
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
                return;

            gmcm.Register(ModManifest, () => Config = new ModConfig(), () => Helper.WriteConfig(Config));
            gmcm.AddBoolOption(ModManifest, () => Config.ModEnabled, value => Config.ModEnabled = value, () => "Mod Enabled");
            gmcm.AddBoolOption(ModManifest, () => Config.AllowTeleport, value => Config.AllowTeleport = value, () => "Allow Teleport From Preview");
            gmcm.AddBoolOption(ModManifest, () => Config.AnimatePreview, value => Config.AnimatePreview = value, () => "Animate Map Preview", () => "Periodically refresh the map preview so animated tiles and sprites can update. Turn this off if another mod conflicts with preview rendering.");
            gmcm.AddKeybindList(ModManifest, () => Config.MapKey, value => Config.MapKey = value, () => "Open Mapster");
            gmcm.AddBoolOption(ModManifest, () => Config.DebugLogging, value => Config.DebugLogging = value, () => "Debug Logging");
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            DisposePreview();
            locations.Clear();
            viewedLocation = null;
            showingMap = false;
            dropdownOpen = false;
            dropdownScroll = 0;
            dropdownDraggingThumb = false;
            frozenToolIndex = -1;
            previewRefreshCountdown = 0;
            previewRefreshFailed = false;
            filteredLocations.Clear();
            searchBox = null;
            lastSearchText = string.Empty;
            searchFocused = false;
            mapZoom = 1f;
            mapPanPixels = Vector2.Zero;
            mapRightDragging = false;
        }

        private void OnWindowResized(object? sender, WindowResizedEventArgs e)
        {
            DisposePreview();
        }

        private void OnMenuChanged(object? sender, MenuChangedEventArgs e)
        {
            if (e.NewMenu is MapsterMenu)
                return;

            if (e.OldMenu is MapsterMenu && e.NewMenu is null)
                return;

            DisposePreview();
            dropdownOpen = false;

            if (e.NewMenu is not null)
                CloseMap(false);
        }

        private void OnButtonsChanged(object? sender, ButtonsChangedEventArgs e)
        {
            if (!Config.ModEnabled || !Context.IsWorldReady || !Config.MapKey.JustPressed())
                return;

            foreach (SButton button in e.Pressed)
                Helper.Input.Suppress(button);

            if (Game1.activeClickableMenu is MapsterMenu)
            {
                CloseMap(true);
                Game1.playSound("bigDeSelect");
                return;
            }

            if (Game1.activeClickableMenu is not null)
                return;

            OpenMap();
            Game1.playSound("bigSelect");
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!showingMap || Game1.activeClickableMenu is not MapsterMenu)
                return;

            if (e.Button == SButton.Escape || e.Button == SButton.ControllerB)
            {
                Helper.Input.Suppress(e.Button);
                CloseMap(true);
                Game1.playSound("bigDeSelect");
                return;
            }

            if (!IsMouseInput(e.Button))
            {
                if (searchFocused && TryGetKeyboardKey(e.Button, out Keys key))
                    AppendSearchCharacter(key);

                Helper.Input.Suppress(e.Button);
                return;
            }

            if (e.Button == SButton.MouseRight && BeginMapDrag(Game1.getMousePosition(true)))
                Helper.Input.Suppress(e.Button);
        }

        private static bool IsMouseInput(SButton button)
        {
            return button.ToString().StartsWith("Mouse", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetKeyboardKey(SButton button, out Keys key)
        {
            return Enum.TryParse(button.ToString(), true, out key);
        }

        private bool BeginMapDrag(Point mouse)
        {
            if (!mapRect.Contains(mouse) || mapZoom <= 1.001f)
                return false;

            mapRightDragging = true;
            lastRightDragPoint = mouse;
            return true;
        }

        private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
        {
            if (e.Button == SButton.MouseRight)
                mapRightDragging = false;
        }

        private void OpenMap()
        {
            showingMap = true;
            dropdownOpen = false;
            dropdownDraggingThumb = false;
            frozenToolIndex = Game1.player?.CurrentToolIndex ?? -1;
            RefreshLocationList();
            viewedLocation = Game1.currentLocation ?? viewedLocation;
            previewRefreshCountdown = 0;
            previewRefreshFailed = false;
            mapZoom = 1f;
            mapPanPixels = Vector2.Zero;
            mapRightDragging = false;
            previousMouseVisible = Game1.game1.IsMouseVisible;
            Game1.game1.IsMouseVisible = false;
            searchBox = CreateSearchBox();
            searchFocused = false;
            Game1.keyboardDispatcher.Subscriber = null;
            lastSearchText = string.Empty;
            ApplyLocationFilter();
            CapturePreview();
            Game1.activeClickableMenu = new MapsterMenu(this);
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (!showingMap)
                return;

            if (frozenToolIndex >= 0 && Game1.player is not null && Game1.player.CurrentToolIndex != frozenToolIndex)
                Game1.player.CurrentToolIndex = frozenToolIndex;

            UpdateSearchFilter();
            UpdateMapRightDrag();

            if (!Config.AnimatePreview || previewRefreshFailed || isCapturingPreview || viewedLocation is null)
                return;

            previewRefreshCountdown--;
            if (previewRefreshCountdown > 0)
                return;

            previewRefreshCountdown = Math.Max(6, Config.AnimatedPreviewRefreshTicks);
            CapturePreview(true);
        }

        private bool HandleMouseLeft(Point mouse)
        {
            if (dropdownOpen)
            {
                Rectangle listRect = GetDropdownListRect();
                if (HasDropdownScrollbar())
                {
                    Rectangle track = GetDropdownScrollbarTrackRect();
                    Rectangle thumb = GetDropdownScrollbarThumbRect();
                    if (thumb.Contains(mouse))
                    {
                        dropdownDraggingThumb = true;
                        dropdownDragYOffset = Math.Clamp(mouse.Y - thumb.Y, 0, Math.Max(0, thumb.Height - 1));
                        SetDropdownScrollFromMouse(mouse.Y, dropdownDragYOffset);
                        return true;
                    }

                    if (track.Contains(mouse))
                    {
                        dropdownDraggingThumb = true;
                        dropdownDragYOffset = GetDropdownScrollbarThumbHeight() / 2;
                        SetDropdownScrollFromMouse(mouse.Y, dropdownDragYOffset);
                        return true;
                    }
                }

                if (listRect.Contains(mouse))
                {
                    int rowHeight = GetDropdownRowHeight();
                    int rowWidth = GetDropdownRowTextWidth(listRect);
                    Rectangle rowsRect = new(listRect.X + 8, listRect.Y + 8, rowWidth, Math.Max(0, listRect.Height - 16));
                    if (!rowsRect.Contains(mouse))
                        return true;

                    int index = dropdownScroll + Math.Max(0, (mouse.Y - rowsRect.Y) / rowHeight);
                    if (index >= 0 && index < filteredLocations.Count)
                    {
                        viewedLocation = filteredLocations[index];
                        dropdownOpen = false;
                        dropdownDraggingThumb = false;
                        dropdownScroll = Math.Clamp(dropdownScroll, 0, GetMaxDropdownScroll());
                        mapZoom = 1f;
                        mapPanPixels = Vector2.Zero;
                        DisposePreview();
                        ClearSearchInput(false);
                        CapturePreview();
                        Game1.playSound("shwip");
                    }
                    return true;
                }

                dropdownOpen = false;
                dropdownDraggingThumb = false;
                ClearSearchInput(false);
                return true;
            }

            if (dropdownRect.Contains(mouse))
            {
                RefreshLocationList();
                searchFocused = true;
                Game1.keyboardDispatcher.Subscriber = null;
                dropdownOpen = filteredLocations.Count > 0;
                dropdownDraggingThumb = false;
                dropdownScroll = string.IsNullOrWhiteSpace(searchBox?.Text) ? Math.Clamp(GetViewedLocationIndex(), 0, GetMaxDropdownScroll()) : 0;
                Game1.playSound("shwip");
                return true;
            }

            if (searchFocused)
            {
                ClearSearchInput(false);
                return true;
            }

            if (Config.AllowTeleport && mapRect.Contains(mouse) && viewedLocation is not null)
            {
                TeleportToPreviewPoint(mouse);
                return true;
            }

            return false;
        }

        private void RefreshLocationList()
        {
            GameLocation? current = Game1.currentLocation;
            locations = Game1.locations
                .Where(location => location?.map is not null && !string.IsNullOrWhiteSpace(GetLocationName(location)))
                .GroupBy(GetLocationName, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(location => current is not null && ReferenceEquals(location, current) ? 0 : 1)
                .ThenBy(GetLocationName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (viewedLocation is null || !locations.Any(location => ReferenceEquals(location, viewedLocation)))
                viewedLocation = current ?? locations.FirstOrDefault();

            ApplyLocationFilter();
        }

        private void CapturePreview(bool refreshOnly = false)
        {
            GameLocation? location = viewedLocation ?? Game1.currentLocation;
            if (location?.map is null || location.map.Layers.Count == 0)
                return;

            int mapPixelWidth = Math.Max(Game1.tileSize, location.map.Layers[0].LayerWidth * Game1.tileSize);
            int mapPixelHeight = Math.Max(Game1.tileSize, location.map.Layers[0].LayerHeight * Game1.tileSize);
            int renderWidth = mapPixelWidth;
            int renderHeight = mapPixelHeight;
            int startX = 0;
            int startY = 0;

            if (previewTexture is RenderTarget2D existing && (existing.Width != renderWidth || existing.Height != renderHeight))
                DisposePreview();

            RenderTarget2D target;
            if (previewTexture is RenderTarget2D reusable)
            {
                target = reusable;
            }
            else
            {
                DisposePreview();
                target = new RenderTarget2D(Game1.graphics.GraphicsDevice, renderWidth, renderHeight, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
                previewTexture = target;
            }

            xTile.Dimensions.Rectangle oldViewport = Game1.viewport;
            GameLocation? oldLocation = Game1.currentLocation;
            bool oldDisplayHud = Game1.displayHUD;
            bool oldTakingMapScreenshot = Game1.game1.takingMapScreenshot;
            float oldZoom = Game1.options.baseZoomLevel;

            try
            {
                isCapturingPreview = true;
                Game1.displayHUD = false;
                Game1.game1.takingMapScreenshot = true;
                Game1.options.baseZoomLevel = 1f;
                Game1.currentLocation = location;
                previewWorldRect = new Rectangle(startX, startY, renderWidth, renderHeight);
                Game1.viewport = new xTile.Dimensions.Rectangle(startX, startY, renderWidth, renderHeight);

                MethodInfo? drawMethod = typeof(Game1).GetMethod("_draw", BindingFlags.Instance | BindingFlags.NonPublic);
                if (drawMethod is null)
                    throw new MissingMethodException(nameof(Game1), "_draw");

                drawMethod.Invoke(Game1.game1, new object[] { new GameTime(), target });
            }
            catch (Exception ex)
            {
                if (refreshOnly)
                    previewRefreshFailed = true;

                LogDebug($"Couldn't render location preview for {GetLocationName(location)}: {ex}");
            }
            finally
            {
                Game1.graphics.GraphicsDevice.SetRenderTarget(null);
                Game1.displayHUD = oldDisplayHud;
                Game1.game1.takingMapScreenshot = oldTakingMapScreenshot;
                Game1.options.baseZoomLevel = oldZoom;
                Game1.currentLocation = oldLocation;
                Game1.viewport = oldViewport;
                isCapturingPreview = false;
            }
        }

        private void DrawMap(SpriteBatch b)
        {
            b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black);

            int outerPad = 32;
            int titleY = 20;
            int dropdownHeight = OptionsDropDown.dropDownButtonSource.Height * 4;
            int controlWidth = Math.Min(Game1.uiViewport.Width - outerPad * 2, 1180);
            int controlX = (Game1.uiViewport.Width - controlWidth) / 2;
            int dropdownY = titleY + 56;
            dropdownRect = new Rectangle(controlX, dropdownY, controlWidth, dropdownHeight);

            if (previewTexture is not null && viewedLocation is not null)
            {
                int mapTop = dropdownRect.Bottom + 36;
                int mapBottomReserve = 28;
                int maxMapWidth = Math.Max(1, Game1.uiViewport.Width - outerPad * 2);
                int maxMapHeight = Math.Max(180, Game1.uiViewport.Height - mapTop - mapBottomReserve);
                Rectangle availableMapRect = new(outerPad, mapTop, maxMapWidth, maxMapHeight);
                Rectangle fittedMapRect = GetFittedMapRect(availableMapRect);
                mapRect = GetZoomedMapRect(availableMapRect, fittedMapRect);

                ClampMapPan();
                b.Draw(previewTexture, mapRect, GetMapSourceRect(), Color.White);

                if (Config.AllowTeleport)
                    DrawSmallText(b, "Click the preview to warp to this relative map position.", new Vector2(mapRect.X, Math.Min(Game1.uiViewport.Height - 32, mapRect.Bottom + 8)));
            }

            DrawHeader(b, 0, titleY - 20, Game1.uiViewport.Width);
            DrawDropdown(b, controlX, dropdownY, controlWidth);

            if (dropdownOpen)
                DrawDropdownList(b);
        }

        private Rectangle GetFittedMapRect(Rectangle bounds)
        {
            if (previewTexture is null)
                return bounds;

            float fitScale = Math.Min(bounds.Width / (float)previewTexture.Width, bounds.Height / (float)previewTexture.Height);
            fitScale = Math.Max(0.1f, fitScale);
            int drawWidth = Math.Max(1, (int)Math.Round(previewTexture.Width * fitScale));
            int drawHeight = Math.Max(1, (int)Math.Round(previewTexture.Height * fitScale));
            return new Rectangle(
                bounds.X + (bounds.Width - drawWidth) / 2,
                bounds.Y + (bounds.Height - drawHeight) / 2,
                drawWidth,
                drawHeight);
        }

        private Rectangle GetZoomedMapRect(Rectangle availableMapRect, Rectangle fittedMapRect)
        {
            if (mapZoom <= 1.001f)
                return fittedMapRect;

            float progress = Math.Clamp((mapZoom - 1f) / 1.5f, 0f, 1f);
            progress = progress * progress * (3f - 2f * progress);
            int width = Math.Max(1, (int)Math.Round(MathHelper.Lerp(fittedMapRect.Width, availableMapRect.Width, progress)));
            int height = Math.Max(1, (int)Math.Round(MathHelper.Lerp(fittedMapRect.Height, availableMapRect.Height, progress)));
            return new Rectangle(
                availableMapRect.X + (availableMapRect.Width - width) / 2,
                availableMapRect.Y + (availableMapRect.Height - height) / 2,
                width,
                height);
        }

        private void DrawHeader(SpriteBatch b, int panelX, int panelY, int panelWidth)
        {
            string title = $"Mapster - {GetLocationName(viewedLocation)}";
            SpriteText.drawStringHorizontallyCenteredAt(b, title, panelX + panelWidth / 2, panelY + 20, 999999, -1, 999999, 1f, 0.8f);
        }

        private TextBox CreateSearchBox()
        {
            return new TextBox(Game1.content.Load<Texture2D>("LooseSprites/textBox"), null, Game1.smallFont, Game1.textColor)
            {
                Text = string.Empty
            };
        }

        private void UpdateSearchFilter()
        {
            if (searchBox is null)
                return;

            if (string.Equals(searchBox.Text, lastSearchText, StringComparison.Ordinal))
                return;

            lastSearchText = searchBox.Text;
            ApplyLocationFilter();
            dropdownScroll = Math.Clamp(dropdownScroll, 0, GetMaxDropdownScroll());
            if (searchFocused)
                dropdownOpen = filteredLocations.Count > 0;
        }

        private void ApplyLocationFilter()
        {
            string query = searchBox?.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(query))
            {
                filteredLocations = locations.ToList();
                return;
            }

            string[] terms = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            filteredLocations = locations
                .Where(location => terms.All(term => GetLocationName(location).IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0))
                .ToList();
        }

        private void ClearSearchInput(bool keepDropdownOpen)
        {
            if (searchBox is not null)
                searchBox.Text = string.Empty;

            lastSearchText = string.Empty;
            searchFocused = false;
            dropdownDraggingThumb = false;
            dropdownOpen = keepDropdownOpen && filteredLocations.Count > 0;
            ApplyLocationFilter();
            dropdownScroll = Math.Clamp(GetViewedLocationIndex(), 0, GetMaxDropdownScroll());
            Game1.keyboardDispatcher.Subscriber = null;
        }

        private void HandleDropdownHeld(int x, int y)
        {
            if (!dropdownDraggingThumb)
                return;

            SetDropdownScrollFromMouse(y, dropdownDragYOffset);
        }

        private void ZoomMap(int direction, Point mouse)
        {
            if (previewTexture is null || !mapRect.Contains(mouse))
                return;

            Rectangle before = GetMapSourceRect();
            float relativeX = Math.Clamp((mouse.X - mapRect.X) / (float)Math.Max(1, mapRect.Width), 0f, 1f);
            float relativeY = Math.Clamp((mouse.Y - mapRect.Y) / (float)Math.Max(1, mapRect.Height), 0f, 1f);
            float anchorX = before.X + before.Width * relativeX;
            float anchorY = before.Y + before.Height * relativeY;

            float factor = direction > 0 ? 1.08f : 1f / 1.08f;
            mapZoom = Math.Clamp(mapZoom * factor, 1f, 10f);

            Rectangle after = GetMapSourceRect(false);
            float centerX = anchorX - after.Width * relativeX;
            float centerY = anchorY - after.Height * relativeY;
            mapPanPixels = new Vector2(
                centerX + after.Width / 2f - previewTexture.Width / 2f,
                centerY + after.Height / 2f - previewTexture.Height / 2f);
            ClampMapPan();
        }

        private void UpdateMapRightDrag()
        {
            if (previewTexture is null || mapZoom <= 1.001f)
            {
                mapRightDragging = false;
                return;
            }

            bool rightDown = Mouse.GetState().RightButton == ButtonState.Pressed;
            Point mouse = Game1.getMousePosition(true);

            if (!rightDown)
            {
                mapRightDragging = false;
                return;
            }

            if (!mapRightDragging)
            {
                if (!mapRect.Contains(mouse))
                    return;

                mapRightDragging = true;
                lastRightDragPoint = mouse;
                return;
            }

            int deltaX = mouse.X - lastRightDragPoint.X;
            int deltaY = mouse.Y - lastRightDragPoint.Y;
            lastRightDragPoint = mouse;

            if (deltaX == 0 && deltaY == 0)
                return;

            Rectangle source = GetMapSourceRect();
            mapPanPixels.X -= deltaX * (source.Width / (float)Math.Max(1, mapRect.Width));
            mapPanPixels.Y -= deltaY * (source.Height / (float)Math.Max(1, mapRect.Height));
            ClampMapPan();
        }

        private Rectangle GetMapSourceRect(bool clamp = true)
        {
            if (previewTexture is null)
                return Rectangle.Empty;

            if (mapZoom <= 1.001f || mapRect.Width <= 0 || mapRect.Height <= 0)
                return new Rectangle(0, 0, previewTexture.Width, previewTexture.Height);

            float zoom = Math.Clamp(mapZoom, 1f, 10f);
            float destinationAspect = mapRect.Width / (float)Math.Max(1, mapRect.Height);
            float sourceWidth = previewTexture.Width / zoom;
            float sourceHeight = sourceWidth / Math.Max(0.001f, destinationAspect);

            if (sourceHeight > previewTexture.Height / zoom)
            {
                sourceHeight = previewTexture.Height / zoom;
                sourceWidth = sourceHeight * destinationAspect;
            }

            sourceWidth = Math.Clamp(sourceWidth, 1f, previewTexture.Width);
            sourceHeight = Math.Clamp(sourceHeight, 1f, previewTexture.Height);

            float centerX = previewTexture.Width / 2f + mapPanPixels.X;
            float centerY = previewTexture.Height / 2f + mapPanPixels.Y;
            int width = Math.Max(1, (int)Math.Round(sourceWidth));
            int height = Math.Max(1, (int)Math.Round(sourceHeight));
            int x = (int)Math.Round(centerX - width / 2f);
            int y = (int)Math.Round(centerY - height / 2f);

            if (clamp)
            {
                x = Math.Clamp(x, 0, Math.Max(0, previewTexture.Width - width));
                y = Math.Clamp(y, 0, Math.Max(0, previewTexture.Height - height));
            }

            return new Rectangle(x, y, width, height);
        }

        private void ClampMapPan()
        {
            if (previewTexture is null)
                return;

            mapZoom = Math.Clamp(mapZoom, 1f, 10f);
            if (mapZoom <= 1.001f)
            {
                mapPanPixels = Vector2.Zero;
                return;
            }

            Rectangle source = GetMapSourceRect(false);
            float maxX = Math.Max(0f, (previewTexture.Width - source.Width) / 2f);
            float maxY = Math.Max(0f, (previewTexture.Height - source.Height) / 2f);
            mapPanPixels = new Vector2(
                Math.Clamp(mapPanPixels.X, -maxX, maxX),
                Math.Clamp(mapPanPixels.Y, -maxY, maxY));
        }

        private void AppendSearchCharacter(Keys key)
        {
            if (searchBox is null || !searchFocused)
                return;

            if (key == Keys.Back)
            {
                if (searchBox.Text.Length > 0)
                    searchBox.Text = searchBox.Text.Substring(0, searchBox.Text.Length - 1);
                return;
            }

            if (key == Keys.Delete)
            {
                searchBox.Text = string.Empty;
                return;
            }

            char? character = GetSearchCharacter(key);
            if (character.HasValue)
                searchBox.Text += character.Value;
        }

        private static char? GetSearchCharacter(Keys key)
        {
            KeyboardState state = Keyboard.GetState();
            bool shift = state.IsKeyDown(Keys.LeftShift) || state.IsKeyDown(Keys.RightShift);

            if (key >= Keys.A && key <= Keys.Z)
            {
                char c = (char)('a' + ((int)key - (int)Keys.A));
                return shift ? char.ToUpperInvariant(c) : c;
            }

            if (key >= Keys.D0 && key <= Keys.D9)
                return (char)('0' + ((int)key - (int)Keys.D0));

            if (key >= Keys.NumPad0 && key <= Keys.NumPad9)
                return (char)('0' + ((int)key - (int)Keys.NumPad0));

            return key switch
            {
                Keys.Space => ' ',
                Keys.OemMinus => shift ? '_' : '-',
                Keys.OemPeriod => '.',
                Keys.OemComma => ',',
                Keys.OemPlus => '+',
                Keys.OemQuestion => '/',
                Keys.OemSemicolon => ';',
                Keys.OemQuotes => '\'',
                Keys.OemOpenBrackets => '[',
                Keys.OemCloseBrackets => ']',
                _ => null
            };
        }

        private void DrawDropdown(SpriteBatch b, int x, int y, int width)
        {
            int height = OptionsDropDown.dropDownButtonSource.Height * 4;
            int buttonWidth = OptionsDropDown.dropDownButtonSource.Width * 4;
            int fieldWidth = Math.Max(1, width - buttonWidth);
            dropdownRect = new Rectangle(x, y, width, height);

            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, OptionsDropDown.dropDownBGSource, x, y, fieldWidth, height, Color.White, 4f, false);
            b.Draw(Game1.mouseCursors, new Vector2(x + fieldWidth, y), OptionsDropDown.dropDownButtonSource, Color.White, 0f, Vector2.Zero, 4f, SpriteEffects.None, 0f);

            string query = searchBox?.Text ?? string.Empty;
            string text = searchFocused || !string.IsNullOrWhiteSpace(query) ? query : GetLocationName(viewedLocation);
            if (string.IsNullOrEmpty(text) && searchFocused)
                text = "Search locations...";

            Color textColor = string.Equals(text, "Search locations...", StringComparison.Ordinal) ? Game1.textColor * 0.65f : Game1.textColor;
            Vector2 textPosition = new(x + 12, y + (height - Game1.smallFont.LineSpacing) / 2f);
            int textMaxWidth = Math.Max(1, fieldWidth - 24);
            DrawClippedText(b, text, Game1.smallFont, textPosition, textColor, textMaxWidth);

            if (searchFocused && DateTime.UtcNow.Millisecond < 500)
                DrawSearchCaret(b, text, textPosition, textMaxWidth, y + 10, height - 20);
        }

        private static void DrawSearchCaret(SpriteBatch b, string text, Vector2 textPosition, int maxWidth, int y, int height)
        {
            if (string.Equals(text, "Search locations...", StringComparison.Ordinal))
                text = string.Empty;

            float width = Game1.smallFont.MeasureString(text).X;
            float x = textPosition.X + Math.Min(width + 2f, Math.Max(0, maxWidth - 4));
            b.Draw(Game1.staminaRect, new Rectangle((int)Math.Round(x), y, 2, Math.Max(12, height)), Game1.textColor);
        }

        private void DrawDropdownList(SpriteBatch b)
        {
            Rectangle listRect = GetDropdownListRect();
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, OptionsDropDown.dropDownBGSource, listRect.X, listRect.Y, listRect.Width, listRect.Height, Color.White, 4f, false);

            int rowHeight = GetDropdownRowHeight();
            int rows = Math.Min(DropdownRowsVisible, filteredLocations.Count);
            int rowWidth = GetDropdownRowTextWidth(listRect);
            for (int i = 0; i < rows; i++)
            {
                int index = dropdownScroll + i;
                if (index >= filteredLocations.Count)
                    break;

                Rectangle row = new(listRect.X + 8, listRect.Y + 8 + i * rowHeight, rowWidth, rowHeight);
                if (row.Contains(Game1.getMousePosition(true)))
                    b.Draw(Game1.fadeToBlackRect, row, Color.Black * 0.06f);

                DrawClippedText(b, GetLocationName(filteredLocations[index]), Game1.smallFont, new Vector2(row.X + 4, row.Y + (row.Height - Game1.smallFont.LineSpacing) / 2f), Game1.textColor, row.Width - 8);
            }

            DrawDropdownScrollbar(b);
        }

        private Rectangle GetDropdownListRect()
        {
            int rowHeight = GetDropdownRowHeight();
            int rows = Math.Min(DropdownRowsVisible, filteredLocations.Count);
            return new Rectangle(dropdownRect.X, dropdownRect.Bottom - 2, dropdownRect.Width, Math.Max(rowHeight + 16, rows * rowHeight + 16));
        }

        private int GetDropdownRowHeight()
        {
            return Math.Max(OptionsDropDown.dropDownButtonSource.Height * 4, Game1.smallFont.LineSpacing + 12);
        }

        private int GetMaxDropdownScroll()
        {
            return Math.Max(0, filteredLocations.Count - DropdownRowsVisible);
        }

        private bool HasDropdownScrollbar()
        {
            return GetMaxDropdownScroll() > 0;
        }

        private int GetDropdownRowTextWidth(Rectangle listRect)
        {
            return listRect.Width - (HasDropdownScrollbar() ? 44 : 16);
        }

        private Rectangle GetDropdownScrollbarTrackRect()
        {
            Rectangle listRect = GetDropdownListRect();
            return new Rectangle(listRect.Right - 32, listRect.Y + 12, 24, Math.Max(24, listRect.Height - 24));
        }

        private int GetDropdownScrollbarThumbHeight()
        {
            Rectangle track = GetDropdownScrollbarTrackRect();
            int visibleRows = Math.Min(DropdownRowsVisible, Math.Max(1, filteredLocations.Count));
            float visibleRatio = Math.Min(1f, visibleRows / (float)Math.Max(1, filteredLocations.Count));
            return Math.Max(44, Math.Min(track.Height, (int)Math.Round(track.Height * visibleRatio)));
        }

        private Rectangle GetDropdownScrollbarThumbRect()
        {
            Rectangle track = GetDropdownScrollbarTrackRect();
            int maxScroll = GetMaxDropdownScroll();
            int thumbHeight = GetDropdownScrollbarThumbHeight();
            int travel = Math.Max(0, track.Height - thumbHeight);
            int y = maxScroll <= 0 ? track.Y : track.Y + (int)Math.Round(travel * (dropdownScroll / (float)maxScroll));
            return new Rectangle(track.X, y, track.Width, thumbHeight);
        }

        private void DrawDropdownScrollbar(SpriteBatch b)
        {
            if (!HasDropdownScrollbar())
                return;

            Rectangle track = GetDropdownScrollbarTrackRect();
            Rectangle thumb = GetDropdownScrollbarThumbRect();
            IClickableMenu.drawTextureBox(b, Game1.mouseCursors, ScrollRunnerSource, track.X, track.Y, track.Width, track.Height, Color.White, 4f, false);
            b.Draw(Game1.mouseCursors, thumb, ScrollThumbSource, Color.White);
        }

        private void SetDropdownScrollFromMouse(int mouseY, int yOffset)
        {
            int maxScroll = GetMaxDropdownScroll();
            if (maxScroll <= 0)
            {
                dropdownScroll = 0;
                return;
            }

            Rectangle track = GetDropdownScrollbarTrackRect();
            int thumbHeight = GetDropdownScrollbarThumbHeight();
            int travel = Math.Max(1, track.Height - thumbHeight);
            float percent = Math.Clamp((mouseY - yOffset - track.Y) / (float)travel, 0f, 1f);
            dropdownScroll = Math.Clamp((int)Math.Round(maxScroll * percent), 0, maxScroll);
        }

        private int GetViewedLocationIndex()
        {
            if (viewedLocation is null)
                return 0;

            int index = filteredLocations.FindIndex(location => ReferenceEquals(location, viewedLocation));
            return Math.Max(0, index);
        }

        private void TeleportToPreviewPoint(Point mouse)
        {
            if (viewedLocation is null || mapRect.Width <= 0 || mapRect.Height <= 0)
                return;

            float xRatio = Math.Clamp((mouse.X - mapRect.X) / (float)mapRect.Width, 0f, 1f);
            float yRatio = Math.Clamp((mouse.Y - mapRect.Y) / (float)mapRect.Height, 0f, 1f);
            Rectangle source = GetMapSourceRect();
            int worldX = previewWorldRect.X + source.X + (int)Math.Round(source.Width * xRatio);
            int worldY = previewWorldRect.Y + source.Y + (int)Math.Round(source.Height * yRatio);
            int tileX = Math.Clamp(worldX / Game1.tileSize, 0, viewedLocation.map.Layers[0].LayerWidth - 1);
            int tileY = Math.Clamp(worldY / Game1.tileSize, 0, viewedLocation.map.Layers[0].LayerHeight - 1);

            Game1.warpFarmer(GetLocationName(viewedLocation), tileX, tileY, false);
            CloseMap(false);
        }

        private static void DrawSmallText(SpriteBatch b, string text, Vector2 position)
        {
            Utility.drawTextWithShadow(b, text, Game1.smallFont, position, Game1.textColor);
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
                text = text.Substring(0, text.Length - 1);

            Utility.drawTextWithShadow(b, text + ellipsis, font, position, color);
        }

        private static string GetLocationName(GameLocation? location)
        {
            if (location is null)
                return "Unknown";

            string name = location.NameOrUniqueName;
            return string.IsNullOrWhiteSpace(name) ? location.Name : name;
        }

        private void CloseMap(bool disposePreview = true)
        {
            showingMap = false;
            dropdownOpen = false;
            dropdownDraggingThumb = false;
            frozenToolIndex = -1;
            previewRefreshCountdown = 0;
            previewRefreshFailed = false;
            searchFocused = false;
            mapRightDragging = false;

            if (Game1.keyboardDispatcher.Subscriber == searchBox)
                Game1.keyboardDispatcher.Subscriber = null;

            Game1.game1.IsMouseVisible = previousMouseVisible;

            if (Game1.activeClickableMenu is MapsterMenu)
                Game1.activeClickableMenu = null;

            if (disposePreview)
                DisposePreview();
        }

        private void DisposePreview()
        {
            previewTexture?.Dispose();
            previewTexture = null;
        }

        private sealed class MapsterMenu : IClickableMenu
        {
            private readonly ModEntry owner;

            public MapsterMenu(ModEntry owner)
                : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, false)
            {
                this.owner = owner;
            }

            public override void draw(SpriteBatch b)
            {
                owner.DrawMap(b);
                drawMouse(b);
            }

            public override void receiveLeftClick(int x, int y, bool playSound = true)
            {
                owner.HandleMouseLeft(new Point(x, y));
            }

            public override void receiveRightClick(int x, int y, bool playSound = true)
            {
                owner.BeginMapDrag(new Point(x, y));
            }

            public override void leftClickHeld(int x, int y)
            {
                owner.HandleDropdownHeld(x, y);
            }

            public override void releaseLeftClick(int x, int y)
            {
                owner.dropdownDraggingThumb = false;
            }

            public override void receiveScrollWheelAction(int direction)
            {
                Point mouse = Game1.getMousePosition(true);
                if (owner.dropdownOpen && (owner.dropdownRect.Contains(mouse) || owner.GetDropdownListRect().Contains(mouse)))
                {
                    owner.dropdownScroll = Math.Clamp(owner.dropdownScroll + (direction > 0 ? -1 : 1), 0, owner.GetMaxDropdownScroll());
                    return;
                }

                if (owner.mapRect.Contains(mouse))
                    owner.ZoomMap(direction, mouse);
            }

            public override void receiveKeyPress(Keys key)
            {
                if (key == Keys.Escape)
                {
                    owner.CloseMap(true);
                    Game1.playSound("bigDeSelect");
                }
            }

            public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
            {
                base.gameWindowSizeChanged(oldBounds, newBounds);
                width = Game1.uiViewport.Width;
                height = Game1.uiViewport.Height;
                owner.mapZoom = 1f;
                owner.mapPanPixels = Vector2.Zero;
                owner.DisposePreview();
            }

            protected override void cleanupBeforeExit()
            {
                if (Game1.keyboardDispatcher.Subscriber == owner.searchBox)
                    Game1.keyboardDispatcher.Subscriber = null;

                owner.CloseMap(true);
                base.cleanupBeforeExit();
            }
        }

        private void LogDebug(string message)
        {
            if (Config.DebugLogging)
                Monitor.Log(message, LogLevel.Debug);
        }
    }
}
