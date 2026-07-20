using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley.GameData.BigCraftables;
using StardewValley.GameData.Shops;
using ThaleTheGreat.Gatherers.Framework;

namespace ThaleTheGreat.Gatherers.Services;

internal sealed class AssetEditor
{
    private readonly IModHelper Helper;

    internal AssetEditor(IModHelper helper)
    {
        Helper = helper;
    }

    internal void OnAssetRequested(object? sender, AssetRequestedEventArgs e)
    {
        if (e.NameWithoutLocale.IsEquivalentTo("Mods/ThaleTheGreat.Gatherers/HarvestStatueEmpty"))
        {
            e.LoadFromModFile<Texture2D>("assets/Harvest Statue/empty.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Mods/ThaleTheGreat.Gatherers/HarvestStatueFilled"))
        {
            e.LoadFromModFile<Texture2D>("assets/Harvest Statue/filled.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Mods/ThaleTheGreat.Gatherers/ParrotPotEmpty"))
        {
            e.LoadFromModFile<Texture2D>("assets/Parrot Pot/empty.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Mods/ThaleTheGreat.Gatherers/ParrotPotFilled"))
        {
            e.LoadFromModFile<Texture2D>("assets/Parrot Pot/filled.png", AssetLoadPriority.Exclusive);
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/BigCraftables"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, BigCraftableData> data = asset.AsDictionary<string, BigCraftableData>().Data;
                data[ModConstants.HarvestStatueItemId] = new BigCraftableData
                {
                    Name = "Harvest Statue",
                    DisplayName = Helper.Translation.Get("item.harvest-statue.name"),
                    Description = Helper.Translation.Get("item.harvest-statue.description"),
                    Price = 0,
                    Fragility = 0,
                    CanBePlacedOutdoors = false,
                    CanBePlacedIndoors = true,
                    Texture = "Mods/ThaleTheGreat.Gatherers/HarvestStatueEmpty",
                    SpriteIndex = 0,
                    ContextTags = new List<string> { "gatherers_harvest_statue" },
                    CustomFields = new Dictionary<string, string> { ["ThaleTheGreat.Gatherers/StorageKind"] = "HarvestStatue" }
                };

                data[ModConstants.ParrotPotItemId] = new BigCraftableData
                {
                    Name = "Parrot Pot",
                    DisplayName = Helper.Translation.Get("item.parrot-pot.name"),
                    Description = Helper.Translation.Get("item.parrot-pot.description"),
                    Price = 0,
                    Fragility = 0,
                    CanBePlacedOutdoors = true,
                    CanBePlacedIndoors = false,
                    Texture = "Mods/ThaleTheGreat.Gatherers/ParrotPotEmpty",
                    SpriteIndex = 0,
                    ContextTags = new List<string> { "gatherers_parrot_pot" },
                    CustomFields = new Dictionary<string, string> { ["ThaleTheGreat.Gatherers/StorageKind"] = "ParrotPot" }
                };
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/CraftingRecipes"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data[ModConstants.HarvestStatueRecipeKey] = $"74 1 390 350 268 150/Home/{ModConstants.HarvestStatueItemId}/true/null/";
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/mail"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, string> data = asset.AsDictionary<string, string>().Data;
                data[ModConstants.HarvestStatueMailKey] = Helper.Translation.Get("mail.harvest-statue");
            });
            return;
        }

        if (e.NameWithoutLocale.IsEquivalentTo("Data/Shops"))
        {
            e.Edit(asset =>
            {
                IDictionary<string, ShopData> data = asset.AsDictionary<string, ShopData>().Data;
                if (!data.TryGetValue(ModConstants.IslandTraderShopId, out ShopData? shop))
                {
                    Log.Error($"Data/Shops does not contain the expected '{ModConstants.IslandTraderShopId}' shop.");
                    return;
                }

                if (shop.Items.Any(item => item.Id == ModConstants.ParrotPotShopEntryId || item.ItemId == ModConstants.ParrotPotQualifiedId))
                    return;

                shop.Items.Add(new ShopItemData
                {
                    Id = ModConstants.ParrotPotShopEntryId,
                    ItemId = ModConstants.ParrotPotQualifiedId,
                    TradeItemId = "(O)848",
                    TradeItemAmount = 50,
                    Price = 0,
                    AvailableStock = -1,
                    Condition = "PLAYER_HAS_MAIL Host Island_UpgradeHouse Received"
                });
            });
        }
    }
}
