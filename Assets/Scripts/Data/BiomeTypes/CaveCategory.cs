
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Arterra.Configuration.Generation.Biome{
    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Biomes/CaveCategory")]
    public class CaveCategory : Category<CInfo<CaveBiome>>
    {
        public Option<List<Option<Category<CInfo<CaveBiome>>>>> Children;
        protected override Option<List<Option<Category<CInfo<CaveBiome>>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CInfo<CaveBiome>>>>> value) => Children = value;
}

}
