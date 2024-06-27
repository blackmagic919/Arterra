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
public sealed class UIgnore : Attribute{}

[CreateAssetMenu(menuName = "Generation/WorldOptions")]
public class WorldOptions : ScriptableObject{
    public int seed;
    public Option<BiomeGenerationData> Biomes;
    public Option<List<Option<StructureData> > > Structures;
    public Option<TextureData> Materials;
    public Option<List<Option<NoiseData> > > Noise;
    public Option<GeneratorSettings> GeoShaders;
    public Option<MeshCreatorSettings> Terrain;
    public Option<SurfaceCreatorSettings> Surface;
    public Option<MemoryBufferSettings> Memory;

    [UIgnore]
    public Option<ReadbackSettings> ReadBackSettings;

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context = default){
        object defaultOptions = Resources.Load<WorldOptions>("Prefabs/DefaultOptions"); 
        object thisRef = this;
        WorldOptions.SupplementTree(ref thisRef, ref defaultOptions);
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

    //Only used by the editor
    public void Create(){
        this.seed = 0;
        this.OnDeserialized();
    }
}

public interface IOption{ bool IsDirty { get; } void Clone();}
    //Option is struct but the types
    [Serializable]
    public struct Option<T> : IOption{
        [SerializeField]
        public T value;
        [HideInInspector][UIgnore]
        public bool isDirty;

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

[System.Serializable]
public struct Vec2{
    public float x; public float y;
    public Vec2(float x, float y){ this.x = x; this.y = y; }
    public Vec2(Vector2 v){ this.x = v.x; this.y = v.y;}
    public Vector2 GetVector(){ return new Vector2(x, y); }
}

[System.Serializable]
public struct Vec3{
    public float x; public float y; public float z;
    public Vec3(float x, float y, float z){ this.x = x; this.y = y; this.z = z; }
    public Vec3(Vector3 v){ this.x = v.x; this.y = v.y; this.z = v.z;}
    public Vector3 GetVector(){ return new Vector3(x, y, z); }
}

[System.Serializable]
public struct Vec4{
    public float x; public float y; public float z; public float w;
    public Vec4(float x, float y, float z, float w){ this.x = x; this.y = y; this.z = z; this.w = w; }
    public Vec4(Vector4 v){ this.x = v.x; this.y = v.y; this.z = v.z; this.w = v.w;}
    public Vec4(Quaternion v){ this.x = v.x; this.y = v.y; this.z = v.z; this.w = v.w;}
    public Vector4 GetVector(){ return new Vector4(x, y, z);}
    public Quaternion GetQuaternion(){ return new Quaternion(x, y, z, w); }
    public Color GetColor(){ return new Color(x,y,z,w); }
}


