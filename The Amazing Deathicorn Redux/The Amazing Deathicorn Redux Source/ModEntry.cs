using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Audio;
using StardewValley.Characters;
using StardewValley.Monsters;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

#nullable enable
namespace TheAmazingDeathicornRedux;

public sealed class ModEntry : Mod
{
  private static readonly Vector2?[] HornTipByFrame;
  private const string ModHorseAsset = "assets/horse.png";
  private const string BeamAtlasAsset = "assets/laser_beam_atlas.png";
  private const string ImpactAtlasAsset = "assets/laser_impact_atlas.png";
  private const string BoltAsset = "assets/laser_bolt.png";
  private Texture2D? BeamAtlas;
  private Texture2D? ImpactAtlas;
  private Texture2D? BoltTexture;
  private ModConfig Config = new ModConfig();
  private LaserColor? LastLoggedGlowColor;
  private LightSource? DeathicornLight;
  private string DeathicornLightKey = "";
  private string? LastGlowLocationName;
  private int DeathicornLightNumericId;
  private bool LoggedLightSourceCtor;
  private bool LoggedLightSourceCtorFailure;
  private int? GlowLightTextureIndex;
  private bool LoggedGlowTextureIndex;
  private readonly List<ModEntry.Beam> ActiveBeams = new List<ModEntry.Beam>();
  private readonly List<ModEntry.Impact> ActiveImpacts = new List<ModEntry.Impact>();

  public override void Entry(IModHelper helper)
  {
    this.Config = helper.ReadConfig<ModConfig>();
    this.DeathicornLightKey = this.ModManifest.UniqueID + "/DeathicornGlow";
    this.DeathicornLightNumericId = ModEntry.StableHash(this.DeathicornLightKey);
    helper.Events.Content.AssetRequested += new EventHandler<AssetRequestedEventArgs>(this.OnAssetRequested);
    helper.Events.Content.AssetsInvalidated += new EventHandler<AssetsInvalidatedEventArgs>(this.OnAssetsInvalidated);
    helper.Events.GameLoop.UpdateTicked += new EventHandler<UpdateTickedEventArgs>(this.OnUpdateTicked);
    helper.Events.GameLoop.GameLaunched += new EventHandler<GameLaunchedEventArgs>(this.OnGameLaunched);
    helper.Events.Display.RenderedWorld += new EventHandler<RenderedWorldEventArgs>(this.OnRenderedWorld);
    helper.Events.Display.RenderingWorld += new EventHandler<RenderingWorldEventArgs>(this.OnRenderingWorld);
    helper.Events.Player.Warped += new EventHandler<WarpedEventArgs>(this.OnWarped);
  }

  private void OnWarped(object? sender, WarpedEventArgs e) => this.RemoveDeathicornLight();

  private string GetVirtualAssetKey(string name) => $"Mods/{this.ModManifest.UniqueID}/{name}";

  private void EnsureFxAssetsLoaded()
  {
    if (this.BeamAtlas == null)
      this.BeamAtlas = this.Helper.GameContent.Load<Texture2D>(this.GetVirtualAssetKey("LaserBeamAtlas"));
    if (this.ImpactAtlas == null)
      this.ImpactAtlas = this.Helper.GameContent.Load<Texture2D>(this.GetVirtualAssetKey("LaserImpactAtlas"));
    if (this.BoltTexture != null)
      return;
    this.BoltTexture = this.Helper.GameContent.Load<Texture2D>(this.GetVirtualAssetKey("LaserBolt"));
  }

