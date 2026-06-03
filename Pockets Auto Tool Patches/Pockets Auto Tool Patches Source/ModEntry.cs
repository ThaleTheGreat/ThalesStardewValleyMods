using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace ThaleTheGreat.PocketsAutoToolPatches;

internal sealed class ModEntry : Mod
{
    private const string PocketsId = "aedenthorn.Pockets";
    private const string AutomateToolSwapId = "Trapyy.AutomatetoolSwap";
    private const string ToolSmartSwitchId = "aedenthorn.ToolSmartSwitch";
    private const string PocketsModDataKey = "aedenthorn.Pockets/pocket";

    private readonly List<PendingProxyItem> pendingProxyItems = new();
    private readonly List<IPocketProvider> pocketProviders = new();

    private Harmony? harmony;
    private MethodInfo? toolSmartSwitchSmartSwitchMethod;
    private MethodInfo? toolSmartSwitchSwitchForTerrainFeatureMethod;
    private MethodInfo? toolSmartSwitchGetToolsMethod;
    private FieldInfo? toolSmartSwitchConfigField;

    internal static ModEntry Instance { get; private set; } = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
        helper.Events.GameLoop.Saving += this.OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => this.ClearBridgeProxies();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        this.ApplyRuntimePatches();
    }

    private void ApplyRuntimePatches()
    {
        bool automateToolSwapLoaded = this.Helper.ModRegistry.IsLoaded(AutomateToolSwapId);
        bool toolSmartSwitchLoaded = this.Helper.ModRegistry.IsLoaded(ToolSmartSwitchId);

        if (!this.Helper.ModRegistry.IsLoaded(PocketsId) || (!automateToolSwapLoaded && !toolSmartSwitchLoaded))
            return;

        this.harmony = new Harmony(this.ModManifest.UniqueID);

        PocketsBridge? pockets = PocketsBridge.TryCreate(this.Monitor);
        if (pockets is null)
        {
            this.Monitor.Log("Could not find Pockets internals. Pockets auto-tool compatibility was not applied.", LogLevel.Warn);
            return;
        }

        this.pocketProviders.Add(pockets);

        if (automateToolSwapLoaded)
            this.PatchAutomateToolSwap();

        if (toolSmartSwitchLoaded)
            this.PatchToolSmartSwitch();
    }

    private void PatchAutomateToolSwap()
    {
        Type? inventoryHandler = AccessTools.TypeByName("Core.InventoryHandler");
        if (inventoryHandler is null)
        {
            this.Monitor.Log("Could not find Automate Tool Swap's inventory handler. Compatibility was not applied.", LogLevel.Warn);
            return;
        }

        MethodInfo? setTool = AccessTools.Method(inventoryHandler, "SetTool");
        MethodInfo? setItem = AccessTools.Method(inventoryHandler, "SetItem");
        if (setTool is null || setItem is null || this.harmony is null)
        {
            this.Monitor.Log("Could not find Automate Tool Swap's switch methods. Compatibility was not applied.", LogLevel.Warn);
            return;
        }

        this.harmony.Patch(setTool, prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetToolPrefix)));
        this.harmony.Patch(setItem, prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetItemPrefix)));
    }

    private void PatchToolSmartSwitch()
    {
        Type? modEntry = AccessTools.TypeByName("ToolSmartSwitch.ModEntry");
        MethodInfo? switchToolType = modEntry is not null
            ? AccessTools.Method(modEntry, "SwitchToolType")
            : null;

        if (switchToolType is null || this.harmony is null)
        {
            this.Monitor.Log("Could not find Tool Smart Switch's switch method. Compatibility was not applied.", LogLevel.Warn);
            return;
        }

        this.toolSmartSwitchSmartSwitchMethod = AccessTools.Method(modEntry, "SmartSwitch", new[] { typeof(Farmer) });
        this.toolSmartSwitchSwitchForTerrainFeatureMethod = AccessTools.Method(modEntry, "SwitchForTerrainFeature", new[] { typeof(Farmer), typeof(TerrainFeature), typeof(Dictionary<int, Tool>) });
        this.toolSmartSwitchGetToolsMethod = AccessTools.Method(modEntry, "GetTools", new[] { typeof(Farmer) });
        this.toolSmartSwitchConfigField = AccessTools.Field(modEntry, "Config");

        this.harmony.Patch(switchToolType, prefix: new HarmonyMethod(typeof(ModEntry), nameof(ToolSmartSwitchPrefix)));

        if (this.toolSmartSwitchSmartSwitchMethod is not null)
        {
            HarmonyMethod useToolPrefix = new(typeof(ModEntry), nameof(ToolSmartSwitchUseToolButtonPrefix))
            {
                priority = Priority.First
            };

            this.harmony.Patch(AccessTools.Method(typeof(Game1), nameof(Game1.pressUseToolButton)), prefix: useToolPrefix);
        }

        if (this.toolSmartSwitchSwitchForTerrainFeatureMethod is not null && this.toolSmartSwitchGetToolsMethod is not null)
        {
            HarmonyMethod cropPrefix = new(typeof(ModEntry), nameof(ToolSmartSwitchHoeDirtPrefix))
            {
                priority = Priority.First
            };

            this.harmony.Patch(AccessTools.Method(typeof(HoeDirt), nameof(HoeDirt.performUseAction)), prefix: cropPrefix);
        }
    }

    private static bool SetToolPrefix(Farmer player, Type toolType, string aux = "", bool anyTool = false)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, player.maxItems.Value, item => MatchesTool(item, toolType, aux, anyTool)))
            return true;

        return !Instance.TryUsePocketProxy(player, item => MatchesTool(item, toolType, aux, anyTool));
    }

    private static bool SetItemPrefix(Farmer player, string category, string item = "", string crops = "Both", int aux = 0)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders())
            return true;

        if (Contains(player.Items, player.maxItems.Value, candidate => MatchesItem(candidate, category, item, crops, aux)))
            return true;

        return !Instance.TryUsePocketProxy(player, candidate => MatchesItem(candidate, category, item, crops, aux));
    }

    private static bool ToolSmartSwitchPrefix(Farmer f, Type? type, Dictionary<int, Tool> tools, ref bool __result)
    {
        if (!Context.IsWorldReady || f is null || !Instance.HasPocketProviders())
            return true;

        if (MatchesToolSmartSwitch(f.CurrentTool, type))
            return true;

        foreach (Tool tool in tools.Values)
        {
            if (MatchesToolSmartSwitch(tool, type))
                return true;
        }

        if (!Instance.TryUsePocketProxy(f, item => MatchesToolSmartSwitch(item, type), playSound: true))
            return true;

        __result = true;
        return false;
    }

    private static bool ToolSmartSwitchUseToolButtonPrefix()
    {
        Farmer player = Game1.player;
        if (!ShouldRunToolSmartSwitchHoldingToolBypass(player, requireCropSwitch: false) || Game1.fadeToBlack || !Context.CanPlayerMove)
            return true;

        try
        {
            Instance.toolSmartSwitchSmartSwitchMethod?.Invoke(null, new object[] { player });
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Tool Smart Switch holding-tool bypass failed: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        return true;
    }

    private static bool ToolSmartSwitchHoeDirtPrefix(HoeDirt __instance)
    {
        Farmer player = Game1.player;
        if (!ShouldRunToolSmartSwitchHoldingToolBypass(player, requireCropSwitch: true))
            return true;

        try
        {
            object? tools = Instance.toolSmartSwitchGetToolsMethod?.Invoke(null, new object[] { player });
            if (tools is Dictionary<int, Tool> toolMap)
                Instance.toolSmartSwitchSwitchForTerrainFeatureMethod?.Invoke(null, new object[] { player, __instance, toolMap });
        }
        catch (Exception ex)
        {
            Instance.Monitor.Log($"Tool Smart Switch crop bypass failed: {ex.GetBaseException().Message}", LogLevel.Warn);
        }

        return true;
    }

    private static bool ShouldRunToolSmartSwitchHoldingToolBypass(Farmer? player, bool requireCropSwitch)
    {
        if (!Context.IsWorldReady || player is null || !Instance.HasPocketProviders() || Instance.toolSmartSwitchConfigField is null)
            return false;

        if (player.CurrentTool is not null || !Instance.HasAnyPocketTool(player))
            return false;

        object? config = Instance.toolSmartSwitchConfigField.GetValue(null);
        if (config is null)
            return false;

        if (!GetBoolProperty(config, "ModEnabled") || !GetBoolProperty(config, "SwitchEnabled") || !GetBoolProperty(config, "HoldingTool"))
            return false;

        return !requireCropSwitch || GetBoolProperty(config, "SwitchForCrops");
    }

    private bool HasAnyPocketTool(Farmer player)
    {
        foreach (PocketInventory pocketInventory in this.GetPocketInventories(player))
        {
            foreach (Item? item in pocketInventory.Items)
            {
                if (item is Tool)
                    return true;
            }
        }

        return false;
    }

    private bool HasPocketProviders()
    {
        return this.pocketProviders.Count > 0;
    }

    private IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
    {
        foreach (IPocketProvider provider in this.pocketProviders)
        {
            foreach (PocketInventory inventory in provider.GetPocketInventories(player))
                yield return inventory;
        }
    }

    private static bool GetBoolProperty(object instance, string name)
    {
        PropertyInfo? property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return property?.GetValue(instance) is bool value && value;
    }

    private bool TryUsePocketProxy(Farmer player, Predicate<Item> predicate, bool playSound = false)
    {
        if (!this.HasPocketProviders())
            return false;

        TemporarySlot? temporarySlot = null;

        foreach (PocketInventory pocketInventory in this.GetPocketInventories(player))
        {
            for (int slot = 0; slot < pocketInventory.Items.Count; slot++)
            {
                Item? pocketItem = pocketInventory.Items[slot];
                if (pocketItem is null || !predicate(pocketItem))
                    continue;

                temporarySlot ??= CreateTemporaryInventorySlot(player);
                if (temporarySlot is null)
                    return false;

                Item proxyItem = CreateProxyItem(pocketItem);
                int previousSlot = player.CurrentToolIndex;
                int proxySlot = temporarySlot.Value.Slot;

                player.Items.Add(proxyItem);
                player.CurrentToolIndex = proxySlot;

                this.pendingProxyItems.RemoveAll(proxy => proxy.PlayerId == player.UniqueMultiplayerID && ReferenceEquals(proxy.ProxyItem, proxyItem));
                this.pendingProxyItems.Add(new PendingProxyItem(player.UniqueMultiplayerID, previousSlot, proxySlot, temporarySlot.Value.OriginalMaxItems, temporarySlot.Value.OriginalItemCount, pocketInventory.Items, slot, pocketItem, proxyItem));

                if (playSound)
                    Game1.playSound("toolSwap");

                return true;
            }
        }

        return false;
    }

    private static TemporarySlot? CreateTemporaryInventorySlot(Farmer player)
    {
        int originalMaxItems = Math.Max(0, player.maxItems.Value);
        int originalItemCount = player.Items.Count;
        int appendedSlot = player.Items.Count;

        if (player.maxItems.Value <= appendedSlot)
            player.maxItems.Value = appendedSlot + 1;

        return new TemporarySlot(appendedSlot, originalMaxItems, originalItemCount);
    }

    private static Item CreateProxyItem(Item source)
    {
        Item proxy = source.getOne();

        if (source.maximumStackSize() > 1)
            proxy.Stack = Math.Min(Math.Max(1, source.Stack), Math.Max(1, proxy.maximumStackSize()));

        return proxy;
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        if (!Context.IsWorldReady || this.pendingProxyItems.Count == 0)
            return;

        for (int i = this.pendingProxyItems.Count - 1; i >= 0; i--)
        {
            Farmer? player = Game1.GetPlayer(this.pendingProxyItems[i].PlayerId);
            if (player is not null)
                RestoreProxyItem(player, this.pendingProxyItems[i], force: true);

            this.pendingProxyItems.RemoveAt(i);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || this.pendingProxyItems.Count == 0)
            return;

        for (int i = this.pendingProxyItems.Count - 1; i >= 0; i--)
        {
            PendingProxyItem proxy = this.pendingProxyItems[i];
            proxy.Ticks++;

            Farmer? player = Game1.GetPlayer(proxy.PlayerId);
            if (player is null)
            {
                this.pendingProxyItems.RemoveAt(i);
                continue;
            }

            if (proxy.Ticks < 8 || this.IsUseToolInputDown() || !player.canMove)
                continue;

            RestoreProxyItem(player, proxy, force: false);
            this.pendingProxyItems.RemoveAt(i);
        }
    }

    private void ClearBridgeProxies()
    {
        this.pendingProxyItems.Clear();
    }

    private static void RestoreProxyItem(Farmer player, PendingProxyItem proxy, bool force)
    {
        if (proxy.PocketSlot < 0 || proxy.PocketSlot >= proxy.PocketItems.Count)
        {
            RestoreInventorySize(player, proxy);
            return;
        }

        int proxySlot = FindItemSlot(player.Items, proxy.ProxyItem);
        if (proxySlot < 0 || proxySlot >= player.Items.Count)
        {
            RestoreInventorySize(player, proxy);
            return;
        }

        Item? currentProxy = player.Items[proxySlot];
        if (!ReferenceEquals(currentProxy, proxy.ProxyItem))
        {
            if (force && currentProxy is not null && currentProxy.canStackWith(proxy.OriginalPocketItem))
            {
                Item? leftover = player.addItemToInventory(currentProxy);
                if (leftover is null)
                    player.Items[proxySlot] = null;
            }

            RestoreInventorySize(player, proxy);
            return;
        }

        SyncPocketItemFromProxy(proxy, currentProxy);
        player.Items.RemoveAt(proxySlot);

        player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, proxy.OriginalMaxItems - 1));
        RestoreInventorySize(player, proxy);
    }

    private static void RestoreInventorySize(Farmer player, PendingProxyItem proxy)
    {
        int proxySlot = FindItemSlot(player.Items, proxy.ProxyItem);
        if (proxySlot >= 0)
            player.Items.RemoveAt(proxySlot);

        for (int i = player.Items.Count - 1; i >= proxy.OriginalItemCount; i--)
        {
            if (player.Items[i] is null || ReferenceEquals(player.Items[i], proxy.ProxyItem))
                player.Items.RemoveAt(i);
            else
                break;
        }

        player.maxItems.Value = proxy.OriginalMaxItems;
        player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, player.maxItems.Value - 1));
    }

    private static int FindItemSlot(IList<Item?> items, Item target)
    {
        for (int i = 0; i < items.Count; i++)
        {
            if (ReferenceEquals(items[i], target))
                return i;
        }

        return -1;
    }

    private static void SyncPocketItemFromProxy(PendingProxyItem proxy, Item proxyItem)
    {
        Item? pocketItem = proxy.PocketItems[proxy.PocketSlot];
        if (!ReferenceEquals(pocketItem, proxy.OriginalPocketItem))
            return;

        if (proxy.OriginalPocketItem.maximumStackSize() > 1)
        {
            int consumed = Math.Max(0, proxy.StartingProxyStack - Math.Max(0, proxyItem.Stack));
            if (consumed <= 0)
                return;

            proxy.OriginalPocketItem.Stack -= consumed;
            if (proxy.OriginalPocketItem.Stack <= 0)
                proxy.PocketItems[proxy.PocketSlot] = null;

            return;
        }

        proxy.PocketItems[proxy.PocketSlot] = proxyItem;
    }

    private bool IsUseToolInputDown()
    {
        foreach (InputButton button in Game1.options.useToolButton)
        {
            SButton sButton = button.ToSButton();
            if (sButton != SButton.None && this.Helper.Input.IsDown(sButton))
                return true;
        }

        return false;
    }

    private static bool Contains(IList<Item?> items, int maxItems, Predicate<Item> predicate)
    {
        int count = Math.Min(maxItems, items.Count);
        for (int i = 0; i < count; i++)
        {
            if (items[i] is Item item && predicate(item))
                return true;
        }

        return false;
    }

    private static bool MatchesTool(Item? item, Type toolType, string aux, bool anyTool)
    {
        if (item is null)
            return false;

        if (toolType == typeof(MeleeWeapon))
        {
            if (item.GetType() != toolType)
                return false;

            bool isScythe = item.Name.Contains("Scythe", StringComparison.OrdinalIgnoreCase);
            return aux switch
            {
                "Scythe" or "ScytheOnly" => isScythe,
                _ => !isScythe
            };
        }

        return item.GetType() == toolType || anyTool && item is Axe or Pickaxe or Hoe;
    }

    private static bool MatchesToolSmartSwitch(Item? item, Type? type)
    {
        if (item is not Tool tool)
            return false;

        if (type is null)
            return tool.GetType() == typeof(MeleeWeapon) && ((MeleeWeapon)tool).isScythe();

        if (type == typeof(MeleeWeapon))
            return tool.GetType() == typeof(MeleeWeapon) && !((MeleeWeapon)tool).isScythe();

        return tool.GetType() == type;
    }

    private static bool MatchesItem(Item? item, string category, string itemName, string crops, int aux)
    {
        if (item is null)
            return false;

        return category switch
        {
            "Trash" or "Fertilizer" => item.Category == aux && !item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase),
            "Minerals" => item.Category == -15 && item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase) && item.Stack >= 5,
            "Cask" => item.Category == -26 && (item.Name.Contains("Cheese", StringComparison.OrdinalIgnoreCase) || item.Name.Contains("Wine", StringComparison.OrdinalIgnoreCase)),
            "Seed" => item.Category == -74 && !item.HasContextTag("tree_seed_item"),
            "Crops" => MatchesCrop(item, crops),
            "Dehydratable" => MatchesDehydratable(item, crops),
            "Oil Maker" => IsOilMakerIngredient(item),
            _ => item.Category == aux && item.Name.Contains(itemName, StringComparison.OrdinalIgnoreCase)
        };
    }

    private static bool MatchesCrop(Item item, string crops)
    {
        bool canFruit = crops is "Both" or "Fruit";
        bool canVegetable = crops is "Both" or "Vegetable";
        return canFruit && item.Category == -79 || canVegetable && item.Category == -75;
    }

    private static bool MatchesDehydratable(Item item, string crops)
    {
        bool canFruit = crops is "Both" or "Fruit";
        bool canMushroom = crops is "Both" or "Mushroom";
        return canFruit && item.Category == -79 || canMushroom && item.Category == -81 && item.Name != "Red Mushroom";
    }

    private static bool IsOilMakerIngredient(Item item)
    {
        return item.Name == "Truffle" && item.Category == -17
            || item.Name == "Sunflower" && item.Category == -80
            || item.Name == "Sunflower Seeds" && item.Category == -74
            || item.Name == "Corn" && item.Category == -75;
    }

    private readonly record struct TemporarySlot(int Slot, int OriginalMaxItems, int OriginalItemCount);

    private sealed class PendingProxyItem
    {
        public PendingProxyItem(long playerId, int previousSlot, int proxySlot, int originalMaxItems, int originalItemCount, IList<Item?> pocketItems, int pocketSlot, Item originalPocketItem, Item proxyItem)
        {
            this.PlayerId = playerId;
            this.PreviousSlot = previousSlot;
            this.ProxySlot = proxySlot;
            this.OriginalMaxItems = originalMaxItems;
            this.OriginalItemCount = originalItemCount;
            this.PocketItems = pocketItems;
            this.PocketSlot = pocketSlot;
            this.OriginalPocketItem = originalPocketItem;
            this.ProxyItem = proxyItem;
            this.StartingProxyStack = Math.Max(0, proxyItem.Stack);
        }

        public long PlayerId { get; }
        public int PreviousSlot { get; }
        public int ProxySlot { get; }
        public int OriginalMaxItems { get; }
        public int OriginalItemCount { get; }
        public IList<Item?> PocketItems { get; }
        public int PocketSlot { get; }
        public Item OriginalPocketItem { get; }
        public Item ProxyItem { get; }
        public int StartingProxyStack { get; }
        public int Ticks { get; set; }
    }

    private sealed class PocketInventory
    {
        public PocketInventory(IList<Item?> items)
        {
            this.Items = items;
        }

        public IList<Item?> Items { get; }
    }

    private interface IPocketProvider
    {
        IEnumerable<PocketInventory> GetPocketInventories(Farmer player);
    }

    private sealed class PocketsBridge : IPocketProvider
    {
        private readonly IMonitor monitor;
        private readonly PropertyInfo pocketDictProperty;
        private readonly FieldInfo configField;
        private readonly FieldInfo inventoryDictField;
        private readonly FieldInfo defaultInventoryDictField;
        private readonly MethodInfo makeInventoryFromXmlMethod;

        private PocketsBridge(IMonitor monitor, PropertyInfo pocketDictProperty, FieldInfo configField, FieldInfo inventoryDictField, FieldInfo defaultInventoryDictField, MethodInfo makeInventoryFromXmlMethod)
        {
            this.monitor = monitor;
            this.pocketDictProperty = pocketDictProperty;
            this.configField = configField;
            this.inventoryDictField = inventoryDictField;
            this.defaultInventoryDictField = defaultInventoryDictField;
            this.makeInventoryFromXmlMethod = makeInventoryFromXmlMethod;
        }

        public static PocketsBridge? TryCreate(IMonitor monitor)
        {
            Type? type = AccessTools.TypeByName("Pockets.ModEntry");
            if (type is null)
                return null;

            PropertyInfo? pocketDict = AccessTools.Property(type, "PocketDict");
            FieldInfo? config = AccessTools.Field(type, "Config");
            FieldInfo? inventoryDict = AccessTools.Field(type, "InventoryDict");
            FieldInfo? defaultInventoryDict = AccessTools.Field(type, "DefaultInventoryDict");
            MethodInfo? makeInventoryFromXml = AccessTools.Method(type, "MakeInventoryFromXML");

            if (pocketDict is null || config is null || inventoryDict is null || defaultInventoryDict is null || makeInventoryFromXml is null)
                return null;

            return new PocketsBridge(monitor, pocketDict, config, inventoryDict, defaultInventoryDict, makeInventoryFromXml);
        }

        public IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
        {
            foreach (PocketSource source in this.GetPocketSources(player))
            {
                IDictionary? inventoryMap = this.GetOrCreateInventoryMap(source);
                if (inventoryMap is null)
                    continue;

                foreach (PocketDefinition definition in source.Definitions)
                {
                    if (!inventoryMap.Contains(definition.Id))
                        inventoryMap[definition.Id] = new Inventory();

                    if (inventoryMap[definition.Id] is not IList<Item?> inventory)
                        continue;

                    while (inventory.Count < definition.Slots)
                        inventory.Add(null);

                    yield return new PocketInventory(inventory);
                }
            }
        }

        private IEnumerable<PocketSource> GetPocketSources(Farmer player)
        {
            object? config = this.configField.GetValue(null);
            bool globalDefaultPockets = GetProperty<bool>(config, "GlobalDefaultPockets");
            List<PocketDefinition> defaultDefinitions = this.GetDefaultDefinitions(config).ToList();

            if (player.pantsItem.Value is Clothing pants)
            {
                List<PocketDefinition> definitions = this.GetDefinitionsForItem(pants).ToList();
                if (definitions.Count > 0)
                    yield return PocketSource.ForClothing(pants, definitions);
                else if (globalDefaultPockets)
                    yield return PocketSource.ForDefault(player.UniqueMultiplayerID, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
                else
                    yield return PocketSource.ForClothing(pants, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
            }

            if (player.shirtItem.Value is Clothing shirt)
            {
                List<PocketDefinition> definitions = this.GetDefinitionsForItem(shirt).ToList();
                if (definitions.Count > 0)
                    yield return PocketSource.ForClothing(shirt, definitions);
                else if (globalDefaultPockets)
                    yield return PocketSource.ForDefault(player.UniqueMultiplayerID, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.SHIRT).ToList());
                else
                    yield return PocketSource.ForClothing(shirt, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.SHIRT).ToList());
            }
        }

        private IEnumerable<PocketDefinition> GetDefinitionsForItem(Item item)
        {
            if (this.pocketDictProperty.GetValue(null) is not IDictionary pocketDict || !pocketDict.Contains(item.ItemId) || pocketDict[item.ItemId] is not IDictionary itemDefinitions)
                yield break;

            foreach (DictionaryEntry entry in itemDefinitions)
            {
                if (entry.Key is string id && TryReadDefinition(id, entry.Value, out PocketDefinition definition))
                    yield return definition;
            }
        }

        private IEnumerable<PocketDefinition> GetDefaultDefinitions(object? config)
        {
            object? defaults = GetPropertyObject(config, "DefaultPockets");
            if (defaults is not IDictionary defaultMap)
                yield break;

            foreach (DictionaryEntry entry in defaultMap)
            {
                if (entry.Key is string id && TryReadDefinition(id, entry.Value, out PocketDefinition definition))
                    yield return definition;
            }
        }

        private static bool TryReadDefinition(string id, object? data, out PocketDefinition definition)
        {
            definition = default;
            if (data is null)
                return false;

            int slots = Math.Max(1, GetProperty<int>(data, "PocketSlots"));
            object? clothesTypeObject = GetPropertyObject(data, "ClothesType");
            Clothing.ClothesType clothesType = clothesTypeObject is Clothing.ClothesType typed ? typed : Clothing.ClothesType.PANTS;
            definition = new PocketDefinition(id, clothesType, slots);
            return true;
        }

        private IDictionary? GetOrCreateInventoryMap(PocketSource source)
        {
            IDictionary? root = source.ClothingItem is not null
                ? this.inventoryDictField.GetValue(null) as IDictionary
                : this.defaultInventoryDictField.GetValue(null) as IDictionary;

            if (root is null)
                return null;

            object key = source.ClothingItem is not null
                ? source.ClothingItem
                : source.PlayerId;

            if (!root.Contains(key))
                root[key] = this.LoadInventoryMap(source);

            return root[key] as IDictionary;
        }

        private IDictionary LoadInventoryMap(PocketSource source)
        {
            Dictionary<string, Inventory> map = new();
            string? serialized = null;

            if (source.ClothingItem is not null)
                source.ClothingItem.modData.TryGetValue(PocketsModDataKey, out serialized);
            else
            {
                Farmer? player = Game1.GetPlayer(source.PlayerId);
                if (player is not null)
                    player.modData.TryGetValue(PocketsModDataKey, out serialized);
            }

            if (!string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    Dictionary<string, string>? xmlByPocket = JsonSerializer.Deserialize<Dictionary<string, string>>(serialized);
                    if (xmlByPocket is not null)
                    {
                        foreach (KeyValuePair<string, string> pair in xmlByPocket)
                        {
                            int max = source.Definitions.FirstOrDefault(def => def.Id == pair.Key).Slots;
                            if (max <= 0)
                                max = 999;

                            if (this.makeInventoryFromXmlMethod.Invoke(null, new object[] { pair.Value, max }) is Inventory inventory)
                                map[pair.Key] = inventory;
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.monitor.Log($"Could not read a Pockets inventory while preparing tool swap compatibility: {ex.Message}", LogLevel.Warn);
                }
            }

            return map;
        }

        private static T GetProperty<T>(object? instance, string name)
        {
            object? value = GetPropertyObject(instance, name);
            return value is T typed ? typed : default!;
        }

        private static object? GetPropertyObject(object? instance, string name)
        {
            return instance?.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(instance);
        }

        private readonly record struct PocketDefinition(string Id, Clothing.ClothesType ClothesType, int Slots);

        private sealed class PocketSource
        {
            private PocketSource(Item? clothingItem, long playerId, List<PocketDefinition> definitions)
            {
                this.ClothingItem = clothingItem;
                this.PlayerId = playerId;
                this.Definitions = definitions;
            }

            public Item? ClothingItem { get; }
            public long PlayerId { get; }
            public List<PocketDefinition> Definitions { get; }

            public static PocketSource ForClothing(Item clothingItem, List<PocketDefinition> definitions)
            {
                return new PocketSource(clothingItem, 0, definitions);
            }

            public static PocketSource ForDefault(long playerId, List<PocketDefinition> definitions)
            {
                return new PocketSource(null, playerId, definitions);
            }
        }
    }
}
