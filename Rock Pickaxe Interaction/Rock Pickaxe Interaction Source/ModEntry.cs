using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace ThaleTheGreat.RockPickaxeInteraction;

internal sealed class ModEntry : Mod
{
    private const string UsedKey = "ThaleTheGreat.RockPickaxeInteraction/UsedDay";
    private ModConfig Config = new();

    public override void Entry(IModHelper helper)
    {
        this.Config = helper.ReadConfig<ModConfig>();
        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(this.ModManifest, () => this.Config = new ModConfig(), () => this.Helper.WriteConfig(this.Config));
        gmcm.AddBoolOption(
            this.ModManifest,
            () => this.Config.RockLikesPickaxe,
            value => this.Config.RockLikesPickaxe = value,
            () => this.Helper.Translation.Get("gmcm.rock-likes-pickaxe.name"),
            () => this.Helper.Translation.Get("gmcm.rock-likes-pickaxe.tooltip")
        );
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Context.CanPlayerMove || !e.Button.IsUseToolButton())
            return;

        Farmer player = Game1.player;
        if (player.CurrentTool is not Pickaxe)
            return;

        NPC? rock = this.FindRock(player);
        if (rock is null)
            return;

        this.Helper.Input.Suppress(e.Button);
        long day = Game1.stats.DaysPlayed;
        if (player.modData.TryGetValue(UsedKey, out string? usedDayText) && long.TryParse(usedDayText, out long usedDay) && usedDay == day)
        {
            Game1.drawObjectDialogue(this.Helper.Translation.Get("dialogue.already-pickaxed"));
            return;
        }

        player.modData[UsedKey] = day.ToString();
        int amount = this.Config.RockLikesPickaxe ? 5 : -30;
        player.changeFriendship(amount, rock);
        Game1.drawObjectDialogue(this.Helper.Translation.Get(this.Config.RockLikesPickaxe ? "dialogue.likes-pickaxe" : "dialogue.dislikes-pickaxe"));
    }

    private NPC? FindRock(Farmer player)
    {
        Vector2 playerTile = player.Tile;
        foreach (NPC npc in Game1.currentLocation.characters)
        {
            if (!string.Equals(npc.Name, "Rock", StringComparison.OrdinalIgnoreCase) && !string.Equals(npc.Name, "boxosoup.rock_rock", StringComparison.OrdinalIgnoreCase))
                continue;

            if (Vector2.Distance(playerTile, npc.Tile) <= 1.5f)
                return npc;
        }

        return null;
    }
}
