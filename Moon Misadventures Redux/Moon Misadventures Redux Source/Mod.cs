using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using ThaleTheGreat.MoonMisadventures.Game;
using ThaleTheGreat.MoonMisadventures.Game.Items;
using ThaleTheGreat.MoonMisadventures.Game.Locations;
using ThaleTheGreat.MoonMisadventures.Game.Monsters;
using ThaleTheGreat.MoonMisadventures.Game.Projectiles;
using ThaleTheGreat.MoonMisadventures.VirtualProperties;
using Netcode;
using Newtonsoft.Json.Linq;
using SpaceShared;
using SpaceShared.APIs;
using StardewModdingAPI;
using StardewModdingAPI.Enums;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.GameData;
using StardewValley.GameData.Buildings;
using StardewValley.GameData.Crops;
using StardewValley.GameData.FarmAnimals;
using StardewValley.GameData.LocationContexts;
using StardewValley.GameData.Locations;
using StardewValley.GameData.Machines;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Powers;
using StardewValley.GameData.Tools;
using StardewValley.GameData.Weapons;
using StardewValley.GameData.WorldMaps;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

/* Art:
 *  paradigmnomad (most art)
 *  finalbossblues https://finalbossblues.itch.io/dark-dimension-tileset (recolored by paradigmnomad)
 *  ... more ...
 * Music:
 *  https://lowenergygirl.itch.io/space-journey (Into the Spaceship)
 */

namespace ThaleTheGreat.MoonMisadventures
{
    public class Mod : StardewModdingAPI.Mod
    {
        public const int CurrentSaveSchemaVersion = 1;
        public const string SaveDataKey = "ThaleTheGreat.MoonMisadventures.SaveData";
        private const string MythicitePrismaticBarRecipeName = "Prismatic Bar (Mythicite)";

        public static Mod instance;
        public Configuration Config;
        internal SaveData CurrentSaveData { get; private set; } = new();
        private bool canWriteSaveData = true;
        private bool mythiciteToolsRegistered;
        private IToolAndSprinklerUpgradesApi? toolAndSprinklerUpgradesApi;
        private IWalletToolsApi? walletToolsApi;

        internal static DepthStencilState DefaultStencilOverride = null;
        internal static DepthStencilState StencilBrighten = new()
        {
            StencilEnable = true,
            StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace,
            ReferenceStencil = 1,
            DepthBufferEnable = false,
        };
        internal static DepthStencilState StencilDarken = new()
        {
            StencilEnable = true,
            StencilFunction = CompareFunction.Always,
            StencilPass = StencilOperation.Replace,
            ReferenceStencil = 0,
            DepthBufferEnable = false,
        };
        internal static DepthStencilState StencilRenderOnDark = new()
        {
            StencilEnable = true,
            StencilFunction = CompareFunction.NotEqual,
            StencilPass = StencilOperation.Keep,
            ReferenceStencil = 1,
            DepthBufferEnable = false,
        };

        public override void Entry( IModHelper helper )
        {
            I18n.Init(helper.Translation);

            Log.Monitor = Monitor;
            instance = this;
            I18n.Init(Helper.Translation);

            Config = Helper.ReadConfig<Configuration>();

            GameStateQuery.Register(
                $"{ModManifest.UniqueID}_HAS_LUNAR_KEY",
                (_, context) => (context.Player ?? Game1.player).team.get_hasLunarKey().Value
            );

            Assets.Load( helper.ModContent );
            SoundEffect mainMusic = SoundEffect.FromFile( Path.Combine( Helper.DirectoryPath, "assets", "into-the-spaceship.wav" ) );
            Game1.soundBank.AddCue( new CueDefinition( "into-the-spaceship", mainMusic, 2, loop: true ) );

            SoundEffect laser = SoundEffect.FromFile(Path.Combine(Helper.DirectoryPath, "assets", "laserShoot.wav"));
            Game1.soundBank.AddCue(new CueDefinition("mm_laser", laser, 3));

            Helper.ConsoleCommands.Add( "mm_key", "Gives you the lunar key.", OnKeyCommand );
            Helper.ConsoleCommands.Add( "mm_infuse", "Opens the celestial infuser menu.", OnInfuseCommand );

            Helper.Events.GameLoop.GameLaunched += OnGameLaunched;
            Helper.Events.GameLoop.TimeChanged += OnTimeChanged;
            Helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
            Helper.Events.GameLoop.DayStarted += OnDayStarted;
            Helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
            Helper.Events.GameLoop.Saving += OnSaving;
            Helper.Events.Specialized.LoadStageChanged += OnLoadStageChanged;
            Helper.Events.Display.MenuChanged += OnMenuChanged;
            Helper.Events.Display.RenderingWorld += OnRenderingWorld;
            Helper.Events.Display.RenderedWorld += OnRenderedWorld;
            Helper.Events.Display.RenderedHud += OnRenderedHud;
            Helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
            Helper.Events.Player.Warped += OnWarped;
            Helper.Events.Content.AssetRequested += OnAssetRequested;


            var necklaceDef = new NecklaceDataDefinition();
            ItemRegistry.ItemTypes.Add(necklaceDef);
            Helper.Reflection.GetField< Dictionary<string, IItemDataDefinition>>( typeof( ItemRegistry ), "IdentifierLookup" ).GetValue()[necklaceDef.Identifier] = necklaceDef;

            var harmony = new Harmony( ModManifest.UniqueID );
            harmony.PatchAll();
            //harmony.Patch( AccessTools.Method( "StardewValley.Game1:_draw" ), transpiler: new HarmonyMethod( typeof( Patches.Game1CatchLightingRenderPatch ).GetMethod( "Transpiler" ) ) );
        }

