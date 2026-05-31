using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.GameData.Locations;
using StardewModdingAPI;
using StardewModdingAPI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Text.Json;
using System.Reflection;
using xTile.Tiles;

namespace WarpMasterFramework
{
    public class WarpEditorMenu : IClickableMenu
    {
        private bool titleInPosition = true;
        private readonly bool previousMouseVisible;
        private List<WarpPointData> currentMapWarps;
        private WarpPointData selectedWarp;
        private bool isDragging;
        private Point selectedWarpDragStartSource;
        private Point dragOffset;
        private IMonitor monitor;
        private readonly Color uiTextColor;

        private readonly bool debugEnabled;

        // Full-scene screenshot capture for the local map viewer.
        // This mirrors the useful parts of Location Map's screenshot flow without SkiaSharp:
        // Game1._draw + takingMapScreenshot + temporary zoom/lightmap control.
        private RenderTarget2D fullSceneCaptureTarget;
        private int fullSceneCaptureWidth = -1;
        private int fullSceneCaptureHeight = -1;
        private static readonly MethodInfo GameDrawMethod = typeof(Game1).GetMethod("_draw", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo AllocateLightmapMethod = typeof(Game1).GetMethod("allocateLightmap", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly FieldInfo LightmapField = typeof(Game1).GetField("_lightmap", BindingFlags.Static | BindingFlags.NonPublic);

        private enum TooltipMode { Hide, Hover, Show }
        private readonly TooltipMode sourceTooltipMode = TooltipMode.Hover;
        private readonly TooltipMode targetTooltipMode = TooltipMode.Hover;

	        private static TooltipMode ParseTooltipMode(string value, TooltipMode fallback)
	        {
	            if (string.IsNullOrWhiteSpace(value))
	                return fallback;

	            switch (value.Trim().ToLowerInvariant())
	            {
	                case "hide":
	                case "off":
	                case "false":
	                    return TooltipMode.Hide;
	                case "show":
	                case "always":
	                case "on":
	                case "true":
	                    return TooltipMode.Show;
	                case "hover":
	                case "default":
	                    return TooltipMode.Hover;
	                default:
	                    return fallback;
	            }
	        }

	        private static LocalMapRenderMode ParseLocalMapRenderMode(string value)
	        {
	            if (string.IsNullOrWhiteSpace(value))
	                return LocalMapRenderMode.FullScene;

	            switch (value.Trim().ToLowerInvariant())
	            {
	                case "terrain only":
	                case "terrain":
	                case "base":
	                case "base map":
	                    return LocalMapRenderMode.TerrainOnly;
	                case "full scene":
	                case "full":
	                case "scene":
	                default:
	                    return LocalMapRenderMode.FullScene;
	            }
	        }


	        private void SafeLog(string message, LogLevel level = LogLevel.Trace)
	        {
	            // When debug logging is disabled, only surface fatal-ish logs.
	            if (!this.debugEnabled && level < LogLevel.Error)
	                return;

	            try
	            {
	                this.monitor?.Log(message, level);
	            }
	            catch
	            {
	                // Never let logging crash the editor.
	            }
	        }


        // Top-left UI (instructions + map selector dropdown)
        private Rectangle instructionsPanelRect;
        private Rectangle mapSelectorRect;
        private Rectangle mapDropdownListRect;
        private bool mapDropdownOpen;
        private bool mapDropdownScrollDragging;
        private int mapDropdownScrollDragOffsetY;

        private int mapDropdownScrollIndex;
        private int mapDropdownSelectedIndex;
        private const int MapDropdownMaxVisibleItems = 12;

// Camera/viewport for fullscreen map view
        private Vector2 cameraPosition;
        private float zoomLevel;
        private const float MIN_ZOOM = 0.25f;
        private const float MAX_ZOOM = 2.0f;
        private const float ZOOM_STEP = 0.1f;
        private Point lastMousePosition;
        private bool isDraggingCamera;
        
        private string mapName;
        private bool showWarpLabels;
        
        // Location navigation
        private Stack<string> locationHistory = new Stack<string>();
        // targetLocationName, optional tile to center on in that location
        private Action<string, Point?, Point?> onNavigateToLocation;
        private readonly bool mapViewerMode;
        private readonly bool showMasterListDropdown;
        private readonly bool closeAtMinimumZoom;
        private readonly Action onCloseAtMinimumZoom;

        private enum LocalMapRenderMode
        {
            FullScene,
            TerrainOnly
        }

        private readonly LocalMapRenderMode localMapRenderMode = LocalMapRenderMode.FullScene;
        public bool IsMapViewerMode => mapViewerMode;
        private Action onNavigateBack;
        private Action onNavigateToRoot;
        private Action onImmediateSaveApply;
        private readonly Action onExportFrameworkData;
        private Rectangle exportButtonRect;
        private Action<string, Point, string, Point> onUpdateWarpDestination;
        
                private Action<string, Point, Point, string, Point> onUpdateWarpSourceAndDestination;
        private Func<string, Point, bool> getExternalWarpEditedOrAdded;
        private Func<string, Point, WarpPointData> getExternalWarpOriginalTarget;
private bool rightClickHandled = false;
        private bool shiftLeftClickHandled = false;
        private bool rKeyHandled = false;
        // Destination editing (hold E + Left Click on a warp)
private WarpDestinationEditPopup destinationEditPopup;
private WarpPointData pendingNewWarp;
private List<LocationOption> cachedLocationOptions;
	private HashSet<string> cachedVanillaLocationNames;
// Inbound destination markers ("X") for tiles that are warp destinations into this map.
		private sealed class InboundDestinationMarker
		{
			public string SourceMap { get; set; }
			public Point SourceTile { get; set; }
			public Point DestinationTile { get; set; }
			public Point OriginalDestinationTile { get; set; }
			public string WarpType { get; set; } = "Warp";
			public bool IsEditedOrAdded { get; set; }
		}

		private readonly List<InboundDestinationMarker> inboundDestinationMarkers = new();
		private readonly VisualWarpEditor inboundDetector;
		private InboundDestinationMarker selectedInboundMarker;
		private bool isDraggingInboundMarker;
        private bool inboundMarkersDirty = true;
        private readonly Dictionary<string, List<InboundDestinationMarker>> inboundCacheByDestMap = new(StringComparer.OrdinalIgnoreCase);


	private enum LocationKind
	{
		Outdoor = 0,
		Indoor = 1,
		Unknown = 2
	}

	private enum LocationSourceKind
	{
		Vanilla = 0,
		Mod = 1,
		Unknown = 2
	}

	private sealed class LocationOption
	{
		public string Name { get; }
		public LocationKind Kind { get; }
		public LocationSourceKind SourceKind { get; }

		public string KindLabel =>
			Kind == LocationKind.Outdoor ? "Outdoor" :
			Kind == LocationKind.Indoor ? "Indoor" :
			"Unknown";

		public string SourceLabel =>
			SourceKind == LocationSourceKind.Vanilla ? "Vanilla" :
			SourceKind == LocationSourceKind.Mod ? "Mod" :
			"Unknown";

		public string DisplayName
		{
			get
			{
				return Name;
			}
		}

		public LocationOption(string name, LocationKind kind, LocationSourceKind sourceKind)
		{
			Name = name ?? "";
			Kind = kind;
			SourceKind = sourceKind;
		}
	}

		// The location we're currently viewing (may differ from Game1.currentLocation)
        private GameLocation viewedLocation;
        
        
        // Commit/save callback (invoked when menu closes via any route).
        private bool _hasCommittedPendingChanges;

        /// <summary>Commit the current editor state into save data (in-memory) and apply for this session.</summary>
        public void CommitPendingChanges()
        {
            if (this.mapViewerMode)
                return;

            if (this._hasCommittedPendingChanges)
                return;

            this._hasCommittedPendingChanges = true;

            try
            {
                this.onImmediateSaveApply?.Invoke();
            }
            catch
            {
                // Never let close-handling throw; worst case is changes remain in-memory until next commit.
            }
        }

public WarpEditorMenu(List<WarpPointData> warps, IMonitor monitor, bool showLabels = true,
            string viewedMapName = null,
            Action<string, Point?, Point?> navigateToLocation = null, Action navigateBack = null, Action navigateToRoot = null, Stack<string> history = null, Action onImmediateSaveApply = null,
            Action onExportFrameworkData = null,
            Action<string, Point, string, Point> onUpdateWarpDestination = null,
            Action<string, Point, Point, string, Point> onUpdateWarpSourceAndDestination = null,
            Point? initialCenterTile = null,
            Point? initialAnchorScreen = null,
            bool suppressShiftLeftUntilRelease = false,
            bool suppressShiftRightUntilRelease = false,
            bool suppressRUntilRelease = false,
            Func<string, Point, bool> getExternalWarpEditedOrAdded = null,
            Func<string, Point, WarpPointData> getExternalWarpOriginalTarget = null,
            Color? uiTextColor = null,
            bool enableDebugLogging = false, string sourceTooltipMode = null, string targetTooltipMode = null,
            bool mapViewerMode = false, bool showMasterListDropdown = true,
            string localMapRenderMode = "Full Scene",
            bool closeAtMinimumZoom = false, Action onCloseAtMinimumZoom = null)
            : base(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height, showUpperRightCloseButton: false)
        {
            previousMouseVisible = Game1.game1.IsMouseVisible;
            Game1.game1.IsMouseVisible = false;

            this.currentMapWarps = warps;
            this.monitor = monitor;
            
			this.inboundDetector = new VisualWarpEditor(monitor);

            this.inboundDetector.EnableDebugLogging = enableDebugLogging;
            this.debugEnabled = enableDebugLogging;

			// Default to Hover to avoid visual clutter unless user explicitly chooses Show.
			this.sourceTooltipMode = ParseTooltipMode(sourceTooltipMode, TooltipMode.Hover);
			this.targetTooltipMode = ParseTooltipMode(targetTooltipMode, TooltipMode.Hover);
            this.mapViewerMode = mapViewerMode;
            this.showMasterListDropdown = showMasterListDropdown;
            this.localMapRenderMode = ParseLocalMapRenderMode(localMapRenderMode);
            this.closeAtMinimumZoom = closeAtMinimumZoom;
            this.onCloseAtMinimumZoom = onCloseAtMinimumZoom;

            if (!this.mapViewerMode)
                BuildInboundCacheAllLocations();
            inboundCacheByDestMap.Clear();
this.uiTextColor = uiTextColor ?? Color.Black;
            this.showWarpLabels = showLabels;
            this.onNavigateToLocation = navigateToLocation;
            this.onNavigateBack = navigateBack;
            this.onNavigateToRoot = navigateToRoot;
            this.onImmediateSaveApply = onImmediateSaveApply;
            this.onExportFrameworkData = onExportFrameworkData;
            this.onUpdateWarpDestination = onUpdateWarpDestination;
            this.onUpdateWarpSourceAndDestination = onUpdateWarpSourceAndDestination;
            this.getExternalWarpEditedOrAdded = getExternalWarpEditedOrAdded;
            this.getExternalWarpOriginalTarget = getExternalWarpOriginalTarget;
            this.locationHistory = history ?? new Stack<string>();

            // If we opened this menu while the shift-click mouse button is still held,
            // suppress the corresponding action until the button is released.
            this.shiftLeftClickHandled = suppressShiftLeftUntilRelease;
            this.rightClickHandled = suppressShiftRightUntilRelease;

            // Debounce R key if it is held while opening a new menu instance.
            this.rKeyHandled = suppressRUntilRelease;

            // If the menu was opened while the user is still holding a mouse button (common during shift-click
            // navigation), treat that click as already handled to prevent auto-navigating multiple times across
            // newly created menu instances.
            this.shiftLeftClickHandled = suppressShiftLeftUntilRelease;
            this.rightClickHandled = suppressShiftRightUntilRelease;
            
            // Determine the map we are *viewing* in the editor.
            // IMPORTANT: don't fall back to Game1.currentLocation when the viewed map has no warps.
            this.mapName = !string.IsNullOrEmpty(viewedMapName)
                ? viewedMapName
                : (warps.Count > 0 ? warps[0].MapName : (Game1.currentLocation?.Name ?? "Unknown"));

            this.viewedLocation = Game1.getLocationFromName(this.mapName) ?? Game1.currentLocation;
            
            zoomLevel = 0.5f;
            ResetCamera();

            // If we navigated here via a warp click, center on the destination tile.
            if (initialCenterTile.HasValue)
                CenterCameraOnTile(initialCenterTile.Value, initialAnchorScreen);
            
            SafeLog($"WarpEditorMenu created:", LogLevel.Info);
            SafeLog($"  Map: {mapName}", LogLevel.Info);
            SafeLog($"  Warps: {currentMapWarps.Count}", LogLevel.Info);
            SafeLog($"  Camera: ({cameraPosition.X:F0}, {cameraPosition.Y:F0})", LogLevel.Info);
            SafeLog($"  Zoom: {zoomLevel:F2}x", LogLevel.Info);
            SafeLog($"  Viewport: {Game1.uiViewport.Width}x{Game1.uiViewport.Height}", LogLevel.Info);
            SafeLog($"  Show Labels: {showWarpLabels}", LogLevel.Info);
            SafeLog($"  History depth: {locationHistory.Count}", LogLevel.Info);
            SafeLog($"  Controls: Scroll=Zoom, RightClickDrag=Pan, Shift+LeftClick=Go to destination, Shift+RightClick=Go back, R=Return to first map", LogLevel.Info);
        }

        private void CenterCameraOnTile(Point tile, Point? anchorScreen)
        {
            // cameraPosition is in world pixels (same space as Game1.viewport).
            float worldX = tile.X * 64f + 32f;
            float worldY = tile.Y * 64f + 32f;

            // If no anchor is provided, center the tile on screen.
            if (!anchorScreen.HasValue)
            {
                cameraPosition = new Vector2(worldX, worldY);
                SafeLog($"Camera centered on tile ({tile.X},{tile.Y}) => px({cameraPosition.X:F0},{cameraPosition.Y:F0})", LogLevel.Debug);
                return;
            }

            // Keep the clicked tile under the mouse cursor after navigation.
            int screenW = Game1.uiViewport.Width;
            int screenH = Game1.uiViewport.Height;
            int worldW = (int)Math.Ceiling(screenW / zoomLevel);
            int worldH = (int)Math.Ceiling(screenH / zoomLevel);

            float camX = worldX + worldW / 2f - (anchorScreen.Value.X / zoomLevel);
            float camY = worldY + worldH / 2f - (anchorScreen.Value.Y / zoomLevel);

            cameraPosition = new Vector2(camX, camY);
            SafeLog($"Camera anchored on tile ({tile.X},{tile.Y}) at cursor ({anchorScreen.Value.X},{anchorScreen.Value.Y}) => px({cameraPosition.X:F0},{cameraPosition.Y:F0})", LogLevel.Debug);
        }
        
        public Stack<string> GetLocationHistory()
        {
            return locationHistory;
        }

        public string GetViewedMapName()
        {
            return mapName;
        }

        public bool HasModalPopupOpen()
        {
            return destinationEditPopup != null;
        }

        /// <summary>
        /// Allow ModEntry to intercept global buttons (e.g., Escape) when a modal popup is open.
        /// Returns true if the button was handled.
        /// </summary>
        public bool TryHandleGlobalButton(SButton button)
        {
            if (destinationEditPopup == null)
                return false;

            if (button == SButton.Escape)
            {
                destinationEditPopup.Cancel();
                return true;
            }

            if (button == SButton.Enter)
            {
                // Only close if apply succeeds; otherwise keep the popup open.
                destinationEditPopup.TryApply();
                return true;
            }

            return false;
        }
        
        // Called by ModEntry via SMAPI's MouseWheelScrolled event
        public void HandleScrollWheel(int delta)
        {
            // If a modal destination editor popup is open, let it consume the wheel (dropdown scrolling) and
            // suppress map zoom while the modal is active.
            if (destinationEditPopup != null)
            {
                destinationEditPopup.OnMouseWheel(delta);
                return;
            }

            // When the map selector dropdown is open, scroll it instead of zooming.
            if (mapDropdownOpen)
            {
                ScrollMapDropdown(delta);
                return;
            }

            float oldZoom = zoomLevel;
            
            if (delta > 0)
            {
                zoomLevel = Math.Min(zoomLevel + ZOOM_STEP, MAX_ZOOM);
            }
            else if (delta < 0)
            {
                if (closeAtMinimumZoom && zoomLevel <= MIN_ZOOM + 0.001f)
                {
                    onCloseAtMinimumZoom?.Invoke();
                    return;
                }

                zoomLevel = Math.Max(zoomLevel - ZOOM_STEP, MIN_ZOOM);

                if (closeAtMinimumZoom && zoomLevel <= MIN_ZOOM + 0.001f)
                {
                    onCloseAtMinimumZoom?.Invoke();
                    return;
                }
            }
            
            if (oldZoom != zoomLevel)
            {
                SafeLog($"[SMAPI Scroll] Zoom: {oldZoom:F2}x -> {zoomLevel:F2}x", LogLevel.Info);
            }
        }
        
        // Called by ModEntry via SMAPI's UpdateTicked event
        public void HandleInputFromSMAPI(IInputHelper input)
        {
            // Get current mouse position from SMAPI
            var cursorPos = input.GetCursorPosition();
            int mouseX = (int)cursorPos.ScreenPixels.X;
            int mouseY = (int)cursorPos.ScreenPixels.Y;
	            Point currentMousePos = new Point(mouseX, mouseY);
	            
            RecalculateUiLayout();
            bool suppressWorldInteractions = IsPointInsideUi(currentMousePos);
            bool shiftHeld = input.IsDown(SButton.LeftShift) || input.IsDown(SButton.RightShift);

            if (mapViewerMode)
            {
                if (!suppressWorldInteractions && input.IsDown(SButton.MouseRight))
                {
                    if (!isDraggingCamera)
                    {
                        isDraggingCamera = true;
                        lastMousePosition = currentMousePos;
                    }
                    else
                    {
                        int deltaX = currentMousePos.X - lastMousePosition.X;
                        int deltaY = currentMousePos.Y - lastMousePosition.Y;
                        if (deltaX != 0 || deltaY != 0)
                        {
                            cameraPosition.X -= deltaX / zoomLevel;
                            cameraPosition.Y -= deltaY / zoomLevel;
                            lastMousePosition = currentMousePos;
                        }
                    }
                }
                else if (!input.IsDown(SButton.MouseRight))
                {
                    isDraggingCamera = false;
                }

                if (input.IsDown(SButton.OemPlus) || input.IsDown(SButton.Add))
                    zoomLevel = Math.Min(zoomLevel + 0.01f, MAX_ZOOM);

                if (input.IsDown(SButton.OemMinus) || input.IsDown(SButton.Subtract))
                {
                    zoomLevel = Math.Max(zoomLevel - 0.01f, MIN_ZOOM);
                    if (closeAtMinimumZoom && zoomLevel <= MIN_ZOOM + 0.001f)
                        onCloseAtMinimumZoom?.Invoke();
                }

                return;
            }

            // Modal destination editor (E+Click) takes exclusive focus while open.
            if (destinationEditPopup != null)
            {
                destinationEditPopup.HandleInputFromSMAPI(input);
                return;
            }
            
            // Handle Shift + Left-click:
            //  - on a warp point: navigate to its destination (center on target tile)
            //  - on an inbound destination marker (X): navigate to the source warp that lands here
            if (!suppressWorldInteractions && shiftHeld && input.IsDown(SButton.MouseLeft) && !shiftLeftClickHandled)
            {
                shiftLeftClickHandled = true;

                Point worldTilePos = ScreenToWorld(currentMousePos);
                WarpPointData clickedWarp = GetWarpAtPosition(worldTilePos);

                if (clickedWarp != null && onNavigateToLocation != null)
                {
                    string targetLocation = clickedWarp.TargetMap;
                    SafeLog($"[Navigation] Shift+Left-clicked on warp to '{targetLocation}'", LogLevel.Info);

                    // Navigate to destination (history is managed by ModEntry)
                    onNavigateToLocation(targetLocation, clickedWarp.TargetPosition, currentMousePos);
                }
                else
                {
                    var inbound = GetInboundMarkerAtTile(worldTilePos);
                    if (inbound != null && onNavigateToLocation != null)
                    {
                        SafeLog($"[Navigation] Shift+Left-clicked on destination marker -> source '{inbound.SourceMap}' at ({inbound.SourceTile.X},{inbound.SourceTile.Y})", LogLevel.Info);
                        onNavigateToLocation(inbound.SourceMap, inbound.SourceTile, currentMousePos);
                    }
                    else
                    {
                        SafeLog($"[Navigation] Shift+Left-click at tile ({worldTilePos.X}, {worldTilePos.Y}) - no warp or destination marker found", LogLevel.Debug);
                    }
                }
            }
            else if (!input.IsDown(SButton.MouseLeft))
            {
                shiftLeftClickHandled = false;
            }
            
            // Handle Shift + Right-click = Go back to previous location
            if (!suppressWorldInteractions && shiftHeld && input.IsDown(SButton.MouseRight) && !rightClickHandled)
            {
                rightClickHandled = true;
                
                if (onNavigateBack != null && locationHistory.Count > 0)
                {
                    SafeLog($"[Navigation] Going back (history depth: {locationHistory.Count})", LogLevel.Info);
                    onNavigateBack();
                }
                else
                {
                    SafeLog($"[Navigation] No previous location to go back to", LogLevel.Debug);
                }
            }
            // Handle Right-click drag for camera panning (only when shift is NOT held)
            else if (!suppressWorldInteractions && !shiftHeld && input.IsDown(SButton.MouseRight))
            {
                if (!isDraggingCamera)
                {
                    // Start a drag; we intentionally don't pan on the first frame.
                    isDraggingCamera = true;
                    lastMousePosition = currentMousePos;
                    SafeLog($"[SMAPI Input] Camera pan started at ({mouseX}, {mouseY})", LogLevel.Debug);
                }
                else
                {
                    // Continue drag: pan based on mouse delta since last tick.
                    int deltaX = currentMousePos.X - lastMousePosition.X;
                    int deltaY = currentMousePos.Y - lastMousePosition.Y;

                    if (deltaX != 0 || deltaY != 0)
                    {
                        cameraPosition.X -= deltaX / zoomLevel;
                        cameraPosition.Y -= deltaY / zoomLevel;
                        lastMousePosition = currentMousePos;
                        SafeLog($"[SMAPI Input] Panning: delta=({deltaX},{deltaY}) cam=({cameraPosition.X:F0},{cameraPosition.Y:F0})", LogLevel.Trace);
                    }
                }
            }
            else if (!input.IsDown(SButton.MouseRight))
            {
                if (isDraggingCamera)
                {
                    SafeLog($"[SMAPI Input] Camera pan ended", LogLevel.Debug);
                }
                isDraggingCamera = false;
                rightClickHandled = false;
            }
            
            // WASD keyboard panning intentionally disabled.
            
            // Handle zoom keys
            if (input.IsDown(SButton.OemPlus) || input.IsDown(SButton.Add))
            {
                zoomLevel = Math.Min(zoomLevel + 0.01f, MAX_ZOOM);
            }
            if (input.IsDown(SButton.OemMinus) || input.IsDown(SButton.Subtract))
            {
                zoomLevel = Math.Max(zoomLevel - 0.01f, MIN_ZOOM);
            }
            
            // R = return to the first (root) location in the editor history.
            if (input.IsDown(SButton.R) && !rKeyHandled)
            {
                rKeyHandled = true;

                if (onNavigateToRoot != null && locationHistory.Count > 0)
                {
                    SafeLog($"[Navigation] Returning to root (history depth: {locationHistory.Count})", LogLevel.Info);
                    onNavigateToRoot();
                }
                else
                {
                    SafeLog("[Navigation] Already at root (no history)", LogLevel.Debug);
                }
            }
            else if (!input.IsDown(SButton.R))
            {
                rKeyHandled = false;
            }
        }
        
        private void ResetCamera()
        {
            GameLocation location = viewedLocation ?? Game1.currentLocation;
            if (location?.Map != null)
            {
                // Center camera on map
                int mapWidth = location.Map.DisplayWidth;
                int mapHeight = location.Map.DisplayHeight;
                cameraPosition = new Vector2(mapWidth / 2, mapHeight / 2);
                zoomLevel = 0.5f;
                SafeLog($"Camera reset: pos=({cameraPosition.X},{cameraPosition.Y}) zoom={zoomLevel}", LogLevel.Debug);
            }
        }
        
        public List<WarpPointData> GetWarps()
        {
            return currentMapWarps;
        }

        public void ReplaceWarps(List<WarpPointData> warps)
        {
            currentMapWarps = warps ?? new List<WarpPointData>();
            selectedWarp = null;
            isDragging = false;
            selectedWarpDragStartSource = Point.Zero;
            inboundCacheByDestMap.Clear();
            inboundMarkersDirty = true;
        }
        
        public override void receiveLeftClick(int x, int y, bool playSound = true)
        {
            base.receiveLeftClick(x, y, playSound);

            // Used for cursor-anchored navigation when shift-clicking from within menu click handlers.
            Point currentMousePos = new Point(x, y);

            // If a modal popup is open, it owns mouse input unless it is waiting for a map tile click.
            if (destinationEditPopup != null)
            {
                if (destinationEditPopup.IsPickingCoordinate && !destinationEditPopup.ContainsScreenPoint(currentMousePos))
                {
                    destinationEditPopup.ApplyMapClick(ScreenToWorld(currentMousePos));
                    return;
                }

                destinationEditPopup.ReceiveLeftClick(x, y);
                return;
            }

            RecalculateUiLayout();

            // Map selector dropdown and framework export button have priority over world interactions.
            Point mouse = currentMousePos;
            if (exportButtonRect.Contains(mouse))
            {
                onExportFrameworkData?.Invoke();
                Game1.playSound("bigSelect");
                return;
            }

            if (mapSelectorRect.Contains(mouse))
            {
                ToggleMapDropdown();
                return;
            }

            if (mapDropdownOpen)
            {
                var options = GetAvailableLocationOptions();
                if (options == null || options.Count == 0)
                {
                    CloseMapDropdown(playSound: true);
                    return;
                }

                var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                    mapSelectorRect,
                    options.Count,
                    WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                    MapDropdownMaxVisibleItems
                );

                if (layout.ListRect.Contains(mouse))
                {
                    if (WarpMasterFrameworkGmcmUi.TryBeginScrollbarDrag(mouse, layout, mapDropdownScrollIndex, out int newScroll, out int dragOffsetY))
                    {
                        mapDropdownScrollIndex = newScroll;
                        mapDropdownScrollDragging = true;
                        mapDropdownScrollDragOffsetY = dragOffsetY;
                        return;
                    }

                    TrySelectMapFromDropdown(mouse);
                }
                else
                {
                    CloseMapDropdown(playSound: true);
                }

                return;
            }

            if (mapViewerMode)
                return;

            // Hold D + Left Click to delete a warp point at the clicked tile.
            if (Keyboard.GetState().IsKeyDown(Keys.D))
            {
                Point dWorldPos = ScreenToWorld(new Point(x, y));
                WarpPointData dWarp = GetWarpAtPosition(dWorldPos);
                if (dWarp != null)
                {
                    currentMapWarps.Remove(dWarp);
                    if (selectedWarp == dWarp)
                        selectedWarp = null;

                    // Apply/save immediately so runtime warps update without leaving the editor.
                    ApplyCurrentEditorEditsImmediately("delete warp");
                    return;
                }
            }

            // Inbound destination markers ("X") have priority over editing/dragging warps on this map.
            // - Left-click drag: moves the destination tile for the source warp.
            // - Shift + left-click: jumps to the source warp.
            {
                Point xWorldPos = ScreenToWorld(new Point(x, y));
                var inbound = GetInboundMarkerAtTile(xWorldPos);
                if (inbound != null)
                {
					if (IsShiftHeldKeyboard() && onNavigateToLocation != null)
                    {
                        // Debounce: prevent HandleInputFromSMAPI from also firing a shift-click action on the same frame.
                        this.shiftLeftClickHandled = true;
                        onNavigateToLocation(inbound.SourceMap, inbound.SourceTile, currentMousePos);
                        return;
                    }

					// Hold E + Left Click on a destination marker to edit the source warp (same popup as normal warps).
					if (Keyboard.GetState().IsKeyDown(Keys.E))
					{
						OpenDestinationEditPopupForInbound(inbound);
						return;
					}

                    // Start dragging this inbound destination marker.
                    this.selectedInboundMarker = inbound;
                    this.isDraggingInboundMarker = true;
                    return;
                }
            }


// Shift + left-click on a SOURCE warp point: jump to its destination (center on target tile).
if (IsShiftHeldKeyboard() && onNavigateToLocation != null)
{
    Point shiftWorldPos = ScreenToWorld(new Point(x, y));
    WarpPointData shiftClickedWarp = GetWarpAtPosition(shiftWorldPos);

    if (shiftClickedWarp != null)
    {
        // Debounce: prevent HandleInputFromSMAPI from also firing a shift-click action on the same frame.
        this.shiftLeftClickHandled = true;

        string targetLocation = shiftClickedWarp.TargetMap;
        onNavigateToLocation(targetLocation, shiftClickedWarp.TargetPosition, currentMousePos);
        return;
    }
}

            // Hold A + Left Click to add a NEW warp point at the clicked tile.
            if (Keyboard.GetState().IsKeyDown(Keys.A))
            {
bool addMagicWarp = Keyboard.GetState().IsKeyDown(Keys.M);

                Point aWorldPos = ScreenToWorld(new Point(x, y));
                WarpPointData existingAtTile = GetWarpAtPosition(aWorldPos);
                if (existingAtTile != null)
                {
                    // If a warp already exists here, just edit it.
                    OpenDestinationEditPopup(existingAtTile);
                    return;
                }

                var newWarp = new WarpPointData
                {
                    MapName = mapName ?? "",
	                    IsAddedByMod = true,
                    WarpType = addMagicWarp ? "Door" : "Warp",
                    OriginalPosition = aWorldPos,
                    ModifiedPosition = aWorldPos,
                    TrueOriginalPosition = aWorldPos,

                    // If adding MagicWarp, default to Back/TouchAction and command "MagicWarp"
                    DoorLayerName = addMagicWarp ? "Back" : "",
                    DoorPropertyName = addMagicWarp ? "TouchAction" : "",
                    DoorTokenOrder = addMagicWarp ? "mapxy" : "",
                    DoorCommand = addMagicWarp ? "MagicWarp" : "Warp",

                    TargetMap = "",
                    TargetPosition = Point.Zero,
                    OriginalTargetMap = "",
                    OriginalTargetPosition = Point.Zero
                };

                currentMapWarps.Add(newWarp);
                pendingNewWarp = newWarp;
                OpenDestinationEditPopup(newWarp);
                return;
            }

            // Hold E + Left Click on a warp to edit its destination.
            if (Keyboard.GetState().IsKeyDown(Keys.E))
            {
                Point eWorldPos = ScreenToWorld(new Point(x, y));
                WarpPointData eWarp = GetWarpAtPosition(eWorldPos);
                if (eWarp != null)
                {
                    OpenDestinationEditPopup(eWarp);
                    return;
                }
            }
            
            SafeLog($"[Menu] Left click at ({x}, {y})", LogLevel.Debug);
            
            // Convert screen position to world position
            Point worldPos = ScreenToWorld(new Point(x, y));
            
            // Try to select a warp
            selectedWarp = GetWarpAtPosition(worldPos);
            if (selectedWarp != null)
            {
                isDragging = true;
                selectedWarpDragStartSource = selectedWarp.ModifiedPosition;
                dragOffset = new Point(
                    selectedWarp.ModifiedPosition.X - worldPos.X,
                    selectedWarp.ModifiedPosition.Y - worldPos.Y
                );
                SafeLog($"Selected warp at ({selectedWarp.ModifiedPosition.X}, {selectedWarp.ModifiedPosition.Y})", LogLevel.Debug);
            }
        }
        
        public override void leftClickHeld(int x, int y)
        {
            if (mapDropdownOpen && mapDropdownScrollDragging)
            {
                var options = GetAvailableLocationOptions();
                if (options != null && options.Count > 0)
                {
                    var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                        mapSelectorRect,
                        options.Count,
                        WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                        MapDropdownMaxVisibleItems
                    );

                    mapDropdownScrollIndex = WarpMasterFrameworkGmcmUi.UpdateScrollbarDrag(y, mapDropdownScrollDragOffsetY, layout);
                }
                return;
            }

            base.leftClickHeld(x, y);

            if (destinationEditPopup != null)
            {
                destinationEditPopup.ReceiveLeftClickHeld(x, y);
                return;
            }

            if (mapViewerMode)
                return;
            
            if (isDraggingInboundMarker && selectedInboundMarker != null)
            {
                Point worldPos = ScreenToWorld(new Point(x, y));
                if (worldPos != selectedInboundMarker.DestinationTile)
                {
                    selectedInboundMarker.DestinationTile = worldPos;

                    // Update the associated SOURCE warp's destination (in another map) immediately.
                    // Destination map is the one we're currently viewing.
                    
if (onUpdateWarpSourceAndDestination != null)
{
    onUpdateWarpSourceAndDestination.Invoke(
        selectedInboundMarker.SourceMap,
        selectedInboundMarker.SourceTile,
        selectedInboundMarker.SourceTile,
        this.mapName ?? "",
        worldPos
    );
}
else
{
    onUpdateWarpDestination?.Invoke(
        selectedInboundMarker.SourceMap,
        selectedInboundMarker.SourceTile,
        this.mapName ?? "",
        worldPos
    );
}

					// Rebuild next draw so the marker reflects any immediate apply/merge logic.
					inboundCacheByDestMap.Clear();
                }
                return;
            }

            if (isDragging && selectedWarp != null)
            {
                Point worldPos = ScreenToWorld(new Point(x, y));
                selectedWarp.ModifiedPosition = new Point(
                    worldPos.X + dragOffset.X,
                    worldPos.Y + dragOffset.Y
                );
            }
        }
        
