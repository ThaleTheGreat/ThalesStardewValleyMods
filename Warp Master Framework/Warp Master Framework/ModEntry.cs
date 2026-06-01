using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace WarpMasterFramework
{
    public class ModEntry : Mod
    {
        private ModConfig config;

        /// <summary>
        /// Log only when mod debug logging is enabled, except errors/alerts which always log.
        /// This keeps the SMAPI console clean unless the user opts in.
        /// </summary>
        private void WMLog(string message, LogLevel level = LogLevel.Debug)
        {
            if (this.config?.EnableDebugLogging == true || level >= LogLevel.Error)
                this.Monitor.Log(message, level);
        }

        private VisualWarpEditor visualEditor;
        private IGenericModConfigMenuApi gmcmApi;

        private const string SaveDataKey = "warp-master-framework";
        private const string LegacySaveDataKey = "mapster";
        private const string OlderLegacySaveDataKey = "warp-master";

        private const string FrameworkOverridesAssetKey = "Mods/ThaleTheGreat.WarpMasterFramework/WarpOverrides";
        private const string FrameworkOriginalWarpsAssetKey = "Mods/ThaleTheGreat.WarpMasterFramework/OriginalWarps";
        private const string FrameworkModDetailsAssetKey = "Mods/ThaleTheGreat.WarpMasterFramework/ModDetails";
        private WarpMasterFrameworkSaveData saveData;
        private bool saveDataDirty;
        

        private readonly Dictionary<string, int> AppliedWarpHashByMap = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, List<WarpPointData>> FrameworkOverridesByMap = new(StringComparer.OrdinalIgnoreCase);

        private static readonly Dictionary<string, Color> UiTextColorMap = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Black"] = Color.Black,
            ["White"] = Color.White,
            ["Gray"] = Color.Gray,
            ["Yellow"] = Color.Yellow,
            ["Orange"] = Color.Orange,
            ["Red"] = Color.Red,
            ["Green"] = Color.LimeGreen,
            ["Blue"] = Color.CornflowerBlue,
            ["Cyan"] = Color.Cyan,
            ["Magenta"] = Color.Magenta
        };

        private Color GetConfiguredUiTextColor()
        {
            string key = this.config?.UiTextColor ?? "Black";
            if (UiTextColorMap.TryGetValue(key, out Color c))
                return c;

            return Color.Black;
        }
        private WarpEditorMenu currentEditorMenu;
        
        public override void Entry(IModHelper helper)
        {
            config = helper.ReadConfig<ModConfig>();
            visualEditor = new VisualWarpEditor(Monitor);
            visualEditor.EnableDebugLogging = config.EnableDebugLogging;
            
            helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            helper.Events.GameLoop.Saving += OnSaving;
            helper.Events.Content.AssetRequested += OnAssetRequested;
            helper.Events.Content.AssetsInvalidated += OnAssetsInvalidated;

            // Apply persisted warp edits as soon as locations are loaded/entered.
            // This ensures edited warps are functional immediately after loading a save,
            // without requiring the user to open/close the editor menu.
            helper.Events.Player.Warped += OnWarped;
            
            helper.Events.Display.MenuChanged += this.OnMenuChanged;
            helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += OnButtonPressed;
            helper.Events.Input.MouseWheelScrolled += OnMouseWheelScrolled;
            helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;

            
            Helper.ConsoleCommands.Add("wmf_export", "Export original warp data and installed mod details for Content Patcher framework patches.", ExportFrameworkDataCommand);
            WMLog("Warp Master Framework initialized", LogLevel.Debug);
        }

        private void OnMouseWheelScrolled(object sender, MouseWheelScrolledEventArgs e)
        {
            if (currentEditorMenu != null && Game1.activeClickableMenu == currentEditorMenu)
            {
                if (config?.EnableDebugLogging == true)
                    WMLog($"[SMAPI Event] Mouse wheel scrolled: {e.Delta}", LogLevel.Debug);

                currentEditorMenu.HandleScrollWheel(e.Delta);
            }
        }

        
        private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
        {
            if (currentEditorMenu != null && Game1.activeClickableMenu == currentEditorMenu)
                currentEditorMenu.HandleInputFromSMAPI(Helper.Input);

            if (currentEditorMenu != null && Game1.activeClickableMenu != currentEditorMenu)
            {
                WMLog("Editor menu was closed externally", LogLevel.Debug);
                currentEditorMenu = null;
            }
        }

        private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo(FrameworkOverridesAssetKey))
            {
                e.LoadFrom(() => new WarpFrameworkOverrideAsset(), AssetLoadPriority.Low);
                return;
            }

            if (e.NameWithoutLocale.IsEquivalentTo(FrameworkOriginalWarpsAssetKey))
            {
                e.LoadFrom(BuildOriginalWarpExport, AssetLoadPriority.Low);
                return;
            }

            if (e.NameWithoutLocale.IsEquivalentTo(FrameworkModDetailsAssetKey))
            {
                e.LoadFrom(BuildModDetailsExport, AssetLoadPriority.Low);
            }
        }

        private void OnAssetsInvalidated(object sender, AssetsInvalidatedEventArgs e)
        {
            if (!Context.IsWorldReady || !e.NamesWithoutLocale.Any(name => name.IsEquivalentTo(FrameworkOverridesAssetKey)))
                return;

            LoadFrameworkOverrides();
            foreach (GameLocation location in Game1.locations)
                ApplyWarpModificationsToLocation(location);
        }

        private void LoadFrameworkOverrides()
        {
            FrameworkOverridesByMap.Clear();

            if (config?.EnableFrameworkOverrides != true || !Context.IsWorldReady)
                return;

            WarpFrameworkOverrideAsset asset;
            try
            {
                asset = Helper.GameContent.Load<WarpFrameworkOverrideAsset>(FrameworkOverridesAssetKey);
            }
            catch (Exception ex)
            {
                WMLog($"Failed to load framework warp overrides from {FrameworkOverridesAssetKey}: {ex.Message}", LogLevel.Error);
                return;
            }

            if (asset?.Overrides == null)
                return;

            foreach (WarpFrameworkOverride entry in asset.Overrides)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.SourceMap))
                    continue;

                GameLocation location = Game1.getLocationFromName(entry.SourceMap);
                GameLocationSnapshotFallback fallback = GetOriginalWarpFallback(location, entry);
                WarpPointData data = entry.ToWarpPointData(fallback);

                if (!FrameworkOverridesByMap.TryGetValue(data.MapName, out var list))
                {
                    list = new List<WarpPointData>();
                    FrameworkOverridesByMap[data.MapName] = list;
                }

                list.Add(data);
            }

            WMLog($"Loaded {FrameworkOverridesByMap.Sum(p => p.Value.Count)} framework warp override(s).", LogLevel.Debug);
        }

        private GameLocationSnapshotFallback GetOriginalWarpFallback(GameLocation location, WarpFrameworkOverride entry)
        {
            if (location != null)
            {
                StardewValley.Warp live = location.warps.FirstOrDefault(w => w.X == entry.OriginalX && w.Y == entry.OriginalY);
                if (live != null)
                    return new GameLocationSnapshotFallback(live.TargetName ?? "", new Point(live.TargetX, live.TargetY));
            }

            return new GameLocationSnapshotFallback(entry.OriginalTargetMap ?? "", new Point(entry.OriginalTargetX ?? 0, entry.OriginalTargetY ?? 0));
        }

        private List<WarpPointData> GetCombinedWarpModifications(string mapName)
        {
            var combined = new List<WarpPointData>();

            if ((this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>()).TryGetValue(mapName, out var saved) && saved != null)
                combined.AddRange(saved);

            if (config?.EnableFrameworkOverrides == true && FrameworkOverridesByMap.TryGetValue(mapName, out var framework) && framework != null)
                combined.AddRange(framework);

            return combined;
        }

        internal void ExportFrameworkDataFromEditor()
        {
            try
            {
                ExportFrameworkData();
                Game1.addHUDMessage(new HUDMessage("Warp Master Framework export complete.", HUDMessage.newQuest_type));
                Monitor.Log("Exported Warp Master Framework data from editor.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Game1.addHUDMessage(new HUDMessage("Warp Master Framework export failed. Check SMAPI.", HUDMessage.error_type));
                Monitor.Log($"Failed to export Warp Master Framework data from editor: {ex}", LogLevel.Error);
            }
        }

        private void ExportFrameworkDataCommand(string command, string[] args)
        {
            try
            {
                ExportFrameworkData();
                Monitor.Log("Exported Warp Master Framework data.", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Monitor.Log($"Failed to export Warp Master Framework data: {ex}", LogLevel.Error);
            }
        }

        private void ExportFrameworkData()
        {
            DirectoryInfo dir = Directory.CreateDirectory(Path.Combine(Helper.DirectoryPath, "exports"));
            JsonSerializerOptions options = new() { WriteIndented = true };

            File.WriteAllText(Path.Combine(dir.FullName, "original-warps.json"), JsonSerializer.Serialize(BuildOriginalWarpExport(), options));
            File.WriteAllText(Path.Combine(dir.FullName, "warp-overrides-template.json"), JsonSerializer.Serialize(BuildOverrideTemplate(), options));
        }

        private WarpFrameworkOverrideAsset BuildOverrideTemplate()
        {
            var template = new WarpFrameworkOverrideAsset();
            foreach (var pair in BuildOriginalWarpExport().Maps)
            {
                WarpFrameworkOriginalWarp first = pair.Value.FirstOrDefault(w => string.Equals(w.WarpType, "Warp", StringComparison.OrdinalIgnoreCase));
                if (first == null)
                    continue;

                template.Overrides.Add(new WarpFrameworkOverride
                {
                    Id = $"{first.SourceMap}_{first.X}_{first.Y}",
                    SourceMap = first.SourceMap,
                    WarpType = first.WarpType,
                    OriginalX = first.X,
                    OriginalY = first.Y,
                    OriginalTargetMap = first.TargetMap,
                    OriginalTargetX = first.TargetX,
                    OriginalTargetY = first.TargetY,
                    NewX = first.X,
                    NewY = first.Y,
                    TargetMap = first.TargetMap,
                    TargetX = first.TargetX,
                    TargetY = first.TargetY
                });
                break;
            }

            return template;
        }

        private WarpFrameworkOriginalExport BuildModDetailsExport()
        {
            return new WarpFrameworkOriginalExport
            {
                Mods = Helper.ModRegistry.GetAll()
                    .Select(modInfo => new WarpFrameworkModDetail
                    {
                        Name = modInfo.Manifest.Name,
                        UniqueID = modInfo.Manifest.UniqueID,
                        Author = modInfo.Manifest.Author,
                        Version = modInfo.Manifest.Version?.ToString() ?? "",
                        IsContentPack = modInfo.Manifest.ContentPackFor != null
                    })
                    .OrderBy(m => m.UniqueID, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            };
        }

        private WarpFrameworkOriginalExport BuildOriginalWarpExport()
        {
            var export = BuildModDetailsExport();

            if (!Context.IsWorldReady)
                return export;

            foreach (GameLocation location in Game1.locations.Where(l => l != null).OrderBy(l => l.NameOrUniqueName ?? l.Name, StringComparer.OrdinalIgnoreCase))
            {
                string mapName = location.NameOrUniqueName ?? location.Name;
                var list = visualEditor.DetectWarpsForLocation(location)
                    .Select(w => new WarpFrameworkOriginalWarp
                    {
                        SourceMap = mapName,
                        WarpType = w.WarpType ?? "Warp",
                        X = (w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition).X,
                        Y = (w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition).Y,
                        TargetMap = w.GetOriginalTargetMapFallback(),
                        TargetX = w.GetOriginalTargetPosFallback().X,
                        TargetY = w.GetOriginalTargetPosFallback().Y,
                        DoorLayerName = w.DoorLayerName ?? "",
                        DoorPropertyName = w.DoorPropertyName ?? "",
                        DoorCommand = w.DoorCommand ?? "Warp",
                        DoorExtraTokens = w.DoorExtraTokens ?? ""
                    })
                    .ToList();

                if (list.Count > 0)
                    export.Maps[mapName] = list;
            }

            return export;
        }

        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            // Get GMCM API
            gmcmApi = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            
            if (gmcmApi != null)
            {
                SetupConfigMenu();
            }
        }
        
        private void SetupConfigMenu()
        {
            // Register mod
            gmcmApi.Register(
                mod: ModManifest,
                reset: () => config = new ModConfig(),
                save: () => 
                {
                    Helper.WriteConfig(config);
                    if (visualEditor != null)
                        visualEditor.EnableDebugLogging = config.EnableDebugLogging;
                    ApplyWarpModifications();
                }
            );
            
            // Add config options
            gmcmApi.AddBoolOption(
                mod: ModManifest,
                name: () => "Enable Visual Editor",
                tooltip: () => "Toggle the visual warp point editor overlay",
                getValue: () => config.EnableVisualEditor,
                setValue: value => config.EnableVisualEditor = value
            );

            gmcmApi.AddBoolOption(
                mod: ModManifest,
                name: () => "Enable Framework Overrides",
                tooltip: () => "Apply warp overrides loaded from the Content Patcher-editable framework asset.",
                getValue: () => config.EnableFrameworkOverrides,
                setValue: value => config.EnableFrameworkOverrides = value
            );
            
            gmcmApi.AddTextOption(
                mod: ModManifest,
                name: () => "Visual Editor Key",
                tooltip: () => "Keyboard key to toggle the visual editor (default: F9)",
                getValue: () => config.VisualEditorKey,
                setValue: value => config.VisualEditorKey = value
            );


            gmcmApi.AddTextOption(
                mod: ModManifest,
                name: () => "UI Text Color",
                tooltip: () => "Text color for editor panels and popups (warp hover labels stay white).",
                getValue: () => config.UiTextColor,
                setValue: value => config.UiTextColor = value,
                allowedValues: new[] { "Black", "White", "Gray", "Yellow", "Orange", "Red", "Green", "Blue", "Cyan", "Magenta" }
            );

            gmcmApi.AddBoolOption(
                mod: ModManifest,
                name: () => "Enable Debug Logging",
                tooltip: () => "If enabled, Warp Master Framework will write detailed debug/trace logs to SMAPI (useful for troubleshooting).",
                getValue: () => config.EnableDebugLogging,
                setValue: value => config.EnableDebugLogging = value
            );


            // Tooltip modes: Hide | Hover | Show
            string[] tooltipModes = new[] { "Hide", "Hover", "Show" };

            gmcmApi.AddTextOption(
                mod: ModManifest,
                name: () => "Source Tooltips",
                tooltip: () => "Controls how Source labels/tooltips are shown in the visual editor (Hide, Hover, or Show).",
                getValue: () => config.SourceTooltipMode ?? "Hover",
                setValue: value => config.SourceTooltipMode = value,
                allowedValues: tooltipModes
            );

            gmcmApi.AddTextOption(
                mod: ModManifest,
                name: () => "Target Tooltips",
                tooltip: () => "Controls how Target labels/tooltips are shown in the visual editor (Hide, Hover, or Show).",
                getValue: () => config.TargetTooltipMode ?? "Hover",
                setValue: value => config.TargetTooltipMode = value,
                allowedValues: tooltipModes
            );
}
        
        private void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
        {
            
// Load per-save warp edits (persisted only when the game saves).
this.saveData = Helper.Data.ReadSaveData<WarpMasterFrameworkSaveData>(SaveDataKey)
                ?? Helper.Data.ReadSaveData<WarpMasterFrameworkSaveData>(LegacySaveDataKey)
                ?? Helper.Data.ReadSaveData<WarpMasterFrameworkSaveData>(OlderLegacySaveDataKey)
                ?? new WarpMasterFrameworkSaveData();
this.saveDataDirty = false;

            // Initial detection
            if (Game1.currentLocation != null)
            {
                visualEditor.DetectWarpsOnCurrentMap();
                LoadWarpModifications();
                LoadFrameworkOverrides();
                ApplyWarpModifications();
            }
        }


private void OnSaving(object sender, SavingEventArgs e)
{
    // We only persist edits at the game's normal end-of-day save.
    // This ensures edits apply immediately in-session, but aren't permanently stored if the player quits before sleeping.
    if (!Context.IsWorldReady)
        return;

    if (!this.saveDataDirty || this.saveData == null)
        return;

    if (!IsOvernightSave())
        return;

    Helper.Data.WriteSaveData(SaveDataKey, this.saveData);
    this.saveDataDirty = false;
    WMLog("Persisted Warp Master Framework edits to save data (overnight save).", LogLevel.Debug);
}

private void OnReturnedToTitle(object sender, ReturnedToTitleEventArgs e)
{
    // Discard any uncommitted changes when leaving the save.
    this.saveData = null;
    this.saveDataDirty = false;

    // Menu state cleanup
    this.currentEditorMenu = null;
}

private static bool IsOvernightSave()
{
    try
    {
        // These flags are only true during the sleep/save pipeline.
        if (Game1.newDay)
            return true;

        if (Game1.player != null && Game1.player.isInBed.Value)
            return true;

        // Time is advanced to >= 2600 during day-end.
        if (Game1.timeOfDay >= 2600)
            return true;
    }
    catch
    {
        // If anything is unavailable, assume not overnight.
    }

    return false;
}

        private void OnWarped(object sender, WarpedEventArgs e)
        {
            if (e.IsLocalPlayer)
            {
                visualEditor.DetectWarpsOnCurrentMap();
                LoadWarpModifications();
                LoadFrameworkOverrides();
                ApplyWarpModifications();
                
                // Refresh GMCM if open
                if (gmcmApi != null)
                {
                    WMLog($"Detected {visualEditor.GetCurrentWarps().Count} warp points on {e.NewLocation.Name}", LogLevel.Debug);
                }
            }
        }
        
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            if (!Context.IsWorldReady)
                return;
            // Handle keypresses while the editor menu is open (via SMAPI event which works even with menu open)
            if (currentEditorMenu != null && Game1.activeClickableMenu == currentEditorMenu)
            {
                // Give the menu a chance to consume the button (e.g. Escape should close a modal popup, not the whole editor).
                if (currentEditorMenu.TryHandleGlobalButton(e.Button))
                {
                    Helper.Input.Suppress(e.Button);
                    return;
                }

                if (e.Button == SButton.F9 || e.Button == SButton.Escape)
                {
                    WMLog($"[SMAPI Event] {e.Button} pressed - closing menu", LogLevel.Debug);
                    CloseEditorMenu();
                    Helper.Input.Suppress(e.Button);
                    return;
                }
            }
            
            // Toggle visual editor with F9
            if (e.Button.ToString() == config.VisualEditorKey)
            {
                WMLog($"F9 pressed! EnableVisualEditor={config.EnableVisualEditor}", LogLevel.Debug);
                
                if (config.EnableVisualEditor)
                {
                    // Check if menu is already open
                    if (Game1.activeClickableMenu is WarpEditorMenu)
                    {
                        CloseEditorMenu();
                    }
                    else
                    {
                        OpenEditorMenu();
                    }
                }
                else
                {
                    WMLog("Visual editor is disabled in config!", LogLevel.Debug);
                }
            }
        }

        // Track location history for navigation
        private Stack<string> editorLocationHistory = new Stack<string>();
        
        private void OpenEditorMenu()
        {
            editorLocationHistory = new Stack<string>();
            OpenEditorMenuForLocation(Game1.currentLocation?.Name, editorLocationHistory, null, null);
        }
        
        private void OpenEditorMenuForLocation(string locationName, Stack<string> existingHistory, Point? initialCenterTile, Point? initialAnchorScreen)
        {
            if (string.IsNullOrEmpty(locationName))
            {
                WMLog("Cannot open editor - no location specified", LogLevel.Debug);
                return;
            }
            
            // Get the location
            GameLocation location = Game1.getLocationFromName(locationName);
            if (location == null)
            {
                WMLog($"Cannot open editor - location '{locationName}' not found", LogLevel.Debug);
                return;
            }
            
            WMLog($"Opening visual editor on map: {locationName}", LogLevel.Debug);
            
            // Detect warps for this location (not necessarily current location)
            var warps = visualEditor.DetectWarpsForLocation(location);
            LoadWarpModificationsForLocation(locationName, warps);
            
            WMLog($"Visual editor enabled. Found {warps.Count} warp points.", LogLevel.Debug);
            
            // Use existing history or start fresh
            editorLocationHistory = existingHistory ?? new Stack<string>();

            // If the user is still holding the mouse button from a shift-click navigation action, the newly
            // created menu instance would immediately interpret that as another navigation click. Debounce
            // by marking the relevant mouse button as already handled until it is released.
            bool suppressShiftLeftUntilRelease = Helper.Input.IsDown(SButton.MouseLeft);
            bool suppressShiftRightUntilRelease = Helper.Input.IsDown(SButton.MouseRight);
            bool suppressRUntilRelease = Helper.Input.IsDown(SButton.R);
            
            // Create and open the menu with navigation callbacks
            currentEditorMenu = new WarpEditorMenu(
                warps,
                Monitor,
                true,
                viewedMapName: locationName,
                navigateToLocation: NavigateToLocation, // callback for shift+left-click navigation
                navigateBack: NavigateBack,             // callback for shift+right-click back
                navigateToRoot: NavigateToRoot,
                history: editorLocationHistory,
                onImmediateSaveApply: SaveCurrentEditorState,
                onExportFrameworkData: ExportFrameworkDataFromEditor,
                onUpdateWarpDestination: this.UpdateWarpDestinationExternal,
                onUpdateWarpSourceAndDestination: this.UpdateWarpSourceAndDestinationExternal,
                initialCenterTile: initialCenterTile,
                initialAnchorScreen: initialAnchorScreen,
                suppressShiftLeftUntilRelease: suppressShiftLeftUntilRelease,
                suppressShiftRightUntilRelease: suppressShiftRightUntilRelease,
                suppressRUntilRelease: suppressRUntilRelease,
                getExternalWarpEditedOrAdded: IsExternalWarpEditedOrAdded,
                getExternalWarpOriginalTarget: GetExternalWarpOriginalTarget,
                enableDebugLogging: config.EnableDebugLogging,
                sourceTooltipMode: config.SourceTooltipMode,
                targetTooltipMode: config.TargetTooltipMode,
                uiTextColor: GetConfiguredUiTextColor()
            );
            Game1.activeClickableMenu = currentEditorMenu;
            
            // Log each warp for debugging
            foreach (var warp in warps)
            {
                WMLog($"  - {warp.WarpType} at ({warp.OriginalPosition.X}, {warp.OriginalPosition.Y}) -> {warp.TargetMap}", LogLevel.Debug);
            }
        }

        private static bool MatchesWarpSourceIdentity(WarpPointData warp, Point tile)
        {
            if (warp == null)
                return false;

            return warp.ModifiedPosition == tile
                || (warp.LastAppliedPosition != Point.Zero && warp.LastAppliedPosition == tile)
                || (warp.TrueOriginalPosition != Point.Zero && warp.TrueOriginalPosition == tile)
                || warp.OriginalPosition == tile;
        }

        private static void PrepareWarpForImmediateMove(WarpPointData warp, Point oldSourceTile)
        {
            if (warp == null)
                return;

            if (warp.TrueOriginalPosition == Point.Zero)
                warp.TrueOriginalPosition = warp.OriginalPosition != Point.Zero ? warp.OriginalPosition : oldSourceTile;

            warp.LastAppliedPosition = oldSourceTile;

            if (string.IsNullOrEmpty(warp.LastAppliedTargetMap))
                warp.LastAppliedTargetMap = warp.TargetMap ?? "";

            if (warp.LastAppliedTargetPosition == Point.Zero)
                warp.LastAppliedTargetPosition = warp.TargetPosition;
        }

        private bool IsExternalWarpEditedOrAdded(string sourceMap, Point sourceTile)
        {
            if (string.IsNullOrEmpty(sourceMap))
                return false;

            if (!(this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>()).TryGetValue(sourceMap, out var mods) || mods == null)
                return false;

            foreach (var w in mods)
            {
                if (w == null)
                    continue;

                // Match by current/last/true/original position (covers moved warps).
                if (w.ModifiedPosition == sourceTile ||
                    (w.LastAppliedPosition != Point.Zero && w.LastAppliedPosition == sourceTile) ||
                    (w.TrueOriginalPosition != Point.Zero && w.TrueOriginalPosition == sourceTile) ||
                    w.OriginalPosition == sourceTile)
                {
                    if (w.IsAddedByMod)
                        return true;

                    Point trueOrig = w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition;
                    if (w.ModifiedPosition != trueOrig)
                        return true;

                    string origMap = string.IsNullOrEmpty(w.OriginalTargetMap) ? (w.TargetMap ?? "") : w.OriginalTargetMap;
                    Point origPos = w.OriginalTargetPosition != Point.Zero ? w.OriginalTargetPosition : w.TargetPosition;
                    if (!string.Equals((w.TargetMap ?? ""), origMap, StringComparison.OrdinalIgnoreCase) || w.TargetPosition != origPos)
                        return true;

                    return false;
                }
            }

            return false;
        }


        private WarpPointData GetExternalWarpOriginalTarget(string sourceMap, Point sourceTile)
        {
            if (string.IsNullOrEmpty(sourceMap))
                return null;

            if (!(this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>())
                .TryGetValue(sourceMap, out var mods) || mods == null)
                return null;

            foreach (var w in mods)
            {
                if (w == null)
                    continue;

                // Match by current/last/true/original position (covers moved warps).
                if (w.ModifiedPosition == sourceTile ||
                    (w.LastAppliedPosition != Point.Zero && w.LastAppliedPosition == sourceTile) ||
                    (w.TrueOriginalPosition != Point.Zero && w.TrueOriginalPosition == sourceTile) ||
                    w.OriginalPosition == sourceTile)
                {
                    return w;
                }
            }

            return null;
        }
        
        private void NavigateToLocation(string targetLocationName, Point? centerOnTile, Point? anchorScreen)
        {
            if (string.IsNullOrEmpty(targetLocationName))
            {
                WMLog("Cannot navigate - no target location", LogLevel.Debug);
                return;
            }

            // Save changes exactly like ESC/F9 before switching maps.
            SaveCurrentEditorState();

            // Ensure history exists.
            editorLocationHistory ??= new Stack<string>();

            // Push the currently viewed map (NOT the player's current location).
            if (currentEditorMenu != null)
            {
                string currentViewed = currentEditorMenu.GetViewedMapName();
                if (!string.IsNullOrEmpty(currentViewed) && currentViewed != targetLocationName)
                    editorLocationHistory.Push(currentViewed);
            }

            WMLog($"Navigating to location: {targetLocationName}", LogLevel.Debug);

            OpenEditorMenuForLocation(targetLocationName, editorLocationHistory, centerOnTile, anchorScreen);
        }
        
        private void NavigateBack()
        {
            if (editorLocationHistory == null || editorLocationHistory.Count == 0)
            {
                WMLog("Cannot go back - no history", LogLevel.Debug);
                return;
            }

            // Save changes exactly like ESC/F9 before switching maps.
            SaveCurrentEditorState();

            string previousLocation = editorLocationHistory.Pop();
            WMLog($"Going back to: {previousLocation}", LogLevel.Debug);

            OpenEditorMenuForLocation(previousLocation, editorLocationHistory, null, null);
        }

        private void NavigateToRoot()
        {
            editorLocationHistory ??= new Stack<string>();

            if (editorLocationHistory.Count == 0)
            {
                WMLog("Already at first map - no history", LogLevel.Debug);
                return;
            }

            // Save changes exactly like ESC/F9 before switching maps.
            SaveCurrentEditorState();

            // Stack enumerates from most-recent to oldest; the root is the LAST item.
            string rootLocation = editorLocationHistory.Last();
            editorLocationHistory.Clear();

            WMLog($"Returning to first map in history: {rootLocation}", LogLevel.Debug);

            OpenEditorMenuForLocation(rootLocation, editorLocationHistory, null, null);
        }
        
        
        private void SaveCurrentEditorState()
        {
            if (currentEditorMenu == null)
                return;

            var warps = currentEditorMenu.GetWarps();
            string viewedMapName = currentEditorMenu.GetViewedMapName();

            // Persist only real edits. Do NOT create/mark save data dirty just by viewing a map.
            if (!string.IsNullOrEmpty(viewedMapName))
            {
                this.saveData ??= new WarpMasterFrameworkSaveData();

                var filtered = FilterToEdits(viewedMapName, warps);

                // If there are no edits, remove any existing entry and only mark dirty if something actually changed.
                if (filtered == null || filtered.Count == 0)
                {
                    if (this.saveData.WarpModifications.Remove(viewedMapName))
                        this.saveDataDirty = true;
                }
                else
                {
                    // Only write if the content materially changed (prevents repeated apply/log spam).
                    if (!this.saveData.WarpModifications.TryGetValue(viewedMapName, out var existing)
                        || ComputeWarpListHash(existing) != ComputeWarpListHash(filtered))
                    {
                        this.saveData.WarpModifications[viewedMapName] = filtered;
                        this.saveDataDirty = true;
                    }
                }
            }

            // Keep runtime state in sync
            visualEditor.UpdateWarps(warps);

            // Write config for the map we are viewing. Do NOT call SaveWarpModifications(), which uses
            // Game1.currentLocation and can overwrite the wrong map.
            Helper.WriteConfig(config);

            // Apply immediately for this session to the viewed location, even if the player isn't
            // physically standing there. This keeps runtime warp lists in sync when editing remote maps
            // (needed for destination markers and for shift-click navigation to work correctly).
            GameLocation viewedLocation = !string.IsNullOrEmpty(viewedMapName)
                ? Game1.getLocationFromName(viewedMapName)
                : null;

            if (viewedLocation != null)
                ApplyWarpModificationsToLocation(viewedLocation);
        }

        private void CloseEditorMenu()
        {
            if (currentEditorMenu != null)
            {
                SaveCurrentEditorState();

                Game1.activeClickableMenu = null;
                currentEditorMenu = null;
                WMLog("Visual editor closed", LogLevel.Debug);
            }
        }
        
        private void LoadWarpModificationsForLocation(string locationName, List<WarpPointData> warps)
        {

            if (string.IsNullOrEmpty(locationName) || warps == null)
                return;

            // Ensure current detections have stable identity fields filled.
            foreach (var warp in warps)
            {
                if (warp.TrueOriginalPosition == Point.Zero)
                    warp.TrueOriginalPosition = warp.OriginalPosition;

                if (string.IsNullOrEmpty(warp.OriginalTargetMap))
                    warp.OriginalTargetMap = warp.TargetMap ?? "";

                if (warp.OriginalTargetPosition == Point.Zero && warp.WarpType == "Warp")
                    warp.OriginalTargetPosition = warp.TargetPosition;
            }

            if ((this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>()).TryGetValue(locationName, out var savedWarps) && savedWarps != null)
            {
                foreach (var currentWarp in warps)
                {
                    // Back-compat for older configs: OriginalTarget* may be missing.
                    // Use savedWarp.OriginalTarget* if present, else fall back to savedWarp.Target*.
                    WarpPointData savedWarp = savedWarps.FirstOrDefault(w =>
                        (w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition) == currentWarp.OriginalPosition
                        && string.Equals(string.IsNullOrEmpty(w.OriginalTargetMap) ? w.TargetMap : w.OriginalTargetMap,
                                         currentWarp.OriginalTargetMap,
                                         StringComparison.OrdinalIgnoreCase)
                        && w.WarpType == currentWarp.WarpType
                        && (currentWarp.WarpType != "Warp" ||
                            (w.OriginalTargetPosition != Point.Zero ? w.OriginalTargetPosition : w.TargetPosition) == currentWarp.OriginalTargetPosition)
                    );

                    // If the location's warps were already patched by Warp Master Framework, then the game's live warp list
                    // contains the *modified* warp (at ModifiedPosition/Target*), not the vanilla/original identity.
                    // In that case, the detection pass will see the patched warp and set currentWarp.OriginalPosition
                    // to the modified tile. Match saved modifications against that applied state so we merge into a
                    // single WarpPointData instead of appending a duplicate.
                    if (savedWarp == null)
                    {
                        savedWarp = savedWarps.FirstOrDefault(w =>
                            string.Equals(w.WarpType, currentWarp.WarpType, StringComparison.OrdinalIgnoreCase)
                            && w.ModifiedPosition == currentWarp.OriginalPosition
                            && string.Equals(w.TargetMap ?? "", currentWarp.TargetMap ?? "", StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(currentWarp.WarpType, "Warp", StringComparison.OrdinalIgnoreCase) ? w.TargetPosition == currentWarp.TargetPosition : true)
                        );
                    }

                    if (savedWarp == null)
                    {
                        savedWarp = savedWarps.FirstOrDefault(w =>
                            string.Equals(w.WarpType, currentWarp.WarpType, StringComparison.OrdinalIgnoreCase)
                            && (w.LastAppliedPosition != Point.Zero && w.LastAppliedPosition == currentWarp.OriginalPosition)
                            && string.Equals((string.IsNullOrEmpty(w.LastAppliedTargetMap) ? (w.TargetMap ?? "") : w.LastAppliedTargetMap),
                                             currentWarp.TargetMap ?? "",
                                             StringComparison.OrdinalIgnoreCase)
                            && (string.Equals(currentWarp.WarpType, "Warp", StringComparison.OrdinalIgnoreCase) ?
                                   ((w.LastAppliedTargetPosition != Point.Zero ? w.LastAppliedTargetPosition : w.TargetPosition) == currentWarp.TargetPosition)
                                   : true)
                        );
                    }

                    // Extra fallback: match by original position + current target map (helps if OriginalTargetMap wasn't saved before).
                    if (savedWarp == null)
                    {
                        savedWarp = savedWarps.FirstOrDefault(w =>
                            (w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition) == currentWarp.OriginalPosition
                            && string.Equals(w.TargetMap, currentWarp.TargetMap, StringComparison.OrdinalIgnoreCase)
                            && w.WarpType == currentWarp.WarpType
                        );
                    }

                    if (savedWarp != null)
                    {
                        // Repair identity for already-patched detections: reset OriginalPosition back to the true original.
                        // This keeps identity stable, prevents duplicates in the editor list, and ensures ApplyWarpModifications
                        // can reliably remove/replace the underlying vanilla warp.
                        Point trueOrig = savedWarp.TrueOriginalPosition != Point.Zero
                            ? savedWarp.TrueOriginalPosition
                            : savedWarp.OriginalPosition;
                        currentWarp.OriginalPosition = trueOrig;

                        // Position changes
                        currentWarp.ModifiedPosition = savedWarp.ModifiedPosition;
                        currentWarp.TrueOriginalPosition = savedWarp.TrueOriginalPosition != Point.Zero
                            ? savedWarp.TrueOriginalPosition
                            : savedWarp.OriginalPosition;

                        // Destination changes
                        currentWarp.TargetMap = savedWarp.TargetMap ?? currentWarp.TargetMap;
                        currentWarp.TargetPosition = savedWarp.TargetPosition;

                        // Track last-applied state so we can correctly replace an already-applied warp
                        // when it is moved/edited again (prevents duplicates in GameLocation.warps).
                        currentWarp.LastAppliedPosition = savedWarp.LastAppliedPosition != Point.Zero ? savedWarp.LastAppliedPosition : savedWarp.ModifiedPosition;
                        currentWarp.LastAppliedTargetMap = !string.IsNullOrEmpty(savedWarp.LastAppliedTargetMap) ? savedWarp.LastAppliedTargetMap : (savedWarp.TargetMap ?? "");
                        currentWarp.LastAppliedTargetPosition = savedWarp.LastAppliedTargetPosition != Point.Zero ? savedWarp.LastAppliedTargetPosition : savedWarp.TargetPosition;

                        // Preserve/repair identity fields. Use saved warp's stable identity even if the current detection
                        // came from an already-patched warp (where OriginalTarget* might equal the edited destination).
                        currentWarp.OriginalTargetMap = savedWarp.GetOriginalTargetMapFallback();

                        if (currentWarp.WarpType == "Warp")
                            currentWarp.OriginalTargetPosition = savedWarp.GetOriginalTargetPosFallback();

	                        // Propagate whether this warp was created by Warp Master Framework.
	                        currentWarp.IsAddedByMod = savedWarp.IsAddedByMod;
                    }
                    else
                    {
	                        // No saved match: treat as vanilla detection (not created by Warp Master Framework).
                        currentWarp.TrueOriginalPosition = currentWarp.OriginalPosition;
	                        currentWarp.IsAddedByMod = false;
                        currentWarp.LastAppliedPosition = currentWarp.ModifiedPosition;
                        currentWarp.LastAppliedTargetMap = currentWarp.TargetMap ?? "";
                        currentWarp.LastAppliedTargetPosition = currentWarp.TargetPosition;
                        if (string.IsNullOrEmpty(currentWarp.OriginalTargetMap))
                            currentWarp.OriginalTargetMap = currentWarp.TargetMap ?? "";
                        if (currentWarp.WarpType == "Warp" && currentWarp.OriginalTargetPosition == Point.Zero)
                            currentWarp.OriginalTargetPosition = currentWarp.TargetPosition;
                    }
                }
            }
            else
            {
                foreach (var warp in warps)
                {
                    warp.TrueOriginalPosition = warp.OriginalPosition;
                    if (string.IsNullOrEmpty(warp.OriginalTargetMap))
                        warp.OriginalTargetMap = warp.TargetMap ?? "";
                    if (warp.WarpType == "Warp" && warp.OriginalTargetPosition == Point.Zero)
                        warp.OriginalTargetPosition = warp.TargetPosition;

                    // No saved edits for this map: last-applied defaults to current.
                    warp.LastAppliedPosition = warp.ModifiedPosition;
                    warp.LastAppliedTargetMap = warp.TargetMap ?? "";
                    warp.LastAppliedTargetPosition = warp.TargetPosition;
                }
            }
        
            // Append any saved warp modifications that aren't currently present in the detected list.
            // This is important for newly-created warps (e.g., via A+Click) which won't exist in vanilla map warps.
            if ((this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>()).TryGetValue(locationName, out var allSavedWarps) && allSavedWarps != null)
            {
                static Point IdPos(WarpPointData w) => w.TrueOriginalPosition != Point.Zero ? w.TrueOriginalPosition : w.OriginalPosition;
                static string IdMap(WarpPointData w) => string.IsNullOrEmpty(w.OriginalTargetMap) ? (w.TargetMap ?? "") : w.OriginalTargetMap;
                static Point IdTargetPos(WarpPointData w) => w.OriginalTargetPosition != Point.Zero ? w.OriginalTargetPosition : w.TargetPosition;

                foreach (var saved in allSavedWarps)
                {
                    if (saved == null)
                        continue;

                    Point savedPos = IdPos(saved);
                    string savedTargetMap = IdMap(saved);
                    Point savedTargetPos = IdTargetPos(saved);

                    bool exists = false;
                    foreach (var current in warps)
                    {
                        if (current == null)
                            continue;

                        if (!string.Equals(current.WarpType, saved.WarpType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (IdPos(current) != savedPos)
                            continue;

                        if (!string.Equals(IdMap(current), savedTargetMap, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(saved.WarpType, "Warp", StringComparison.OrdinalIgnoreCase) && IdTargetPos(current) != savedTargetPos)
                            continue;

                        exists = true;
                        break;
                    }

                    if (!exists)
                    {
                        var clone = new WarpPointData
                        {
                            MapName = locationName,
	                            IsAddedByMod = true,
                            WarpType = saved.WarpType,
                            OriginalPosition = saved.OriginalPosition,
                            ModifiedPosition = saved.ModifiedPosition,
                            TargetMap = saved.TargetMap,
                            TargetPosition = saved.TargetPosition,
                            TrueOriginalPosition = saved.TrueOriginalPosition,
                            OriginalTargetMap = saved.OriginalTargetMap,
                            OriginalTargetPosition = saved.OriginalTargetPosition,
                            LastAppliedPosition = saved.LastAppliedPosition,
                            LastAppliedTargetMap = saved.LastAppliedTargetMap,
                            LastAppliedTargetPosition = saved.LastAppliedTargetPosition
                        };

                        if (clone.TrueOriginalPosition == Point.Zero)
                            clone.TrueOriginalPosition = clone.OriginalPosition;

                        if (clone.LastAppliedPosition == Point.Zero)
                            clone.LastAppliedPosition = clone.ModifiedPosition;

                        if (string.IsNullOrEmpty(clone.LastAppliedTargetMap))
                            clone.LastAppliedTargetMap = clone.TargetMap ?? "";

                        if (clone.LastAppliedTargetPosition == Point.Zero)
                            clone.LastAppliedTargetPosition = clone.TargetPosition;

                        if (string.IsNullOrEmpty(clone.OriginalTargetMap))
                            clone.OriginalTargetMap = clone.TargetMap ?? "";

                        if (string.Equals(clone.WarpType, "Warp", StringComparison.OrdinalIgnoreCase) && clone.OriginalTargetPosition == Point.Zero)
                            clone.OriginalTargetPosition = clone.TargetPosition;

                        warps.Add(clone);
                    }
                }
            }

}
        private void LoadWarpModifications()
        {
            if (Game1.currentLocation == null)
                return;

            var warps = visualEditor.GetCurrentWarps();
            if (warps is null)
                return;

            LoadWarpModificationsForLocation(Game1.currentLocation.Name, warps);

            // Ensure the visual editor has the updated warp data (including any saved modifications).
            visualEditor.UpdateWarps(warps);
        }

        
private static List<WarpPointData> FilterToEdits(string mapName, IEnumerable<WarpPointData> warps)
{
    var list = new List<WarpPointData>();
    if (warps == null)
        return list;

    foreach (var w in warps)
    {
        if (w == null)
            continue;

        bool positionChanged =
            (w.ModifiedPosition != Point.Zero && w.OriginalPosition != Point.Zero && w.ModifiedPosition != w.OriginalPosition);

        bool destChanged =
            (!string.IsNullOrEmpty(w.OriginalTargetMap) && !string.Equals(w.TargetMap ?? "", w.OriginalTargetMap ?? "", StringComparison.OrdinalIgnoreCase))
            || (w.OriginalTargetPosition != Point.Zero && w.TargetPosition != Point.Zero && w.TargetPosition != w.OriginalTargetPosition);

        bool added = w.IsAddedByMod;

        if (added || positionChanged || destChanged)
        {
            w.MapName = mapName ?? (w.MapName ?? "");
            list.Add(w);
        }
    }

    return list;
}

    /// <summary>
    /// Compute a stable-ish hash for a list of warp point data. Used to avoid rewriting save data
    /// when nothing materially changed.
    /// </summary>
    private static int ComputeWarpListHash(IList<WarpPointData> list)
    {
        if (list == null || list.Count == 0)
            return 0;

        static Point IdPos(WarpPointData m) => m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition;
        static string IdMap(WarpPointData m) => string.IsNullOrEmpty(m.OriginalTargetMap) ? (m.TargetMap ?? "") : m.OriginalTargetMap;
        static Point IdTargetPos(WarpPointData m) => m.OriginalTargetPosition != Point.Zero ? m.OriginalTargetPosition : m.TargetPosition;

        unchecked
        {
            int hash = 17;

            foreach (var m in list.Where(p => p != null)
                                  .OrderBy(p => (p.WarpType ?? "Warp"))
                                  .ThenBy(p => IdPos(p).X).ThenBy(p => IdPos(p).Y)
                                  .ThenBy(p => IdMap(p), StringComparer.OrdinalIgnoreCase)
                                  .ThenBy(p => IdTargetPos(p).X).ThenBy(p => IdTargetPos(p).Y))
            {
                hash = hash * 31 + (m.WarpType ?? "").GetHashCode();
                hash = hash * 31 + IdPos(m).GetHashCode();
                hash = hash * 31 + IdMap(m).GetHashCode();
                hash = hash * 31 + IdTargetPos(m).GetHashCode();

                hash = hash * 31 + m.ModifiedPosition.GetHashCode();
                hash = hash * 31 + (m.TargetMap ?? "").GetHashCode();
                hash = hash * 31 + m.TargetPosition.GetHashCode();

                hash = hash * 31 + m.IsAddedByMod.GetHashCode();

                hash = hash * 31 + (m.DoorLayerName ?? "").GetHashCode();
                hash = hash * 31 + (m.DoorPropertyName ?? "").GetHashCode();
                hash = hash * 31 + (m.DoorCommand ?? "").GetHashCode();
                hash = hash * 31 + (m.DoorExtraTokens ?? "").GetHashCode();
            }

            return hash;
        }
    }


private void SaveWarpModifications()
        {
            if (Game1.currentLocation == null)
                return;
            
            string mapName = Game1.currentLocation.Name;
            (this.saveData?.WarpModifications ?? new Dictionary<string, List<WarpPointData>>())[mapName] = new List<WarpPointData>(visualEditor.GetCurrentWarps());
            Helper.WriteConfig(config);
        }
private void ApplyWarpModifications()
{
    if (Game1.currentLocation == null)
        return;

    ApplyWarpModificationsToLocation(Game1.currentLocation);
}

/// <summary>Apply saved warp modifications to an arbitrary loaded location.</summary>

private static List<WarpPointData> DeduplicateWarpList(List<WarpPointData> warps)
{
    if (warps == null)
        return new List<WarpPointData>();

    static Point IdPos(WarpPointData m) => m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition;
    static string IdMap(WarpPointData m) => string.IsNullOrEmpty(m.OriginalTargetMap) ? (m.TargetMap ?? "") : m.OriginalTargetMap;
    static Point IdTargetPos(WarpPointData m) => m.OriginalTargetPosition != Point.Zero ? m.OriginalTargetPosition : m.TargetPosition;

    var seen = new HashSet<(string warpType, Point pos, string map, Point targetPos)>();
    var result = new List<WarpPointData>();

    foreach (var m in warps)
    {
        if (m == null)
            continue;

        var key = (m.WarpType ?? "Warp", IdPos(m), IdMap(m), IdTargetPos(m));
        if (seen.Add(key))
            result.Add(m);
    }

    return result;
}

private void ApplyWarpModificationsToLocation(GameLocation location)
{
    if (location == null)
        return;

    string mapName = location.Name;

    var modifications = GetCombinedWarpModifications(mapName);
    if (modifications == null || modifications.Count == 0)
        return;

    // Door support removed; treat all saved modifications as regular tile warps.
    // Defensive copy + de-duplication by stable identity (true original + original destination identity).
    // This guards against any past bugs that may have produced duplicate entries.
    var warpMods = new List<WarpPointData>();
    {
        static Point IdPos(WarpPointData m) => m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition;
        static string IdMap(WarpPointData m) => string.IsNullOrEmpty(m.OriginalTargetMap) ? (m.TargetMap ?? "") : m.OriginalTargetMap;
        static Point IdTargetPos(WarpPointData m) => m.OriginalTargetPosition != Point.Zero ? m.OriginalTargetPosition : m.TargetPosition;

        var seen = new HashSet<(string warpType, Point pos, string map, Point targetPos)>();
        foreach (var m in modifications)
        {
            if (m == null)
                continue;

            var key = (m.WarpType ?? "Warp", IdPos(m), IdMap(m), IdTargetPos(m));
            // If duplicates exist, keep the first occurrence (they should be identical after LoadWarpModificationsForLocation merges).
            if (seen.Add(key))
                warpMods.Add(m);
        }
    }
    if (warpMods.Count == 0)
        return;


// Skip if nothing materially changed since last apply (prevents log spam / repeated rebuilds).
unchecked
{
    int hash = 17;
    foreach (var m2 in warpMods)
    {
        if (m2 == null) continue;
        hash = hash * 31 + (m2.WarpType ?? "").GetHashCode();
        hash = hash * 31 + m2.ModifiedPosition.GetHashCode();
        hash = hash * 31 + (m2.TargetMap ?? "").GetHashCode();
        hash = hash * 31 + m2.TargetPosition.GetHashCode();
        hash = hash * 31 + (m2.DoorLayerName ?? "").GetHashCode();
        hash = hash * 31 + (m2.DoorPropertyName ?? "").GetHashCode();
        hash = hash * 31 + (m2.DoorCommand ?? "").GetHashCode();
        hash = hash * 31 + (m2.DoorExtraTokens ?? "").GetHashCode();
        hash = hash * 31 + m2.IsDeleted.GetHashCode();
    }

    if (AppliedWarpHashByMap.TryGetValue(mapName, out var lastHash) && lastHash == hash)
        return;

    AppliedWarpHashByMap[mapName] = hash;
}


    // Split regular tile warps vs door warps.
    var doorMods = warpMods.Where(m => m != null && string.Equals(m.WarpType, "Door", StringComparison.OrdinalIgnoreCase)).ToList();
    var tileMods = warpMods.Where(m => m == null || !string.Equals(m.WarpType, "Door", StringComparison.OrdinalIgnoreCase)).ToList();

    // Apply door warp modifications via map tile properties.
    if (doorMods.Count > 0)
        ApplyDoorWarpModificationsToLocation(location, doorMods);

    // Nothing else to do if no regular tile warps.
    if (tileMods.Count == 0)
        return;

    static string GetOrigTargetMap(WarpPointData m) =>
        string.IsNullOrEmpty(m.OriginalTargetMap) ? (m.TargetMap ?? "") : m.OriginalTargetMap;

    static Point GetOrigTargetPos(WarpPointData m) =>
        m.OriginalTargetPosition != Point.Zero ? m.OriginalTargetPosition : m.TargetPosition;


    static string GetLastAppliedTargetMap(WarpPointData m) =>
        string.IsNullOrEmpty(m.LastAppliedTargetMap) ? (m.TargetMap ?? "") : m.LastAppliedTargetMap;

    static Point GetLastAppliedTargetPos(WarpPointData m) =>
        m.LastAppliedTargetPosition != Point.Zero ? m.LastAppliedTargetPosition : m.TargetPosition;

    bool MatchesWarp(StardewValley.Warp w, WarpPointData m)
    {
        string origMap = GetOrigTargetMap(m);
        Point origPos = GetOrigTargetPos(m);

        // Use the stable true-original position for identity matching if available.
        Point trueOrigPos = m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition;

        // Match original identity
        if (w.X == trueOrigPos.X &&
            w.Y == trueOrigPos.Y &&
            string.Equals(w.TargetName, origMap, StringComparison.OrdinalIgnoreCase) &&
            w.TargetX == origPos.X &&
            w.TargetY == origPos.Y)
            return true;

        // Match last-applied state (used when the user moves/edits the same warp multiple times).
        Point lastPos = m.LastAppliedPosition != Point.Zero ? m.LastAppliedPosition : m.ModifiedPosition;
        string lastMap = GetLastAppliedTargetMap(m);
        Point lastTargetPos = GetLastAppliedTargetPos(m);
        if (w.X == lastPos.X &&
            w.Y == lastPos.Y &&
            string.Equals(w.TargetName, lastMap, StringComparison.OrdinalIgnoreCase) &&
            w.TargetX == lastTargetPos.X &&
            w.TargetY == lastTargetPos.Y)
            return true;

        // Match already-applied state (idempotent apply)
        if (w.X == m.ModifiedPosition.X &&
            w.Y == m.ModifiedPosition.Y &&
            string.Equals(w.TargetName, m.TargetMap ?? "", StringComparison.OrdinalIgnoreCase) &&
            w.TargetX == m.TargetPosition.X &&
            w.TargetY == m.TargetPosition.Y)
            return true;

        return false;
    }

    var existing = location.warps?.ToList() ?? new List<StardewValley.Warp>();

    // Keep all warps that aren't controlled by our modifications.
    var kept = new List<StardewValley.Warp>();
    foreach (var w in existing)
    {
        if (!tileMods.Any(m => MatchesWarp(w, m)))
            kept.Add(w);
    }

    // Re-add all modified warps (position and/or destination).
    foreach (var m in tileMods)
    {
        if (m?.IsDeleted == true)
            continue;

        var match = existing.FirstOrDefault(w => MatchesWarp(w, m));
        bool flip = match?.flipFarmer?.Value ?? false;

        kept.Add(new StardewValley.Warp(
            m.ModifiedPosition.X,
            m.ModifiedPosition.Y,
            m.TargetMap ?? "",
            m.TargetPosition.X,
            m.TargetPosition.Y,
            flip
        ));
    }

    location.warps.Clear();
    foreach (var w in kept)
        location.warps.Add(w);

    // Update last-applied for next edit cycle (prevents duplicates on re-apply).
    foreach (var m in tileMods)
    {
        if (m == null)
            continue;

        m.LastAppliedPosition = m.ModifiedPosition;
        m.LastAppliedTargetMap = m.TargetMap ?? "";
        m.LastAppliedTargetPosition = m.TargetPosition;
    }

    // Invalidate detection cache so any subsequent menu open/destination marker rebuild sees updated warps/door actions.
    this.visualEditor?.InvalidateDetectionCache(mapName);

    WMLog($"Applied {warpMods.Count} warp modifications (including destinations) to {mapName}", LogLevel.Debug);
}

/// <summary>
/// Apply door-warp modifications by updating tile properties on the map layers.
/// Door warps are represented by tile properties like Action/TouchAction/Warp which start with "Warp ".
/// </summary>
private void ApplyDoorWarpModificationsToLocation(GameLocation location, List<WarpPointData> doorMods)
{
    if (location?.Map == null || doorMods == null || doorMods.Count == 0)
        return;

    var map = location.Map;

    foreach (var m in doorMods)
    {
        if (m == null)
            continue;

        if (m.IsDeleted)
        {
            Point deletePos = m.LastAppliedPosition != Point.Zero
                ? m.LastAppliedPosition
                : (m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition);
            TryClearDoorWarpProperty(map, string.IsNullOrEmpty(m.DoorLayerName) ? "Buildings" : m.DoorLayerName, string.IsNullOrEmpty(m.DoorPropertyName) ? "Action" : m.DoorPropertyName, deletePos, m);
            continue;
        }

        // Determine which property to write; default to Action if missing.
        string layerName = string.IsNullOrEmpty(m.DoorLayerName) ? "Buildings" : m.DoorLayerName;
        string propName = string.IsNullOrEmpty(m.DoorPropertyName) ? "Action" : m.DoorPropertyName;

        // Only rewrite door properties when needed. Most door warps should be left as-is unless edited/added.
        Point basePos = m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition;
        string origTargetMap = string.IsNullOrEmpty(m.OriginalTargetMap) ? (m.TargetMap ?? "") : m.OriginalTargetMap;
        Point origTargetPos = m.OriginalTargetPosition != Point.Zero ? m.OriginalTargetPosition : m.TargetPosition;
        bool targetChanged = (m.TargetMap ?? "") != origTargetMap || m.TargetPosition != origTargetPos;
        bool sourceChanged = m.ModifiedPosition != basePos;
        bool shouldWrite = m.IsAddedByMod || targetChanged || sourceChanged;

        // Safety repair: if a previous version wrote an invalid Action warp (Warp <map> <x> <y> ...),
        // convert it back to the game's expected Action format (Warp <x> <y> <map> ...), even if not edited.
        if (!shouldWrite && string.Equals(propName, "Action", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGetDoorWarpProperty(map, layerName, propName, m.ModifiedPosition, out string existing) &&
                TryNormalizeActionWarpString(existing, out string normalized) &&
                !string.Equals(existing, normalized, StringComparison.Ordinal))
            {
                TrySetDoorWarpProperty(map, layerName, propName, m.ModifiedPosition, normalized);
            }
        }

        if (!shouldWrite)
            continue;


        // Remove the previously-applied property if the source moved.
        Point oldPos = m.LastAppliedPosition != Point.Zero
            ? m.LastAppliedPosition
            : (m.TrueOriginalPosition != Point.Zero ? m.TrueOriginalPosition : m.OriginalPosition);

        if (oldPos != m.ModifiedPosition)
        {
            TryClearDoorWarpProperty(map, layerName, propName, oldPos, m);
        }

        // Write updated warp string at the new source position.
        string warpString = BuildDoorWarpString(m, propName);
        TrySetDoorWarpProperty(map, layerName, propName, m.ModifiedPosition, warpString);

        // Update last-applied so subsequent edits can remove/replace cleanly.
        m.LastAppliedPosition = m.ModifiedPosition;
        m.LastAppliedTargetMap = m.TargetMap ?? "";
        m.LastAppliedTargetPosition = m.TargetPosition;
    }
}

/// <summary>
/// Try to synthesize a door-warp WarpPointData from a tile property at the given tile.
/// Used as a fallback when we need to edit a destination marker but can't match a live warp.
/// </summary>
private bool TryCreateDoorWarpPointData(GameLocation location, Point tile, out WarpPointData data)
{
    data = null;
    if (location?.Map == null)
        return false;

    var map = location.Map;
    string mapName = location.Name;

    var layers = new[] { "Buildings", "Back", "Front" };
    var props = new[] { "Action", "TouchAction", "Warp" };

    foreach (var layerName in layers)
    {
        var layer = map.GetLayer(layerName);
        if (layer == null)
            continue;

        if (tile.X < 0 || tile.Y < 0 || tile.X >= layer.LayerWidth || tile.Y >= layer.LayerHeight)
            continue;

        var t = layer.Tiles[tile.X, tile.Y];
        if (t?.Properties == null)
            continue;

        foreach (var propName in props)
        {
            if (!t.Properties.TryGetValue(propName, out var pv))
                continue;

            string raw = pv?.ToString();
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            raw = raw.Trim();
            if (!(raw.StartsWith("Warp ", StringComparison.OrdinalIgnoreCase)
                  || raw.StartsWith("MagicWarp ", StringComparison.OrdinalIgnoreCase)
                  || raw.StartsWith("LockedDoorWarp ", StringComparison.OrdinalIgnoreCase)))
                continue;

            var parts = raw.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string cmd = parts.Length > 0 ? parts[0] : "";
            if (parts.Length < 4)
                continue;

            string targetMap = "";
            int tx = 0;
            int ty = 0;
            string extra = "";
            string tokenOrder = "";

            // Prefer parsing based on property name, but also accept the other order to be safe.
            bool isAction = string.Equals(propName, "Action", StringComparison.OrdinalIgnoreCase);

            if (isAction)
            {
                // Expected: Warp <x> <y> <map> [extra...]
                if (int.TryParse(parts[1], out tx) && int.TryParse(parts[2], out ty))
                {
                    targetMap = parts[3];
                    tokenOrder = "xymap";
                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                }
                else if (int.TryParse(parts[2], out tx) && int.TryParse(parts[3], out ty))
                {
                    // Safety: Warp <map> <x> <y> [extra...]
                    targetMap = parts[1];
                    tokenOrder = "mapxy";
                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                }
                else
                    continue;
            }
            else
            {
                // Expected: Warp <map> <x> <y> [extra...]
                if (int.TryParse(parts[2], out tx) && int.TryParse(parts[3], out ty))
                {
                    targetMap = parts[1];
                    tokenOrder = "mapxy";
                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                }
                else if (int.TryParse(parts[1], out tx) && int.TryParse(parts[2], out ty))
                {
                    // Safety: Warp <x> <y> <map> [extra...]
                    targetMap = parts[3];
                    tokenOrder = "xymap";
                    extra = parts.Length > 4 ? string.Join(" ", parts.Skip(4)) : "";
                }
                else
                    continue;
            }

            data = new WarpPointData
            {
                MapName = mapName,
                WarpType = "Door",
                IsAddedByMod = false,

                OriginalPosition = tile,
                ModifiedPosition = tile,
                TrueOriginalPosition = tile,

                TargetMap = targetMap,
                TargetPosition = new Point(tx, ty),
                OriginalTargetMap = targetMap,
                OriginalTargetPosition = new Point(tx, ty),

                DoorLayerName = layerName,
                DoorPropertyName = propName,
                DoorTokenOrder = tokenOrder,
                DoorExtraTokens = extra,
                    DoorCommand = cmd,

            };

            return true;
        }
    }

    return false;
}

private static string BuildDoorWarpString(WarpPointData m, string propName)
{
    string command = string.IsNullOrWhiteSpace(m.DoorCommand) ? "Warp" : m.DoorCommand.Trim();
    string targetMap = m.TargetMap ?? "";
    Point targetTile = m.TargetPosition;
    string extraTokens = m.DoorExtraTokens ?? "";

    // Game expects different argument order depending on property:
    // - Action:       Warp <x> <y> <map> [extra...]
    // - TouchAction:  Warp <map> <x> <y> [extra...]
    bool actionOrderXYMap = string.Equals(propName, "Action", StringComparison.OrdinalIgnoreCase)
        || string.Equals(m.DoorTokenOrder, "xymap", StringComparison.OrdinalIgnoreCase);

    string baseStr = actionOrderXYMap
        ? $"{command} {targetTile.X} {targetTile.Y} {targetMap}"
        : $"{command} {targetMap} {targetTile.X} {targetTile.Y}";

    if (!string.IsNullOrWhiteSpace(extraTokens))
        baseStr += " " + extraTokens.Trim();

    return baseStr.Trim();
}

private static void TrySetDoorWarpProperty(xTile.Map map, string layerName, string propName, Point tile, string value)
{
    var layer = map?.GetLayer(layerName);
    if (layer == null)
        return;
    if (tile.X < 0 || tile.Y < 0 || tile.X >= layer.LayerWidth || tile.Y >= layer.LayerHeight)
        return;
    var t = layer.Tiles[tile.X, tile.Y];
    if (t?.Properties == null)
        return;

    // Tile properties are xTile PropertyValue; wrap the string.
    t.Properties[propName] = new xTile.ObjectModel.PropertyValue(value);
}


private static bool TryGetDoorWarpProperty(xTile.Map map, string layerName, string propName, Point tile, out string value)
{
    value = null;
    var layer = map?.GetLayer(layerName);
    if (layer == null)
        return false;
    if (tile.X < 0 || tile.Y < 0 || tile.X >= layer.LayerWidth || tile.Y >= layer.LayerHeight)
        return false;
    var t = layer.Tiles[tile.X, tile.Y];
    if (t?.Properties == null)
        return false;

    if (!t.Properties.TryGetValue(propName, out var pv) || pv == null)
        return false;

    value = pv.ToString();
    return !string.IsNullOrWhiteSpace(value);
}

private static bool TryNormalizeActionWarpString(string existing, out string normalized)
{
    normalized = existing;
    if (string.IsNullOrWhiteSpace(existing))
        return false;

    var parts = existing.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
    if (parts.Length < 4)
        return false;

    if (!string.Equals(parts[0], "Warp", System.StringComparison.OrdinalIgnoreCase))
        return false;

    // If token 1 is not an int but tokens 2/3 are ints, this is the invalid "Warp <map> <x> <y> ..." order.
    if (!int.TryParse(parts[1], out _) && int.TryParse(parts[2], out int x) && int.TryParse(parts[3], out int y))
    {
        string mapName = parts[1];
        var rest = parts.Length > 4 ? string.Join(" ", parts[4..]) : "";
        normalized = rest.Length > 0 ? $"Warp {x} {y} {mapName} {rest}".Trim() : $"Warp {x} {y} {mapName}";
        return true;
    }

    return false;
}


private static void TryClearDoorWarpProperty(xTile.Map map, string layerName, string propName, Point tile, WarpPointData m)
{
    var layer = map?.GetLayer(layerName);
    if (layer == null)
        return;
    if (tile.X < 0 || tile.Y < 0 || tile.X >= layer.LayerWidth || tile.Y >= layer.LayerHeight)
        return;
    var t = layer.Tiles[tile.X, tile.Y];
    if (t?.Properties == null)
        return;

    // Only clear if it looks like a Warp property. Avoid nuking unrelated actions.
    if (t.Properties.TryGetValue(propName, out var pv))
    {
        string raw = pv?.ToString() ?? "";
        if (raw.Trim().StartsWith("Warp ", StringComparison.OrdinalIgnoreCase))
            t.Properties.Remove(propName);
    }
}

/// <summary>
/// Update the destination tile for a warp which originates in a different map than the one currently being viewed.
/// This is used by the editor's inbound-destination markers ("X").
/// </summary>
private void UpdateWarpDestinationExternal(string sourceLocationName, Point sourceWarpTile, string destinationMapName, Point newDestinationTile)
{
    if (!Context.IsWorldReady)
        return;

    if (string.IsNullOrEmpty(sourceLocationName) || string.IsNullOrEmpty(destinationMapName))
        return;

    GameLocation sourceLocation = Game1.getLocationFromName(sourceLocationName);
    if (sourceLocation == null)
        return;

    // Detect + merge modifications for the SOURCE location.
    var warps = visualEditor.DetectWarpsForLocation(sourceLocation);
    LoadWarpModificationsForLocation(sourceLocationName, warps);

    WarpPointData match = warps.FirstOrDefault(w =>
        MatchesWarpSourceIdentity(w, sourceWarpTile));

    if (match == null)
    {
        // If we somehow couldn't match a WarpPointData (e.g. exotic warp list), synthesize one from the live warp.
        StardewValley.Warp live = sourceLocation.warps?.FirstOrDefault(w => w.X == sourceWarpTile.X && w.Y == sourceWarpTile.Y);
        if (live == null)
        {
            // Try door warp from tile property.
            if (!TryCreateDoorWarpPointData(sourceLocation, sourceWarpTile, out var doorData))
                return;

            match = doorData;
        }
        else
        {
            match = new WarpPointData
        {
            MapName = sourceLocationName,
            WarpType = "Warp",
            OriginalPosition = new Point(live.X, live.Y),
            ModifiedPosition = new Point(live.X, live.Y),
            TrueOriginalPosition = new Point(live.X, live.Y),
            TargetMap = live.TargetName ?? "",
            TargetPosition = new Point(live.TargetX, live.TargetY),
            OriginalTargetMap = live.TargetName ?? "",
            OriginalTargetPosition = new Point(live.TargetX, live.TargetY)
        };
        }

        warps.Add(match);
    }

    // Ensure original destination identity is recorded so destination edits are detectable.
    if (string.IsNullOrEmpty(match.OriginalTargetMap) && match.OriginalTargetPosition == Point.Zero)
    {
        match.OriginalTargetMap = match.TargetMap ?? "";
        match.OriginalTargetPosition = match.TargetPosition;
    }

    PrepareWarpForImmediateMove(match, sourceWarpTile);

    match.TargetMap = destinationMapName;
    match.TargetPosition = newDestinationTile;

    this.saveData ??= new WarpMasterFrameworkSaveData();
    warps = DeduplicateWarpList(warps);

    this.saveData.WarpModifications[sourceLocationName] = FilterToEdits(sourceLocationName, warps);
    this.saveDataDirty = true;

    // Apply immediately for this session to the SOURCE location.
    ApplyWarpModificationsToLocation(sourceLocation);
    RefreshActiveEditorWarpsForLocation(sourceLocationName, sourceLocation);
}

/// <summary>
/// Update BOTH the source tile and target for a warp which originates in a different map than the one currently being viewed.
/// This is used by the editor's inbound-target markers ("X") edit popup.
/// </summary>
private void UpdateWarpSourceAndDestinationExternal(string sourceLocationName, Point oldSourceWarpTile, Point newSourceWarpTile, string destinationMapName, Point newDestinationTile)
{
    if (!Context.IsWorldReady)
        return;

    if (string.IsNullOrEmpty(sourceLocationName) || string.IsNullOrEmpty(destinationMapName))
        return;

    GameLocation sourceLocation = Game1.getLocationFromName(sourceLocationName);
    if (sourceLocation == null)
        return;

    // Detect + merge modifications for the SOURCE location.
    var warps = visualEditor.DetectWarpsForLocation(sourceLocation);
    LoadWarpModificationsForLocation(sourceLocationName, warps);

    WarpPointData match = warps.FirstOrDefault(w =>
        MatchesWarpSourceIdentity(w, oldSourceWarpTile));

    if (match == null)
    {
        // Fallback: synthesize from the live warp list (prefer old tile, then new tile). If no live warp exists, try a door warp.
        StardewValley.Warp live = sourceLocation.warps?.FirstOrDefault(w => w.X == oldSourceWarpTile.X && w.Y == oldSourceWarpTile.Y)
            ?? sourceLocation.warps?.FirstOrDefault(w => w.X == newSourceWarpTile.X && w.Y == newSourceWarpTile.Y);

        if (live == null)
        {
            if (!TryCreateDoorWarpPointData(sourceLocation, oldSourceWarpTile, out var doorData) &&
                !TryCreateDoorWarpPointData(sourceLocation, newSourceWarpTile, out doorData))
                return;

            match = doorData;
        }
        else
        {
            match = new WarpPointData
            {
                MapName = sourceLocationName,
                WarpType = "Warp",
                OriginalPosition = new Point(live.X, live.Y),
                ModifiedPosition = new Point(live.X, live.Y),
                TrueOriginalPosition = new Point(live.X, live.Y),
                TargetMap = live.TargetName ?? "",
                TargetPosition = new Point(live.TargetX, live.TargetY),
                OriginalTargetMap = live.TargetName ?? "",
                OriginalTargetPosition = new Point(live.TargetX, live.TargetY)
            };
        }

        warps.Add(match);
    }
// Ensure original destination identity is recorded so destination edits are detectable.
    if (string.IsNullOrEmpty(match.OriginalTargetMap) && match.OriginalTargetPosition == Point.Zero)
    {
        match.OriginalTargetMap = match.TargetMap ?? "";
        match.OriginalTargetPosition = match.TargetPosition;
    }

    PrepareWarpForImmediateMove(match, oldSourceWarpTile);

    match.ModifiedPosition = newSourceWarpTile;
    match.TargetMap = destinationMapName;
    match.TargetPosition = newDestinationTile;
this.saveData ??= new WarpMasterFrameworkSaveData();
    warps = DeduplicateWarpList(warps);

    this.saveData.WarpModifications[sourceLocationName] = FilterToEdits(sourceLocationName, warps);
    this.saveDataDirty = true;

    // Apply immediately for this session to the SOURCE location.
    ApplyWarpModificationsToLocation(sourceLocation);
    RefreshActiveEditorWarpsForLocation(sourceLocationName, sourceLocation);
}


    private void RefreshActiveEditorWarpsForLocation(string locationName, GameLocation location)
    {
        if (currentEditorMenu == null || location == null || string.IsNullOrEmpty(locationName))
            return;

        if (!string.Equals(currentEditorMenu.GetViewedMapName(), locationName, StringComparison.OrdinalIgnoreCase))
            return;

        var refreshed = visualEditor.DetectWarpsForLocation(location);
        LoadWarpModificationsForLocation(locationName, refreshed);
        currentEditorMenu.ReplaceWarps(refreshed);
        visualEditor.UpdateWarps(refreshed);
    }

    /// <summary>Ensure pending changes are committed when the editor menu closes (even if closed by game/UI).</summary>
    private void OnMenuChanged(object sender, StardewModdingAPI.Events.MenuChangedEventArgs e)
    {
        if (e.OldMenu is WarpEditorMenu oldMenu)
            oldMenu.CommitPendingChanges();
    }
}
}