using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Characters;
using StardewValley.GameData.Powers;
using StardewValley.Menus;
using StardewValley.Tools;

namespace ThaleTheGreat.RoadKill;

public sealed class ModEntry : Mod
{
    private const string AnimalHusbandryModId = "DIGUS.ANIMALHUSBANDRYMOD";
    private const string TractorModId = "Pathoschild.TractorMod";
    private const string WalletToolsId = "ThaleTheGreat.WalletTools";
    private const string WalletToolsForAnimalHusbandryId = "ThaleTheGreat.WalletToolsForAnimalHusbandry";
    private const string WalletToolsForTractorModId = "ThaleTheGreat.WalletToolsForTractorMod";
    private const string TractorManagerTypeName = "Pathoschild.Stardew.TractorMod.Framework.TractorManager";
    private const string WalletTractorModEntryTypeName = "ThaleTheGreat.WalletToolsForTractorMod.ModEntry";
    private const string WalletAnimalHusbandryModEntryTypeName = "ThaleTheGreat.WalletToolsForAnimalHusbandry.ModEntry";
    private const string MeatToolItemId = "DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolQualifiedItemId = "(T)DIGUS.ANIMALHUSBANDRYMOD.MeatCleaver";
    private const string MeatToolModDataKey = "DIGUS.ANIMALHUSBANDRYMOD/MeatCleaver";
    private const string WalletMeatToolKind = "AnimalHusbandryMeatTool";
    private const string WalletMeatToolPowerId = "ThaleTheGreat.WalletTools_AnimalHusbandryMeatTool";
    private const string TractorDataKey = "Pathoschild.TractorMod";
    private const int IconSize = 64;

    private static ModEntry? Instance;

