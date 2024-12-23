using System;
using System.Collections;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;


[CreateAssetMenu(menuName = "Generation/MaterialData/GenericMat")]
public class GenericMaterial : MaterialData
{
    public override void UpdateMat(int3 GCoord)
    {

    }
}

public struct MaterialItem : InventoryController.ISlot{
    public uint data;
    public static IRegister register => WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
    [JsonIgnore]
    public readonly bool IsStackable => true;
    [JsonIgnore]
    public int Index{
        readonly get => (int)(data >> 15) & 0x7FFF;
        set => data = (data & 0xC0007FFF) | (((uint)value & 0x7FFF) << 15);
    }
    [JsonIgnore]
    public uint Id{
        readonly get => data & 0xFFFF8000;
        set => data = (data & 0x7FFF) | (value & 0xFFFF8000);
    }
    [JsonIgnore]
    public float Amount{
        readonly get => (data & 0x7FFF) / (float)0xFF;
        set => data = (data & 0xFFFF8000) | (((uint)Mathf.Round(value * 0xFF)) & 0x7FFF);
    }
    [JsonIgnore]
    public int AmountRaw{
        readonly get => (int)(data & 0x7FFF);
        set => data = (data & 0xFFFF8000) | ((uint)value & 0x7FFF);
    }

    //Slot-Type Specific Accessors
    [JsonIgnore]
    public bool IsSolid{
        readonly get => (data & 0x40000000) != 0;
        set => data = value ? data | 0x40000000 : data & 0xBFFFFFFF;
    }
    [JsonIgnore]
    public bool IsLiquid => !IsSolid;
    [JsonIgnore]
    public int TextureIndex => Index;

    public void Serialize(Func<string, int> lookup){
        Index = lookup(register.RetrieveName(Index));
    }

    public void Deserialize(Func<int, string> lookup){
        Index = register.RetrieveIndex(lookup(Index));
    }

    public object Clone()
    {
        return new MaterialItem{data = data};
    }
}
