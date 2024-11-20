using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;
using System.Reflection;
using System.Linq;

/*
Rules of Usage:
- Class Types can hold Value Types or Options only
- Options can hold class or value types only
- Value Types can hold value types or options only 

Basically, only options can hold class types :)

Purpose:
- When you change an object, all values(and nested value types)
  associated to it will be saved seperate from the template
- Option represents a lazy-store break-node, where unless the object
  held by the option is changed it is not stored. 
*/

[AttributeUsage(AttributeTargets.Field)]
public sealed class UISetting : Attribute{
    public bool Ignore{get; set;}
    public string Message{get; set;}
    public string Warning{get; set;}
    public string Alias{get; set;}

}

[CreateAssetMenu(menuName = "Generation/WorldOptions")]
public class WorldOptions : ScriptableObject{
    public int seed;
    [UISetting(Alias = "Quality")]
    public Option<QualitySettings> _Quality;
    [UISetting(Alias = "Generation")]
    public Option<GenerationSettings> _Generation;
    [UISetting(Alias = "Gameplay")]
    public Option<GamePlaySettings> _GamePlay;
    [UISetting(Ignore = true)]
    public Option<SystemSettings> _System;
    public QualitySettings Quality => _Quality;

    [JsonIgnore]
    public ref GenerationSettings Generation => ref _Generation.value;
    [JsonIgnore]
    public ref GamePlaySettings GamePlay => ref _GamePlay.value;
    [JsonIgnore]
    public ref SystemSettings System => ref _System.value;

    [Serializable]
    public struct QualitySettings{
        [UISetting(Message = "Improve Performance By Reducing Quality")]
        public Option<AtmosphereBakeSettings> Atmosphere;
        public Option<RenderSettings> Rendering;
        public Option<GeneratorSettings> GeoShaders;
        public Option<MemoryBufferSettings> Memory;
    }

    [Serializable]
    public struct GenerationSettings{
        [UISetting(Message = "Controls How The World Is Generated")]
        public Registry<NoiseData> Noise;
        public Option<MeshCreatorSettings> Terrain;
        public Option<SurfaceCreatorSettings> Surface;
        public Option<BiomeGenerationData> Biomes;
        public Registry<StructureData> Structures;
        public Option<TextureData> Materials;
        public Registry<EntityAuthoring> Entities;
    }

    [Serializable]
    public struct GamePlaySettings{
        [UISetting(Message = "Controls How The Player Interacts With The World")]
        public Option<TerraformSettings> Terraforming;
        public Option<RigidFPController.RigidFPControllerSettings> Movement;
        public Option<CraftingMenuSettings> Crafting;
        public Option<InventoryController.Settings> Inventory;
        public Option<DayNightContoller.Settings> DayNightCycle;
    }

    [Serializable]
    public struct SystemSettings{
        public Option<ReadbackSettings> ReadBack;
    }

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context = default){
        object defaultOptions = WorldStorageHandler.OPTIONS_TEMPLATE; 
        object thisRef = this;
        SupplementTree(ref thisRef, ref defaultOptions);
    }

    //To Do: Flatten Options into a list and store index if it isn't dirty to the template list
    public static void SupplementTree(ref object dest, ref object src){
        System.Reflection.FieldInfo[] fields = src.GetType().GetFields();
        foreach(FieldInfo field in fields){
            if(field.FieldType.IsGenericType && field.FieldType.GetGenericTypeDefinition() == typeof(Option<>)){
                if(((IOption)field.GetValue(dest)).IsDirty){
                    FieldInfo nField = field.FieldType.GetField("value");
                    if(nField.FieldType.IsGenericType && nField.FieldType.GetGenericTypeDefinition() == typeof(List<>)){
                        object oDest = field.GetValue(dest), oSrc  = field.GetValue(src);
                        IList nDest = (IList)nField.GetValue(oDest), nSrc = (IList)nField.GetValue(oSrc);
                        CopyList(nDest, nSrc);
                        nField.SetValue(oDest, nDest); field.SetValue(dest, oDest);
                    } else {
                        object oDest = field.GetValue(dest), oSrc  = field.GetValue(src);
                        object nDest = nField.GetValue(oDest), nSrc = nField.GetValue(oSrc);
                        SupplementTree(ref nDest, ref nSrc);
                        nField.SetValue(oDest, nDest); field.SetValue(dest, oDest);
                    }
                } else field.SetValue(dest, field.GetValue(src)); //This is the only line that actually fills in anything
            }
            else if (field.FieldType.IsPrimitive || field.FieldType == typeof(string)) continue;
            else if (field.FieldType.IsValueType) {
                object nDest = field.GetValue(dest), nSrc = field.GetValue(src);
                SupplementTree(ref nDest, ref nSrc);
                field.SetValue(dest, nDest);
            } else throw new Exception("Settings objects must contain either only value types or options");
        }
    }


    static void CopyList(IList dest, IList src){
        for(int i = 0; i < dest.Count && i < src.Count; i++){
            object srcEl = src[i]; object destEl = dest[i]; Type destType = destEl.GetType();
            if(destType.IsGenericType && destType.GetGenericTypeDefinition() == typeof(Option<>)){
                if(((IOption)destEl).IsDirty) {
                    object nDest, nSrc; 
                    FieldInfo field = destType.GetField("value");
                    nDest = field.GetValue(destEl); nSrc = field.GetValue(srcEl);
                    SupplementTree(ref nDest, ref nSrc);
                    field.SetValue(destEl, nDest); dest[i] = destEl;
                } else dest[i] = src[i];
            } 
            else if(destType.IsPrimitive || destType == typeof(string)) continue;
            else if(destType.IsValueType){
                SupplementTree(ref destEl, ref srcEl);
                dest[i] = destEl;
            } else throw new Exception("Settings objects must contain either only value types or options");
        }
    }

    public static WorldOptions Create(){
        WorldOptions newOptions = Instantiate(WorldStorageHandler.OPTIONS_TEMPLATE);
        newOptions.seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
        return newOptions;
    }
}

public interface IOption{ bool IsDirty { get; } void Clone();}
//Option is struct but the types
[Serializable]
public struct Option<T> : IOption{
    [SerializeField]
    public T value;
    [HideInInspector][UISetting(Ignore = true)]
    public bool isDirty;

    public static implicit operator T(Option<T> option) => option.value;
    public static implicit operator Option<T>(T val) => new Option<T> { value = val };
    public bool ShouldSerializevalue() { return isDirty; }
    //Default value is false so it's the same if we don't store it

    [JsonIgnore]
    public readonly bool IsDirty{
        get { return isDirty; }
    }

    public void Clone() { 
        if(isDirty) return;
        isDirty = true;

        if(value is UnityEngine.Object)
            value = (T)(object)UnityEngine.Object.Instantiate((UnityEngine.Object)(object)value);
        else if(value is ICloneable cloneable)
            value = (T)cloneable.Clone();
        else if (value is IList list){
            value = (T)Activator.CreateInstance(list.GetType(), list);
        }
    }
}


