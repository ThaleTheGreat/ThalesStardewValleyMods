using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Tools;

namespace RockPickaxeMod;

public sealed class ModEntry : Mod
{
    private const string RockInteractionKey = "Rock_Interaction";

    private readonly HashSet<string> interactedToday = new();
    private ModConfig config = new();

    public override void Entry(IModHelper helper)
    {
        this.config = helper.ReadConfig<ModConfig>();

        helper.Events.Input.ButtonPressed += this.OnButtonPressed;
        helper.Events.GameLoop.DayStarted += this.OnDayStarted;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.RegisterWithGMCM();
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.Button.IsUseToolButton())
            return;

        Farmer player = Game1.player;
        if (player.CurrentTool is not Pickaxe)
            return;

        Vector2 grabTile = e.Cursor.GrabTile;
        foreach (NPC character in Game1.currentLocation.characters)
        {
            if (!this.IsRock(character))
                continue;

            if (Vector2.Distance(grabTile, character.Tile) > 1.5f)
                continue;

            player.BeginUsingTool();
            this.HandleRockHit(player, character);
            break;
        }
    }

    private bool IsRock(NPC npc)
    {
        string name = npc.Name;
        return name.Equals("Rock", StringComparison.OrdinalIgnoreCase)
            || name.Equals("boxosoup.rock_rock", StringComparison.OrdinalIgnoreCase);
    }

    private void HandleRockHit(Farmer player, NPC rock)
    {
        if (this.interactedToday.Contains(RockInteractionKey))
        {
            Game1.drawObjectDialogue(this.I18n("dialogue.already-pickaxed"));
            return;
        }

        this.interactedToday.Add(RockInteractionKey);

        if (this.config.RockLikesPickaxe)
        {
            player.changeFriendship(5, rock);
            Game1.drawObjectDialogue(this.I18n("dialogue.likes-pickaxe"));
        }
        else
        {
            player.changeFriendship(-30, rock);
            Game1.drawObjectDialogue(this.I18n("dialogue.dislikes-pickaxe"));
        }
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        this.interactedToday.Clear();
    }

    private void RegisterWithGMCM()
    {
        IGenericModConfigMenuApi? api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (api is null)
            return;

        api.Register(
            this.ModManifest,
            reset: () => this.config = new ModConfig(),
            save: () => this.Helper.WriteConfig(this.config)
        );

        api.AddBoolOption(
            this.ModManifest,
            getValue: () => this.config.RockLikesPickaxe,
            setValue: value => this.config.RockLikesPickaxe = value,
            name: () => this.I18n("gmcm.rock-likes-pickaxe.name"),
            tooltip: () => this.I18n("gmcm.rock-likes-pickaxe.tooltip"),
            fieldId: nameof(ModConfig.RockLikesPickaxe)
        );
    }

    private string I18n(string key)
    {
        return this.Helper.Translation.Get(key);
    }
}
