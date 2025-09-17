using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using WorldConfig;


[Serializable]
public class ToolTag : ICloneable {
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float TerraformSpeed;
    /// <summary> Scales how much a tool is damaged when removing this material. Damaging
    /// a tool decreases its durability. If this is a material, this usually scales
    /// the amount of material used up instead </summary>
    public float ToolDamage = 0;
    /// <summary> Whether or not the material removed by this tool will give 
    /// the player the corresponding item. </summary>
    public bool GivesItem;
    public virtual object Clone() {
        return new ToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem
        };
    }
}


[Serializable]
public class ConvertibleTag : IMaterialConverting {
    public WorldConfig.Generation.Structure.StructureData.CheckInfo _convertBounds;
    /// <summary> The <see cref="MapStorage.MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
    public WorldConfig.Generation.Structure.StructureData.CheckInfo _neighborBounds;
    [JsonIgnore]
    public WorldConfig.Generation.Structure.StructureData.CheckInfo ConvertBounds => _convertBounds;
    [JsonIgnore]
    public WorldConfig.Generation.Structure.StructureData.CheckInfo NeighborBounds => _neighborBounds;

    public bool GivesItem;

    public virtual object Clone() {
        return new ConvertibleTag {
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
            GivesItem = GivesItem
        };
    }
}

[Serializable]
public class ConvertibleToolTag : ToolTag, IMaterialConverting {
    public WorldConfig.Generation.Structure.StructureData.CheckInfo _convertBounds;
    /// <summary> The <see cref="MapStorage.MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
    public WorldConfig.Generation.Structure.StructureData.CheckInfo _neighborBounds;
    [JsonIgnore]
    public WorldConfig.Generation.Structure.StructureData.CheckInfo ConvertBounds => _convertBounds;
    [JsonIgnore]
    public WorldConfig.Generation.Structure.StructureData.CheckInfo NeighborBounds => _neighborBounds;

    public override object Clone() {
        return new ConvertibleToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem,
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
        };
    }
}

[Serializable]
public class ConverterToolTag : ConvertibleToolTag {
    [RegistryReference("Materials")]
    public string ConvertTarget;
    public override object Clone() {
        return new ConverterToolTag {
            TerraformSpeed = TerraformSpeed,
            ToolDamage = ToolDamage,
            GivesItem = GivesItem,
            _convertBounds = _convertBounds,
            _neighborBounds = _neighborBounds,
            ConvertTarget = ConvertTarget
        };
    }
}


public interface IMaterialConverting : ICloneable {
    public WorldConfig.Generation.Structure.StructureData.CheckInfo ConvertBounds { get; }
    public WorldConfig.Generation.Structure.StructureData.CheckInfo NeighborBounds { get; }

    static readonly Unity.Mathematics.int3[] dP = new Unity.Mathematics.int3[6]{
        new (0,1,0),
        new (0,-1,0),
        new (1,0,0),
        new (0,0,-1),
        new (-1,0,0),
        new (0,0,1),
    };
    public static bool CanConvert<T>(MapStorage.MapData neighbor, Unity.Mathematics.int3 GCoord, TagRegistry.Tags tag, out T TagInfo)
        where T : class, IMaterialConverting {
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        TagInfo = null;

        if (matInfo.GetMostSpecificTag(tag, neighbor.material, out object prop))
            TagInfo = prop as T; //matInfo.RetrieveIndex((prop as ConverterToolTag).ConvertTarget);
        else return false;
        if (!TagInfo.ConvertBounds.Contains(neighbor)) return false;
        //No neighbor bounds, so always valid
        if (TagInfo.NeighborBounds.IsNull) return true;
        for (int i = 0; i < 6; i++) {
            MapStorage.MapData nNeighbor = MapStorage.CPUMapManager.SampleMap(GCoord + dP[i]);
            if (TagInfo.NeighborBounds.Contains(nNeighbor)) return true;
        }
        return false;
    }
}

