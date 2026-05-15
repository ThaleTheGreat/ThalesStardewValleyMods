using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StardewObject = StardewValley.Object;

namespace MineLevelIndicators;

public sealed class ModEntry : Mod
{
    private readonly List<(int Level, string Type)> infestedLevels = new();
    private readonly List<int> mushroomLevels = new();
    private ClickableTextureComponent icon;
    private string hoverText = string.Empty;
    private bool reflectionWarned;

    public override void Entry(IModHelper helper)
    {
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.SaveLoaded += (_, _) => ClearLists();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => ClearLists();
        helper.Events.Display.RenderingHud += OnRenderingHud;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    private void EnsureIcon()
    {
        if (icon != null || Game1.objectSpriteSheet == null)
            return;

        icon = new ClickableTextureComponent(
            name: string.Empty,
            bounds: new Rectangle(GetHudRightEdge() - 134, 290, 40, 40),
            label: string.Empty,
            hoverText: string.Empty,
            texture: Game1.objectSpriteSheet,
            sourceRect: new Rectangle(351, 496, 18, 14),
            scale: 3f,
            drawShadow: false
        );
    }

    private void OnDayStarted(object sender, DayStartedEventArgs e)
    {
        ClearLists();

        int deepestMineLevel = Game1.player?.deepestMineLevel ?? 0;
        int maxLevel = Math.Min(Math.Max(deepestMineLevel, 0), 120);

        if (maxLevel >= 1)
            LocateInfestedLevels(maxLevel);

        if (maxLevel >= 81)
            LocateMushroomLevels(maxLevel);

        hoverText = BuildHoverText(deepestMineLevel);
    }

    private void OnRenderedHud(object sender, RenderedHudEventArgs e)
    {
        if (Game1.eventUp)
            return;

        EnsureIcon();
        if (icon == null)
            return;

        Point position = new(GetHudRightEdge() - 50, Game1.options.zoomButtons ? 290 : 260);
        icon.bounds.X = position.X;
        icon.bounds.Y = position.Y;
        icon.draw(e.SpriteBatch, Color.White, 1f, 0, 0, 0);
    }

    private void OnRenderingHud(object sender, RenderingHudEventArgs e)
    {
        if (icon == null || !icon.containsPoint(Game1.getMouseX(), Game1.getMouseY()))
            return;

        IClickableMenu.drawHoverText(e.SpriteBatch, hoverText, Game1.dialogueFont);
    }

    private void ClearLists()
    {
        infestedLevels.Clear();
        mushroomLevels.Clear();
        hoverText = string.Empty;
        reflectionWarned = false;
    }

    private string BuildHoverText(int deepest)
    {
        string infestedText = infestedLevels.Any()
            ? string.Join(Environment.NewLine, infestedLevels.Select(level => $"{level.Level}{(string.IsNullOrEmpty(level.Type) ? string.Empty : " - " + level.Type)}"))
            : "None";

        string mushroomText = deepest > 80
            ? mushroomLevels.Any() ? string.Join(Environment.NewLine, mushroomLevels.Select(level => level.ToString())) : "None"
            : "Mine Not Sufficiently Explored";

        return $"Infested Levels:\n{infestedText}\n\nMushroom Levels:\n{mushroomText}";
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

    private int GetInfestedType(MineShaft mine)
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

            MineShaft mine = TryGetMine(level);
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

    private MineShaft TryGetMine(int level)
    {
        try
        {
            return MineShaft.GetMine($"UndergroundMine{level}");
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to load mine level {level}: {ex.Message}", LogLevel.Trace);
            return null;
        }
    }

    private bool TryReadMineBool(MineShaft mine, string fieldName, out bool value)
    {
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo field = mine.GetType().GetField(fieldName, flags);

        if (field != null)
        {
            object fieldValue = field.GetValue(mine);
            switch (fieldValue)
            {
                case bool boolValue:
                    value = boolValue;
                    return true;

                case NetBool netBool:
                    value = netBool.Value;
                    return true;
            }
        }

        value = false;
        if (!reflectionWarned && (fieldName == "netIsSlimeArea" || fieldName == "netIsMonsterArea"))
        {
            reflectionWarned = true;
            Monitor.Log("Could not access MineShaft fields used to detect infested floors (netIsSlimeArea/netIsMonsterArea). If this persists, the mod needs updated 1.6 detection logic.", LogLevel.Warn);
        }

        return false;
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
