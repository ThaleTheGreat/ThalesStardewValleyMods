using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

using ThaleTheGreat.WalletTools;

namespace ThaleTheGreat.WalletToolsForTractorMod;

internal sealed class TractorModModule : WalletModule
{
    internal const string ModuleKey = "TractorMod";
    internal const string LegacyUniqueId = "ThaleTheGreat.WalletToolsForTractorMod";

    internal TractorModModule(ThaleTheGreat.WalletTools.ModEntry host)
        : base(host, ModuleKey, "Tractor Mod", LegacyUniqueId, "Pathoschild.TractorMod")
    {
    }
    private const string TractorModId = "Pathoschild.TractorMod";
    private const string TractorManagerTypeName = "Pathoschild.Stardew.TractorMod.Framework.TractorManager";
    private const string TractorDataKey = "Pathoschild.TractorMod";
    private const string WalletRuntimeToolMarker = "ThaleTheGreat.WalletTools/RuntimeTool";
    private const string GmcmId = "spacechase0.GenericModConfigMenu";
    private const string WalletPowerPrefix = "ThaleTheGreat.WalletTools_";
    private const string OverlayTopOfScreen = "Top of Screen";
    private const string OverlayLeftOfInventoryBar = "Left of Inventory Bar";
    private const string OverlayRightOfInventoryBar = "Right of Inventory Bar";
    private const int InventorySlotSize = 80;
    private static readonly string[] OverlayPositionChoices =
    {
        OverlayTopOfScreen,
        OverlayLeftOfInventoryBar,
        OverlayRightOfInventoryBar
    };

    private static readonly TractorWalletTool[] SupportedWalletTools =
    {
        new("Axe", typeof(Axe)),
        new("Pickaxe", typeof(Pickaxe)),
        new("Hoe", typeof(Hoe)),
        new("WateringCan", typeof(WateringCan)),
        new("MilkPail", typeof(MilkPail)),
        new("Shears", typeof(Shears))
    };

    private static TractorModModule? Instance;

    private readonly Dictionary<string, Tool?> OverlayToolCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, StoredToolIcon?> OverlayIconCache = new(StringComparer.OrdinalIgnoreCase);

    private Harmony? Harmony;
    private ThaleTheGreat.WalletTools.IWalletToolsApi? WalletToolsApi;
    private IGenericModConfigMenuApi? GmcmApi;
    private bool GmcmRegistered;
    private MethodInfo? TractorUpdateAttachmentEffects;
    private MethodInfo? DrawInMenuMethod;
    private object? DrawStackHideValue;
    private ModConfig Config = new();
    private bool BypassPatch;
    private bool WasRidingTractor;
    private string? SelectedWalletToolKind;

    internal override void Initialize()
    {
        IModHelper helper = Helper;
        Instance = this;
        Config = Host.ReadModuleConfig<ModConfig>(ModuleKey);
        NormalizeConfig();
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Display.RenderedHud += OnRenderedHud;
    }

    internal override void OnGameLaunched()
    {
        RegisterGmcm();

        if (!Helper.ModRegistry.IsLoaded(TractorModId))
            return;

        WalletToolsApi = Host.GetWalletToolsApi();

        Type? tractorManagerType = AccessTools.TypeByName(TractorManagerTypeName);
        TractorUpdateAttachmentEffects = AccessTools.Method(tractorManagerType, "UpdateAttachmentEffects");
        MethodInfo? prefix = AccessTools.Method(typeof(TractorModModule), nameof(BeforeTractorUpdateAttachmentEffects));
        if (TractorUpdateAttachmentEffects is null || prefix is null)
        {
            Monitor.Log("Could not find Tractor Mod's attachment update method. Tractor compatibility patch was not applied.", LogLevel.Warn);
            return;
        }

        Harmony = new Harmony(LegacyUniqueId);
        Harmony.Patch(TractorUpdateAttachmentEffects, prefix: new HarmonyMethod(prefix));
        CacheDrawInMenuMethod();
        DebugLog("Patched Tractor Mod wallet-tool selector bridge.");
    }

