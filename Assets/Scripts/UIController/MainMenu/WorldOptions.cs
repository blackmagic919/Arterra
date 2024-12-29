using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;

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


[CreateAssetMenu(menuName = "Generation/WorldOptions")]
public class WorldOptions : ScriptableObject{
    public int Seed;
    [UISetting(Alias = "Quality")]
    public Option<QualitySettings> _Quality;
    [UISetting(Alias = "Generation")]
    public Option<GenerationSettings> _Generation;
    [UISetting(Alias = "Gameplay")]
    public Option<GamePlaySettings> _GamePlay;
    [UISetting(Ignore = true)]
    public Option<SystemSettings> _System;

    [JsonIgnore]
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
        public Option<Biome.GenerationData> Biomes;
        public Registry<StructureData> Structures;
        public Option<MaterialGeneration> Materials;
        public Registry<EntityAuthoring> Entities;
        public Registry<ItemAuthoring> Items;
    }

    //These settings may change during gameplay so reference through direct getter functions
    [Serializable]
    public struct GamePlaySettings{
        [UISetting(Message = "Controls How The Player Interacts With The World")]
        public Option<TerraformSettings> Terraforming;
        public Option<RigidFPController.RigidFPControllerSettings> Movement;
        public Registry<InputPoller.KeyBind> Input;
        public Option<CraftingMenuSettings> Crafting;
        public Option<InventoryController.Settings> Inventory;
        public Option<DayNightContoller.Settings> DayNightCycle;
    }

    [Serializable]
    public struct SystemSettings{
        public Option<ReadbackSettings> ReadBack;
        public Registry<int> LayerHeads;
    }

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context = default){
        object defaultOptions = WorldStorageHandler.OPTIONS_TEMPLATE; 
        object thisRef = this;
        SegmentedUIEditor.SupplementTree(ref thisRef, ref defaultOptions);
    }

    public static WorldOptions Create(){
        WorldOptions newOptions = Instantiate(WorldStorageHandler.OPTIONS_TEMPLATE);
        newOptions.Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
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


