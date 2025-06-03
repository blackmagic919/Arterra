
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

namespace WorldConfig.Generation.Structure{

[Serializable]
[CreateAssetMenu(menuName = "Generation/Structure/Category")]
public class StructureCategory : Category<StructureData>
{
    public Option<List<Option<Category<StructureData>>>> Children;
    protected override Option<List<Option<Category<StructureData>>>>? GetChildren() => Children;
}

}
