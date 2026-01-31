
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Data.Item{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Items/Category")]
    public class ItemCategory : Category<Authoring>
    {
        public Option<List<Option<Category<Authoring>>>> Children;
        protected override Option<List<Option<Category<Authoring>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<Authoring>>>> value) => Children = value;
}

}
