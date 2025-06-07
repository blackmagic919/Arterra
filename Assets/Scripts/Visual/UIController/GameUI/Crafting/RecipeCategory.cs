
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

namespace WorldConfig.Intrinsic{

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Crafting/Category")]
    public class RecipeCategory : Category<CraftingRecipe>
    {
        public Option<List<Option<Category<CraftingRecipe>>>> Children;
        protected override Option<List<Option<Category<CraftingRecipe>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CraftingRecipe>>>> value) => Children = value;
}

}