        public override void releaseLeftClick(int x, int y)
        {
            mapDropdownScrollDragging = false;
            base.releaseLeftClick(x, y);

            if (destinationEditPopup != null)
            {
                destinationEditPopup.ReceiveLeftRelease(x, y);
                return;
            }

            if (mapViewerMode)
                return;

            if (isDraggingInboundMarker)
            {
                isDraggingInboundMarker = false;
                ApplyCurrentEditorEditsImmediately("move inbound destination marker");
                selectedInboundMarker = null;
                return;
            }
            
            if (isDragging)
            {
                isDragging = false;
                if (selectedWarp != null)
                {
                    SafeLog($"Warp moved to ({selectedWarp.ModifiedPosition.X}, {selectedWarp.ModifiedPosition.Y})", LogLevel.Info);

                        // Commit source move immediately so edited coloring updates without changing maps.
                        // Use the source tile where this drag actually started, not the immutable true-original tile.
                        // Otherwise repeated moves try to remove the original warp instead of the currently applied live warp,
                        // causing the marker to snap back, duplicate, or move again on the next editor open.
                        Point oldSource = selectedWarpDragStartSource != Point.Zero
                            ? selectedWarpDragStartSource
                            : (selectedWarp.LastAppliedPosition != Point.Zero ? selectedWarp.LastAppliedPosition : selectedWarp.ModifiedPosition);
                        Point newSource = selectedWarp.ModifiedPosition;
                        if (oldSource != newSource)
                        {
                            if (selectedWarp.TrueOriginalPosition == Point.Zero)
                                selectedWarp.TrueOriginalPosition = selectedWarp.OriginalPosition != Point.Zero ? selectedWarp.OriginalPosition : oldSource;

                            selectedWarp.LastAppliedPosition = oldSource;
                            if (string.IsNullOrEmpty(selectedWarp.LastAppliedTargetMap))
                                selectedWarp.LastAppliedTargetMap = selectedWarp.TargetMap ?? "";
                            if (selectedWarp.LastAppliedTargetPosition == Point.Zero)
                                selectedWarp.LastAppliedTargetPosition = selectedWarp.TargetPosition;

                            onUpdateWarpSourceAndDestination?.Invoke(this.mapName ?? "", oldSource, newSource, selectedWarp.TargetMap ?? "", selectedWarp.TargetPosition);
                            ApplyCurrentEditorEditsImmediately("drag warp source");
                        }
                        selectedWarpDragStartSource = Point.Zero;

                }
                selectedWarpDragStartSource = Point.Zero;
                selectedWarp = null;
            }
        }
        
