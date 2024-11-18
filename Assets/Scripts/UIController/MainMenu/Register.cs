using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEngine;
using System.Reflection;


public static class RegisterBuilder{
    public static void Initialize(){
        //Create all registers, then convert their dependencies
        WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.biomes.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Structures.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Entities.Construct();
        WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary.Construct();
    }
}
[Serializable]
public struct Registry<T> : IRegister
{
    public Option<List<Pair > > Reg;
    [UISetting(Ignore = true)] 
    private Dictionary<string, int> Index;
    public T[] SerializedData => Reg.value.Select(x => x.Value).ToArray();

    public void Construct(){
        Index = new Dictionary<string, int>();
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
    [Serializable]
    public struct Pair{
        public string Name;
        public Option<T> _value;
        public T Value => _value.value;
    }
}


public interface IRegister{
    public abstract void Construct();
    public abstract string RetrieveName(int index);
    public abstract int RetrieveIndex(string name);
}