    private void RegisterGmcm()
    {
        if (GmcmRegistered)
            return;

        GmcmApi = Host.GetGmcmAdapter<IGenericModConfigMenuApi>(ModuleKey);
        if (GmcmApi is null)
            return;

        try
        {
            GmcmApi.Unregister(ModManifest);
            GmcmApi.Register(
                ModManifest,
                reset: () =>
                {
                    Config = new ModConfig();
                    NormalizeConfig();
                    SelectedWalletToolKind = null;
                },
                save: () =>
                {
                    NormalizeConfig();
                    Host.WriteModuleConfig(ModuleKey, Config);
                    NormalizeSelectedWalletTool();
                }
            );

            AddBool(GmcmApi, nameof(Config.EnableTractorWalletSelector), () => Config.EnableTractorWalletSelector, value => Config.EnableTractorWalletSelector = value, "Enable Tractor Wallet Selector", "Allows Tractor Mod to use supported tools stored by Wallet Tools while riding the tractor.");
            AddKeybind(GmcmApi, nameof(Config.CyclePreviousToolKey), () => Config.CyclePreviousToolKey, value => Config.CyclePreviousToolKey = value, "Cycle Previous Tool", "Cycles the tractor wallet selector to the previous stored tool.");
            AddKeybind(GmcmApi, nameof(Config.CycleNextToolKey), () => Config.CycleNextToolKey, value => Config.CycleNextToolKey = value, "Cycle Next Tool", "Cycles the tractor wallet selector to the next stored tool.");
            AddBool(GmcmApi, nameof(Config.ShowSelectorOverlay), () => Config.ShowSelectorOverlay, value => Config.ShowSelectorOverlay = value, "Show Selector Overlay", "Shows the selected tractor wallet tool while riding the tractor.");
            AddText(GmcmApi, nameof(Config.SelectorOverlayPosition), () => GetNormalizedOverlayPosition(), SetOverlayPosition, "Selector Overlay Position", "Moves the selector overlay to the top-center of the screen, left of the inventory bar, or right of the inventory bar.", OverlayPositionChoices);
            AddBool(GmcmApi, nameof(Config.PlayCycleSound), () => Config.PlayCycleSound, value => Config.PlayCycleSound = value, "Play Cycle Sound", "Plays a sound when cycling the tractor wallet selector.");
            AddBool(GmcmApi, nameof(Config.DebugLogging), () => Config.DebugLogging, value => Config.DebugLogging = value, "Debug Logging", "Writes extra diagnostic messages to the SMAPI console.");

            GmcmRegistered = true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Tools for Tractor Mod options with Generic Mod Config Menu: {ex}", LogLevel.Error);
        }
    }

    private void AddBool(IGenericModConfigMenuApi gmcm, string fieldId, Func<bool> getValue, Action<bool> setValue, string name, string tooltip)
    {
        gmcm.AddBoolOption(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void AddKeybind(IGenericModConfigMenuApi gmcm, string fieldId, Func<StardewModdingAPI.Utilities.KeybindList> getValue, Action<StardewModdingAPI.Utilities.KeybindList> setValue, string name, string tooltip)
    {
        gmcm.AddKeybindList(ModManifest, getValue, setValue, () => name, () => tooltip, fieldId);
    }

    private void AddText(IGenericModConfigMenuApi gmcm, string fieldId, Func<string> getValue, Action<string> setValue, string name, string tooltip, string[] allowedValues)
    {
        gmcm.AddTextOption(ModManifest, getValue, setValue, () => name, () => tooltip, allowedValues, null, fieldId);
    }

    private void SetOverlayPosition(string value)
    {
        Config.SelectorOverlayPosition = NormalizeOverlayPosition(value);
    }

    private string GetNormalizedOverlayPosition()
    {
        Config.SelectorOverlayPosition = NormalizeOverlayPosition(Config.SelectorOverlayPosition);
        return Config.SelectorOverlayPosition;
    }

    private void NormalizeConfig()
    {
        Config.SelectorOverlayPosition = NormalizeOverlayPosition(Config.SelectorOverlayPosition);
    }

    private static string NormalizeOverlayPosition(string? value)
    {
        foreach (string choice in OverlayPositionChoices)
        {
            if (string.Equals(value, choice, StringComparison.OrdinalIgnoreCase))
                return choice;
        }

        return OverlayTopOfScreen;
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        SelectedWalletToolKind = null;
        WasRidingTractor = false;
        OverlayToolCache.Clear();
        OverlayIconCache.Clear();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        SelectedWalletToolKind = null;
        WasRidingTractor = false;
        OverlayToolCache.Clear();
        OverlayIconCache.Clear();
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        bool riding = IsCurrentPlayerRidingTractor();
        if (riding != WasRidingTractor)
        {
            WasRidingTractor = riding;
            SelectedWalletToolKind = null;
        }

        if (riding)
            NormalizeSelectedWalletTool();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.EnableTractorWalletSelector || Game1.activeClickableMenu is not null || !IsCurrentPlayerRidingTractor())
            return;

        if (Config.CyclePreviousToolKey.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CycleSelectedWalletTool(-1);
            return;
        }

        if (Config.CycleNextToolKey.JustPressed())
        {
            Helper.Input.Suppress(e.Button);
            CycleSelectedWalletTool(1);
        }
    }

    private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.EnableTractorWalletSelector || !Config.ShowSelectorOverlay || !IsCurrentPlayerRidingTractor())
            return;

