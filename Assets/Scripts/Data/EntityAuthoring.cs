using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;

public abstract class EntityAuthoring : ScriptableObject{
    //Managed Memory
    public virtual EntityController Controller{get; }
    //Allocated in buffer/memory block
    public virtual IEntity Entity{get; set;}
    public virtual Entity.Info info{get; set;}
    //Allocated in buffer/memory block
    public virtual uint2[] Profile{get; set;}
}


/*
Recreated polymorphism with explicit vtable
Pls know what you're doing when using this
*/
public unsafe struct Entity{
    [NativeDisableUnsafePtrRestriction]
    public FunctionPointer<IEntity.UpdateDelegate> _Update;
    public FunctionPointer<IEntity.DisableDelegate> _Disable;
    public IntPtr obj; //This is a struct that implements IEntity
    public Info info; 
    public bool active;

    public static void Update(Entity* entity, EntityJob.Context* context){ entity->_Update.Invoke(entity, context); }
    public static void Disable(Entity* entity){ entity->_Disable.Invoke(entity); }

    
    [System.Serializable]
    public struct Info{
        public ProfileInfo profile;
        public uint SpatialId;
        public uint entityId;

        //Node in chunk's LL for all entity's belonging to chunk
        [System.Serializable]
        public struct ProfileInfo{
            public uint3 bounds;
            [HideInInspector][UIgnore][JsonIgnore]
            public uint profileStart;
        }
    }
    
}


//Structures implementing IEntity will be placed in unmanaged memory so they can be used in Jobs
public interface IEntity{
    public abstract unsafe IntPtr Initialize(ref Entity entity, int3 GCoord);
    public unsafe delegate void UpdateDelegate(Entity* entity, EntityJob.Context* context);
    public unsafe delegate void DisableDelegate(Entity* entity);
}


