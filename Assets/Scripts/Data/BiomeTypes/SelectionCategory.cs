
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;


namespace Arterra.Data.Biome{
    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Biomes/Category")]
    public class SelectionCategory : Category<BiomeVariety>
    {
        public Option<List<Option<Category<BiomeVariety>>>> Children;
        protected override Option<List<Option<Category<BiomeVariety>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<BiomeVariety>>>> value) => Children = value;
}

}
