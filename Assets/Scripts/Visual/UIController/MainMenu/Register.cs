using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;


//Curiously recurring template pattern!!
[Serializable]
public class Category<T> : ScriptableObject where T : Category<T>
{
    public string Name;
    public TagRegistry Tags;
    protected virtual Option<List<Option<Category<T>>>>? GetChildren() => null;
    protected virtual void SetChildren(Option<List<Option<Category<T>>>> value) { }

    public virtual void AddChildren(ref List<T> flat, ref List<BackEdge> inv, BackEdge parent = null)
    {
        flat ??= new List<T>();
        inv ??= new List<BackEdge>();

        Tags.Construct();
        var children = GetChildren()?.value;
        BackEdge invertedDependency = new BackEdge(parent, this);
        //If children is null, then it is the leaf type being contained
        if (children == null)//
        {
            inv.Add(invertedDependency);
            flat.Add((T)this);
            return;
        }
        foreach (var pair in children)
        {
            if (pair.value != null) pair.value.AddChildren(ref flat, ref inv, invertedDependency);
        }
    }

    public virtual void OnValidate() => Tags.OnValidate();

    public class BackEdge
    {
        public BackEdge Parent = null;
        public Category<T> Category = null;
        public BackEdge(BackEdge Parent, Category<T> Category){
            this.Parent = Parent;
            this.Category = Category;
        }
    }
}

[Serializable]
public struct Catalogue<T> : IRegister, ICloneable where T : Category<T>
{
    public Option<Category<T>> Category;
    [HideInInspector]
    [UISetting(Ignore = true, Defaulting = true)]
    [JsonIgnore]
    public List<T> Reg;
    [HideInInspector]
    [UISetting(Ignore = true, Defaulting = true)]
    [JsonIgnore]
    private Dictionary<string, int> Index;
    [HideInInspector]
    [UISetting(Ignore = true, Defaulting = true)]
    [JsonIgnore]
    private List<Category<T>.BackEdge> InvertedDependency;

    public void Construct()
    {
        Reg = new List<T>();
        InvertedDependency = new List<Category<T>.BackEdge>();
        Category.value.AddChildren(ref Reg, ref InvertedDependency);
        ReconstructIndex();
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
        InvertedDependency ??= new List<Category<T>.BackEdge>();

        Reg.Add(value);
        Index.Add(name, Reg.Count - 1);
        InvertedDependency.Add(new Category<T>.BackEdge(null, value));
    }

    public bool TryRemove(string name)
    {
        if (Reg == null || Index == null) return false;
        if (InvertedDependency == null) return false;
        if (!Index.ContainsKey(name)) return false;

        InvertedDependency.RemoveAt(Index[name]);
        Reg.RemoveAt(Index[name]);
        ReconstructIndex();
        return true;
    }

    public readonly bool TrySet(string name, T value)
    {
        if (Reg == null || Index == null) return false;
        if (!Index.ContainsKey(name)) return false;

        int index = Index[name];
        InvertedDependency[index].Category = value;
        Reg[index] = value;
        return true;
    }

    public readonly bool GetMostSpecificTag(TagRegistry.Tags tag, string name, out object prop) => GetMostSpecificTag(tag, RetrieveIndex(name), out prop);
    public readonly bool GetMostSpecificTag(TagRegistry.Tags tag, int index, out object prop) {
        prop = null;
        if (index < 0 || index >= Reg.Count) return false;
        Category<T>.BackEdge cur = InvertedDependency[index];
        while (cur != null)
        {
            if (cur.Category.Tags.Contains(tag))
                break;
            cur = cur.Parent;
        }
        if (cur == null) return false;
        prop = cur.Category.Tags.Retrieve(tag);
        return true;
    }

    private void ReconstructIndex()
    {
        Index = new Dictionary<string, int>();
        for (int i = 0; i < Reg.Count; i++)
        {
            Index.Add(Reg[i].Name, i);
        }
    }

    public object Clone() => new Catalogue<T> { Reg = Reg };
    
    public int Count() => Reg.Count;
    
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

[Serializable]
public struct Registry<T> : IRegister, ICloneable
{
    public List<Pair> Reg;
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    private Dictionary<string, int> Index;