        public T Load<T>( IAssetInfo asset )
        {
            return default( T );
        }
        private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
        {
            if (e.NameWithoutLocale.IsEquivalentTo($"{ModManifest.UniqueID}/LunarKey"))
            {
                e.LoadFromModFile<Texture2D>("assets/key.png", AssetLoadPriority.Exclusive);
                return;
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            {
                e.Edit(asset =>
                {
                    asset.AsDictionary<string, PowersData>().Data[ModManifest.UniqueID + ".LunarKey"] = new PowersData
                    {
                        DisplayName = I18n.Item_LunarKey_Name(),
                        Description = Helper.Translation.Get("item.lunar-key.description").ToString(),
                        TexturePath = $"{ModManifest.UniqueID}/LunarKey",
                        TexturePosition = Point.Zero,
                        UnlockedCondition = $"{ModManifest.UniqueID}_HAS_LUNAR_KEY",
                    };
                });
                return;
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
            {
                e.Edit(asset =>
                {
                    IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                    if (toolAndSprinklerUpgradesApi?.IsPrismaticHighest == true)
                        data[MythicitePrismaticBarRecipeName] = $"{ItemIds.MythiciteBar} 1 74 1/Home/{ItemIds.PrismaticBar} 1/false/null";
                    else
                        data.Remove(MythicitePrismaticBarRecipeName);
                });
                return;
            }

            //Assets.ApplyEdits(e);

            if (e.NameWithoutLocale.IsEquivalentTo(ModManifest.UniqueID + "/Necklaces"))
            {
                e.LoadFrom(() => new Dictionary<string, NecklaceData>
                {
                    { "looting", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Looting_Name(),
                        Description = I18n.Item_Necklace_Looting_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 0,
                    } },
                    { "shocking", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Shocking_Name(),
                        Description = I18n.Item_Necklace_Shocking_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 1,
                    } },
                    { "speed", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Speed_Name(),
                        Description = I18n.Item_Necklace_Speed_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 2,
                    } },
                    { "health", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Health_Name(),
                        Description = I18n.Item_Necklace_Health_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 3,
                    } },
                    { "cooling", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Cooling_Name(),
                        Description = I18n.Item_Necklace_Cooling_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 4,
                    } },
                    { "lunar", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Lunar_Name(),
                        Description = I18n.Item_Necklace_Lunar_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 5,
                        CanBeSelectedAtAltar = false,
                    } },
                    { "water", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Water_Name(),
                        Description = I18n.Item_Necklace_Water_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 6,
                    } },
                    { "sea", new NecklaceData()
                    {
                        DisplayName = I18n.Item_Necklace_Sea_Name(),
                        Description = I18n.Item_Necklace_Sea_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/necklaces.png",
                        TextureIndex = 7,
                    } },
                }, AssetLoadPriority.Exclusive);
            }
            foreach (string file in Directory.GetFiles(Path.Combine(Helper.DirectoryPath, "assets", "dga")))
            {
                string filename = Path.GetFileName(file);
                if (e.NameWithoutLocale.Name.EndsWith(".png") && e.NameWithoutLocale.IsEquivalentTo("ThaleTheGreat.MoonMisadventures/assets/" + filename))
                {
                    e.LoadFrom(() => Helper.ModContent.Load<Texture2D>("assets/dga/" + filename), AssetLoadPriority.Exclusive);
                }
            }
            if (e.NameWithoutLocale.IsEquivalentTo( "TerrainFeatures/hoeDirt" ) && Game1.currentLocation is LunarLocation)
            {
                e.LoadFrom(() => Helper.ModContent.Load<Texture2D>("assets/hoedirt.png"), AssetLoadPriority.High);
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Buildings"))
            {
                e.Edit((asset) =>
                {
                    var bData = asset.AsDictionary<string, BuildingData>().Data;
                    bData.Add("ThaleTheGreat.MoonMisadventures_MoonObelisk", new()
                    {
                        Name = I18n.Building_Obelisk_Name(),
                        Description = I18n.Building_Obelisk_Description(),
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/obelisk.png",
                        BuildMaterials = new[]
                        {
                            new BuildingMaterial()
                            {
                                ItemId = ItemIds.MythiciteBar,
                                Amount = 10,
                            },
                            new BuildingMaterial()
                            {
                                ItemId = ItemIds.StellarEssence,
                                Amount = 25,
                            },
                            new BuildingMaterial()
                            {
                                ItemId = ItemIds.SoulSapphire,
                                Amount = 3,
                            },
                        }.ToList(),
                        BuildCost = 2000000,
                        BuildCondition = "PLAYER_HAS_FLAG Any ThaleTheGreat.MoonMisadventures_FirstUfoTravel",
                        Size = new(3, 2),
                        DefaultAction = "ObeliskWarp Custom_MM_MoonFarm 7 11 true",
                    });
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/LocationContexts"))
            {
                e.Edit((asset) =>
                {
                    var locData = asset.AsDictionary<string, LocationContextData>().Data;
                    locData.Add("Moon", new()
                    {
                        PlayRandomAmbientSounds = false,
                        AllowRainTotem = false,
                        WeatherConditions = new[]
                        {
                            new WeatherCondition()
                            {
                                Condition = "",
                                Weather = "Sun",
                            }
                        }.ToList(),
                        ReviveLocations = new[]
                        {
                            new ReviveLocation()
                            {
                                Location = "Custom_MM_MoonLandingArea",
                                Position = new( 9, 31 )
                            }
                        }.ToList(),
                        PassOutLocations = new[]
                        {
                            new ReviveLocation()
                            {
                                Location = "Custom_MM_MoonFarm",
                                Position = new(7, 11)
                            }
                        }.ToList(),
                    });
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/WorldMap"))
            {
                e.Edit((asset) =>
                {
                    var regionData = new WorldMapRegionData();
                    regionData.BaseTexture.Add(new()
                    {
                        Id = "moon_bg",
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/map.png",
                        SourceRect = new Rectangle( 0, 0, 300, 180 )
                    });
                    var mapData = regionData.MapAreas;
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_farm",
                        PixelArea = new Rectangle(194, 91, 24, 24),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonFarm",
                                TileArea = new Rectangle(0, 0, 49, 39),
                                MapPixelArea = new Rectangle(194, 91, 24, 24),
                            },
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonFarmHouse",
                                TileArea = new Rectangle(0, 0, 19, 11),
                                MapPixelArea = new Rectangle(199, 97, 1, 1),
                            },
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonFarmCave",
                                TileArea = new Rectangle(0, 0, 19, 11),
                                MapPixelArea = new Rectangle(220, 106, 1, 1),
                            },
                        }),
                        Tooltips = new(new[] {
                            new WorldMapTooltipData()
                            {
                                Text = I18n.Location_LunarFarm(),
                            }
                        }),
                        ScrollText = I18n.Location_LunarFarm(),
                    });
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_planetview",
                        PixelArea = new Rectangle(216, 82, 7, 11),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonPlanetOverlook",
                                TileArea = new Rectangle(0, 0, 49, 39),
                                MapPixelArea = new Rectangle(216, 82, 7, 11),
                            },
                        }),
                        ScrollText = I18n.Location_PlanetOverlook(),
                    });
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_temple",
                        PixelArea = new Rectangle(170, 91, 9, 12),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonInfuserRoom",
                                TileArea = new Rectangle(0, 0, 29, 29),
                                MapPixelArea = new Rectangle(170, 91, 9, 12),
                            },
                        }),
                        ScrollText = I18n.Location_MoonTemple(),
                    });
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_landingarea",
                        PixelArea = new Rectangle(165, 109, 26, 21),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonLandingArea",
                                TileArea = new Rectangle(0, 0, 34, 37),
                                MapPixelArea = new Rectangle(165, 109, 26, 21),
                            },
                        }),
                        Tooltips = new(new[] {
                            new WorldMapTooltipData()
                            {
                                Text = I18n.Location_LandingArea(),
                            }
                        }),
                        ScrollText = I18n.Location_LandingArea(),
                    });
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_asteroidsentrance",
                        PixelArea = new Rectangle(147, 89, 13, 17),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonAsteroidsEntrance",
                                TileArea = new Rectangle(0, 0, 49, 94),
                                MapPixelArea = new Rectangle(147, 89, 13, 17),
                            },
                        }),
                        Tooltips = new(new[] {
                            new WorldMapTooltipData()
                            {
                                Text = I18n.Location_AsteroidsEntrance(),
                            }
                        }),
                        ScrollText = I18n.Location_AsteroidsEntrance(),
                    });
                    mapData.Add(new WorldMapAreaData()
                    {
                        Id = "moon_asteroids",
                        PixelArea = new Rectangle(64, 40, 1, 1),
                        WorldPositions = new(new[] {
                            new WorldMapAreaPositionData()
                            {
                                LocationContext = "Moon",
                                LocationName = "Custom_MM_MoonAsteroidsDungeon",
                                TileArea = new Rectangle(0, 0, 149, 149),
                                MapPixelArea = new Rectangle(64, 40, 1, 1),
                            },
                        }),
                        Tooltips = new(new[] {
                            new WorldMapTooltipData()
                            {
                                Text = I18n.Location_Asteroids(),
                            }
                        }),
                        ScrollText = I18n.Location_Asteroids(),
                    });
                    // TODO: Mountain top
                    /*
                    mapData.Add("mountaintop", new()
                    {
                        AreaID = "mountaintop",
                        Group = "SDV",
                        Texture = "LooseSprites/map",
                        Zones = new(new[] {
                            new WorldMapAreaZone()
                            {
                                ValidAreas = new List<string>( new[] { "Custom_MM_MountainTop" } ),
                                MapTileCorners = "0 0 47 47",
                                MapImageCorners = "210 1 211 1",
                                DisplayName = "???",
                            },
                        }),
                    });
                    */
                    asset.AsDictionary<string, WorldMapRegionData>().Data.Add("Moon", regionData);
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Objects"))
            {
                e.Edit((asset) =>
                {
                    var sourceData = Helper.ModContent.Load<Dictionary<string, JToken>>("assets/item-data.json");
                    var objectData = asset.AsDictionary<string, ObjectData>().Data;
                    foreach (var entry in sourceData)
                    {
                        if (!entry.Key.StartsWith("Object:", StringComparison.Ordinal) || entry.Value is not JObject model)
                            continue;

                        string key = entry.Key.Substring("Object:".Length);
                        ObjectData value = model.ToObject<ObjectData>()
                            ?? throw new InvalidDataException($"Invalid object data entry '{entry.Key}'.");
                        value.DisplayName = ResolveTranslationTokens(value.DisplayName);
                        value.Description = ResolveTranslationTokens(value.Description);
                        objectData.Add(ModManifest.UniqueID + "_" + key, value);
                    }
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Boots"))
            {
                e.Edit((asset) =>
                {
                    var sourceData = Helper.ModContent.Load<Dictionary<string, JToken>>("assets/item-data.json");
                    var bootsData = asset.AsDictionary<string, string>().Data;
                    foreach (var entry in sourceData)
                    {
                        if (!entry.Key.StartsWith("Boots:", StringComparison.Ordinal))
                            continue;

                        string key = entry.Key.Substring("Boots:".Length);
                        bootsData.Add(ModManifest.UniqueID + "_" + key, ResolveTranslationTokens(entry.Value.ToString()));
                    }
                });
            }
            if (e.NameWithoutLocale.IsEquivalentTo("Data/FarmAnimals"))
            {
                e.Edit((asset) =>
                {
                    var dict = asset.AsDictionary<string, FarmAnimalData>().Data;
                    dict.Add("Lunar Cow", new()
                    {
                        DisplayName = I18n.FarmAnimal_LunarCow(),
                        House = "Barn",
                        DaysToMature = 5,
                        CanGetPregnant = true,
                        HarvestType = FarmAnimalHarvestType.HarvestWithTool,
                        HarvestTool = "Milk Pail",
                        ProduceItemIds = new( new[]
                        {
                            new FarmAnimalProduce()
                            {
                                Id = "Default",
                                ItemId = "ThaleTheGreat.MoonMisadventures_GalaxyMilk",
                            }
                        } ),
                        DeluxeProduceItemIds = new(new[]
                        {
                            new FarmAnimalProduce()
                            {
                                Id = "Default",
                                ItemId = "ThaleTheGreat.MoonMisadventures_GalaxyMilk",
                            }
                        }),
                        ProfessionForHappinessBoost = 3,
                        ProfessionForQualityBoost = 3,
                        ProfessionForFasterProduce = -1,
                        Sound = "cow",
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/cow.png",
                        SpriteWidth = 32,
                        SpriteHeight = 32,
                        GrassEatAmount = 4,
                        HappinessDrain = 10,
                        UpDownPetHitboxTileSize = new Vector2(1, 1.75f),
                        LeftRightPetHitboxTileSize = new Vector2(1.75f, 1.25f),
                        BabyUpDownPetHitboxTileSize = new Vector2(1, 1.75f),
                        BabyLeftRightPetHitboxTileSize = new Vector2(1.75f, 1),
                    });
                    dict.Add("Lunar Chicken", new()
                    {
                        DisplayName = I18n.FarmAnimal_LunarChicken(),
                        House = "Coop",
                        DaysToMature = 3,
                        CanGetPregnant = false,
                        HarvestType = FarmAnimalHarvestType.DropOvernight,
                        ProduceItemIds = new(new[]
                        {
                            new FarmAnimalProduce()
                            {
                                Id = "Default",
                                ItemId = "ThaleTheGreat.MoonMisadventures_GalaxyEgg",
                            }
                        }),
                        DeluxeProduceItemIds = new(new[]
                        {
                            new FarmAnimalProduce()
                            {
                                Id = "Default",
                                ItemId = "ThaleTheGreat.MoonMisadventures_GalaxyEgg",
                            }
                        }),
                        ProfessionForHappinessBoost = 2,
                        ProfessionForQualityBoost = 2,
                        ProfessionForFasterProduce = -1,
                        Sound = "cluck",
                        Texture = "ThaleTheGreat.MoonMisadventures/assets/chicken.png",
                        SpriteWidth = 16,
                        SpriteHeight = 16,
                        EmoteOffset = new( 0, -16 ),
                        GrassEatAmount = 2,
                        HappinessDrain = 14,
                        UpDownPetHitboxTileSize = new Vector2(1, 1),
                        LeftRightPetHitboxTileSize = new Vector2(1, 1),
                        BabyUpDownPetHitboxTileSize = new Vector2(1, 1),
                        BabyLeftRightPetHitboxTileSize = new Vector2(1, 1),
                    });
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Machines"))
            {
                e.Edit((asset) =>
                {
                    var dict = asset.AsDictionary<string, MachineData>().Data;

                    dict["(BC)13"].OutputRules.Add(new()
                    {
                        Id = "ThaleTheGreat.MoonMisadventures_SmeltMythiciteOre",
                        Triggers = new(new[]
                        {
                            new MachineOutputTriggerRule()
                            {
                                Trigger = MachineOutputTrigger.ItemPlacedInMachine,
                                RequiredItemId = "(O)ThaleTheGreat.MoonMisadventures_MythiciteOre",
                                RequiredCount = 5,
                            }
                        }),
                        OutputItem = new(new[]
                        {
                            new MachineItemOutput()
                            {
                                Id = "(O)ThaleTheGreat.MoonMisadventures_MythiciteBar",
                                ItemId = "(O)ThaleTheGreat.MoonMisadventures_MythiciteBar",
                            }
                        }),
                        MinutesUntilReady = 720,
                    });
                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Tools"))
            {
                e.Edit((asset) =>
                {
                    var dict = asset.AsDictionary<string, ToolData>().Data;

                    dict.Add("ThaleTheGreat.MoonMisadventures.AnimalGloves",
                             new()
                             {
                                 ClassName = "AnimalGauntlets, MoonMisadventures",
                                 Name = "AnimalGauntlets",
                                 DisplayName = I18n.Tool_AnimalGauntlets_Name(),
                                 Description = I18n.Tool_AnimalGauntlets_Description(),
                                 Texture = "ThaleTheGreat.MoonMisadventures/assets/animal-gauntlets.png",
                                SpriteIndex = 0,
                             });
                    dict.Add("ThaleTheGreat.MoonMisadventures.LaserGun",
                             new()
                             {
                                 ClassName = "LaserGun, MoonMisadventures",
                                 Name = "LaserGun",
                                 DisplayName = I18n.Tool_LaserGun_Name(),
                                 Description = I18n.Tool_LaserGun_Description(),
                                 Texture = "ThaleTheGreat.MoonMisadventures/assets/animal-gauntlets.png",
                                 SpriteIndex = 0,
                             });

                });
            }

            if (e.NameWithoutLocale.IsEquivalentTo("Data/Locations"))
            {
                e.Edit((asset) =>
                {
                    var locs = asset.Data as Dictionary<string, LocationData>;

                    // TODO: Move over to CreateLocations

                    locs.Add("Custom_MM_MountainTop", new()
                    {
                        DisplayName = I18n.Location_MountainTop(),
                        DefaultArrivalTile = new(22, 24),
                    });
                    locs.Add("Custom_MM_MoonLandingArea", new()
                    {
                        DisplayName = I18n.Location_LandingArea(),
                        DefaultArrivalTile = new(9, 31),
                    });
                    locs.Add("Custom_MM_MoonAsteroidsEntrance", new()
                    {
                        DisplayName = I18n.Location_AsteroidsEntrance(),
                        DefaultArrivalTile = new(25, 26),
                    });
                    locs.Add("Custom_MM_MoonFarm", new()
                    {
                        DisplayName = I18n.Location_LunarFarm(),
                        DefaultArrivalTile = new(7, 11),
                        CanPlantHere = true,
                    });
                    locs.Add("Custom_MM_MoonFarmCave", new()
                    {
                        DisplayName = I18n.Location_LunarFarm(),
                        DefaultArrivalTile = new(6, 8),
                    });
                    locs.Add("Custom_MM_MoonFarmHouse", new()
                    {
                        DisplayName = I18n.Location_LunarFarm(),
                        DefaultArrivalTile = new(9, 9),
                    });
                    locs.Add("Custom_MM_MoonPlanetOverlook", new()
                    {
                        DisplayName = I18n.Location_PlanetOverlook(),
                        DefaultArrivalTile = new(24, 31),
                    });
                    locs.Add("Custom_MM_UfoInterior", new()
                    {
                        DisplayName = I18n.Location_UfoInterior(),
                        DefaultArrivalTile = new(12, 15),
                    });
                    locs.Add("Custom_MM_MoonInfuserRoom", new()
                    {
                        DisplayName = I18n.Location_MoonTemple(),
                        DefaultArrivalTile = new(15, 22),
                    });
                });
            }

            string currType = null;
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Crops"))
                currType = "Crop";
            if (e.NameWithoutLocale.IsEquivalentTo("Data/Weapons"))
                currType = "Weapon";
            if (currType == null)
                return;

            e.Edit((asset) =>
            {
                var data = Helper.ModContent.Load<Dictionary<string, JToken>>("assets/item-data.json");
                foreach (var entry in data)
                {
                    if (entry.Key.StartsWith(currType + ":") && entry.Value is JObject jobj)
                    {

                        string key = entry.Key.Substring(currType.Length + 1);

                        switch (currType)
                        {
                            case "Crop":
                                asset.AsDictionary<string, CropData>().Data.Add(ModManifest.UniqueID + "_" + key, jobj.ToObject<CropData>());
                                break;
                            case "Weapon":
                                WeaponData weapon = jobj.ToObject<WeaponData>()
                                    ?? throw new InvalidDataException($"Invalid weapon data entry '{entry.Key}'.");
                                weapon.DisplayName = ResolveTranslationTokens(weapon.DisplayName);
                                weapon.Description = ResolveTranslationTokens(weapon.Description);
                                asset.AsDictionary<string, WeaponData>().Data.Add(ModManifest.UniqueID + "_" + key, weapon);
                                break;

                        }
                    }
                }
            });
        }

        private void OnKeyCommand( string cmd, string[] args )
        {
            //Game1.player.addItemByMenuIfNecessary(new Necklace("speed"));
            Game1.player.team.get_hasLunarKey().Value = true;
        }

        private void OnInfuseCommand( string cmd, string[] args )
        {
            if (!Context.IsPlayerFree)
                return;

            Game1.activeClickableMenu = new InfuserMenu();
        }

        private void RegisterMythiciteToolTier()
        {
            if (mythiciteToolsRegistered)
                return;

            toolAndSprinklerUpgradesApi = Helper.ModRegistry.GetApi<IToolAndSprinklerUpgradesApi>("ThaleTheGreat.ToolAndSprinklerUpgrades");
            if (toolAndSprinklerUpgradesApi == null)
            {
                Log.Error("Tool and Sprinkler Upgrades is loaded, but its public API could not be accessed after all mods initialized.");
                return;
            }

            mythiciteToolsRegistered = toolAndSprinklerUpgradesApi.RegisterAlternativeLevelSixCoreTools(
                ModManifest.UniqueID,
                "Mythicite",
                "ThaleTheGreat.MoonMisadventures/assets/tools-mythicite.png",
                ItemIds.MythiciteBar,
                ItemIds.MythiciteAxe,
                Helper.Translation.Get("tool.axe.mythicite").ToString(),
                ItemIds.MythicitePickaxe,
                Helper.Translation.Get("tool.pick.mythicite").ToString(),
                ItemIds.MythiciteHoe,
                Helper.Translation.Get("tool.hoe.mythicite").ToString(),
                ItemIds.MythiciteWateringCan,
                Helper.Translation.Get("tool.wcan.mythicite").ToString()
            );

            if (!mythiciteToolsRegistered)
            {
                Log.Error("Tool and Sprinkler Upgrades rejected the Mythicite tool registration.");
                return;
            }

            ApplyMythiciteTransmutationSetting();
        }

        private void ApplyMythiciteTransmutationSetting()
        {
            if (!mythiciteToolsRegistered || toolAndSprinklerUpgradesApi == null)
                return;

            if (!toolAndSprinklerUpgradesApi.SetAlternativeTierBarTransmutationEnabled(
                ModManifest.UniqueID,
                "Mythicite",
                Config.EnableCobaltMythiciteTransmutation
            ))
            {
                Log.Error("Tool and Sprinkler Upgrades rejected the Mythicite transmutation setting.");
            }
        }

        private void ResetConfig()
        {
            Config = new Configuration();
            ApplyMythiciteTransmutationSetting();
        }

        private void SaveConfig()
        {
            Helper.WriteConfig(Config);
            ApplyMythiciteTransmutationSetting();
        }

        internal int GetBestPickaxeUpgradeLevel(Farmer farmer)
        {
            int level = farmer.Items
                .OfType<Pickaxe>()
                .Select(pickaxe => pickaxe.UpgradeLevel)
                .DefaultIfEmpty(0)
                .Max();

            if (walletToolsApi != null && farmer.UniqueMultiplayerID == Game1.player.UniqueMultiplayerID)
            {
                try
                {
                    level = Math.Max(level, walletToolsApi.GetStoredToolLevel("Pickaxe"));
                }
                catch
                {
                }
            }

            return Math.Max(0, level);
        }

        private void OnGameLaunched( object sender, GameLaunchedEventArgs e )
        {
            RegisterMythiciteToolTier();
            walletToolsApi = Helper.ModRegistry.GetApi<IWalletToolsApi>("ThaleTheGreat.WalletTools");

            var configMenu = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
            if (configMenu is null && this.Helper.ModRegistry.IsLoaded("spacechase0.GenericModConfigMenu"))
                Log.Warn("Generic Mod Config Menu is loaded, but its API could not be accessed.");
            if (configMenu != null)
            {
                configMenu.Register(
                    mod: this.ModManifest,
                    reset: ResetConfig,
                    save: SaveConfig,
                    titleScreenOnly: true
                );
                configMenu.AddBoolOption(
                    mod: this.ModManifest,
                    name: I18n.Config_FlashingUfo_Name,
                    tooltip: I18n.Config_FlashingUfo_Description,
                    getValue: () => this.Config.FlashingUfo,
                    setValue: value => this.Config.FlashingUfo = value
                );
                configMenu.AddBoolOption(
                    mod: this.ModManifest,
                    name: I18n.Config_CobaltMythiciteTransmutation_Name,
                    tooltip: I18n.Config_CobaltMythiciteTransmutation_Description,
                    getValue: () => this.Config.EnableCobaltMythiciteTransmutation,
                    setValue: value =>
                    {
                        this.Config.EnableCobaltMythiciteTransmutation = value;
                        ApplyMythiciteTransmutationSetting();
                    },
                    fieldId: "CobaltMythiciteTransmutation"
                );
            }

            ISpaceCoreApi? sc = Helper.ModRegistry.GetApi<ISpaceCoreApi>("spacechase0.SpaceCore");
            if (sc is null)
                throw new InvalidOperationException("SpaceCore's public API is unavailable.");

            sc.RegisterSerializerType( typeof( MountainTop ) );
            sc.RegisterSerializerType( typeof( LunarLocation ) );
            sc.RegisterSerializerType( typeof( MoonLandingArea ) );
            sc.RegisterSerializerType( typeof( AsteroidsEntrance ) );
            sc.RegisterSerializerType( typeof( AsteroidsDungeon ) );
            sc.RegisterSerializerType( typeof( BoomEye ) );
            sc.RegisterSerializerType( typeof( BoomProjectile ) );
            sc.RegisterSerializerType( typeof( AsteroidProjectile ) );
            sc.RegisterSerializerType( typeof( LunarFarm ) );
            sc.RegisterSerializerType( typeof( LunarFarmCave ) );
            sc.RegisterSerializerType( typeof( AnimalGauntlets ) );
            sc.RegisterSerializerType( typeof( Necklace ) );
            sc.RegisterSerializerType( typeof( MoonPlanetOverlook ) );
            sc.RegisterSerializerType( typeof( UfoInterior ) );
            sc.RegisterSerializerType( typeof( LunarFarmHouse ) );
            sc.RegisterSerializerType( typeof( MoonInfuserRoom ) );
            sc.RegisterSerializerType( typeof( LunarSlime ) );
            sc.RegisterSerializerType( typeof( UfoInteriorArsenal ) );
            sc.RegisterSerializerType( typeof( CrystalBehemoth ) );
            sc.RegisterSerializerType( typeof( EyeOfCthulhu ) );
            sc.RegisterSerializerType( typeof( ServantOfCthulhu ) );
            sc.RegisterCustomProperty( typeof( FarmerTeam ), "hasLunarKey", typeof( NetBool ), AccessTools.Method( typeof( FarmerTeam_LunarKey ), nameof( FarmerTeam_LunarKey.get_hasLunarKey ) ), AccessTools.Method( typeof( FarmerTeam_LunarKey ), nameof( FarmerTeam_LunarKey.set_hasLunarKey ) ) );
            sc.RegisterCustomProperty( typeof( Farmer ), "necklaceItem", typeof( NetRef< Item > ), AccessTools.Method( typeof( Farmer_Necklace ), nameof( Farmer_Necklace.get_necklaceItem ) ), AccessTools.Method( typeof( Farmer_Necklace ), nameof( Farmer_Necklace.set_necklaceItem ) ) );
        }

        private void OnTimeChanged( object sender, TimeChangedEventArgs e )
        {
            SyncMythicitePrismaticBarRecipe();
            AsteroidsDungeon.UpdateLevels10Minutes( e.NewTime );
        }

        private void OnUpdateTicked( object sender, UpdateTickedEventArgs e )
        {
            var necklace = Game1.player.get_necklaceItem().Value as Necklace;
            if ( necklace != null )
            {
                switch ( necklace.ItemId )
                {
                    case "speed":
                        {
                            var buff = Game1.player.buffs.AppliedBuffs.FirstOrDefault( b => b.Key == "necklace" ).Value;
                            if ( buff == null )
                            {
                                buff = new Buff("necklace", "necklace", I18n.Necklace(), 10 * 7000, effects: new BuffEffects() { Speed = { 3 } });
                                Game1.player.buffs.Apply( buff );
                            }
                            buff.millisecondsDuration = 1000;
                        }
                        break;
                    case "cooling":
                        {
                            if ( Game1.player.currentLocation is VolcanoDungeon volcano )
                            {
                                for ( int ix = -1; ix <= 1; ++ix )
                                {
                                    for ( int iy = -1; iy <= 1; ++iy )
                                    {
                                        var spot = Game1.player.Tile + new Vector2( ix, iy );
                                        if ( volcano.isTileOnMap( spot ) && volcano.waterTiles[ ( int ) spot.X, ( int ) spot.Y ] && !volcano.cooledLavaTiles.ContainsKey( spot ) )
                                            volcano.coolLavaEvent.Fire( new Point( ( int ) spot.X, ( int ) spot.Y ) );
                                    }
                                }
                            }
                        }
                        break;
                    case "sea":
                        {
                            if ( Game1.player.CurrentTool is FishingRod fr )
                            {
                                if ( fr.timeUntilFishingBite != -1 )
                                {
                                    fr.fishingBiteAccumulator += (int)(Game1.currentGameTime.ElapsedGameTime.Milliseconds * 1.5);
                                }
                                else if ( Game1.activeClickableMenu is BobberBar bb )
                                {
                                    if ( Helper.Reflection.GetField< bool >( bb, "bobberInBar" ).GetValue() )
                                    {
                                        var distCatchField = Helper.Reflection.GetField<float>( bb, "distanceFromCatching" );
                                        distCatchField.SetValue( distCatchField.GetValue() + 0.003f );
                                    }
                                }
                            }
                        }
                        break;
                }
            }
        }

        private static void SyncMythicitePrismaticBarRecipe()
        {
            if (!Context.IsWorldReady)
                return;

            if (Mod.instance.toolAndSprinklerUpgradesApi?.IsPrismaticHighest == true
                && Game1.player.craftingRecipes.ContainsKey("Prismatic Bar"))
            {
                if (!Game1.player.craftingRecipes.ContainsKey(MythicitePrismaticBarRecipeName))
                    Game1.player.craftingRecipes.Add(MythicitePrismaticBarRecipeName, 0);
            }
            else
            {
                Game1.player.craftingRecipes.Remove(MythicitePrismaticBarRecipeName);
            }
        }

        private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
        {
            SyncMythicitePrismaticBarRecipe();

            if (!Context.IsMainPlayer)
                return;

            canWriteSaveData = true;
            CurrentSaveData = Helper.Data.ReadSaveData<SaveData>(SaveDataKey) ?? new SaveData();
            if (CurrentSaveData.SchemaVersion > CurrentSaveSchemaVersion)
            {
                canWriteSaveData = false;
                Monitor.Log($"This save uses Moon Misadventures schema {CurrentSaveData.SchemaVersion}, but this build only supports schema {CurrentSaveSchemaVersion}. Save data will not be overwritten.", LogLevel.Error);
                return;
            }

            CurrentSaveData = MigrateSaveData(CurrentSaveData);
        }

        private static SaveData MigrateSaveData(SaveData data)
        {
            while (data.SchemaVersion < CurrentSaveSchemaVersion)
            {
                data = data.SchemaVersion switch
                {
                    _ => throw new InvalidDataException($"No migration is registered from Moon Misadventures schema {data.SchemaVersion} to {CurrentSaveSchemaVersion}.")
                };
            }

            return data;
        }

        private void OnSaving(object? sender, SavingEventArgs e)
        {
            if (!Context.IsMainPlayer || !canWriteSaveData)
                return;

            Helper.Data.WriteSaveData(SaveDataKey, CurrentSaveData);
        }

        internal bool IsEyeOfCthulhuDefeated()
        {
            return CurrentSaveData.EyeOfCthulhuDefeated;
        }

        internal void MarkEyeOfCthulhuDefeated()
        {
            if (!Context.IsMainPlayer)
                return;

            CurrentSaveData.EyeOfCthulhuDefeated = true;
            Game1.player.team.get_hasLunarKey().Value = true;
        }

        internal bool IsEyeOfCthulhuRewardClaimed()
        {
            return CurrentSaveData.EyeOfCthulhuRewardClaimed;
        }

        internal void MarkEyeOfCthulhuRewardClaimed()
        {
            if (Context.IsMainPlayer)
                CurrentSaveData.EyeOfCthulhuRewardClaimed = true;
        }

        private void OnDayStarted(object sender, DayStartedEventArgs e)
        {
            SyncMythicitePrismaticBarRecipe();
            AsteroidsDungeon.ClearAllLevels();
        }

        private void OnLoadStageChanged( object sender, LoadStageChangedEventArgs e )
        {
            if ( e.NewStage == LoadStage.CreatedInitialLocations || e.NewStage == LoadStage.SaveAddedLocations )
            {
                Game1.locations.Add( new MountainTop( Helper.ModContent ) );
                Game1.locations.Add( new MoonLandingArea( Helper.ModContent ) );
                Game1.locations.Add( new AsteroidsEntrance( Helper.ModContent ) );
                Game1.locations.Add( new LunarFarm( Helper.ModContent ) );
                Game1.locations.Add( new LunarFarmCave( Helper.ModContent ) );
                Game1.locations.Add( new MoonPlanetOverlook( Helper.ModContent ) );
                Game1.locations.Add( new UfoInterior( Helper.ModContent ) );
                Game1.locations.Add( new LunarFarmHouse( Helper.ModContent ) );
                Game1.locations.Add( new MoonInfuserRoom( Helper.ModContent ) );
                Game1.locations.Add( new UfoInteriorArsenal( Helper.ModContent ) );
            }
        }

        private void OnMenuChanged( object sender, MenuChangedEventArgs e )
        {
            if ( e.NewMenu is AnimalQueryMenu aquery )
            {
                if (Game1.currentLocation is LunarLocation)
                {
                    // We don't want the move animal button at all.
                    // Hide it off screen, make it unreachable with controllers
                    aquery.moveHomeButton.bounds = new Rectangle(99999, 99999, 1, 1);
                    aquery.textBoxCC.downNeighborID = aquery.sellButton.myID;
                    aquery.sellButton.upNeighborID = aquery.textBoxCC.myID;
                }
            }
        }

        private void OnRenderingWorld( object sender, RenderingWorldEventArgs e )
        {
            if ( Game1.background is SpaceBackground )
            {
                // This part doesn't do anything normally (https://github.com/MonoGame/MonoGame/issues/5441),
                // but SpriteMaster makes it work. So need this for compatibility.
                if ( Game1.graphics.PreferredDepthStencilFormat != DepthFormat.Depth24Stencil8 )
                {
                    Game1.graphics.PreferredDepthStencilFormat = DepthFormat.Depth24Stencil8;
                    Game1.graphics.ApplyChanges();
                }

                DefaultStencilOverride = StencilDarken;
                Game1.graphics.GraphicsDevice.Clear( ClearOptions.Stencil, Color.Transparent, 0, 0 );
            }
        }

        private void OnRenderedWorld( object sender, RenderedWorldEventArgs e )
        {
            DefaultStencilOverride = null;
        }

        private void OnRenderedHud(object? sender, RenderedHudEventArgs e)
        {
            if (Game1.currentLocation is not AsteroidsDungeon dungeon || dungeon.level.Value != AsteroidsDungeon.BossLevel)
                return;

            EyeOfCthulhu? boss = dungeon.characters.OfType<EyeOfCthulhu>().FirstOrDefault(monster => monster.Health > 0);
            if (boss != null)
                BossHealthBar.Draw(e.SpriteBatch, boss);
        }

        private string ResolveTranslationTokens(string value)
        {
            const string prefix = "{{i18n:";
            while (value.Contains(prefix, StringComparison.Ordinal))
            {
                int start = value.IndexOf(prefix, StringComparison.Ordinal);
                int end = value.IndexOf("}}", start, StringComparison.Ordinal);
                if (end < 0)
                    throw new InvalidDataException($"Unclosed translation token in '{value}'.");

                string key = value.Substring(start + prefix.Length, end - start - prefix.Length);
                value = value.Substring(0, start) + Helper.Translation.Get(key) + value.Substring(end + 2);
            }
            return value;
        }

        private void OnReturnedToTitle( object sender, ReturnedToTitleEventArgs e )
        {
            AsteroidsDungeon.ClearAllLevels();
            CurrentSaveData = new SaveData();
            canWriteSaveData = true;
        }

        private void OnWarped( object sender, WarpedEventArgs e )
        {
            if ( e.OldLocation is LunarLocation || e.NewLocation is LunarLocation )
            {
                Helper.GameContent.InvalidateCache( "TerrainFeatures/hoeDirt" );
            }

            if ( e.NewLocation?.NameOrUniqueName == "Mine" )
            {
                global::ThaleTheGreat.MoonMisadventures.MapTileHelper.SetMapTile( e.NewLocation, 43, 10, 173, "Buildings", "Warp 21 39 Custom_MM_MountainTop", 1);
            }
        }

        internal static void HandleGiftGiven(Farmer farmer, NPC npc, StardewValley.Object gift)
        {
            if (gift.ItemId != ItemIds.SoulSapphire)
                return;

            foreach (string key in Game1.objectData.Keys)
            {
                var obj = new StardewValley.Object(key, 1);
                if (!obj.canBeGivenAsGift() || obj.questItem.Value || obj.QualifiedItemId == "(O)809")
                    continue;

                if (!farmer.giftedItems[npc.Name].ContainsKey(key) && (obj.Name != "Stone" || key == "390"))
                    farmer.giftedItems[npc.Name].Add(key, 0);
            }
        }
    }
}
