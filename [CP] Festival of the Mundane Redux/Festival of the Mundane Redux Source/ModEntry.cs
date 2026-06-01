using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Extensions;
using StardewValley.GameData;
using StardewValley.GameData.Shirts;
using StardewValley.Menus;
using StardewValley.Minigames;
using StardewValley.Monsters;
using StardewValley.Objects;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using xTile;
using xTile.Layers;
using xTile.ObjectModel;
using xTile.Tiles;

namespace ShadowFestival;

public class ModEntry : Mod
{
  internal static ModData Data;
  internal static readonly Random Random = new Random();
  private static readonly Vector2 VendorTile = new Vector2(8f, 19f);
  protected bool _gettingKickedOut;
  protected bool _mapChanged;
  protected readonly Dictionary<string, int> _currentDialogueIndex = new Dictionary<string, int>();
  private Dictionary<string, string> _betterThingsTranslations;
  private string _betterThingsTranslationLocale;
  protected readonly List<(LightSource Light, Vector2 Position)> _weirdLights = new List<(LightSource, Vector2)>();
  protected readonly List<(object Firefly, Vector2 Tile, object Light)> _lampFireflies = new List<(object, Vector2, object)>();
  private readonly List<ModEntry.FloatingGlow> _floatingGlows = new List<ModEntry.FloatingGlow>();
  private readonly List<ModEntry.FestivalJunimo> _festivalJunimos = new List<ModEntry.FestivalJunimo>();
  private Texture2D _junimoTexture;
  private readonly List<ModEntry.FestivalFrogSpot> _festivalFrogSpots = new List<ModEntry.FestivalFrogSpot>();
  protected Texture2D _goblinNoseTexture;
  protected Texture2D _glowOrbTexture;
  protected bool _registeredUpdate;
  public static Action<Vector2> setGoblinNosePosition;
  protected GameLocation _goblinNoseSpriteLocation;
  protected TemporaryAnimatedSprite _goblinNoseSprite;

  internal static ModEntry Instance { get; private set; }






  public override void Entry(IModHelper helper)
  {
    ModEntry.Instance = this;
    ModEntry.Data = this.LoadFestivalData();
    this._mapChanged = false;
    this._gettingKickedOut = false;
    this._registeredUpdate = false;
    this._goblinNoseTexture = this.LoadTexture("Mods/MouseyPounds.ShadowFestival/Goblin_Nose", "assets/Goblin_Nose.png");
    this._glowOrbTexture = this.LoadTexture("Mods/MouseyPounds.ShadowFestival/GlowOrb", "assets/GlowOrb.png");
    HarmonyPatcher.Hook(new Harmony(this.ModManifest.UniqueID), this.Monitor);
    helper.Events.Input.ButtonPressed += new EventHandler<ButtonPressedEventArgs>(this.Input_ButtonPressed);
    helper.Events.GameLoop.DayStarted += new EventHandler<DayStartedEventArgs>(this.GameLoop_DayStarted);
    helper.Events.GameLoop.SaveLoaded += new EventHandler<SaveLoadedEventArgs>(this.GameLoop_SaveLoaded);
    helper.Events.Content.AssetRequested += new EventHandler<AssetRequestedEventArgs>(this.Content_AssetRequested);
    helper.Events.Player.Warped += new EventHandler<WarpedEventArgs>(this.Player_Warped);
  }

  private ModData LoadFestivalData()
  {
    try
    {
      ModData data = this.Helper.GameContent.Load<ModData>("Mods/MouseyPounds.ShadowFestival/Data");
      if (data != null)
        return data;
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Couldn't load CP-provided festival data; falling back to local data.json. Details: {ex.Message}", LogLevel.Trace);
    }

    return this.Helper.Data.ReadJsonFile<ModData>("data.json") ?? new ModData();
  }

  private Texture2D LoadTexture(string assetName, string fallbackPath)
  {
    try
    {
      return this.Helper.GameContent.Load<Texture2D>(assetName);
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Couldn't load CP-provided asset '{assetName}'; falling back to '{fallbackPath}'. Details: {ex.Message}", LogLevel.Trace);
      return this.Helper.ModContent.Load<Texture2D>(fallbackPath);
    }
  }

  private bool TryOpenShop(string shopId)
  {
    try
    {
      foreach (MethodInfo method in typeof(Utility).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
      {
        if (!string.Equals(method.Name, "TryOpenShopMenu", StringComparison.Ordinal) && !string.Equals(method.Name, "OpenShopMenu", StringComparison.Ordinal))
          continue;

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length < 1 || parameters[0].ParameterType != typeof(string))
          continue;

        object[] args = new object[parameters.Length];
        args[0] = shopId;
        for (int i = 1; i < parameters.Length; i++)
        {
          if (parameters[i].HasDefaultValue)
            args[i] = parameters[i].DefaultValue;
          else if (parameters[i].ParameterType == typeof(bool))
            args[i] = false;
          else if (parameters[i].ParameterType == typeof(string))
            args[i] = null;
          else if (parameters[i].ParameterType.IsValueType)
            args[i] = Activator.CreateInstance(parameters[i].ParameterType);
          else
            args[i] = null;
        }

        object result = method.Invoke(null, args);
        if (method.ReturnType == typeof(bool) && result is bool opened)
          return opened;
        return Game1.activeClickableMenu != null;
      }
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Couldn't open shop '{shopId}' through Utility reflection. Details: {ex.Message}", LogLevel.Trace);
    }

    return false;
  }

  public void SetGoblinNosePosition(Vector2 position)
  {
    this._RemoveGoblinNose();
    if (Game1.currentLocation == null || !Game1.currentLocation.Name.Equals("Sewer"))
      return;
    this._goblinNoseSpriteLocation = Game1.currentLocation;
    position.Y = 1216f;
    this._goblinNoseSprite = new TemporaryAnimatedSprite()
    {
      texture = this._goblinNoseTexture,
      animationLength = 1,
      position = position,
      sourceRect = new Rectangle(0, 0, 16, 16),
      sourceRectStartingPos = Vector2.Zero,
      interval = 9999f,
      totalNumberOfLoops = 9999,
      scale = 4f
    };
    this._goblinNoseSpriteLocation.TemporarySprites.Add(this._goblinNoseSprite);
  }

  protected void _RemoveGoblinNose()
  {
    if (this._goblinNoseSprite == null)
      return;
    this._goblinNoseSpriteLocation?.TemporarySprites.Remove(this._goblinNoseSprite);
    this._goblinNoseSprite = null;
    this._goblinNoseSpriteLocation = null;
  }

