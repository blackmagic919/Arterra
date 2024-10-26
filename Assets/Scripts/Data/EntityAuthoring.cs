using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using System;
using System.Runtime.InteropServices;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using UnityEditor;

public abstract class EntityAuthoring : ScriptableObject{
    //Managed Memory
    public virtual EntityController Controller{get; }
    public virtual IEntity Entity{get; set;}
    public virtual IEntitySetting Setting{get; set;}
    public virtual ProfileE[] Profile{get; set;}
    public virtual Entity.Info.ProfileInfo Info{get; set;}
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
        public uint entityType;

        //Node in chunk's LL for all entity's belonging to chunk
        [System.Serializable]
        public struct ProfileInfo{
            public uint3 bounds;
            [HideInInspector][UISetting(Ignore = true)][JsonIgnore]
            public uint profileStart;
        }
    }
    
}

//Structures implementing IEntity will be placed in unmanaged memory so they can be used in Jobs
public interface IEntity{
    public abstract unsafe void Preset(IEntitySetting Settings);
    public abstract unsafe IntPtr Initialize(ref Entity entity, int3 GCoord);
    public unsafe delegate void UpdateDelegate(Entity* entity, EntityJob.Context* context);
    public unsafe delegate void DisableDelegate(Entity* entity);
}

public interface IEntitySetting{} 

[Serializable]
public struct ProfileE {
    public uint bounds;
    public uint flags;
    public readonly bool AndFlag => (flags & 0x1) != 0;
    public readonly bool OrFlag => (flags & 0x2) != 0;
    public readonly bool ExFlag => (flags & 0x4) != 0;
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ProfileE))]
public class MapDataDrawer : PropertyDrawer{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty boundProp = property.FindPropertyRelative("bounds");
        uint data = boundProp.uintValue;

        //bool isDirty = (data & 0x80000000) != 0;
        int[] densityB = new int[2]{
            (int)(data & 0xFF),
            (int)((data >> 8) & 0xFF)
        };
        int[] viscosityB = new int[2]{
            (int)((data >> 16) & 0xFF),
            (int)((data >> 24) & 0xFF)
        };

        Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Density L"), new ("Density U") }, densityB);
        rect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Viscosity L"), new ("Viscosity U") }, viscosityB);
        rect.y += EditorGUIUtility.singleLineHeight;


        data = (data & 0xFFFFFF00) | ((uint)densityB[0] & 0xFF);
        data = (data & 0xFFFF00FF) | (((uint)densityB[1] & 0xFF) << 8);
        data = (data & 0xFF00FFFF) | (((uint)viscosityB[0] & 0xFF) << 16);
        data = (data & 0x00FFFFFF) | (((uint)viscosityB[1] & 0xFF) << 24);
        boundProp.uintValue = data;

        SerializedProperty flagProp = property.FindPropertyRelative("flags");
        uint flag = flagProp.uintValue;

        bool[] flags = new bool[3]{
            (flag & 0x1) != 0,
            (flag & 0x2) != 0,
            (flag & 0x4) != 0
        };

        float toggleWidth = rect.width / 3;
        Rect flag1Rect = new(rect.x, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
        flags[0] = EditorGUI.ToggleLeft(flag1Rect, "And", flags[0]);
        Rect flag2Rect = new(rect.x + toggleWidth, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
        flags[1] = EditorGUI.ToggleLeft(flag2Rect, "Or", flags[1]);
        Rect flag3Rect = new(rect.x + toggleWidth * 2, rect.y, toggleWidth, EditorGUIUtility.singleLineHeight);
        flags[2] = EditorGUI.ToggleLeft(flag3Rect, "Exclude", flags[2]);
//
        flag = flags[0] ? flag | 0x1 : flag & 0xFFFFFFFE;
        flag = flags[1] ? flag | 0x2 : flag & 0xFFFFFFFD;
        flag = flags[2] ? flag | 0x4 : flag & 0xFFFFFFFB;
        flagProp.uintValue = flag;
    }

    // Override this method to make space for the custom fields
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 3;
    }
}
#endif


