using System;
using System.Collections.Generic;

namespace Leclair.Stardew.BetterCrafting;

public interface IBetterCrafting
{
    void CreateDefaultCategory(bool cooking, string categoryId, Func<string> name, IEnumerable<string>? recipeNames = null, string? iconRecipe = null, bool useRules = false, IEnumerable<IDynamicRuleData>? rules = null);

    void AddRecipesToDefaultCategory(bool cooking, string categoryId, IEnumerable<string> recipeNames);

    void RemoveRecipesFromDefaultCategory(bool cooking, string categoryId, IEnumerable<string> recipeNames);
}

public interface IDynamicRuleData
{
}
