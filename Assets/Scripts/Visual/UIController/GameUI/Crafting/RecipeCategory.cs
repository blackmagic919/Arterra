
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Data.Intrinsic{

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Crafting/Category")]
    public class RecipeCategory : Category<CraftingAuthoring>
    {
        public Option<List<Option<Category<CraftingAuthoring>>>> Children;
        protected override Option<List<Option<Category<CraftingAuthoring>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CraftingAuthoring>>>> value) => Children = value;
}

}