        public override void receiveRightClick(int x, int y, bool playSound = true)
        {
            // Camera pan is handled by SMAPI events in HandleInputFromSMAPI
            SafeLog($"[Menu] Right click at ({x}, {y})", LogLevel.Debug);
        }
        
        public override void receiveScrollWheelAction(int direction)
        {
            // Scroll is handled by SMAPI events, but keep this as fallback
            base.receiveScrollWheelAction(direction);
            if (debugEnabled) SafeLog($"[Menu] Scroll wheel: {direction}", LogLevel.Debug);
            HandleScrollWheel(direction);
        }
        
        public override void receiveKeyPress(Keys key)
        {
            // Key handling is done via SMAPI events in ModEntry
            // Don't call base - it might interfere
            if (debugEnabled) SafeLog($"[Menu] Key press: {key}", LogLevel.Debug);
        }

        public override void update(GameTime time)
        {
            base.update(time);
            // Input handling moved to SMAPI events.

            // Animate map tiles for the viewed location (e.g. water, lava, animated floors).
            // The game normally advances xTile animations via Map.Update.
            // Since we're rendering maps without actually "entering" them, we need to tick the map ourselves.
            try
            {
                // xTile.Map.Update expects elapsed milliseconds (long), not XNA GameTime.
                long elapsedMs = (long)time.ElapsedGameTime.TotalMilliseconds;
                this.viewedLocation?.Map?.Update(elapsedMs);
            }
            catch (Exception ex)
            {
                // Don't crash the menu if a modded map has unexpected state.
                SafeLog($"[AnimateTiles] Failed to update map animations for '{this.mapName}': {ex}", LogLevel.Trace);
            }
        }
        
        public override void performHoverAction(int x, int y)
        {
            base.performHoverAction(x, y);
            // Input handling moved to SMAPI events
        }
        
        private WarpPointData GetWarpAtPosition(Point worldTilePos)
        {
            SafeLog($"Looking for warp at tile ({worldTilePos.X}, {worldTilePos.Y})", LogLevel.Debug);
            
            foreach (var warp in currentMapWarps)
            {
                // Exact tile match only - must click directly on the warp tile
                if (warp.ModifiedPosition.X == worldTilePos.X && warp.ModifiedPosition.Y == worldTilePos.Y)
                {
                    SafeLog($"Found warp: {warp.WarpType} at ({warp.ModifiedPosition.X}, {warp.ModifiedPosition.Y}) -> {warp.TargetMap}", LogLevel.Debug);
                    return warp;
                }
            }
            
            SafeLog($"No warp found at tile ({worldTilePos.X}, {worldTilePos.Y})", LogLevel.Debug);
            return null;
        }

        private bool IsShiftHeldKeyboard()
        {
            var ks = Keyboard.GetState();
            return ks.IsKeyDown(Keys.LeftShift) || ks.IsKeyDown(Keys.RightShift);
        }

private void BuildInboundCacheAllLocations()
{
    inboundCacheByDestMap.Clear();

    // Use Data/Locations so we can detect door/warp tile properties for maps the player hasn't visited yet.
    Dictionary<string, LocationData> allLocations = null;
    try
    {
        allLocations = Game1.content.Load<Dictionary<string, LocationData>>("Data/Locations");
    }
    catch
    {
        allLocations = null;
    }

    // Fallback to loaded locations if Data/Locations isn't available in the current context.
    if (allLocations == null || allLocations.Count == 0)
    {
        foreach (var loc in Game1.locations)
        {
            if (loc == null)
                continue;
            AddInboundFromSourceLocation(loc.Name, inboundDetector?.DetectWarpsForLocation(loc));
        }
        return;
    }

    foreach (var pair in allLocations)
    {
        string sourceLocationName = pair.Key;
        if (string.IsNullOrWhiteSpace(sourceLocationName))
            continue;

        List<WarpPointData> detected = null;

        try
        {
            // Detect even if not currently loaded.
            detected = inboundDetector?.DetectWarpsForLocationName(sourceLocationName);
        }
        catch
        {
            detected = null;
        }

        AddInboundFromSourceLocation(sourceLocationName, detected);
    }
}

void AddInboundFromSourceLocation(string sourceLocationName, List<WarpPointData> detected)
{
    if (string.IsNullOrWhiteSpace(sourceLocationName))
        return;

    if (detected == null || detected.Count == 0)
        return;

    foreach (var w in detected)
    {
        if (w == null)
            continue;

        string destMap = (w.TargetMap ?? "");
        if (string.IsNullOrWhiteSpace(destMap))
            continue;

        if (!inboundCacheByDestMap.TryGetValue(destMap, out var list))
        {
            list = new List<InboundDestinationMarker>();
            inboundCacheByDestMap[destMap] = list;
        }

        var marker = new InboundDestinationMarker
        {
            SourceMap = sourceLocationName,
            SourceTile = w.ModifiedPosition,
            WarpType = w.WarpType ?? "Warp",
            DestinationTile = w.TargetPosition,
            OriginalDestinationTile = (
                (getExternalWarpOriginalTarget?.Invoke(sourceLocationName, w.ModifiedPosition) is WarpPointData orig &&
                 string.Equals(orig.GetOriginalTargetMapFallback(), destMap, StringComparison.OrdinalIgnoreCase))
                    ? orig.GetOriginalTargetPosFallback()
                    : (string.Equals(w.GetOriginalTargetMapFallback(), destMap, StringComparison.OrdinalIgnoreCase)
                        ? w.GetOriginalTargetPosFallback()
                        : w.TargetPosition)
            ),
            IsEditedOrAdded = getExternalWarpEditedOrAdded?.Invoke(sourceLocationName, w.ModifiedPosition) ?? false
        };

        list.Add(marker);
    }
}

void RebuildInboundDestinationMarkers()
    {
        string destMap = this.mapName ?? "";
        if (string.IsNullOrEmpty(destMap))
            return;

        // Ensure cache exists (build once, and rebuild when explicitly marked dirty).
        if (inboundCacheByDestMap.Count == 0)
            BuildInboundCacheAllLocations();

        // Preserve selection stability if possible.
        var existingBySource = new Dictionary<(string map, Point tile, string warpType), InboundDestinationMarker>();
        foreach (var m in inboundDestinationMarkers)
        {
            if (m == null)
                continue;
            existingBySource[(m.SourceMap ?? "", m.SourceTile, m.WarpType ?? "Warp")] = m;
        }

        inboundDestinationMarkers.Clear();

        if (!inboundCacheByDestMap.TryGetValue(destMap, out var cached) || cached == null)
        {
            inboundMarkersDirty = false;
            return;
        }

        foreach (var c in cached)
        {
            if (c == null)
                continue;

            var key = (c.SourceMap ?? "", c.SourceTile, c.WarpType ?? "Warp");
            if (existingBySource.TryGetValue(key, out var existing) && existing != null)
            {
                existing.DestinationTile = c.DestinationTile;
                if (c.OriginalDestinationTile != Point.Zero)
                    existing.OriginalDestinationTile = c.OriginalDestinationTile;
                existing.IsEditedOrAdded = c.IsEditedOrAdded;
                inboundDestinationMarkers.Add(existing);
            }
            else
            {
                inboundDestinationMarkers.Add(c);
            }
        }

        inboundMarkersDirty = false;
    }

        private InboundDestinationMarker GetInboundMarkerAtTile(Point worldTilePos)
        {
			// Rebuild on-demand to stay in sync with any runtime edits.
			if (inboundMarkersDirty || inboundDestinationMarkers.Count == 0)
				RebuildInboundDestinationMarkers();

            // If multiple source warps share the same destination tile, we take the first.
            return inboundDestinationMarkers.FirstOrDefault(m => m.DestinationTile == worldTilePos);
        }
        
        private Point ScreenToWorld(Point screenPos)
        {
			// Game-like mapping: screen -> world based on a viewport rectangle.
			// We treat cameraPosition as the world-pixel center of the view.
			var vp = BuildWorldViewport();
			float worldPixelX = vp.X + (screenPos.X / zoomLevel);
			float worldPixelY = vp.Y + (screenPos.Y / zoomLevel);
			int tileX = (int)Math.Floor(worldPixelX / 64f);
			int tileY = (int)Math.Floor(worldPixelY / 64f);
			return new Point(tileX, tileY);
        }
        
        private Vector2 WorldToScreen(Point tilePos)
        {
			// Game-like mapping: world -> screen based on a viewport rectangle.
			var vp = BuildWorldViewport();
			float worldX = tilePos.X * 64f + 32f;
			float worldY = tilePos.Y * 64f + 32f;
			float sx = (worldX - vp.X) * zoomLevel;
			float sy = (worldY - vp.Y) * zoomLevel;
			return new Vector2(sx, sy);
        }


			/// <summary>
			/// Convert a world tile position to LOCAL viewport space (world pixels relative to <see cref="Game1.viewport"/>).
			/// This is intended for the world draw pass where we apply <see cref="zoomLevel"/> via the SpriteBatch transform.
			/// </summary>
			private Vector2 WorldToLocal(Point tilePos)
			{
				var vp = Game1.viewport;
				float worldX = tilePos.X * 64f + 32f;
				float worldY = tilePos.Y * 64f + 32f;
				return new Vector2(worldX - vp.X, worldY - vp.Y);
			}

		private Vector2 WorldPixelToScreen(Vector2 worldPixel)
		{
			var vp = BuildWorldViewport();
			float sx = (worldPixel.X - vp.X) * zoomLevel;
			float sy = (worldPixel.Y - vp.Y) * zoomLevel;
			return new Vector2(sx, sy);
		}

		/// <summary>Build the world viewport rectangle (in world pixels) for the current camera + zoom.</summary>
		private xTile.Dimensions.Rectangle BuildWorldViewport()
		{
			int screenW = Game1.uiViewport.Width;
			int screenH = Game1.uiViewport.Height;
			int worldW = (int)Math.Ceiling(screenW / zoomLevel);
			int worldH = (int)Math.Ceiling(screenH / zoomLevel);
			int x = (int)Math.Floor(cameraPosition.X - worldW / 2f);
			int y = (int)Math.Floor(cameraPosition.Y - worldH / 2f);
			return new xTile.Dimensions.Rectangle(x, y, worldW, worldH);
		}
        
        public override void draw(SpriteBatch b)
        {
            b.End();

            // Draw dark background first (screen space).
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
            b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * 0.9f);
            b.End();

            var viewport = BuildWorldViewport();
            var oldViewport = Game1.viewport;
            var oldCurrentLocation = Game1.currentLocation;
            var location = viewedLocation ?? Game1.currentLocation;

            Game1.viewport = viewport;
            try
            {
                if (location != null)
                    Game1.currentLocation = location;

                b.Begin(
                    SpriteSortMode.Deferred,
                    BlendState.AlphaBlend,
                    SamplerState.PointClamp,
                    DepthStencilState.None,
                    RasterizerState.CullNone,
                    transformMatrix: Matrix.CreateScale(zoomLevel, zoomLevel, 1f)
                );

                bool drewFullSceneCapture = false;
                if (location != null && ShouldUseFullSceneScreenshotCapture())
                {
                    b.End();
                    drewFullSceneCapture = TryDrawFullSceneScreenshotLocalViewport(b, location, viewport);
                    b.Begin(
                        SpriteSortMode.Deferred,
                        BlendState.AlphaBlend,
                        SamplerState.PointClamp,
                        DepthStencilState.None,
                        RasterizerState.CullNone,
                        transformMatrix: Matrix.CreateScale(zoomLevel, zoomLevel, 1f)
                    );
                }

                if (!drewFullSceneCapture)
                {
                    // Draw stable terrain first so missing tilesheets/terrain never disappear from the preview.
                    if (location?.Map != null)
                        DrawMapLayersLocalViewport(b, location);

                    // Then optionally ask the game to draw the live scene on top: player, NPCs, foliage,
                    // furniture, objects, effects, and other transient visuals.
                    if (location != null && ShouldDrawLiveSceneOverlay())
                        DrawLiveSceneOverlayLocalViewport(b, location);
                }

                if (!mapViewerMode)
                {
                    foreach (var warp in currentMapWarps)
                        DrawWarpPoint(b, warp, warp == selectedWarp);

                    if (inboundMarkersDirty || inboundDestinationMarkers.Count == 0)
                        RebuildInboundDestinationMarkers();

                    foreach (var marker in inboundDestinationMarkers)
                        DrawInboundDestinationMarker(b, marker, marker == selectedInboundMarker);
                }

                b.End();
            }
            finally
            {
                Game1.currentLocation = oldCurrentLocation;
                Game1.viewport = oldViewport;
            }

