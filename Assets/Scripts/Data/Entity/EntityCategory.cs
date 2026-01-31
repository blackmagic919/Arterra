
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Data.Entity{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Entity/Category")]
    public class EntityCategory : Category<Authoring>
    {
        public Option<List<Option<Category<Authoring>>>> Children;
        protected override Option<List<Option<Category<Authoring>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<Authoring>>>> value) => Children = value;
}

}
