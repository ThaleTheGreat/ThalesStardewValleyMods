using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Pathfinding;

namespace WaitMenu;

public sealed class ModEntry : Mod
{
    private ModConfig config = new();
    private GamePadState previousPadState;
    private bool hasPreviousPadState;

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
    }

    private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api == null)
            return;

        api.Register(
            this.ModManifest,
            reset: () =>
            {
                this.config = new ModConfig();
                this.Helper.WriteConfig(this.config);
            },
            save: () => this.Helper.WriteConfig(this.config));

        api.AddKeybindList(
            this.ModManifest,
            getValue: () => this.config.OpenMenuKey,
            setValue: value => this.config.OpenMenuKey = value,
            name: () => "Open menu keybind",
            tooltip: () => "Keybind to open the wait menu. Default is Ctrl+W.");

        api.AddBoolOption(
            this.ModManifest,
            getValue: () => this.config.AllowDuringFestival,
            setValue: value => this.config.AllowDuringFestival = value,
            name: () => "Allow during festivals",
            tooltip: () => "If disabled, the wait menu won't open during festivals.");

        api.AddBoolOption(
            this.ModManifest,
            getValue: () => this.config.AllowDuringEvents,
            setValue: value => this.config.AllowDuringEvents = value,
            name: () => "Allow during events",
            tooltip: () => "If disabled, the wait menu won't open during cutscenes/events.");

        api.AddNumberOption(
            this.ModManifest,
            getValue: () => this.config.MaxWaitMinutes,
            setValue: value => this.config.MaxWaitMinutes = value,
            name: () => "Max wait minutes",
            tooltip: () => "Safety clamp for max minutes advanced per wait action.",
            min: 30,
            max: 240,
            interval: 30);

        api.AddBoolOption(
            this.ModManifest,
            getValue: () => this.config.StabilizeAfterTeleport,
            setValue: value => this.config.StabilizeAfterTeleport = value,
            name: () => "Stabilize after NPC teleport",
            tooltip: () => "Clears stale NPC pathing/animation state after placing NPCs at schedule destinations.");
    }

    private void OnUpdateTicked(object sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        bool controllerPressed = this.UpdateControllerTrigger();
        if (!this.config.OpenMenuKey.JustPressed() && !controllerPressed)
            return;

        if (Game1.activeClickableMenu != null || !Game1.player.CanMove)
            return;

        if (Game1.eventUp && !this.config.AllowDuringEvents)
            return;

        if (Game1.isFestival() && !this.config.AllowDuringFestival)
            return;

        Game1.activeClickableMenu = new WaitSelectMenu(this.AdvanceTime, this.config.MaxWaitMinutes);
    }

    private void AdvanceTime(int minutes)
    {
        if (!Context.IsWorldReady)
            return;

        if (Game1.timeOfDay >= 2600)
        {
            Game1.addHUDMessage(new HUDMessage("It's too late to wait.", HUDMessage.error_type));
            return;
        }

        int maxWaitMinutes = Math.Clamp(this.config.MaxWaitMinutes, 30, 240);
        minutes = Math.Clamp(minutes, 30, maxWaitMinutes);
        minutes -= minutes % 10;

        int ticks = minutes / 10;
        for (int index = 0; index < ticks; index++)
        {
            try
            {
                Game1.performTenMinuteClockUpdate();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"WaitMenu: error while advancing time at {Game1.timeOfDay}: {ex}", LogLevel.Error);
                Game1.addHUDMessage(new HUDMessage("WaitMenu: An NPC schedule error occurred while advancing time. Check SMAPI log.", HUDMessage.error_type));
                return;
            }
        }

        this.SnapAllNpcsToScheduleNow(this.config.StabilizeAfterTeleport);
        Game1.playSound("smallSelect");
    }

    private void SnapAllNpcsToScheduleNow(bool stabilizeAfterTeleport)
    {
        try
        {
            int moved = 0;

            foreach (NPC npc in this.GetAllNpcsSnapshot())
            {
                if (npc == null || npc.IsMonster)
                    continue;

                try
                {
                    npc.checkSchedule(Game1.timeOfDay);
                }
                catch
                {
                }

                if (TryInvokeNpcWarpMethod(npc, Game1.timeOfDay))
                {
                    moved++;
                    NormalizeNpcPostWarp(npc);
                }
                else if (TryWarpFromScheduleDictionary(npc, Game1.timeOfDay, stabilizeAfterTeleport))
                {
                    moved++;
                    NormalizeNpcPostWarp(npc);
                }
            }

        }
        catch (Exception ex)
        {
            this.Monitor.Log($"WaitMenu: failed to snap NPCs to schedule: {ex}", LogLevel.Warn);
        }
    }

    private List<NPC> GetAllNpcsSnapshot()
    {
        HashSet<NPC> seen = new();
        List<NPC> result = new();

        try
        {
            MethodInfo method = typeof(Utility).GetMethod("getAllCharacters", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method != null)
            {
                object raw = method.Invoke(null, null);
                if (raw is IEnumerable enumerable)
                {
                    foreach (object entry in enumerable.Cast<object>().ToList())
                    {
                        if (entry is NPC npc && seen.Add(npc))
                            result.Add(npc);
                    }
                }
            }
        }
        catch
        {
            result.Clear();
            seen.Clear();
        }

        if (result.Count > 0)
            return result;

        try
        {
            foreach (GameLocation location in Game1.locations.ToList())
            {
                foreach (NPC npc in location.characters.ToList())
                {
                    if (npc != null && seen.Add(npc))
                        result.Add(npc);
                }
            }
        }
        catch
        {
            // Return the stable partial snapshot, if any.
        }

        return result;
    }

    private static bool TryInvokeNpcWarpMethod(NPC npc, int timeOfDay)
    {
        string[] methodNames =
        {
            "warpToScheduleLocation",
            "warpToScheduleLocationAndPosition",
            "WarpToScheduleLocation",
            "WarpToScheduleLocationAndPosition"
        };

        foreach (string name in methodNames)
        {
            MethodInfo method = npc.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            try
            {
                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                {
                    method.Invoke(npc, new object[] { timeOfDay });
                    return true;
                }

                if (parameters.Length == 0)
                {
                    method.Invoke(npc, null);
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    private static bool TryWarpFromScheduleDictionary(NPC npc, int timeOfDay, bool stabilizeAfterTeleport)
    {
        PropertyInfo scheduleProperty = npc.GetType().GetProperty("Schedule", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (scheduleProperty == null)
            return false;

        object scheduleObject;
        try
        {
            scheduleObject = scheduleProperty.GetValue(npc);
        }
        catch
        {
            return false;
        }

        if (scheduleObject is not IDictionary dictionary || dictionary.Count == 0)
            return false;

        int selectedTime = int.MinValue;
        object selectedEntry = null;
        foreach (DictionaryEntry entry in dictionary)
        {
            if (entry.Key is int key && key <= timeOfDay && key > selectedTime)
            {
                selectedTime = key;
                selectedEntry = entry.Value;
            }
        }

        if (selectedEntry == null)
            return false;

        FieldInfo routeField = selectedEntry.GetType().GetField("route", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? selectedEntry.GetType().GetField("Route", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        FieldInfo facingField = selectedEntry.GetType().GetField("facingDirection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? selectedEntry.GetType().GetField("FacingDirection", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        Point? destination = TryGetPointMember(selectedEntry);
        object routeObject = routeField?.GetValue(selectedEntry);
        Stack<Point> routeStack = routeObject as Stack<Point>;

        if (!destination.HasValue && routeObject is IEnumerable routeEnumerable)
        {
            foreach (object routePoint in routeEnumerable)
            {
                if (routePoint is Point point)
                    destination = point;
            }
        }

        if (!destination.HasValue)
            return false;

        int facingDirection = 2;
        try
        {
            if (facingField?.GetValue(selectedEntry) is int facing)
                facingDirection = facing;
        }
        catch
        {
            // keep default south-facing fallback
        }

        string locationName = TryGetLocationName(selectedEntry);
        GameLocation targetLocation = npc.currentLocation;
        if (!string.IsNullOrWhiteSpace(locationName))
        {
            GameLocation location = Game1.getLocationFromName(locationName);
            if (location != null)
                targetLocation = location;
        }

        if (targetLocation == null)
            return false;

        if (npc.currentLocation != targetLocation)
        {
            try
            {
                npc.currentLocation?.characters?.Remove(npc);
            }
            catch
            {
            }

            try
            {
                if (!targetLocation.characters.Contains(npc))
                    targetLocation.characters.Add(npc);
            }
            catch
            {
            }

            npc.currentLocation = targetLocation;
        }

        npc.controller = null;
        npc.Halt();
        npc.ignoreMovementAnimation = false;

        if (stabilizeAfterTeleport)
        {
            TrySetField(npc, "temporaryController", null);

            try
            {
                if (npc.Sprite != null)
                {
                    npc.Sprite.StopAnimation();
                    npc.Sprite.CurrentAnimation = null;
                }
            }
            catch
            {
            }

            try
            {
                routeStack?.Clear();
            }
            catch
            {
            }
        }

        npc.faceDirection(facingDirection);
        npc.setTileLocation(new Vector2(destination.Value.X, destination.Value.Y));
        NormalizeNpcPostWarp(npc);
        return true;
    }

    private static Point? TryGetPointMember(object scheduleEntry)
    {
        string[] names =
        {
            "endPoint", "EndPoint",
            "destination", "Destination",
            "target", "Target",
            "point", "Point"
        };

        foreach (string name in names)
        {
            FieldInfo field = scheduleEntry.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(Point))
            {
                try
                {
                    return (Point)field.GetValue(scheduleEntry);
                }
                catch
                {
                    }
            }
        }

        foreach (string name in names)
        {
            PropertyInfo property = scheduleEntry.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.CanRead && property.PropertyType == typeof(Point))
            {
                try
                {
                    return (Point)property.GetValue(scheduleEntry);
                }
                catch
                {
                    }
            }
        }

        return null;
    }

    private static string TryGetLocationName(object scheduleEntry)
    {
        foreach (FieldInfo field in scheduleEntry.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.FieldType != typeof(string))
                continue;

            string name = field.Name.ToLowerInvariant();
            if (!name.Contains("location") || (!name.Contains("name") && !name.Contains("target") && !name.Contains("end")))
                continue;

            try
            {
                string value = field.GetValue(scheduleEntry) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch
            {
            }
        }

        foreach (PropertyInfo property in scheduleEntry.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (property.PropertyType != typeof(string) || !property.CanRead)
                continue;

            string name = property.Name.ToLowerInvariant();
            if (!name.Contains("location") || (!name.Contains("name") && !name.Contains("target") && !name.Contains("end")))
                continue;

            try
            {
                string value = property.GetValue(scheduleEntry) as string;
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
            catch
            {
            }
        }

        return null;
    }

    private static void TrySetField(object obj, string fieldName, object value)
    {
        try
        {
            FieldInfo field = obj.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field == null)
                return;

            field.SetValue(obj, value);
        }
        catch
        {
            // best effort
        }
    }

    private bool UpdateControllerTrigger()
    {
        GamePadState state = GamePad.GetState(PlayerIndex.One);
        bool result = this.hasPreviousPadState && BothSticksJustPressed(this.previousPadState, state);
        this.previousPadState = state;
        this.hasPreviousPadState = true;
        return result;
    }

    private static bool BothSticksJustPressed(GamePadState previous, GamePadState current)
    {
        return current.IsConnected
            && !previous.IsButtonDown(Buttons.LeftStick)
            && current.IsButtonDown(Buttons.LeftStick)
            && !previous.IsButtonDown(Buttons.RightStick)
            && current.IsButtonDown(Buttons.RightStick);
    }

    private static void NormalizeNpcPostWarp(NPC npc)
    {
        try
        {
            npc.ignoreMovementAnimation = false;
        }
        catch
        {
            // best effort
        }

        try
        {
            npc.movementPause = 0;
        }
        catch
        {
            // best effort
        }

        TrySetField(npc, "temporaryController", null);

        try
        {
            if (npc.Sprite == null)
                return;

            npc.Sprite.StopAnimation();
            npc.Sprite.CurrentAnimation = null;
            npc.Sprite.UpdateSourceRect();
        }
        catch
        {
            // best effort
        }
    }
}
