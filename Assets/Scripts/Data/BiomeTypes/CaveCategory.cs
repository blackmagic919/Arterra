
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;


namespace Arterra.Data.Biome{
    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Biomes/CaveCategory")]
    public class CaveCategory : Category<CInfo<CaveBiome>>
    {
        public Option<List<Option<Category<CInfo<CaveBiome>>>>> Children;
        protected override Option<List<Option<Category<CInfo<CaveBiome>>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CInfo<CaveBiome>>>>> value) => Children = value;
}

}
