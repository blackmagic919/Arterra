
using System;
using System.Collections.Generic;
using UnityEngine;


namespace Arterra.Config.Generation.Biome{
    [Serializable]
    [CreateAssetMenu(menuName = "Generation/Biomes/SurfaceCategory")]
    public class SurfaceCategory : Category<CInfo<SurfaceBiome>>
    {
        public Option<List<Option<Category<CInfo<SurfaceBiome>>>>> Children;
        protected override Option<List<Option<Category<CInfo<SurfaceBiome>>>>>? GetChildren() => Children;
        protected override void SetChildren(Option<List<Option<Category<CInfo<SurfaceBiome>>>>> value) => Children = value;
}

}
