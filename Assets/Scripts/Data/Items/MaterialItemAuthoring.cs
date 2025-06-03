using UnityEngine;
using Newtonsoft.Json;
using System;
using WorldConfig;

namespace WorldConfig.Generation.Item{
[CreateAssetMenu(menuName = "Generation/Items/Material")] 
public class MaterialItemAuthoring : AuthoringTemplate<MaterialItem> {}

[System.Serializable]
public struct MaterialItem : IItem{
    public uint data;
    private static Registry<Authoring> ItemRegistry => Config.CURRENT.Generation.Items;
    private static Registry<TextureContainer> TextureAtlas => Config.CURRENT.Generation.Textures;
    [JsonIgnore]
    public readonly bool IsStackable => true;
    [JsonIgnore]
    public readonly int TexIndex => TextureAtlas.RetrieveIndex(ItemRegistry.Retrieve(Index).TextureName);

    [JsonIgnore]
    public int Index{
        readonly get => (int)(data >> 16) & 0x7FFF;
        set => data = (data & 0x8000FFFF) | (((uint)value & 0x7FFF) << 16);
    }
    [JsonIgnore]
    public string Display{
        readonly get => ((data & 0xFFFF) / (float)0xFF).ToString();
        set => data = (data & 0xFFFF0000) | (((uint)Mathf.Round(uint.Parse(value) * 0xFF)) & 0xFFFF);
    }
    [JsonIgnore]
    public int AmountRaw{
        readonly get => (int)(data & 0xFFFF);
        set => data = (data & 0xFFFF0000) | ((uint)value & 0xFFFF);
    }
    [JsonIgnore]
    public bool IsDirty{
        readonly get => (data & 0x80000000) != 0;
        set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
    }
    public IRegister GetRegistry() => Config.CURRENT.Generation.Items;
    public object Clone()
    {
        return new MaterialItem{data = data};
    }

    public readonly void OnEnterSecondary(){} 
    public readonly void OnLeaveSecondary(){}
    public readonly void OnEnterPrimary(){} 
    public readonly void OnLeavePrimary(){} 
    public readonly void OnSelect(){} 
    public readonly void OnDeselect(){} 
    public readonly void UpdateEItem(){} 
}
}
