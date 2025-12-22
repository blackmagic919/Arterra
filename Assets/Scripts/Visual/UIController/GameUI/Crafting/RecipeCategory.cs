
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Config.Intrinsic{

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Crafting/Category")]
    public class RecipeCategory : Category<CraftingAuthoring>
    {
        public Option<List<Option<Category<CraftingAuthoring>>>> Children;
        protected override Option<List<Option<Category<CraftingAuthoring>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CraftingAuthoring>>>> value) => Children = value;
}

}