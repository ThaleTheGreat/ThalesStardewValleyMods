using System.Linq;
using Microsoft.Xna.Framework;
using ThaleTheGreat.PyromancersJourney.Framework;
using ThaleTheGreat.PyromancersJourney.Integrations;
using SpaceShared;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Locations;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.PyromancersJourney
{
    public class Mod : StardewModdingAPI.Mod
    {
        private const string ArcadeTileAction = "ThaleTheGreat.PyromancersJourney_FireArcadeGame";
        private const string CompletionMailFlag = "ThaleTheGreat.PyromancersJourney_Beaten";
        private const string PrizeMailFlag = "ThaleTheGreat.PyromancersJourney_PrizeClaimed";

        public static Mod Instance { get; private set; } = null!;
        public ModConfig Config { get; private set; } = new();

        private PendingRunState PendingRun;

        public override void Entry(IModHelper helper)
        {
            Mod.Instance = this;
            Log.Monitor = this.Monitor;
            this.Config = helper.ReadConfig<ModConfig>();

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Player.Warped += this.OnWarped;

            GameLocation.RegisterTileAction(ArcadeTileAction, OnActionActivated);

            helper.ConsoleCommands.Add("pyrojourney", "Start the minigame!", this.DoCommands);
        }

        public void QueueCompletedRun(bool usedInfiniteHealth)
        {
            this.PendingRun = usedInfiniteHealth
                ? PendingRunState.ShowIneligibleResult
                : PendingRunState.ShowEligibleResult;
        }

        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (this.PendingRun == PendingRunState.None
                || !Context.IsWorldReady
                || Game1.currentMinigame is not null
                || Game1.activeClickableMenu is not null)
            {
                return;
            }

            switch (this.PendingRun)
            {
                case PendingRunState.ShowIneligibleResult:
                    this.PendingRun = PendingRunState.None;
                    Game1.drawObjectDialogue("You won! Runs completed with Infinite Health do not count and do not award a prize.");
                    break;

                case PendingRunState.ShowEligibleResult:
                    if (Game1.player.hasOrWillReceiveMail(PrizeMailFlag))
                    {
                        this.PendingRun = PendingRunState.None;
                        Game1.drawObjectDialogue("You won!");
                    }
                    else
                    {
                        this.PendingRun = PendingRunState.DeliverPrize;
                        Game1.drawObjectDialogue("You won! Your prize is 25 Cinder Shards.");
                    }
                    break;

                case PendingRunState.DeliverPrize:
                    Game1.player.addItemByMenuIfNecessaryElseHoldUp(new SObject("848", 25));
                    Game1.player.mailReceived.Add(CompletionMailFlag);
                    Game1.player.mailReceived.Add(PrizeMailFlag);
                    this.PendingRun = PendingRunState.None;
                    break;
            }
        }

        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            IGenericModConfigMenuApi? gmcm = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (gmcm is null)
                return;

            gmcm.Register(
                this.ModManifest,
                reset: () => this.Config = new ModConfig(),
                save: () => this.Helper.WriteConfig(this.Config)
            );

            gmcm.AddBoolOption(
                this.ModManifest,
                getValue: () => this.Config.InfiniteHealth,
                setValue: value => this.Config.InfiniteHealth = value,
                name: () => "Infinite Health",
                tooltip: () => "Prevent all health loss in Pyromancer's Journey.",
                fieldId: nameof(ModConfig.InfiniteHealth)
            );

            gmcm.AddBoolOption(
                this.ModManifest,
                getValue: () => this.Config.ShowReticle,
                setValue: value => this.Config.ShowReticle = value,
                name: () => "Show Reticle",
                tooltip: () => "Show a dot at the center of the Pyromancer's Journey screen.",
                fieldId: nameof(ModConfig.ShowReticle)
            );

            gmcm.AddTextOption(
                this.ModManifest,
                getValue: () => this.Config.ReticleColor,
                setValue: value => this.Config.ReticleColor = value,
                name: () => "Reticle Color",
                tooltip: () => "Choose the reticle dot color.",
                allowedValues: ReticlePalette.Names,
                fieldId: nameof(ModConfig.ReticleColor)
            );
        }

        private bool OnActionActivated(GameLocation loc, string[] args, Farmer farmer, Point pos)
        {
            Game1.currentMinigame = new PyromancerMinigame();
            return true;
        }

        private void OnWarped(object? sender, WarpedEventArgs e)
        {
            if (e.NewLocation is VolcanoDungeon vd && vd.level.Value == 5)
            {
                var ts = vd.Map.TileSheets.FirstOrDefault(t => t.ImageSource.Contains("arcade-machine"));
                if (ts == null)
                {
                    ts = new xTile.Tiles.TileSheet(vd.Map, this.Helper.ModContent.GetInternalAssetName("assets/arcade-machine.png").BaseName, new xTile.Dimensions.Size(2, 2), new xTile.Dimensions.Size(16, 16));
                    ts.Id = "z" + ts.Id;
                    vd.Map.AddTileSheet(ts);
                    SetMapTile(vd, 31, 28, 3, "Buildings", ts.Id, ArcadeTileAction);
                    SetMapTile(vd, 31, 27, 1, "Front", ts.Id);
                    Game1.mapDisplayDevice.LoadTileSheet(ts);
                }
            }
        }

        private void DoCommands(string cmd, string[] args)
        {
            if (cmd == "pyrojourney")
            {
                if (!Context.IsPlayerFree)
                    Log.Info("You must have a save loaded and be not busy.");
                else
                    Game1.currentMinigame = new PyromancerMinigame();
            }
        }

        private static void SetMapTile(GameLocation location, int tileX, int tileY, int index, string layerName, string tileSheetId, string? action = null)
        {
            var layer = location.Map.GetLayer(layerName) ?? throw new global::System.InvalidOperationException($"Map layer '{layerName}' was not found.");
            var tileSheet = location.Map.GetTileSheet(tileSheetId) ?? throw new global::System.InvalidOperationException($"Map tilesheet '{tileSheetId}' was not found.");
            var tile = new xTile.Tiles.StaticTile(layer, tileSheet, xTile.Tiles.BlendMode.Alpha, index);
            layer.Tiles[tileX, tileY] = tile;
            if (action is not null && layerName == "Buildings")
                tile.Properties.Add("Action", action);
        }

        private enum PendingRunState
        {
            None,
            ShowEligibleResult,
            ShowIneligibleResult,
            DeliverPrize
        }
    }
}
