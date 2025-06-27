using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using WorldConfig;


[Serializable]
public class ToolTag : TagRegistry.IProperty
{
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float TerraformSpeed;
    /// <summary> Scales how much a tool is damaged when removing this material. Damaging
    /// a tool decreases its durability </summary>
    public float ToolDamage = 0;
    /// <summary> Whether or not the material removed by this tool will give 
    /// the player the corresponding item. </summary>
    public bool GivesItem;
    public object Clone()
    {
        return new ToolTag
        {
            TerraformSpeed = TerraformSpeed,
            GivesItem = GivesItem
        };
    }
}

[Serializable]
public struct TagRegistry
{
    public static readonly IProperty[] TagTemplates = new IProperty[] {
        null, new ToolTag(), new ToolTag(), new ToolTag(), new ToolTag()
    };
    public enum Tags
    {
        //Tools
        None = 0, BareHand = 1, Axe = 2, Shovel = 3, Pickaxe = 4
    }

    public interface IProperty
    {
        public object Clone();
    }

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
            Index.TryAdd(Reg.value[i].Tag, i);
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
                if (pair.value.value != null && pair.value.value.GetType() == TagTemplates[(int)tag].GetType())
                    continue;
                pair.value.value = TagTemplates[(int)tag].Clone() as IProperty;

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

    public IProperty Retrieve(Tags tag)
    {
        return Reg.value[Index[tag]].value.value;
    }
    public IProperty Retrieve(int index)
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
    public void Add(Tags tag, IProperty value)
    {
        Reg.value ??= new List<Pair>();
        Index ??= new Dictionary<Tags, int>();

        Reg.value.Add(new Pair { Tag = tag, value = new ReferenceOption<IProperty> { value = value } });
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

    public bool TrySet(Tags tag, IProperty value)
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
        public ReferenceOption<IProperty> value;

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
