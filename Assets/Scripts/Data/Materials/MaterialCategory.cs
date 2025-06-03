
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

[Serializable]
[CreateAssetMenu(menuName = "Generation/MaterialData/Category")]
public class MaterialCategory : Category<MaterialData>
{
    public Option<List<Option<Category<MaterialData> > > > Children;
    protected override Option<List<Option<Category<MaterialData> > > >? GetChildren() => Children;
}
