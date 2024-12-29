using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using System.Reflection;
using Newtonsoft.Json;


public static class RegisterBuilder{
    public static void Initialize(){
        //Create all registers, then convert their dependencies
        WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.SurfaceBiomes.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.CaveBiomes.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.SkyBiomes.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Structures.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Entities.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Items.Construct();
    }
}
[Serializable]
public struct Registry<T> : IRegister, ICloneable
{
    public Option<List<Pair> > Reg;
    [UISetting(Ignore = true)] 
    private Dictionary<string, int> Index;
    [JsonIgnore]
    public T[] SerializedData => Reg.value.Select(x => x.Value).ToArray();

    public void Construct(){
        Index = new Dictionary<string, int>();
        Reg.value ??= new List<Pair>();
        for(int i = 0; i < Reg.value.Count; i++){
            Index.Add(Reg.value[i].Name, i);
        }
    }

    public readonly int RetrieveIndex(string name){
        return Index[name];
    }
    public readonly string RetrieveName(int index){
        return Reg.value[index].Name;
    }

    public readonly T Retrieve(string name){
        return Reg.value[Index[name]].Value;
    }
    public readonly T Retrieve(int index){
        return Reg.value[index].Value;
    }
    public readonly bool Contains(string name){
        if(Index == null) return false;
        return Index.ContainsKey(name);
    }
    public readonly bool Contains(int index){
        return index >= 0 && index < Reg.value.Count;
    }
    public void Add(string name, T value){
        Reg.value ??= new List<Pair>();
        Index ??= new Dictionary<string, int>();

        Reg.value.Add(new Pair{Name = name, _value = new Option<T>{value = value}});
        Index.Add(name, Reg.value.Count - 1);
    }

    public bool TryRemove(string name){
        if(Reg.value == null || Index == null) return false;
        if(!Index.ContainsKey(name)) return false;

        Reg.value.RemoveAt(Index[name]);
        Index.Remove(name);
        Construct(); //Rebuild the index
        return true;
    }

    public readonly bool TrySet(string name, T value){
        if(Reg.value == null || Index == null) return false;
        if(!Index.ContainsKey(name)) return false;

        int index = Index[name];
        var tPair = Reg.value[index];
        tPair._value.value = value;
        Reg.value[index] = tPair;
        return true;
    }

    public object Clone(){
        return new Registry<T>{Reg = Reg};
    }

    [Serializable]
    public struct Pair : ICloneable{
        public string Name;
        [UISetting(Alias = "Value")]
        public Option<T> _value;
        [JsonIgnore]
        public readonly T Value => _value.value;

        public object Clone(){
            return new Pair{
                Name = Name, 
                _value = _value
            };
        }
    }
}


public interface IRegister{
    public abstract void Construct();
    public abstract string RetrieveName(int index);
    public abstract int RetrieveIndex(string name);
    public abstract bool Contains(string name);
    public abstract bool Contains(int index);
    public abstract object Clone();
    
}



