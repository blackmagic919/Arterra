using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;
using System.Reflection;

[CreateAssetMenu(menuName = "Generation/WorldOptions")]
public class WorldOptions : ScriptableObject{
    public Option<int> seed;
    public Option<TestOption> testOption;

    [OnDeserialized]
    internal void OnDeserialized(StreamingContext context = default){
        //This is definately also a piece of code lol
        WorldOptions defaultOptions = Resources.Load<WorldOptions>("Prefabs/DefaultOptions"); 
        foreach(FieldInfo field in typeof(WorldOptions).GetFields())
        {  if(!((IOption)field.GetValue(this)).IsDirty) field.SetValue(this, field.GetValue(defaultOptions)); }
    }

    //This is definately a piece of code...


    public interface IOption{ bool IsDirty { get; } void Clone();}
    //Option is struct but the types
    [Serializable]
    public struct Option<T> : IOption{
        [SerializeField]
        public T value;
        [HideInInspector]
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
        }
    }
}

[Serializable]
public class TestOption : ICloneable{
    public List<WorldOptions.Option<TestOption1>> options;
    public int value3;

    public object Clone(){ return this.MemberwiseClone(); }
}

[Serializable]
public class TestOption1 : ICloneable{
    public int value1;
    public int value2;
    public object Clone(){ return this.MemberwiseClone(); }
}
