using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.ItemTypeDefinitions;
using StardewValley.Menus;
using StardewValley.Locations;
using StardewValley.GameData.Powers;
using Netcode;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.WalletAutoPetter;

public sealed class ModEntry : Mod
{
    private const string StateKey = "WalletAutoPetter.State";
    private const string AutoPetterQualifiedId = "(BC)272";
    private const int AutoPetterBigCraftableId = 272;
    private const string WalletFlagKey = "ThaleTheGreat.WalletAutoPetter/HasAutoPetter";
    private const string WalletPowerId = "ThaleTheGreat.WalletAutoPetter_AutoPetter";
    private const string PowerCategoryId = "ThaleTheGreat.WalletAutoPetter";
    private const string PowerIconAssetPath = "Mods/ThaleTheGreat.WalletAutoPetter/AutoPetterInventoryIcon";

    private ModConfig Config = null!;
    private SaveState State = new();
    private bool SuppressNextStoreNotice;
    private ISpecialPowerAPI? SpecialPowerApi;

    public override void Entry(IModHelper helper)
    {
        Config = helper.ReadConfig<ModConfig>();

        helper.Events.Content.AssetRequested += OnAssetRequested;
        helper.Events.GameLoop.GameLaunched += OnGameLaunched;
        helper.Events.GameLoop.SaveLoaded += OnSaveLoaded;
        helper.Events.GameLoop.DayStarted += OnDayStarted;
        helper.Events.GameLoop.Saving += OnSaving;
        helper.Events.GameLoop.ReturnedToTitle += OnReturnedToTitle;
        helper.Events.Player.InventoryChanged += OnInventoryChanged;
        helper.Events.Input.ButtonPressed += OnButtonPressed;
        helper.Events.Display.RenderedActiveMenu += OnRenderedActiveMenu;
    }


