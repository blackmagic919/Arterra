
using System;
using System.Collections.Generic;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Material;

[Serializable]
[CreateAssetMenu(menuName = "Generation/MaterialData/Category")]
public class MaterialCategory : Category<MaterialData>
{
    public Option<List<Option<Category<MaterialData>>>> Children;
    protected override Option<List<Option<Category<MaterialData>>>>? GetChildren() => Children;
    protected override void SetChildren(Option<List<Option<Category<MaterialData>>>> value) => Children = value;
}
