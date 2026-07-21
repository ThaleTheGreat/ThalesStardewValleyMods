using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;
using StardewValley.Menus;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.IO;

namespace GMCMAdvancedSearch;

internal sealed class ModConfig
{
    public KeybindList OpenSearchMenuKey { get; set; } = new(SButton.F2);
    public bool ShowResultDetails { get; set; } = false;
    public bool ShowModTooltips { get; set; } = true;
    public bool IncludeContentPacks { get; set; } = true;
    public bool IncludeConfigFileFallback { get; set; } = true;
    public bool IncludeConfigValues { get; set; } = false;
    public bool DebugLogging { get; set; } = false;
}

public sealed class ModEntry : Mod
{
    private ModConfig Config = new();
    private IGenericModConfigMenuApi? Gmcm;
    private IMobilePhoneApi? MobilePhone;
    private List<GmcmOptionRecord> IndexedOptions = new();
    private bool IndexBuilt;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (Game1.activeClickableMenu is not SearchMenu menu)
            return;

        if (e.Button is SButton.Escape or SButton.ControllerB)
        {
            if (menu.LetChildMenuHandleBackInput())
                return;

            Helper.Input.Suppress(e.Button);
            menu.BackOrCloseFromInput();
            return;
        }

        if (!IsMouseInput(e.Button))
            Helper.Input.Suppress(e.Button);
    }

    private static bool IsMouseInput(SButton button)
    {
        string name = button.ToString();
        return name.StartsWith("Mouse", StringComparison.OrdinalIgnoreCase);
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        Gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (Gmcm == null)
        {
            Monitor.Log("Generic Mod Config Menu not found. GMCM Advanced Search requires GMCM to open mod config pages.", LogLevel.Warn);
            return;
        }

        Gmcm.Register(ModManifest, ResetConfig, SaveConfig);
        Gmcm.AddSectionTitle(ModManifest, () => "(TTG) GMCM Advanced Search", () => "Search inside GMCM authors, mod names, UniqueIDs, Nexus IDs, option labels, tooltips, field IDs, and config keys.");
        Gmcm.AddKeybindList(ModManifest, () => Config.OpenSearchMenuKey, v => Config.OpenSearchMenuKey = v, () => "Open search menu", () => "Hotkey to open the GMCM Advanced Search menu.");
        Gmcm.AddBoolOption(ModManifest, () => Config.ShowResultDetails, v => Config.ShowResultDetails = v, () => "Show Advanced Details", () => "Show extra metadata under each search result, including section/page, tooltip text, field ID, config key path, and option type.");
        Gmcm.AddBoolOption(ModManifest, () => Config.ShowModTooltips, v => Config.ShowModTooltips = v, () => "Show Mod Tooltips", () => "Show GMCM-style hover tooltips with the result mod name and description.");
        Gmcm.AddBoolOption(ModManifest, () => Config.IncludeContentPacks, SetIncludeContentPacks, () => "Include content packs", () => "Include GMCM-registered content packs when searching.");
        Gmcm.AddBoolOption(ModManifest, () => Config.IncludeConfigFileFallback, SetIncludeConfigFileFallback, () => "Include config.json fallback", () => "Also search config.json keys when GMCM option metadata can't be fully read.");
        Gmcm.AddBoolOption(ModManifest, () => Config.IncludeConfigValues, SetIncludeConfigValues, () => "Search config values", () => "Also index simple config.json values. This can produce more noisy results.");
        Gmcm.AddBoolOption(ModManifest, () => Config.DebugLogging, v => Config.DebugLogging = v, () => "Debug logging", () => "Log index counts and reflection details for troubleshooting.");
        Gmcm.AddParagraph(ModManifest, () => "Tip: search results show matching authors first, then matching mod names, UniqueIDs, or Nexus IDs with matching option labels, tooltips, field IDs, and config keys grouped underneath.");

        TryRegisterMobilePhoneApp();
    }

    private void TryRegisterMobilePhoneApp()
    {
        MobilePhone = Helper.ModRegistry.GetApi<IMobilePhoneApi>("JoXW.MobilePhone")
            ?? Helper.ModRegistry.GetApi<IMobilePhoneApi>("aedenthorn.MobilePhone");
        if (MobilePhone == null)
            return;

        Texture2D? icon = LoadEmbeddedMobilePhoneIcon();
        if (icon == null)
            return;

        bool registered = MobilePhone.AddApp(ModManifest.UniqueID, "GMCM Search", OpenSearchMenuFromPhone, icon);

        if (Config.DebugLogging)
            Monitor.Log($"Mobile Phone app registration: {registered}.", LogLevel.Debug);
    }

    private Texture2D? LoadEmbeddedMobilePhoneIcon()
    {
        const string resourceName = "GMCMAdvancedSearch.MobilePhone.png";
        Stream? stream = typeof(ModEntry).Assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            if (Config.DebugLogging)
                Monitor.Log($"Embedded Mobile Phone icon resource '{resourceName}' was not found.", LogLevel.Debug);
            return null;
        }

        using (stream)
            return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream);
    }

    private void ResetConfig()
    {
        Config = new ModConfig();
        MarkIndexDirty();
    }

    private void SaveConfig()
    {
        Helper.WriteConfig(Config);
    }

    private void SetIncludeContentPacks(bool value)
    {
        if (Config.IncludeContentPacks == value)
            return;

        Config.IncludeContentPacks = value;
        MarkIndexDirty();
    }

    private void SetIncludeConfigFileFallback(bool value)
    {
        if (Config.IncludeConfigFileFallback == value)
            return;

        Config.IncludeConfigFileFallback = value;
        MarkIndexDirty();
    }

    private void SetIncludeConfigValues(bool value)
    {
        if (Config.IncludeConfigValues == value)
            return;

        Config.IncludeConfigValues = value;
        MarkIndexDirty();
    }

    private void MarkIndexDirty()
    {
        IndexedOptions.Clear();
        IndexBuilt = false;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (Config.OpenSearchMenuKey.JustPressed())
            TryOpenSearchMenu(true);
    }

    private void OpenSearchMenuFromPhone()
    {
        if (Game1.activeClickableMenu is SearchMenu)
        {
            TryOpenSearchMenu(true);
            return;
        }

        Game1.activeClickableMenu = null;
        Helper.Events.GameLoop.UpdateTicked -= OpenMenuNextTick;
        Helper.Events.GameLoop.UpdateTicked += OpenMenuNextTick;
    }

    private void TryOpenSearchMenu(bool allowToggle)
    {
        if (Game1.activeClickableMenu is SearchMenu menu)
        {
            if (!allowToggle)
                return;

            Game1.playSound("bigDeSelect");
            menu.CloseFromInput();
            return;
        }

        if (!Context.IsWorldReady && Game1.activeClickableMenu is not TitleMenu)
            return;

        bool isTitleMenu = Game1.activeClickableMenu is TitleMenu;
        bool isGmcmMenu = Game1.activeClickableMenu?.GetType().FullName?.Contains("GenericModConfigMenu", StringComparison.OrdinalIgnoreCase) == true;
        if (Game1.activeClickableMenu != null && !isTitleMenu && !isGmcmMenu)
            return;

        if (Gmcm == null)
        {
            Game1.showRedMessage("Generic Mod Config Menu is not installed.");
            return;
        }

        if (!IndexBuilt)
        {
            IndexedOptions = OptionIndexBuilder.Build(Helper, Gmcm, Monitor, ModManifest, Config);
            IndexBuilt = true;
            if (Config.DebugLogging)
                Monitor.Log($"Indexed {IndexedOptions.Count} searchable GMCM/config option records.", LogLevel.Info);
        }

        if (IndexedOptions.Count == 0)
        {
            Game1.showRedMessage("No GMCM options or config keys found.");
            return;
        }

        if (isGmcmMenu)
        {
            Game1.activeClickableMenu = null;
            Helper.Events.GameLoop.UpdateTicked -= OpenMenuNextTick;
            Helper.Events.GameLoop.UpdateTicked += OpenMenuNextTick;
        }
        else
        {
            OpenMenu();
        }
    }

    private void OpenMenuNextTick(object? sender, UpdateTickedEventArgs e)
    {
        Helper.Events.GameLoop.UpdateTicked -= OpenMenuNextTick;
        TryOpenSearchMenu(false);
    }

    private void OpenMenu()
    {
        Game1.activeClickableMenu = new SearchMenu("(TTG) GMCM Advanced Search", Config.ShowResultDetails, Config.ShowModTooltips, IndexedOptions, TryOpenMod);
    }

    private bool TryOpenMod(IManifest manifest)
    {
        if (Gmcm == null)
            return false;

        try
        {
            Gmcm.OpenModMenuAsChildMenu(manifest);
            return true;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to open GMCM menu for '{manifest.UniqueID}': {ex}", LogLevel.Warn);
            return false;
        }
    }
}