  private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
  {
    IGenericModConfigMenuApi api = this.Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
    if (api == null)
      return;
    api.Register(this.ModManifest, (Action) (() => this.Config = new ModConfig()), (Action) (() => this.Helper.WriteConfig<ModConfig>(this.Config)));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this.Config.EnableHornLasers), (Action<bool>) (value => this.Config.EnableHornLasers = value), (Func<string>) (() => "Horn lasers"), (Func<string>) (() => "Enable/disable the Deathicorn's horn laser auto-attack."));
    api.AddNumberOption(this.ModManifest, (Func<float>) (() => this.Config.LaserIntervalSeconds), (Action<float>) (value => this.Config.LaserIntervalSeconds = Math.Max(0.1f, value)), (Func<string>) (() => "Laser interval (seconds)"), (Func<string>) (() => "Seconds between horn laser shots."), new float?(0.1f), new float?(10f), new float?(0.1f));
    api.AddNumberOption(this.ModManifest, (Func<int>) (() => this.Config.RangeTiles), (Action<int>) (value => this.Config.RangeTiles = Math.Clamp(value, 1, 40)), (Func<string>) (() => "Laser range (tiles)"), (Func<string>) (() => "How far the horn lasers can target enemies."), new int?(1), new int?(40), new int?(1));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this.Config.EnableDeathicornGlow), (Action<bool>) (value => this.Config.EnableDeathicornGlow = value), (Func<string>) (() => "Deathicorn glow"), (Func<string>) (() => "Emit light like a glow ring (mounted or nearby when dismounted)."));
    string[] names = Enum.GetNames(typeof (LaserColor));
    api.AddTextOption(this.ModManifest, (Func<string>) (() => this.Config.BoltCoreColor.ToString()), (Action<string>) (value => this.Config.BoltCoreColor = ModEntry.ParseLaserColor(value)), (Func<string>) (() => "Bolt core color"), (Func<string>) (() => "Color of the traveling bolt core at the tip of the beam."), names, (Func<string, string>) (v => ModEntry.FormatLaserColorChoiceForGmcm(v)));
    api.AddTextOption(this.ModManifest, (Func<string>) (() => this.Config.BoltGlowColor.ToString()), (Action<string>) (value => this.Config.BoltGlowColor = ModEntry.ParseLaserColor(value)), (Func<string>) (() => "Bolt glow color"), (Func<string>) (() => "Color of the glow around the traveling bolt core."), names, (Func<string, string>) (v => ModEntry.FormatLaserColorChoiceForGmcm(v)));
    api.AddTextOption(this.ModManifest, (Func<string>) (() => this.Config.ImpactSplashColor.ToString()), (Action<string>) (value => this.Config.ImpactSplashColor = ModEntry.ParseLaserColor(value)), (Func<string>) (() => "Impact splash color"), (Func<string>) (() => "Color of the splash/flash when the bolt hits an enemy."), names, (Func<string, string>) (v => ModEntry.FormatLaserColorChoiceForGmcm(v)));
    api.AddTextOption(this.ModManifest, (Func<string>) (() => this.Config.GlowColor.ToString()), (Action<string>) (value => this.Config.GlowColor = ModEntry.ParseLaserColor(value)), (Func<string>) (() => "Glow color"), (Func<string>) (() => "Color of the Deathicorn's light (Prismatic cycles)."), names, (Func<string, string>) (v => ModEntry.FormatLaserColorChoiceForGmcm(v)));
    api.AddNumberOption(this.ModManifest, (Func<float>) (() => this.Config.GlowRadius), (Action<float>) (value => this.Config.GlowRadius = MathHelper.Clamp(value, 0.5f, 15f)), (Func<string>) (() => "Glow radius"), (Func<string>) (() => "Light radius in tiles. (Glow Ring is ~10; Small Glow Ring is ~5.)"), new float?(0.5f), new float?(15f), new float?(0.5f));
  }

  private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
  {
    if (e.NameWithoutLocale.IsEquivalentTo(this.GetVirtualAssetKey("LaserBeamAtlas"), false))
    {
      e.LoadFromModFile<Texture2D>(BeamAtlasAsset, AssetLoadPriority.Medium);
      return;
    }

    if (e.NameWithoutLocale.IsEquivalentTo(this.GetVirtualAssetKey("LaserImpactAtlas"), false))
    {
      e.LoadFromModFile<Texture2D>(ImpactAtlasAsset, AssetLoadPriority.Medium);
      return;
    }

    if (e.NameWithoutLocale.IsEquivalentTo(this.GetVirtualAssetKey("LaserBolt"), false))
    {
      e.LoadFromModFile<Texture2D>(BoltAsset, AssetLoadPriority.Medium);
      return;
    }
  }

  private void OnAssetsInvalidated(object? sender, AssetsInvalidatedEventArgs e)
  {
    foreach (IAssetName name in e.NamesWithoutLocale)
    {
      if (name.IsEquivalentTo(this.GetVirtualAssetKey("LaserBeamAtlas"), false))
        this.BeamAtlas = null;
      else if (name.IsEquivalentTo(this.GetVirtualAssetKey("LaserImpactAtlas"), false))
        this.ImpactAtlas = null;
      else if (name.IsEquivalentTo(this.GetVirtualAssetKey("LaserBolt"), false))
        this.BoltTexture = null;
    }
  }

  private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
  {
    if (!Context.IsWorldReady)
      return;
    this.UpdateDeathicornGlow();
    if (!this.Config.EnableHornLasers)
      return;
    int num = Math.Max(1, (int) Math.Round((double) this.Config.LaserIntervalSeconds * 60.0));
    if (!e.IsMultipleOf((uint) num))
      return;
    GameLocation currentLocation = ((Character) Game1.player).currentLocation;
    if (currentLocation == null)
      return;
    Horse trackedHorse = this.GetTrackedHorse();
    if (trackedHorse == null)
      return;
    float rangePx = Math.Max(1f, (float) this.Config.RangeTiles) * 64f;
    Rectangle boundingBox = ((Character) trackedHorse).GetBoundingBox();
    Vector2 horseCenterPx = new Vector2((float) boundingBox.Center.X, (float) boundingBox.Center.Y);
    Monster monster = ((IEnumerable) currentLocation.characters).OfType<Monster>().Where<Monster>((Func<Monster, bool>) (m => !((NPC) m).IsInvisible && m.Health > 0)).Select(m => new
    {
      Monster = m,
      Dist = Vector2.Distance(horseCenterPx, ((Character) m).Position)
    }).Where(x => (double) x.Dist <= (double) rangePx).OrderBy(x => x.Dist).Select(x => x.Monster).FirstOrDefault<Monster>();
    if (monster == null)
      return;
    Vector2 hornWorldPixel = this.GetHornWorldPixel(trackedHorse);
    Vector2 vector2 = ((Character) monster).Position + new Vector2(32f, 32f);
    this.ActiveBeams.Add(new ModEntry.Beam(hornWorldPixel, vector2, 12, 96f));
    int delayTicks = Math.Max(0, 10);
    this.ActiveImpacts.Add(new ModEntry.Impact(vector2, 12, delayTicks));
    monster.takeDamage(99999, 0, 0, false, 0.0, Game1.player);
    try
    {
      currentLocation.playSound("magic_arrow", new Vector2?(), new int?(), (SoundContext) 0);
    }
    catch
    {
    }
  }

  private string GetEffectiveLightId(LightSource? light)
  {
    if (light == null)
      return this.DeathicornLightKey;
    try
    {
      if (light.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) light) is string effectiveLightId && !string.IsNullOrWhiteSpace(effectiveLightId))
        return effectiveLightId;
    }
    catch
    {
    }
    return this.DeathicornLightKey;
  }

  private void EnsureLightIdAndScope(LightSource light, GameLocation? scopeLocation = null)
  {
    try
    {
      Type type = light.GetType();
      PropertyInfo property = type.GetProperty("Id", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (property != null)
      {
        string str = (string) null;
        try
        {
          str = property.GetValue((object) light) as string;
        }
        catch
        {
        }
        if (string.IsNullOrWhiteSpace(str) || str != this.DeathicornLightKey)
        {
          if (property.CanWrite)
          {
            property.SetValue((object) light, (object) this.DeathicornLightKey);
          }
          else
          {
            FieldInfo field = type.GetField("<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof (string))
              field.SetValue((object) light, (object) this.DeathicornLightKey);
          }
        }
      }
    }
    catch
    {
    }
    try
    {
      FieldInfo field = light.GetType().GetField("onlyLocation", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if (field == null)
        return;
      object obj = (object) null;
      Type fieldType = field.FieldType;
      GameLocation gameLocation = scopeLocation ?? ((Character) Game1.player)?.currentLocation ?? Game1.currentLocation;
      if (gameLocation == null)
        return;
      if (fieldType == typeof (GameLocation) || fieldType.IsAssignableFrom(typeof (GameLocation)))
        obj = (object) gameLocation;
      else if (fieldType == typeof (string))
        obj = (object) gameLocation.NameOrUniqueName;
      else if (fieldType.IsGenericType && fieldType.GetGenericTypeDefinition() == typeof (WeakReference<>))
      {
        Type genericArgument = fieldType.GetGenericArguments()[0];
        if (genericArgument == typeof (GameLocation) || genericArgument.IsAssignableFrom(typeof (GameLocation)))
          obj = Activator.CreateInstance(fieldType, (object) gameLocation);
      }
      if (obj != null)
        field.SetValue((object) light, obj);
    }
    catch
    {
    }
  }

  private void UpdateDeathicornGlow()
  {
    if (!this.Config.EnableDeathicornGlow || Game1.currentLocation == null || Game1.player == null)
    {
      this.RemoveDeathicornLight();
    }
    else
    {
      Horse trackedHorse = this.GetTrackedHorse();
      if (trackedHorse == null)
      {
        this.RemoveDeathicornLight();
      }
      else
      {
        GameLocation gameLocation = ((Character) Game1.player).currentLocation ?? Game1.currentLocation;
        if (gameLocation == null)
        {
          this.RemoveDeathicornLight();
        }
        else
        {
          Rectangle boundingBox = ((Character) trackedHorse).GetBoundingBox();
          Vector2 vector2 = new Vector2((float) boundingBox.Center.X, (float) boundingBox.Center.Y);
          Vector2 position = vector2;
          Color desired = ModEntry.ResolveConfiguredColor(this.Config.GlowColor, (float) ((double) Game1.ticks * 0.008500000461935997 % 1.0), 0.75f, 1f);
          if (this.Config.GlowColor == LaserColor.Pink)
          {
            desired = new Color((int) byte.MaxValue, 105, 180);
          }
          Color lightSourceTint = ModEntry.ToLightSourceTint(desired);
          LaserColor? lastLoggedGlowColor = this.LastLoggedGlowColor;
          LaserColor glowColor = this.Config.GlowColor;
          if (!(lastLoggedGlowColor.GetValueOrDefault() == glowColor & lastLoggedGlowColor.HasValue))
          {
            this.Monitor.Log($"Deathicorn glow: option={this.Config.GlowColor} desired={$"#{desired.R:X2}{desired.G:X2}{desired.B:X2}"} light={$"#{lightSourceTint.R:X2}{lightSourceTint.G:X2}{lightSourceTint.B:X2}"}.", (LogLevel) 1);
            this.LastLoggedGlowColor = new LaserColor?(this.Config.GlowColor);
          }
          float radius = MathHelper.Clamp(this.Config.GlowRadius, 0.5f, 15f);
          if ((double) this.Config.GlowRadius <= 3.5)
            radius = MathHelper.Clamp(radius * 4f, 0.5f, 15f);
          if (this.Config.GlowColor == LaserColor.Blue)
            radius = MathHelper.Clamp(radius * 1.35f, 0.5f, 15f);
          else if (this.Config.GlowColor == LaserColor.Pink)
            radius = MathHelper.Clamp(radius * 1.2f, 0.5f, 15f);
          if (this.DeathicornLight == null)
          {
            try
            {
              this.DeathicornLight = this.CreateLightSource(position, radius, lightSourceTint, gameLocation);
              this.EnsureLightIdAndScope(this.DeathicornLight, gameLocation);
            }
            catch (Exception ex)
            {
              if (!this.LoggedLightSourceCtorFailure)
              {
                this.Monitor.Log("Deathicorn glow: failed to create LightSource; disabling glow for this session.\n" + ex?.ToString(), (LogLevel) 4);
                this.LoggedLightSourceCtorFailure = true;
              }
              this.Config.EnableDeathicornGlow = false;
              this.RemoveDeathicornLight();
              return;
            }
          }
          this.EnsureLightIdAndScope(this.DeathicornLight, gameLocation);
          ((NetFieldBase<Vector2, NetVector2>) this.DeathicornLight.position).Value = position;
          ((NetFieldBase<float, NetFloat>) this.DeathicornLight.radius).Value = radius;
          ((NetFieldBase<Color, NetColor>) this.DeathicornLight.color).Value = lightSourceTint;
          this.RegisterDeathicornLight(gameLocation, position);
        }
      }
    }
  }

  private void RemoveDeathicornLight()
  {
    GameLocation loc = ((Character) Game1.player)?.currentLocation ?? Game1.currentLocation;
    string str = this.DeathicornLight != null ? this.GetEffectiveLightId(this.DeathicornLight) : this.DeathicornLightKey;
    if (!string.IsNullOrWhiteSpace(str))
    {
      Game1.currentLightSources.Remove(str);
      this.TryRemoveLocationLight(loc, str);
    }
    if (!string.IsNullOrWhiteSpace(this.DeathicornLightKey) && this.DeathicornLightKey != str)
    {
      Game1.currentLightSources.Remove(this.DeathicornLightKey);
      this.TryRemoveLocationLight(loc, this.DeathicornLightKey);
    }
    if (this.DeathicornLightNumericId != 0)
    {
      try
      {
        Game1.currentLightSources.Remove(this.DeathicornLightNumericId.ToString());
      }
      catch
      {
      }
      try
      {
        Game1.currentLightSources.Remove(((long) this.DeathicornLightNumericId).ToString());
      }
      catch
      {
      }
    }
    this.DeathicornLight = (LightSource) null;
  }

  private Horse? GetTrackedHorse()
  {
    if (!Context.IsWorldReady || Game1.player == null || Game1.currentLocation == null)
      return (Horse) null;
    Horse mount = Game1.player.mount;
    if (mount != null)
      return mount;
    foreach (NPC character in Game1.currentLocation.characters)
    {
      if (character is Horse trackedHorse)
        return trackedHorse;
    }
    return (Horse) null;
  }

  private static int StableHash(string value)
  {
    int num = -2128831035;
    for (int index = 0; index < value.Length; ++index)
      num = (num ^ (int) value[index]) * 16777619;
    return num;
  }

  private int GetGlowLightTextureIndex()
  {
    if (!this.GlowLightTextureIndex.HasValue)
    {
      int num = ModEntry.TryGetLightSourceIntField("lantern", "Lantern") ?? 1;
      this.GlowLightTextureIndex = new int?(num);
      if (!this.LoggedGlowTextureIndex)
      {
        this.Monitor.Log($"Deathicorn glow: using LightSource texture Lantern = {num}.", (LogLevel) 1);
        this.LoggedGlowTextureIndex = true;
      }
    }
    return this.GlowLightTextureIndex.Value;
  }

  private static int? TryGetLightSourceIntField(params string[] fieldNames)
  {
    foreach (string fieldName in fieldNames)
    {
      FieldInfo field = typeof (LightSource).GetField(fieldName, BindingFlags.Static | BindingFlags.Public);
      if (field?.FieldType == typeof (int))
        return new int?((int) field.GetValue((object) null));
    }
    return new int?();
  }

  private LightSource CreateLightSource(
    Vector2 position,
    float radius,
    Color color,
    GameLocation scopeLocation)
  {
    foreach (ConstructorInfo constructorInfo in (IEnumerable<ConstructorInfo>) ((IEnumerable<ConstructorInfo>) typeof (LightSource).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).OrderByDescending<ConstructorInfo, int>((Func<ConstructorInfo, int>) (p => ((MethodBase) p).GetParameters().Length)))
    {
      ParameterInfo[] parameters = ((MethodBase) constructorInfo).GetParameters();
      int[] array = ((IEnumerable<ParameterInfo>) parameters).Select((p, idx) => new
      {
        p = p,
        idx = idx
      }).Where(t => t.p.ParameterType == typeof (string)).Select(t => t.idx).ToArray<int>();
      object[] objArray1 = new object[parameters.Length];
      int num1 = 0;
      int num2 = 0;
      bool flag = true;
      for (int index1 = 0; index1 < parameters.Length; ++index1)
      {
        Type parameterType = parameters[index1].ParameterType;
        Type underlyingType = Nullable.GetUnderlyingType(parameterType);
        Type type1 = underlyingType;
        if ((object) type1 == null)
          type1 = parameterType;
        Type type2 = type1;
        if (type2 == typeof (Vector2))
          objArray1[index1] = (object) position;
        else if (type2 == typeof (Color))
          objArray1[index1] = (object) color;
        else if (type2 == typeof (string))
          objArray1[index1] = array.Length <= 1 || index1 != array[array.Length - 1] || index1 == array[0] ? (object) this.DeathicornLightKey : (object) scopeLocation?.NameOrUniqueName;
        else if (type2 == typeof (float))
        {
          objArray1[index1] = (object) (float) (num2 == 0 ? (double) radius : 1.0);
          ++num2;
        }
        else if (type2 == typeof (double))
        {
          objArray1[index1] = (object) (num2 == 0 ? (double) radius : 1.0);
          ++num2;
        }
        else if (type2 == typeof (int))
        {
          objArray1[index1] = (object) (num1 == 0 ? this.GetGlowLightTextureIndex() : this.DeathicornLightNumericId);
          ++num1;
        }
        else if (type2 == typeof (long))
        {
          object[] objArray2 = objArray1;
          int index2 = index1;
          Farmer player = Game1.player;
          long uniqueMultiplayerId = player != null ? player.UniqueMultiplayerID : 0L;
          objArray2[index2] = (object) uniqueMultiplayerId;
        }
        else if (type2 == typeof (bool))
          objArray1[index1] = (object) true;
        else if (type2.IsEnum)
        {
          object obj = Enum.ToObject(type2, 0);
          try
          {
            string[] names = Enum.GetNames(type2);
            int index3 = Array.FindIndex<string>(names, (Predicate<string>) (n => string.Equals(n, "None", StringComparison.OrdinalIgnoreCase)));
            if (index3 >= 0)
              obj = Enum.Parse(type2, names[index3]);
          }
          catch
          {
          }
          objArray1[index1] = obj;
        }
        else if (type2.IsValueType)
        {
          objArray1[index1] = Activator.CreateInstance(type2);
        }
        else
        {
          flag = false;
          break;
        }
        if ((object) underlyingType != null && objArray1[index1] == null)
          objArray1[index1] = (object) null;
      }
      if (flag)
      {
        try
        {
          if (constructorInfo.Invoke(objArray1) is LightSource lightSource)
          {
            if (!this.LoggedLightSourceCtor)
            {
              this.Monitor.Log($"Deathicorn glow: using LightSource ctor ({string.Join(", ", ((IEnumerable<ParameterInfo>) parameters).Select<ParameterInfo, string>((Func<ParameterInfo, string>) (p => p.ParameterType.Name)))}).", (LogLevel) 0);
              this.LoggedLightSourceCtor = true;
            }
            return lightSource;
          }
        }
        catch
        {
        }
      }
    }
    throw new InvalidOperationException("Couldn't construct LightSource (no compatible constructor found).");
  }

  private void OnRenderingWorld(object? sender, RenderingWorldEventArgs e)
  {
    if (!Context.IsWorldReady || !this.Config.EnableDeathicornGlow || this.DeathicornLight == null)
      return;
    GameLocation loc = ((Character) Game1.player)?.currentLocation ?? Game1.currentLocation;
    if (loc == null)
      return;
    this.RegisterDeathicornLight(loc, ((NetFieldBase<Vector2, NetVector2>) this.DeathicornLight.position).Value);
  }

  private void RegisterDeathicornLight(GameLocation loc, Vector2 position)
  {
    if (this.DeathicornLight == null)
      return;
    string str1 = this.GetEffectiveLightId(this.DeathicornLight);
    if (string.IsNullOrWhiteSpace(str1))
      str1 = this.DeathicornLightKey;
    if (!string.IsNullOrWhiteSpace(str1))
      Game1.currentLightSources[str1] = this.DeathicornLight;
    this.TrySetLocationLight(loc, str1, this.DeathicornLight);
    try
    {
      string str2 = loc?.NameOrUniqueName ?? "<null>";
      if (this.LastGlowLocationName != str2)
      {
        this.LastGlowLocationName = str2;
        Vector2 vector2 = ((NetFieldBase<Vector2, NetVector2>) this.DeathicornLight.position).Value;
        this.Monitor.Log($"Deathicorn glow registered: id={str1}, loc={str2}, pos=({vector2.X:0},{vector2.Y:0}), radius={((NetFieldBase<float, NetFloat>) this.DeathicornLight.radius).Value:0.00}, color={((NetFieldBase<Color, NetColor>) this.DeathicornLight.color).Value}.", (LogLevel) 0);
      }
    }
    catch
    {
    }
    if (this.TryRepositionLocationLight(loc, str1, position))
      return;
    try
    {
      Utility.repositionLightSource(str1, position);
    }
    catch
    {
    }
  }

  private bool TrySetLocationLight(GameLocation loc, string id, LightSource light)
  {
    try
    {
      MethodInfo methodInfo = ((IEnumerable<MethodInfo>) loc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).FirstOrDefault<MethodInfo>((Func<MethodInfo, bool>) (m => (((MemberInfo) m).Name.Equals("addLightSource", StringComparison.OrdinalIgnoreCase) || ((MemberInfo) m).Name.Equals("AddLightSource", StringComparison.OrdinalIgnoreCase)) && ((MethodBase) m).GetParameters().Length == 1 && typeof (LightSource).IsAssignableFrom(((MethodBase) m).GetParameters()[0].ParameterType)));
      if (methodInfo != null)
      {
        ((MethodBase) methodInfo).Invoke((object) loc, new object[1]
        {
          (object) light
        });
        return true;
      }
      object obj = loc.GetType().GetProperty("sharedLights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) loc) ?? loc.GetType().GetField("sharedLights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) loc);
      if (obj == null || !(obj is IDictionary dictionary))
        return false;
      object key = this.ChooseSharedLightsKey(dictionary.GetType(), id);
      dictionary[key] = (object) light;
      return true;
    }
    catch
    {
    }
    return false;
  }

  private void TryRemoveLocationLight(GameLocation? loc, string id)
  {
    if (loc == null)
      return;
    try
    {
      foreach (MethodInfo methodInfo in ((IEnumerable<MethodInfo>) loc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).Where<MethodInfo>((Func<MethodInfo, bool>) (m => (((MemberInfo) m).Name.Equals("removeLightSource", StringComparison.OrdinalIgnoreCase) || ((MemberInfo) m).Name.Equals("RemoveLightSource", StringComparison.OrdinalIgnoreCase)) && ((MethodBase) m).GetParameters().Length == 1)))
      {
        Type parameterType = ((MethodBase) methodInfo).GetParameters()[0].ParameterType;
        if (parameterType == typeof (string))
          ((MethodBase) methodInfo).Invoke((object) loc, new object[1]
          {
            (object) id
          });
        else if (parameterType == typeof (int))
          ((MethodBase) methodInfo).Invoke((object) loc, new object[1]
          {
            (object) this.DeathicornLightNumericId
          });
        else if (parameterType == typeof (long))
          ((MethodBase) methodInfo).Invoke((object) loc, new object[1]
          {
            (object) (long) this.DeathicornLightNumericId
          });
      }
      if (!((loc.GetType().GetProperty("sharedLights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) loc) ?? loc.GetType().GetField("sharedLights", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) loc)) is IDictionary dictionary))
        return;
      try
      {
        dictionary.Remove((object) id);
      }
      catch
      {
      }
      try
      {
        dictionary.Remove((object) this.DeathicornLightNumericId);
      }
      catch
      {
      }
      try
      {
        dictionary.Remove((object) (long) this.DeathicornLightNumericId);
      }
      catch
      {
      }
    }
    catch
    {
    }
  }

  private bool TryRepositionLocationLight(GameLocation loc, string id, Vector2 position)
  {
    try
    {
      foreach (MethodInfo methodInfo in ((IEnumerable<MethodInfo>) loc.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).Where<MethodInfo>((Func<MethodInfo, bool>) (m => (((MemberInfo) m).Name.Equals("repositionLightSource", StringComparison.OrdinalIgnoreCase) || ((MemberInfo) m).Name.Equals("RepositionLightSource", StringComparison.OrdinalIgnoreCase)) && ((MethodBase) m).GetParameters().Length == 2)))
      {
        ParameterInfo[] parameters = ((MethodBase) methodInfo).GetParameters();
        if (!(parameters[1].ParameterType != typeof (Vector2)))
        {
          if (parameters[0].ParameterType == typeof (string))
          {
            ((MethodBase) methodInfo).Invoke((object) loc, new object[2]
            {
              (object) id,
              (object) position
            });
            return true;
          }
          if (parameters[0].ParameterType == typeof (int))
          {
            ((MethodBase) methodInfo).Invoke((object) loc, new object[2]
            {
              (object) this.DeathicornLightNumericId,
              (object) position
            });
            return true;
          }
          if (parameters[0].ParameterType == typeof (long))
          {
            ((MethodBase) methodInfo).Invoke((object) loc, new object[2]
            {
              (object) (long) this.DeathicornLightNumericId,
              (object) position
            });
            return true;
          }
        }
      }
    }
    catch
    {
    }
    return false;
  }

  private object ChooseSharedLightsKey(Type dictType, string id)
  {
    try
    {
      Type genericArgument = ((IEnumerable<Type>) dictType.GetInterfaces()).FirstOrDefault<Type>((Func<Type, bool>) (i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (IDictionary<,>)))?.GetGenericArguments()[0];
      if (genericArgument == typeof (int))
        return (object) this.DeathicornLightNumericId;
      if (genericArgument == typeof (long))
        return (object) (long) this.DeathicornLightNumericId;
    }
    catch
    {
    }
    return (object) id;
  }

  private void OnRenderedWorld(object? sender, RenderedWorldEventArgs e)
  {
    if (!Context.IsWorldReady || this.ActiveBeams.Count == 0 && this.ActiveImpacts.Count == 0)
      return;

    this.EnsureFxAssetsLoaded();
    this.UpdateActiveEffects();
    this.DrawActiveBeams(e.SpriteBatch);
    this.DrawActiveImpacts(e.SpriteBatch);
  }

  private void UpdateActiveEffects()
  {
    for (int index = this.ActiveBeams.Count - 1; index >= 0; --index)
    {
      ModEntry.Beam beam = this.ActiveBeams[index];
      --beam.TicksLeft;
      if (beam.TicksLeft <= 0)
        this.ActiveBeams.RemoveAt(index);
    }

    for (int index = this.ActiveImpacts.Count - 1; index >= 0; --index)
    {
      ModEntry.Impact impact = this.ActiveImpacts[index];
      if (impact.DelayTicks > 0)
      {
        --impact.DelayTicks;
      }
      else
      {
        --impact.TicksLeft;
        if (impact.TicksLeft <= 0)
          this.ActiveImpacts.RemoveAt(index);
      }
    }
  }

  private void DrawActiveBeams(SpriteBatch spriteBatch)
  {
    foreach (ModEntry.Beam beam in this.ActiveBeams)
    {
      float progress = beam.TotalTicks > 0
        ? Math.Clamp(1f - beam.TicksLeft / (float) beam.TotalTicks, 0f, 1f)
        : 1f;

      Vector2 direction = beam.End - beam.Start;
      float beamLength = direction.Length();
      if (beamLength <= 1f)
        continue;

      direction /= beamLength;
      Vector2 beamTip = beam.Start + direction * (beamLength * progress);
      float segmentLength = Math.Min(beam.SegmentLength, beamLength);
      Vector2 segmentStart = beamTip - direction * segmentLength;
      if (Vector2.Dot(segmentStart - beam.Start, direction) < 0f)
        segmentStart = beam.Start;

      Vector2 localStart = Game1.GlobalToLocal(Game1.viewport, segmentStart);
      Vector2 localTip = Game1.GlobalToLocal(Game1.viewport, beamTip);
      Vector2 localDelta = localTip - localStart;
      float localLength = localDelta.Length();
      if (localLength <= 1f)
        continue;

      float rotation = (float)Math.Atan2(localDelta.Y, localDelta.X);
      Vector2 wobble = Vector2.Normalize(new Vector2(-localDelta.Y, localDelta.X)) * ((float)Math.Sin((Game1.ticks + beam.Seed) * 0.55f) * 1.35f);
      float pulse = 0.65f + 0.35f * (float)Math.Sin((Game1.ticks + beam.Seed) * 0.18f);
      float hue = (Game1.ticks * 0.0105f + (beam.Seed & 1023) / 1023f) % 1f;
      int frameIndex = (Game1.ticks / 2 + (beam.Seed & 7)) % 8;

      Color outer = ModEntry.HsvToColor(hue, 0.85f, 1f);
      Color mid = ModEntry.HsvToColor((hue + 0.03f) % 1f, 1f, 1f);
      Color inner = ModEntry.HsvToColor((hue + 0.08f) % 1f, 0.7f, 1f);
      Color core = ModEntry.HsvToColor((hue + 0.02f) % 1f, 0.2f, 1f);

      this.DrawBeamLayer(spriteBatch, localStart + wobble * 1.1f, rotation, localLength, 14f, outer * (0.10f * pulse), frameIndex);
      this.DrawBeamLayer(spriteBatch, localStart + wobble, rotation, localLength, 9f, mid * (0.18f * pulse), frameIndex);
      this.DrawBeamLayer(spriteBatch, localStart + wobble * 0.6f, rotation, localLength, 5.5f, inner * (0.62f * pulse), frameIndex);
      this.DrawBeamLayer(spriteBatch, localStart, rotation, localLength, 2.4f, core * 0.95f, frameIndex);

      this.DrawBolt(spriteBatch, localTip, hue, pulse);
    }
  }

  private void DrawBolt(SpriteBatch spriteBatch, Vector2 position, float hue, float pulse)
  {
    float coreSize = 7f + 2.5f * (0.55f + 0.45f * (float)Math.Sin(Game1.ticks * 0.35f));
    float glowSize = coreSize * 2.1f;
    Color glowColor = ModEntry.ResolveConfiguredColor(this.Config.BoltGlowColor, (hue + 0.08f) % 1f, 0.55f, 1f);
    Color coreColor = ModEntry.ResolveConfiguredColor(this.Config.BoltCoreColor, (hue + 0.12f) % 1f, 0.35f, 1f);

    if (this.BoltTexture != null)
    {
      Vector2 origin = new Vector2(this.BoltTexture.Width / 2f, this.BoltTexture.Height / 2f);
      spriteBatch.Draw(this.BoltTexture, position, null, glowColor * (0.22f * pulse), 0f, origin, glowSize / this.BoltTexture.Width, SpriteEffects.None, 0.999f);
      spriteBatch.Draw(this.BoltTexture, position, null, coreColor * (0.95f * pulse), 0f, origin, coreSize / this.BoltTexture.Width, SpriteEffects.None, 1f);
    }
    else
    {
      spriteBatch.Draw(Game1.staminaRect, position - new Vector2(glowSize / 2f, glowSize / 2f), new Rectangle(0, 0, 1, 1), glowColor * (0.22f * pulse), 0f, Vector2.Zero, new Vector2(glowSize, glowSize), SpriteEffects.None, 0.999f);
      spriteBatch.Draw(Game1.staminaRect, position - new Vector2(coreSize / 2f, coreSize / 2f), new Rectangle(0, 0, 1, 1), coreColor * (0.95f * pulse), 0f, Vector2.Zero, new Vector2(coreSize, coreSize), SpriteEffects.None, 1f);
    }
  }

  private void DrawActiveImpacts(SpriteBatch spriteBatch)
  {
    foreach (ModEntry.Impact impact in this.ActiveImpacts)
    {
      if (impact.DelayTicks > 0)
        continue;

      Vector2 localPosition = Game1.GlobalToLocal(Game1.viewport, impact.Position);
      float life = impact.TicksLeft / (float)impact.MaxTicks;
      float size = MathHelper.Lerp(34f, 10f, 1f - life);
      float alpha = MathHelper.Clamp(life * 1.15f, 0f, 1f);
      float hue = (Game1.ticks * 0.0105f + (impact.Seed & 1023) / 1023f) % 1f;
      int frameIndex = (Game1.ticks / 2 + (impact.Seed & 7)) % 8;
      Color configured = ModEntry.ResolveConfiguredColor(this.Config.ImpactSplashColor, hue, 1f, 1f);
      Color bright = ModEntry.Brighten(configured, 1.15f);
      Color dim = ModEntry.Brighten(configured, 0.75f);

      if (this.ImpactAtlas != null)
      {
        int frameCount = Math.Max(1, this.ImpactAtlas.Height / 32);
        int atlasFrame = (int)Math.Floor((1f - impact.TicksLeft / (float)impact.MaxTicks) * frameCount);
        atlasFrame = Math.Clamp(atlasFrame, 0, frameCount - 1);
        Rectangle source = new Rectangle(0, atlasFrame * 32, 32, 32);
        Vector2 origin = new Vector2(16f, 16f);

        spriteBatch.Draw(this.ImpactAtlas, localPosition, source, configured * (alpha * 0.55f), 0f, origin, size / 32f * 1.15f, SpriteEffects.None, 1f);
        spriteBatch.Draw(this.ImpactAtlas, localPosition, source, bright * (alpha * 0.95f), 0f, origin, size / 32f * 0.85f, SpriteEffects.None, 1f);
        float shimmer = 0.6f + 0.4f * (float)Math.Sin((Game1.ticks + impact.Seed) * 0.22f);
        spriteBatch.Draw(this.ImpactAtlas, localPosition, source, dim * (alpha * 0.18f * shimmer), 0f, origin, size / 32f * 1.55f, SpriteEffects.None, 1f);
      }
      else
      {
        spriteBatch.Draw(Game1.staminaRect, localPosition - new Vector2(size / 2f, size / 2f), null, configured * (alpha * 0.25f), 0f, Vector2.Zero, new Vector2(size, size), SpriteEffects.None, 1f);
        float innerSize = size * 0.55f;
        spriteBatch.Draw(Game1.staminaRect, localPosition - new Vector2(innerSize / 2f, innerSize / 2f), null, bright * (alpha * 0.7f), 0f, Vector2.Zero, new Vector2(innerSize, innerSize), SpriteEffects.None, 1f);
        float outerSize = size * 1.15f;
        float shimmer = 0.6f + 0.4f * (float)Math.Sin((Game1.ticks + impact.Seed) * 0.22f);
        spriteBatch.Draw(Game1.staminaRect, localPosition - new Vector2(outerSize / 2f, outerSize / 2f), null, dim * (alpha * 0.12f * shimmer), 0f, Vector2.Zero, new Vector2(outerSize, outerSize), SpriteEffects.None, 1f);
      }

      float crossLength = size * 0.8f;
      Color cross = ModEntry.Brighten(configured, 1.25f);
      this.DrawBeamLayer(spriteBatch, localPosition - new Vector2(crossLength / 2f, 0f), 0f, crossLength, 2f, cross * (alpha * 0.75f), frameIndex);
      this.DrawBeamLayer(spriteBatch, localPosition - new Vector2(0f, crossLength / 2f), 1.57079637f, crossLength, 2f, cross * (alpha * 0.75f), frameIndex);
    }
  }

  private static Color HsvToColor(float h, float s, float v)
  {
    h -= (float) Math.Floor((double) h);
    s = MathHelper.Clamp(s, 0.0f, 1f);
    v = MathHelper.Clamp(v, 0.0f, 1f);
    if ((double) s <= 9.9999997473787516E-05)
    {
      byte num = (byte) Math.Clamp((int) Math.Round((double) v * (double) byte.MaxValue), 0, (int) byte.MaxValue);
      return new Color((int) num, (int) num, (int) num);
    }
    float num1 = h * 6f;
    int num2 = (int) Math.Floor((double) num1);
    float num3 = num1 - (float) num2;
    float num4 = v * (1f - s);
    float num5 = v * (float) (1.0 - (double) s * (double) num3);
    float num6 = v * (float) (1.0 - (double) s * (1.0 - (double) num3));
    float num7;
    float num8;
    float num9;
    switch (num2 % 6)
    {
      case 0:
        num7 = v;
        num8 = num6;
        num9 = num4;
        break;
      case 1:
        num7 = num5;
        num8 = v;
        num9 = num4;
        break;
      case 2:
        num7 = num4;
        num8 = v;
        num9 = num6;
        break;
      case 3:
        num7 = num4;
        num8 = num5;
        num9 = v;
        break;
      case 4:
        num7 = num6;
        num8 = num4;
        num9 = v;
        break;
      default:
        num7 = v;
        num8 = num4;
        num9 = num5;
        break;
    }
    return new Color((int) (byte) Math.Clamp((int) Math.Round((double) num7 * (double) byte.MaxValue), 0, (int) byte.MaxValue), (int) (byte) Math.Clamp((int) Math.Round((double) num8 * (double) byte.MaxValue), 0, (int) byte.MaxValue), (int) (byte) Math.Clamp((int) Math.Round((double) num9 * (double) byte.MaxValue), 0, (int) byte.MaxValue));
  }

  private static string FormatLaserColorChoiceForGmcm(string value)
  {
    return ModEntry.ParseLaserColor(value).ToString();
  }

  private static LaserColor ParseLaserColor(string? value)
  {
    if (string.IsNullOrWhiteSpace(value))
      return LaserColor.Prismatic;
    string str = value.Trim();
    int length1 = str.IndexOf('(');
    if (length1 > 0)
      str = str.Substring(0, length1).Trim();
    int length2 = str.IndexOf(' ');
    if (length2 > 0)
      str = str.Substring(0, length2).Trim();
    LaserColor result;
    return Enum.TryParse<LaserColor>(str, true, out result) ? result : LaserColor.Prismatic;
  }

  private static Color ResolveConfiguredColor(
    LaserColor choice,
    float hue,
    float prismaticS,
    float prismaticV)
  {
    switch (choice)
    {
      case LaserColor.Prismatic:
        return ModEntry.HsvToColor(hue, prismaticS, prismaticV);
      case LaserColor.Red:
        return new Color((int) byte.MaxValue, 0, 0);
      case LaserColor.Green:
        return new Color(0, (int) byte.MaxValue, 0);
      case LaserColor.Blue:
        return new Color(0, 0, (int) byte.MaxValue);
      case LaserColor.Pink:
        return new Color((int) byte.MaxValue, 0, (int) byte.MaxValue);
      case LaserColor.Purple:
        return new Color(128 /*0x80*/, 0, (int) byte.MaxValue);
      case LaserColor.Yellow:
        return new Color((int) byte.MaxValue, (int) byte.MaxValue, 0);
      case LaserColor.Black:
        return new Color(0, 0, 0);
      default:
        return ModEntry.HsvToColor(hue, prismaticS, prismaticV);
    }
  }

  private static Color ToLightSourceTint(Color desired)
  {
    return new Color((byte) ((uint) byte.MaxValue - (uint) desired.R), (byte) ((uint) byte.MaxValue - (uint) desired.G), (byte) ((uint) byte.MaxValue - (uint) desired.B), desired.A);
  }

  private static Color Brighten(Color c, float factor)
  {
    int num1 = (int) Math.Round((double) c.R * (double) factor);
    int num2 = (int) Math.Round((double) c.G * (double) factor);
    int num3 = (int) Math.Round((double) c.B * (double) factor);
    return new Color((byte) Math.Clamp(num1, 0, (int) byte.MaxValue), (byte) Math.Clamp(num2, 0, (int) byte.MaxValue), (byte) Math.Clamp(num3, 0, (int) byte.MaxValue), c.A);
  }

  private void DrawBeamLayer(
    SpriteBatch sb,
    Vector2 start,
    float rotation,
    float length,
    float thickness,
    Color color,
    int frameIndex,
    float alphaScale = 1f)
  {
    this.EnsureFxAssetsLoaded();
    if (this.BeamAtlas == null)
      return;
    int num1 = Math.Max(1, this.BeamAtlas.Height / 16 /*0x10*/);
    int num2 = frameIndex % num1;
    if (num2 < 0)
      num2 += num1;
    Rectangle rectangle = new Rectangle(0, num2 * 16 /*0x10*/, 64 /*0x40*/, 16 /*0x10*/);
    Vector2 vector2_1 = new Vector2(0.0f, 8f);
    Vector2 vector2_2 = new Vector2(length / 64f, thickness / 16f);
    sb.Draw(this.BeamAtlas, start, new Rectangle?(rectangle), color * alphaScale, rotation, vector2_1, vector2_2, (SpriteEffects) 0, 1f);
  }

  private Vector2 GetHornWorldPixel(Horse horse)
  {
    Rectangle rectangle;
    try
    {
      AnimatedSprite sprite = ((Character) horse).Sprite;
      rectangle = sprite != null ? sprite.SourceRect : Rectangle.Empty;
    }
    catch
    {
      rectangle = Rectangle.Empty;
    }
    if (rectangle.Width <= 0 || rectangle.Height <= 0)
    {
      Rectangle boundingBox = ((Character) horse).GetBoundingBox();
      return new Vector2((float) boundingBox.Center.X, (float) boundingBox.Y + 16f);
    }
    Vector2? nullable = new Vector2?();
    int num = rectangle.X / 32 /*0x20*/;
    int index = rectangle.Y / 32 /*0x20*/ * 7 + num;
    if (index >= 0 && index < ModEntry.HornTipByFrame.Length && ModEntry.HornTipByFrame[index].HasValue)
      nullable = new Vector2?(ModEntry.HornTipByFrame[index].Value);
    if (!nullable.HasValue)
    {
      Rectangle boundingBox = ((Character) horse).GetBoundingBox();
      return new Vector2((float) boundingBox.Center.X, (float) boundingBox.Y + 16f);
    }
    bool flag = ((Character) horse).FacingDirection == 3;
    Vector2 vector2 = nullable.Value;
    if (flag)
      vector2.X = (float) (rectangle.Width - 1) - vector2.X;
    Vector2 hornWorldPixel = ((Character) horse).Position + vector2 * 4f;
    hornWorldPixel.Y -= 64f;
    return hornWorldPixel;
  }

  static ModEntry()
  {
    Vector2?[] nullableArray = new Vector2?[28];
    nullableArray[0] = new Vector2?(new Vector2(16f, 19f));
    nullableArray[1] = new Vector2?(new Vector2(16f, 20f));
    nullableArray[2] = new Vector2?(new Vector2(16f, 21f));
    nullableArray[3] = new Vector2?(new Vector2(16f, 20f));
    nullableArray[4] = new Vector2?(new Vector2(16f, 19f));
    nullableArray[5] = new Vector2?(new Vector2(16f, 18f));
    nullableArray[6] = new Vector2?(new Vector2(16f, 19f));
    nullableArray[7] = new Vector2?(new Vector2(29f, 8f));
    nullableArray[8] = new Vector2?(new Vector2(30f, 8f));
    nullableArray[9] = new Vector2?(new Vector2(30f, 9f));
    nullableArray[10] = new Vector2?(new Vector2(29f, 9f));
    nullableArray[11] = new Vector2?(new Vector2(29f, 8f));
    nullableArray[12] = new Vector2?(new Vector2(29f, 7f));
    nullableArray[13] = new Vector2?(new Vector2(30f, 7f));
    nullableArray[14] = new Vector2?(new Vector2(16f, 3f));
    nullableArray[15] = new Vector2?(new Vector2(16f, 4f));
    nullableArray[16 /*0x10*/] = new Vector2?(new Vector2(16f, 5f));
    nullableArray[17] = new Vector2?(new Vector2(16f, 4f));
    nullableArray[18] = new Vector2?(new Vector2(16f, 3f));
    nullableArray[19] = new Vector2?(new Vector2(16f, 2f));
    nullableArray[20] = new Vector2?(new Vector2(16f, 3f));
    nullableArray[21] = new Vector2?(new Vector2(29f, 10f));
    nullableArray[22] = new Vector2?(new Vector2(30f, 13f));
    nullableArray[23] = new Vector2?(new Vector2(30f, 16f));
    nullableArray[24] = new Vector2?(new Vector2(30f, 16f));
    nullableArray[25] = new Vector2?(new Vector2(16f, 4f));
    nullableArray[26] = new Vector2?(new Vector2(4f, 5f));
    ModEntry.HornTipByFrame = nullableArray;
  }

  private sealed class Beam
  {
    public Vector2 Start;
    public Vector2 End;
    public int TicksLeft;
    public int TotalTicks;
    public float SegmentLength;
    public int Seed;

    public Beam(Vector2 start, Vector2 end, int ticksLeft, float segmentLength)
    {
      this.Start = start;
      this.End = end;
      this.TicksLeft = ticksLeft;
      this.TotalTicks = ticksLeft;
      this.SegmentLength = segmentLength;
      Random random = Game1.random;
      this.Seed = random != null ? random.Next() : Environment.TickCount;
    }
  }

  private sealed class Impact
  {
    public Vector2 Position;
    public int DelayTicks;
    public int TicksLeft;
    public int MaxTicks;
    public int Seed;

    public Impact(Vector2 position, int ticksLeft, int delayTicks)
    {
      this.Position = position;
      this.DelayTicks = delayTicks;
      this.TicksLeft = ticksLeft;
      this.MaxTicks = Math.Max(1, ticksLeft);
      Random random = Game1.random;
      this.Seed = random != null ? random.Next() : Environment.TickCount;
    }
  }
}
