using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Netcode;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.GameData;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Objects;
using StardewValley.GameData.Shops;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using SObject = StardewValley.Object;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

#nullable disable
namespace ThaleTheGreat.StarBull;

internal sealed class ModEntry : Mod
{
  private const string EmbeddedRoot = "ThaleTheGreat.StarBull.Resources.";
  private const string ContentJsonPath = "content.json";
  private const string VendingMachineFurnitureId = "ThaleTheGreat.StarBull_VendingMachine";
  private const string VendingMachineMailFlag = "ThaleTheGreat.StarBull_VendingMachineReceived";
  private readonly Dictionary<string, string> _loadMap = new Dictionary<string, string>((IEqualityComparer<string>) StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, List<ModEntry.EditOp>> _editOpsByTarget = new Dictionary<string, List<ModEntry.EditOp>>((IEqualityComparer<string>) StringComparer.OrdinalIgnoreCase);
  private ModConfig _config = new ModConfig();
  private bool _enableLogging = false;
  private bool _easyMode = false;
  private List<string> _cachedAllDrinkQids;
  private Texture2D _cachedCustomBuffIcon;

  private string CustomBuffIconTexture => $"Mods/{this.ModManifest.UniqueID}/buff_icon";

  private string VendingMachineTexture => $"Mods_{this.ModManifest.UniqueID}_vending_machine";

  public override void Entry(IModHelper helper)
  {
    this._config = helper.ReadConfig<ModConfig>();
    this._enableLogging = this._config.EnableLogging;
    this._easyMode = this._config.EasyMode;
    helper.Events.GameLoop.GameLaunched += (EventHandler<GameLaunchedEventArgs>) ((_1, _2) => this.RegisterGmcm(helper));
    this.LoadEmbeddedContentPackJson();
    helper.Events.Content.AssetRequested += new EventHandler<AssetRequestedEventArgs>(this.OnAssetRequested);
    helper.Events.Content.AssetsInvalidated += new EventHandler<AssetsInvalidatedEventArgs>(this.OnAssetsInvalidated);
    helper.Events.GameLoop.DayStarted += new EventHandler<DayStartedEventArgs>(this.OnDayStarted);
    helper.Events.Input.ButtonPressed += new EventHandler<ButtonPressedEventArgs>(this.OnButtonPressed);
    helper.Events.Display.MenuChanged += new EventHandler<MenuChangedEventArgs>(this.OnMenuChanged);
  }

  private void OnAssetsInvalidated(object sender, AssetsInvalidatedEventArgs e)
  {
    foreach (IAssetName iassetName in (IEnumerable<IAssetName>) e.NamesWithoutLocale)
    {
      if (string.Equals(iassetName.BaseName, this.CustomBuffIconTexture, StringComparison.OrdinalIgnoreCase))
        this._cachedCustomBuffIcon = null;
    }
  }

  internal void Log(string message, LogLevel level = LogLevel.Info)
  {
    if (!this._enableLogging && level != LogLevel.Error)
      return;
    this.Monitor.Log(message, level);
  }

  private void RegisterGmcm(IModHelper helper)
  {
    IGenericModConfigMenuApi api = helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
    if (api == null)
      return;
    api.Register(this.ModManifest, (Action) (() =>
    {
      this._enableLogging = false;
      this._easyMode = false;
    }), (Action) (() =>
    {
      this._config.EnableLogging = this._enableLogging;
      this._config.EasyMode = this._easyMode;
      helper.WriteConfig<ModConfig>(this._config);
    }));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this._enableLogging), (Action<bool>) (value => this._enableLogging = value), (Func<string>) (() => "Debug Logging"), (Func<string>) (() => "Log routine asset, shop, and vending-machine diagnostics. Errors are always logged."));
    api.AddBoolOption(this.ModManifest, (Func<bool>) (() => this._easyMode), (Action<bool>) (value => this._easyMode = value), (Func<string>) (() => "Easy Mode"), (Func<string>) (() => "When enabled: (1) the vending machine is sold at Robin's Carpentry Shop, and (2) all Star Bull editions are sold at Pierre's and JojaMart."));
  }

  private void LoadEmbeddedContentPackJson()
  {
    string str1;
    try
    {
      str1 = ModEntry.ReadEmbeddedText("content.json");
    }
    catch (Exception ex)
    {
      this.Log($"Failed to read embedded {"content.json"}: {ex}", LogLevel.Error);
      return;
    }
    JObject jobject1 = JObject.Parse(str1.Replace("{{ModId}}", this.ModManifest.UniqueID, StringComparison.Ordinal));
    try
    {
      JArray source1 = (JArray) jobject1["Changes"];
      if (source1 != null)
      {
        foreach (JObject jobject2 in source1.OfType<JObject>())
        {
          if (string.Equals((string) jobject2["Action"], "EditData", StringComparison.OrdinalIgnoreCase) && string.Equals((string) jobject2["Target"], "Data/Objects", StringComparison.OrdinalIgnoreCase) && jobject2["Entries"] is JObject jobject5)
          {
            foreach (JProperty property in jobject5.Properties())
            {
              if (property.Value is JObject jobject4 && jobject4["Buffs"] is JArray source2)
              {
                foreach (JObject jobject3 in source2.OfType<JObject>())
                {
                  string str2 = (string) jobject3["BuffId"];
                  if (!(str2 == "23") && !(str2 == "24"))
                  {
                    jobject3["IconTexture"] = (JToken) this.CustomBuffIconTexture;
                    if (jobject3["Id"] == null)
                      jobject3["Id"] = (JToken) $"{this.ModManifest.UniqueID}/Buff/{property.Name}";
                    if (jobject3["IconSpriteIndex"] == null)
                      jobject3["IconSpriteIndex"] = (JToken) 0;
                  }
                }
              }
            }
          }
        }
      }
    }
    catch (Exception ex)
    {
      this.Log($"Failed to inject custom buff icons (continuing without): {ex}", LogLevel.Warn);
    }
    foreach (JToken jtoken in (JArray) jobject1["Changes"] ?? new JArray())
    {
      if (jtoken is JObject jobject6)
      {
        string str3 = (string) jobject6["Action"] ?? "";
        string str4 = (string) jobject6["Target"] ?? "";
        if (!string.IsNullOrWhiteSpace(str3) && !string.IsNullOrWhiteSpace(str4))
        {
          switch (str3)
          {
            case "Load":
              string str5 = (string) jobject6["FromFile"];
              if (!string.IsNullOrWhiteSpace(str5))
              {
                this._loadMap[str4] = str5;
                break;
              }
              break;
            case "EditData":
              if (jobject6["Entries"] is JObject Entries)
              {
                if (string.Equals(str4, "Data/TriggerActions", StringComparison.OrdinalIgnoreCase))
                {
                  this.Log($"Skipping embedded EditData target '{str4}' (handled in code).", (LogLevel) 0);
                  break;
                }
                string[] TargetField = (string[]) null;
                if (jobject6["TargetField"] is JArray source && source.Count > 0)
                  TargetField = source.Select<JToken, string>((Func<JToken, string>) (p => (string) p ?? "")).Where<string>((Func<string, bool>) (p => !string.IsNullOrWhiteSpace(p))).ToArray<string>();
                ModEntry.EditOp editOp = new ModEntry.EditOp(str4, TargetField, Entries);
                List<ModEntry.EditOp> editOpList;
                if (!this._editOpsByTarget.TryGetValue(str4, out editOpList))
                  this._editOpsByTarget[str4] = editOpList = new List<ModEntry.EditOp>();
                editOpList.Add(editOp);
                break;
              }
              break;
          }
        }
      }
    }
    this._loadMap[this.VendingMachineTexture] = "assets/vending_machine.png";
    this.Log($"Embedded pack loaded: {this._loadMap.Count} texture load(s), {this._editOpsByTarget.Values.Sum<List<ModEntry.EditOp>>((Func<List<ModEntry.EditOp>, int>) (p => p.Count))} data edit(s).");
  }

  private void OnAssetRequested(object sender, AssetRequestedEventArgs e)
  {
    if (e.NameWithoutLocale.BaseName.Equals(this.CustomBuffIconTexture, StringComparison.OrdinalIgnoreCase))
      e.LoadFrom((Func<object>) (() => (object) this.LoadEmbeddedTexture("assets/buff_icon.png")), (AssetLoadPriority) 0, (string) null);
    else if (e.NameWithoutLocale.BaseName.Equals(this.VendingMachineTexture, StringComparison.OrdinalIgnoreCase))
    {
      e.LoadFrom((Func<object>) (() => (object) this.LoadEmbeddedTexture("assets/vending_machine.png")), (AssetLoadPriority) 0, (string) null);
    }
    else
    {
      string fromFile;
      if (this._loadMap.TryGetValue(e.NameWithoutLocale.BaseName, out fromFile) && !string.IsNullOrWhiteSpace(fromFile))
      {
        e.LoadFrom((Func<object>) (() => (object) this.LoadEmbeddedTexture(fromFile)), (AssetLoadPriority) 0, (string) null);
        this.Log($"Loaded embedded texture: {e.NameWithoutLocale.BaseName} <- {fromFile}", (LogLevel) 0);
      }
      else
      {
        List<ModEntry.EditOp> ops;
        if (this._editOpsByTarget.TryGetValue(e.NameWithoutLocale.BaseName, out ops))
        {
          e.Edit((Action<IAssetData>) (asset =>
          {
            foreach (ModEntry.EditOp op in ops)
              this.ApplyEdit(asset, op);
          }), (AssetEditPriority) 0, (string) null);
          this.Log($"Applied {ops.Count} data edit(s) to: {e.NameWithoutLocale.BaseName}", (LogLevel) 0);
        }
        if (!e.NameWithoutLocale.BaseName.Equals("Data/Furniture", StringComparison.OrdinalIgnoreCase))
          return;
        e.Edit((Action<IAssetData>) (asset => ((IAssetData<IDictionary<string, string>>) asset.AsDictionary<string, string>()).Data["ThaleTheGreat.StarBull_VendingMachine"] = $"StarBullVendingMachine/other/1 2/1 1/1/0/2/Star Bull Vending Machine/0/{this.VendingMachineTexture}/true/starbull vending_machine"), (AssetEditPriority) 0, (string) null);
      }
    }
  }

  private void OnDayStarted(object sender, DayStartedEventArgs e)
  {
    try
    {
      if (!GameStateQuery.CheckConditions("ANY \"IS_COMMUNITY_CENTER_COMPLETE\" \"IS_JOJA_MART_COMPLETE\"", (GameLocation) null, Game1.player, (Item) null, (Item) null, (Random) null, (HashSet<string>) null) || ((NetHashSet<string>) Game1.player.mailReceived).Contains("ThaleTheGreat.StarBull_VendingMachineReceived"))
        return;
      Game1.player.addItemByMenuIfNecessary(ItemRegistry.Create("(F)ThaleTheGreat.StarBull_VendingMachine", 1, 0, false), (ItemGrabMenu.behaviorOnItemSelect) null, false);
      ((NetHashSet<string>) Game1.player.mailReceived).Add("ThaleTheGreat.StarBull_VendingMachineReceived");
      Game1.addHUDMessage(new HUDMessage("A Star Bull vending machine has been delivered!", 2));
      this.Log("Delivered Star Bull vending machine.");
    }
    catch (Exception ex)
    {
      this.Log($"Failed while delivering vending machine: {ex}", LogLevel.Error);
    }
  }

  private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
  {
    if (!Context.IsWorldReady || Game1.activeClickableMenu != null || !SButtonExtensions.IsActionButton(e.Button))
      return;
    Vector2 grabTile = this.Helper.Input.GetCursorPosition().GrabTile;
    GameLocation currentLocation = Game1.currentLocation;
    Furniture furniture = null;
    try
    {
      foreach (Furniture f in currentLocation.furniture)
      {
        if (f != null)
        {
          Rectangle boundingBoxCompat = ModEntry.GetFurnitureBoundingBoxCompat(f);
          Point point = new Point((int) ((double) grabTile.X * 64.0 + 32.0), (int) ((double) grabTile.Y * 64.0 + 32.0));
          if (boundingBoxCompat.Contains(point))
          {
            furniture = f;
            break;
          }
        }
      }
    }
    catch
    {
      return;
    }
    if (furniture == null || !string.Equals(((Item) furniture).ItemId, "ThaleTheGreat.StarBull_VendingMachine", StringComparison.OrdinalIgnoreCase))
      return;
    this.Helper.Input.Suppress(e.Button);
    try
    {
      this.DispenseDailyCoreDrinks();
    }
    catch (Exception ex)
    {
      this.Log($"Failed opening Star Bull vending machine: {ex}", LogLevel.Error);
    }
  }

  private void DispenseDailyCoreDrinks()
  {
    int totalDays = Game1.Date.TotalDays;
    string s;
    int result;
    if (((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>) ((Character) Game1.player).modData).TryGetValue("ThaleTheGreat.StarBull/VendClaimDay", out s) && int.TryParse(s, out result) && result == totalDays)
    {
      Game1.playSound("cancel", new int?());
      Game1.addHUDMessage(new HUDMessage("The Star Bull vending machine is empty for today. Check back tomorrow!", 3));
    }
    else
    {
      ((NetDictionary<string, string, NetString, SerializableDictionary<string, string>, NetStringDictionary<string, NetString>>) ((Character) Game1.player).modData)["ThaleTheGreat.StarBull/VendClaimDay"] = totalDays.ToString();
      string[] strArray = new string[3]
      {
        $"(O){this.ModManifest.UniqueID}_Original",
        $"(O){this.ModManifest.UniqueID}_Sugarfree",
        $"(O){this.ModManifest.UniqueID}_Zero"
      };
      int num = 0;
      foreach (string str1 in strArray)
      {
        Item obj = (Item) null;
        try
        {
          obj = ItemRegistry.Create(str1, 1, 0, false);
        }
        catch
        {
          string str2 = str1.Replace("_Sugarfree", "_SugarFree");
          try
          {
            obj = ItemRegistry.Create(str2, 1, 0, false);
          }
          catch
          {
          }
        }
        if (obj != null)
        {
          if (!Game1.player.addItemToInventoryBool(obj, false))
            Game1.createItemDebris(obj, ((Character) Game1.player).getStandingPosition(), ((Character) Game1.player).FacingDirection, Game1.currentLocation, -1, false);
          ++num;
        }
      }
      if (num > 0)
      {
        Game1.playSound("purchase", new int?());
        Game1.addHUDMessage(new HUDMessage("Star Bull vending machine dispensed today's drinks!", 2));
        this.Log($"Vending machine dispensed daily set (x{num}).", (LogLevel) 0);
      }
      else
      {
        Game1.playSound("cancel", new int?());
        Game1.addHUDMessage(new HUDMessage("The vending machine rattled, but nothing came out.", 3));
      }
    }
  }

  private IEnumerable<string> GetVendingStockQualifiedItemIds()
  {
    yield return $"(O){this.ModManifest.UniqueID}_Original";
    yield return $"(O){this.ModManifest.UniqueID}_Sugarfree";
    yield return $"(O){this.ModManifest.UniqueID}_Zero";
    yield return $"(O){this.ModManifest.UniqueID}_TotalZero";
    yield return $"(O){this.ModManifest.UniqueID}_RedEdition";
    yield return $"(O){this.ModManifest.UniqueID}_YellowEdition";
    yield return $"(O){this.ModManifest.UniqueID}_BlueEdition";
    yield return $"(O){this.ModManifest.UniqueID}_CoconutEdition";
    yield return $"(O){this.ModManifest.UniqueID}_PeachEdition";
    yield return $"(O){this.ModManifest.UniqueID}_AmberEdition";
    yield return $"(O){this.ModManifest.UniqueID}_SeaBlueEdition";
    yield return $"(O){this.ModManifest.UniqueID}_GreenEdition";
    yield return $"(O){this.ModManifest.UniqueID}_PinkEdition";
    yield return $"(O){this.ModManifest.UniqueID}_IcedEdition";
  }

  private object CreateItemStockInformationProxy(int price, int stock)
  {
    Type fieldType = typeof (ShopMenu).GetField("itemPriceAndStock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.FieldType;
    Type type = !(fieldType != (Type) null) || !fieldType.IsGenericType ? (Type) null : ((IEnumerable<Type>) fieldType.GetGenericArguments()).LastOrDefault<Type>();
    if ((object) type == null)
      type = typeof (object);
    return ModEntry.CreateItemStockInformation(Activator.CreateInstance(typeof (Dictionary<,>).MakeGenericType(typeof (ISalable), type)), price, stock);
  }

  private ShopMenu TryCreateShopMenuFromStock(List<Item> items, int price, int stock)
  {
    foreach (ConstructorInfo constructor in typeof (ShopMenu).GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
    {
      try
      {
        ParameterInfo[] parameters = ((MethodBase) constructor).GetParameters();
        if (parameters.Length != 0)
        {
          Type parameterType1 = parameters[0].ParameterType;
          Type type1 = !parameterType1.IsGenericType || !(parameterType1.GetGenericTypeDefinition() == typeof (IDictionary<,>)) ? ((IEnumerable<Type>) parameterType1.GetInterfaces()).FirstOrDefault<Type>((Func<Type, bool>) (i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof (IDictionary<,>))) : parameterType1;
          if ((object) type1 != null)
          {
            Type[] genericArguments = type1.GetGenericArguments();
            if (genericArguments.Length == 2)
            {
              Type type2 = genericArguments[0];
              Type valueType = genericArguments[1];
              if (typeof (ISalable).IsAssignableFrom(type2) || type2.IsAssignableFrom(typeof (Item)) || type2 == typeof (object))
              {
                Type type3 = typeof (Dictionary<,>).MakeGenericType(type2, valueType);
                object instance = Activator.CreateInstance(type3);
                MethodInfo methodInfo1 = type3.GetMethod("Add", new Type[2]
                {
                  type2,
                  valueType
                });
                if ((object) methodInfo1 == null)
                  methodInfo1 = ((IEnumerable<MethodInfo>) type3.GetMethods()).FirstOrDefault<MethodInfo>((Func<MethodInfo, bool>) (m => ((MemberInfo) m).Name == "Add" && ((MethodBase) m).GetParameters().Length == 2));
                MethodInfo methodInfo2 = methodInfo1;
                foreach (Item obj1 in items)
                {
                  object obj2 = ModEntry.CoerceKey(type2, obj1);
                  object stockValue = ModEntry.CreateStockValue(valueType, price, stock);
                  ((MethodBase) methodInfo2).Invoke(instance, new object[2]
                  {
                    obj2,
                    stockValue
                  });
                }
                object[] objArray = new object[parameters.Length];
                objArray[0] = instance;
                for (int index = 1; index < parameters.Length; ++index)
                {
                  Type parameterType2 = parameters[index].ParameterType;
                  if (parameterType2 == typeof (int))
                    objArray[index] = (object) 0;
                  else if (parameterType2 == typeof (bool))
                    objArray[index] = (object) false;
                  else if (parameterType2 == typeof (string))
                    objArray[index] = (object) "Star Bull Vending";
                  else if (parameterType2 == typeof (Farmer))
                    objArray[index] = (object) Game1.player;
                  else if (typeof (IList).IsAssignableFrom(parameterType2))
                  {
                    objArray[index] = Activator.CreateInstance(parameterType2);
                  }
                  else
                  {
                    int num = !typeof (IEnumerable).IsAssignableFrom(parameterType2) ? 0 : (parameterType2 != typeof (string) ? 1 : 0);
                    objArray[index] = num == 0 ? (!parameterType2.IsEnum ? ModEntry.GetDefaultValue(parameterType2) : Activator.CreateInstance(parameterType2)) : (object) null;
                  }
                }
                if (constructor.Invoke(objArray) is ShopMenu shopMenuFromStock)
                  return shopMenuFromStock;
              }
            }
          }
        }
      }
      catch
      {
      }
    }
    return (ShopMenu) null;
  }

  private static object CoerceKey(Type keyType, Item item)
  {
    return keyType.IsAssignableFrom(item.GetType()) || !typeof (ISalable).IsAssignableFrom(keyType) ? (object) item : (object) item;
  }

  private static object CreateStockValue(Type valueType, int price, int stock)
  {
    if (valueType == typeof (int[]))
      return (object) new int[2]{ price, stock };
    if (!(valueType.Name == "ItemStockInformation"))
      return Activator.CreateInstance(valueType);
    foreach (ConstructorInfo constructorInfo in (IEnumerable<ConstructorInfo>) ((IEnumerable<ConstructorInfo>) valueType.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).OrderByDescending<ConstructorInfo, int>((Func<ConstructorInfo, int>) (c => ((MethodBase) c).GetParameters().Length)))
    {
      ParameterInfo[] parameters = ((MethodBase) constructorInfo).GetParameters();
      if (parameters.Length >= 2 && parameters[0].ParameterType == typeof (int) && parameters[1].ParameterType == typeof (int))
      {
        object[] objArray = new object[parameters.Length];
        objArray[0] = (object) price;
        objArray[1] = (object) stock;
        for (int index = 2; index < parameters.Length; ++index)
          objArray[index] = ModEntry.GetDefaultValue(parameters[index].ParameterType);
        return constructorInfo.Invoke(objArray);
      }
    }
    object instance = Activator.CreateInstance(valueType);
    ModEntry.TrySetMember(instance, "Price", (object) price);
    ModEntry.TrySetMember(instance, nameof (price), (object) price);
    ModEntry.TrySetMember(instance, "Stock", (object) stock);
    ModEntry.TrySetMember(instance, nameof (stock), (object) stock);
    return instance;
  }

  private static bool TrySetMember(object instance, string memberName, object value)
  {
    if (instance == null)
      return false;
    Type type = instance.GetType();
    PropertyInfo property = type.GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (property != null && property.CanWrite)
    {
      try
      {
        object obj = value;
        if (value != null && !property.PropertyType.IsInstanceOfType(value))
        {
          try
          {
            obj = Convert.ChangeType(value, property.PropertyType);
          }
          catch
          {
          }
        }
        property.SetValue(instance, obj);
        return true;
      }
      catch
      {
      }
    }
    FieldInfo field = type.GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    if (field != null)
    {
      try
      {
        object obj = value;
        if (value != null && !field.FieldType.IsInstanceOfType(value))
        {
          try
          {
            obj = Convert.ChangeType(value, field.FieldType);
          }
          catch
          {
          }
        }
        field.SetValue(instance, obj);
        return true;
      }
      catch
      {
      }
    }
    return false;
  }

  private static object CoerceForSaleArg(Type targetType, List<ISalable> list)
  {
    if (targetType.IsAssignableFrom(list.GetType()) || targetType.IsAssignableFrom(typeof (List<ISalable>)) || !targetType.IsGenericType)
      return (object) list;
    targetType.GetGenericTypeDefinition();
    Type[] genericArguments = targetType.GetGenericArguments();
    if (genericArguments.Length != 1 || !typeof (ISalable).IsAssignableFrom(genericArguments[0]))
      return (object) list;
    IList instance = (IList) Activator.CreateInstance(typeof (List<>).MakeGenericType(genericArguments[0]));
    foreach (ISalable isalable in list)
      instance.Add((object) isalable);
    return (object) instance;
  }

  private void OnMenuChanged(object sender, MenuChangedEventArgs e)
  {
    if (!Context.IsWorldReady || !this._easyMode)
      return;
    if (!(e.NewMenu is ShopMenu newMenu))
      return;
    try
    {
      string str1 = ReadStringMember((object) newMenu, "storeContext", "StoreContext");
      string str2 = ReadStringMember((object) newMenu, "shopId", "ShopId", "ShopID");
      NPC npc1 = (NPC) null;
      if (newMenu.GetType().GetField("portraitPerson", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) newMenu) is NPC npc2)
        npc1 = npc2;
      string name = ((Character) npc1)?.Name;
      bool flag1 = string.Equals(name, "Robin", StringComparison.OrdinalIgnoreCase) || str1 != null && str1.IndexOf("Carpenter", StringComparison.OrdinalIgnoreCase) >= 0 || str2 != null && str2.IndexOf("Carpenter", StringComparison.OrdinalIgnoreCase) >= 0;
      bool flag2 = string.Equals(name, "Pierre", StringComparison.OrdinalIgnoreCase) || str1 != null && str1.IndexOf("SeedShop", StringComparison.OrdinalIgnoreCase) >= 0 || str1 != null && str1.IndexOf("Pierre", StringComparison.OrdinalIgnoreCase) >= 0 || str2 != null && str2.IndexOf("SeedShop", StringComparison.OrdinalIgnoreCase) >= 0 || str2 != null && str2.IndexOf("Pierre", StringComparison.OrdinalIgnoreCase) >= 0;
      bool flag3 = str1 != null && str1.IndexOf("Joja", StringComparison.OrdinalIgnoreCase) >= 0 || str1 != null && str1.IndexOf("JojaMart", StringComparison.OrdinalIgnoreCase) >= 0 || str2 != null && str2.IndexOf("Joja", StringComparison.OrdinalIgnoreCase) >= 0 || str2 != null && str2.IndexOf("JojaMart", StringComparison.OrdinalIgnoreCase) >= 0 || string.Equals(name, "Morris", StringComparison.OrdinalIgnoreCase);
      this.Log($"Easy Mode shop open detected: npc='{name ?? ""}', shopId='{str2 ?? ""}', storeContext='{str1 ?? ""}'", (LogLevel) 0);
      if (flag1)
      {
        Item obj = ItemRegistry.Create("(F)ThaleTheGreat.StarBull_VendingMachine", 1, 0, false);
        this.InjectShopItem(newMenu, obj, 10000, -1);
        this.Log("Easy Mode: injected Star Bull vending machine into Robin's shop.", (LogLevel) 0);
      }
      if (!(flag2 | flag3))
        return;
      foreach (string drinkQualifiedItemId in this.GetAllStarBullDrinkQualifiedItemIds())
      {
        Item obj;
        try
        {
          obj = ItemRegistry.Create(drinkQualifiedItemId, 1, 0, false);
        }
        catch
        {
          continue;
        }
        int price = this.TryGetConfiguredShopPriceForItem(drinkQualifiedItemId) ?? 750;
        this.InjectShopItem(newMenu, obj, price, -1);
      }
      this.Log($"Easy Mode: injected Star Bull drink editions into {(flag2 ? "Pierre" : "Joja")} shop.", (LogLevel) 0);
    }
    catch (Exception ex)
    {
      this.Log($"Failed Easy Mode shop injection: {ex}", LogLevel.Warn);
    }

    static string ReadStringMember(object obj, params string[] names)
    {
      Type type = obj.GetType();
      foreach (string name in names)
      {
        if (type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) is string str3 && !string.IsNullOrWhiteSpace(str3))
          return str3;
        if (type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(obj) is string str4 && !string.IsNullOrWhiteSpace(str4))
          return str4;
      }
      return (string) null;
    }
  }

  private static void SetShopStock(ShopMenu shop, Item item, int price, int stock)
  {
    try
    {
      object dictObj = typeof (ShopMenu).GetField("itemPriceAndStock", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue((object) shop);
      if (dictObj == null)
        return;
      object stockInformation = ModEntry.CreateItemStockInformation(dictObj, price, stock);
      dictObj.GetType().GetProperty("Item", BindingFlags.Instance | BindingFlags.Public)?.SetValue(dictObj, stockInformation, new object[1]
      {
        (object) item
      });
    }
    catch
    {
    }
  }

  private void InjectShopItem(ShopMenu shop, Item item, int price, int stock)
  {
    foreach (ISalable isalable in shop.forSale)
    {
      if (isalable is Item obj && string.Equals(obj.QualifiedItemId, item.QualifiedItemId, StringComparison.OrdinalIgnoreCase))
        return;
    }
    shop.forSale.Add((ISalable) item);
    ModEntry.SetShopStock(shop, item, price, stock);
  }

  private IEnumerable<string> GetAllStarBullDrinkQualifiedItemIds()
  {
    if (this._cachedAllDrinkQids == null)
      this._cachedAllDrinkQids = this.BuildAllStarBullDrinkQualifiedItemIds();
    return (IEnumerable<string>) this._cachedAllDrinkQids;
  }

  private List<string> BuildAllStarBullDrinkQualifiedItemIds()
  {
    List<string> stringList = new List<string>();
    Dictionary<string, ObjectData> dictionary;
    try
    {
      dictionary = this.Helper.GameContent.Load<Dictionary<string, ObjectData>>("Data/Objects");
    }
    catch
    {
      return stringList;
    }
    string str = this.ModManifest.UniqueID + "_";
    foreach (KeyValuePair<string, ObjectData> keyValuePair in dictionary)
    {
      if (keyValuePair.Key.StartsWith(str, StringComparison.OrdinalIgnoreCase))
      {
        ObjectData objectData = keyValuePair.Value;
        if (objectData != null)
        {
          try
          {
            if (objectData.Edibility > 0)
            {
              if (!objectData.IsDrink)
                continue;
            }
            else
              continue;
          }
          catch
          {
            if (objectData.Edibility <= 0)
              continue;
          }
          stringList.Add("(O)" + keyValuePair.Key);
        }
      }
    }
    stringList.Sort((IComparer<string>) StringComparer.OrdinalIgnoreCase);
    return stringList;
  }

  private int? TryGetConfiguredShopPriceForItem(string qualifiedItemId)
  {
    if (qualifiedItemId.EndsWith("_Original", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_Sugarfree", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_SugarFree", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_Zero", StringComparison.OrdinalIgnoreCase))
      return new int?(250);
    if (qualifiedItemId.EndsWith("_TotalZero", StringComparison.OrdinalIgnoreCase))
      return new int?(275);
    if (qualifiedItemId.EndsWith("_BlueEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_IcedEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_PeachEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_PinkEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_SeaBlueEdition", StringComparison.OrdinalIgnoreCase))
      return new int?(500);
    if (qualifiedItemId.Contains("_SpringEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.Contains("_WinterEdition", StringComparison.OrdinalIgnoreCase))
      return new int?(750);
    if (qualifiedItemId.Contains("_SummerEdition", StringComparison.OrdinalIgnoreCase))
      return new int?(500);
    if (qualifiedItemId.EndsWith("_StarBullMusk", StringComparison.OrdinalIgnoreCase))
      return new int?(2000);
    if (qualifiedItemId.EndsWith("_StarBullGarlic", StringComparison.OrdinalIgnoreCase))
      return new int?(4000);
    if (qualifiedItemId.EndsWith("_AmberEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_CoconutEdition", StringComparison.OrdinalIgnoreCase))
      return new int?(10000);
    if (qualifiedItemId.EndsWith("_YellowEdition", StringComparison.OrdinalIgnoreCase) || qualifiedItemId.EndsWith("_GreenEdition", StringComparison.OrdinalIgnoreCase))
      return new int?(750);
    return qualifiedItemId.EndsWith("_RedEdition", StringComparison.OrdinalIgnoreCase) ? new int?(500) : new int?(750);
  }

  private static object CreateItemStockInformation(object dictObj, int price, int stock)
  {
    Type type1 = dictObj.GetType();
    Type type2 = type1.IsGenericType ? ((IEnumerable<Type>) type1.GetGenericArguments()).LastOrDefault<Type>() : (Type) null;
    if ((object) type2 == null)
      type2 = typeof (object);
    foreach (ConstructorInfo constructorInfo in (IEnumerable<ConstructorInfo>) ((IEnumerable<ConstructorInfo>) type2.GetConstructors(BindingFlags.Instance | BindingFlags.Public)).OrderBy<ConstructorInfo, int>((Func<ConstructorInfo, int>) (c => ((MethodBase) c).GetParameters().Length)))
    {
      ParameterInfo[] parameters = ((MethodBase) constructorInfo).GetParameters();
      if (parameters.Length >= 2 && !(parameters[0].ParameterType != typeof (int)) && !(parameters[1].ParameterType != typeof (int)))
      {
        object[] objArray = new object[parameters.Length];
        objArray[0] = (object) price;
        objArray[1] = (object) stock;
        for (int index = 2; index < parameters.Length; ++index)
          objArray[index] = ModEntry.GetDefaultValue(parameters[index].ParameterType);
        return constructorInfo.Invoke(objArray);
      }
    }
    return Activator.CreateInstance(type2) ?? new object();
  }

  private static object GetDefaultValue(Type type)
  {
    return !type.IsValueType || Nullable.GetUnderlyingType(type) != (Type) null ? (object) null : Activator.CreateInstance(type);
  }

  private bool PlayerOwnsStarBullVendingMachine()
  {
    try
    {
      if (Game1.player?.Items != null)
      {
        foreach (Item obj in Game1.player.Items)
        {
          if (obj != null && string.Equals(obj.QualifiedItemId, "(F)ThaleTheGreat.StarBull_VendingMachine", StringComparison.OrdinalIgnoreCase))
            return true;
        }
      }
      foreach (GameLocation location in (IEnumerable<GameLocation>) Game1.locations)
      {
        foreach (Furniture furniture in location.furniture)
        {
          if (furniture != null && (string.Equals(((Item) furniture).QualifiedItemId, "(F)ThaleTheGreat.StarBull_VendingMachine", StringComparison.OrdinalIgnoreCase) || string.Equals(((Item) furniture).ItemId, "ThaleTheGreat.StarBull_VendingMachine", StringComparison.OrdinalIgnoreCase)))
            return true;
        }
      }
    }
    catch
    {
    }
    return false;
  }

  private static Rectangle GetFurnitureBoundingBoxCompat(Furniture f)
  {
    try
    {
      Type type = f.GetType();
      MethodInfo method1 = type.GetMethod("getBoundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, (Binder) null, new Type[1]
      {
        typeof (Vector2)
      }, (ParameterModifier[]) null);
      if (method1 != null)
        return (Rectangle) ((MethodBase) method1).Invoke((object) f, new object[1]
        {
          (object) ((SObject) f).TileLocation
        });
      MethodInfo method2 = type.GetMethod("GetBoundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, (Binder) null, new Type[1]
      {
        typeof (Vector2)
      }, (ParameterModifier[]) null);
      if (method2 != null)
        return (Rectangle) ((MethodBase) method2).Invoke((object) f, new object[1]
        {
          (object) ((SObject) f).TileLocation
        });
      object obj = (object) null;
      PropertyInfo property1 = type.GetProperty("boundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      if ((object) property1 == null)
        property1 = type.GetProperty("BoundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
      PropertyInfo propertyInfo = property1;
      if (propertyInfo != null)
        obj = propertyInfo.GetValue((object) f);
      if (obj == null)
      {
        FieldInfo field = type.GetField("boundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if ((object) field == null)
          field = type.GetField("BoundingBox", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        FieldInfo fieldInfo = field;
        if (fieldInfo != null)
          obj = fieldInfo.GetValue((object) f);
      }
      if (obj is Rectangle boundingBoxCompat)
        return boundingBoxCompat;
      if (obj != null)
      {
        PropertyInfo property2 = obj.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property2 != null && property2.PropertyType == typeof (Rectangle))
          return (Rectangle) property2.GetValue(obj);
      }
    }
    catch
    {
    }
    return new Rectangle((int) ((SObject) f).TileLocation.X * 64 /*0x40*/, (int) ((SObject) f).TileLocation.Y * 64 /*0x40*/, 64 /*0x40*/, 64 /*0x40*/);
  }

  private void ApplyEdit(IAssetData asset, ModEntry.EditOp op)
  {
    if (op.Target.Equals("Data/Shops", StringComparison.OrdinalIgnoreCase))
    {
      this.ApplyShopEdits(asset, op);
    }
    else
    {
      switch (op.Target)
      {
        case "Data/Objects":
          IDictionary<string, ObjectData> data = ((IAssetData<IDictionary<string, ObjectData>>) asset.AsDictionary<string, ObjectData>()).Data;
          ModEntry.MergeDict<ObjectData>(data, op.Entries, new Func<JToken, ObjectData>(ModEntry.Deserialize<ObjectData>));
          using (IEnumerator<KeyValuePair<string, ObjectData>> enumerator = data.GetEnumerator())
          {
            while (enumerator.MoveNext())
            {
              KeyValuePair<string, ObjectData> current = enumerator.Current;
              if (current.Key != null && current.Key.StartsWith(this.ModManifest.UniqueID, StringComparison.OrdinalIgnoreCase) && current.Value != null)
                current.Value.Price = 0;
            }
            break;
          }
        case "Data/BigCraftables":
          ModEntry.MergeDict<BigCraftableData>(((IAssetData<IDictionary<string, BigCraftableData>>) asset.AsDictionary<string, BigCraftableData>()).Data, op.Entries, new Func<JToken, BigCraftableData>(ModEntry.Deserialize<BigCraftableData>));
          break;
        case "Data/Mail":
          ModEntry.MergeDict<string>(((IAssetData<IDictionary<string, string>>) asset.AsDictionary<string, string>()).Data, op.Entries, (Func<JToken, string>) (token => token.Type != JTokenType.String ? token.ToString(Formatting.None) : token.Value<string>()));
          break;
        case "Data/Hats":
          ModEntry.MergeDict<string>(((IAssetData<IDictionary<string, string>>) asset.AsDictionary<string, string>()).Data, op.Entries, (Func<JToken, string>) (token => token.Type != JTokenType.String ? token.ToString(Formatting.None) : token.Value<string>()));
          break;
        case "Data/TriggerActions":
          break;
        default:
          this.Log($"Unhandled EditData target '{op.Target}'.", LogLevel.Warn);
          break;
      }
    }
  }

  private void ApplyShopEdits(IAssetData asset, ModEntry.EditOp op)
  {
    IDictionary<string, ShopData> data = ((IAssetData<IDictionary<string, ShopData>>) asset.AsDictionary<string, ShopData>()).Data;
    if (op.TargetField == null || op.TargetField.Length < 2)
    {
      this.Log("Shop edit missing TargetField; skipping.", LogLevel.Warn);
    }
    else
    {
      string key = op.TargetField[0];
      string str = op.TargetField[1];
      ShopData shopData1;
      if (!data.TryGetValue(key, out shopData1))
        this.Log($"Shop '{key}' not found in Data/Shops; skipping entries.", LogLevel.Warn);
      else if (!str.Equals("Items", StringComparison.OrdinalIgnoreCase))
      {
        this.Log($"Unsupported TargetField '{str}' for shop edits; expected 'Items'.", LogLevel.Warn);
      }
      else
      {
        ShopData shopData2 = shopData1;
        if (shopData2.Items == null)
          shopData2.Items = new List<ShopItemData>();
        foreach (JProperty property in op.Entries.Properties())
        {
          ShopItemData item = ModEntry.Deserialize<ShopItemData>(property.Value);
          if (string.IsNullOrWhiteSpace(((GenericSpawnItemData) item).Id))
            ((GenericSpawnItemData) item).Id = property.Name;
          int index = shopData1.Items.FindIndex((Predicate<ShopItemData>) (p => string.Equals(((GenericSpawnItemData) p).Id, ((GenericSpawnItemData) item).Id, StringComparison.OrdinalIgnoreCase)));
          if (index >= 0)
            shopData1.Items[index] = item;
          else
            shopData1.Items.Add(item);
        }
        data[key] = shopData1;
      }
    }
  }

  private Texture2D LoadEmbeddedTexture(string fromFile)
  {
    Stream stream1 = ModEntry.OpenEmbeddedStream(fromFile);
    if (stream1 == null)
      throw new FileNotFoundException($"Embedded texture not found for '{fromFile}'.");
    using (Stream stream2 = stream1)
      return Texture2D.FromStream(Game1.graphics.GraphicsDevice, stream2);
  }

  private static void MergeDict<T>(
    IDictionary<string, T> dict,
    JObject entries,
    Func<JToken, T> convert)
  {
    foreach (JProperty property in entries.Properties())
      dict[property.Name] = convert(property.Value);
  }

  private static T Deserialize<T>(JToken token)
  {
    return JsonConvert.DeserializeObject<T>(token.ToString(Formatting.None)) ?? throw new InvalidOperationException($"Failed to deserialize {typeof (T).Name} from token.");
  }

  private static string ReadEmbeddedText(string relativePath)
  {
    Stream stream1 = ModEntry.OpenEmbeddedStream(relativePath);
    if (stream1 == null)
      throw new FileNotFoundException("Embedded resource not found: " + relativePath);
    using (Stream stream2 = stream1)
    {
      using (StreamReader streamReader = new StreamReader(stream2))
        return ((TextReader) streamReader).ReadToEnd();
    }
  }

  private static Stream OpenEmbeddedStream(string relativePath)
  {
    string str1 = relativePath.Replace('\\', '/').TrimStart('/');
    Assembly assembly = typeof (ModEntry).Assembly;
    string str2 = "ThaleTheGreat.StarBull.Resources." + str1.Replace('/', '.');
    Stream manifestResourceStream = assembly.GetManifestResourceStream(str2);
    if (manifestResourceStream != null)
      return manifestResourceStream;
    string str3 = "." + str1.Replace('/', '.');
    foreach (string manifestResourceName in assembly.GetManifestResourceNames())
    {
      if (manifestResourceName.EndsWith(str3, StringComparison.OrdinalIgnoreCase) || manifestResourceName.EndsWith(".Resources" + str3, StringComparison.OrdinalIgnoreCase))
        return assembly.GetManifestResourceStream(manifestResourceName);
    }
    return null;
  }

  internal object GetCustomBuffIconTexture()
  {
    try
    {
      return (object) (this._cachedCustomBuffIcon ?? (this._cachedCustomBuffIcon = this.Helper.GameContent.Load<Texture2D>(this.CustomBuffIconTexture)));
    }
    catch
    {
      return (object) null;
    }
  }

  internal bool IsSpecialEffectEdition(SObject obj)
  {
    string str = ((Item) obj)?.ItemId ?? ((Item) obj)?.QualifiedItemId ?? "";
    return str.Contains("_StarBullMusk", StringComparison.OrdinalIgnoreCase) || str.Contains("_StarBullGarlic", StringComparison.OrdinalIgnoreCase);
  }

  private sealed record EditOp(string Target, string[] TargetField, JObject Entries);
}
