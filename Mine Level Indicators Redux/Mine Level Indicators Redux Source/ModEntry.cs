using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewObject = StardewValley.Object;

namespace MineLevelIndicators;

public sealed class ModEntry : Mod
{
    private const int MaxSkullCavernDinoLevel = 300;
    private const int MaxEntriesPerLine = 12;
    private const int MaxCharactersPerLine = 72;

    private readonly List<(int Level, string Type)> infestedLevels = new();
    private readonly List<int> mushroomLevels = new();
    private readonly List<int> dinoLevels = new();
    private ClickableTextureComponent? icon;
    private Texture2D? indicatorIconTexture;
    private ModConfig config = new();
    private string hoverText = string.Empty;
    private bool reflectionWarned;
    private bool mobilePhoneLoaded;

    public override void Entry(IModHelper helper)
    {
        config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.SaveLoaded += (_, _) => RefreshIndicators();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => ClearLists();
        helper.Events.Display.RenderingHud += OnRenderingHud;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        RegisterMobilePhoneApp();
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm == null)
            return;

        gmcm.Register(
            ModManifest,
            reset: () =>
            {
                config = new ModConfig();
                RefreshIndicators();
            },
            save: () =>
            {
                Helper.WriteConfig(config);
                RefreshIndicators();
            }
        );

        gmcm.AddBoolOption(
            ModManifest,
            getValue: () => config.ShowInfestedLevelIndicators,
            setValue: value =>
            {
                config.ShowInfestedLevelIndicators = value;
                RefreshIndicators();
            },
            name: () => "Show Infested Level Indicators",
            tooltip: () => "Show infested mine levels in the HUD tooltip."
        );

        gmcm.AddBoolOption(
            ModManifest,
            getValue: () => config.ShowMushroomLevelIndicators,
            setValue: value =>
            {
                config.ShowMushroomLevelIndicators = value;
                RefreshIndicators();
            },
            name: () => "Show Mushroom Level Indicators",
            tooltip: () => "Show mushroom mine levels in the HUD tooltip."
        );