    public static Registry<T> FromCatalogue<TR>(Catalogue<TR> registry) where TR : Category<TR>, T {
        Registry<T> NewRegistry = new Registry<T>();
        NewRegistry.Reg = new List<Pair>(new Pair[registry.Count()]);
        for (int i = 0; i < registry.Count(); i++) {
            NewRegistry.Reg[i] = new Pair {
                Name = registry.RetrieveName(i),
                _value = new Option<T> { value = registry.Retrieve(i) }
            };
        }
        NewRegistry.Construct();
        return NewRegistry;
    }
    public void Construct() {
        Index = new Dictionary<string, int>();
        Reg ??= new List<Pair>();
        for (int i = 0; i < Reg.Count; i++) {
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

        Reg.Add(new Pair { Name = name, _value = new Option<T> { value = value } });
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

    public object Clone() => new Registry<T> { Reg = Reg };

    public int Count() => Reg.Count;
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





public interface IRegister {
    public abstract int Count();
    public abstract void Construct();
    public abstract string RetrieveName(int index);
    public abstract int RetrieveIndex(string name);
    public abstract bool Contains(string name);
    public abstract bool Contains(int index);
    public abstract object Clone();
    public static void Setup(Config settings) {
        //Create all registers, then convert their dependencies
        settings.Generation.Noise.Construct();
        settings.Generation.Entities.Construct();
        settings.Generation.Textures.Construct();
        settings.Generation.Biomes.value.SurfaceBiomes.Construct();
        settings.Generation.Biomes.value.SeafloorBiomes.Construct();
        settings.Generation.Biomes.value.CaveBiomes.Construct();
        settings.Generation.Biomes.value.SkyBiomes.Construct();
        settings.Generation.Biomes.value.SeaBiomes.Construct();
        settings.Generation.Structures.value.StructureDictionary.Construct();
        settings.Generation.Materials.value.MaterialDictionary.Construct();
        settings.Generation.Items.Construct();
        settings.System.Crafting.value.Recipes.Construct();
        settings.System.FurnaceFormulas.Construct();
        settings.System.MortarFormulas.Construct();
        settings.Quality.GeoShaders.value.Categories.Construct();
        settings.System.Armor.value.Variants.Construct();
        
        foreach (GeoShader shader in settings.Quality.GeoShaders.value.Categories.Reg) {
            IRegister reg = shader.GetRegistry();
            if (reg == null) continue;
            reg.Construct(); shader.SetRegistry(reg);
        }
    }

    public static void AssociateRegistries(Config settings, ref Dictionary<string, IRegister> Association) {
        Setup(settings);
        Association ??= new Dictionary<string, IRegister>();
        Association.Add("Noise", settings.Generation.Noise);
        Association.Add("Entities", settings.Generation.Entities);
        Association.Add("Textures", settings.Generation.Textures);
        Association.Add("SurfaceBiomes", settings.Generation.Biomes.value.SurfaceBiomes);
        Association.Add("SeafloorBiomes", settings.Generation.Biomes.value.SeafloorBiomes);
        Association.Add("CaveBiomes", settings.Generation.Biomes.value.CaveBiomes);
        Association.Add("SkyBiomes", settings.Generation.Biomes.value.SkyBiomes);
        Association.Add("SeaBiomes", settings.Generation.Biomes.value.SeaBiomes);
        Association.Add("Structures", settings.Generation.Structures.value.StructureDictionary);
        Association.Add("Materials", settings.Generation.Materials.value.MaterialDictionary);
        Association.Add("Items", settings.Generation.Items);
        Association.Add("FurnaceRecipies", settings.System.FurnaceFormulas);
        Association.Add("MortarRecipies", settings.System.MortarFormulas);
        Association.Add("CraftingRecipes", settings.System.Crafting.value.Recipes);
        Association.Add("GeoShaders", settings.Quality.GeoShaders.value.Categories);
        Association.Add("ArmorVariants", settings.System.Armor.value.Variants);

        foreach (GeoShader shader in settings.Quality.GeoShaders.value.Categories.Reg) {
            Association.Add("GeoShaders::" + shader.Name, shader.GetRegistry());
        }
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

