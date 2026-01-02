
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Configuration.Generation.Structure{

    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Structure/Category")]
    public class StructureCategory : Category<StructureData>
    {
        public Option<List<Option<Category<StructureData>>>> Children;
        protected override Option<List<Option<Category<StructureData>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<StructureData>>>> value) => Children = value;
}

}
