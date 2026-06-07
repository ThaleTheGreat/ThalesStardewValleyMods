using System.Collections.Generic;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace ThaleTheGreat.CraftablePrismaticShard;

public sealed class ModEntry : Mod
{
    private const string RecipeName = "Prismatic Shard";
    private const string RecipeData = "72 10 64 10 60 10 62 10 68 10 66 10 70 10 749 10/Field/74/false/none";

    public override void Entry(IModHelper helper)
    {
        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
    }

    private static void OnAssetRequested(object sender, AssetRequestedEventArgs e)
    {
        if (!e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, string> recipes = asset.AsDictionary<string, string>().Data;
            recipes[RecipeName] = RecipeData;
        });
    }

    private static void OnSaveLoaded(object sender, SaveLoadedEventArgs e)
    {
        LearnRecipe();
    }

    private static void OnDayStarted(object sender, DayStartedEventArgs e)
    {
        LearnRecipe();
    }

    private static void LearnRecipe()
    {
        if (!Context.IsWorldReady)
            return;

        Farmer player = Game1.player;
        if (player is null)
            return;

        if (!player.craftingRecipes.ContainsKey(RecipeName))
            player.craftingRecipes[RecipeName] = 0;
    }
}
