
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;


namespace WorldConfig.Generation.Biome{
[Serializable]
[CreateAssetMenu(menuName = "Generation/Biomes/CaveCategory")]
public class CaveCategory : Category<CInfo<CaveBiome>>
{
    public Option<List<Option<Category<CInfo<CaveBiome> > > > > Children;
    protected override Option<List<Option<Category<CInfo<CaveBiome> > > > >? GetChildren() => Children;
}

}
