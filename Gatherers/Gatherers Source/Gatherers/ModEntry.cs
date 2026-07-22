using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using System.Reflection;
using StardewValley;
using StardewValley.Characters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using ThaleTheGreat.Gatherers.Api;
using ThaleTheGreat.Gatherers.Framework;
using ThaleTheGreat.Gatherers.Integration;
using ThaleTheGreat.Gatherers.Patches;
using ThaleTheGreat.Gatherers.Services;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers;

public sealed class ModEntry : Mod
{
    internal static ModEntry Instance { get; private set; } = null!;

    private ModConfig Config = null!;
    private GathererService Gatherer = null!;

    public override object GetApi()
    {
        return new GatherersApi();
    }

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        Log.Init(Monitor);
        Config = helper.ReadConfig<ModConfig>();
        Gatherer = new GathererService(Config);

        AssetEditor assetEditor = new(helper);
        helper.Events.Content.AssetRequested += assetEditor.OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.Player.Warped += OnWarped;

        ApplyHarmonyPatches();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        new GmcmIntegration(Helper, ModManifest, Config).Register();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Game1.IsMasterGame)
            return;

        MigrationService.Run();
        EnsureHarvestStatueRecipeUnlock();

        foreach (GameLocation location in LocationScanner.AllLocationsAndBuildingInteriors())
        {
            foreach (Chest chest in location.objects.Values.OfType<Chest>().Where(StorageMarker.IsHarvestStatue).ToList())
                Gatherer.HarvestLocation(location, chest, GathererKind.HarvestStatue);
        }

        GameLocation? islandWest = Game1.getLocationFromName("IslandWest");
        if (islandWest is not null)
        {
            foreach (Chest chest in islandWest.objects.Values.OfType<Chest>().Where(StorageMarker.IsParrotPot).ToList())
                Gatherer.HarvestLocation(islandWest, chest, GathererKind.ParrotPot);
        }
    }

    private void EnsureHarvestStatueRecipeUnlock()
    {
        Farmer player = Game1.MasterPlayer;
        bool canReceiveLetter = Config.ForceRecipeUnlock || player.mailReceived.Contains("hasPickedUpMagicInk");
        if (canReceiveLetter
            && !player.mailReceived.Contains(ModConstants.HarvestStatueMailKey)
            && !player.mailbox.Contains(ModConstants.HarvestStatueMailKey))
        {
            player.mailbox.Add(ModConstants.HarvestStatueMailKey);
        }

        if (player.mailReceived.Contains(ModConstants.HarvestStatueMailKey)
            && !player.craftingRecipes.ContainsKey(ModConstants.HarvestStatueRecipeKey))
        {
            player.craftingRecipes[ModConstants.HarvestStatueRecipeKey] = 0;
        }
    }

    private void OnWarped(object? sender, WarpedEventArgs e)
    {
        if (e.OldLocation.objects.Values.OfType<Chest>().Any(StorageMarker.IsHarvestStatue) && e.OldLocation.NameOrUniqueName != "CommunityCenter")
        {
            for (int i = e.OldLocation.characters.Count - 1; i >= 0; i--)
            {
                if (e.OldLocation.characters[i] is Junimo)
                    e.OldLocation.characters.RemoveAt(i);
            }
        }

        if (!Config.DoJunimosAppearAfterHarvest || e.NewLocation.NameOrUniqueName == "CommunityCenter")
            return;

        Chest? chest = e.NewLocation.objects.Values.OfType<Chest>().FirstOrDefault(StorageMarker.IsHarvestStatue);
        if (chest is null || StorageMarker.HasSpawned(chest))
            return;

        List<Vector2> harvestedTiles = StorageMarker.GetHarvestedTiles(chest);
        if (harvestedTiles.Count == 0)
            return;

        SpawnJunimos(e.NewLocation, chest, harvestedTiles);
    }

    private void SpawnJunimos(GameLocation location, Chest chest, List<Vector2> harvestedTiles)
    {
        int maxJunimosToSpawn = Config.MaxAmountOfJunimosToAppearAfterHarvest;
        if (maxJunimosToSpawn == -1)
            maxJunimosToSpawn = harvestedTiles.Count / 2;

        int upper = Math.Min(harvestedTiles.Count, maxJunimosToSpawn);
        int amount = upper >= 0 ? Game1.random.Next(upper / 4, upper) : 0;
        for (int i = 0; i < amount; i++)
        {
            Vector2 tile = location.getRandomTile();
            string? backType = location.doesTileHaveProperty((int)tile.X, (int)tile.Y, "Type", "Back");
            if (!location.CanItemBePlacedHere(tile) || backType is not ("Wood" or "Stone"))
                continue;

            Junimo junimo = new(tile * 64f, 6, false);
            if (!location.isCollidingPosition(junimo.GetBoundingBox(), Game1.viewport, junimo))
                location.characters.Add(junimo);
        }

        Game1.playSound("tinyWhip");
        StorageMarker.MarkSpawned(chest);
    }

    private void ApplyHarmonyPatches()
    {
        Harmony harmony = new(ModManifest.UniqueID);

        ApplyPatch(
            harmony,
            AccessTools.Method(typeof(SObject), nameof(SObject.placementAction), new[] { typeof(GameLocation), typeof(int), typeof(int), typeof(Farmer) }),
            prefix: new HarmonyMethod(typeof(PlacementPatch), nameof(PlacementPatch.Prefix)),
            postfix: new HarmonyMethod(typeof(PlacementPatch), nameof(PlacementPatch.Postfix)),
            "Object.placementAction(GameLocation, int, int, Farmer)");

        ApplyPatch(
            harmony,
            AccessTools.Method(typeof(Chest), nameof(Chest.draw), new[] { typeof(Microsoft.Xna.Framework.Graphics.SpriteBatch), typeof(int), typeof(int), typeof(float) }),
            prefix: new HarmonyMethod(typeof(ChestDrawPatch), nameof(ChestDrawPatch.Prefix)),
            postfix: null,
            "Chest.draw(SpriteBatch, int, int, float)");

        ApplyPatch(
            harmony,
            AccessTools.Method(typeof(Crop), nameof(Crop.harvest), new[] { typeof(int), typeof(int), typeof(HoeDirt), typeof(JunimoHarvester), typeof(bool) }),
            prefix: new HarmonyMethod(typeof(CropHarvestPatch), nameof(CropHarvestPatch.Prefix)),
            postfix: null,
            "Crop.harvest(int, int, HoeDirt, JunimoHarvester, bool)");
    }

    private static void ApplyPatch(Harmony harmony, MethodBase? target, HarmonyMethod? prefix, HarmonyMethod? postfix, string targetDescription)
    {
        if (target is null)
        {
            Log.Error($"Failed to find Harmony target {targetDescription}.");
            return;
        }

        try
        {
            harmony.Patch(target, prefix, postfix);
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to apply Harmony patch to {targetDescription}", ex);
        }
    }
}