            // Draw UI overlay in screen space.
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
            if (!mapViewerMode)
                DrawWarpLabels(b);
            DrawUI(b);
            destinationEditPopup?.Draw(b);
            drawMouse(b);
            b.End();

            // Restart the sprite batch in the default state for the game.
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
        }
        


        
        private Dictionary<string, Texture2D> tileSheetTextureCache = new Dictionary<string, Texture2D>();


        private Texture2D GetTileSheetTexture(xTile.Tiles.TileSheet tileSheet)
        {
            string imageSource = tileSheet.ImageSource;
            
            if (string.IsNullOrEmpty(imageSource))
                return null;
                
            // Check cache first
            if (tileSheetTextureCache.TryGetValue(imageSource, out Texture2D cached))
            {
                return cached;
            }
            
            Texture2D texture = null;
            
            try
            {
                // Clean up the path
                string assetName = imageSource.Replace("\\", "/");
                if (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    assetName = assetName.Substring(0, assetName.Length - 4);
                if (assetName.EndsWith(".xnb", StringComparison.OrdinalIgnoreCase))
                    assetName = assetName.Substring(0, assetName.Length - 4);
                
                // Try various path combinations
                string[] pathsToTry = new string[]
                {
                    assetName,
                    "Maps/" + assetName,
                    "Maps/" + System.IO.Path.GetFileName(assetName),
                    System.IO.Path.GetFileName(assetName),
                    assetName.Replace("Maps/", ""),
                };
                
                foreach (string path in pathsToTry)
                {
                    try
                    {
                        texture = Game1.content.Load<Texture2D>(path);
                        if (texture != null)
                        {
                            SafeLog($"Loaded tilesheet '{tileSheet.Id}' from: {path}", LogLevel.Trace);
                            break;
                        }
                    }
                    catch { }
                }
                
                // Try using Game1.temporaryContent as fallback
                if (texture == null)
                {
                    try
                    {
                        texture = Game1.temporaryContent.Load<Texture2D>(assetName);
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                SafeLog($"Error loading tilesheet texture '{tileSheet?.Id}': {ex}", LogLevel.Trace);
            }

            tileSheetTextureCache[imageSource] = texture;
            
            if (texture == null)
            {
                SafeLog($"Could not load texture for tilesheet: {tileSheet.Id} (ImageSource: {imageSource})", LogLevel.Debug);
            }
            
            return texture;
        }

                        private bool ShouldUseFullSceneScreenshotCapture()
            {
                // The editor should render the full live scene just like Mapster's full-scene map view.
                // Terrain-only is only respected for the optional map-viewer mode.
                return !mapViewerMode || localMapRenderMode == LocalMapRenderMode.FullScene;
            }

            private bool TryDrawFullSceneScreenshotLocalViewport(SpriteBatch b, GameLocation location, xTile.Dimensions.Rectangle viewport)
            {
                if (location == null || viewport.Width <= 0 || viewport.Height <= 0)
                    return false;

                if (GameDrawMethod == null || Game1.graphics?.GraphicsDevice == null)
                    return false;

                if (!EnsureFullSceneCaptureTarget(viewport.Width, viewport.Height))
                    return false;

                GraphicsDevice graphics = Game1.graphics.GraphicsDevice;
                RenderTargetBinding[] oldRenderTargets = graphics.GetRenderTargets();
                bool oldTakingMapScreenshot = Game1.game1.takingMapScreenshot;
                bool oldDisplayHud = Game1.displayHUD;
                float oldBaseZoom = Game1.options.baseZoomLevel;
                IClickableMenu oldActiveClickableMenu = Game1.activeClickableMenu;
                RenderTarget2D oldLightmap = null;

                try
                {
                    if (LightmapField != null)
                    {
                        oldLightmap = LightmapField.GetValue(null) as RenderTarget2D;
                        LightmapField.SetValue(null, null);
                    }

                    Game1.game1.takingMapScreenshot = true;
                    Game1.displayHUD = false;
                    Game1.options.baseZoomLevel = 1f;
                    Game1.activeClickableMenu = null;

                    // Location Map allocates a lightmap matching its render chunk before calling Game1._draw.
                    // That is the missing piece from our earlier attempt and helps trees/grass/live terrain render correctly.
                    try
                    {
                        AllocateLightmapMethod?.Invoke(null, new object[] { viewport.Width, viewport.Height });
                    }
                    catch (Exception ex)
                    {
                        SafeLog($"Could not allocate screenshot lightmap: {ex.Message}", LogLevel.Trace);
                    }

                    graphics.SetRenderTarget(fullSceneCaptureTarget);
                    graphics.Clear(Color.Transparent);

                    GameDrawMethod.Invoke(Game1.game1, new object[] { Game1.currentGameTime, fullSceneCaptureTarget });

                    graphics.SetRenderTargets(oldRenderTargets);

                    b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);
                    b.Draw(
                        fullSceneCaptureTarget,
                        new Rectangle(
                            0,
                            0,
                            (int)Math.Ceiling(viewport.Width * zoomLevel),
                            (int)Math.Ceiling(viewport.Height * zoomLevel)
                        ),
                        Color.White
                    );
                    b.End();

                    return true;
                }
                catch (Exception ex)
                {
                    try { graphics.SetRenderTargets(oldRenderTargets); }
                    catch { try { graphics.SetRenderTarget(null); } catch { } }

                    SafeLog($"Full-scene screenshot capture failed; falling back to hybrid renderer: {ex}", LogLevel.Trace);
                    return false;
                }
                finally
                {
                    try
                    {
                        if (LightmapField != null)
                        {
                            RenderTarget2D tempLightmap = LightmapField.GetValue(null) as RenderTarget2D;
                            if (tempLightmap != null && tempLightmap != oldLightmap && !tempLightmap.IsDisposed)
                                tempLightmap.Dispose();

                            LightmapField.SetValue(null, oldLightmap);
                        }
                    }
                    catch
                    {
                        // Lightmap restoration must never crash the menu.
                    }

                    Game1.activeClickableMenu = oldActiveClickableMenu;
                    Game1.options.baseZoomLevel = oldBaseZoom;
                    Game1.displayHUD = oldDisplayHud;
                    Game1.game1.takingMapScreenshot = oldTakingMapScreenshot;
                }
            }

            private bool EnsureFullSceneCaptureTarget(int width, int height)
            {
                if (width <= 0 || height <= 0)
                    return false;

                if (fullSceneCaptureTarget != null
                    && !fullSceneCaptureTarget.IsDisposed
                    && fullSceneCaptureWidth == width
                    && fullSceneCaptureHeight == height)
                {
                    return true;
                }

                DisposeFullSceneCaptureTarget();

                try
                {
                    fullSceneCaptureTarget = new RenderTarget2D(
                        Game1.graphics.GraphicsDevice,
                        width,
                        height,
                        false,
                        SurfaceFormat.Color,
                        DepthFormat.None,
                        0,
                        RenderTargetUsage.DiscardContents
                    );
                    fullSceneCaptureWidth = width;
                    fullSceneCaptureHeight = height;
                    return true;
                }
                catch (Exception ex)
                {
                    SafeLog($"Could not allocate full-scene capture target {width}x{height}: {ex.Message}", LogLevel.Trace);
                    fullSceneCaptureTarget = null;
                    fullSceneCaptureWidth = -1;
                    fullSceneCaptureHeight = -1;
                    return false;
                }
            }

            private void DisposeFullSceneCaptureTarget()
            {
                try
                {
                    if (fullSceneCaptureTarget != null && !fullSceneCaptureTarget.IsDisposed)
                        fullSceneCaptureTarget.Dispose();
                }
                catch
                {
                    // Capture cleanup must never crash the menu.
                }

                fullSceneCaptureTarget = null;
                fullSceneCaptureWidth = -1;
                fullSceneCaptureHeight = -1;
            }

            private bool ShouldDrawLiveSceneOverlay()
            {
                // The editor keeps full-scene rendering by default.
                // The Terrain Only setting applies to the local map viewer.
                return !mapViewerMode || localMapRenderMode == LocalMapRenderMode.FullScene;
            }

private void DrawLiveSceneOverlayLocalViewport(SpriteBatch b, GameLocation location)
            {
                if (location == null)
                    return;

                try
                {
                    location.draw(b);
                }
                catch (Exception ex)
                {
                    SafeLog($"Error drawing live location scene overlay: {ex}", LogLevel.Trace);
                }
            }

			private void DrawMapLayersLocalViewport(SpriteBatch b, GameLocation location)
			{
				if (location?.Map == null)
					return;
				DrawMapLayerLocalViewport(b, location, "Back");
				DrawMapLayerLocalViewport(b, location, "Buildings");
				DrawMapLayerLocalViewport(b, location, "Front");
			}

			private void DrawMapLayerLocalViewport(SpriteBatch b, GameLocation location, string layerName)
			{
				var map = location.Map;
				if (map == null)
					return;
				var layer = map.GetLayer(layerName);
				if (layer == null)
					return;

				var vp = Game1.viewport; // world-pixel rectangle
				const int tileWorldSize = 64; // Stardew renders tiles at 64x64 world pixels

				int startTileX = Math.Max(0, (vp.X / tileWorldSize) - 1);
				int startTileY = Math.Max(0, (vp.Y / tileWorldSize) - 1);
				int endTileX = Math.Min(layer.LayerWidth, ((vp.X + vp.Width) / tileWorldSize) + 2);
				int endTileY = Math.Min(layer.LayerHeight, ((vp.Y + vp.Height) / tileWorldSize) + 2);

				for (int y = startTileY; y < endTileY; y++)
				{
					for (int x = startTileX; x < endTileX; x++)
					{
						var tile = layer.Tiles[x, y];
						if (tile == null)
							continue;

						var tileSheet = tile.TileSheet;
						if (tileSheet == null)
							continue;

						Texture2D texture = GetTileSheetTexture(tileSheet);
						if (texture == null)
							continue;

						// Source rect in the tilesheet (16x16 per tile)
						const int srcTileSize = 16;
						int tilesPerRow = Math.Max(1, texture.Width / srcTileSize);
						int tileIndex = tile.TileIndex;
						int srcX = (tileIndex % tilesPerRow) * srcTileSize;
						int srcY = (tileIndex / tilesPerRow) * srcTileSize;
						Rectangle sourceRect = new Rectangle(srcX, srcY, srcTileSize, srcTileSize);

						// Destination rect in LOCAL viewport space.
						int localX = x * tileWorldSize - vp.X;
						int localY = y * tileWorldSize - vp.Y;
						Rectangle destRect = new Rectangle(localX, localY, tileWorldSize, tileWorldSize);

						b.Draw(texture, destRect, sourceRect, Color.White);
					}
				}
			}
        


        
	        private bool IsWarpEditedOrAdded(WarpPointData warp)
	        {
	            if (warp == null)
	                return false;

	            if (warp.IsAddedByMod)
	                return true;

	            Point trueOriginal = warp.TrueOriginalPosition != Point.Zero ? warp.TrueOriginalPosition : warp.OriginalPosition;
	            if (trueOriginal != warp.ModifiedPosition)
	                return true;

	            // Destination edits are only detectable once an original identity has been recorded.
	            bool hasOrigDest = !string.IsNullOrEmpty(warp.OriginalTargetMap) || warp.OriginalTargetPosition != Point.Zero;
	            if (hasOrigDest)
	            {
	                string origMap = warp.GetOriginalTargetMapFallback();
	                Point origPos = warp.GetOriginalTargetPosFallback();
	                if (!string.Equals(warp.TargetMap ?? "", origMap, StringComparison.OrdinalIgnoreCase) || warp.TargetPosition != origPos)
	                    return true;
	            }

	            return false;
	        }

			/// <summary>
			/// Fallback renderer for base map layers (Back/Buildings/Front) in the editor world draw pass.
			/// This draws tiles in LOCAL viewport space (world pixels relative to <see cref="Game1.viewport"/>),
			/// and relies on the sprite batch transform matrix to apply the editor zoom.
			/// </summary>


	            private void DrawWarpPoint(SpriteBatch b, WarpPointData warp, bool isSelected)
            {
                // Draw in LOCAL viewport space (GameLocation.draw() already localizes to Game1.viewport).
                // The world draw pass applies zoom via the SpriteBatch transform matrix, so sizes/positions here are unscaled.
                Vector2 center = WorldToLocal(warp.ModifiedPosition);

                bool isDoor = string.Equals(warp.WarpType, "Door", StringComparison.OrdinalIgnoreCase);

                Color fillColor;
                Color accentColor;
                if (isSelected)
                {
                    fillColor = Color.Yellow;
                    accentColor = Color.Yellow;
                }
                else if (IsWarpEditedOrAdded(warp))
                {
                    fillColor = Color.Green;
                    accentColor = Color.Orange;
                }
                else
                {
                    fillColor = Color.Green;
                    accentColor = Color.Blue;
                }

                // ---- Filled shape + outline ----
                if (isDoor)
                {
                    // Door warps: render as a round marker (match regular icon style).
                    float radius = 22f;
                    Rectangle fillRectOuter = new Rectangle((int)(center.X - radius - 2), (int)(center.Y - radius - 2), (int)((radius * 2f) + 4), (int)((radius * 2f) + 4));
                    Rectangle fillRect = new Rectangle((int)(center.X - radius), (int)(center.Y - radius), (int)(radius * 2f), (int)(radius * 2f));
                    b.Draw(Game1.staminaRect, fillRectOuter, Color.Black * 0.75f);
                    b.Draw(Game1.staminaRect, fillRect, fillColor * 0.70f);
                    int dotSize = 4;
                    int dotCount = 40;
                    for (int i = 0; i < dotCount; i++)
                    {
                        float angle = MathHelper.ToRadians(i * 360f / dotCount);
                        Vector2 pos = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);
                        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X - (dotSize / 2) - 1, (int)pos.Y - (dotSize / 2) - 1, dotSize + 2, dotSize + 2), Color.Black * 0.9f);
                        b.Draw(Game1.staminaRect, new Rectangle((int)pos.X - dotSize / 2, (int)pos.Y - dotSize / 2, dotSize, dotSize), accentColor);
                    }
                }
                else
                {
                    // Regular warp sources: circular marker.
                    float radius = 26f;

                    Rectangle fillRectOuter = new Rectangle(
                        (int)(center.X - radius - 2),
                        (int)(center.Y - radius - 2),
                        (int)((radius * 2f) + 4),
                        (int)((radius * 2f) + 4)
                    );
                    Rectangle fillRect = new Rectangle(
                        (int)(center.X - radius),
                        (int)(center.Y - radius),
                        (int)(radius * 2f),
                        (int)(radius * 2f)
                    );

                    // Black outline underlay + fill.
                    b.Draw(Game1.staminaRect, fillRectOuter, Color.Black * 0.75f);
                    b.Draw(Game1.staminaRect, fillRect, fillColor * 0.70f);

                    // Dotted circle outline: black underlay then colored.
                    int dotSize = 4;
                    int dotCount = 48;
                    for (int i = 0; i < dotCount; i++)
                    {
                        float angle = MathHelper.ToRadians(i * 360f / dotCount);
                        Vector2 pos = center + new Vector2((float)Math.Cos(angle) * radius, (float)Math.Sin(angle) * radius);

                        b.Draw(Game1.staminaRect,
                            new Rectangle((int)pos.X - (dotSize / 2) - 1, (int)pos.Y - (dotSize / 2) - 1, dotSize + 2, dotSize + 2),
                            Color.Black * 0.9f);

                        b.Draw(Game1.staminaRect,
                            new Rectangle((int)pos.X - dotSize / 2, (int)pos.Y - dotSize / 2, dotSize, dotSize),
                            accentColor);
                    }
                }

