using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;
using WorldConfig;

//Curiously recurring template pattern!!
[Serializable]
public class Category<T> : ScriptableObject where T : Category<T>{
    public string Name;
    protected virtual Option<List<Option<Category<T> > > >? GetChildren() => null;
    protected virtual void SetChildren(Option<List<Option<Category<T>>>> value){}

    public virtual void AddChildren(ref List<T> list)
    {
        list ??= new List<T>();
        var children = GetChildren()?.value;
        //If children is null, then it is the leaf type being contained
        if (children == null)
        {
            list.Add((T)this);
            return;
        }
        foreach (var pair in children)
        {
            if (pair.value != null) pair.value.AddChildren(ref list);
        }
    }
}

[Serializable]
public struct Registry<T> : IRegister, ICloneable where T : Category<T>
{
    public Option<Category<T>> Category;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    public List<T> Reg;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    private Dictionary<string, int> Index;

    public void Construct()
    {
        Index = new Dictionary<string, int>();
        Reg = new List<T>();
        Category.value.AddChildren(ref Reg);
        for (int i = 0; i < Reg.Count; i++)
        {
            Index.Add(Reg[i].Name, i);
        }
    }

    public readonly int RetrieveIndex(string name)
    {
        return Index[name];
    }
    public readonly string RetrieveName(int index)
    {
        return Reg[index].Name;
    }

    public readonly T Retrieve(string name)
    {
        return Reg[Index[name]];
    }
    public readonly T Retrieve(int index)
    {
        return Reg[index];
    }
    public readonly bool Contains(string name)
    {
        if (Index == null) return false;
        return Index.ContainsKey(name);
    }
    public readonly bool Contains(int index)
    {
        return index >= 0 && index < Reg.Count;
    }
    public void Add(string name, T value)
    {
        Reg ??= new List<T>();
        Index ??= new Dictionary<string, int>();

        Reg.Add(value);
        Index.Add(name, Reg.Count - 1);
    }

    public bool TryRemove(string name)
    {
        if (Reg == null || Index == null) return false;
        if (!Index.ContainsKey(name)) return false;

        Reg.RemoveAt(Index[name]);
        Index.Remove(name);
        Construct(); //Rebuild the index
        return true;
    }

    public readonly bool TrySet(string name, T value)
    {
        if (Reg == null || Index == null) return false;
        if (!Index.ContainsKey(name)) return false;

        int index = Index[name];
        Reg[index] = value;
        return true;
    }

    public object Clone()
    {
        return new Registry<T> { Reg = Reg };
    }

    [Serializable]
    public struct Pair : ICloneable
    {
        public string Name;
        [UISetting(Alias = "Value")]
        public Option<T> _value;
        [JsonIgnore]
        public readonly T Value => _value.value;

        public object Clone()
        {
            return new Pair
            {
                Name = Name,
                _value = _value
            };
        }
    }
}

public struct DynamicRegistry<T> : IRegister, ICloneable 
{
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    public List<Pair> Reg;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    private Dictionary<string, int> Index;

    public void Construct()
    {
        Index = new Dictionary<string, int>();
        Reg ??= new List<Pair>();
        for (int i = 0; i < Reg.Count; i++)
        {
            Index.Add(Reg[i].Name, i);
        }
    }

    public readonly int RetrieveIndex(string name)
    {
        return Index[name];
    }
    public readonly string RetrieveName(int index)
    {
        return Reg[index].Name;
    }

    public readonly T Retrieve(string name)
    {
        return Reg[Index[name]].Value;
    }
    public readonly T Retrieve(int index)
    {
        return Reg[index].Value;
    }
    public readonly bool Contains(string name)
    {
        if (Index == null) return false;
        return Index.ContainsKey(name);
    }
    public readonly bool Contains(int index)
    {
        return index >= 0 && index < Reg.Count;
    }
    public void Add(string name, T value)
    {
        Reg ??= new List<Pair>();
        Index ??= new Dictionary<string, int>();

        Reg.Add(new Pair{Name = name, _value = new Option<T>{value = value}});
        Index.Add(name, Reg.Count - 1);

    }

    public bool TryRemove(string name)
    {
        if (Reg == null || Index == null) return false;
        if (!Index.ContainsKey(name)) return false;

        Reg.RemoveAt(Index[name]);
        Index.Remove(name);
        Construct(); //Rebuild the index
        return true;
    }

    public readonly bool TrySet(string name, T value)
    {
        if (Reg == null || Index == null) return false;
        if (!Index.ContainsKey(name)) return false;

        int index = Index[name];
        var tPair = Reg[index];
        tPair._value.value = value;
        Reg[index] = tPair;
        return true;
    }

    public object Clone()
    {
        return new DynamicRegistry<T> { Reg = Reg };
    }

    [Serializable]
    public struct Pair : ICloneable
    {
        public string Name;
        [UISetting(Alias = "Value")]
        public Option<T> _value;
        [JsonIgnore]
        public readonly T Value => _value.value;

        public object Clone()
        {
            return new Pair
            {
                Name = Name,
                _value = _value
            };
        }
    }
}





public interface IRegister
{
    public abstract void Construct();
    public abstract string RetrieveName(int index);
    public abstract int RetrieveIndex(string name);
    public abstract bool Contains(string name);
    public abstract bool Contains(int index);
    public abstract object Clone();
    public static void Setup()
    {
        //Create all registers, then convert their dependencies
        Config.CURRENT.Generation.Noise.Construct();
        Config.CURRENT.Generation.Entities.Construct();
        Config.CURRENT.Generation.Textures.Construct();
        Config.CURRENT.Generation.Biomes.value.SurfaceBiomes.Construct();
        Config.CURRENT.Generation.Biomes.value.CaveBiomes.Construct();
        Config.CURRENT.Generation.Biomes.value.SkyBiomes.Construct();
        Config.CURRENT.Generation.Structures.value.StructureDictionary.Construct();
        Config.CURRENT.Generation.Materials.value.MaterialDictionary.Construct();
        Config.CURRENT.Generation.Items.Construct();
        Config.CURRENT.Quality.GeoShaders.Construct();
        Config.CURRENT.System.Crafting.value.Recipes.Construct();
    }
}

public interface IRegistered{
    public abstract IRegister GetRegistry();
    public int Index{get;set;}
}

public struct Registerable<T> where T : IRegistered {
    public string Name;
    public T Value;
    public Registerable(T obj){
        Name = null;
        Value = obj;
    }

    [OnSerializing]
    internal void OnSerializing(StreamingContext cxt){
        if(Value == null) return;
        IRegister registry = Value.GetRegistry();
        Name = registry.RetrieveName(Value.Index);
    }

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext cxt){
        if(Value == null) return;
        if(String.IsNullOrEmpty(Name)) return;
        IRegister registry = Value.GetRegistry();
        Value.Index = registry.RetrieveIndex(Name);
    }
}

