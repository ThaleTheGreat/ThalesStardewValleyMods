using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewModdingAPI.Utilities;
using StardewValley;

namespace ThaleTheGreat.SaloonCover;

internal sealed class ModEntry : Mod
{
    private const string PaidKey = "ThaleTheGreat.SaloonCover/PaidDay";
    private const string BouncerName = "SaloonCoverBouncer";

    private ModConfig Config = new();
    private bool pendingCoverPrompt;
    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        helper.Events.Player.Warped += this.OnWarped;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.Saving += (_, _) => this.RemoveBouncer();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) =>
        {
            this.pendingCoverPrompt = false;
        };
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterGmcm();
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(this.ModManifest, () => this.Config = new ModConfig(), () => this.Helper.WriteConfig(this.Config));
        gmcm.AddNumberOption(this.ModManifest, () => this.Config.CoverCharge, value => this.Config.CoverCharge = value, () => T("gmcm.cover-charge.name"), () => T("gmcm.cover-charge.tooltip"), 0, 1000, 5);
        gmcm.AddNumberOption(this.ModManifest, () => this.Config.CoverStartsAt, value => this.Config.CoverStartsAt = value, () => T("gmcm.cover-starts-at.name"), () => T("gmcm.cover-starts-at.tooltip"), 600, 2600, 100);
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnSunday, value => this.Config.CoverOnSunday = value, () => T("gmcm.sunday.name"), () => T("gmcm.sunday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnMonday, value => this.Config.CoverOnMonday = value, () => T("gmcm.monday.name"), () => T("gmcm.monday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnTuesday, value => this.Config.CoverOnTuesday = value, () => T("gmcm.tuesday.name"), () => T("gmcm.tuesday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnWednesday, value => this.Config.CoverOnWednesday = value, () => T("gmcm.wednesday.name"), () => T("gmcm.wednesday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnThursday, value => this.Config.CoverOnThursday = value, () => T("gmcm.thursday.name"), () => T("gmcm.thursday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnFriday, value => this.Config.CoverOnFriday = value, () => T("gmcm.friday.name"), () => T("gmcm.friday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.CoverOnSaturday, value => this.Config.CoverOnSaturday = value, () => T("gmcm.saturday.name"), () => T("gmcm.saturday.tooltip"));
        gmcm.AddBoolOption(this.ModManifest, () => this.Config.PayOncePerDay, value => this.Config.PayOncePerDay = value, () => T("gmcm.pay-once.name"), () => T("gmcm.pay-once.tooltip"));
        gmcm.AddNumberOption(this.ModManifest, () => this.Config.BouncerTileX, value => this.Config.BouncerTileX = value, () => T("gmcm.bouncer-x.name"), () => T("gmcm.bouncer-x.tooltip"), 0, 100, 1);
        gmcm.AddNumberOption(this.ModManifest, () => this.Config.BouncerTileY, value => this.Config.BouncerTileY = value, () => T("gmcm.bouncer-y.name"), () => T("gmcm.bouncer-y.tooltip"), 0, 100, 1);
    }


    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (!e.IsLocalPlayer)
            return;

        this.UpdateBouncer();
        if (e.NewLocation.NameOrUniqueName != "Saloon" || !this.CoverActiveNow())
            return;

        if (this.Config.PayOncePerDay && e.Player.modData.TryGetValue(PaidKey, out string? paidText) && long.TryParse(paidText, out long paidDay) && paidDay == Game1.stats.DaysPlayed)
            return;

        this.pendingCoverPrompt = true;
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        this.UpdateBouncer();
        if (!this.pendingCoverPrompt || Game1.currentLocation?.NameOrUniqueName != "Saloon" || !this.CoverActiveNow())
            return;

        if (Game1.activeClickableMenu is not null || Game1.fadeToBlackAlpha > 0f || Game1.fadeToBlack)
            return;

        this.pendingCoverPrompt = false;
        this.AskForCover(Game1.player);
    }

    private void AskForCover(Farmer player)
    {
        if (Game1.currentLocation?.NameOrUniqueName != "Saloon" || Game1.activeClickableMenu != null)
            return;

        Response[] responses =
        {
            new("Pay", T("response.pay")),
            new("Leave", T("response.leave"))
        };
        Game1.currentLocation.createQuestionDialogue(T("question.cover"), responses, (_, answer) => this.HandleAnswer(player, answer));
    }

    private void HandleAnswer(Farmer player, string answer)
    {
        if (answer == "Pay" && player.Money >= this.Config.CoverCharge)
        {
            player.Money -= this.Config.CoverCharge;
            player.modData[PaidKey] = Game1.stats.DaysPlayed.ToString();
            Game1.drawObjectDialogue(T("dialogue.paid"));
            return;
        }

        Game1.drawObjectDialogue(answer == "Pay" ? T("dialogue.no-money") : T("dialogue.leave"));
        Game1.delayedActions.Add(new DelayedAction(300, this.KickPlayerOut));
    }

    private void KickPlayerOut()
    {
        KickOutTarget target = this.GetSaloonExitTarget();
        Game1.warpFarmer(target.Location, target.X, target.Y, target.Facing);
    }

    private KickOutTarget GetSaloonExitTarget()
    {
        GameLocation? saloon = Game1.getLocationFromName("Saloon");
        if (saloon is not null)
        {
            foreach (Warp warp in saloon.warps)
            {
                if (string.Equals(warp.TargetName, "Town", StringComparison.OrdinalIgnoreCase))
                    return new KickOutTarget(warp.TargetName, warp.TargetX, warp.TargetY, 2);
            }
        }

        return new KickOutTarget("Town", 47, 58, 2);
    }

    private void UpdateBouncer()
    {
        if (!Context.IsWorldReady)
            return;

        GameLocation? saloon = Game1.getLocationFromName("Saloon");
        if (saloon is null)
            return;

        NPC? existing = saloon.characters.FirstOrDefault(npc => npc.Name == BouncerName);
        if (!this.CoverActiveNow())
        {
            if (existing is not null)
                saloon.characters.Remove(existing);
            return;
        }

        Vector2 tile = new(this.Config.BouncerTileX, this.Config.BouncerTileY);
        if (existing is null)
        {
            NPC bouncer = new(new AnimatedSprite("Characters\\Bouncer", 0, 16, 32), tile * Game1.tileSize, Game1.down, BouncerName);
            saloon.characters.Add(bouncer);
            return;
        }

        existing.Position = tile * Game1.tileSize;
        existing.faceDirection(Game1.down);
    }

    private void RemoveBouncer()
    {
        GameLocation? saloon = Game1.getLocationFromName("Saloon");
        NPC? existing = saloon?.characters.FirstOrDefault(npc => npc.Name == BouncerName);
        if (saloon is not null && existing is not null)
            saloon.characters.Remove(existing);
    }

    private bool CoverActiveNow()
    {
        return Context.IsWorldReady && Game1.timeOfDay >= this.Config.CoverStartsAt && this.IsCoverDay();
    }

    private bool IsCoverDay()
    {
        return SDate.Now().DayOfWeek switch
        {
            DayOfWeek.Sunday => this.Config.CoverOnSunday,
            DayOfWeek.Monday => this.Config.CoverOnMonday,
            DayOfWeek.Tuesday => this.Config.CoverOnTuesday,
            DayOfWeek.Wednesday => this.Config.CoverOnWednesday,
            DayOfWeek.Thursday => this.Config.CoverOnThursday,
            DayOfWeek.Friday => this.Config.CoverOnFriday,
            DayOfWeek.Saturday => this.Config.CoverOnSaturday,
            _ => true
        };
    }

    private string T(string key)
    {
        string coverStartsAtText = Context.IsWorldReady
            ? Game1.getTimeOfDayString(this.Config.CoverStartsAt)
            : this.Config.CoverStartsAt.ToString();

        return this.Helper.Translation.Get(key, new
        {
            CoverCharge = this.Config.CoverCharge,
            CoverStartsAt = this.Config.CoverStartsAt,
            CoverStartsAtText = coverStartsAtText
        });
    }

    private readonly record struct KickOutTarget(string Location, int X, int Y, int Facing);
}