        DrawSelectorOverlay(e.SpriteBatch);
    }

    private static bool BeforeTractorUpdateAttachmentEffects(object __instance)
    {
        return Instance?.BeforeTractorUpdateAttachmentEffectsImpl(__instance) ?? true;
    }

    private bool BeforeTractorUpdateAttachmentEffectsImpl(object tractorManager)
    {
        if (BypassPatch || !Context.IsWorldReady || !Config.EnableTractorWalletSelector || WalletToolsApi is null)
            return true;

        if (!IsCurrentPlayerRidingTractor())
            return true;

        NormalizeSelectedWalletTool();
        TractorWalletTool? selectedTool = GetSelectedWalletTool();
        if (selectedTool is null)
            return true;

        Farmer player = Game1.player;
        if (player.CurrentItem is Tool currentTool && IsWalletRuntimeTool(currentTool))
            return true;

        if (player.CurrentTool is Tool selectedInventoryTool && selectedTool.Matches(selectedInventoryTool))
            return true;

        if (TractorUpdateAttachmentEffects is null || !TryCreateWalletTool(selectedTool, out Tool? walletTool))
            return true;

        if (!TryGetCurrentSlot(player, out int currentSlot))
            return true;

        try
        {
            RunTractorPassWithTemporaryTool(tractorManager, player, currentSlot, walletTool);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
        }

        return false;
    }

    private void CycleSelectedWalletTool(int direction)
    {
        string?[] choices = GetSelectorChoices().ToArray();
        if (choices.Length == 0)
            return;

        int currentIndex = Array.FindIndex(choices, choice => string.Equals(choice, SelectedWalletToolKind, StringComparison.OrdinalIgnoreCase));
        if (currentIndex < 0)
            currentIndex = 0;

        int nextIndex = ((currentIndex + direction) % choices.Length + choices.Length) % choices.Length;
        string? previousKind = SelectedWalletToolKind;
        SelectedWalletToolKind = choices[nextIndex];

        if (Config.PlayCycleSound && !string.Equals(previousKind, SelectedWalletToolKind, StringComparison.OrdinalIgnoreCase))
            Game1.playSound(SelectedWalletToolKind is null ? "shiny4" : "toolSwap");

        DebugLog($"Tractor wallet selector changed to {SelectedWalletToolKind ?? "Inventory"}.");
    }

    private IEnumerable<string?> GetSelectorChoices()
    {
        yield return null;

        if (WalletToolsApi is null)
            yield break;

        foreach (TractorWalletTool tool in SupportedWalletTools)
        {
            if (WalletToolsApi.IsToolStored(tool.Kind))
                yield return tool.Kind;
        }
    }

    private void NormalizeSelectedWalletTool()
    {
        if (SelectedWalletToolKind is null || WalletToolsApi is null)
            return;

        TractorWalletTool? tool = GetSupportedWalletTool(SelectedWalletToolKind);
        if (tool is null || !WalletToolsApi.IsToolStored(tool.Kind))
            SelectedWalletToolKind = null;
    }

    private TractorWalletTool? GetSelectedWalletTool()
    {
        return SelectedWalletToolKind is null ? null : GetSupportedWalletTool(SelectedWalletToolKind);
    }

    private static TractorWalletTool? GetSupportedWalletTool(string kind)
    {
        foreach (TractorWalletTool tool in SupportedWalletTools)
        {
            if (tool.Kind.Equals(kind, StringComparison.OrdinalIgnoreCase))
                return tool;
        }

        return null;
    }

    private bool TryCreateWalletTool(TractorWalletTool requestedTool, out Tool walletTool)
    {
        Tool? tool = TryCreateToolFromWalletState(requestedTool) ?? TryCreateToolFromWalletApi(requestedTool);
        if (tool is not null)
        {
            walletTool = tool;
            return true;
        }

        walletTool = null!;
        return false;
    }

    private Tool? TryCreateToolFromWalletState(TractorWalletTool requestedTool)
    {
        try
        {
            if (!TryGetWalletToolState(requestedTool.Kind, out object state))
                return null;

            MethodInfo? createTool = AccessTools.Method(state.GetType(), "CreateTool", new[] { typeof(IMonitor) });
            if (createTool?.Invoke(state, new object?[] { Monitor }) is Tool createdTool)
                return requestedTool.Matches(createdTool) ? createdTool : null;
        }
        catch (Exception ex)
        {
            DebugLog($"Could not recreate wallet {requestedTool.Kind} through Wallet Tools state: {ex.GetBaseException().Message}");
        }

        return null;
    }

    private bool TryGetWalletToolState(string toolKind, out object state)
    {
        state = null!;
        object? walletModEntry = GetWalletToolsModEntry();
        MethodInfo? tryGetStoredTool = walletModEntry is null
            ? null
            : AccessTools.Method(walletModEntry.GetType(), "TryGetStoredToolForApi");
        if (walletModEntry is null || tryGetStoredTool is null)
            return false;

        object?[] args = { toolKind, null };
        if (tryGetStoredTool.Invoke(walletModEntry, args) is true && args[1] is object walletState)
        {
            state = walletState;
            return true;
        }

        return false;
    }

    private Tool? TryCreateToolFromWalletApi(TractorWalletTool requestedTool)
    {
        if (WalletToolsApi is null || !WalletToolsApi.TryGetStoredTool(requestedTool.Kind, out string qualifiedItemId, out int upgradeLevel, out _) || string.IsNullOrWhiteSpace(qualifiedItemId))
            return null;

        try
        {
            Tool tool = ItemRegistry.Create<Tool>(qualifiedItemId);
            SetUpgradeLevel(tool, upgradeLevel);
            return requestedTool.Matches(tool) ? tool : null;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Could not recreate wallet tool '{qualifiedItemId}' for Tractor Mod: {ex.Message}", LogLevel.Warn);
            return null;
        }
    }

    private object GetWalletToolsModEntry()
    {
        return Host;
    }

    private static void SetUpgradeLevel(Tool tool, int upgradeLevel)
    {
        if (upgradeLevel <= 0)
            return;

        PropertyInfo? property = AccessTools.Property(tool.GetType(), "UpgradeLevel") ?? AccessTools.Property(typeof(Tool), "UpgradeLevel");
        if (property?.CanWrite == true)
        {
            property.SetValue(tool, upgradeLevel);
            return;
        }

        FieldInfo? field = AccessTools.Field(tool.GetType(), "upgradeLevel") ?? AccessTools.Field(typeof(Tool), "upgradeLevel");
        field?.SetValue(tool, upgradeLevel);
    }

    private void RunTractorPassWithTemporaryTool(object tractorManager, Farmer player, int currentSlot, Tool walletTool)
    {
        Item? previousItem = player.Items[currentSlot];
        int previousCurrentToolIndex = player.CurrentToolIndex;

        try
        {
            player.Items[currentSlot] = walletTool;
            player.CurrentToolIndex = currentSlot;
            InvokeOriginalTractorAttachmentPass(tractorManager);
        }
        finally
        {
            if (currentSlot >= 0 && currentSlot < player.Items.Count)
                player.Items[currentSlot] = previousItem;

            player.CurrentToolIndex = Math.Max(0, Math.Min(previousCurrentToolIndex, Math.Max(0, player.Items.Count - 1)));
        }
    }

    private void InvokeOriginalTractorAttachmentPass(object tractorManager)
    {
        if (TractorUpdateAttachmentEffects is null)
            return;

        bool previousBypass = BypassPatch;
        BypassPatch = true;
        try
        {
            TractorUpdateAttachmentEffects.Invoke(tractorManager, null);
        }
        finally
        {
            BypassPatch = previousBypass;
        }
    }

    private void DrawSelectorOverlay(SpriteBatch spriteBatch)
    {
        Rectangle box = GetSelectorOverlayBounds();
        DrawInventorySlot(spriteBatch, box);

        TractorWalletTool? selectedTool = GetSelectedWalletTool();
        if (selectedTool is null)
            return;

        if (TryDrawWalletIcon(spriteBatch, selectedTool, box))
            return;

        Tool? iconTool = GetOverlayTool(selectedTool);
        if (iconTool is not null)
        {
            Rectangle iconBounds = GetCenteredIconBounds(box);
            DrawItemIcon(spriteBatch, iconTool, new Vector2(iconBounds.X, iconBounds.Y));
        }
    }

    private Rectangle GetSelectorOverlayBounds()
    {
        const int topMargin = 16;
        const int sideGap = 8;
        string position = GetNormalizedOverlayPosition();

        if (TryGetToolbarBounds(out Rectangle toolbarBounds))
        {
            int y = ClampToViewport(toolbarBounds.Y + (toolbarBounds.Height - InventorySlotSize) / 2, InventorySlotSize, topMargin, Game1.uiViewport.Height);

            if (string.Equals(position, OverlayLeftOfInventoryBar, StringComparison.OrdinalIgnoreCase))
            {
                int x = ClampToViewport(toolbarBounds.X - InventorySlotSize - sideGap, InventorySlotSize, sideGap, Game1.uiViewport.Width);
                return new Rectangle(x, y, InventorySlotSize, InventorySlotSize);
            }

            if (string.Equals(position, OverlayRightOfInventoryBar, StringComparison.OrdinalIgnoreCase))
            {
                int x = ClampToViewport(toolbarBounds.Right + sideGap, InventorySlotSize, sideGap, Game1.uiViewport.Width);
                return new Rectangle(x, y, InventorySlotSize, InventorySlotSize);
            }
        }

        int centeredX = Math.Max(0, (Game1.uiViewport.Width - InventorySlotSize) / 2);
        return new Rectangle(centeredX, topMargin, InventorySlotSize, InventorySlotSize);
    }

    private static bool TryGetToolbarBounds(out Rectangle bounds)
    {
        foreach (IClickableMenu menu in Game1.onScreenMenus)
        {
            if (menu is Toolbar)
            {
                bounds = new Rectangle(menu.xPositionOnScreen, menu.yPositionOnScreen, menu.width, menu.height);
                return true;
            }
        }

        bounds = Rectangle.Empty;
        return false;
    }

    private static int ClampToViewport(int value, int size, int margin, int viewportSize)
    {
        int max = Math.Max(margin, viewportSize - size - margin);
        return Math.Max(margin, Math.Min(value, max));
    }

    private static void DrawInventorySlot(SpriteBatch spriteBatch, Rectangle box)
    {
        IClickableMenu.drawTextureBox(
            spriteBatch,
            Game1.menuTexture,
            new Rectangle(0, 256, 60, 60),
            box.X,
            box.Y,
            box.Width,
            box.Height,
            Color.White,
            1f,
            false
        );
    }

    private bool TryDrawWalletIcon(SpriteBatch spriteBatch, TractorWalletTool selectedTool, Rectangle box)
    {
        if (!TryGetWalletIcon(selectedTool, out StoredToolIcon icon))
            return false;

        try
        {
            Texture2D texture = Game1.content.Load<Texture2D>(icon.TexturePath);
            Rectangle source = new(icon.TexturePosition.X, icon.TexturePosition.Y, 16, 16);
            Rectangle destination = GetCenteredIconBounds(box);
            spriteBatch.Draw(
                texture,
                destination,
                source,
                Color.White,
                0f,
                Vector2.Zero,
                SpriteEffects.None,
                0.865f
            );
            return true;
        }
        catch (Exception ex)
        {
            DebugLog($"Could not draw wallet texture icon for {selectedTool.Kind}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private static Rectangle GetCenteredIconBounds(Rectangle box)
    {
        int iconSize = Math.Min(64, Math.Min(box.Width, box.Height));
        return new Rectangle(
            box.X + (box.Width - iconSize) / 2,
            box.Y + (box.Height - iconSize) / 2,
            iconSize,
            iconSize
        );
    }

    private bool TryGetWalletIcon(TractorWalletTool selectedTool, out StoredToolIcon icon)
    {
        icon = null!;
        if (WalletToolsApi?.IsToolStored(selectedTool.Kind) != true)
            return false;

        try
        {
            Dictionary<string, PowersData> powers = Game1.content.Load<Dictionary<string, PowersData>>("Data/Powers");
            string powerId = WalletPowerPrefix + selectedTool.Kind;
            if (!powers.TryGetValue(powerId, out PowersData? power) || string.IsNullOrWhiteSpace(power.TexturePath))
                return false;

            icon = new StoredToolIcon(power.TexturePath, power.TexturePosition);
            return true;
        }
        catch (Exception ex)
        {
            DebugLog($"Could not read Wallet Tools power texture icon for {selectedTool.Kind}: {ex.GetBaseException().Message}");
            return false;
        }
    }

    private Tool? GetOverlayTool(TractorWalletTool selectedTool)
    {
        if (WalletToolsApi?.IsToolStored(selectedTool.Kind) != true)
            return null;

        if (OverlayToolCache.TryGetValue(selectedTool.Kind, out Tool? cachedTool))
            return cachedTool;

        Tool? tool = TryCreateToolFromWalletState(selectedTool) ?? TryCreateToolFromWalletApi(selectedTool);
        OverlayToolCache[selectedTool.Kind] = tool;
        return tool;
    }

    private void CacheDrawInMenuMethod()
    {
        foreach (MethodInfo method in typeof(Item).GetMethods(BindingFlags.Instance | BindingFlags.Public))
        {
            if (method.Name != "drawInMenu")
                continue;

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 8)
                continue;

            if (parameters[0].ParameterType != typeof(SpriteBatch)
                || parameters[1].ParameterType != typeof(Vector2)
                || parameters[2].ParameterType != typeof(float)
                || parameters[3].ParameterType != typeof(float)
                || parameters[4].ParameterType != typeof(float)
                || !parameters[5].ParameterType.IsEnum
                || parameters[6].ParameterType != typeof(Color)
                || parameters[7].ParameterType != typeof(bool))
                continue;

            DrawInMenuMethod = method;
            DrawStackHideValue = Enum.Parse(parameters[5].ParameterType, "Hide");
            return;
        }
    }

    private void DrawItemIcon(SpriteBatch spriteBatch, Item item, Vector2 position)
    {
        try
        {
            if (DrawInMenuMethod is null || DrawStackHideValue is null)
                CacheDrawInMenuMethod();

            if (DrawInMenuMethod is not null && DrawStackHideValue is not null)
            {
                DrawInMenuMethod.Invoke(item, new object?[] { spriteBatch, position, 1f, 1f, 0.89f, DrawStackHideValue, Color.White, true });
            }
        }
        catch (Exception ex)
        {
            DebugLog($"Could not draw tractor wallet selector icon: {ex.GetBaseException().Message}");
        }
    }

    private static bool TryGetCurrentSlot(Farmer player, out int slot)
    {
        slot = player.CurrentToolIndex;
        return slot >= 0 && slot < player.Items.Count;
    }

    private static bool IsCurrentPlayerRidingTractor()
    {
        return Game1.player?.mount is Horse horse && horse.modData.ContainsKey(TractorDataKey);
    }

    private static bool IsWalletRuntimeTool(Tool tool)
    {
        return tool.modData.ContainsKey(WalletRuntimeToolMarker);
    }

    private void DebugLog(string message)
    {
        if (Config.DebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }

    private sealed class StoredToolIcon
    {
        public StoredToolIcon(string texturePath, Point texturePosition)
        {
            TexturePath = texturePath;
            TexturePosition = texturePosition;
        }

        public string TexturePath { get; }
        public Point TexturePosition { get; }
    }

    private sealed class TractorWalletTool
    {
        public TractorWalletTool(string kind, Type toolType)
        {
            Kind = kind;
            ToolType = toolType;
        }

        public string Kind { get; }
        public Type ToolType { get; }

        public bool Matches(Tool tool)
        {
            return ToolType.IsInstanceOfType(tool);
        }
    }
}
