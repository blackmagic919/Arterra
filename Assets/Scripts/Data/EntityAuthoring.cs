using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;

public abstract class EntityAuthoring : ScriptableObject{
    //Managed Memory
    public virtual GameObject Controller{get; set;}
    //Allocated in buffer/memory block
    public virtual IEntity Entity{get; set;}
    //Allocated in buffer/memory block
    public virtual uint2[] Profile{get; set;}
}

//Structures implementing IEntity will be placed in unmanaged memory so they can be used in Jobs
public interface IEntity{
    public void Initialize();
    public void Update();
    public void Release();
    public Info info{get; set;}
    [System.Serializable]
    public struct Info{
        public uint3 bounds;
        [HideInInspector][UIgnore][JsonIgnore]
        public uint profileStart;
    }
}


