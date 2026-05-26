using System.Collections;
using System.Reflection;
using System.Text.Json;
using HarmonyLib;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Inventories;
using StardewValley.Objects;
using StardewValley.Tools;

namespace ThaleTheGreat.PocketsAutomateToolSwapPatch;

public sealed class ModEntry : Mod
{
    private const string PocketsId = "aedenthorn.Pockets";
    private const string AutomateToolSwapId = "Trapyy.AutomatetoolSwap";
    private const string PocketsModDataKey = "aedenthorn.Pockets/pocket";

    private readonly List<PendingProxyItem> pendingProxyItems = new();
    private Harmony? harmony;
    private PocketsBridge? pockets;

    internal static ModEntry Instance { get; private set; } = null!;

    public override void Entry(IModHelper helper)
    {
        Instance = this;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.UpdateTicked += OnUpdateTicked;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => pendingProxyItems.Clear();
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        if (!Helper.ModRegistry.IsLoaded(PocketsId) || !Helper.ModRegistry.IsLoaded(AutomateToolSwapId))
            return;

        Type? inventoryHandler = AccessTools.TypeByName("Core.InventoryHandler");
        if (inventoryHandler is null)
        {
            Monitor.Log("Could not find Automate Tool Swap's inventory handler. Compatibility patch was not applied.", LogLevel.Warn);
            return;
        }

        pockets = PocketsBridge.TryCreate(Monitor);
        if (pockets is null)
        {
            Monitor.Log("Could not find Pockets internals. Compatibility patch was not applied.", LogLevel.Warn);
            return;
        }

        harmony = new Harmony(ModManifest.UniqueID);
        harmony.Patch(
            original: AccessTools.Method(inventoryHandler, "SetTool"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetToolPrefix))
        );
        harmony.Patch(
            original: AccessTools.Method(inventoryHandler, "SetItem"),
            prefix: new HarmonyMethod(typeof(ModEntry), nameof(SetItemPrefix))
        );
    }

    private static bool SetToolPrefix(Farmer player, Type toolType, string aux = "", bool anyTool = false)
    {
        if (!Context.IsWorldReady || player is null || Instance.pockets is null)
            return true;

        if (Contains(player.Items, player.maxItems.Value, item => MatchesTool(item, toolType, aux, anyTool)))
            return true;

        return !Instance.TryUsePocketProxy(player, item => MatchesTool(item, toolType, aux, anyTool));
    }

    private static bool SetItemPrefix(Farmer player, string category, string item = "", string crops = "Both", int aux = 0)
    {
        if (!Context.IsWorldReady || player is null || Instance.pockets is null)
            return true;

        if (Contains(player.Items, player.maxItems.Value, candidate => MatchesItem(candidate, category, item, crops, aux)))
            return true;

        return !Instance.TryUsePocketProxy(player, candidate => MatchesItem(candidate, category, item, crops, aux));
    }

    private bool TryUsePocketProxy(Farmer player, Predicate<Item> predicate)
    {
        if (pockets is null)
            return false;

        TemporarySlot? temporarySlot = null;

        foreach (PocketInventory pocketInventory in pockets.GetPocketInventories(player))
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

                player.Items[proxySlot] = proxyItem;
                player.CurrentToolIndex = proxySlot;

                pendingProxyItems.RemoveAll(proxy => proxy.PlayerId == player.UniqueMultiplayerID && proxy.ProxySlot == proxySlot);
                pendingProxyItems.Add(new PendingProxyItem(player.UniqueMultiplayerID, previousSlot, proxySlot, temporarySlot.Value.OriginalMaxItems, temporarySlot.Value.OriginalItemCount, pocketInventory.Items, slot, pocketItem, proxyItem));
                return true;
            }
        }

        return false;
    }

    private static TemporarySlot? CreateTemporaryInventorySlot(Farmer player)
    {
        int originalMaxItems = Math.Max(0, player.maxItems.Value);
        int originalItemCount = player.Items.Count;
        int slot = player.Items.Count;

        player.Items.Add(null);
        if (player.maxItems.Value <= slot)
            player.maxItems.Value = slot + 1;

        return new TemporarySlot(slot, originalMaxItems, originalItemCount);
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
        if (!Context.IsWorldReady || pendingProxyItems.Count == 0)
            return;

        for (int i = pendingProxyItems.Count - 1; i >= 0; i--)
        {
            Farmer? player = Game1.GetPlayer(pendingProxyItems[i].PlayerId);
            if (player is not null)
                RestoreProxyItem(player, pendingProxyItems[i], force: true);

            pendingProxyItems.RemoveAt(i);
        }
    }

    private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
    {
        if (!Context.IsWorldReady || pendingProxyItems.Count == 0)
            return;

        for (int i = pendingProxyItems.Count - 1; i >= 0; i--)
        {
            PendingProxyItem proxy = pendingProxyItems[i];
            proxy.Ticks++;

            Farmer? player = Game1.GetPlayer(proxy.PlayerId);
            if (player is null)
            {
                pendingProxyItems.RemoveAt(i);
                continue;
            }

            if (proxy.Ticks < 8 || IsUseToolInputDown() || !player.canMove)
                continue;

            RestoreProxyItem(player, proxy, force: false);
            pendingProxyItems.RemoveAt(i);
        }
    }

    private static void RestoreProxyItem(Farmer player, PendingProxyItem proxy, bool force)
    {
        if (proxy.ProxySlot < 0 || proxy.ProxySlot >= player.Items.Count || proxy.PocketSlot < 0 || proxy.PocketSlot >= proxy.PocketItems.Count)
        {
            RestoreInventorySize(player, proxy);
            return;
        }

        Item? currentProxy = player.Items[proxy.ProxySlot];
        if (!ReferenceEquals(currentProxy, proxy.ProxyItem))
        {
            if (force && currentProxy is not null && currentProxy.canStackWith(proxy.OriginalPocketItem))
            {
                Item? leftover = player.addItemToInventory(currentProxy);
                if (leftover is null)
                    player.Items[proxy.ProxySlot] = null;
            }

            RestoreInventorySize(player, proxy);
            return;
        }

        SyncPocketItemFromProxy(proxy, currentProxy);
        player.Items[proxy.ProxySlot] = null;

        if (player.CurrentToolIndex == proxy.ProxySlot)
            player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, proxy.OriginalMaxItems - 1));

        RestoreInventorySize(player, proxy);
    }

    private static void RestoreInventorySize(Farmer player, PendingProxyItem proxy)
    {
        for (int i = player.Items.Count - 1; i >= proxy.OriginalItemCount; i--)
        {
            if (player.Items[i] is null || ReferenceEquals(player.Items[i], proxy.ProxyItem))
                player.Items.RemoveAt(i);
            else
                break;
        }

        player.maxItems.Value = proxy.OriginalMaxItems;
        if (player.CurrentToolIndex >= player.maxItems.Value)
            player.CurrentToolIndex = Math.Clamp(proxy.PreviousSlot, 0, Math.Max(0, player.maxItems.Value - 1));
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
            if (sButton != SButton.None && Helper.Input.IsDown(sButton))
                return true;
        }

        return Helper.Input.IsDown(SButton.MouseLeft) || Helper.Input.IsDown(SButton.ControllerX);
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
            PlayerId = playerId;
            PreviousSlot = previousSlot;
            ProxySlot = proxySlot;
            OriginalMaxItems = originalMaxItems;
            OriginalItemCount = originalItemCount;
            PocketItems = pocketItems;
            PocketSlot = pocketSlot;
            OriginalPocketItem = originalPocketItem;
            ProxyItem = proxyItem;
            StartingProxyStack = Math.Max(0, proxyItem.Stack);
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
            Items = items;
        }

        public IList<Item?> Items { get; }
    }

    private sealed class PocketsBridge
    {
        private readonly IMonitor monitor;
        private readonly Type modEntryType;
        private readonly PropertyInfo pocketDictProperty;
        private readonly FieldInfo configField;
        private readonly FieldInfo inventoryDictField;
        private readonly FieldInfo defaultInventoryDictField;
        private readonly MethodInfo makeInventoryFromXmlMethod;

        private PocketsBridge(IMonitor monitor, Type modEntryType, PropertyInfo pocketDictProperty, FieldInfo configField, FieldInfo inventoryDictField, FieldInfo defaultInventoryDictField, MethodInfo makeInventoryFromXmlMethod)
        {
            this.monitor = monitor;
            this.modEntryType = modEntryType;
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

            return new PocketsBridge(monitor, type, pocketDict, config, inventoryDict, defaultInventoryDict, makeInventoryFromXml);
        }

        public IEnumerable<PocketInventory> GetPocketInventories(Farmer player)
        {
            foreach (PocketSource source in GetPocketSources(player))
            {
                IDictionary? inventoryMap = GetOrCreateInventoryMap(source);
                if (inventoryMap is null)
                    continue;

                foreach (var definition in source.Definitions)
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
            object? config = configField.GetValue(null);
            bool globalDefaultPockets = GetProperty<bool>(config, "GlobalDefaultPockets");
            List<PocketDefinition> defaultDefinitions = GetDefaultDefinitions(config).ToList();

            if (player.pantsItem.Value is Clothing pants)
            {
                List<PocketDefinition> definitions = GetDefinitionsForItem(pants).ToList();
                if (definitions.Count > 0)
                    yield return PocketSource.ForClothing(pants, definitions);
                else if (globalDefaultPockets)
                    yield return PocketSource.ForDefault(player.UniqueMultiplayerID, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
                else
                    yield return PocketSource.ForClothing(pants, defaultDefinitions.Where(def => def.ClothesType == Clothing.ClothesType.PANTS).ToList());
            }

            if (player.shirtItem.Value is Clothing shirt)
            {
                List<PocketDefinition> definitions = GetDefinitionsForItem(shirt).ToList();
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
            if (pocketDictProperty.GetValue(null) is not IDictionary pocketDict || !pocketDict.Contains(item.ItemId) || pocketDict[item.ItemId] is not IDictionary itemDefinitions)
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

        private bool TryReadDefinition(string id, object? data, out PocketDefinition definition)
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
                ? inventoryDictField.GetValue(null) as IDictionary
                : defaultInventoryDictField.GetValue(null) as IDictionary;

            if (root is null)
                return null;

            object key = source.ClothingItem is not null
                ? source.ClothingItem
                : source.PlayerId;
            if (!root.Contains(key))
                root[key] = LoadInventoryMap(source);

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

                            if (makeInventoryFromXmlMethod.Invoke(null, new object[] { pair.Value, max }) is Inventory inventory)
                                map[pair.Key] = inventory;
                        }
                    }
                }
                catch (Exception ex)
                {
                    monitor.Log($"Could not read a Pockets inventory while preparing Automate Tool Swap compatibility: {ex.Message}", LogLevel.Warn);
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
                ClothingItem = clothingItem;
                PlayerId = playerId;
                Definitions = definitions;
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