    private void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo(PowerIconAssetPath))
        {
            e.LoadFrom(CreateAutoPetterInventoryIconTexture, AssetLoadPriority.Exclusive);
            return;
        }

        if (!e.NameWithoutLocale.IsEquivalentTo("Data/Powers"))
            return;

        e.Edit(asset =>
        {
            IDictionary<string, PowersData> powers = asset.AsDictionary<string, PowersData>().Data;

            if (!Context.IsWorldReady || !HasWalletAutoPetter())
            {
                powers.Remove(WalletPowerId);
                return;
            }

            (string texturePath, Point texturePosition, Point _) = GetAutoPetterPowerTexture();
            powers[WalletPowerId] = new PowersData
            {
                DisplayName = GetAutoPetterDisplayName(),
                Description = GetAutoPetterDescription(),
                TexturePath = texturePath,
                TexturePosition = texturePosition,
                UnlockedCondition = $"PLAYER_MOD_DATA Current {WalletFlagKey} true"
            };
        });
    }

    private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
    {
        RegisterGmcm();
        RegisterSpecialPowerUtilities();
    }

    private void OnSaveLoaded(object? sender, SaveLoadedEventArgs e)
    {
        State = Helper.Data.ReadSaveData<SaveState>(StateKey) ?? new SaveState();
        MigrateStoredAutoPetterCount();
        SyncWalletFlagFromState();
        CollectAutoPetterFromInventory(showNotice: false);
        InvalidatePowers();
    }

    private void OnDayStarted(object? sender, DayStartedEventArgs e)
    {
        if (!Context.IsWorldReady)
            return;

        CollectAutoPetterFromInventory(showNotice: false);
        InvalidatePowers();

        if (Config.Enabled && HasStoredAutoPetter() && Context.IsMainPlayer)
            ApplyAutoPetterEffect();
    }

    private void OnSaving(object? sender, SavingEventArgs e)
    {
        SaveState();
    }

    private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
    {
        State = new SaveState();
        SuppressNextStoreNotice = false;
    }

    private void OnInventoryChanged(object? sender, InventoryChangedEventArgs e)
    {
        if (!Context.IsWorldReady || !e.IsLocalPlayer || !Config.Enabled || !Config.AutoStoreFromInventory)
            return;

        CollectAutoPetterFromInventory(showNotice: Config.ShowStoredMessage);
    }

    private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || !HasStoredAutoPetter() || !Config.ReturnToInventoryKey.JustPressed())
            return;

        if (!IsInventoryPageOpen())
            return;

        Helper.Input.Suppress(e.Button);
        ReturnAutoPetterToInventory(showNotice: true);
    }

    private void OnRenderedActiveMenu(object? sender, RenderedActiveMenuEventArgs e)
    {
        if (!Context.IsWorldReady || !Config.Enabled || !Config.ShowWalletIcon || !HasStoredAutoPetter() || !IsInventoryPageOpen())
            return;

        DrawWalletIcon(e.SpriteBatch);
    }

    private void CollectAutoPetterFromInventory(bool showNotice)
    {
        if (!Config.Enabled || !Config.AutoStoreFromInventory)
            return;

        Farmer player = Game1.player;

        if (State.SuppressAutoStoreUntilInventoryAutoPetterLeaves)
        {
            if (InventoryHasAutoPetter(player))
                return;

            State.SuppressAutoStoreUntilInventoryAutoPetterLeaves = false;
            SaveState();
        }

        int collected = 0;

        for (int i = 0; i < player.Items.Count; i++)
        {
            Item? item = player.Items[i];
            if (!IsAutoPetter(item))
                continue;

            collected += Math.Max(1, item!.Stack);
            player.Items[i] = null;
        }

        if (collected <= 0)
            return;

        State.AutoPetterCount += collected;
        State.HasAutoPetter = State.AutoPetterCount > 0;
        SetWalletFlag(player, State.HasAutoPetter);
        CollapseTrailingOverflowNulls(player);
        SaveState();
        InvalidatePowers();

        if (showNotice && !SuppressNextStoreNotice)
        {
            string name = GetAutoPetterDisplayName();
            string message = collected == 1
                ? $"{name} moved to wallet."
                : $"{collected} {name}s moved to wallet.";
            Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
        }

        SuppressNextStoreNotice = false;
    }

    private void ReturnAutoPetterToInventory(bool showNotice)
    {
        if (!HasStoredAutoPetter())
            return;

        Item item = ItemRegistry.Create(AutoPetterQualifiedId);
        State.AutoPetterCount = Math.Max(0, State.AutoPetterCount - 1);
        State.HasAutoPetter = State.AutoPetterCount > 0;
        State.SuppressAutoStoreUntilInventoryAutoPetterLeaves = true;
        SuppressNextStoreNotice = true;
        SetWalletFlag(Game1.player, State.HasAutoPetter);
        SaveState();
        InvalidatePowers();

        if (!Game1.player.addItemToInventoryBool(item))
            Game1.player.addItemByMenuIfNecessary(item);

        if (showNotice)
            Game1.addHUDMessage(new HUDMessage($"{GetAutoPetterDisplayName()} returned to inventory.", HUDMessage.newQuest_type));
    }

    private void ReturnAllAutoPettersToInventory(bool showNotice)
    {
        while (HasStoredAutoPetter())
            ReturnAutoPetterToInventory(showNotice: false);

        if (showNotice)
            Game1.addHUDMessage(new HUDMessage($"Stored {GetAutoPetterDisplayName()}s returned to inventory.", HUDMessage.newQuest_type));
    }

    private void ApplyAutoPetterEffect()
    {
        int affected = 0;

        foreach (FarmAnimal animal in EnumerateFarmAnimals())
        {
            if (animal is null || animal.wasPet.Value)
                continue;

            animal.wasPet.Value = true;
            TryMarkAsAutoPet(animal);

            if (Config.ApplyFriendshipGain)
                animal.friendshipTowardFarmer.Value = Math.Min(1000, animal.friendshipTowardFarmer.Value + Math.Max(0, Config.FriendshipPointsPerDay));

            affected++;
        }

    }

    private void TryMarkAsAutoPet(FarmAnimal animal)
    {
        try
        {
            object? value = Helper.Reflection.GetField<object>(animal, "wasAutoPet", required: false).GetValue();
            if (value is NetBool wasAutoPet)
                wasAutoPet.Value = true;
        }
        catch
        {
        }
    }

    private IEnumerable<FarmAnimal> EnumerateFarmAnimals()
    {
        Farm farm = Game1.getFarm();

        foreach (FarmAnimal animal in farm.animals.Values)
            yield return animal;

        foreach (Building building in farm.buildings)
        {
            if (building.indoors.Value is AnimalHouse house)
            {
                foreach (FarmAnimal animal in house.animals.Values)
                    yield return animal;
            }
        }
    }

    private static bool InventoryHasAutoPetter(Farmer player)
    {
        foreach (Item? item in player.Items)
        {
            if (IsAutoPetter(item))
                return true;
        }

        return false;
    }

    private static bool IsAutoPetter(Item? item)
    {
        if (item is null)
            return false;

        if (string.Equals(item.QualifiedItemId, AutoPetterQualifiedId, StringComparison.OrdinalIgnoreCase))
            return true;

        return item is SObject obj && obj.bigCraftable.Value && obj.ParentSheetIndex == AutoPetterBigCraftableId;
    }

    private static void CollapseTrailingOverflowNulls(Farmer player)
    {
        while (player.Items.Count > player.MaxItems && player.Items.Count > 0 && player.Items[player.Items.Count - 1] is null)
            player.Items.RemoveAt(player.Items.Count - 1);
    }

    private bool IsInventoryPageOpen()
    {
        if (Game1.activeClickableMenu is not GameMenu gameMenu)
            return false;

        try
        {
            int currentTab = Helper.Reflection.GetField<int>(gameMenu, "currentTab", required: false).GetValue();
            List<IClickableMenu>? pages = Helper.Reflection.GetField<List<IClickableMenu>>(gameMenu, "pages", required: false).GetValue();

            if (pages is null || currentTab < 0 || currentTab >= pages.Count)
                return false;

            return pages[currentTab].GetType().Name == "InventoryPage";
        }
        catch
        {
            return false;
        }
    }

    private void DrawWalletIcon(SpriteBatch b)
    {
        if (Game1.activeClickableMenu is not GameMenu menu)
            return;

        Item item = CreateAutoPetterItem();
        int size = 64;
        int x = menu.xPositionOnScreen + menu.width - 276;
        int y = menu.yPositionOnScreen + menu.height - 230;
        Rectangle slot = new(x, y, size, size);

        IClickableMenu.drawTextureBox(b, Game1.menuTexture, new Rectangle(0, 256, 60, 60), slot.X, slot.Y, slot.Width, slot.Height, Color.White, 1f, false);
        item.drawInMenu(b, new Vector2(slot.X, slot.Y), 1f, 1f, 0.89f, StackDrawType.Hide, Color.White, drawShadow: true);

        if (slot.Contains(Game1.getMouseX(), Game1.getMouseY()))
        {
            IClickableMenu.drawHoverText(
                b,
                item.DisplayName + "\n" + item.getDescription() + GetStoredCountTooltipLine() + "\nShift + right-click to return one to your inventory.",
                Game1.smallFont
            );
        }
    }


    private void RegisterSpecialPowerUtilities()
    {
        SpecialPowerApi = Helper.ModRegistry.GetApi<ISpecialPowerAPI>("Spiderbuttons.SpecialPowerUtilities");
        if (SpecialPowerApi is null)
            return;

        try
        {
            (string texturePath, Point texturePosition, Point textureSize) = GetAutoPetterPowerTexture();
            SpecialPowerApi.RegisterPowerCategory(
                PowerCategoryId,
                () => GetAutoPetterDisplayName(),
                texturePath,
                texturePosition,
                textureSize
            );
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to register Wallet Auto-Petter Special Power Utilities category: {ex.Message}", LogLevel.Warn);
        }
    }

    private void SetEnabled(bool value)
    {
        bool wasEnabled = Config.Enabled;
        Config.Enabled = value;

        if (wasEnabled && !value && Context.IsWorldReady && HasStoredAutoPetter())
            ReturnAllAutoPettersToInventory(showNotice: false);

        InvalidatePowers();
    }

    private bool HasWalletAutoPetter()
    {
        if (Context.IsWorldReady && Game1.player.modData.TryGetValue(WalletFlagKey, out string? flag) && bool.TryParse(flag, out bool flagValue))
            return flagValue;

        return HasStoredAutoPetter();
    }

    private bool HasStoredAutoPetter()
    {
        return State.AutoPetterCount > 0 || State.HasAutoPetter;
    }

    private string GetStoredCountTooltipLine()
    {
        int count = Math.Max(State.AutoPetterCount, State.HasAutoPetter ? 1 : 0);
        return count > 1 ? $"\nStored: {count}" : string.Empty;
    }

    private void MigrateStoredAutoPetterCount()
    {
        if (State.AutoPetterCount <= 0 && State.HasAutoPetter)
            State.AutoPetterCount = 1;

        State.HasAutoPetter = State.AutoPetterCount > 0;
    }

    private void SyncWalletFlagFromState()
    {
        if (!Context.IsWorldReady)
            return;

        if (State.AutoPetterCount > 0 || State.HasAutoPetter)
        {
            State.AutoPetterCount = Math.Max(1, State.AutoPetterCount);
            State.HasAutoPetter = true;
            SetWalletFlag(Game1.player, true);
        }
        else if (Game1.player.modData.TryGetValue(WalletFlagKey, out string? flag) && bool.TryParse(flag, out bool flagValue) && flagValue)
        {
            State.AutoPetterCount = 1;
            State.HasAutoPetter = true;
        }
        else
        {
            State.AutoPetterCount = 0;
            State.HasAutoPetter = false;
            SetWalletFlag(Game1.player, false);
        }
    }

    private static void SetWalletFlag(Farmer player, bool value)
    {
        if (value)
            player.modData[WalletFlagKey] = "true";
        else
            player.modData.Remove(WalletFlagKey);
    }

    private void InvalidatePowers()
    {
        Helper.GameContent.InvalidateCache("Data/Powers");
    }

    private static Item CreateAutoPetterItem()
    {
        return ItemRegistry.Create(AutoPetterQualifiedId);
    }

    private static string GetAutoPetterDisplayName()
    {
        try
        {
            return CreateAutoPetterItem().DisplayName;
        }
        catch
        {
            return "Auto-Petter";
        }
    }

    private static string GetAutoPetterDescription()
    {
        try
        {
            return CreateAutoPetterItem().getDescription();
        }
        catch
        {
            return "Automatically pets all barn and coop animals each morning.";
        }
    }

    private static (string TexturePath, Point TexturePosition, Point TextureSize) GetAutoPetterPowerTexture()
    {
        return (PowerIconAssetPath, Point.Zero, new Point(16, 16));
    }

    private Texture2D CreateAutoPetterInventoryIconTexture()
    {
        const int renderSize = 128;
        const int inventorySlotSize = 64;
        const int powerIconSize = 16;

        GraphicsDevice graphicsDevice = Game1.graphics.GraphicsDevice;
        RenderTargetBinding[] previousTargets = graphicsDevice.GetRenderTargets();

        RenderTarget2D inventoryRender = new(graphicsDevice, renderSize, renderSize, false, SurfaceFormat.Color, DepthFormat.None);
        RenderTarget2D powerIcon = new(graphicsDevice, powerIconSize, powerIconSize, false, SurfaceFormat.Color, DepthFormat.None);

        try
        {
            Item item = CreateAutoPetterItem();
            Vector2 inventoryPosition = new((renderSize - inventorySlotSize) / 2f, (renderSize - inventorySlotSize) / 2f);
            Rectangle inventorySlotRect = new((renderSize - inventorySlotSize) / 2, (renderSize - inventorySlotSize) / 2, inventorySlotSize, inventorySlotSize);

            graphicsDevice.SetRenderTarget(inventoryRender);
            graphicsDevice.Clear(Color.Transparent);

            using (SpriteBatch b = new(graphicsDevice))
            {
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                item.drawInMenu(b, inventoryPosition, 1f, 1f, 0.89f, StackDrawType.Hide, Color.White, drawShadow: true);
                b.End();
            }

            graphicsDevice.SetRenderTarget(powerIcon);
            graphicsDevice.Clear(Color.Transparent);

            using (SpriteBatch b = new(graphicsDevice))
            {
                b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                b.Draw(inventoryRender, new Rectangle(0, 0, powerIconSize, powerIconSize), inventorySlotRect, Color.White);
                b.End();
            }

            return powerIcon;
        }
        catch (Exception ex)
        {
            Monitor.Log($"Failed to render Auto-Petter power icon from the vanilla inventory draw path: {ex.Message}", LogLevel.Warn);
            Texture2D empty = new(graphicsDevice, powerIconSize, powerIconSize);
            Color[] pixels = new Color[powerIconSize * powerIconSize];
            empty.SetData(pixels);
            return empty;
        }
        finally
        {
            graphicsDevice.SetRenderTargets(previousTargets);
            inventoryRender.Dispose();
        }
    }

    private void RegisterGmcm()
    {
        IGenericModConfigMenuApi? gmcm = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
        if (gmcm is null)
            return;

        gmcm.Register(ModManifest, ResetConfig, SaveConfig);

        gmcm.AddSectionTitle(ModManifest, () => "Wallet Auto-Petter");
        gmcm.AddBoolOption(ModManifest, () => Config.Enabled, SetEnabled, () => "Enabled", () => "Enable Wallet Auto-Petter behavior.");
        gmcm.AddBoolOption(ModManifest, () => Config.AutoStoreFromInventory, value => Config.AutoStoreFromInventory = value, () => "Auto-store from inventory", () => "Automatically move the first Auto-Petter you obtain into the wallet.");
        gmcm.AddBoolOption(ModManifest, () => Config.ShowWalletIcon, value => Config.ShowWalletIcon = value, () => "Show wallet icon", () => "Draw the stored Auto-Petter in the inventory wallet area.");
        gmcm.AddBoolOption(ModManifest, () => Config.ShowStoredMessage, value => Config.ShowStoredMessage = value, () => "Show stored message", () => "Show a HUD message when an Auto-Petter is moved to the wallet.");
        gmcm.AddKeybindList(ModManifest, () => Config.ReturnToInventoryKey, value => Config.ReturnToInventoryKey = value, () => "Return to inventory key", () => "While the inventory page is open, press this keybind to return the wallet Auto-Petter to your inventory.");

        gmcm.AddSectionTitle(ModManifest, () => "Animal Effect");
        gmcm.AddBoolOption(ModManifest, () => Config.ApplyFriendshipGain, value => Config.ApplyFriendshipGain = value, () => "Apply friendship gain", () => "Apply a small daily friendship increase when the wallet Auto-Petter pets animals.");
        gmcm.AddNumberOption(ModManifest, () => Config.FriendshipPointsPerDay, value => Config.FriendshipPointsPerDay = value, () => "Friendship points per day", () => "Daily friendship points added to each affected animal.", min: 0, max: 15, interval: 1);

    }

    private void ResetConfig()
    {
        Config = new ModConfig();
    }

    private void SaveConfig()
    {
        Helper.WriteConfig(Config);
    }

    private void SaveState()
    {
        Helper.Data.WriteSaveData(StateKey, State);
    }

}