    private Harmony Harmony = null!;
    private ModConfig Config = new();
    private MethodInfo? TractorUpdateAttachmentEffects;
    private FieldInfo? TractorGetDistanceField;
    private MethodInfo? TractorTemporaryInteractionMethod;
    private bool TractorPatchApplied;
    private bool WalletSelectorPatchApplied;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Config = helper.ReadConfig<ModConfig>();
        Harmony = new Harmony(ModManifest.UniqueID);

        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        PatchTractorMod();
        PatchWalletTractorSelectorIfAvailable();
    }

    private void PatchTractorMod()
    {
        if (TractorPatchApplied)
            return;

        if (!Helper.ModRegistry.IsLoaded(AnimalHusbandryModId) || !Helper.ModRegistry.IsLoaded(TractorModId))
            return;

        Type? tractorManagerType = AccessTools.TypeByName(TractorManagerTypeName);
        TractorUpdateAttachmentEffects = AccessTools.Method(tractorManagerType, "UpdateAttachmentEffects");
        TractorGetDistanceField = AccessTools.Field(tractorManagerType, "GetDistance");
        TractorTemporaryInteractionMethod = AccessTools.Method(tractorManagerType, "TemporarilyFakeInteraction", new[] { typeof(Action) });
        MethodInfo? postfix = AccessTools.Method(typeof(ModEntry), nameof(AfterTractorUpdateAttachmentEffects));

        if (TractorUpdateAttachmentEffects is null || TractorGetDistanceField is null || postfix is null)
        {
            Monitor.Log("Could not find Tractor Mod's attachment internals. Road Kill was not applied.", LogLevel.Warn);
            return;
        }

        Harmony.Patch(TractorUpdateAttachmentEffects, postfix: new HarmonyMethod(postfix));
        TractorPatchApplied = true;
        DebugLog("Patched Tractor Mod attachment pass.");
    }

    private void PatchWalletTractorSelectorIfAvailable()
    {
        if (WalletSelectorPatchApplied || !Config.EnableWalletTractorIntegration)
            return;

        if (!Helper.ModRegistry.IsLoaded(WalletToolsId)
            || !Helper.ModRegistry.IsLoaded(WalletToolsForAnimalHusbandryId)
            || !Helper.ModRegistry.IsLoaded(WalletToolsForTractorModId))
            return;

        Type? walletTractorType = AccessTools.TypeByName(WalletTractorModEntryTypeName);
        MethodInfo? getChoices = AccessTools.Method(walletTractorType, "GetSelectorChoices");
        MethodInfo? normalize = AccessTools.Method(walletTractorType, "NormalizeSelectedWalletTool");
        MethodInfo? drawOverlay = AccessTools.Method(walletTractorType, "DrawSelectorOverlay", new[] { typeof(SpriteBatch) });

        MethodInfo? choicesPostfix = AccessTools.Method(typeof(ModEntry), nameof(AfterWalletTractorGetSelectorChoices));
        MethodInfo? normalizePrefix = AccessTools.Method(typeof(ModEntry), nameof(BeforeWalletTractorNormalizeSelectedWalletTool));
        MethodInfo? normalizePostfix = AccessTools.Method(typeof(ModEntry), nameof(AfterWalletTractorNormalizeSelectedWalletTool));
        MethodInfo? drawPostfix = AccessTools.Method(typeof(ModEntry), nameof(AfterWalletTractorDrawSelectorOverlay));

        if (getChoices is null || normalize is null || drawOverlay is null || choicesPostfix is null || normalizePrefix is null || normalizePostfix is null || drawPostfix is null)
        {
            Monitor.Log("Could not find Wallet Tools for Tractor Mod selector internals. The optional wallet selector integration was not applied.", LogLevel.Warn);
            return;
        }

        Harmony.Patch(getChoices, postfix: new HarmonyMethod(choicesPostfix));
        Harmony.Patch(normalize, prefix: new HarmonyMethod(normalizePrefix), postfix: new HarmonyMethod(normalizePostfix));
        Harmony.Patch(drawOverlay, postfix: new HarmonyMethod(drawPostfix));
        WalletSelectorPatchApplied = true;
        DebugLog("Patched Wallet Tools for Tractor Mod selector.");
    }

    private static void AfterTractorUpdateAttachmentEffects(object __instance)
    {
        Instance?.ApplyRoadKillEffects(__instance);
    }

    private void ApplyRoadKillEffects(object tractorManager)
    {
        if (!Context.IsWorldReady || Game1.activeClickableMenu is not null || !IsCurrentPlayerRidingTractor())
            return;

        Tool? tool = GetActiveMeatToolForTractor();
        if (tool is null)
            return;

        Farmer player = Game1.player;
        GameLocation location = Game1.currentLocation;
        int distance = GetTractorDistance(tractorManager);
        Vector2 origin = player.Tile;
        List<Vector2> targetTiles = GetAnimalTargetTiles(location, origin, distance).ToList();
        if (targetTiles.Count == 0)
            return;

        RunWithTractorInteraction(tractorManager, () =>
        {
            foreach (Vector2 tile in targetTiles)
                UseMeatToolOnTile(tool, location, player, origin, tile);
        });
    }

    private Tool? GetActiveMeatToolForTractor()
    {
        string? selectedWalletTool = GetWalletTractorSelectedKind();
        if (IsWalletMeatToolKind(selectedWalletTool))
        {
            if (Config.EnableWalletTractorIntegration && IsFullWalletIntegrationLoaded())
                return TryCreateWalletAnimalHusbandryTool();

            return null;
        }

        if (!string.IsNullOrWhiteSpace(selectedWalletTool))
            return null;

        if (!Config.EnableDirectTractorUse)
            return null;

        return Game1.player.CurrentItem is Tool tool && IsMeatTool(tool)
            ? tool
            : null;
    }

    private int GetTractorDistance(object tractorManager)
    {
        try
        {
            if (TractorGetDistanceField?.GetValue(tractorManager) is Func<int> getDistance)
                return Math.Max(0, getDistance());
        }
        catch (Exception ex)
        {
            DebugLog($"Could not read Tractor Mod distance: {ex.GetBaseException().Message}");
        }

        return 0;
    }

    private static IEnumerable<Vector2> GetAnimalTargetTiles(GameLocation location, Vector2 origin, int distance)
    {
        HashSet<string> seenTiles = new(StringComparer.Ordinal);

        foreach (FarmAnimal animal in location.animals.Values)
        {
            if (animal.currentLocation != location)
                continue;

            Vector2 tile = new((int)animal.Tile.X, (int)animal.Tile.Y);
            if (Math.Abs(tile.X - origin.X) > distance || Math.Abs(tile.Y - origin.Y) > distance)
                continue;

            string key = $"{tile.X},{tile.Y}";
            if (seenTiles.Add(key))
                yield return tile;
        }
    }

    private void RunWithTractorInteraction(object tractorManager, Action action)
    {
        if (TractorTemporaryInteractionMethod is not null)
        {
            try
            {
                TractorTemporaryInteractionMethod.Invoke(tractorManager, new object[] { action });
                return;
            }
            catch (TargetInvocationException ex) when (ex.InnerException is not null)
            {
                throw ex.InnerException;
            }
            catch (Exception ex)
            {
                DebugLog($"Could not use Tractor Mod's temporary interaction wrapper: {ex.GetBaseException().Message}");
            }
        }

        RunWithLocalInteraction(action);
    }

    private static void RunWithLocalInteraction(Action action)
    {
        Farmer player = Game1.player;
        Vector2 position = player.Position;
        int facingDirection = player.FacingDirection;
        int currentToolIndex = player.CurrentToolIndex;
        bool canMove = player.canMove;
        float stamina = player.stamina;

        try
        {
            action();
        }
        finally
        {
            player.Position = position;
            player.FacingDirection = facingDirection;
            player.CurrentToolIndex = Math.Max(0, Math.Min(currentToolIndex, Math.Max(0, player.Items.Count - 1)));
            player.canMove = canMove;
            player.stamina = stamina;
        }
    }

    private static void UseMeatToolOnTile(Tool tool, GameLocation location, Farmer player, Vector2 origin, Vector2 tile)
    {
        GetRadialAdjacentTile(origin, tile, player.FacingDirection, out Vector2 adjacentTile, out int facingDirection);
        Vector2 useAt = GetToolPixelPosition(tile);

        player.Position = adjacentTile * Game1.tileSize;
        player.FacingDirection = facingDirection;
        player.lastClick = useAt;
        tool.lastUser = player;

        int x = (int)useAt.X;
        int y = (int)useAt.Y;
        tool.beginUsing(location, x, y, player);
        tool.beginUsing(location, x, y, player);
        tool.DoFunction(location, x, y, 0, player);
    }

    private static Vector2 GetToolPixelPosition(Vector2 tile)
    {
        return (tile * Game1.tileSize) + new Vector2(Game1.tileSize / 2f);
    }

    private static void GetRadialAdjacentTile(Vector2 origin, Vector2 tile, int fallbackFacingDirection, out Vector2 adjacent, out int facingDirection)
    {
        if (tile == origin)
        {
            facingDirection = fallbackFacingDirection;
            adjacent = tile;
            return;
        }

        facingDirection = Utility.getDirectionFromChange(tile, origin);
        adjacent = facingDirection switch
        {
            Game1.up => new Vector2(tile.X, tile.Y + 1),
            Game1.down => new Vector2(tile.X, tile.Y - 1),
            Game1.left => new Vector2(tile.X + 1, tile.Y),
            Game1.right => new Vector2(tile.X - 1, tile.Y),
            _ => tile
        };
    }

    private Tool? TryCreateWalletAnimalHusbandryTool()
    {
        object? walletAnimalHusbandryMod = GetWalletAnimalHusbandryModEntry();
        if (walletAnimalHusbandryMod is null)
            return null;

        try
        {
            MethodInfo? getStoredTool = AccessTools.Method(walletAnimalHusbandryMod.GetType(), "GetStoredTool", new[] { typeof(Farmer) });
            if (getStoredTool?.Invoke(walletAnimalHusbandryMod, new object?[] { Game1.player }) is not object state)
                return null;

            MethodInfo? createTool = AccessTools.Method(state.GetType(), "CreateTool", new[] { typeof(IMonitor) });
            if (createTool?.Invoke(state, new object?[] { Monitor }) is Tool tool && IsMeatTool(tool))
                return tool;
        }
        catch (Exception ex)
        {
            DebugLog($"Could not create Animal Husbandry wallet tool: {ex.GetBaseException().Message}");
        }

        return null;
    }

    private bool HasStoredWalletAnimalHusbandryTool()
    {
        if (!Config.EnableWalletTractorIntegration || !IsFullWalletIntegrationLoaded())
            return false;

        return TryCreateWalletAnimalHusbandryTool() is not null;
    }

    private bool IsFullWalletIntegrationLoaded()
    {
        return Helper.ModRegistry.IsLoaded(WalletToolsId)
            && Helper.ModRegistry.IsLoaded(WalletToolsForAnimalHusbandryId)
            && Helper.ModRegistry.IsLoaded(WalletToolsForTractorModId);
    }

    private static object? GetWalletAnimalHusbandryModEntry()
    {
        Type? type = AccessTools.TypeByName(WalletAnimalHusbandryModEntryTypeName);
        return AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static object? GetWalletTractorModEntry()
    {
        Type? type = AccessTools.TypeByName(WalletTractorModEntryTypeName);
        return AccessTools.Field(type, "Instance")?.GetValue(null);
    }

    private static string? GetWalletTractorSelectedKind()
    {
        object? walletTractorMod = GetWalletTractorModEntry();
        return walletTractorMod is null
            ? null
            : AccessTools.Field(walletTractorMod.GetType(), "SelectedWalletToolKind")?.GetValue(walletTractorMod) as string;
    }

    private static void SetWalletTractorSelectedKind(object walletTractorMod, string? kind)
    {
        AccessTools.Field(walletTractorMod.GetType(), "SelectedWalletToolKind")?.SetValue(walletTractorMod, kind);
    }

    private static bool IsWalletMeatToolKind(string? kind)
    {
        return string.Equals(kind, WalletMeatToolKind, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMeatTool(Item? item)
    {
        if (item is not Tool tool)
            return false;

        if (tool.modData.ContainsKey(MeatToolModDataKey))
            return true;

        string text = string.Join("\n", tool.ItemId, tool.QualifiedItemId, tool.Name, tool.DisplayName);
        return text.Contains(MeatToolItemId, StringComparison.OrdinalIgnoreCase)
            || text.Contains(MeatToolQualifiedItemId, StringComparison.OrdinalIgnoreCase)
            || text.Contains("MeatCleaver", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Meat Cleaver", StringComparison.OrdinalIgnoreCase)
            || text.Contains("Meat Wand", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCurrentPlayerRidingTractor()
    {
        return Game1.player?.mount is Horse horse && horse.modData.ContainsKey(TractorDataKey);
    }

    private static void AfterWalletTractorGetSelectorChoices(ref IEnumerable<string?> __result)
    {
        if (Instance is not null)
            __result = Instance.AppendWalletMeatToolChoice(__result);
    }

    private IEnumerable<string?> AppendWalletMeatToolChoice(IEnumerable<string?> choices)
    {
        bool found = false;
        foreach (string? choice in choices)
        {
            if (IsWalletMeatToolKind(choice))
                found = true;

            yield return choice;
        }

        if (!found && HasStoredWalletAnimalHusbandryTool())
            yield return WalletMeatToolKind;
    }

    private static void BeforeWalletTractorNormalizeSelectedWalletTool(object __instance, ref string? __state)
    {
        __state = AccessTools.Field(__instance.GetType(), "SelectedWalletToolKind")?.GetValue(__instance) as string;
    }

    private static void AfterWalletTractorNormalizeSelectedWalletTool(object __instance, string? __state)
    {
        if (Instance is null || !IsWalletMeatToolKind(__state) || !Instance.HasStoredWalletAnimalHusbandryTool())
            return;

        SetWalletTractorSelectedKind(__instance, WalletMeatToolKind);
    }

    private static void AfterWalletTractorDrawSelectorOverlay(object __instance, SpriteBatch spriteBatch)
    {
        Instance?.DrawWalletMeatToolOverlay(__instance, spriteBatch);
    }

    private void DrawWalletMeatToolOverlay(object walletTractorMod, SpriteBatch spriteBatch)
    {
        if (!IsWalletMeatToolKind(GetWalletTractorSelectedKind()) || !HasStoredWalletAnimalHusbandryTool())
            return;

        try
        {
            MethodInfo? getBounds = AccessTools.Method(walletTractorMod.GetType(), "GetSelectorOverlayBounds");
            if (getBounds?.Invoke(walletTractorMod, null) is not Rectangle box)
                return;

            if (TryDrawPowerIcon(spriteBatch, box))
                return;

            Tool? tool = TryCreateWalletAnimalHusbandryTool();
            if (tool is not null)
                DrawItemIcon(spriteBatch, tool, GetCenteredIconPosition(box));
        }
        catch (Exception ex)
        {
            DebugLog($"Could not draw Animal Husbandry wallet tractor overlay icon: {ex.GetBaseException().Message}");
        }
    }

    private bool TryDrawPowerIcon(SpriteBatch spriteBatch, Rectangle box)
    {
        try
        {
            Dictionary<string, PowersData> powers = Game1.content.Load<Dictionary<string, PowersData>>("Data/Powers");
            if (!powers.TryGetValue(WalletMeatToolPowerId, out PowersData? power) || string.IsNullOrWhiteSpace(power.TexturePath))
                return false;

            Texture2D texture = Game1.content.Load<Texture2D>(power.TexturePath);
            Rectangle destination = GetCenteredIconBounds(box);
            Rectangle source = new(power.TexturePosition.X, power.TexturePosition.Y, 16, 16);
            spriteBatch.Draw(texture, destination, source, Color.White, 0f, Vector2.Zero, SpriteEffects.None, 0.865f);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog($"Could not draw Animal Husbandry power icon: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static Rectangle GetCenteredIconBounds(Rectangle box)
    {
        int iconSize = Math.Min(IconSize, Math.Min(box.Width, box.Height));
        return new Rectangle(box.X + (box.Width - iconSize) / 2, box.Y + (box.Height - iconSize) / 2, iconSize, iconSize);
    }

    private static Vector2 GetCenteredIconPosition(Rectangle box)
    {
        Rectangle bounds = GetCenteredIconBounds(box);
        return new Vector2(bounds.X, bounds.Y);
    }

    private void DrawItemIcon(SpriteBatch spriteBatch, Item item, Vector2 position)
    {
        try
        {
            foreach (MethodInfo method in typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public))
            {
                if (method.Name != "drawInMenu")
                    continue;

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length != 8
                    || parameters[0].ParameterType != typeof(SpriteBatch)
                    || parameters[1].ParameterType != typeof(Vector2)
                    || parameters[2].ParameterType != typeof(float)
                    || parameters[3].ParameterType != typeof(float)
                    || parameters[4].ParameterType != typeof(float)
                    || !parameters[5].ParameterType.IsEnum
                    || parameters[6].ParameterType != typeof(Color)
                    || parameters[7].ParameterType != typeof(bool))
                    continue;

                object hideStack = Enum.Parse(parameters[5].ParameterType, "Hide");
                method.Invoke(item, new object?[] { spriteBatch, position, 1f, 1f, 0.89f, hideStack, Color.White, true });
                return;
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not draw Animal Husbandry wallet tool fallback icon: {ex.GetBaseException().Message}");
        }
    }

    private void DebugLog(string message)
    {
        if (Config.DebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }
}