[Serializable]
public struct TagRegistry
{
    public static readonly Dictionary<Tags, ICloneable> TagTemplates = new(){
        //Tool Tags
        { Tags.None, null },
        { Tags.BareHand, new ToolTag() },
        { Tags.Axe, new ToolTag() },
        { Tags.Shovel, new ToolTag() },
        { Tags.Pickaxe, new ToolTag() },
        { Tags.Hoe, new ToolTag() },
        //Converter Tags
        { Tags.Flammable, new ConverterToolTag() },
        { Tags.Tillable, new ConverterToolTag() },
        { Tags.Seedable, new ConvertibleToolTag() },
        //Convertable Tags
        { Tags.Grassy, new ConvertibleTag() },
        { Tags.Vegetative, new ConvertibleTag() },
        { Tags.AquaMicrobial, new ConvertibleTag() },
        //Interaction Type
        { Tags.FocusedPlace, null }
    };

    public enum Tags {
        //Tools
        None = 0, BareHand = 1, Axe = 2, Shovel = 3, Pickaxe = 4, Hoe = 5,
        //Converters
        Flammable = 1000, Tillable = 1001, Seedable = 1002,
        //Convertables
        Grassy = 2000, Vegetative = 2001, AquaMicrobial = 2002,
        //Interactions
        FocusedPlace = 9000,
    }//

    public Option<List<Pair>> Reg;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    private Dictionary<Tags, int> Index;

    public void Construct()
    {
        Index = new Dictionary<Tags, int>();
        Reg.value ??= new List<Pair>();
        for (int i = 0; i < Reg.value.Count; i++)
        {
            Index.TryAdd(Reg.value[i].Tag, i);//
        }
    }

    public void OnValidate()
    {
        if (Reg.value == null) return;
        for (int i = 0; i < Reg.value.Count; i++)
        {
            Tags tag = Reg.value[i].Tag;
            var pair = Reg.value[i];
            if (tag == Tags.None) pair.value.value = null;
            else
            {
                if (pair.value.value != null && pair.value.value.GetType() == TagTemplates[tag]?.GetType())
                    continue;
                pair.value.value = TagTemplates[tag]?.Clone() as ICloneable;

            } Reg.value[i] = pair;//
        }
    }

    public int RetrieveIndex(Tags tag)
    {
        return Index[tag];
    }
    public Tags RetrieveName(int index)
    {
        return Reg.value[index].Tag;
    }

    public ICloneable Retrieve(Tags tag)
    {
        return Reg.value[Index[tag]].value.value;
    }
    public ICloneable Retrieve(int index)
    {
        return Reg.value[index].value.value;
    }
    public bool Contains(Tags tag)
    {
        if (Index == null) return false;
        return Index.ContainsKey(tag);
    }
    public bool Contains(int index)
    {
        return index >= 0 && index < Reg.value.Count;
    }
    public void Add(Tags tag, ICloneable value)
    {
        Reg.value ??= new List<Pair>();
        Index ??= new Dictionary<Tags, int>();

        Reg.value.Add(new Pair { Tag = tag, value = new ReferenceOption<ICloneable> { value = value } });
        Index.Add(tag, Reg.value.Count - 1);
    }

    public bool TryRemove(Tags tag)
    {
        if (Reg.value == null || Index == null) return false;
        if (!Index.ContainsKey(tag)) return false;

        Reg.value.RemoveAt(Index[tag]);
        Index.Remove(tag);
        Construct(); //Rebuild the index
        return true;
    }

    public bool TrySet(Tags tag, ICloneable value)
    {
        if (Reg.value == null || Index == null) return false;
        if (!Index.ContainsKey(tag)) return false;

        int index = Index[tag];
        var tPair = Reg.value[index];
        tPair.value.value = value;
        Reg.value[index] = tPair;
        return true;
    }

    public object Clone()
    {
        return new TagRegistry { Reg = Reg };
    }

    [Serializable]
    public struct Pair : ICloneable
    {
        public Tags Tag;
        [UISetting(Alias = "Value")]
        public ReferenceOption<ICloneable> value;

        public object Clone()
        {
            return new Pair
            {
                Tag = Tag,
                value = value
            };
        }
    }

}
