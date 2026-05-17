using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ThaleTheGreat.SaloonCover;

public sealed class ModEntry : Mod
{
    private const string SaloonLocationName = "Saloon";
    private readonly HashSet<long> paidToday = new();
    private readonly Dictionary<long, int> pendingCoverQuestion = new();
    private ModConfig Config = new();
    private Texture2D? bouncerTexture;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();
        SanitizeConfig();
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.Player.Warped += OnWarped;
        helper.Events.Display.RenderedWorld += OnRenderedWorld;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        paidToday.Clear();
        pendingCoverQuestion.Clear();
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!ReferenceEquals(e.Player, Game1.player) || !IsSaloon(e.NewLocation) || Game1.timeOfDay < Config.CoverStartsAt)
            return;

        long playerId = e.Player.UniqueMultiplayerID;
        if (Config.PayOncePerDay && paidToday.Contains(playerId))
            return;

        int cover = Math.Max(0, Config.CoverCharge);
        if (cover <= 0)
        {
            paidToday.Add(playerId);
            return;
        }

        pendingCoverQuestion[playerId] = 8;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || !IsSaloon(Game1.currentLocation) || Game1.timeOfDay < Config.CoverStartsAt)
            return;

        long playerId = Game1.player.UniqueMultiplayerID;
        if (!pendingCoverQuestion.TryGetValue(playerId, out int delayTicks))
            return;

        if (Config.PayOncePerDay && paidToday.Contains(playerId))
        {
            pendingCoverQuestion.Remove(playerId);
            return;
        }

        if (Game1.activeClickableMenu != null || Game1.fadeToBlackAlpha > 0.01f)
            return;

        if (delayTicks > 0)
        {
            pendingCoverQuestion[playerId] = delayTicks - 1;
            return;
        }

        AskCoverQuestion(playerId);
    }

    private void AskCoverQuestion(long playerId)
    {
        pendingCoverQuestion.Remove(playerId);
        int cover = Math.Max(0, Config.CoverCharge);

        Game1.currentLocation.createQuestionDialogue(
            $"Cover is {cover}g after 8pm. Pay to enter?",
            new[]
            {
                new Response("Pay", $"Pay {cover}g"),
                new Response("Leave", "Leave")
            },
            (Farmer who, string answer) => HandleCoverAnswer(who, answer)
        );
    }

    private void HandleCoverAnswer(Farmer who, string answer)
    {
        pendingCoverQuestion.Remove(who.UniqueMultiplayerID);

        if (!string.Equals(answer, "Pay", StringComparison.OrdinalIgnoreCase))
        {
            EjectPlayer("The bouncer sends you back outside.");
            return;
        }

        int cover = Math.Max(0, Config.CoverCharge);
        if (Game1.player.Money < cover)
        {
            EjectPlayer($"The bouncer blocks the way. Cover is {cover}g.");
            return;
        }

        Game1.player.Money -= cover;
        paidToday.Add(who.UniqueMultiplayerID);
        Game1.drawObjectDialogue($"The bouncer collects a {cover}g cover charge.");
        DebugLog($"Charged {cover}g cover to {Game1.player.Name}.");
    }

    private void EjectPlayer(string message)
    {
        Game1.drawObjectDialogue(message);
        Game1.warpFarmer("Town", Config.EjectTileX, Config.EjectTileY, 2);
        DebugLog($"Ejected {Game1.player.Name} from the Saloon.");
    }

    private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
    {
        if (!Context.IsWorldReady || !IsSaloon(Game1.currentLocation) || Game1.timeOfDay < Config.CoverStartsAt)
            return;

        bouncerTexture ??= Game1.content.Load<Texture2D>("Characters/Bouncer");
        Rectangle source = new(0, 0, 16, 32);
        Vector2 worldPosition = new(Config.BouncerTileX * Game1.tileSize, (Config.BouncerTileY - 1) * Game1.tileSize);
        Vector2 screenPosition = Game1.GlobalToLocal(Game1.viewport, worldPosition);
        float layerDepth = (Config.BouncerTileY * Game1.tileSize + 64) / 10000f;

        e.SpriteBatch.Draw(
            bouncerTexture,
            screenPosition,
            source,
            Color.White,
            0f,
            Vector2.Zero,
            Game1.pixelZoom,
            SpriteEffects.None,
            layerDepth
        );
    }

    private static bool IsSaloon(GameLocation? location)
    {
        return location != null && (location.Name == SaloonLocationName || location.NameOrUniqueName == SaloonLocationName);
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm == null)
            return;

        gmcm.Register(
            ModManifest,
            () => Config = new ModConfig(),
            () =>
            {
                SanitizeConfig();
                Helper.WriteConfig(Config);
            }
        );

        gmcm.AddNumberOption(
            ModManifest,
            () => Config.CoverCharge,
            value => Config.CoverCharge = value,
            () => "Cover Charge",
            () => "Gold charged when entering the Saloon after the cover starts.",
            min: 0,
            max: 1000,
            interval: 5,
            formatValue: value => $"{value}g"
        );

        gmcm.AddNumberOption(
            ModManifest,
            () => Config.CoverStartsAt,
            value => Config.CoverStartsAt = value,
            () => "Cover Starts At",
            () => "Stardew time when the bouncer starts asking for cover.",
            min: 600,
            max: 2600,
            interval: 100,
            formatValue: FormatTime
        );

        gmcm.AddBoolOption(
            ModManifest,
            () => Config.PayOncePerDay,
            value => Config.PayOncePerDay = value,
            () => "Pay Once Per Day",
            () => "When enabled, each player only pays the cover once per in-game day."
        );

        gmcm.AddNumberOption(
            ModManifest,
            () => Config.BouncerTileX,
            value => Config.BouncerTileX = value,
            () => "Bouncer Tile X",
            () => "Horizontal tile where the vanilla bouncer is drawn inside the Saloon.",
            min: 0,
            max: 100,
            interval: 1
        );

        gmcm.AddNumberOption(
            ModManifest,
            () => Config.BouncerTileY,
            value => Config.BouncerTileY = value,
            () => "Bouncer Tile Y",
            () => "Vertical tile where the vanilla bouncer is drawn inside the Saloon.",
            min: 0,
            max: 100,
            interval: 1
        );

        gmcm.AddBoolOption(
            ModManifest,
            () => Config.DebugLogging,
            value => Config.DebugLogging = value,
            () => "Debug Logging",
            () => "Enable extra diagnostic log messages."
        );
    }

    private void SanitizeConfig()
    {
        Config.CoverCharge = Math.Clamp(Config.CoverCharge, 0, 1000);
        Config.CoverStartsAt = Math.Clamp(Config.CoverStartsAt, 600, 2600);
        Config.BouncerTileX = Math.Clamp(Config.BouncerTileX, 0, 100);
        Config.BouncerTileY = Math.Clamp(Config.BouncerTileY, 0, 100);
        Config.EjectTileX = Math.Clamp(Config.EjectTileX, 0, 200);
        Config.EjectTileY = Math.Clamp(Config.EjectTileY, 0, 200);
    }

    private static string FormatTime(int time)
    {
        int hour = time / 100;
        int minute = time % 100;
        string suffix = hour >= 12 ? "pm" : "am";
        int displayHour = hour % 12;
        if (displayHour == 0)
            displayHour = 12;

        return $"{displayHour}:{minute:00}{suffix}";
    }

    private void DebugLog(string message)
    {
        if (Config.DebugLogging)
            Monitor.Log(message, LogLevel.Debug);
    }
}
