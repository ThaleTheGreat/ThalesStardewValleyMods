using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Spacechase.Shared.Patching;
using SpaceShared;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Pathfinding;
using ThaleTheGreat.SurfingFestival.Framework;
using ThaleTheGreat.SurfingFestival.Patches;
using xTile;
using xTile.Layers;
using xTile.Tiles;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.SurfingFestival
{
    public class Mod : StardewModdingAPI.Mod
    {
        public static Mod Instance { get; private set; } = null!;

        public const int SurfSpeed = 8;
        public const string ShopId = "ThaleTheGreat.SurfingFestival_Shop";
        public const string TrophyId = "ThaleTheGreat.SurfingFestival_SurfingTrophy";
        public const string InvitationMailId = "ThaleTheGreat.SurfingFestival_Invitation";
        private const string InvitationYearKey = "ThaleTheGreat.SurfingFestival/InvitationYear";
        private const string BonfireAction = "ThaleTheGreat.SurfingFestival_Bonfire";
        private const string SecretOfferingAction = "ThaleTheGreat.SurfingFestival_SecretOffering";
        internal const string HostMessageKey = "Strings\\StringsFromCSFiles:ThaleTheGreat.SurfingFestival.HostMessage";

        internal static BonfireState PlayerDidBonfire = BonfireState.NotDone;
        public static List<string> Racers = new();
        internal static Dictionary<string, RacerState> RacerState = new();
        public static string? RaceWinner;
        internal static bool RaceCourseActive;
        internal static List<Obstacle> Obstacles = new();
        private readonly Dictionary<long, int> remoteFacingDirections = new();
        private string raceSessionId = string.Empty;
        private long raceSnapshotSequence;
        private long lastAppliedSnapshotSequence = -1;
        private int nextObstacleId;
        private int lastSentFacing = -1;
        private long lastInputMilliseconds;

        public static Texture2D SurfboardTex = null!;
        public static Texture2D SurfboardWaterTex = null!;
        public static Texture2D StunTex = null!;
        public static Texture2D ObstaclesTex = null!;

        public static string FestivalName = "Surfing Festival";

        public override void Entry(IModHelper helper)
        {
            I18n.Init(helper.Translation);
            Mod.Instance = this;
            Log.Monitor = this.Monitor;

            Mod.SurfboardTex = helper.ModContent.Load<Texture2D>("assets/surfboards.png");
            Mod.SurfboardWaterTex = helper.ModContent.Load<Texture2D>("assets/surfboard-water.png");
            Mod.StunTex = helper.ModContent.Load<Texture2D>("assets/net-stun.png");
            Mod.ObstaclesTex = helper.ModContent.Load<Texture2D>("assets/obstacles.png");

            helper.Events.GameLoop.DayStarted += this.OnDayStarted;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Display.RenderedWorld += this.OnRenderedWorld;
            helper.Events.Multiplayer.ModMessageReceived += this.OnMessageReceived;
            helper.Events.Content.AssetRequested += this.OnAssetRequested;

            Event.RegisterCommand("warpSurfingRacers", EventCommand_WarpSurfingRacers);
            Event.RegisterCommand("warpSurfingRacersFinish", EventCommand_WarpSurfingRacersFinish);
            Event.RegisterCommand("awardSurfingPrize", EventCommand_AwardSurfingPrize);
            GameLocation.RegisterTileAction(BonfireAction, this.OnBonfireAction);
            GameLocation.RegisterTileAction(SecretOfferingAction, this.OnSecretOfferingAction);

            HarmonyPatcher.Apply(this,
                new EventPatcher()
            );
        }

        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo("Data\\Festivals\\summer5"))
            {
                e.LoadFrom(() =>
                {
                    var data = this.BuildFestivalData();
                    Mod.FestivalName = data["name"];
                    return data;
                }, AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Maps\\Beach-Surfing"))
            {
                e.LoadFrom(() =>
                {
                    Map map = this.Helper.ModContent.Load<Map>("assets/Beach.tbin");
                    NormalizeMapActions(map);
                    PlaceBonfire(map, 30, 5, false);
                    return map;
                }, AssetLoadPriority.Exclusive);
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Maps\\surfing"))
                e.LoadFromModFile<Texture2D>("assets/surfing.png", AssetLoadPriority.Exclusive);
            else if (e.NameWithoutLocale.IsEquivalentTo("Strings\\StringsFromCSFiles"))
            {
                e.Edit(asset =>
                {
                    asset.AsDictionary<string, string>().Data["ThaleTheGreat.SurfingFestival.HostMessage"] =
                        $"$q -1 null#{I18n.Race_Start_Question()}#$r -1 0 yes#{I18n.Race_Start_Yes()}#$r -1 0 no#{I18n.Race_Start_No()}";
                });
            }
            else if (e.NameWithoutLocale.IsEquivalentTo("Data\\Festivals\\FestivalDates"))
                e.Edit(static (asset) =>
                {
                    asset.AsDictionary<string, string>().Data.Add("summer5", Mod.FestivalName);
                });
        }

        /// <summary>Build the festival data file from the mod translations.</summary>
        private IDictionary<string, string> BuildFestivalData()
        {
            // base data
            var data = new Dictionary<string, string>
            {
                ["name"] = I18n.Festival_Name(),
                ["conditions"] = "Beach/900 1400",
                ["set-up"] = "event2/-1000 -1000/farmer 38 3 2/changeToTemporaryMap Beach-Surfing/loadActors Set-Up/animate Robin false true 500 20 21 20 22/animate Demetrius false true 500 24 25 24 26/viewport 38 2 clamp true/pause 1000/playerControl surfing",
                ["mainEvent"] = $@"pause 500/playMusic none/pause 500/globalFade/viewport -1000 -1000/loadActors MainEvent/warpSurfingRacers/viewport 18 57 true unfreeze/pause 2000/message ""{I18n.Race_Instructions()}""/speak Lewis ""{I18n.Race_LewisStart_0()}""/speak Lewis ""{I18n.Race_LewisStart_1()}""/speak Lewis ""{I18n.Race_LewisStart_2()}""/waitForOtherPlayers actualRace/playSound whistle/playMusic cowboy_outlawsong/playerControl surfingRace",
                ["afterSurfingRace"] = "pause 100/playSound whistle/waitForOtherPlayers endContest/pause 1000/globalFade/viewport -1000 -1000/playMusic event1/loadActors PostEvent/warpSurfingRacersFinish/pause 1000/viewport 34 12 true/pause 2000/speak Lewis \"{{winDialog}}\"/awardSurfingPrize/pause 600/viewport move 1 0 5000/pause 2000/globalFade/viewport -1000 -1000/waitForOtherPlayers festivalEnd/end",
                ["HarveyWin"] = I18n.Race_Winner_Harvey(),
                ["EmilyWin"] = I18n.Race_Winner_Emily(),
                ["MaruWin"] = I18n.Race_Winner_Maru(),
                ["ShaneWin"] = I18n.Race_Winner_Shane()
            };

            // NPC dialogue strings
            foreach (var translation in this.Helper.Translation.GetTranslations())
            {
                const string prefix = "npc.";
                if (translation.Key.StartsWith(prefix))
                    data[translation.Key.Substring(prefix.Length)] = translation.ToString();
            }

            return data;
        }

        private void OnDayStarted(object? sender, DayStartedEventArgs e)
        {
            if (Game1.currentSeason != "summer" || Game1.dayOfMonth != 1)
                return;

            string year = Game1.year.ToString();
            if (Game1.player.modData.TryGetValue(InvitationYearKey, out string? sentYear) && sentYear == year)
                return;

            Game1.player.mailReceived.Remove(InvitationMailId);
            if (!Game1.player.mailbox.Contains(InvitationMailId))
                Game1.player.mailbox.Add(InvitationMailId);
            Game1.player.modData[InvitationYearKey] = year;
        }

        private Event? PrevEvent;
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            if (++Mod.SurfboardWaterAnimTimer >= 5)
            {
                Mod.SurfboardWaterAnimTimer = 0;
                if (++Mod.SurfboardWaterAnim >= 3)
                    Mod.SurfboardWaterAnim = 0;
            }
            if (++this.ItemBobbleTimer >= 25)
            {
                this.ItemBobbleTimer = 0;
                if (++this.ItemBobbleFrame >= 4)
                    this.ItemBobbleFrame = 0;
            }
            ++this.NetBobTimer;

            Event? observedEvent = Game1.CurrentEvent;
            if (observedEvent?.FestivalName == Mod.FestivalName && this.PrevEvent?.FestivalName != Mod.FestivalName)
                Mod.PlayerDidBonfire = BonfireState.NotDone;

            this.PrevEvent = observedEvent;
            if (observedEvent is not Event currentEvent || currentEvent.FestivalName != Mod.FestivalName)
            {
                Mod.RaceCourseActive = false;
                return;
            }

            if (currentEvent.playerControlSequenceID != "surfingRace")
                return;

            if (!Context.IsMainPlayer)
            {
                this.SendRaceInput(useItem: false);
                return;
            }

            var rand = new Random();
            foreach (var actor in currentEvent.actors)
            {
                if (Mod.Racers.Contains(actor.Name))
                    continue;
                if (rand.Next(30 * currentEvent.actors.Count / 2) == 0)
                    actor.jumpWithoutSound();
            }

            foreach (var obstacle in Mod.Obstacles)
            {
                if (obstacle.Type is ObstacleType.HomingProjectile or ObstacleType.FirstPlaceProjectile)
                {
                    var targetRect = currentEvent.getCharacterByName(obstacle.HomingTarget).GetBoundingBox().Center;
                    var target = new Vector2(targetRect.X, targetRect.Y);
                    var current = obstacle.Position;

                    int speed = 15;
                    if (obstacle.Type == ObstacleType.FirstPlaceProjectile)
                        speed = 25;

                    if (Vector2.Distance(target, current) < speed)
                    {
                        current = target;
                    }
                    else
                    {
                        var unit = (target - current);
                        unit.Normalize();

                        current += unit * speed;
                    }
                    obstacle.Position = current;
                }
            }

            Vector2[][] switchDirs = new[]
            {
                new Vector2[]
                {
                    new(16, 60),
                    new(15, 61),
                    new(14, 62),
                    new(13, 63),
                    new(12, 64),
                    new(11, 65),
                    new(10, 66),
                    new(9, 67),
                    new(8, 68),
                    new(7, 69)
                },
                new Vector2[]
                {
                    new(16, 58),
                    new(15, 57),
                    new(14, 56),
                    new(13, 55),
                    new(12, 54),
                    new(11, 53),
                    new(10, 52),
                    new(9, 51),
                    new(8, 50),
                    new(7, 49)
                },
                new Vector2[]
                {
                    new(133, 58),
                    new(134, 57),
                    new(135, 56),
                    new(136, 55),
                    new(137, 54),
                    new(138, 53),
                    new(139, 52),
                    new(140, 51),
                    new(141, 50),
                    new(142, 49)
                },
                new Vector2[]
                {
                    new(133, 60),
                    new(134, 61),
                    new(135, 62),
                    new(136, 63),
                    new(137, 64),
                    new(138, 65),
                    new(139, 66),
                    new(140, 67),
                    new(141, 68),
                    new(142, 69)
                }
            };

            foreach (string racerName in Mod.Racers)
            {
                var state = Mod.RacerState[racerName];
                Character? racer = currentEvent.getCharacterByName(racerName);
                if (racer is null)
                    continue;

                for (int i = Mod.Obstacles.Count - 1; i >= 0; --i)
                {
                    var obstacle = Mod.Obstacles[i];
                    if (obstacle.GetBoundingBox().Intersects(racer.GetBoundingBox()))
                    {
                        switch (obstacle.Type)
                        {
                            case ObstacleType.Item:
                                if (!state.CurrentItem.HasValue && state.ItemObtainTimer == -1 && state.ItemUsageTimer == -1)
                                {
                                    state.ItemObtainTimer = 120;
                                }
                                else continue;
                                break;
                            case ObstacleType.Net:
                                if (!(state.CurrentItem == SurfItem.Invincibility && state.ItemUsageTimer >= 0))
                                {
                                    state.StunTimer = 90;
                                }
                                break;
                            case ObstacleType.Rock:
                                if (!(state.CurrentItem == SurfItem.Invincibility && state.ItemUsageTimer >= 0))
                                {
                                    if (state.SlowdownTimer == -1)
                                        state.Speed /= 2;
                                    state.SlowdownTimer = 150;
                                }
                                // spawn particles
                                break;
                            case ObstacleType.FirstPlaceProjectile:
                            case ObstacleType.HomingProjectile:
                                if (racerName != obstacle.HomingTarget)
                                    continue;
                                if (!(state.CurrentItem == SurfItem.Invincibility && state.ItemUsageTimer >= 0))
                                {
                                    if (state.SlowdownTimer == -1)
                                        state.Speed /= 2;
                                    state.SlowdownTimer = obstacle.Type == ObstacleType.HomingProjectile ? 90 : 180;
                                }
                                if (obstacle.Type == ObstacleType.FirstPlaceProjectile)
                                    Game1.playSound("thunder");
                                if (obstacle.Type == ObstacleType.HomingProjectile)
                                    currentEvent?.underwaterSprites?.Remove(obstacle.UnderwaterSprite);
                                break;
                        }
                        Mod.Obstacles.Remove(obstacle);
                    }
                }

                if (state.ItemObtainTimer >= 0)
                {
                    --state.ItemObtainTimer;
                    if (state.ItemObtainTimer != 0 && state.ItemObtainTimer % 5 == 0)
                    {
                        if (racer == Game1.player)
                            Game1.playSound("shiny4");
                    }
                    else if (state.ItemObtainTimer == -1)
                    {
                        while (true)
                        {
                            state.CurrentItem = (SurfItem)Game1.recentMultiplayerRandom.Next(Enum.GetValues(typeof(SurfItem)).Length);
                            if (Mod.GetRacePlacement()[Mod.GetRacePlacement().Count - 1] == racerName && state.CurrentItem == SurfItem.FirstPlaceProjectile)
                            { }
                            else break;
                        }
                    }
                }
                if (state.ItemUsageTimer >= 0)
                {
                    if (--state.ItemUsageTimer < 0)
                    {
                        if (state.CurrentItem is SurfItem expiredItem)
                        {
                            if (expiredItem == SurfItem.Boost)
                            {
                                state.Speed /= 2;
                                racer.stopGlowing();
                            }
                            else if (expiredItem == SurfItem.Invincibility)
                            {
                                state.AddedSpeed -= 3;
                                racer.stopGlowing();
                            }
                        }
                        state.CurrentItem = null;
                    }
                    else
                    {
                        if (state.CurrentItem == SurfItem.Invincibility)
                            racer.glowingColor = Mod.MyGetPrismaticColor();
                    }
                }
                if (state.SlowdownTimer >= 0)
                {
                    if (--state.SlowdownTimer < 0)
                    {
                        state.Speed *= 2;
                    }
                }
                if (state.StunTimer >= 0)
                {
                    --state.StunTimer;
                    if (racer == Game1.player)
                    {
                        Game1.player.controller = new PathFindController(Game1.player, Game1.currentLocation, Game1.player.TilePoint, Game1.player.FacingDirection)
                        {
                            pathToEndPoint = null
                        };
                        Game1.player.Halt();
                    }
                    continue;
                }

                if (racer is Farmer farmer)
                {
                    int requestedFacing = farmer == Game1.player
                        ? Game1.player.FacingDirection
                        : this.remoteFacingDirections.GetValueOrDefault(farmer.UniqueMultiplayerID, state.Facing);

                    int opposite = state.Facing switch
                    {
                        Game1.up => Game1.down,
                        Game1.down => Game1.up,
                        Game1.left => Game1.right,
                        Game1.right => Game1.left,
                        _ => state.Facing
                    };

                    if (requestedFacing != state.Facing && requestedFacing != opposite)
                    {
                        racer.faceDirection(requestedFacing);
                        int wasSpeed = racer.speed;
                        racer.speed = (state.Speed + state.AddedSpeed) / 2;
                        racer.tryToMoveInDirection(racer.FacingDirection, true, 0, false);
                        racer.speed = wasSpeed;
                    }

                    if (farmer == Game1.player)
                    {
                        Game1.player.controller = new PathFindController(Game1.player, Game1.currentLocation, Game1.player.TilePoint, Game1.player.FacingDirection)
                        {
                            pathToEndPoint = null
                        };
                        Game1.player.Halt();
                    }
                }
                else if (racer is NPC npc)
                {
                    npc.CurrentDialogue.Clear();

                    int checkDirX = 0, checkDirY = 0;
                    int inDir = 0, outDir = 0;
                    switch (state.Facing)
                    {
                        case Game1.up: checkDirY = -1; inDir = Game1.right; outDir = Game1.left; break;
                        case Game1.down: checkDirY = 1; inDir = Game1.left; outDir = Game1.right; break;
                        case Game1.left: checkDirX = -1; inDir = Game1.up; outDir = Game1.down; break;
                        case Game1.right: checkDirX = 1; inDir = Game1.down; outDir = Game1.up; break;
                    }

                    bool foundObstacle = false;
                    for (int i = 0; i < 7; ++i)
                    {
                        var bb = racer.GetBoundingBox();
                        bb.X += checkDirX * Game1.tileSize;
                        bb.Y += checkDirY * Game1.tileSize;

                        foreach (var obstacle in Mod.Obstacles)
                        {
                            if ((obstacle.Type is ObstacleType.Net or ObstacleType.Rock) &&
                                 obstacle.GetBoundingBox().Intersects(bb))
                            {
                                foundObstacle = true;
                                break;
                            }
                        }

                        if (foundObstacle)
                            break;
                    }

                    var r = new Random(((int)Game1.uniqueIDForThisGame + (int)Game1.stats.DaysPlayed) ^ racerName.GetHashCode() + (int)racer.Tile.X / 15);
                    int facingDir = -1;
                    if (foundObstacle)
                        facingDir = (r.Next(2) == 0) ? inDir : outDir;
                    else
                    {
                        switch (r.Next(3))
                        {
                            case 0: facingDir = inDir; break;
                            case 1: break;
                            case 2: facingDir = outDir; break;
                        }
                    }

                    facingDir = state.Facing switch
                    {
                        // Fix some times they get stuck on the inner wall
                        Game1.up when racer.Position.X >= 16 * Game1.tileSize + 1 => Game1.left,
                        Game1.down when racer.Position.X <= 133 * Game1.tileSize => Game1.right,
                        Game1.left when racer.Position.Y <= 60 * Game1.tileSize => Game1.down,
                        Game1.right when racer.Position.Y >= 58 * Game1.tileSize + 1 => Game1.up,
                        _ => facingDir
                    };

                    if (facingDir != -1)
                    {
                        racer.faceDirection(facingDir);

                        int oldSpeed = racer.speed;
                        racer.speed = (state.Speed + state.AddedSpeed) / 2;
                        racer.tryToMoveInDirection(racer.FacingDirection, racer is Farmer, 0, false);
                        racer.speed = oldSpeed;
                    }

                    if (state.CurrentItem.HasValue && state.ItemObtainTimer == -1 && state.ItemUsageTimer == -1)
                    {
                        state.ShouldUseItem = true;
                    }
                }

                if (state.ShouldUseItem)
                {
                    state.ShouldUseItem = false;
                    if (state.CurrentItem is not SurfItem currentItem)
                        continue;

                    switch (currentItem)
                    {
                        case SurfItem.Boost:
                            state.Speed *= 2;
                            state.ItemUsageTimer = 80;
                            racer.startGlowing(Color.DarkViolet, false, 0.05f);
                            Game1.playSound("wand");
                            break;
                        case SurfItem.HomingProjectile:
                            List<string> placement = Mod.GetRacePlacement();
                            string target = placement[placement.Count - 2];
                            bool next = false;
                            foreach (string other in placement)
                            {
                                if (other == racerName)
                                    next = true;
                                else if (next)
                                {
                                    target = other;
                                    break;
                                }
                            }

                            state.CurrentItem = null;
                            TemporaryAnimatedSprite tas = new TemporaryAnimatedSprite(128, 0, 0, 0, new Vector2(), false, false);
                            Mod.Obstacles.Add(new Obstacle
                            {
                                Id = ++this.nextObstacleId,
                                Type = ObstacleType.HomingProjectile,
                                Position = new Vector2(racer.GetBoundingBox().Center.X, racer.GetBoundingBox().Center.Y),
                                HomingTarget = target,
                                UnderwaterSprite = tas
                            });
                            currentEvent?.underwaterSprites?.Add(tas);
                            Game1.playSound("throwDownITem");
                            break;
                        case SurfItem.FirstPlaceProjectile:
                            state.CurrentItem = null;
                            Mod.Obstacles.Add(new Obstacle
                            {
                                Id = ++this.nextObstacleId,
                                Type = ObstacleType.FirstPlaceProjectile,
                                Position = new Vector2(racer.GetBoundingBox().Center.X, racer.GetBoundingBox().Center.Y),
                                HomingTarget = Mod.GetRacePlacement()[Mod.GetRacePlacement().Count - 1]
                            });
                            Game1.playSound("fishEscape");
                            break;
                        case SurfItem.Invincibility:
                            state.ItemUsageTimer = 150;
                            if (state.SlowdownTimer > 0)
                                state.SlowdownTimer = 0;
                            if (state.StunTimer > 0)
                                state.StunTimer = 0;
                            racer.startGlowing(Mod.MyGetPrismaticColor(), false, 0);
                            racer.glowingTransparency = 1;
                            state.AddedSpeed += 3;
                            Game1.playSound("yoba");
                            break;
                    }
                }

                // Fix some times they get stuck on the inner wall
                int go = state.Facing switch
                {
                    Game1.up when racer.Position.X >= 16 * Game1.tileSize + 1 => Game1.left,
                    Game1.down when racer.Position.X <= 133 * Game1.tileSize => Game1.right,
                    Game1.left when racer.Position.Y <= 60 * Game1.tileSize => Game1.down,
                    Game1.right when racer.Position.Y >= 58 * Game1.tileSize + 1 => Game1.up,
                    _ => -1
                };

                if (go != -1)
                {
                    racer.faceDirection(go);

                    int oldSpeed = racer.speed;
                    racer.speed = (state.Speed + state.AddedSpeed) / 2;
                    racer.tryToMoveInDirection(racer.FacingDirection, racer is Farmer, 0, false);
                    racer.speed = oldSpeed;
                }

                racer.faceDirection(state.Facing);

                {
                    int wasSpeed = racer.speed;
                    racer.speed = state.Speed + state.AddedSpeed;
                    racer.tryToMoveInDirection(racer.FacingDirection, racer is Farmer, 0, false);
                    racer.speed = wasSpeed;
                }

                for (int i = 0; i < switchDirs.Length; ++i)
                {
                    var switchDir = switchDirs[i];
                    foreach (var tile in switchDir)
                    {
                        if (racer.GetBoundingBox().Intersects(new Rectangle((int)tile.X * Game1.tileSize, (int)tile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize)))
                        {
                            racer.faceDirection(i);
                            state.Facing = i;
                        }
                    }
                }

                if (racer.Tile.X >= 132 && racer.Position.Y >= 59 * Game1.tileSize)
                {
                    state.ReachedHalf = true;
                }
                if (state.ReachedHalf && racer.Tile.X >= 17 && racer.Position.Y <= 59 * Game1.tileSize - 1)
                {
                    ++state.LapsDone;
                    state.ReachedHalf = false;

                    if (state.LapsDone >= 2 && Mod.RaceWinner == null)
                    {
                        this.FinishRace(currentEvent, racerName);
                        this.BroadcastRaceSnapshot(raceActive: false);
                    }
                }
            }

            if (e.IsMultipleOf(3) && Mod.RaceWinner is null)
                this.BroadcastRaceSnapshot(raceActive: true);
        }

        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (this.TryHandleFestivalInteraction(e))
                return;

            if (Game1.CurrentEvent?.FestivalName != Mod.FestivalName || Game1.CurrentEvent?.playerControlSequenceID != "surfingRace")
                return;

            string racerKey = "farmer" + Utility.getFarmerNumberFromFarmer(Game1.player);
            if (!Mod.RacerState.TryGetValue(racerKey, out RacerState? state))
                return;

            if (e.Button.IsActionButton() && state.CurrentItem.HasValue && state.ItemObtainTimer == -1 && state.ItemUsageTimer == -1)
            {
                this.Helper.Input.Suppress(e.Button);
                if (Context.IsMainPlayer)
                    state.ShouldUseItem = true;
                else
                    this.SendRaceInput(useItem: true);
            }
        }

        private bool TryHandleFestivalInteraction(ButtonPressedEventArgs e)
        {
            if (!e.Button.IsActionButton()
                || Game1.activeClickableMenu != null
                || Game1.CurrentEvent?.FestivalName != Mod.FestivalName
                || Game1.CurrentEvent?.playerControlSequenceID == "surfingRace")
            {
                return false;
            }

            Vector2 cursorTile = e.Cursor.GrabTile;
            Vector2 playerTile = Game1.player.Tile;
            Vector2 facingTile = GetFacingTile(playerTile, Game1.player.FacingDirection);

            if (!TryFindFestivalAction(Game1.currentLocation, cursorTile, facingTile, playerTile, out string action, out Point actionTile))
            {
                NPC? pierre = Game1.CurrentEvent?.getActorByName("Pierre");
                if (pierre is null
                    || TileDistance(playerTile, pierre.Tile) > 3f
                    || Math.Min(TileDistance(cursorTile, pierre.Tile), TileDistance(facingTile, pierre.Tile)) > 2f)
                {
                    return false;
                }

                action = $"OpenShop {ShopId}";
                actionTile = new Point((int)pierre.Tile.X, (int)pierre.Tile.Y);
            }

            this.Helper.Input.Suppress(e.Button);
            if (IsFestivalShopAction(action))
            {
                Utility.TryOpenShopMenu(ShopId, Game1.currentLocation, null, null, true, true, null);
                return true;
            }

            Game1.currentLocation.performAction(action, Game1.player, new xTile.Dimensions.Location(actionTile.X, actionTile.Y));
            return true;
        }

        private static bool TryFindFestivalAction(GameLocation location, Vector2 cursorTile, Vector2 facingTile, Vector2 playerTile, out string action, out Point actionTile)
        {
            action = string.Empty;
            actionTile = Point.Zero;
            Layer? buildings = location.Map.GetLayer("Buildings");
            if (buildings is null)
                return false;

            float bestDistance = float.MaxValue;

            for (int x = 0; x < buildings.LayerWidth; x++)
            {
                for (int y = 0; y < buildings.LayerHeight; y++)
                {
                    Tile tile = buildings.Tiles[x, y];
                    if (tile?.Properties.TryGetValue("Action", out var actionProperty) != true)
                        continue;

                    string candidateAction = NormalizeFestivalAction(actionProperty?.ToString() ?? string.Empty);
                    if (candidateAction != BonfireAction
                        && candidateAction != SecretOfferingAction
                        && !IsFestivalShopAction(candidateAction))
                    {
                        continue;
                    }

                    Vector2 candidateTile = new(x, y);
                    float targetDistance = Math.Min(TileDistance(cursorTile, candidateTile), TileDistance(facingTile, candidateTile));
                    if (targetDistance > 2f || TileDistance(playerTile, candidateTile) > 3f || targetDistance >= bestDistance)
                        continue;

                    bestDistance = targetDistance;
                    action = candidateAction;
                    actionTile = new Point(x, y);
                }
            }

            return bestDistance < float.MaxValue;
        }

        private static Vector2 GetFacingTile(Vector2 playerTile, int facingDirection)
        {
            return facingDirection switch
            {
                0 => playerTile + new Vector2(0f, -1f),
                1 => playerTile + new Vector2(1f, 0f),
                2 => playerTile + new Vector2(0f, 1f),
                3 => playerTile + new Vector2(-1f, 0f),
                _ => playerTile
            };
        }

        private static float TileDistance(Vector2 first, Vector2 second)
        {
            return Math.Max(Math.Abs(first.X - second.X), Math.Abs(first.Y - second.Y));
        }

        private static bool IsFestivalShopAction(string action)
        {
            return action == $"OpenShop {ShopId}"
                || action == $"Shop {ShopId}"
                || action == "Shop SurfingFestival.Shop"
                || action == "OpenShop SurfingFestival.Shop";
        }

        private static string NormalizeFestivalAction(string action)
        {
            if (action == $"Shop {ShopId}")
                return $"OpenShop {ShopId}";

            return action switch
            {
                "SurfingBonfire" => BonfireAction,
                "Shop SurfingFestival.Shop" => $"OpenShop {ShopId}",
                "OpenShop SurfingFestival.Shop" => $"OpenShop {ShopId}",
                "SurfingFestival.SecretOffering" => SecretOfferingAction,
                _ => action
            };
        }

        private int ItemBobbleFrame;
        private int ItemBobbleTimer;
        private uint NetBobTimer;
        public void DrawObstacles(SpriteBatch b)
        {
            if (Game1.CurrentEvent?.FestivalName != Mod.FestivalName || Game1.CurrentEvent?.playerControlSequenceID != "surfingRace")
                return;

            foreach (var obstacle in Mod.Obstacles)
            {
                Texture2D? srcTex = null;
                Rectangle srcRect = new Rectangle();
                Vector2 origin = new Vector2();
                Vector2 offset = new Vector2();
                switch (obstacle.Type)
                {
                    case ObstacleType.Item:
                        srcTex = Mod.ObstaclesTex;
                        srcRect = new Rectangle(48 + 16 * this.ItemBobbleFrame, 0, 16, 16);
                        break;
                    case ObstacleType.Net:
                        srcTex = Mod.ObstaclesTex;
                        srcRect = new Rectangle(0, 48, 48, 32);
                        origin = new Vector2(0, 16);
                        offset = new Vector2(0, (float)Math.Sin(this.NetBobTimer / 10) * 3);
                        break;
                    case ObstacleType.Rock:
                        srcTex = Mod.ObstaclesTex;
                        srcRect = new Rectangle(0, 0, 48, 48);
                        origin = new Vector2(0, 32);
                        break;
                    case ObstacleType.HomingProjectile:
                        // These are rendered differently, underneath the water
                        obstacle.UnderwaterSprite.Position = new Vector2(obstacle.GetBoundingBox().Center.X, obstacle.GetBoundingBox().Center.Y);
                        /*
                        srcTex = Game1.objectSpriteSheet;
                        srcRect = Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 128, 16, 16);
                        origin = new Vector2(8, 8);
                        */
                        break;
                    case ObstacleType.FirstPlaceProjectile:
                        srcTex = Game1.mouseCursors;
                        srcRect = new Rectangle(643, 1043, 61, 92);
                        origin = new Vector2(662 - 643, 1134 - 1043);

                        var target = Game1.CurrentEvent.getCharacterByName(obstacle.HomingTarget);
                        if (Vector2.Distance(new Vector2(obstacle.GetBoundingBox().Center.X, obstacle.GetBoundingBox().Center.Y),
                                               new Vector2(target.GetBoundingBox().Center.X, target.GetBoundingBox().Center.Y))
                             >= Game1.tileSize * 2)
                            srcRect.Height = 35;
                        break;

                }
                float depth = (obstacle.Position.Y + srcRect.Height - origin.Y) / 10000f;

                if (srcTex == null)
                    continue;

                b.Draw(srcTex, Game1.GlobalToLocal(obstacle.Position + offset), srcRect, Color.White, 0, origin, Game1.pixelZoom, SpriteEffects.None, depth);
                //e.SpriteBatch.Draw(Game1.staminaRect, Game1.GlobalToLocal(Game1.viewport, obstacle.GetBoundingBox()), Color.Red);
            }
        }

        private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
        {
            if (Game1.CurrentEvent?.FestivalName != Mod.FestivalName || Game1.CurrentEvent?.playerControlSequenceID != "surfingRace")
                return;

            Game1.InUIMode(() => this.DrawRaceHud(e.SpriteBatch));
        }

        private void DrawRaceHud(SpriteBatch b)
        {
            string racerKey = "farmer" + Utility.getFarmerNumberFromFarmer(Game1.player);
            if (!Mod.RacerState.TryGetValue(racerKey, out RacerState? state))
                return;

            var pos = new Vector2(Game1.uiViewport.Width - (74 + 14) * 2 - 25, 25);
            b.Draw(Game1.mouseCursors, pos, new Rectangle(603, 414, 74, 74), Color.White, 0, Vector2.Zero, 2, SpriteEffects.None, 0);
            b.Draw(Game1.mouseCursors, new Vector2(pos.X - 14 * 2, pos.Y + 74 * 2), new Rectangle(589, 488, 102, 18), Color.White, 0, Vector2.Zero, 2, SpriteEffects.None, 0);
            if (state.CurrentItem.HasValue || state.ItemObtainTimer >= 0)
            {
                int displayItem = state.ItemObtainTimer / 5 % Enum.GetValues(typeof(SurfItem)).Length;
                if (state.CurrentItem.HasValue)
                    displayItem = (int)state.CurrentItem.Value;

                Texture2D? displayTex = null;
                Rectangle displayRect = Rectangle.Empty;
                Color displayColor = Color.White;
                string? displayName = null;
                switch (displayItem)
                {
                    case (int)SurfItem.Boost:
                        displayTex = Game1.objectSpriteSheet;
                        displayRect = Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 434, 16, 16);
                        displayName = I18n.Item_Boost();
                        break;

                    case (int)SurfItem.HomingProjectile:
                        displayTex = Game1.objectSpriteSheet;
                        displayRect = Game1.getSourceRectForStandardTileSheet(Game1.objectSpriteSheet, 128, 16, 16);
                        displayName = I18n.Item_HomingProjectile();
                        break;

                    case (int)SurfItem.FirstPlaceProjectile:
                        displayTex = Game1.mouseCursors;
                        displayRect = new Rectangle(643, 1043, 61, 61);
                        displayName = I18n.Item_FirstPlaceProjectile();
                        break;

                    case (int)SurfItem.Invincibility:
                        displayTex = Game1.content.Load<Texture2D>("Characters\\Junimo");
                        displayRect = new Rectangle(80, 80, 16, 16);
                        displayColor = Mod.MyGetPrismaticColor();
                        displayName = I18n.Item_Invincibility();
                        break;
                }

                if (displayTex is not null && displayName is not null)
                {
                    b.Draw(displayTex, new Rectangle((int)pos.X + 42, (int)pos.Y + 42, 64, 64), displayRect, displayColor);
                    b.DrawString(Game1.smallFont, displayName, new Vector2((int)pos.X + 74, (int)pos.Y + 74 * 2 + 6), Game1.textColor, 0, new Vector2(Game1.smallFont.MeasureString(displayName).X / 2, 0), 0.85f, SpriteEffects.None, 0.88f);
                }
            }

            string lapsStr = I18n.Ui_Laps(laps: state.LapsDone);
            SpriteText.drawStringHorizontallyCenteredAt(b, lapsStr, (int)pos.X + 74, (int)pos.Y + 74 * 2 + 18 * 2 + 8);

            string rankingLabel = I18n.Ui_Ranking();
            SpriteText.drawStringHorizontallyCenteredAt(b, rankingLabel, (int)pos.X + 74, Game1.uiViewport.Height - 128 - (Mod.Racers.Count - 1) / 5 * 40);

            int i = 0;
            var sortedRacers = Mod.GetRacePlacement();
            sortedRacers.Reverse();
            foreach (string racerName in sortedRacers)
            {
                Character? racer = Game1.CurrentEvent?.getCharacterByName(racerName);
                if (racer is null)
                    continue;

                int x = (int)pos.X + 74 - SpriteText.getWidthOfString(rankingLabel) / 2 + i % 5 * 40 - 20;
                int y = Game1.uiViewport.Height - 64 + i / 5 * 50;

                if (racer is NPC)
                {
                    var rect = new Rectangle(0, 3, 16, 16);
                    b.Draw(racer.Sprite.Texture, new Vector2(x, y), rect, Color.White, 0, Vector2.Zero, 2, SpriteEffects.None, 1);
                }
                else if (racer is Farmer farmer)
                {
                    farmer.FarmerRenderer.drawMiniPortrat(b, new Vector2(x, y), 0, 2, 0, farmer);
                }
                ++i;
            }
        }

        private void OnMessageReceived(object? sender, ModMessageReceivedEventArgs e)
        {
            if (e.FromModID != this.ModManifest.UniqueID)
                return;

            if (e.Type == RaceInputMessage.Type && Context.IsMainPlayer)
            {
                RaceInputMessage msg = e.ReadAs<RaceInputMessage>();
                if (msg.SessionId != this.raceSessionId || Game1.CurrentEvent?.playerControlSequenceID != "surfingRace")
                    return;

                Farmer? senderFarmer = Game1.GetPlayer(e.FromPlayerID, onlyOnline: true);
                if (senderFarmer is null)
                    return;

                string racerName = "farmer" + Utility.getFarmerNumberFromFarmer(senderFarmer);
                if (!Mod.RacerState.TryGetValue(racerName, out RacerState? state))
                    return;

                if (msg.FacingDirection is >= Game1.up and <= Game1.right)
                    this.remoteFacingDirections[e.FromPlayerID] = msg.FacingDirection;

                if (msg.UseItem && state.CurrentItem.HasValue && state.ItemObtainTimer == -1 && state.ItemUsageTimer == -1)
                    state.ShouldUseItem = true;
                return;
            }

            if (e.Type == RaceSnapshotMessage.Type && !Context.IsMainPlayer && e.FromPlayerID == Game1.MasterPlayer.UniqueMultiplayerID)
            {
                this.ApplyRaceSnapshot(e.ReadAs<RaceSnapshotMessage>());
            }
        }

        private void SendRaceInput(bool useItem)
        {
            if (Context.IsMainPlayer || string.IsNullOrEmpty(this.raceSessionId))
                return;

            int facing = Game1.player.FacingDirection;
            long now = (long)Game1.currentGameTime.TotalGameTime.TotalMilliseconds;
            if (!useItem && facing == this.lastSentFacing && now - this.lastInputMilliseconds < 250)
                return;

            this.lastSentFacing = facing;
            this.lastInputMilliseconds = now;
            this.Helper.Multiplayer.SendMessage(
                new RaceInputMessage
                {
                    SessionId = this.raceSessionId,
                    FacingDirection = facing,
                    UseItem = useItem
                },
                RaceInputMessage.Type,
                new[] { this.ModManifest.UniqueID },
                new[] { Game1.MasterPlayer.UniqueMultiplayerID });
        }

        private void BroadcastRaceSnapshot(bool raceActive)
        {
            if (!Context.IsMainPlayer || string.IsNullOrEmpty(this.raceSessionId) || Game1.CurrentEvent is not Event currentEvent)
                return;

            var snapshot = new RaceSnapshotMessage
            {
                SessionId = this.raceSessionId,
                Sequence = ++this.raceSnapshotSequence,
                RaceActive = raceActive,
                Winner = Mod.RaceWinner,
                Racers = new List<string>(Mod.Racers)
            };

            foreach (string racerName in Mod.Racers)
            {
                Character? racer = currentEvent.getCharacterByName(racerName);
                if (racer is null || !Mod.RacerState.TryGetValue(racerName, out RacerState? state))
                    continue;

                snapshot.RacerStates.Add(new RacerSnapshot
                {
                    Name = racerName,
                    Position = racer.Position,
                    FacingDirection = racer.FacingDirection,
                    Speed = state.Speed,
                    AddedSpeed = state.AddedSpeed,
                    Surfboard = state.Surfboard,
                    RaceFacing = state.Facing,
                    LapsDone = state.LapsDone,
                    ReachedHalf = state.ReachedHalf,
                    CurrentItem = state.CurrentItem,
                    ItemObtainTimer = state.ItemObtainTimer,
                    ItemUsageTimer = state.ItemUsageTimer,
                    SlowdownTimer = state.SlowdownTimer,
                    StunTimer = state.StunTimer
                });
            }

            foreach (Obstacle obstacle in Mod.Obstacles)
            {
                snapshot.Obstacles.Add(new ObstacleSnapshot
                {
                    Id = obstacle.Id,
                    Type = obstacle.Type,
                    Position = obstacle.Position,
                    HomingTarget = obstacle.HomingTarget
                });
            }

            this.Helper.Multiplayer.SendMessage(snapshot, RaceSnapshotMessage.Type, new[] { this.ModManifest.UniqueID });
        }

        private void ApplyRaceSnapshot(RaceSnapshotMessage snapshot)
        {
            if (snapshot.SessionId != this.raceSessionId || snapshot.Sequence <= this.lastAppliedSnapshotSequence)
                return;
            if (Game1.CurrentEvent is not Event currentEvent || currentEvent.FestivalName != Mod.FestivalName)
                return;

            this.lastAppliedSnapshotSequence = snapshot.Sequence;
            Mod.Racers = new List<string>(snapshot.Racers);
            Mod.RaceWinner = snapshot.Winner;
            Mod.RaceCourseActive = snapshot.RaceActive;

            foreach (RacerSnapshot racerSnapshot in snapshot.RacerStates)
            {
                Character? racer = currentEvent.getCharacterByName(racerSnapshot.Name);
                if (racer is null)
                    continue;

                racer.position.X = racerSnapshot.Position.X;
                racer.position.Y = racerSnapshot.Position.Y;
                racer.faceDirection(racerSnapshot.FacingDirection);
                if (!Mod.RacerState.TryGetValue(racerSnapshot.Name, out RacerState? state))
                {
                    state = new RacerState();
                    Mod.RacerState[racerSnapshot.Name] = state;
                }

                state.Speed = racerSnapshot.Speed;
                state.AddedSpeed = racerSnapshot.AddedSpeed;
                state.Surfboard = racerSnapshot.Surfboard;
                state.Facing = racerSnapshot.RaceFacing;
                state.LapsDone = racerSnapshot.LapsDone;
                state.ReachedHalf = racerSnapshot.ReachedHalf;
                state.CurrentItem = racerSnapshot.CurrentItem;
                state.ItemObtainTimer = racerSnapshot.ItemObtainTimer;
                state.ItemUsageTimer = racerSnapshot.ItemUsageTimer;
                state.SlowdownTimer = racerSnapshot.SlowdownTimer;
                state.StunTimer = racerSnapshot.StunTimer;
            }

            Mod.Obstacles.Clear();
            foreach (ObstacleSnapshot obstacle in snapshot.Obstacles)
            {
                Mod.Obstacles.Add(new Obstacle
                {
                    Id = obstacle.Id,
                    Type = obstacle.Type,
                    Position = obstacle.Position,
                    HomingTarget = obstacle.HomingTarget
                });
            }

            if (!snapshot.RaceActive && snapshot.Winner is not null && currentEvent.playerControlSequenceID == "surfingRace")
                this.FinishRace(currentEvent, snapshot.Winner);
        }

        private void FinishRace(Event? currentEvent, string winner)
        {
            if (currentEvent is null)
                return;

            Mod.RaceWinner = winner;
            currentEvent.playerControlSequence = false;
            currentEvent.playerControlSequenceID = null;
            Dictionary<string, string> festData = this.Helper.Reflection.GetField<Dictionary<string, string>>(currentEvent, "festivalData").GetValue();
            Character? winnerCharacter = currentEvent.getCharacterByName(winner);
            festData.TryGetValue($"{winner}Win", out string? winDialog);
            winDialog ??= I18n.Race_Winner_Player(name: winnerCharacter?.Name ?? winner);
            currentEvent.eventCommands = festData["afterSurfingRace"].Replace("{{winDialog}}", winDialog).Split('/');
            currentEvent.currentCommand = 0;

            foreach (string racerName in Mod.Racers)
                currentEvent.getCharacterByName(racerName)?.stopGlowing();
        }

        private static void NormalizeMapActions(Map map)
        {
            foreach (Layer layer in map.Layers)
            {
                for (int x = 0; x < layer.LayerWidth; x++)
                {
                    for (int y = 0; y < layer.LayerHeight; y++)
                    {
                        Tile tile = layer.Tiles[x, y];
                        if (tile?.Properties.TryGetValue("Action", out var actionProperty) != true)
                            continue;

                        string action = actionProperty?.ToString() ?? string.Empty;
                        tile.Properties["Action"] = NormalizeFestivalAction(action);
                    }
                }
            }
        }

        private static void PlaceBonfire(Map map, int x, int y, bool purple)
        {
            int width = 48 / 16;
            int height = 80 / 16;
            TileSheet tileSheet = map.GetTileSheet("surfing");
            int baseY = (purple ? 272 : 112) / 16 * tileSheet.SheetWidth;
            Layer buildings = map.GetLayer("Buildings");
            Layer front = map.GetLayer("Front");

            for (int offsetX = 0; offsetX < width; offsetX++)
            {
                for (int offsetY = 0; offsetY < height; offsetY++)
                {
                    Layer layer = offsetY < height - 2 ? front : buildings;
                    var frames = new List<StaticTile>();
                    for (int frame = 0; frame < 8; frame++)
                    {
                        int tileOffset = offsetX + offsetY * tileSheet.SheetWidth;
                        int frameOffset = (frame % 4) * 3 + (frame / 4) * (tileSheet.SheetWidth * height);
                        frames.Add(new StaticTile(layer, tileSheet, BlendMode.Alpha, baseY + tileOffset + frameOffset));
                    }

                    layer.Tiles[x + offsetX, y + offsetY] = new AnimatedTile(layer, frames.ToArray(), 75);
                    if (layer == buildings)
                        layer.Tiles[x + offsetX, y + offsetY].Properties["Action"] = BonfireAction;
                }
            }
        }

        private bool OnBonfireAction(GameLocation location, string[] args, Farmer farmer, Point position)
        {
            if (Mod.PlayerDidBonfire == (BonfireState.Normal | BonfireState.Shorts))
                return true;

            bool Highlight(StardewValley.Item item) => item is SObject obj
                && !obj.bigCraftable.Value
                && ((!Mod.PlayerDidBonfire.HasFlag(BonfireState.Normal)
                        && obj.QualifiedItemId == "(O)388"
                        && CountInventoryItem(farmer, "(O)388") >= 50)
                    || (!Mod.PlayerDidBonfire.HasFlag(BonfireState.Shorts)
                        && (obj.QualifiedItemId is "(O)71" or "(O)789")));

            void BehaviorOnSelect(StardewValley.Item item, Farmer who)
            {
                if (item == null)
                    return;

                if (!Mod.PlayerDidBonfire.HasFlag(BonfireState.Normal)
                    && item.QualifiedItemId == "(O)388"
                    && CountInventoryItem(who, "(O)388") >= 50)
                {
                    ConsumeInventoryItem(who, "(O)388", 50);

                    if (Game1.CurrentEvent is Event currentEvent)
                    {
                        foreach (NPC character in currentEvent.actors)
                        {
                            if (character != null)
                                who.changeFriendship(50, character);
                        }
                    }

                    Mod.PlayerDidBonfire |= BonfireState.Normal;
                    Game1.drawObjectDialogue(I18n.Dialog_Wood());
                    Game1.playSound("fireball");
                    PlaceBonfire(location.Map, 30, 5, Mod.PlayerDidBonfire.HasFlag(BonfireState.Shorts));
                }
                else if (!Mod.PlayerDidBonfire.HasFlag(BonfireState.Shorts)
                    && (item.QualifiedItemId is "(O)71" or "(O)789"))
                {
                    who.removeItemFromInventory(item);
                    Mod.PlayerDidBonfire |= BonfireState.Shorts;
                    if (Game1.getCharacterFromName("Lewis") is NPC lewis)
                        Game1.activeClickableMenu = new DialogueBox(new Dialogue(lewis, "ThaleTheGreat.SurfingFestival_BonfireShorts", I18n.Dialog_Shorts()));
                    else
                        Game1.drawObjectDialogue(I18n.Dialog_Shorts());
                    Game1.playSound("fireball");
                    PlaceBonfire(location.Map, 30, 5, true);
                }
            }

            Game1.activeClickableMenu = new ItemGrabMenu(null, true, false, Highlight, BehaviorOnSelect, I18n.Ui_Wood(), BehaviorOnSelect);
            return true;
        }

        private static int CountInventoryItem(Farmer farmer, string qualifiedItemId)
        {
            int count = 0;
            foreach (StardewValley.Item? item in farmer.Items)
            {
                if (item?.QualifiedItemId == qualifiedItemId)
                    count += item.Stack;
            }
            return count;
        }

        private static void ConsumeInventoryItem(Farmer farmer, string qualifiedItemId, int amount)
        {
            for (int i = farmer.Items.Count - 1; i >= 0 && amount > 0; i--)
            {
                StardewValley.Item? item = farmer.Items[i];
                if (item?.QualifiedItemId != qualifiedItemId)
                    continue;

                int consumed = Math.Min(amount, item.Stack);
                item.Stack -= consumed;
                amount -= consumed;
                if (item.Stack <= 0)
                    farmer.Items[i] = null;
            }
        }

        private bool OnSecretOfferingAction(GameLocation location, string[] args, Farmer farmer, Point position)
        {
            if (farmer != Game1.player || farmer.hasOrWillReceiveMail("ThaleTheGreat.SurfingFestival_Offering"))
                return true;

            Response[] answers =
            {
                new("MakeOffering", I18n.Secret_Yes()),
                new("Leave", I18n.Secret_No())
            };

            void AfterQuestion(Farmer who, string choice)
            {
                if (choice != "MakeOffering")
                    return;

                if (who.Money >= 100000)
                {
                    who.mailReceived.Add("ThaleTheGreat.SurfingFestival_Offering");
                    Game1.drawObjectDialogue(I18n.Secret_Purchased());
                }
                else
                    Game1.drawObjectDialogue(I18n.Secret_Broke());
            }

            location.createQuestionDialogue(Game1.parseText(I18n.Secret_Text()), answers, AfterQuestion);
            return true;
        }

        private static int SurfboardWaterAnim;
        private static int SurfboardWaterAnimTimer;
        private static int PrevRacerFrame = -1;
        public static void DrawSurfboard(Character instance, SpriteBatch b)
        {
            NPC? npc = instance as NPC;
            Farmer? farmer = instance as Farmer;
            string? racerName = GetRacerName(instance);
            if (racerName is null || !Mod.Racers.Contains(racerName))
                return;

            bool player = instance is Farmer;
            int ox = 0, oy = 0;

            if (!Mod.RacerState.TryGetValue(racerName, out RacerState? state))
                return;

            var rect = new Rectangle(state.Surfboard % 2 * 32, state.Surfboard / 2 * 16, 32, 16);
            var rect2 = new Rectangle(Mod.SurfboardWaterAnim * 64, 0, 64, 48);
            var origin = new Vector2(16, 8);
            var origin2 = new Vector2(32, 24);
            switch (state.Facing)
            {
                case Game1.up:
                    ox = 8;
                    b.Draw(Mod.SurfboardTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect, Color.White, 90 * 3.14f / 180, origin, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0002f);
                    b.Draw(Mod.SurfboardWaterTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect2, Color.White, -90 * 3.14f / 180, origin2, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0001f);
                    break;
                case Game1.down:
                    ox = player ? -8 : -4;
                    b.Draw(Mod.SurfboardTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect, Color.White, -90 * 3.14f / 180, origin, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0002f);
                    b.Draw(Mod.SurfboardWaterTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect2, Color.White, 90 * 3.14f / 180, origin2, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0001f);
                    break;
                case Game1.left:
                    oy = player ? 0 : 8;
                    b.Draw(Mod.SurfboardTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect, Color.White, 180 * 3.14f / 180, origin, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0002f);
                    b.Draw(Mod.SurfboardWaterTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect2, Color.White, 180 * 3.14f / 180, origin2, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0001f);
                    break;
                case Game1.right:
                    oy = player ? -8 : 0;
                    b.Draw(Mod.SurfboardTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect, Color.White, 0 * 3.14f / 180, origin, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0002f);
                    b.Draw(Mod.SurfboardWaterTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + 8 * Game1.pixelZoom + ox, instance.Position.Y + 8 * Game1.pixelZoom + oy)), rect2, Color.White, 0 * 3.14f / 180, origin2, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f - 0.0001f);
                    break;
            }

            if (state.StunTimer >= 0)
            {
                if (npc != null)
                {
                    var shockedFrames = new Dictionary<string, int>
                    {
                        ["Shane"] = 18,
                        ["Harvey"] = 30,
                        ["Maru"] = 27,
                        ["Emily"] = 26
                    };

                    Mod.PrevRacerFrame = npc.Sprite.CurrentFrame;
                    if (shockedFrames.TryGetValue(instance.Name, out int frame))
                        npc.Sprite.CurrentFrame = frame;
                }
                else if (farmer != null)
                {
                    Mod.PrevRacerFrame = farmer.FarmerSprite.CurrentFrame;
                    farmer.FarmerSprite.setCurrentSingleFrame(94, 1);
                }
            }
        }

        public static void DrawSurfingStatuses(Character instance, SpriteBatch b)
        {
            NPC? npc = instance as NPC;
            Farmer? farmer = instance as Farmer;
            string? racerName = GetRacerName(instance);
            if (racerName is null || !Mod.Racers.Contains(racerName))
                return;

            var state = Mod.RacerState[racerName];
            if (state.StunTimer >= 0)
            {
                int ox = 0, oy = 0;
                if (farmer != null)
                {
                    oy = -6 * Game1.pixelZoom;
                }
                b.Draw(Mod.StunTex, Game1.GlobalToLocal(new Vector2(instance.Position.X + ox, instance.Position.Y - 17 * Game1.pixelZoom + oy)), null, Color.White, 0, Vector2.Zero, Game1.pixelZoom, SpriteEffects.None, instance.GetBoundingBox().Center.Y / 10000f + 0.0003f);

                if (npc != null)
                {
                    npc.Sprite.CurrentFrame = Mod.PrevRacerFrame;
                    Mod.PrevRacerFrame = -1;
                }
                else if (farmer != null)
                {
                    Mod.PrevRacerFrame = -1;
                }
            }
        }

        private static string? GetRacerName(Character character)
        {
            if (character is NPC npc)
                return npc.Name;
            if (character is Farmer farmer)
                return $"farmer{Utility.getFarmerNumberFromFarmer(farmer)}";
            return null;
        }

        private static Farmer? GetFarmerByRacerName(string racerName)
        {
            foreach (Farmer farmer in Game1.getOnlineFarmers())
            {
                if ($"farmer{Utility.getFarmerNumberFromFarmer(farmer)}" == racerName)
                    return farmer;
            }

            return null;
        }

        private static void EventCommand_WarpSurfingRacers(Event instance, string[] args, EventContext context)
        {
            Mod.RaceWinner = null;
            Mod.Instance.raceSessionId = $"{Game1.uniqueIDForThisGame}:{Game1.stats.DaysPlayed}:summer5";
            Mod.Instance.raceSnapshotSequence = 0;
            Mod.Instance.lastAppliedSnapshotSequence = -1;
            Mod.Instance.nextObstacleId = 0;
            Mod.Instance.remoteFacingDirections.Clear();

            // Generate obstacles
            Mod.Obstacles.Clear();
            Point obstaclesStart = new Point(6, 48);
            Point obstaclesEnd = new Point(143, 70);
            var obstaclesLayer = Game1.currentLocation.Map.GetLayer("RaceObstacles");
            for (int ix = obstaclesStart.X; ix <= obstaclesEnd.X; ++ix)
            {
                for (int iy = obstaclesStart.Y; iy <= obstaclesEnd.Y; ++iy)
                {
                    var tile = obstaclesLayer.Tiles[ix, iy];
                    if (tile?.TileIndex == 3)
                        Mod.Obstacles.Add(new Obstacle
                        {
                            Id = ++Mod.Instance.nextObstacleId,
                            Type = ObstacleType.Item,
                            Position = new Vector2(ix * Game1.tileSize, iy * Game1.tileSize)
                        });
                    else if (tile?.TileIndex == 64)
                        Mod.Obstacles.Add(new Obstacle
                        {
                            Id = ++Mod.Instance.nextObstacleId,
                            Type = ObstacleType.Net,
                            Position = new Vector2(ix * Game1.tileSize, iy * Game1.tileSize)
                        });
                    else if (tile?.TileIndex == 32)
                        Mod.Obstacles.Add(new Obstacle
                        {
                            Id = ++Mod.Instance.nextObstacleId,
                            Type = ObstacleType.Rock,
                            Position = new Vector2(ix * Game1.tileSize, iy * Game1.tileSize)
                        });
                }
            }

            // Add racers
            Mod.Racers = new List<string>
            {
                "Shane",
                "Harvey",
                "Maru",
                "Emily"
            };
            foreach (var farmer in Game1.getOnlineFarmers())
            {
                Mod.Racers.Add("farmer" + Utility.getFarmerNumberFromFarmer(farmer));
                farmer.CanMove = false;
            }

            // Shuffle them
            var r = new Random((int)Game1.uniqueIDForThisGame + (int)Game1.stats.DaysPlayed);
            for (int i = 0; i < Mod.Racers.Count; ++i)
            {
                int ni = r.Next(Mod.Racers.Count);
                string old = Mod.Racers[ni];
                Mod.Racers[ni] = Mod.Racers[i];
                Mod.Racers[i] = old;
            }

            // Set states and surfboards
            Mod.RacerState.Clear();
            foreach (string racerName in Mod.Racers)
            {
                Mod.RacerState.Add(racerName, new RacerState
                {
                    Surfboard = r.Next(6)
                });

                // NPCs get a buff since they're dumb
                if (!racerName.StartsWith("farmer"))
                    Mod.RacerState[racerName].AddedSpeed += 1;
                // Farmer's do if they paid the secret offering
                else if (GetFarmerByRacerName(racerName)?.hasOrWillReceiveMail("ThaleTheGreat.SurfingFestival_Offering") == true)
                    Mod.RacerState[racerName].AddedSpeed += 2;
            }

            Mod.RaceCourseActive = true;

            // Move them to their start
            var startPos = new Vector2(18, 57);
            if (Mod.Racers.Count <= 6)
            {
                startPos.X += 1;
                startPos.Y -= 1;
            }
            var actualPos = startPos;
            foreach (string racerName in Mod.Racers)
            {
                var racer = instance.getCharacterByName(racerName);

                racer.position.X = actualPos.X * Game1.tileSize + 4;
                racer.position.Y = actualPos.Y * Game1.tileSize;
                racer.faceDirection(Game1.right);

                actualPos.X += 1;
                actualPos.Y -= 1;

                // If a more than 4 players mod is used, things might go out of bounds.
                if (actualPos.Y < 50)
                    actualPos.Y = 57;
            }

            if (Context.IsMainPlayer)
                Mod.Instance.BroadcastRaceSnapshot(raceActive: true);

            // Go to next command
            ++instance.CurrentCommand;
        }

        private static void EventCommand_WarpSurfingRacersFinish(Event instance, string[] args, EventContext context)
        {
            Mod.RaceCourseActive = false;

            // Move the racers
            var startPos = new Vector2(32, 12);
            if (Mod.Racers.Count <= 6)
                ++startPos.X;
            var actualPos = startPos;
            foreach (string racerName in Mod.Racers)
            {
                var racer = instance.getCharacterByName(racerName);

                racer.position.X = actualPos.X * Game1.tileSize + 4;
                racer.position.Y = actualPos.Y * Game1.tileSize;
                racer.faceDirection(Game1.up);

                actualPos.X += 1;

                // If a more than 4 players mod is used, things might go out of bounds.
                if (actualPos.X > 39)
                {
                    actualPos.X = 32;
                    ++actualPos.Y;
                }
            }

            // Go to next command
            ++instance.CurrentCommand;
        }

        private static void EventCommand_AwardSurfingPrize(Event instance, string[] args, EventContext context)
        {
            if (Mod.RaceWinner == "farmer" + Utility.getFarmerNumberFromFarmer(Game1.player))
            {
                if (!Game1.player.mailReceived.Contains("ThaleTheGreat.SurfingFestival_Winner"))
                {
                    Game1.player.mailReceived.Add("ThaleTheGreat.SurfingFestival_Winner");
                    Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create<SObject>($"(BC){TrophyId}"));
                }

                Game1.playSound("money");
                Game1.player.Money += 1500;
                Game1.drawObjectDialogue(I18n.Dialog_PrizeMoney());
            }

            instance.CurrentCommand++;
            if (Game1.activeClickableMenu == null)
                ++instance.CurrentCommand;
        }

        public static List<string> GetRacePlacement()
        {
            List<string> ret = new List<string>(Mod.Racers);
            var cmp = new RacerPlacementComparer();
            ret.Sort(cmp);

            return ret;
        }

        public static Color MyGetPrismaticColor(int offset = 0)
        {
            float interval = 250f;
            int currentIndex = ((int)((float)Game1.currentGameTime.TotalGameTime.TotalMilliseconds / interval) + offset) % Utility.PRISMATIC_COLORS.Length;
            int nextIndex = (currentIndex + 1) % Utility.PRISMATIC_COLORS.Length;
            float position = (float)Game1.currentGameTime.TotalGameTime.TotalMilliseconds / interval % 1f;
            Color prismaticColor = default(Color);
            prismaticColor.R = (byte)(Utility.Lerp(Utility.PRISMATIC_COLORS[currentIndex].R / 255f, Utility.PRISMATIC_COLORS[nextIndex].R / 255f, position) * 255f);
            prismaticColor.G = (byte)(Utility.Lerp(Utility.PRISMATIC_COLORS[currentIndex].G / 255f, Utility.PRISMATIC_COLORS[nextIndex].G / 255f, position) * 255f);
            prismaticColor.B = (byte)(Utility.Lerp(Utility.PRISMATIC_COLORS[currentIndex].B / 255f, Utility.PRISMATIC_COLORS[nextIndex].B / 255f, position) * 255f);
            prismaticColor.A = (byte)(Utility.Lerp(Utility.PRISMATIC_COLORS[currentIndex].A / 255f, Utility.PRISMATIC_COLORS[nextIndex].A / 255f, position) * 255f);
            return prismaticColor;
        }
    }
}
