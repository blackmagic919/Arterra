
using System;
using System.Collections.Generic;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

namespace WorldConfig.Generation.Item{

[Serializable]
[CreateAssetMenu(menuName = "Generation/Items/Category")]
public class ItemCategory : Category<Authoring>
{
    public Option<List<Option<Category<Authoring> > > > Children;
    protected override Option<List<Option<Category<Authoring> > > >? GetChildren() => Children;
}

}