                // ---- Red line from TRUE original position to current position if moved ----
                Point trueOriginal = warp.TrueOriginalPosition != Point.Zero ? warp.TrueOriginalPosition : warp.OriginalPosition;
                if (trueOriginal != warp.ModifiedPosition)
                {
                    Vector2 origCenter = WorldToLocal(trueOriginal);

                    // Black outline under the red line.
                    DrawLine(b, origCenter, center, Color.Black * 0.75f, 6);
                    DrawLine(b, origCenter, center, Color.Red * 0.85f, 4);

                    // Small marker at original position with outline.
                    float origMarkerSize = 16f;
                    Rectangle origOuter = new Rectangle(
                        (int)(origCenter.X - origMarkerSize / 2f) - 2,
                        (int)(origCenter.Y - origMarkerSize / 2f) - 2,
                        (int)origMarkerSize + 4,
                        (int)origMarkerSize + 4
                    );
                    Rectangle origRect = new Rectangle(
                        (int)(origCenter.X - origMarkerSize / 2f),
                        (int)(origCenter.Y - origMarkerSize / 2f),
                        (int)origMarkerSize,
                        (int)origMarkerSize
                    );
                    b.Draw(Game1.staminaRect, origOuter, Color.Black * 0.75f);
                    b.Draw(Game1.staminaRect, origRect, Color.Red * 0.55f);
                }
            }


			private void DrawInboundDestinationMarker(SpriteBatch b, InboundDestinationMarker marker, bool isSelected)
	        {
            if (marker == null)
                return;

            // Refresh edited/added state dynamically so outline color updates immediately after edits.
            if (getExternalWarpEditedOrAdded != null)
                marker.IsEditedOrAdded = getExternalWarpEditedOrAdded.Invoke(marker.SourceMap ?? "", marker.SourceTile);


	            Vector2 center = WorldToLocal(marker.DestinationTile);

	            float half = 18f;
	            int thickness = isSelected ? 8 : 6;
            Color color;
            Color outline;
            if (isSelected)
            {
                color = Color.Yellow;
                outline = Color.Yellow;
            }
            else if (marker.IsEditedOrAdded)
            {
                color = Color.Green;
                outline = Color.Orange;
            }
            else
            {
                color = Color.Green;
                outline = Color.Blue;
            }

            // Black outline underlay
            DrawLine(b, center + new Vector2(-half, -half), center + new Vector2(half, half), Color.Black * 0.85f, thickness + 2);
            DrawLine(b, center + new Vector2(-half, half), center + new Vector2(half, -half), Color.Black * 0.85f, thickness + 2);

            // Colored X (outlined)
            DrawLine(b, center + new Vector2(-half, -half), center + new Vector2(half, half), outline, thickness);
            DrawLine(b, center + new Vector2(-half, half), center + new Vector2(half, -half), outline, thickness);

            DrawLine(b, center + new Vector2(-half, -half), center + new Vector2(half, half), color, Math.Max(2, thickness - 2));
            DrawLine(b, center + new Vector2(-half, half), center + new Vector2(half, -half), color, Math.Max(2, thickness - 2));
        
            // ---- Red line from original destination tile to current destination tile if moved ----
            if (marker.OriginalDestinationTile != Point.Zero && marker.OriginalDestinationTile != marker.DestinationTile)
            {
                Vector2 origCenter = WorldToLocal(marker.OriginalDestinationTile);
                DrawLine(b, origCenter, center, Color.Black * 0.75f, 6);
                DrawLine(b, origCenter, center, Color.Red * 0.85f, 4);

                float origMarkerSize = 14f;
                Rectangle origOuter = new Rectangle(
                    (int)(origCenter.X - origMarkerSize / 2f) - 2,
                    (int)(origCenter.Y - origMarkerSize / 2f) - 2,
                    (int)origMarkerSize + 4,
                    (int)origMarkerSize + 4
                );
                Rectangle origRect = new Rectangle(
                    (int)(origCenter.X - origMarkerSize / 2f),
                    (int)(origCenter.Y - origMarkerSize / 2f),
                    (int)origMarkerSize,
                    (int)origMarkerSize
                );
                b.Draw(Game1.staminaRect, origOuter, Color.Black * 0.75f);
                b.Draw(Game1.staminaRect, origRect, Color.Red * 0.55f);
            }
        }

        
        // Draw labels in screen space (called after transform batch ends)
        private void DrawWarpLabels(SpriteBatch b)
        {
            if (!showWarpLabels) return;

            // Determine hovered warp/marker for Hover tooltip mode.
            Point mouseWorld = ScreenToWorld(new Point(Game1.getMouseX(), Game1.getMouseY()));
            WarpPointData hoveredWarp = null;
            InboundDestinationMarker hoveredInbound = null;
            try
            {
                // Match against our drawn warp points (WarpPointData) rather than raw location warps.
                hoveredWarp = currentMapWarps.FirstOrDefault(w => (int)w.ModifiedPosition.X == mouseWorld.X && (int)w.ModifiedPosition.Y == mouseWorld.Y);
                hoveredInbound = GetInboundMarkerAtTile(mouseWorld);
            }
            catch { /* ignore hover probe errors */ }
            
            foreach (var warp in currentMapWarps)
            {
                bool drawTargetLabel = targetTooltipMode == TooltipMode.Show
                    || (targetTooltipMode == TooltipMode.Hover && hoveredWarp == warp);

                if (!drawTargetLabel)
                    continue;

                // Convert world position to screen position
                Vector2 screenPos = WorldToScreen(warp.ModifiedPosition);
                
                // Check if on screen
                if (screenPos.X < -100 || screenPos.X > Game1.uiViewport.Width + 100 ||
                    screenPos.Y < -100 || screenPos.Y > Game1.uiViewport.Height + 100)
                    continue;
                
                // Draw label above the warp point
                string label = warp.TargetMap;
                var font = Game1.tinyFont ?? Game1.smallFont;
                Vector2 labelSize = font.MeasureString(label);
                Vector2 labelPos = new Vector2(screenPos.X - labelSize.X / 2, screenPos.Y - 28 * zoomLevel - labelSize.Y);
                
                // Background for readability
                Rectangle labelBg = new Rectangle(
                    (int)(labelPos.X - 4),
                    (int)(labelPos.Y - 2),
                    (int)(labelSize.X + 8),
                    (int)(labelSize.Y + 4)
                );
                b.Draw(Game1.staminaRect, labelBg, Color.Black * 0.7f);
                
                // Text with shadow
                b.DrawString(font, label, labelPos + new Vector2(1, 1), Color.Black * 0.8f);
                b.DrawString(font, label, labelPos, Color.White);
            }
            // Also label inbound destination markers (X) with their source location, like GMCM-style clarity.
            foreach (var marker in inboundDestinationMarkers)
            {
                bool drawSourceLabel = sourceTooltipMode == TooltipMode.Show
                    || (sourceTooltipMode == TooltipMode.Hover && hoveredInbound == marker);

                if (!drawSourceLabel)
                    continue;

                if (marker == null)
                    continue;

                Vector2 screenPos = WorldToScreen(marker.DestinationTile);
                if (screenPos.X < -100 || screenPos.X > Game1.uiViewport.Width + 100 ||
                    screenPos.Y < -100 || screenPos.Y > Game1.uiViewport.Height + 100)
                    continue;

                string label = marker.SourceMap;
                var font = Game1.tinyFont ?? Game1.smallFont;
                Vector2 labelSize = font.MeasureString(label);
                Vector2 labelPos = new Vector2(screenPos.X - labelSize.X / 2, screenPos.Y - 28 * zoomLevel - labelSize.Y);

                Rectangle labelBg = new Rectangle(
                    (int)(labelPos.X - 4),
                    (int)(labelPos.Y - 2),
                    (int)(labelSize.X + 8),
                    (int)(labelSize.Y + 4)
                );
                b.Draw(Game1.staminaRect, labelBg, Color.Black * 0.7f);
                b.DrawString(font, label, labelPos + new Vector2(1, 1), Color.Black * 0.8f);
                b.DrawString(font, label, labelPos, Color.White);
            }

        }
        
        private void DrawLine(SpriteBatch b, Vector2 start, Vector2 end, Color color, int thickness = 2)
        {
            Vector2 edge = end - start;
            float angle = (float)Math.Atan2(edge.Y, edge.X);
            
            b.Draw(Game1.staminaRect,
                new Rectangle((int)start.X, (int)start.Y, (int)edge.Length(), thickness),
                null,
                color,
                angle,
                Vector2.Zero,
                SpriteEffects.None,
                0);
        }

        private static string TruncateToWidth(string text, SpriteFont font, float maxWidth)
        {
            if (string.IsNullOrEmpty(text))
                return "";
            if (font.MeasureString(text).X <= maxWidth)
                return text;

            const string ellipsis = "...";
            float ellW = font.MeasureString(ellipsis).X;
            if (ellW >= maxWidth)
                return "";

            int lo = 0;
            int hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                string candidate = text.Substring(0, mid) + ellipsis;
                if (font.MeasureString(candidate).X <= maxWidth)
                    lo = mid + 1;
                else
                    hi = mid;
            }

            int keep = Math.Max(0, lo - 1);
            return text.Substring(0, keep) + ellipsis;
        }

        private void RecalculateUiLayout()
        {
            int rowHeight = WarpMasterFrameworkGmcmUi.RowHeight;

            if (mapViewerMode)
            {
                int viewerTitleHeight = 70;
                int viewerPanelWidth = Math.Min(760, Math.Max(520, Game1.uiViewport.Width - 96));
                int viewerPanelHeight = showMasterListDropdown
                    ? viewerTitleHeight + 8 + 18 + rowHeight + 24
                    : viewerTitleHeight;

                instructionsPanelRect = new Rectangle(24, 24, viewerPanelWidth, viewerPanelHeight);

                if (showMasterListDropdown)
                {
                    int viewerContentX = instructionsPanelRect.X + 24;
                    int viewerTableY = instructionsPanelRect.Y + viewerTitleHeight + 8 + 18;
                    int viewerContentW = instructionsPanelRect.Width - 48;
                    mapSelectorRect = new Rectangle(viewerContentX + 120, viewerTableY + 6, viewerContentW - 120, rowHeight - 12);
                }
                else
                {
                    mapSelectorRect = Rectangle.Empty;
                }

                int viewerVisible = MapDropdownMaxVisibleItems;
                if (cachedLocationOptions != null && cachedLocationOptions.Count > 0)
                    viewerVisible = Math.Min(MapDropdownMaxVisibleItems, cachedLocationOptions.Count);
                viewerVisible = Math.Max(1, viewerVisible);

                mapDropdownListRect = showMasterListDropdown
                    ? new Rectangle(mapSelectorRect.X, mapSelectorRect.Bottom + 4, mapSelectorRect.Width, (viewerVisible * WarpMasterFrameworkGmcmUi.DropdownRowHeight) + 20)
                    : Rectangle.Empty;
                return;
            }
            int titleHeight = 70;
            int titleGap = 8;
            int paddingY = 36;

            // One map row + one framework row + compact controls + legend/footer rows.
            int rowCount = 1 + 1 + 6 + 4;
            int panelWidth = Math.Min(760, Math.Max(680, Game1.uiViewport.Width - 96));
            int panelHeight = titleHeight + titleGap + paddingY + rowCount * rowHeight + 24;

            Vector2 panelPos = new Vector2(24, 24);
            instructionsPanelRect = new Rectangle((int)panelPos.X, (int)panelPos.Y, panelWidth, panelHeight);

            int contentX = instructionsPanelRect.X + 24;
            int tableY = instructionsPanelRect.Y + titleHeight + titleGap + 18;
            int contentW = instructionsPanelRect.Width - 48;

            mapSelectorRect = new Rectangle(contentX + contentW - 520, tableY + 6, 520, rowHeight - 12);

            int visible = MapDropdownMaxVisibleItems;
            if (cachedLocationOptions != null && cachedLocationOptions.Count > 0)
                visible = Math.Min(MapDropdownMaxVisibleItems, cachedLocationOptions.Count);
            visible = Math.Max(1, visible);

            mapDropdownListRect = new Rectangle(
                mapSelectorRect.X,
                mapSelectorRect.Bottom + 4,
                mapSelectorRect.Width,
                (visible * WarpMasterFrameworkGmcmUi.DropdownRowHeight) + 20
            );
        }

        private bool IsPointInsideUi(Point screenPos)
        {
            if (instructionsPanelRect.Contains(screenPos))
                return true;

            if (mapDropdownOpen && mapDropdownListRect.Contains(screenPos))
                return true;

            return false;
        }

        private void ToggleMapDropdown()
        {
            if (mapDropdownOpen)
                CloseMapDropdown(playSound: true);
            else
                OpenMapDropdown();
        }

        private void OpenMapDropdown()
        {
            mapDropdownOpen = true;
            Game1.playSound("smallSelect");

            var options = GetAvailableLocationOptions();
            if (options == null || options.Count == 0)
            {
                mapDropdownSelectedIndex = 0;
                mapDropdownScrollIndex = 0;
                return;
            }

            int idx = options.FindIndex(o => string.Equals(o.Name, mapName, StringComparison.OrdinalIgnoreCase));
            mapDropdownSelectedIndex = idx >= 0 ? idx : 0;

            var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                mapSelectorRect,
                options.Count,
                WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                MapDropdownMaxVisibleItems
            );

            mapDropdownScrollIndex = WarpMasterFrameworkGmcmUi.CenterOnSelection(mapDropdownSelectedIndex, options.Count, layout.VisibleItems);
        }

        private void CloseMapDropdown(bool playSound)
        {
            if (!mapDropdownOpen)
                return;

            mapDropdownOpen = false;
            if (playSound)
                Game1.playSound("bigDeSelect");
        }

        private void ScrollMapDropdown(int delta)
        {
            var options = GetAvailableLocationOptions();
            if (!mapDropdownOpen || options == null || options.Count == 0)
                return;

            var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                mapSelectorRect,
                options.Count,
                WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                MapDropdownMaxVisibleItems
            );

            mapDropdownScrollIndex = WarpMasterFrameworkGmcmUi.ScrollOffset(mapDropdownScrollIndex, delta, options.Count, layout.VisibleItems);
        }

        private void TrySelectMapFromDropdown(Point mouse)
        {
            var options = GetAvailableLocationOptions();
            if (options == null || options.Count == 0)
            {
                CloseMapDropdown(playSound: true);
                return;
            }

            var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                mapSelectorRect,
                options.Count,
                WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                MapDropdownMaxVisibleItems
            );

            int idx = WarpMasterFrameworkGmcmUi.GetIndexAtPoint(mouse, layout, mapDropdownScrollIndex, WarpMasterFrameworkGmcmUi.DropdownRowHeight);
            if (idx < 0 || idx >= options.Count)
                return;

            mapDropdownSelectedIndex = idx;
            string selectedName = options[idx].Name ?? "";
            CloseMapDropdown(playSound: true);

            if (string.Equals(selectedName, mapName, StringComparison.OrdinalIgnoreCase))
                return;

            NavigateToLocationFromDropdown(selectedName, mouse);
        }


        private void DrawMapDropdownList(SpriteBatch b)
        {
            var options = GetAvailableLocationOptions() ?? new List<LocationOption>();
            var layout = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
                mapSelectorRect,
                options.Count,
                WarpMasterFrameworkGmcmUi.DropdownRowHeight,
                MapDropdownMaxVisibleItems
            );

            mapDropdownListRect = layout.ListRect;
            WarpMasterFrameworkGmcmUi.DrawListPanel(b, layout.ListRect);

            if (options.Count > layout.VisibleItems)
                WarpMasterFrameworkGmcmUi.DrawScrollbar(b, layout.ScrollbarRect, options.Count, layout.VisibleItems, mapDropdownScrollIndex);

            Point mouse = new Point(Game1.getMouseX(), Game1.getMouseY());
            mapDropdownScrollIndex = Math.Clamp(mapDropdownScrollIndex, 0, layout.MaxScroll);

            for (int i = 0; i < layout.VisibleItems; i++)
            {
                int idx = mapDropdownScrollIndex + i;
                if (idx < 0 || idx >= options.Count)
                    break;

                Rectangle rowRect = new Rectangle(layout.ItemsRect.X, layout.ItemsRect.Y + i * WarpMasterFrameworkGmcmUi.DropdownRowHeight, layout.ItemsRect.Width, WarpMasterFrameworkGmcmUi.DropdownRowHeight);
                if (rowRect.Contains(mouse))
                    WarpMasterFrameworkGmcmUi.DrawHoverOverlay(b, rowRect);

                string name = options[idx].DisplayName ?? options[idx].Name ?? "";
                WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, name, new Vector2(rowRect.X + 10, rowRect.Y + (WarpMasterFrameworkGmcmUi.DropdownRowHeight - Game1.smallFont.LineSpacing) / 2f), rowRect.Width - 20, uiTextColor);
            }
        }


        private void DrawMapViewerUI(SpriteBatch b)
        {
            Rectangle titleBox = new Rectangle(instructionsPanelRect.X, instructionsPanelRect.Y, instructionsPanelRect.Width, 70);
            WarpMasterFrameworkGmcmUi.DrawTitleBox(b, titleBox, "Warp Master Framework");

            if (showMasterListDropdown)
            {
                Rectangle tableBox = new Rectangle(instructionsPanelRect.X, titleBox.Bottom + 8, instructionsPanelRect.Width, instructionsPanelRect.Height - titleBox.Height - 8);
                WarpMasterFrameworkGmcmUi.DrawMenuBox(b, tableBox);

                string mapDisplay = mapName ?? "";
                var opts = GetAvailableLocationOptions();
                if (opts != null && opts.Count > 0)
                {
                    int idx = opts.FindIndex(o => string.Equals(o.Name, mapName, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                        mapDisplay = opts[idx].DisplayName ?? mapDisplay;
                }

                int rowHeight = WarpMasterFrameworkGmcmUi.RowHeight;
                int contentX = instructionsPanelRect.X + 24;
                int contentW = instructionsPanelRect.Width - 48;
                Rectangle row = new Rectangle(contentX, tableBox.Y + 18, contentW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Map", new Rectangle(row.X, row.Y, 110, row.Height), uiTextColor);
                WarpMasterFrameworkGmcmUi.DrawDropdownField(b, mapSelectorRect, mapDisplay, Game1.smallFont, uiTextColor, mapDropdownOpen);

                if (mapDropdownOpen)
                    DrawMapDropdownList(b);
            }

            string modeLabel = localMapRenderMode == LocalMapRenderMode.FullScene ? "Full Scene" : "Terrain Only";
            string mapInfo = $"{mapName ?? ""}    Zoom: {zoomLevel:F2}x    {modeLabel}";
            Vector2 mapInfoSize = Game1.smallFont.MeasureString(mapInfo);
            Rectangle mapInfoBg = new Rectangle(
                Game1.uiViewport.Width - (int)mapInfoSize.X - 72,
                Game1.uiViewport.Height - 76,
                (int)mapInfoSize.X + 48,
                52
            );
            WarpMasterFrameworkGmcmUi.DrawMenuBox(b, mapInfoBg);
            WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, mapInfo, new Vector2(mapInfoBg.X + 24, mapInfoBg.Y + 16), mapInfoBg.Width - 48, uiTextColor);
        }

        private void DrawUI(SpriteBatch b)
        {
            _ = titleInPosition;
            RecalculateUiLayout();

            if (mapViewerMode)
            {
                DrawMapViewerUI(b);
                return;
            }

            int padding = 24;
            int rowHeight = WarpMasterFrameworkGmcmUi.RowHeight;
            int contentX = instructionsPanelRect.X + padding;
            int contentW = instructionsPanelRect.Width - padding * 2;

            Rectangle titleBox = new Rectangle(instructionsPanelRect.X, instructionsPanelRect.Y, instructionsPanelRect.Width, 70);
            Rectangle tableBox = new Rectangle(instructionsPanelRect.X, titleBox.Bottom + 8, instructionsPanelRect.Width, instructionsPanelRect.Height - titleBox.Height - 8);

            WarpMasterFrameworkGmcmUi.DrawTitleBox(b, titleBox, "Warp Master Framework");
            WarpMasterFrameworkGmcmUi.DrawMenuBox(b, tableBox);

            int rowY = tableBox.Y + 18;

            string mapDisplay = mapName ?? "";
            var opts = GetAvailableLocationOptions();
            if (opts != null && opts.Count > 0)
            {
                int idx = opts.FindIndex(o => string.Equals(o.Name, mapName, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                    mapDisplay = opts[idx].DisplayName ?? mapDisplay;
            }

            Rectangle row = new Rectangle(contentX, rowY, contentW, rowHeight);
            WarpMasterFrameworkGmcmUi.DrawLabel(b, "Map", new Rectangle(row.X, row.Y, 140, row.Height), uiTextColor);
            mapSelectorRect = new Rectangle(row.Right - 520, row.Y + 6, 520, row.Height - 12);
            WarpMasterFrameworkGmcmUi.DrawDropdownField(b, mapSelectorRect, mapDisplay, Game1.smallFont, uiTextColor, mapDropdownOpen);
            rowY += rowHeight;

            row = new Rectangle(contentX, rowY, contentW, rowHeight);
            WarpMasterFrameworkGmcmUi.DrawLabel(b, "Framework", new Rectangle(row.X, row.Y, 140, row.Height), uiTextColor);
            exportButtonRect = new Rectangle(row.Right - 160, row.Y + 6, 160, row.Height - 12);
            WarpMasterFrameworkGmcmUi.DrawFlatButton(b, exportButtonRect, "Export", uiTextColor);
            rowY += rowHeight;

            string historyInfo = locationHistory.Count > 0 ? $"History: {locationHistory.Count}" : "History: 0";
            string[] controlRows =
            {
                $"{historyInfo}    Zoom: {zoomLevel:F2}x",
                "Right Drag: Pan    Wheel/+/-: Zoom",
                "Shift+Click: Follow warp/source history",
                "Drag marker: Move source or target",
                "A+Click: Add    E+Click: Edit",
                "D+Click: Delete    R: Root map"
            };

            foreach (string text in controlRows)
            {
                row = new Rectangle(contentX, rowY, contentW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, text, new Vector2(row.X, row.Y + 14), row.Width - 10, uiTextColor);
                rowY += rowHeight;
            }

            string[] colorRows =
            {
                "Legend: Blue default, Orange custom",
                "Red line: Original position",
                "ESC or F9: Close",
                "Export: Writes framework files"
            };

            foreach (string text in colorRows)
            {
                row = new Rectangle(contentX, rowY, contentW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, text, new Vector2(row.X, row.Y + 14), row.Width - 10, uiTextColor);
                rowY += rowHeight;
            }

            if (mapDropdownOpen)
                DrawMapDropdownList(b);

            string mapInfo = $"Total Warps: {currentMapWarps.Count}    Edited/Added: {currentMapWarps.Count(w => IsWarpEditedOrAdded(w))}    Zoom: {zoomLevel:F2}x";
            Vector2 mapInfoSize = Game1.smallFont.MeasureString(mapInfo);
            Rectangle mapInfoBg = new Rectangle(
                Game1.uiViewport.Width - (int)mapInfoSize.X - 72,
                Game1.uiViewport.Height - 76,
                (int)mapInfoSize.X + 48,
                52
            );
            WarpMasterFrameworkGmcmUi.DrawMenuBox(b, mapInfoBg);
            WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, mapInfo, new Vector2(mapInfoBg.X + 24, mapInfoBg.Y + 16), mapInfoBg.Width - 48, uiTextColor);
        }


        protected override void cleanupBeforeExit()
        {
            DisposeFullSceneCaptureTarget();
            Game1.game1.IsMouseVisible = previousMouseVisible;
            base.cleanupBeforeExit();
        }


        private void SwitchViewedLocationInPlace(string locationName, Point? centerOnTile = null, Point? anchorScreen = null, bool pushHistory = true)
        {
            if (string.IsNullOrWhiteSpace(locationName))
            {
                SafeLog("[Map Switch] Cannot switch: map name is empty.", LogLevel.Warn);
                Game1.playSound("cancel");
                return;
            }

            if (string.Equals(locationName, this.mapName, StringComparison.OrdinalIgnoreCase))
            {
                if (centerOnTile.HasValue)
                    CenterCameraOnTile(centerOnTile.Value, anchorScreen);
                else
                    ResetCamera();

                Game1.playSound("smallSelect");
                return;
            }

            GameLocation location = Game1.getLocationFromName(locationName);
            if (location == null)
            {
                SafeLog($"[Map Switch] Cannot switch: location '{locationName}' was not found.", LogLevel.Warn);
                Game1.playSound("cancel");
                return;
            }

            // Save/apply current editor state before replacing the viewed map data.
            try
            {
                onImmediateSaveApply?.Invoke();
            }
            catch (Exception ex)
            {
                SafeLog($"[Map Switch] Failed to apply current map state before switching: {ex}", LogLevel.Error);
            }

            if (pushHistory && !string.IsNullOrWhiteSpace(this.mapName) && !string.Equals(this.mapName, locationName, StringComparison.OrdinalIgnoreCase))
                locationHistory.Push(this.mapName);

            this.mapName = locationName;
            this.viewedLocation = location;
            this.currentMapWarps = inboundDetector?.DetectWarpsForLocation(location) ?? new List<WarpPointData>();
            this.selectedWarp = null;
            this.isDragging = false;
            this.isDraggingInboundMarker = false;
            this.selectedInboundMarker = null;
            this.selectedWarpDragStartSource = Point.Zero;
            this.mapDropdownOpen = false;
            this.mapDropdownScrollDragging = false;
            this.inboundCacheByDestMap.Clear();
            this.inboundDestinationMarkers.Clear();
            this.inboundMarkersDirty = true;
            this._hasCommittedPendingChanges = false;

            RecalculateUiLayout();

            if (centerOnTile.HasValue)
                CenterCameraOnTile(centerOnTile.Value, anchorScreen);
            else
                ResetCamera();

            SafeLog($"[Map Switch] Switched editor view to {locationName}. Warps={currentMapWarps.Count}", LogLevel.Debug);
            Game1.playSound("smallSelect");
        }

        private void NavigateToLocationFromDropdown(string locationName, Point mouse)
        {
            SwitchViewedLocationInPlace(locationName, null, mouse, pushHistory: true);
        }

	        private void NavigateFromEditPopup(string locationName, Point tile)
        {
            if (string.IsNullOrWhiteSpace(locationName))
            {
                SafeLog("[Edit Warp] Cannot navigate: map name is empty.", LogLevel.Warn);
                Game1.playSound("cancel");
                return;
            }

            // Keep the edit popup open while changing the viewed map. This lets users jump
            // to the source/target map, click Pick, and fill coordinates without reopening the popup.
            SwitchViewedLocationInPlace(locationName, tile, null, pushHistory: true);
        }

        private void ApplyCurrentEditorEditsImmediately(string reason)
        {
            try
            {
                inboundCacheByDestMap.Clear();
                inboundMarkersDirty = true;
                onImmediateSaveApply?.Invoke();
                SafeLog($"[Realtime Save] Applied editor changes immediately: {reason}", LogLevel.Debug);
            }
            catch (Exception ex)
            {
                SafeLog($"[Realtime Save] Immediate apply failed after {reason}: {ex}", LogLevel.Warn);
            }
        }

        private void OpenDestinationEditPopup(WarpPointData warp)
        {
            if (warp == null)
                return;

            // Seed identity fields before editing (backwards compatibility).
            if (string.IsNullOrEmpty(warp.OriginalTargetMap))
                warp.OriginalTargetMap = warp.TargetMap ?? "";

            if (warp.OriginalTargetPosition == Point.Zero)
                warp.OriginalTargetPosition = warp.TargetPosition;

			destinationEditPopup = new WarpDestinationEditPopup(this, warp, monitor, GetAvailableLocationOptions());
			SafeLog($"[Edit Warp] Opened for {warp.MapName} ({warp.ModifiedPosition.X},{warp.ModifiedPosition.Y}) -> {warp.TargetMap} ({warp.TargetPosition.X},{warp.TargetPosition.Y})", LogLevel.Debug);
        }

		private void OpenDestinationEditPopupForInbound(InboundDestinationMarker inbound)
		{
			if (inbound == null)
				return;

			string destMap = this.mapName ?? "";
			Point destTile = inbound.DestinationTile;

			destinationEditPopup = new WarpDestinationEditPopup(
				this,
				inbound.SourceMap ?? "",
				inbound.SourceTile,
				destMap,
				destTile,
				monitor,
				GetAvailableLocationOptions());

			SafeLog($"[Edit Warp] Opened from destination marker: {inbound.SourceMap} ({inbound.SourceTile.X},{inbound.SourceTile.Y}) -> {destMap} ({destTile.X},{destTile.Y})", LogLevel.Debug);
		}

		private HashSet<string> GetVanillaLocationNameSet()
		{
			if (cachedVanillaLocationNames != null && cachedVanillaLocationNames.Count > 0)
				return cachedVanillaLocationNames;

			cachedVanillaLocationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			try
			{
				string jsonPath = Path.Combine(Constants.ContentPath, "Data", "Locations.json");
				if (File.Exists(jsonPath))
				{
					using var doc = JsonDocument.Parse(File.ReadAllText(jsonPath));
					JsonElement root = doc.RootElement;

					// Some data assets wrap entries in an "Entries" object; handle both.
					if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("Entries", out JsonElement entries) && entries.ValueKind == JsonValueKind.Object)
						root = entries;

					if (root.ValueKind == JsonValueKind.Object)
					{
						foreach (var prop in root.EnumerateObject())
						{
							if (!string.IsNullOrEmpty(prop.Name))
								cachedVanillaLocationNames.Add(prop.Name);
						}
					}
				}
			}
            catch (Exception ex)
            {
                SafeLog($"Error reading vanilla location list: {ex}", LogLevel.Trace);
            }

			return cachedVanillaLocationNames;
		}

		private List<LocationOption> GetAvailableLocationOptions()
		{
			if (cachedLocationOptions != null && cachedLocationOptions.Count > 0)
				return cachedLocationOptions;

			var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			Dictionary<string, LocationData> locationData = null;

			try
			{
				// Loaded locations (includes modded locations that are currently loaded).
				foreach (var loc in new[] { Game1.getLocationFromName(this.mapName) ?? Game1.currentLocation })
				{
					if (!string.IsNullOrEmpty(loc?.NameOrUniqueName))
						names.Add(loc.NameOrUniqueName);
				}

				// Location definitions (includes locations not currently loaded).
				try
				{
					locationData = Game1.content.Load<Dictionary<string, LocationData>>("Data/Locations");
					foreach (var key in locationData.Keys)
					{
						if (!string.IsNullOrEmpty(key))
							names.Add(key);
					}
				}
				catch
				{
					// Ignore (some edge cases / early timing).
				}
			}
            catch (Exception ex)
            {
                SafeLog($"Error collecting location options: {ex}", LogLevel.Trace);
            }

			HashSet<string> vanillaNames = GetVanillaLocationNameSet();

			var options = new List<LocationOption>(names.Count);
			foreach (string name in names)
			{
				LocationKind kind = GetLocationKind(name, locationData);
				LocationSourceKind sourceKind = (vanillaNames != null && vanillaNames.Contains(name))
					? LocationSourceKind.Vanilla
					: LocationSourceKind.Mod;

				options.Add(new LocationOption(name, kind, sourceKind));
			}

			// Sort: alphabetical by name, but push locations starting with "Custom" to the bottom.
			static bool IsCustom(string n) => !string.IsNullOrEmpty(n) && n.StartsWith("Custom", StringComparison.OrdinalIgnoreCase);

			cachedLocationOptions = options
				.OrderBy(o => IsCustom(o.Name) ? 1 : 0)
				.ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
				.ToList();
return cachedLocationOptions;
		}


private static LocationKind GetLocationKind(string locationName, Dictionary<string, LocationData> data)
{
    if (string.IsNullOrEmpty(locationName))
        return LocationKind.Unknown;

    try
    {
        // Prefer loaded location classification when available.
        GameLocation loc = Game1.getLocationFromName(locationName);
        if (loc != null && TryReadBoolMember(loc, new[] { "IsOutdoors", "isOutdoors" }, out bool isOutdoors))
            return isOutdoors ? LocationKind.Outdoor : LocationKind.Indoor;

        // Fall back to Data/Locations classification (reflection to avoid hard dependency on field/property names).
        if (data != null && data.TryGetValue(locationName, out LocationData locData) && locData != null)
        {
            if (TryReadBoolMember(locData, new[] { "IsOutdoors", "Outdoors", "IsOutdoor", "Outdoor", "outdoors", "outdoor" }, out bool isOutdoors2))
                return isOutdoors2 ? LocationKind.Outdoor : LocationKind.Indoor;
        }
    }
    catch
    {
        // Ignore; treat as unknown.
    }

    return LocationKind.Unknown;
}

private static bool TryReadBoolMember(object obj, string[] memberNames, out bool value)
{
    value = false;
    if (obj == null || memberNames == null)
        return false;

    Type t = obj.GetType();

    foreach (string name in memberNames)
    {
        try
        {
            var prop = t.GetProperty(name);
            if (prop != null && prop.PropertyType == typeof(bool))
            {
                value = (bool)prop.GetValue(obj);
                return true;
            }

            var field = t.GetField(name);
            if (field != null && field.FieldType == typeof(bool))
            {
                value = (bool)field.GetValue(obj);
                return true;
            }
        }
        catch
        {
            // Ignore and continue.
        }
    }

    return false;
}

/// <summary>
/// Simple modal popup for editing a warp's destination map + tile.
/// This keeps WarpEditorMenu as the active menu (ModEntry relies on that) and draws/handles input inside this menu.
/// </summary>
        /// For editing a warp's destination map + tile.
        /// This keeps WarpEditorMenu as the active menu (ModEntry relies on that) and draws/handles input inside this menu.
        /// </summary>
        
		/// <summary>
		/// Modal popup for editing a warp destination.
		/// Opens with: hold E + Left Click on a warp tile.
		/// </summary>
		private class WarpDestinationEditPopup
		{
			private const int HeaderHeight = 34;

			private readonly WarpEditorMenu parent;
			private readonly WarpPointData warp; // optional (null when editing via destination marker)
			private readonly string sourceMapName;
			private readonly Point sourceTile;
			private readonly IMonitor monitor;


			private readonly List<LocationOption> allOptions;
			private List<LocationOption> displayedOptions;

			private Rectangle bounds;
				// Warp source fields
				private Rectangle sourceXFieldRect;
				private Rectangle sourceYFieldRect;
				private TextBox sourceXBox;
				private TextBox sourceYBox;

			private Rectangle mapFieldRect;
			private Rectangle mapDropdownButtonRect;
			private Rectangle xFieldRect;
			private Rectangle yFieldRect;
			private Rectangle okRect;
			private Rectangle cancelRect;
            private Rectangle sourceGoRect;
            private Rectangle sourcePickRect;
            private Rectangle targetGoRect;
            private Rectangle targetPickRect;
			private TextBox xBox;
			private TextBox yBox;

            private enum CoordinatePickMode
            {
                None,
                Source,
                Target
            }

            private CoordinatePickMode coordinatePickMode = CoordinatePickMode.None;
			private bool dropdownOpen;
			private int scrollIndex;

			private string selectedName = "";
			private int selectedIndex;
// Scrollbar dragging (for the main location list)
			private bool isDraggingScrollbar;
			private int scrollbarDragOffsetY;

            private bool isDraggingPopup;
            private Point popupDragOffset;

			public WarpDestinationEditPopup(WarpEditorMenu parent, WarpPointData warp, IMonitor monitor, List<LocationOption> locationOptions)
				: this(
					parent,
					warp,
					(!string.IsNullOrEmpty(warp?.MapName) ? warp.MapName : (parent?.mapName ?? "")),
					warp?.ModifiedPosition ?? Point.Zero,
					warp?.TargetMap ?? "",
					warp?.TargetPosition ?? Point.Zero,
					monitor,
					locationOptions)
			{
			}

			public WarpDestinationEditPopup(WarpEditorMenu parent, string sourceMapName, Point sourceTile, string targetMapName, Point targetTile, IMonitor monitor, List<LocationOption> locationOptions)
				: this(parent, null, sourceMapName, sourceTile, targetMapName, targetTile, monitor, locationOptions)
			{
			}

			private WarpDestinationEditPopup(WarpEditorMenu parent, WarpPointData warp, string sourceMapName, Point sourceTile, string targetMapName, Point targetTile, IMonitor monitor, List<LocationOption> locationOptions)
			{
				this.parent = parent;
				this.warp = warp;
				this.sourceMapName = sourceMapName ?? "";
				this.sourceTile = sourceTile;
				this.monitor = monitor;
				this.allOptions = locationOptions ?? new List<LocationOption>();

				
int width = 760;
int height = 430;

int x = Game1.uiViewport.Width / 2 - width / 2;
int y = Game1.uiViewport.Height / 2 - height / 2;
bounds = new Rectangle(x, y, width, height);

int bodyX = bounds.X + 28;
int bodyY = bounds.Y + 70 + 8 + 14;
int bodyW = bounds.Width - 56;
int rowHeight = WarpMasterFrameworkGmcmUi.RowHeight;
int fieldH = Math.Max(40, WarpMasterFrameworkGmcmUi.GetDropdownButtonHeight());
int fieldYPad = Math.Max(0, (rowHeight - fieldH) / 2);

int fieldX = bodyX + 116;
int fieldW = 92;
int yLabelX = fieldX + fieldW + 18;
int yFieldX = yLabelX + 28;
int actionX = yFieldX + fieldW + 18;
int actionButtonW = 92;
int actionGap = 8;

int sourceRowY = bodyY;
sourceXFieldRect = new Rectangle(fieldX, sourceRowY + fieldYPad, fieldW, fieldH);
sourceYFieldRect = new Rectangle(yFieldX, sourceRowY + fieldYPad, fieldW, fieldH);
sourceGoRect = new Rectangle(actionX, sourceRowY + fieldYPad, actionButtonW, fieldH);
sourcePickRect = new Rectangle(sourceGoRect.Right + actionGap, sourceRowY + fieldYPad, actionButtonW, fieldH);

int targetMapRowY = sourceRowY + rowHeight;
mapFieldRect = new Rectangle(fieldX, targetMapRowY + fieldYPad, actionX + actionButtonW * 2 + actionGap - fieldX, fieldH);
mapDropdownButtonRect = mapFieldRect;

int targetTileRowY = targetMapRowY + rowHeight;
xFieldRect = new Rectangle(fieldX, targetTileRowY + fieldYPad, fieldW, fieldH);
yFieldRect = new Rectangle(yFieldX, targetTileRowY + fieldYPad, fieldW, fieldH);
targetGoRect = new Rectangle(actionX, targetTileRowY + fieldYPad, actionButtonW, fieldH);
targetPickRect = new Rectangle(targetGoRect.Right + actionGap, targetTileRowY + fieldYPad, actionButtonW, fieldH);

int buttonY = bounds.Y + bounds.Height - 54;
cancelRect = GetVanillaButtonRect(bounds.X + bounds.Width - 24, buttonY, "Cancel");
okRect = GetVanillaButtonRect(cancelRect.X - 12, buttonY, "OK");
				
Texture2D textBoxTexture = Game1.content.Load<Texture2D>("LooseSprites\\textBox");

sourceXBox = new TextBox(textBoxTexture, null, Game1.smallFont, parent.uiTextColor)
{
    X = sourceXFieldRect.X,
    Y = sourceXFieldRect.Y,
    Width = sourceXFieldRect.Width,
    Text = sourceTile.X.ToString()
};

sourceYBox = new TextBox(textBoxTexture, null, Game1.smallFont, parent.uiTextColor)
{
    X = sourceYFieldRect.X,
    Y = sourceYFieldRect.Y,
    Width = sourceYFieldRect.Width,
    Text = sourceTile.Y.ToString()
};

xBox = new TextBox(textBoxTexture, null, Game1.smallFont, parent.uiTextColor)
				{
					X = xFieldRect.X,
					Y = xFieldRect.Y,
					Width = xFieldRect.Width,
					Text = targetTile.X.ToString()
				};

				yBox = new TextBox(textBoxTexture, null, Game1.smallFont, parent.uiTextColor)
				{
					X = yFieldRect.X,
					Y = yFieldRect.Y,
					Width = yFieldRect.Width,
					Text = targetTile.Y.ToString()
				};

				selectedName = targetMapName ?? "";

				RebuildDisplayedOptions(resetScroll: true);
			}

            public bool IsPickingCoordinate => coordinatePickMode != CoordinatePickMode.None;

            public bool ContainsScreenPoint(Point point)
            {
                if (bounds.Contains(point))
                    return true;

                if (dropdownOpen)
                    return GetDropdownLayout().ListRect.Contains(point);

                return false;
            }

            public void ApplyMapClick(Point tile)
            {
                if (coordinatePickMode == CoordinatePickMode.Source)
                {
                    sourceXBox.Text = tile.X.ToString();
                    sourceYBox.Text = tile.Y.ToString();
                }
                else if (coordinatePickMode == CoordinatePickMode.Target)
                {
                    xBox.Text = tile.X.ToString();
                    yBox.Text = tile.Y.ToString();
                }

                coordinatePickMode = CoordinatePickMode.None;
                Game1.playSound("smallSelect");
            }

            private void BeginCoordinatePick(CoordinatePickMode mode)
            {
                coordinatePickMode = mode;
                dropdownOpen = false;
                isDraggingScrollbar = false;
                ClearTextBoxFocus();
                Game1.playSound("smallSelect");
            }

            private void ClearTextBoxFocus()
            {
                sourceXBox.Selected = false;
                sourceYBox.Selected = false;
                xBox.Selected = false;
                yBox.Selected = false;

                if (Game1.keyboardDispatcher.Subscriber == sourceXBox
                    || Game1.keyboardDispatcher.Subscriber == sourceYBox
                    || Game1.keyboardDispatcher.Subscriber == xBox
                    || Game1.keyboardDispatcher.Subscriber == yBox)
                {
                    Game1.keyboardDispatcher.Subscriber = null;
                }
            }

            private Point GetSourceTileFromBoxes()
            {
                int x = sourceTile.X;
                int y = sourceTile.Y;
                _ = int.TryParse(sourceXBox.Text, out x);
                _ = int.TryParse(sourceYBox.Text, out y);
                return new Point(x, y);
            }

            private Point GetTargetTileFromBoxes()
            {
                int x = warp?.TargetPosition.X ?? 0;
                int y = warp?.TargetPosition.Y ?? 0;
                _ = int.TryParse(xBox.Text, out x);
                _ = int.TryParse(yBox.Text, out y);
                return new Point(x, y);
            }

            private Rectangle GetTitleDragRect()
            {
                return new Rectangle(bounds.X, bounds.Y, bounds.Width, 70);
            }

            private void MovePopupTo(int x, int y)
            {
                int clampedX = Math.Clamp(x, -bounds.Width + 120, Game1.uiViewport.Width - 120);
                int clampedY = Math.Clamp(y, 0, Game1.uiViewport.Height - 80);

                int dx = clampedX - bounds.X;
                int dy = clampedY - bounds.Y;
                if (dx == 0 && dy == 0)
                    return;

                OffsetPopup(dx, dy);
            }

            private void OffsetPopup(int dx, int dy)
            {
                bounds.Offset(dx, dy);
                sourceXFieldRect.Offset(dx, dy);
                sourceYFieldRect.Offset(dx, dy);
                mapFieldRect.Offset(dx, dy);
                mapDropdownButtonRect.Offset(dx, dy);
                xFieldRect.Offset(dx, dy);
                yFieldRect.Offset(dx, dy);
                sourceGoRect.Offset(dx, dy);
                sourcePickRect.Offset(dx, dy);
                targetGoRect.Offset(dx, dy);
                targetPickRect.Offset(dx, dy);
                okRect.Offset(dx, dy);
                cancelRect.Offset(dx, dy);

                sourceXBox.X += dx;
                sourceXBox.Y += dy;
                sourceYBox.X += dx;
                sourceYBox.Y += dy;
                xBox.X += dx;
                xBox.Y += dy;
                yBox.X += dx;
                yBox.Y += dy;
            }

			public void HandleInputFromSMAPI(IInputHelper input)
			{
				// No-op: keyboard input is handled by Game1.keyboardDispatcher via the TextBox subscribers.
				// Mouse wheel scrolling is handled via WarpEditorMenu.HandleScrollWheel -> OnMouseWheel.
			}

			public void OnMouseWheel(int delta)
			{
				if (!dropdownOpen)
					return;

				var layout = GetDropdownLayout();
				scrollIndex = WarpMasterFrameworkGmcmUi.ScrollOffset(scrollIndex, delta, layout.TotalItems, layout.VisibleItems);
			}

			public void ReceiveLeftClickHeld(int x, int y)
			{
                if (isDraggingPopup)
                {
                    MovePopupTo(x - popupDragOffset.X, y - popupDragOffset.Y);
                    return;
                }

				if (!dropdownOpen || !isDraggingScrollbar)
					return;

				var layout = GetDropdownLayout();
				scrollIndex = WarpMasterFrameworkGmcmUi.UpdateScrollbarDrag(y, scrollbarDragOffsetY, ToVanillaLayout(layout));
			}

			public void ReceiveLeftRelease(int x, int y)
			{
				isDraggingScrollbar = false;
                isDraggingPopup = false;
			}

			public void ReceiveLeftClick(int x, int y)
			{
				Point mouse = new Point(x, y);

                if (GetTitleDragRect().Contains(mouse))
                {
                    isDraggingPopup = true;
                    popupDragOffset = new Point(mouse.X - bounds.X, mouse.Y - bounds.Y);
                    dropdownOpen = false;
                    isDraggingScrollbar = false;
                    ClearTextBoxFocus();
                    return;
                }

				if (okRect.Contains(mouse))
				{
					TryApply();
					return;
				}

				if (cancelRect.Contains(mouse))
				{
					Cancel();
					return;
				}

                if (sourceGoRect.Contains(mouse))
                {
                    parent.NavigateFromEditPopup(sourceMapName, GetSourceTileFromBoxes());
                    return;
                }

                if (sourcePickRect.Contains(mouse))
                {
                    BeginCoordinatePick(CoordinatePickMode.Source);
                    return;
                }

                if (targetGoRect.Contains(mouse))
                {
                    parent.NavigateFromEditPopup(selectedName, GetTargetTileFromBoxes());
                    return;
                }

                if (targetPickRect.Contains(mouse))
                {
                    BeginCoordinatePick(CoordinatePickMode.Target);
                    return;
                }

				// Map field toggles dropdown open/closed.
				if (mapFieldRect.Contains(mouse) || mapDropdownButtonRect.Contains(mouse))
				{
					dropdownOpen = !dropdownOpen;
					isDraggingScrollbar = false;

					if (dropdownOpen)
					{
						RebuildDisplayedOptions(resetScroll: false);
						var layout = GetDropdownLayout();
						scrollIndex = WarpMasterFrameworkGmcmUi.CenterOnSelection(selectedIndex, layout.TotalItems, layout.VisibleItems);
						Game1.playSound("smallSelect");
					}
					else
					{
						Game1.playSound("bigDeSelect");
					}

					return;
				}

				if (dropdownOpen)
				{
					var layout = GetDropdownLayout();
					var vanillaLayout = ToVanillaLayout(layout);

					if (layout.ListRect.Contains(mouse))
					{
						if (WarpMasterFrameworkGmcmUi.TryBeginScrollbarDrag(mouse, vanillaLayout, scrollIndex, out int newScroll, out int dragOffsetY))
						{
							scrollIndex = newScroll;
							scrollbarDragOffsetY = dragOffsetY;
							isDraggingScrollbar = true;
							return;
						}

						int idx = WarpMasterFrameworkGmcmUi.GetIndexAtPoint(mouse, vanillaLayout, scrollIndex, WarpMasterFrameworkGmcmUi.DropdownRowHeight);
						if (idx >= 0 && idx < displayedOptions.Count)
						{
							selectedIndex = idx;
							selectedName = displayedOptions[idx].Name;
							dropdownOpen = false;
							isDraggingScrollbar = false;
							Game1.playSound("smallSelect");
						}

						return;
					}

					dropdownOpen = false;
					isDraggingScrollbar = false;
					Game1.playSound("bigDeSelect");
					return;
				}

				// Warp source/target tile coordinate fields.
				if (sourceXFieldRect.Contains(mouse))
				{
					sourceXBox.Selected = true;
					sourceYBox.Selected = false;
					xBox.Selected = false;
					yBox.Selected = false;
					Game1.keyboardDispatcher.Subscriber = sourceXBox;
					return;
				}

				if (sourceYFieldRect.Contains(mouse))
				{
					sourceYBox.Selected = true;
					sourceXBox.Selected = false;
					xBox.Selected = false;
					yBox.Selected = false;
					Game1.keyboardDispatcher.Subscriber = sourceYBox;
					return;
				}

				if (xFieldRect.Contains(mouse))
				{
					xBox.Selected = true;
					yBox.Selected = false;
					sourceXBox.Selected = false;
					sourceYBox.Selected = false;
					Game1.keyboardDispatcher.Subscriber = xBox;
					return;
				}

				if (yFieldRect.Contains(mouse))
				{
					yBox.Selected = true;
					xBox.Selected = false;
					sourceXBox.Selected = false;
					sourceYBox.Selected = false;
					Game1.keyboardDispatcher.Subscriber = yBox;
					return;
				}

				sourceXBox.Selected = false;
				sourceYBox.Selected = false;
				xBox.Selected = false;
				yBox.Selected = false;

				if (Game1.keyboardDispatcher.Subscriber == sourceXBox
					|| Game1.keyboardDispatcher.Subscriber == sourceYBox
					|| Game1.keyboardDispatcher.Subscriber == xBox
					|| Game1.keyboardDispatcher.Subscriber == yBox)
				{
					Game1.keyboardDispatcher.Subscriber = null;
				}
			}


			private static string TruncateToWidth(string text, SpriteFont font, float maxWidth)
			{
				if (string.IsNullOrEmpty(text))
					return "";
				if (font.MeasureString(text).X <= maxWidth)
					return text;

				const string ellipsis = "...";
				float ellW = font.MeasureString(ellipsis).X;
				if (ellW >= maxWidth)
					return "";

				int lo = 0;
				int hi = text.Length;
				while (lo < hi)
				{
					int mid = (lo + hi) / 2;
					string candidate = text.Substring(0, mid) + ellipsis;
					if (font.MeasureString(candidate).X <= maxWidth)
						lo = mid + 1;
					else
						hi = mid;
				}
				int keep = Math.Max(0, lo - 1);
				return text.Substring(0, keep) + ellipsis;
			}

			public void Draw(SpriteBatch b)
            {
                float dimAlpha = coordinatePickMode == CoordinatePickMode.None ? 0.55f : 0.25f;
                b.Draw(Game1.fadeToBlackRect, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height), Color.Black * dimAlpha);

                Rectangle titleBox = new Rectangle(bounds.X, bounds.Y, bounds.Width, 70);
                Rectangle bodyBox = new Rectangle(bounds.X, titleBox.Bottom + 8, bounds.Width, bounds.Height - 70 - 8 - 70);
                Rectangle buttonBar = new Rectangle(bounds.X, bounds.Bottom - 62, bounds.Width, 62);

                WarpMasterFrameworkGmcmUi.DrawTitleBox(b, titleBox, "Edit Warp");
                WarpMasterFrameworkGmcmUi.DrawClippedText(
                    b,
                    Game1.smallFont,
                    "Drag title bar to move",
                    new Vector2(titleBox.X + titleBox.Width - 210, titleBox.Y + 24),
                    180,
                    parent.uiTextColor
                );
                WarpMasterFrameworkGmcmUi.DrawMenuBox(b, bodyBox);
                WarpMasterFrameworkGmcmUi.DrawMenuBox(b, buttonBar);

                int rowHeight = WarpMasterFrameworkGmcmUi.RowHeight;
                int rowX = bodyBox.X + 36;
                int rowW = bodyBox.Width - 72;
                int rowY = bodyBox.Y + 18;

                int labelX1 = rowX;
                int labelW1 = 104;

                Rectangle row = new Rectangle(rowX, rowY, rowW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Source", new Rectangle(labelX1, row.Y, labelW1, row.Height), parent.uiTextColor);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "X", new Rectangle(sourceXFieldRect.X - 22, row.Y, 20, row.Height), parent.uiTextColor);
                sourceXBox.Draw(b);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Y", new Rectangle(sourceYFieldRect.X - 22, row.Y, 20, row.Height), parent.uiTextColor);
                sourceYBox.Draw(b);
                DrawButton(b, sourceGoRect, "Go");
                DrawButton(b, sourcePickRect, coordinatePickMode == CoordinatePickMode.Source ? "Click" : "Pick");
                rowY += rowHeight;

                row = new Rectangle(rowX, rowY, rowW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Target map", new Rectangle(labelX1, row.Y, labelW1, row.Height), parent.uiTextColor);
                WarpMasterFrameworkGmcmUi.DrawDropdownField(b, mapFieldRect, selectedName, Game1.smallFont, parent.uiTextColor, dropdownOpen);
                rowY += rowHeight;

                row = new Rectangle(rowX, rowY, rowW, rowHeight);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Target", new Rectangle(labelX1, row.Y, labelW1, row.Height), parent.uiTextColor);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "X", new Rectangle(xFieldRect.X - 22, row.Y, 20, row.Height), parent.uiTextColor);
                xBox.Draw(b);
                WarpMasterFrameworkGmcmUi.DrawLabel(b, "Y", new Rectangle(yFieldRect.X - 22, row.Y, 20, row.Height), parent.uiTextColor);
                yBox.Draw(b);
                DrawButton(b, targetGoRect, "Go");
                DrawButton(b, targetPickRect, coordinatePickMode == CoordinatePickMode.Target ? "Click" : "Pick");
                rowY += rowHeight;

                if (coordinatePickMode != CoordinatePickMode.None)
                {
                    string pickText = coordinatePickMode == CoordinatePickMode.Source
                        ? "Click the map to set the source tile."
                        : "Click the map to set the target tile.";
                    WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, pickText, new Vector2(rowX, rowY + 10), rowW, parent.uiTextColor);
                }

                DrawButton(b, okRect, "OK");
                DrawButton(b, cancelRect, "Cancel");

                if (dropdownOpen)
                    DrawDropdownList(b);
            }
			private Rectangle GetVanillaButtonRect(int right, int y, string text)
			{
				int width = Math.Max(96, (int)Game1.smallFont.MeasureString(text).X + 48);
				int height = Math.Max(44, WarpMasterFrameworkGmcmUi.GetDropdownButtonHeight());
				return new Rectangle(right - width, y, width, height);
			}

			private void DrawButton(SpriteBatch b, Rectangle rect, string text)
            {
                WarpMasterFrameworkGmcmUi.DrawFlatButton(b, rect, text, parent.uiTextColor);
            }

			private int GetVisibleItemCount()
			{
				var layout = GetDropdownLayout();
				return layout.VisibleItems;
			}

			private Rectangle GetDropdownListRect()
			{
				int total = displayedOptions?.Count ?? Math.Max(1, allOptions?.Count ?? 1);
				return WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
					mapFieldRect,
					total,
					WarpMasterFrameworkGmcmUi.DropdownRowHeight,
					9,
					listExcludesButton: true
				).ListRect;
			}

			

			

						private void DrawDropdownList(SpriteBatch b)
			{
				RebuildDisplayedOptions(resetScroll: false);

				var layout = GetDropdownLayout();

				// Vanilla OptionsDropDown list box
                WarpMasterFrameworkGmcmUi.DrawListPanel(b, layout.ListRect);
				int total = layout.TotalItems;
				int visible = layout.VisibleItems;

				int maxScroll = layout.MaxScroll;
				scrollIndex = Math.Clamp(scrollIndex, 0, maxScroll);

				

                int mouseX = Game1.getMouseX();
                int mouseY = Game1.getMouseY();
                int hoveredIndex = -1;
                if (layout.ItemsRect.Contains(mouseX, mouseY))
                {
                    int row = (mouseY - layout.ItemsRect.Y) / WarpMasterFrameworkGmcmUi.DropdownRowHeight;
                    if (row >= 0 && row < visible)
                    {
                        int idx = scrollIndex + row;
                        if (idx >= 0 && idx < total)
                            hoveredIndex = idx;
                    }
                }
				for (int i = 0; i < visible; i++)
				{
					int idx = scrollIndex + i;
					if (idx >= total)
						break;

					LocationOption opt = displayedOptions[idx];
					Rectangle itemRect = new Rectangle(layout.ItemsRect.X, layout.ItemsRect.Y + i * WarpMasterFrameworkGmcmUi.DropdownRowHeight, layout.ItemsRect.Width, WarpMasterFrameworkGmcmUi.DropdownRowHeight);
                    if (idx == hoveredIndex)
                        WarpMasterFrameworkGmcmUi.DrawHoverOverlay(b, itemRect);

                    WarpMasterFrameworkGmcmUi.DrawClippedText(b, Game1.smallFont, opt.DisplayName, new Vector2(itemRect.X + 10, itemRect.Y + (WarpMasterFrameworkGmcmUi.DropdownRowHeight - Game1.smallFont.LineSpacing) / 2f), itemRect.Width - 20, parent.uiTextColor);
				}

				// Vanilla mouseCursors scrollbar
                if (total > visible)
                    WarpMasterFrameworkGmcmUi.DrawScrollbar(b, layout.ScrollbarRect, total, visible, scrollIndex);
            }

			
			private static WarpMasterFrameworkGmcmUi.VanillaDropdownLayout ToVanillaLayout(DropdownLayout layout)
			{
				return new WarpMasterFrameworkGmcmUi.VanillaDropdownLayout
				{
					ListRect = layout.ListRect,
					ItemsRect = layout.ItemsRect,
					ScrollbarRect = layout.ScrollbarRect,
					VisibleItems = layout.VisibleItems,
					TotalItems = layout.TotalItems,
					MaxScroll = layout.MaxScroll
				};
			}

private struct DropdownLayout

			{
				public Rectangle ListRect;
				public Rectangle ItemsRect;
				public Rectangle ScrollbarRect;
				public int VisibleItems;
				public int TotalItems;
				public int MaxScroll;
			}

			private DropdownLayout GetDropdownLayout()
			{
				int total = displayedOptions?.Count ?? 0;
				var vanilla = WarpMasterFrameworkGmcmUi.CreateDropdownLayout(
					mapFieldRect,
					total,
					WarpMasterFrameworkGmcmUi.DropdownRowHeight,
					9,
					listExcludesButton: true
				);

				return new DropdownLayout
				{
					ListRect = vanilla.ListRect,
					ItemsRect = vanilla.ItemsRect,
					ScrollbarRect = vanilla.ScrollbarRect,
					VisibleItems = vanilla.VisibleItems,
					TotalItems = vanilla.TotalItems,
					MaxScroll = vanilla.MaxScroll
				};
			}

			private void RebuildDisplayedOptions(bool resetScroll)
			{
				// No filtering: always show all locations.
				// Sort alphabetically, but push locations starting with "Custom" to the bottom.
				displayedOptions = allOptions
					.OrderBy(o => (o?.Name ?? "").StartsWith("Custom", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
					.ThenBy(o => o?.Name ?? "", StringComparer.OrdinalIgnoreCase)
					.ToList();

// Ensure selection points to a valid option.
				selectedIndex = 0;

				if (!string.IsNullOrEmpty(selectedName))
				{
					int idx = displayedOptions.FindIndex(o => string.Equals(o.Name, selectedName, StringComparison.OrdinalIgnoreCase));
					if (idx >= 0)
						selectedIndex = idx;
				}

				if (displayedOptions.Count > 0)
				{
					selectedIndex = Math.Clamp(selectedIndex, 0, displayedOptions.Count - 1);
					selectedName = displayedOptions[selectedIndex].Name;
				}

				if (resetScroll)
					scrollIndex = 0;

				int visible = GetVisibleItemCount();
				int maxScroll = Math.Max(0, displayedOptions.Count - visible);
				scrollIndex = Math.Clamp(scrollIndex, 0, maxScroll);
			}

			public void Cancel()
			{
				dropdownOpen = false;
				isDraggingScrollbar = false;

				// If we were adding a new warp and the user cancelled, remove it.
				if (parent.pendingNewWarp == warp)
				{
					parent.currentMapWarps.Remove(warp);
					parent.pendingNewWarp = null;
				}

						sourceXBox.Selected = false;
						sourceYBox.Selected = false;
						xBox.Selected = false;
						yBox.Selected = false;
						if (Game1.keyboardDispatcher.Subscriber == sourceXBox
						    || Game1.keyboardDispatcher.Subscriber == sourceYBox
						    || Game1.keyboardDispatcher.Subscriber == xBox
						    || Game1.keyboardDispatcher.Subscriber == yBox)
						    Game1.keyboardDispatcher.Subscriber = null;

				Game1.playSound("bigDeSelect");
				parent.destinationEditPopup = null;
			}

			public bool TryApply()
			{
				dropdownOpen = false;
				isDraggingScrollbar = false;

				string newMap = !string.IsNullOrEmpty(selectedName)
					? selectedName
					: (warp?.TargetMap ?? "");

				if (string.IsNullOrWhiteSpace(newMap))
				{
					parent.SafeLog("[Edit Warp] Please choose a warp target map.", LogLevel.Warn);
					Game1.playSound("cancel");
					return false;
				}

				
int newSourceX = sourceTile.X;
int newSourceY = sourceTile.Y;

if (!int.TryParse(sourceXBox.Text, out newSourceX) || !int.TryParse(sourceYBox.Text, out newSourceY))
{
				    parent.SafeLog("[Edit Warp] Invalid warp source coordinates; please enter integers.", LogLevel.Warn);
    Game1.playSound("cancel");
    return false;
}

int newX = warp?.TargetPosition.X ?? 0;
int newY = warp?.TargetPosition.Y ?? 0;

if (!int.TryParse(xBox.Text, out newX) || !int.TryParse(yBox.Text, out newY))
{
				    parent.SafeLog("[Edit Warp] Invalid warp target coordinates; please enter integers.", LogLevel.Warn);
    Game1.playSound("cancel");
    return false;
}
				if (warp != null)
				{
					// Track old source so we can keep selection stable and ensure apply removes the previous live warp.
					Point oldSourceTile = warp.ModifiedPosition;
					// Preserve original identity if missing (important for matching across reloads).
					// For brand new warps, TargetMap/TargetPosition may be empty, so fall back to the new values.
					if (string.IsNullOrEmpty(warp.OriginalTargetMap))
						warp.OriginalTargetMap = string.IsNullOrEmpty(warp.TargetMap) ? newMap : (warp.TargetMap ?? "");

					if (warp.OriginalTargetPosition == Point.Zero)
						warp.OriginalTargetPosition = (warp.TargetPosition != Point.Zero) ? warp.TargetPosition : new Point(newX, newY);

					warp.ModifiedPosition = new Point(newSourceX, newSourceY);
					warp.TargetMap = newMap;
					warp.TargetPosition = new Point(newX, newY);

					// If this warp was selected in the editor, keep it selected after changing its source tile.
					if (parent.selectedWarp == warp)
						parent.selectedWarp = warp;

					// If the user moved the source via the popup, update our last-applied hint so ApplyWarpModifications()
					// can reliably remove the previously-applied live warp at the old source tile.
					if (oldSourceTile != warp.ModifiedPosition && oldSourceTile != Point.Zero)
						warp.LastAppliedPosition = oldSourceTile;
						// Apply/save immediately so runtime warps update without leaving the editor.
						parent.ApplyCurrentEditorEditsImmediately("edit warp popup");

					// If this was a newly added warp, it's now committed.
					if (parent.pendingNewWarp == warp)
						parent.pendingNewWarp = null;
				}
				else
				{
					// Editing via inbound destination marker: update the source warp (which may not be on the viewed map).
					try
					{
						parent.inboundCacheByDestMap.Clear();
						if (parent.onUpdateWarpSourceAndDestination != null)
    parent.onUpdateWarpSourceAndDestination.Invoke(this.sourceMapName, this.sourceTile, new Point(newSourceX, newSourceY), newMap, new Point(newX, newY));
else
    parent.onUpdateWarpDestination?.Invoke(this.sourceMapName, this.sourceTile, newMap, new Point(newX, newY));
					}
					catch
					{
						// Never let UI apply throw; at worst the change will apply on close.
					}
				}

				
// Unfocus
sourceXBox.Selected = false;
sourceYBox.Selected = false;
xBox.Selected = false;
yBox.Selected = false;
if (Game1.keyboardDispatcher.Subscriber == sourceXBox
    || Game1.keyboardDispatcher.Subscriber == sourceYBox
    || Game1.keyboardDispatcher.Subscriber == xBox
    || Game1.keyboardDispatcher.Subscriber == yBox)
    Game1.keyboardDispatcher.Subscriber = null;

				Game1.playSound("bigSelect");
				if (warp != null)
						parent.SafeLog($"[Edit Warp] Updated warp target: -> {warp.TargetMap} ({warp.TargetPosition.X},{warp.TargetPosition.Y})", LogLevel.Info);
				else
						parent.SafeLog($"[Edit Warp] Updated warp target: {sourceMapName} ({sourceTile.X},{sourceTile.Y}) -> {newMap} ({newX},{newY})", LogLevel.Info);
				parent.destinationEditPopup = null;
				return true;
			}
		}


    }
}