        gmcm.AddBoolOption(
            ModManifest,
            getValue: () => config.ShowDinoLevelIndicators,
            setValue: value =>
            {
                config.ShowDinoLevelIndicators = value;
                RefreshIndicators();
            },
            name: () => "Show Dino Level Indicators",
            tooltip: () => "Show prehistoric Skull Cavern levels in the HUD tooltip."
        );
    }

    private void RegisterMobilePhoneApp()
    {
        IMobilePhoneApi? phoneApi = Helper.ModRegistry.GetApi<IMobilePhoneApi>("aedenthorn.MobilePhone");
        if (phoneApi == null)
            return;

        mobilePhoneLoaded = true;

        phoneApi.AddApp(ModManifest.UniqueID, "Mine Levels", ShowMineLevelReport, GetIndicatorIconTexture());
    }

    private void ShowMineLevelReport()
    {
        if (!Context.IsWorldReady)
        {
            Game1.addHUDMessage(new HUDMessage("Mine level indicators are available after loading a save.", HUDMessage.error_type));
            return;
        }

        RefreshIndicators();
        string text = BuildMobileReportText(Game1.player?.deepestMineLevel ?? 0, HasSkullKey());
        Game1.drawObjectDialogue(string.IsNullOrWhiteSpace(text) ? "No mine level indicators are currently enabled." : text);
    }

    private Texture2D GetIndicatorIconTexture()
    {
        return indicatorIconTexture ??= Helper.ModContent.Load<Texture2D>("assets/MobilePhone.png");
    }

    private void EnsureIcon()
    {
        if (icon != null)
            return;

        Texture2D iconTexture = GetIndicatorIconTexture();
        icon = new ClickableTextureComponent(
            name: string.Empty,
            bounds: new Rectangle(GetHudRightEdge() - 54, 290, 48, 48),
            label: string.Empty,
            hoverText: string.Empty,
            texture: iconTexture,
            sourceRect: new Rectangle(0, 0, iconTexture.Width, iconTexture.Height),
            scale: 1f,
            drawShadow: false
        );
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        RefreshIndicators();
    }

    private void RefreshIndicators()
    {
        ClearLists();
        if (!Context.IsWorldReady || (!config.ShowInfestedLevelIndicators && !config.ShowMushroomLevelIndicators && !config.ShowDinoLevelIndicators))
            return;

        int deepestMineLevel = Game1.player?.deepestMineLevel ?? 0;
        int maxLevel = Math.Min(Math.Max(deepestMineLevel, 0), 120);

        if (config.ShowInfestedLevelIndicators && maxLevel >= 1)
            LocateInfestedLevels(maxLevel);

        if (config.ShowMushroomLevelIndicators && maxLevel >= 81)
            LocateMushroomLevels(maxLevel);

        bool hasSkullKey = HasSkullKey();
        if (config.ShowDinoLevelIndicators && hasSkullKey)
            LocateDinoLevels();

        hoverText = BuildHoverText(deepestMineLevel, hasSkullKey);
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!ShouldDrawHudIndicator())
            return;

        EnsureIcon();
        if (icon == null)
            return;

        Point position = new(GetHudRightEdge() - 54, GetHudIconY());
        icon.bounds.X = position.X;
        icon.bounds.Y = position.Y;
        icon.draw(e.SpriteBatch, Color.White, 1f, 0, 0, 0);
    }

    private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
    {
        if (!ShouldDrawHudIndicator() || icon == null || !icon.containsPoint(Game1.getMouseX(), Game1.getMouseY()))
            return;

        IClickableMenu.drawHoverText(e.SpriteBatch, hoverText, Game1.dialogueFont);
    }

    private bool ShouldDrawHudIndicator()
    {
        return Context.IsWorldReady
            && !Game1.eventUp
            && Game1.displayHUD
            && !Game1.game1.takingMapScreenshot
            && Game1.activeClickableMenu == null
            && !string.IsNullOrWhiteSpace(hoverText);
    }

    private void ClearLists()
    {
        infestedLevels.Clear();
        mushroomLevels.Clear();
        dinoLevels.Clear();
        hoverText = string.Empty;
        reflectionWarned = false;
    }

    private string BuildHoverText(int deepest, bool hasSkullKey)
    {
        List<string> sections = new();

        if (config.ShowInfestedLevelIndicators)
        {
            string infestedText = infestedLevels.Any()
                ? FormatWrappedList(infestedLevels.Select(level => $"{level.Level}{(string.IsNullOrEmpty(level.Type) ? string.Empty : " (" + level.Type + ")")}"), Environment.NewLine)
                : "None";
            sections.Add($"Infested Levels:{Environment.NewLine}{infestedText}");
        }

        if (config.ShowMushroomLevelIndicators)
        {
            string mushroomText = deepest > 80
                ? mushroomLevels.Any() ? FormatWrappedList(mushroomLevels.Select(level => level.ToString()), Environment.NewLine) : "None"
                : "Mine Not Sufficiently Explored";
            sections.Add($"Mushroom Levels:{Environment.NewLine}{mushroomText}");
        }

        if (config.ShowDinoLevelIndicators)
        {
            string dinoText;
            if (!hasSkullKey)
                dinoText = "Skull Key Required";
            else if (dinoLevels.Any())
                dinoText = FormatWrappedList(dinoLevels.Select(level => level.ToString()), Environment.NewLine);
            else
                dinoText = $"None through Skull Cavern {MaxSkullCavernDinoLevel}";

            sections.Add($"Dino Levels:{Environment.NewLine}{dinoText}");
        }

        return string.Join(Environment.NewLine + Environment.NewLine, sections);
    }

    private string BuildMobileReportText(int deepest, bool hasSkullKey)
    {
        List<string> rows = new();

        if (config.ShowInfestedLevelIndicators)
        {
            string infestedText = infestedLevels.Any()
                ? FormatWrappedList(infestedLevels.Select(level => $"{level.Level}{(string.IsNullOrEmpty(level.Type) ? string.Empty : " (" + level.Type + ")")}"), "^")
                : "None";
            rows.Add($"Infested Levels: {infestedText}");
        }

        if (config.ShowMushroomLevelIndicators)
        {
            string mushroomText = deepest > 80
                ? mushroomLevels.Any() ? FormatWrappedList(mushroomLevels.Select(level => level.ToString()), "^") : "None"
                : "Mine Not Sufficiently Explored";
            rows.Add($"Mushroom Levels: {mushroomText}");
        }

        if (config.ShowDinoLevelIndicators)
        {
            string dinoText;
            if (!hasSkullKey)
                dinoText = "Skull Key Required";
            else if (dinoLevels.Any())
                dinoText = FormatWrappedList(dinoLevels.Select(level => level.ToString()), "^");
            else
                dinoText = $"None through Skull Cavern {MaxSkullCavernDinoLevel}";

            rows.Add($"Dino Levels: {dinoText}");
        }

        return string.Join("^", rows);
    }

    private static string FormatWrappedList(IEnumerable<string> entries, string rowBreak)
    {
        List<string> rows = new();
        List<string> currentRow = new();
        int rowLength = 0;

        foreach (string entry in entries)
        {
            int nextLength = currentRow.Count == 0 ? entry.Length : rowLength + 2 + entry.Length;
            if (currentRow.Count > 0 && (currentRow.Count >= MaxEntriesPerLine || nextLength > MaxCharactersPerLine))
            {
                rows.Add(string.Join(", ", currentRow));
                currentRow.Clear();
                rowLength = 0;
            }

            currentRow.Add(entry);
            rowLength = currentRow.Count == 1 ? entry.Length : rowLength + 2 + entry.Length;
        }

        if (currentRow.Count > 0)
            rows.Add(string.Join(", ", currentRow));

        return string.Join(rowBreak, rows);
    }

    private void LocateInfestedLevels(int maxLevel)
    {
        for (int level = 1; level <= maxLevel; level++)
        {
            if (level % 5 == 0)
                continue;

            int infestedType = GetInfestedType(TryGetMine(level));
            if (infestedType <= 0)
                continue;

            string type = infestedType switch
            {
                1 => "Slime",
                2 => "Monster",
                _ => string.Empty
            };
            infestedLevels.Add((level, type));
        }
    }

    private int GetInfestedType(MineShaft? mine)
    {
        if (mine == null || mine.Objects == null)
            return 0;

        if (!TryReadMineBool(mine, "netIsSlimeArea", out bool slimeArea))
            return 0;

        if (!TryReadMineBool(mine, "netIsMonsterArea", out bool monsterArea))
            return 0;

        if (slimeArea && monsterArea)
            return int.MaxValue;

        if (slimeArea)
            return 1;

        return monsterArea ? 2 : 0;
    }

    private void LocateMushroomLevels(int maxLevel)
    {
        for (int level = 81; level <= maxLevel; level++)
        {
            if (level % 5 == 0)
                continue;

            MineShaft? mine = TryGetMine(level);
            if (mine != null && IsMushroomLevel(mine))
                mushroomLevels.Add(level);
        }
    }

    private bool IsMushroomLevel(MineShaft mine)
    {
        if (mine.Objects == null)
            return false;

        if (TryReadMineBool(mine, "netIsSlimeArea", out bool slimeArea) && slimeArea)
            return false;

        if (TryReadMineBool(mine, "netIsMonsterArea", out bool monsterArea) && monsterArea)
            return false;

        if (TryReadMineBool(mine, "rainbowLights", out bool rainbowLights) && rainbowLights)
            return false;

        return HasAnyMushroom(mine.Objects.Pairs);
    }

    private static bool HasAnyMushroom(IEnumerable<KeyValuePair<Vector2, StardewObject>> objects)
    {
        foreach (KeyValuePair<Vector2, StardewObject> pair in objects)
        {
            string displayName = pair.Value?.DisplayName ?? string.Empty;
            if (displayName.Contains("Mushroom", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private void LocateDinoLevels()
    {
        for (int skullLevel = 1; skullLevel <= MaxSkullCavernDinoLevel; skullLevel++)
        {
            MineShaft? mine = TryGetMine(skullLevel + 120);
            if (mine != null && IsDinoLevel(mine))
                dinoLevels.Add(skullLevel);
        }
    }

    private static bool HasSkullKey()
    {
        return Game1.player?.hasSkullKey == true;
    }

    private bool IsDinoLevel(MineShaft mine)
    {
        if (TryReadMineBool(mine, "netIsDinoArea", out bool netDinoArea) && netDinoArea)
            return true;

        if (TryReadMineBool(mine, "isDinoArea", out bool dinoArea) && dinoArea)
            return true;

        return mine.characters.Any(character => character.GetType().Name.Contains("Dino", StringComparison.OrdinalIgnoreCase));
    }

    private MineShaft? TryGetMine(int level)
    {
        try
        {
            return MineShaft.GetMine($"UndergroundMine{level}");
        }
        catch
        {
            return null;
        }
    }

    private bool TryReadMineBool(MineShaft mine, string memberName, out bool value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        Type mineType = mine.GetType();

        FieldInfo? field = mineType.GetField(memberName, flags);
        if (field != null && TryConvertToBool(field.GetValue(mine), out value))
            return true;

        PropertyInfo? property = mineType.GetProperty(memberName, flags);
        if (property != null && TryConvertToBool(property.GetValue(mine), out value))
            return true;

        value = false;
        if (!reflectionWarned && (memberName == "netIsSlimeArea" || memberName == "netIsMonsterArea"))
        {
            reflectionWarned = true;
            Monitor.Log(
                "Could not access MineShaft fields used to detect infested floors (netIsSlimeArea/netIsMonsterArea). If this persists, the mod needs updated 1.6 detection logic.",
                LogLevel.Warn
            );
        }

        return false;
    }

    private static bool TryConvertToBool(object? source, out bool value)
    {
        switch (source)
        {
            case bool boolValue:
                value = boolValue;
                return true;
            case NetBool netBool:
                value = netBool.Value;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private int GetHudIconY()
    {
        int y = Game1.options.zoomButtons ? 290 : 260;
        return mobilePhoneLoaded ? y + 52 : y;
    }

    private static int GetHudRightEdge()
    {
        Viewport viewport = Game1.graphics.GraphicsDevice.Viewport;
        Rectangle titleSafeArea = viewport.TitleSafeArea;
        if (Game1.isOutdoorMapSmallerThanViewport())
        {
            int mapWidth = Game1.currentLocation.map.Layers[0].LayerWidth * Game1.tileSize;
            return titleSafeArea.Right - (titleSafeArea.Right - mapWidth) / 2;
        }

        return titleSafeArea.Right;
    }
}
