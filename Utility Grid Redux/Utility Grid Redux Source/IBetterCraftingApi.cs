namespace Leclair.Stardew.BetterCrafting;

public interface IBetterCrafting
{
    void CreateDefaultCategory(bool cooking, string categoryId, Func<string> name, IEnumerable<string>? recipeNames = null, string? iconRecipe = null, bool useRules = false, IEnumerable<IDynamicRuleData>? rules = null);

    void AddRecipesToDefaultCategory(bool cooking, string categoryId, IEnumerable<string> recipeNames);
}

public interface IDynamicRuleData
{
}