  private void Player_Warped(object sender, WarpedEventArgs e)
  {
    this._RemoveGoblinNose();
    this._gettingKickedOut = false;
    if (e.IsLocalPlayer && this._registeredUpdate)
    {
      this.Helper.Events.GameLoop.UpdateTicking -= new EventHandler<UpdateTickingEventArgs>(this.UpdateWeirdLights);
      ModEntry.setGoblinNosePosition = (Action<Vector2>) null;
      this._registeredUpdate = false;
      if (e.OldLocation != null && this._lampFireflies.Count > 0)
        this.RemoveLampFireflies(e.OldLocation);
      if (e.OldLocation != null && this._floatingGlows.Count > 0)
        this.RemoveFloatingGlows(e.OldLocation);
      if (e.OldLocation != null && this._festivalFrogSpots.Count > 0)
        this.RemoveFestivalFrogs(e.OldLocation);
      if (e.OldLocation != null && this._festivalJunimos.Count > 0)
        this.RemoveFestivalJunimos(e.OldLocation);
      this._lampFireflies.Clear();
      this._floatingGlows.Clear();
      this._weirdLights.Clear();
      this._festivalFrogSpots.Clear();
      this._festivalJunimos.Clear();
    }
    if (!e.IsLocalPlayer || e.NewLocation == null || !e.NewLocation.Name.Equals("Sewer") || !this.IsShadowFestivalToday())
      return;
    ModEntry.setGoblinNosePosition += new Action<Vector2>(this.SetGoblinNosePosition);
    ((NetHashSet<string>) e.Player.mailReceived).Add("ShadowFestivalVisited");
    string str = null;
    try
    {
      if (e.NewLocation.Map?.TileSheets != null && e.NewLocation.Map.TileSheets.Count > 1)
        str = ((Component) e.NewLocation.Map.TileSheets[1]).Id;
    }
    catch
    {
    }
    if (str == null)
      str = "untitled tile sheet";
    e.NewLocation.setMapTile(31, 16, 77, "Front", str, null, true);
    e.NewLocation.setMapTile(31, 17, 85, "Buildings", str, null, true);
    e.NewLocation.setTileProperty(31, 17, "Buildings", "Action", "FestivalDialogue BigShadow");
    if (e.NewLocation.characters.Count > 0)
    {
      foreach (NPC character in e.NewLocation.characters)
      {
        if (((Character) character)?.Name == "Krobus")
        {
          character.setTilePosition(3, 20);
          this.ApplyKrobusTrenchcoat(character);
          break;
        }
      }
    }
    this.ApplyRuffianCottonFestivalPosition(e.NewLocation);
    IReflectedField<List<Critter>> field = this.Helper.Reflection.GetField<List<Critter>>((object) e.NewLocation, "critters", false);
    if (field != null && field.GetValue() == null)
      field.SetValue(new List<Critter>());
    this.SetupFestivalFrogs(e.NewLocation);
    this.RemoveFloatingGlows(e.NewLocation);
    this._floatingGlows.Clear();
    int num1 = 100;
    Map map1 = e.NewLocation.Map;
    int? nullable1;
    if (map1 == null)
    {
      nullable1 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map1.Layers;
      nullable1 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerWidth : new int?();
    }
    int num2 = nullable1 ?? 40;
    Map map2 = e.NewLocation.Map;
    int? nullable2;
    if (map2 == null)
    {
      nullable2 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map2.Layers;
      nullable2 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerHeight : new int?();
    }
    int num3 = nullable2 ?? 60;
    float num4 = 192f;
    float num5 = (float) (((double) num2 - 3.0) * 64.0);
    float num6 = 640f;
    float num7 = (float) (((double) num3 - 6.0) * 64.0);
    for (int index = 0; index < num1; ++index)
    {
      float num8 = MathHelper.Lerp(num4, num5, (float) ModEntry.Random.NextDouble());
      float num9 = MathHelper.Lerp(num6, num7, (float) ModEntry.Random.NextDouble());
      Vector2 position;
      position = new Vector2(num8, num9);
      Vector2 velocity;
      velocity = new Vector2((float) (ModEntry.Random.NextDouble() * 0.6 - 0.3), (float) (ModEntry.Random.NextDouble() * 0.4 - 0.2));
      float offset = (float) ModEntry.Random.NextDouble() * 10f;
      TemporaryAnimatedSprite sprite = new TemporaryAnimatedSprite()
      {
        texture = this._glowOrbTexture,
        animationLength = 1,
        totalNumberOfLoops = 999999,
        interval = 99999f,
        position = position,
        sourceRect = new Rectangle(0, 0, this._glowOrbTexture.Width, this._glowOrbTexture.Height),
        scale = 2f,
        alpha = 0.75f,
        color = ModEntry.GetPrismaticColor(0.0f, offset),
        layerDepth = (float) (((double) position.Y + 32.0) / 10000.0)
      };
      e.NewLocation.TemporarySprites.Add(sprite);
      this._floatingGlows.Add(new ModEntry.FloatingGlow(sprite, position, velocity, offset));
    }
    this._weirdLights.Clear();
    HashSet<(int, int)> lampTiles = new HashSet<(int, int)>();
    Map map3 = e.NewLocation.Map;
    Layer layer1 = map3?.GetLayer("Front");
    Layer layer2 = map3?.GetLayer("AlwaysFront");
    Layer layer3 = map3?.GetLayer("Buildings");
    if (layer1 != null || layer2 != null)
    {
      Layer layer4 = layer1 ?? layer2;
      for (int x = 0; x < layer4.LayerWidth; ++x)
      {
        for (int y = 0; y < layer4.LayerHeight; ++y)
        {
          Tile tileFront = layer1?.Tiles[x, y];
          Tile tileAF = layer2?.Tiles[x, y];
          Tile tileBld = layer3?.Tiles[x, y];
          if (HasTileIndex(122))
            this.AddWeirdLight(e.NewLocation, x, y);
          if (HasTileIndex(65) || HasTileIndex(66) || HasTileIndex(67) || HasTileIndex(122))
            lampTiles.Add((x, y));

          bool HasTileIndex(int index)
          {
            return matches(tileFront) || matches(tileAF) || matches(tileBld);

            bool matches(Tile t) => t != null && t.TileIndex == index;
          }
        }
      }
    }
    this._lampFireflies.Clear();
    this.Monitor.Log($"ShadowFestival: found {lampTiles.Count} lamp tiles for prismatic fireflies.", (LogLevel) 0);
    if (lampTiles.Count > 0)
      this.AddLampFireflies(e.NewLocation, lampTiles);
    Game1.changeMusicTrack("WizardSong", false, (MusicContext) 0);
    this.Helper.Events.GameLoop.UpdateTicking += new EventHandler<UpdateTickingEventArgs>(this.UpdateWeirdLights);
    this._registeredUpdate = true;
  }

  private void AddStaticLight(
    int textureIndex,
    int x,
    int y,
    float yOffsetTiles,
    float radius,
    Color color)
  {
    Vector2 position;
    position = new Vector2((float) (((double) x + 0.5) * 64.0), (float) (((double) y + (double) yOffsetTiles) * 64.0));
    LightSource lightSource = ModEntry.TryCreateLightSource(textureIndex, position, radius, color);
    if (lightSource == null)
      return;
    GameExtensions.Add((IDictionary<string, LightSource>) Game1.currentLightSources, lightSource);
  }

  public void AddWeirdLight(GameLocation location, int x, int y)
  {
    Vector2 position;
    position = new Vector2((float) (((double) x + 0.5) * 64.0), (float) (((double) y + 0.5) * 64.0));
    LightSource lightSource = ModEntry.TryCreateLightSource(4, position, 1.0f, new Color((int) byte.MaxValue, 10, 10));
    if (lightSource == null)
      return;
    this._weirdLights.Add((lightSource, position));
    GameExtensions.Add((IDictionary<string, LightSource>) Game1.currentLightSources, lightSource);
  }

