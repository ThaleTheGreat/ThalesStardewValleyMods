using StardewValley;
using SObject = StardewValley.Object;

namespace ThaleTheGreat.Gatherers.Services;

internal static class ItemFactory
{
    internal static Item CreateObject(string itemId, int stack = 1, int quality = 0)
    {
        string qualifiedId = itemId.StartsWith("(", StringComparison.Ordinal) ? itemId : $"(O){itemId}";
        return ItemRegistry.Create(qualifiedId, stack, quality);
    }

    internal static bool IsFlower(string itemId)
    {
        try
        {
            Item item = CreateObject(itemId);
            return item is SObject obj && obj.Category == SObject.flowersCategory;
        }
        catch
        {
            return false;
        }
    }
}