  public void UpdateWeirdLights(object sender, UpdateTickingEventArgs e)
  {
    float t = (float) Game1.currentGameTime.TotalGameTime.TotalMilliseconds / 1000f;
    foreach ((LightSource Light, Vector2 Position) in this._weirdLights)
    {
      float num1 = (float) ((Math.Sin((double) t * 0.3 + (double) Position.X * 1.15 + (double) Position.Y * 0.33) + 1.0) / 2.0);
      Color color;
      color = new Color(0, 0, 0);
      if ((double) num1 < 0.33000001311302185)
      {
        float num2 = MathHelper.Clamp(num1 / 0.33f, 0.0f, 1f);
        color.R = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(1f, 0.0f, num2));
        color.G = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(0.0f, 1f, num2));
      }
      else if ((double) num1 < 0.6600000262260437)
      {
        float num3 = MathHelper.Clamp((float) (((double) num1 - 0.33000001311302185) / 0.33000001311302185), 0.0f, 1f);
        color.G = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(1f, 0.0f, num3));
        color.B = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(0.0f, 1f, num3));
      }
      else
      {
        float num4 = MathHelper.Clamp((float) (((double) num1 - 0.6600000262260437) / 0.33000001311302185), 0.0f, 1f);
        color.B = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(1f, 0.0f, num4));
        color.R = (byte) ((double) byte.MaxValue * (double) MathHelper.Lerp(0.0f, 1f, num4));
      }
      float num5 = (float) (0.3 + (Math.Sin((double) t + (double) Position.X + (double) Position.Y) + 1.0) / 2.0 * 0.3);
      ModEntry.TrySetNetFieldValue((object) Light, "color", (object) color);
      ModEntry.TrySetNetFieldValue((object) Light, "radius", (object) num5);
    }
    this.UpdateFloatingGlows(t, Game1.currentLocation);
    this.UpdateLampFireflies(t);
    this.UpdateFestivalFrogs(Game1.currentLocation);
    this.ApplyRuffianCottonFestivalPosition(Game1.currentLocation);
    this.UpdatePassiveFestivalSewerSlimes(Game1.currentLocation);
  }

  private void ApplyRuffianCottonFestivalPosition(GameLocation location)
  {
    if (!ModEntry.IsFestivalSewerActive(location) || !this.Helper.ModRegistry.IsLoaded("Fellowclown.TR"))
      return;

    foreach (NPC npc in location.characters.ToList())
    {
      string internalName = ((Character) npc)?.Name ?? string.Empty;
      string displayName = npc.displayName ?? string.Empty;
      if (!string.Equals(internalName, "FC.Cotton", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(internalName, "Cotton", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(displayName, "Cotton", StringComparison.OrdinalIgnoreCase))
        continue;

      npc.setTilePosition(15, 11);
      npc.faceDirection(2);
      npc.Halt();
      return;
    }
  }

  private void UpdatePassiveFestivalSewerSlimes(GameLocation location)
  {
    if (!ModEntry.IsFestivalSewerActive(location))
      return;

    foreach (NPC character in location.characters.ToList())
    {
      if (character is not Monster monster || !ModEntry.IsSlimeMonster(monster))
        continue;

      ModEntry.TrySetFieldOrProperty(monster, "focusedOnFarmers", false);
      ModEntry.TrySetFieldOrProperty(monster, "withinPlayerThreshold", false);
      ModEntry.TrySetFieldOrProperty(monster, "seenPlayer", false);
    }
  }

  private void SetupFestivalFrogs(GameLocation location)
  {
    if (location == null)
      return;
    this.RemoveFestivalFrogs(location);
    this._festivalFrogSpots.Clear();
    GameTime currentGameTime = Game1.currentGameTime;
    double nowMs = currentGameTime != null ? currentGameTime.TotalGameTime.TotalMilliseconds : 0.0;
    this.AddFestivalFrogSpotRange(location, 4, 26, 30, true, false, nowMs);
    this.AddFestivalFrogSpotFixed(location, 15, 26, true, true, nowMs);
    this.AddFestivalFrogSpotFixed(location, 18, 27, true, false, nowMs);
  }

  private void AddFestivalFrogSpotFixed(
    GameLocation location,
    int x,
    int y,
    bool flip,
    bool jumpIntoWater,
    double nowMs)
  {
    ModEntry.FestivalFrogSpot festivalFrogSpot = new ModEntry.FestivalFrogSpot()
    {
      X = x,
      YFixed = y,
      UseRange = false,
      Flip = flip,
      JumpIntoWater = jumpIntoWater
    };
    Frog frog = new Frog(festivalFrogSpot.GetSpawnTile(), festivalFrogSpot.Flip, festivalFrogSpot.JumpIntoWater);
    location.addCritter((Critter) frog);
    festivalFrogSpot.Current = frog;
    festivalFrogSpot.NextRespawnMs = nowMs + (double) ModEntry.Random.Next(2500, 6500);
    this._festivalFrogSpots.Add(festivalFrogSpot);
  }

  private void AddFestivalFrogSpotRange(
    GameLocation location,
    int x,
    int yMin,
    int yMax,
    bool flip,
    bool jumpIntoWater,
    double nowMs)
  {
    ModEntry.FestivalFrogSpot festivalFrogSpot = new ModEntry.FestivalFrogSpot()
    {
      X = x,
      YMin = yMin,
      YMax = yMax,
      UseRange = true,
      Flip = flip,
      JumpIntoWater = jumpIntoWater
    };
    Frog frog = new Frog(festivalFrogSpot.GetSpawnTile(), festivalFrogSpot.Flip, festivalFrogSpot.JumpIntoWater);
    location.addCritter((Critter) frog);
    festivalFrogSpot.Current = frog;
    festivalFrogSpot.NextRespawnMs = nowMs + (double) ModEntry.Random.Next(2500, 6500);
    this._festivalFrogSpots.Add(festivalFrogSpot);
  }

  private void RemoveFestivalFrogs(GameLocation location)
  {
    if (location == null || this._festivalFrogSpots.Count == 0)
      return;
    List<Critter> critterList = this.Helper.Reflection.GetField<List<Critter>>((object) location, "critters", false)?.GetValue() ?? this.Helper.Reflection.GetField<List<Critter>>((object) location, "Critters", false)?.GetValue();
    if (critterList == null)
      return;
    foreach (ModEntry.FestivalFrogSpot festivalFrogSpot in this._festivalFrogSpots)
    {
      if (festivalFrogSpot.Current != null)
        critterList.Remove((Critter) festivalFrogSpot.Current);
      festivalFrogSpot.Current = (Frog) null;
    }
  }

  private void UpdateFestivalFrogs(GameLocation location)
  {
    if (location == null || this._festivalFrogSpots.Count == 0 || !location.Name.Equals("Sewer") || !this.IsShadowFestivalToday())
      return;
    GameTime currentGameTime = Game1.currentGameTime;
    double num = currentGameTime != null ? currentGameTime.TotalGameTime.TotalMilliseconds : 0.0;
    List<Critter> critterList = this.Helper.Reflection.GetField<List<Critter>>((object) location, "critters", false)?.GetValue() ?? this.Helper.Reflection.GetField<List<Critter>>((object) location, "Critters", false)?.GetValue();
    if (critterList == null)
      return;
    for (int index = 0; index < this._festivalFrogSpots.Count; ++index)
    {
      ModEntry.FestivalFrogSpot festivalFrogSpot = this._festivalFrogSpots[index];
      if (num >= festivalFrogSpot.NextRespawnMs)
      {
        if (festivalFrogSpot.Current != null)
          critterList.Remove((Critter) festivalFrogSpot.Current);
        Frog frog = new Frog(festivalFrogSpot.GetSpawnTile(), festivalFrogSpot.Flip, festivalFrogSpot.JumpIntoWater);
        location.addCritter((Critter) frog);
        festivalFrogSpot.Current = frog;
        festivalFrogSpot.NextRespawnMs = num + (double) ModEntry.Random.Next(2500, 6500);
      }
    }
  }

  private void UpdateFloatingGlows(float t, GameLocation location)
  {
    if (this._floatingGlows.Count == 0 || location == null)
      return;
    Map map1 = location.Map;
    int? nullable1;
    if (map1 == null)
    {
      nullable1 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map1.Layers;
      nullable1 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerWidth : new int?();
    }
    int num1 = nullable1 ?? 40;
    Map map2 = location.Map;
    int? nullable2;
    if (map2 == null)
    {
      nullable2 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map2.Layers;
      nullable2 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerHeight : new int?();
    }
    int num2 = nullable2 ?? 60;
    float num3 = 192f;
    float num4 = (float) (((double) num1 - 3.0) * 64.0);
    float num5 = 640f;
    float num6 = (float) (((double) num2 - 6.0) * 64.0);
    for (int index = 0; index < this._floatingGlows.Count; ++index)
    {
      ModEntry.FloatingGlow floatingGlow1 = this._floatingGlows[index];
      ModEntry.FloatingGlow floatingGlow2 = floatingGlow1;
      floatingGlow2.Position = (floatingGlow2.Position + floatingGlow1.Velocity);
      floatingGlow1.Position.Y += (float) Math.Sin((double) t * 0.800000011920929 + (double) floatingGlow1.Offset) * 0.08f;
      if ((double) floatingGlow1.Position.X < (double) num3 || (double) floatingGlow1.Position.X > (double) num4)
      {
        floatingGlow1.Velocity.X *= -1f;
        floatingGlow1.Position.X = MathHelper.Clamp(floatingGlow1.Position.X, num3, num4);
      }
      if ((double) floatingGlow1.Position.Y < (double) num5 || (double) floatingGlow1.Position.Y > (double) num6)
      {
        floatingGlow1.Velocity.Y *= -1f;
        floatingGlow1.Position.Y = MathHelper.Clamp(floatingGlow1.Position.Y, num5, num6);
      }
      floatingGlow1.Sprite.position = floatingGlow1.Position;
      floatingGlow1.Sprite.layerDepth = (float) (((double) floatingGlow1.Position.Y + 32.0) / 10000.0);
      floatingGlow1.Sprite.color = ModEntry.GetPrismaticColor(t, floatingGlow1.Offset);
      floatingGlow1.Sprite.alpha = (float) (0.5 + (Math.Sin((double) t * 1.7000000476837158 + (double) floatingGlow1.Offset) + 1.0) / 2.0 * 0.5);
    }
  }

  private void RemoveFloatingGlows(GameLocation location)
  {
    if (location == null || this._floatingGlows.Count == 0)
      return;
    foreach (ModEntry.FloatingGlow floatingGlow in this._floatingGlows)
      location.TemporarySprites.Remove(floatingGlow.Sprite);
  }

  private void SpawnFestivalJunimos(GameLocation location)
  {
    if (location == null)
      return;
    this.RemoveFestivalJunimos(location);
    this._festivalJunimos.Clear();
    try
    {
      if (this._junimoTexture == null)
        this._junimoTexture = Game1.content.Load<Texture2D>("Characters/Junimo");
    }
    catch
    {
      return;
    }
    Map map1 = location.Map;
    int? nullable1;
    if (map1 == null)
    {
      nullable1 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map1.Layers;
      nullable1 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerWidth : new int?();
    }
    int layerWidth = nullable1 ?? 40;
    Map map2 = location.Map;
    int? nullable2;
    if (map2 == null)
    {
      nullable2 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map2.Layers;
      nullable2 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerHeight : new int?();
    }
    int layerHeight = nullable2 ?? 60;
    List<Vector2> landTiles = new List<Vector2>(256);
    for (int x = 0; x < layerWidth; ++x)
    {
      for (int y = 0; y < layerHeight; ++y)
      {
        if (!ModEntry.IsWaterTile(location, x, y))
        {
          Vector2 v;
          v = new Vector2((float) x, (float) y);
          if (location.isTileLocationTotallyClearAndPlaceable(v) && y >= 8 && x >= 2 && x <= layerWidth - 3 && y <= layerHeight - 3)
            landTiles.Add(v);
        }
      }
    }
    if (landTiles.Count == 0)
      return;
    List<(Vector2 Shore, Vector2 Water)> shoreWaterPairs = this.FindShoreWaterPairs(location, layerWidth, layerHeight);
    int num1 = 6;
    int num2 = shoreWaterPairs.Count > 0 ? 2 : 0;
    for (int index = 0; index < num1; ++index)
    {
      Vector2 vector2 = landTiles[ModEntry.Random.Next(landTiles.Count)];
      ModEntry.FestivalJunimo junimo = this.CreateJunimo(location, vector2, false);
      junimo.State = ModEntry.JunimoState.Wander;
      junimo.Target = this.PickNearbyLandTarget(vector2, landTiles);
      this._festivalJunimos.Add(junimo);
    }
    for (int index = 0; index < num2; ++index)
    {
      (Vector2 vector2, Vector2 Water) = shoreWaterPairs[ModEntry.Random.Next(shoreWaterPairs.Count)];
      ModEntry.FestivalJunimo junimo = this.CreateJunimo(location, vector2, true);
      junimo.State = ModEntry.JunimoState.Idle;
      junimo.IdleTicks = ModEntry.Random.Next(60, 220);
      junimo.JumpFrom = (vector2 * 64f);
      junimo.JumpTo = (Water * 64f);
      junimo.JumpHeight = (float) ModEntry.Random.Next(26, 46);
      this._festivalJunimos.Add(junimo);
    }
  }

  private void RemoveFestivalJunimos(GameLocation location)
  {
    if (location == null || this._festivalJunimos.Count == 0)
      return;
    foreach (ModEntry.FestivalJunimo festivalJunimo in this._festivalJunimos)
    {
      if (festivalJunimo?.Sprite != null)
        location.TemporarySprites.Remove(festivalJunimo.Sprite);
    }
  }

  private void UpdateFestivalJunimos(GameLocation location)
  {
    if (location == null || this._festivalJunimos.Count == 0 || !location.Name.Equals("Sewer") || !this.IsShadowFestivalToday())
      return;
    Map map1 = location.Map;
    int? nullable1;
    if (map1 == null)
    {
      nullable1 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map1.Layers;
      nullable1 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerWidth : new int?();
    }
    int layerWidth = nullable1 ?? 40;
    Map map2 = location.Map;
    int? nullable2;
    if (map2 == null)
    {
      nullable2 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map2.Layers;
      nullable2 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerHeight : new int?();
    }
    int layerHeight = nullable2 ?? 60;
    List<(Vector2, Vector2)> shoreWaterPairs = this.FindShoreWaterPairs(location, layerWidth, layerHeight);
    for (int index = 0; index < this._festivalJunimos.Count; ++index)
    {
      ModEntry.FestivalJunimo festivalJunimo = this._festivalJunimos[index];
      if (festivalJunimo?.Sprite != null)
      {
        if (!festivalJunimo.IsWaterJumper)
          this.UpdateJunimoWander(location, festivalJunimo);
        else
          this.UpdateJunimoWaterCycle(location, festivalJunimo, shoreWaterPairs);
      }
    }
  }

  private void UpdateJunimoWander(GameLocation location, ModEntry.FestivalJunimo j)
  {
    if ((double) Vector2.DistanceSquared(j.Position, j.Target) < 6.0)
    {
      j.State = ModEntry.JunimoState.Idle;
      j.IdleTicks = ModEntry.Random.Next(40, 160);
    }
    if (j.State == ModEntry.JunimoState.Idle)
    {
      --j.IdleTicks;
      if (j.IdleTicks <= 0)
      {
        j.State = ModEntry.JunimoState.Wander;
        j.Target = (j.Position + new Vector2((float) ModEntry.Random.Next(-8, 9) * 64f, (float) ModEntry.Random.Next(-6, 7) * 64f));
      }
    }
    if (j.State == ModEntry.JunimoState.Wander)
    {
      Vector2 vector2_1 = (j.Target - j.Position);
      float num = vector2_1.Length();
      if ((double) num > 0.0099999997764825821)
      {
        Vector2 vector2_2 = (vector2_1 / num) * j.Speed;
        ModEntry.FestivalJunimo festivalJunimo = j;
        festivalJunimo.Position = (festivalJunimo.Position + vector2_2);
      }
    }
    Map map1 = location.Map;
    int? nullable1;
    if (map1 == null)
    {
      nullable1 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map1.Layers;
      nullable1 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerWidth : new int?();
    }
    int num1 = nullable1 ?? 40;
    Map map2 = location.Map;
    int? nullable2;
    if (map2 == null)
    {
      nullable2 = new int?();
    }
    else
    {
      ReadOnlyCollection<Layer> layers = map2.Layers;
      nullable2 = layers != null ? layers.FirstOrDefault<Layer>()?.LayerHeight : new int?();
    }
    int num2 = nullable2 ?? 60;
    int x = (int) Math.Floor((double) j.Position.X / 64.0);
    int y = (int) Math.Floor((double) j.Position.Y / 64.0);
    if (x < 0 || y < 0 || x >= num1 || y >= num2)
    {
      x = Math.Clamp(x, 0, num1 - 1);
      y = Math.Clamp(y, 0, num2 - 1);
      j.Position = new Vector2(x * 64f, y * 64f);
      j.Target = (j.Position + new Vector2((float) ModEntry.Random.Next(-6, 7) * 64f, (float) ModEntry.Random.Next(-5, 6) * 64f));
    }
    Vector2 v;
    v = new Vector2((float) x, (float) y);
    if (!location.isTileLocationTotallyClearAndPlaceable(v) || ModEntry.IsWaterTile(location, x, y))
    {
      ModEntry.FestivalJunimo festivalJunimo = j;
      festivalJunimo.Position -= new Vector2(ModEntry.Random.Next(-2, 2) * 16f, ModEntry.Random.Next(-1, 2) * 16f);
      j.Target = (j.Position + new Vector2((float) ModEntry.Random.Next(-8, 9) * 64f, (float) ModEntry.Random.Next(-6, 7) * 64f));
    }
    j.Sprite.position = j.Position;
    j.Sprite.layerDepth = (float) (((double) j.Position.Y + 64.0) / 10000.0);
  }

  private void UpdateJunimoWaterCycle(
    GameLocation location,
    ModEntry.FestivalJunimo j,
    List<(Vector2 Shore, Vector2 Water)> shoreWaterPairs)
  {
    switch (j.State)
    {
      case ModEntry.JunimoState.Idle:
        --j.IdleTicks;
        if (j.IdleTicks <= 0)
        {
          j.State = ModEntry.JunimoState.JumpIn;
          j.JumpProgress = 0.0f;
          j.Sprite.alpha = 1f;
          break;
        }
        break;
      case ModEntry.JunimoState.JumpIn:
        j.JumpProgress += 0.06f;
        ModEntry.ApplyJumpArc(j, j.JumpFrom, j.JumpTo, true);
        if ((double) j.JumpProgress >= 1.0)
        {
          j.State = ModEntry.JunimoState.Underwater;
          j.UnderwaterTicks = ModEntry.Random.Next(80, 220);
          j.Sprite.alpha = 0.0f;
          break;
        }
        break;
      case ModEntry.JunimoState.Underwater:
        --j.UnderwaterTicks;
        if (j.UnderwaterTicks <= 0)
        {
          if (shoreWaterPairs.Count > 0)
          {
            (Vector2 Shore, Vector2 Water) shoreWaterPair = shoreWaterPairs[ModEntry.Random.Next(shoreWaterPairs.Count)];
            j.JumpFrom = (shoreWaterPair.Water * 64f);
            j.JumpTo = (shoreWaterPair.Shore * 64f);
          }
          j.State = ModEntry.JunimoState.JumpOut;
          j.JumpProgress = 0.0f;
          j.Sprite.alpha = 1f;
          break;
        }
        break;
      case ModEntry.JunimoState.JumpOut:
        j.JumpProgress += 0.06f;
        ModEntry.ApplyJumpArc(j, j.JumpFrom, j.JumpTo, false);
        if ((double) j.JumpProgress >= 1.0)
        {
          j.State = ModEntry.JunimoState.Idle;
          j.IdleTicks = ModEntry.Random.Next(60, 260);
          j.Sprite.alpha = 1f;
          break;
        }
        break;
    }
    j.Sprite.layerDepth = (float) (((double) j.Sprite.position.Y + 64.0) / 10000.0);
  }

  private static void ApplyJumpArc(
    ModEntry.FestivalJunimo j,
    Vector2 from,
    Vector2 to,
    bool goingIntoWater)
  {
    float num1 = MathHelper.Clamp(j.JumpProgress, 0.0f, 1f);
    Vector2 vector2 = Vector2.Lerp(from, to, num1);
    float num2 = (float) Math.Sin((double) num1 * Math.PI) * j.JumpHeight;
    vector2.Y -= num2;
    if (goingIntoWater && (double) num1 > 0.699999988079071)
      j.Sprite.alpha = MathHelper.Clamp((float) (1.0 - ((double) num1 - 0.699999988079071) / 0.30000001192092896), 0.0f, 1f);
    j.Position = vector2;
    j.Sprite.position = vector2;
  }

  private ModEntry.FestivalJunimo CreateJunimo(
    GameLocation location,
    Vector2 tile,
    bool isWaterJumper)
  {
    int num1 = 16;
    int num2 = 16;
    int num3 = Math.Max(0, this._junimoTexture.Height / num2 - 1);
    int num4 = num3 > 0 ? ModEntry.Random.Next(Math.Min(6, num3 + 1)) : 0;
    TemporaryAnimatedSprite temporaryAnimatedSprite = new TemporaryAnimatedSprite()
    {
      texture = this._junimoTexture,
      animationLength = 4,
      totalNumberOfLoops = 999999,
      interval = 200f,
      position = (tile * 64f),
      sourceRect = new Rectangle(0, num4 * num2, num1, num2),
      sourceRectStartingPos = new Vector2(0.0f, (float) (num4 * num2)),
      scale = 4f,
      alpha = 1f,
      layerDepth = (float) (((double) tile.Y * 64.0 + 64.0) / 10000.0)
    };
    location.TemporarySprites.Add(temporaryAnimatedSprite);
    return new ModEntry.FestivalJunimo()
    {
      Sprite = temporaryAnimatedSprite,
      Position = (tile * 64f),
      Target = (tile * 64f),
      Speed = (float) (0.550000011920929 + ModEntry.Random.NextDouble() * 0.550000011920929),
      IsWaterJumper = isWaterJumper,
      JumpHeight = 34f
    };
  }

  private Vector2 PickNearbyLandTarget(Vector2 startTile, List<Vector2> landTiles)
  {
    for (int index = 0; index < 25; ++index)
    {
      Vector2 landTile = landTiles[ModEntry.Random.Next(landTiles.Count)];
      if ((double) Vector2.DistanceSquared(landTile, startTile) <= 196.0)
        return (landTile * 64f);
    }
    return (landTiles[ModEntry.Random.Next(landTiles.Count)] * 64f);
  }

  private static bool IsWaterTile(GameLocation location, int x, int y)
  {
    return location != null && (location.doesTileHaveProperty(x, y, "Water", "Back", false) != null || location.doesTileHaveProperty(x, y, "Water", "Buildings", false) != null || location.doesTileHaveProperty(x, y, "Water", "Front", false) != null);
  }

  private List<(Vector2 Shore, Vector2 Water)> FindShoreWaterPairs(
    GameLocation location,
    int layerWidth,
    int layerHeight)
  {
    List<(Vector2, Vector2)> shoreWaterPairs = new List<(Vector2, Vector2)>(64);
    if (location == null)
      return shoreWaterPairs;
    for (int x1 = 0; x1 < layerWidth; ++x1)
    {
      for (int y1 = 0; y1 < layerHeight; ++y1)
      {
        if (ModEntry.IsWaterTile(location, x1, y1))
        {
          (int, int)[] valueTupleArray = new (int, int)[4]
          {
            (1, 0),
            (-1, 0),
            (0, 1),
            (0, -1)
          };
          foreach ((int num1, int num2) in valueTupleArray)
          {
            int x2 = x1 + num1;
            int y2 = y1 + num2;
            if (IsLandAndClear(x2, y2))
            {
              shoreWaterPairs.Add((new Vector2((float) x2, (float) y2), new Vector2((float) x1, (float) y1)));
              break;
            }
          }
        }
      }
    }
    return shoreWaterPairs;

    bool IsLandAndClear(int x, int y)
    {
      return x >= 0 && y >= 0 && x < layerWidth && y < layerHeight && !ModEntry.IsWaterTile(location, x, y) && location.isTileLocationTotallyClearAndPlaceable(new Vector2((float) x, (float) y));
    }
  }

  private void AddLampFireflies(GameLocation location, HashSet<(int X, int Y)> lampTiles)
  {
    foreach ((int X, int Y) in lampTiles)
    {
      Vector2 vector2;
      vector2 = new Vector2((float) X + 0.5f, (float) Y - 0.35f);
      Firefly firefly = new Firefly(vector2);
      object obj = this.Helper.Reflection.GetField<object>((object) firefly, "light", false)?.GetValue();
      if (obj != null)
      {
        Color prismaticColor = ModEntry.GetPrismaticColor(0.0f, (float) ((double) X * 0.70999997854232788 + (double) Y * 1.1299999952316284));
        ModEntry.TrySetNetFieldValue(obj, "color", (object) prismaticColor);
        ModEntry.TrySetNetFieldValue(obj, "radius", (object) 0.28f);
      }
      ModEntry.TrySetFieldOrProperty((object) firefly, "motion", (object) Vector2.Zero);
      ModEntry.TrySetFieldOrProperty((object) firefly, "velocity", (object) Vector2.Zero);
      location.addCritter((Critter) firefly);
      this._lampFireflies.Add(((object) firefly, vector2, obj));
    }
  }

  private void UpdateLampFireflies(float t)
  {
    if (this._lampFireflies.Count == 0)
      return;
    for (int index = 0; index < this._lampFireflies.Count; ++index)
    {
      (object Firefly, Vector2 Tile, object Light) = this._lampFireflies[index];
      ModEntry.TrySetFieldOrProperty(Firefly, "position", (object) Tile);
      ModEntry.TrySetFieldOrProperty(Firefly, "tilePosition", (object) Tile);
      ModEntry.TrySetFieldOrProperty(Firefly, "motion", (object) Vector2.Zero);
      ModEntry.TrySetFieldOrProperty(Firefly, "velocity", (object) Vector2.Zero);
      if (Light != null)
      {
        float offset = (float) ((double) Tile.X * 0.70999997854232788 + (double) Tile.Y * 1.1299999952316284);
        Color prismaticColor = ModEntry.GetPrismaticColor(t, offset);
        float num = (float) (0.24 + (Math.Sin((double) t * 1.7000000476837158 + (double) offset) + 1.0) / 2.0 * 0.1);
        ModEntry.TrySetNetFieldValue(Light, "color", (object) prismaticColor);
        ModEntry.TrySetNetFieldValue(Light, "radius", (object) num);
      }
    }
  }

  private void RemoveLampFireflies(GameLocation location)
  {
    if (location == null || this._lampFireflies.Count == 0 || !(this.Helper.Reflection.GetField<object>((object) location, "critters", false)?.GetValue() is IList list))
      return;
    foreach ((object Firefly, Vector2 Tile, object Light) lampFirefly in this._lampFireflies)
    {
      if (lampFirefly.Firefly is Critter firefly)
        list.Remove((object) firefly);
    }
  }

  private static Color GetPrismaticColor(float t, float offset)
  {
    float num = t * 1.35f + offset;
    return new Color((byte) (60.0 + (Math.Sin((double) num) * 0.5 + 0.5) * 160.0), (byte) (60.0 + (Math.Sin((double) num + 2.0943951606750488) * 0.5 + 0.5) * 160.0), (byte) (60.0 + (Math.Sin((double) num + 4.1887903213500977) * 0.5 + 0.5) * 160.0), (byte) 255);
  }

  private void Content_AssetRequested(object sender, AssetRequestedEventArgs e)
  {
    if (!Context.IsWorldReady || !this.IsShadowFestivalToday() || !this.Helper.ModRegistry.IsLoaded("maat.BetterThings"))
      return;

    if (e.NameWithoutLocale.IsEquivalentTo("Characters/Dialogue/Wizard"))
      e.Edit(asset => this.AliasBetterThingsFestivalDialogue(asset.AsDictionary<string, string>().Data, "wizard-dialogue-festival"), AssetEditPriority.Late);
    else if (e.NameWithoutLocale.IsEquivalentTo("Characters/Dialogue/Agatha"))
      e.Edit(asset => this.AliasBetterThingsFestivalDialogue(asset.AsDictionary<string, string>().Data, "agatha-dialogue-festival-of-mundane"), AssetEditPriority.Late);
    else if (e.NameWithoutLocale.IsEquivalentTo("Strings/Schedules/Agatha"))
      e.Edit(asset => this.AliasBetterThingsAgathaScheduleStrings(asset.AsDictionary<string, string>().Data), AssetEditPriority.Late);
  }

  private void AliasBetterThingsFestivalDialogue(IDictionary<string, string> dialogue, string fallbackTranslationKey)
  {
    if (dialogue == null)
      return;

    if (dialogue.TryGetValue("Custom_FestivalOfTheMundane", out string festivalDialogue) && !string.IsNullOrWhiteSpace(festivalDialogue))
    {
      dialogue["Sewer"] = festivalDialogue;
      return;
    }

    if (this.TryGetBetterThingsTranslation(fallbackTranslationKey, out festivalDialogue))
      dialogue["Sewer"] = festivalDialogue;
  }

  private void AliasBetterThingsAgathaScheduleStrings(IDictionary<string, string> strings)
  {
    if (strings == null)
      return;

    this.CopyBetterThingsTranslation(strings, "agatha-dialogue-festival-of-mundane");
    this.CopyBetterThingsTranslation(strings, "agatha-spouse-dialogue-festival-of-mundane");
    this.CopyBetterThingsTranslation(strings, "agatha-dialogue-festival-of-mundane-working");
    this.CopyBetterThingsTranslation(strings, "agatha-spouse-dialogue-festival-of-mundane-working");
  }

  private void CopyBetterThingsTranslation(IDictionary<string, string> target, string key)
  {
    if (this.TryGetBetterThingsTranslation(key, out string value))
      target[key] = value;
  }

  private bool TryGetBetterThingsTranslation(string key, out string value)
  {
    value = null;

    Dictionary<string, string> translations = this.GetBetterThingsTranslations();
    return translations != null && translations.TryGetValue(key, out value) && !string.IsNullOrWhiteSpace(value);
  }

  private Dictionary<string, string> GetBetterThingsTranslations()
  {
    string locale = LocalizedContentManager.CurrentLanguageCode.ToString();
    if (this._betterThingsTranslations != null && string.Equals(this._betterThingsTranslationLocale, locale, StringComparison.OrdinalIgnoreCase))
      return this._betterThingsTranslations;

    this._betterThingsTranslationLocale = locale;
    this._betterThingsTranslations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    object modInfo = this.Helper.ModRegistry.Get("maat.BetterThings");
    string directoryPath = modInfo?.GetType().GetProperty("DirectoryPath")?.GetValue(modInfo) as string;
    if (string.IsNullOrWhiteSpace(directoryPath))
      return this._betterThingsTranslations;

    this.ReadBetterThingsTranslationFile(Path.Combine(directoryPath, "i18n", "default.json"), this._betterThingsTranslations);
    if (!string.Equals(locale, "en", StringComparison.OrdinalIgnoreCase))
      this.ReadBetterThingsTranslationFile(Path.Combine(directoryPath, "i18n", locale + ".json"), this._betterThingsTranslations);

    return this._betterThingsTranslations;
  }

  private void ReadBetterThingsTranslationFile(string path, Dictionary<string, string> translations)
  {
    if (!File.Exists(path))
      return;

    try
    {
      Dictionary<string, string> fileTranslations = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), new JsonSerializerOptions
      {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
      });
      if (fileTranslations == null)
        return;

      foreach (KeyValuePair<string, string> pair in fileTranslations)
      {
        if (!string.IsNullOrWhiteSpace(pair.Value))
          translations[pair.Key] = pair.Value;
      }
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Failed reading Better Things translation file '{path}': {ex.Message}", LogLevel.Warn);
    }
  }

  private void GameLoop_SaveLoaded(object sender, SaveLoadedEventArgs e)
  {
    this.InvalidateBetterThingsFestivalDialogueAssets();
  }

  private void InvalidateBetterThingsFestivalDialogueAssets()
  {
    if (!Context.IsWorldReady || !this.IsShadowFestivalToday() || !this.Helper.ModRegistry.IsLoaded("maat.BetterThings"))
      return;

    this._betterThingsTranslations = null;
    this.Helper.GameContent.InvalidateCache("Characters/Dialogue/Wizard");
    this.Helper.GameContent.InvalidateCache("Characters/Dialogue/Agatha");
    this.Helper.GameContent.InvalidateCache("Strings/Schedules/Agatha");
  }

  private void GameLoop_DayStarted(object sender, DayStartedEventArgs e)
  {
    this._currentDialogueIndex.Clear();
    PinGame.claimedPrizes?.Clear();
    this.InvalidateBetterThingsFestivalDialogueAssets();

    if (this.IsShadowFestivalTomorrow())
    {
      this.Monitor.Log("It is the day before the shadow festival.", LogLevel.Trace);
      if (!((NetHashSet<string>) Game1.player.mailReceived).Contains("Wizard_ShadowFestival"))
        ((NetHashSet<string>) Game1.player.mailForTomorrow).Add("Wizard_ShadowFestival");
    }
  }

  private static bool IsClickingFestivalHatMouse(Vector2 tile)
  {
    return Math.Abs(tile.X - ModEntry.VendorTile.X) <= 1f && Math.Abs(tile.Y - ModEntry.VendorTile.Y) <= 1f;
  }

  private void Input_ButtonPressed(object sender, ButtonPressedEventArgs e)
  {
    if (!Context.IsWorldReady || Game1.currentLocation == null || !this.IsShadowFestivalToday() || Game1.player.freezePause > 0 || this._gettingKickedOut || !Game1.currentLocation.Name.Equals("Sewer") || Game1.activeClickableMenu != null || !SButtonExtensions.IsActionButton(e.Button))
      return;

    Vector2 grabTile = e.Cursor.GrabTile;
    Vector2 facingTile = Game1.player.GetGrabTile();
    Vector2 playerTile = Game1.player.Tile;

    if (this.IsClickingKrobus(grabTile))
      return;

    if (Game1.player.FacingDirection == 0 && (ModEntry.IsClickingFestivalHatMouse(grabTile) || ModEntry.IsClickingFestivalHatMouse(facingTile) || ModEntry.IsClickingFestivalHatMouse(playerTile)))
    {
      this.Helper.Input.Suppress(e.Button);
      DelayedAction.functionAfterDelay(() =>
      {
        if (Game1.activeClickableMenu != null || Game1.currentLocation == null || !Game1.currentLocation.Name.Equals("Sewer") || !this.IsShadowFestivalToday())
          return;
        this.TryOpenShop("HatMouse");
      }, 1);
      return;
    }

    string str1 = Game1.currentLocation.doesTileHaveProperty((int) grabTile.X, (int) grabTile.Y, "Action", "Buildings", false);
    if (str1 == null)
      return;
    string[] strArray = str1.Split(' ', StringSplitOptions.None);
    if (strArray.Length >= 2 && strArray[0] == "FestivalDialogue")
    {
      this.Helper.Input.Suppress(e.Button);
      bool flag = ((NetFieldBase<Hat, NetRef<Hat>>) Game1.player.hat).Value != null && ModEntry.Data.CalmingHats.Contains(((Item) ((NetFieldBase<Hat, NetRef<Hat>>) Game1.player.hat).Value).Name);
      if (!strArray[1].StartsWith("BigShadow") && !strArray[1].StartsWith("Snack") && !strArray[1].StartsWith("Festival_AncientDoll") && !flag)
      {
        this._gettingKickedOut = true;
        Game1.playSound("shadowpeep");
        DelayedAction.playSoundAfterDelay("clubSmash", 1200, null, new Vector2?(), -1, false);
        Game1.globalFadeToBlack(new Game1.afterFadeFunction(this.AfterFade), 0.045f);
        Game1.player.CanMove = false;
        Game1.player.freezePause = 1000;
      }
      else
      {
        string key = strArray[1];
        if (!this._currentDialogueIndex.ContainsKey(key))
          this._currentDialogueIndex[key] = 0;
        int num = this._currentDialogueIndex[key];
        string str2 = $"{key}_{num}";
        string str3 = this.Helper.Translation.Get(str2).ToString();
        if (str3.Contains(str2))
        {
          this._currentDialogueIndex[key] = 0;
          str3 = this.Helper.Translation.Get(key + "_0").ToString();
        }
        Game1.drawObjectDialogue(str3);
        this._currentDialogueIndex[key]++;
      }
    }
    else
    {
      if (strArray.Length < 1 || !(strArray[0] == "PinGame"))
        return;
      this.Helper.Input.Suppress(e.Button);
      string str4 = "PinGame.Barker.0";
      if (PinGame.claimedPrizes != null)
        str4 = $"PinGame.Barker.{Math.Min(5, PinGame.claimedPrizes.Count)}";
      Game1.currentLocation.createQuestionDialogue(this.Helper.Translation.Get(str4).ToString(), new Response[2]
      {
        new Response("Play", this.Helper.Translation.Get("PinGame.Answer.Play").ToString()),
        new Response("Leave", this.Helper.Translation.Get("PinGame.Answer.Leave").ToString())
      }, new GameLocation.afterQuestionBehavior(this.OnPinGameAnswer), null);
    }
  }

  private bool IsClickingKrobus(Vector2 grabTile)
  {
    GameLocation location = Game1.currentLocation;
    if (location == null)
      return false;

    Rectangle clickedTileArea = new Rectangle((int) grabTile.X * Game1.tileSize, (int) grabTile.Y * Game1.tileSize, Game1.tileSize, Game1.tileSize);
    Vector2 clickPixel = grabTile * Game1.tileSize + new Vector2(Game1.tileSize / 2f, Game1.tileSize / 2f);

    foreach (NPC character in location.characters)
    {
      if (((Character) character)?.Name != "Krobus")
        continue;

      Rectangle krobusBounds = character.GetBoundingBox();
      if (krobusBounds.Contains((int) clickPixel.X, (int) clickPixel.Y) || krobusBounds.Intersects(clickedTileArea))
        return true;
    }

    return false;
  }

  private void ApplyKrobusTrenchcoat(NPC krobus)
  {
    if (krobus == null || !this.IsShadowFestivalToday())
      return;

    try
    {
      if (krobus.Sprite != null)
        krobus.Sprite.LoadTexture("Characters\\Krobus_Trenchcoat");
      else
        krobus.Sprite = new AnimatedSprite("Characters\\Krobus_Trenchcoat", 0, 16, 32);
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Couldn't load vanilla Krobus trenchcoat sprite from Characters/Krobus_Trenchcoat; leaving vanilla Krobus sprite. Details: {ex.Message}", LogLevel.Warn);
    }

    try
    {
      krobus.Portrait = this.Helper.GameContent.Load<Texture2D>("Portraits/Krobus_Trenchcoat");
    }
    catch (Exception ex)
    {
      this.Monitor.Log($"Couldn't load vanilla Krobus trenchcoat portrait from Portraits/Krobus_Trenchcoat; leaving vanilla Krobus portrait. Details: {ex.Message}", LogLevel.Warn);
    }
  }

  private void OnPinGameAnswer(Farmer who, string answer)
  {
    if (!(answer == "Play"))
      return;
    Game1.currentMinigame = (IMinigame) new PinGame();
  }

  public bool IsShadowFestivalToday()
  {
    return string.Equals(Game1.currentSeason, "fall", StringComparison.OrdinalIgnoreCase) && Game1.dayOfMonth == 27;
  }

  internal static bool IsFestivalSewerActive(GameLocation location = null)
  {
    if (!Context.IsWorldReady || ModEntry.Instance == null || !ModEntry.Instance.IsShadowFestivalToday())
      return false;

    GameLocation currentLocation = location ?? Game1.currentLocation;
    return currentLocation != null && string.Equals(currentLocation.Name, "Sewer", StringComparison.Ordinal);
  }

  internal static bool IsSlimeMonster(Monster monster)
  {
    if (monster == null)
      return false;

    Type type = monster.GetType();
    if (type == typeof(GreenSlime) || string.Equals(type.Name, "GreenSlime", StringComparison.Ordinal) || string.Equals(type.Name, "BigSlime", StringComparison.Ordinal))
      return true;

    string monsterName = monster.Name;
    return (!string.IsNullOrWhiteSpace(monsterName) && monsterName.IndexOf("Slime", StringComparison.OrdinalIgnoreCase) >= 0)
      || (monster.displayName != null && monster.displayName.IndexOf("Slime", StringComparison.OrdinalIgnoreCase) >= 0)
      || type.Name.IndexOf("Slime", StringComparison.OrdinalIgnoreCase) >= 0;
  }

  public bool IsShadowFestivalTomorrow()
  {
    return string.Equals(Game1.currentSeason, "fall", StringComparison.OrdinalIgnoreCase) && Game1.dayOfMonth == 26;
  }

  public void AfterFade()
  {
    ((NetFieldBase<bool, NetBool>) ((Character) Game1.player).swimming).Value = false;
    Game1.player.changeOutOfSwimSuit();
    Game1.drawObjectDialogue(this.Helper.Translation.Get("interaction.nohat").ToString());
    Game1.messagePause = true;
    Game1.warpFarmer("Forest", 94, 100, 2);
    Game1.fadeToBlackAlpha = 1f;
  }

  private static void TrySetNetFieldValue(object obj, string memberName, object value)
  {
    if (obj == null)
      return;
    Type type = obj.GetType();
    FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((field != (FieldInfo) null) && ModEntry.TrySetValueProperty(field.GetValue(obj), value))
      return;
    PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (!(property != (PropertyInfo) null))
      return;
    ModEntry.TrySetValueProperty(property.GetValue(obj), value);
  }

  private static bool TrySetValueProperty(object target, object value)
  {
    if (target == null)
      return false;
    PropertyInfo property = target.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((property != (PropertyInfo) null) && property.CanWrite)
    {
      property.SetValue(target, value);
      return true;
    }
    Type type = target.GetType();
    Type c = value?.GetType();
    if ((object) c == null)
      c = typeof (object);
    if (!type.IsAssignableFrom(c))
      return false;
    target = value;
    return true;
  }

  private static LightSource TryCreateLightSource(
    int textureIndex,
    Vector2 position,
    float radius,
    Color color)
  {
    try
    {
      ConstructorInfo[] constructors = typeof (LightSource).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      foreach (ConstructorInfo constructorInfo in (IEnumerable<ConstructorInfo>) ((IEnumerable<ConstructorInfo>) constructors).OrderBy<ConstructorInfo, int>((Func<ConstructorInfo, int>) (p => ((MethodBase) p).GetParameters().Length)))
      {
        ParameterInfo[] parameters = ((MethodBase) constructorInfo).GetParameters();
        if (parameters.Length >= 5 && !(parameters[0].ParameterType != typeof (string)) && parameters[1].ParameterType == typeof (int) && parameters[2].ParameterType == typeof (Vector2) && parameters[3].ParameterType == typeof (float) && parameters[4].ParameterType == typeof (Color))
        {
          string str = $"ShadowFestival:{textureIndex}:{(int) position.X}:{(int) position.Y}";
          object[] objArray = new object[parameters.Length];
          objArray[0] = (object) str;
          objArray[1] = (object) textureIndex;
          objArray[2] = (object) position;
          objArray[3] = (object) radius;
          objArray[4] = (object) color;
          for (int index = 5; index < parameters.Length; ++index)
            objArray[index] = !parameters[index].HasDefaultValue ? (parameters[index].ParameterType.IsValueType ? Activator.CreateInstance(parameters[index].ParameterType) : (object) null) : parameters[index].DefaultValue;
          if (constructorInfo.Invoke(objArray) is LightSource lightSource)
            return lightSource;
        }
      }
      foreach (ConstructorInfo constructorInfo in (IEnumerable<ConstructorInfo>) ((IEnumerable<ConstructorInfo>) constructors).OrderBy<ConstructorInfo, int>((Func<ConstructorInfo, int>) (p => ((MethodBase) p).GetParameters().Length)))
      {
        ParameterInfo[] parameters = ((MethodBase) constructorInfo).GetParameters();
        if (parameters.Length >= 4 && !(parameters[0].ParameterType != typeof (int)) && !(parameters[1].ParameterType != typeof (Vector2)) && !(parameters[2].ParameterType != typeof (float)) && !(parameters[3].ParameterType != typeof (Color)))
        {
          object[] objArray = new object[parameters.Length];
          objArray[0] = (object) textureIndex;
          objArray[1] = (object) position;
          objArray[2] = (object) radius;
          objArray[3] = (object) color;
          for (int index = 4; index < parameters.Length; ++index)
            objArray[index] = !parameters[index].HasDefaultValue ? (parameters[index].ParameterType.IsValueType ? Activator.CreateInstance(parameters[index].ParameterType) : (object) null) : parameters[index].DefaultValue;
          if (constructorInfo.Invoke(objArray) is LightSource lightSource)
            return lightSource;
        }
      }
    }
    catch
    {
    }
    return (LightSource) null;
  }

  private static string GetStringFieldOrProperty(object obj, string name)
  {
    if (obj == null)
      return null;
    Type type = obj.GetType();
    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((property != (PropertyInfo) null) && property.PropertyType == typeof (string))
      return property.GetValue(obj) as string;
    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    return (field != (FieldInfo) null) && field.FieldType == typeof (string) ? field.GetValue(obj) as string : null;
  }

  private static void SetString(object obj, string name, string value)
  {
    if (obj == null)
      return;
    Type type = obj.GetType();
    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((property != (PropertyInfo) null) && property.PropertyType == typeof (string) && property.CanWrite)
    {
      property.SetValue(obj, (object) value);
    }
    else
    {
      FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (!(field != (FieldInfo) null) || !(field.FieldType == typeof (string)))
        return;
      field.SetValue(obj, (object) value);
    }
  }

  private static void SetInt(object obj, string name, int value)
  {
    if (obj == null)
      return;
    Type type = obj.GetType();
    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((property != (PropertyInfo) null) && property.PropertyType == typeof (int) && property.CanWrite)
    {
      property.SetValue(obj, (object) value);
    }
    else
    {
      FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (!(field != (FieldInfo) null) || !(field.FieldType == typeof (int)))
        return;
      field.SetValue(obj, (object) value);
    }
  }

  private static void SetBool(object obj, string name, bool value)
  {
    if (obj == null)
      return;
    Type type = obj.GetType();
    PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((property != (PropertyInfo) null) && property.PropertyType == typeof (bool) && property.CanWrite)
    {
      property.SetValue(obj, (object) value);
    }
    else
    {
      FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (!(field != (FieldInfo) null) || !(field.FieldType == typeof (bool)))
        return;
      field.SetValue(obj, (object) value);
    }
  }

  private static void TrySetFieldOrProperty(object obj, string name, object value)
  {
    if (obj == null)
      return;
    Type type = obj.GetType();
    FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if ((field != (FieldInfo) null) && field.FieldType.IsAssignableFrom(value.GetType()))
    {
      field.SetValue(obj, value);
    }
    else
    {
      PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (!(property != (PropertyInfo) null) || !property.CanWrite || !property.PropertyType.IsAssignableFrom(value.GetType()))
        return;
      property.SetValue(obj, value);
    }
  }

  private static void TryInvokeNoArgs(object obj, string methodName)
  {
    if (obj == null || string.IsNullOrEmpty(methodName))
      return;

    MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
    method?.Invoke(obj, null);
  }

  private sealed class FloatingGlow
  {
    public TemporaryAnimatedSprite Sprite;
    public Vector2 Position;
    public Vector2 Velocity;
    public float Offset;

    public FloatingGlow(
      TemporaryAnimatedSprite sprite,
      Vector2 position,
      Vector2 velocity,
      float offset)
    {
      this.Sprite = sprite;
      this.Position = position;
      this.Velocity = velocity;
      this.Offset = offset;
    }
  }

  private sealed class FestivalJunimo
  {
    public TemporaryAnimatedSprite Sprite;
    public Vector2 Position;
    public Vector2 Target;
    public float Speed;
    public bool IsWaterJumper;
    public Vector2 JumpFrom;
    public Vector2 JumpTo;
    public float JumpProgress;
    public float JumpHeight;
    public int UnderwaterTicks;
    public int IdleTicks;
    public ModEntry.JunimoState State;
  }

  private enum JunimoState
  {
    Wander,
    Idle,
    JumpIn,
    Underwater,
    JumpOut,
  }

  private sealed class FestivalFrogSpot
  {
    public int X;
    public int YFixed;
    public int YMin;
    public int YMax;
    public bool UseRange;
    public bool Flip;
    public bool JumpIntoWater;
    public Frog Current;
    public double NextRespawnMs;

    public Vector2 GetSpawnTile()
    {
      return new Vector2((float) this.X, this.UseRange ? (float) ModEntry.Random.Next(this.YMin, this.YMax + 1) : (float) this.YFixed);
    }
  }
}
