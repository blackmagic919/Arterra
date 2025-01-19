/*using System;
using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using UnityEditor;
namespace WorldConfig.Generation.Entity{
/// <summary>
/// A generic contract that ensures that all entities contain a certain
/// set of properties and methods necessary for the system to function.
/// </summary>
public abstract class Authoring : ScriptableObject{
    /// <summary> A reference to the controller that manages the entity. See <see cref="EntityController"/> for more information. </summary>
    public virtual GameObject Prefab{get; set; }
    /// <summary> A reference to the entity that contains the actual instance of the entity. See <see cref="IEntity"/> for more information</summary>
    public virtual Entity Entity{get; set;}
    /// <summary> A reference to the readonly shared settings that all instances of this entity uses. See <see cref="IEntitySetting"/> for more information</summary>
    public virtual IEntitySetting Setting{get; set;}
    /// <summary> A list of points defining the profile of the entity, the list is linearly encoded through the dimensions
    /// specified in <see cref="Entity.ProfileInfo"/>. See <see cref="ProfileE"/> for more information </summary>
    public virtual ProfileE[] Profile{get; set;}
    /// <summary> A reference to the profile information of the entity. Used during generation to place the entity. See <see cref="Entity.ProfileInfo"/> 
    /// for more information  </summary>
    public virtual Entity.ProfileInfo Info{get; set;}
}


/// <summary>
/// A generic entity that defines a virtual function table that all entities must implement.
/// This is necessary because Unity's Job system does not support managed types. This struct
/// allows each thread to process a signle entity every game tick without knowing the actual
/// type of the entity.
/// </summary>
public abstract class Entity{
    /// <summary> Information about the entity instance that is required of every instance for the system to function. See <see cref="Info"/> for more information. </summary>
    public Info info; 
    /// <summary> Whether or not the entity is active. This is the flag set by <see cref="_Disable"/> to indicate to the controller
    /// that it can release the entity. Once this flag is set to false, it cannot be set to true without risking race
    /// conditions and undefined behavior. </summary>
    public bool active;
    /// <summary> A pointer to the specific entity within unmanaged memory. Information specific to the entity's type
    /// is stored in this structure. The structure being pointed to implements the <see cref="IEntity"/>
    /// interface and has populated the virtual function table(see <see cref="IEntity.Initialize(ref Entity, int3)"/>)
    /// with the correct functions. </summary>

    /// <summary> A single line property calling the entity's virtual update function. See <see cref="Entity._Update"/> for more information. </summary>
    /// <param name="entity">The entity that is the instance the function is being called on. Equivalent to <c>this</c> in a managed type's method</param>
    /// <param name="context">The context referencing unmaanged system information to be used by the Update. See <see cref="EntityJob.Context"/> for more info. </param>
    public abstract void Update();
    /// <summary> A single line property calling the entity's virtual disable function. See <see cref="Entity._Disable"/> for more information.</summary> 
    /// <param name="entity">The entity that is the instance the function is being called on. Equivalent to <c>this</c> in a managed type's method</param>
    public abstract void Disable();

    public abstract void Preset(IEntitySetting setting);
    public abstract void Unset();

    public abstract void Initialize(GameObject prefab, int3 GCoord);
    public abstract void Deserialize(GameObject prefab, out int3 GCoord);
    
    /// <summary>
    /// Settings for a structure that is required for the systems governing how entities are identified
    /// and co-exist in the world. An incorrect setting can lead to not only invalidation of the entity
    /// but corruption of the entire entity system. This may be read from within the <see cref="obj">entity's type</see>
    /// but should not be modified except by the <see cref="EntityManager"> entity system </see>
    /// </summary>
    [System.Serializable]
    public struct Info{
        /// <summary> The profile information of the entity. See <see cref="ProfileInfo"/> for more information. </summary>
        public ProfileInfo profile;
        /// <summary>
        /// The index within the <see cref="EntityManager.STree"> spatial partition tree </see> that contains all entities of the 
        /// entity type. This spatial index and tree may change while the entity is active as the entity moves so do not rely on this
        /// always providing the correct spatial index. See <see cref="EntityManager.STree"/> for more info.
        /// </summary>
        public uint SpatialId;
        /// <summary> The job-safe unique identifier of the entity that remains the same throughout the entity's life cycle, regardless of if it's serialized
        /// or saved to disk. See <see cref="JGuid"/> for more information. </summary>
        public JGuid entityId;
        /// <summary> The index of the entity's name within the <see cref="WorldConfig.Config.GenerationSettings.Entities"/> registry. This is guaranteed to be different
        /// for two different types of entities and can be used to test such. If the entity is serialized, this should be decoupled and recoupled upon deserialization. </summary>
        public uint entityType;
    }

    /// <summary>
    /// Settings facilitiating the reading of the entity's profile from a shared location in memory. An entity's profile should define a region of 
    /// space the entity can exist in and path through. The profile is a list of conditions spanning from the entity's grid space integer 
    /// origin(the bottom-left corner) in the positive direction such that each condition aligns with an integer grid space. The map 
    /// entry at each grid position is checked against the <see cref="ProfileE"> condition </see> to determine if the profile matches the location. 
    /// A profile is used to assist in many processess from entity placement, pathfinding, pathfind recalculation, and more. An invalid 
    /// profile may result in erroneous entity behavior. 
    /// </summary>
    //Node in chunk's LL for all entity's belonging to chunk
    [System.Serializable]
    public struct ProfileInfo{
        /// <summary>
        /// The size of the entity's profile in grid space, Describes a 3D cuboid whose volume is the amount of <see cref="ProfileE"> checks </see> that 
        /// are performed to validate the entity's profile. The conditions are linearly encoded with the first x compoennt being the major axis and
        /// the final z component being the minor axis.
        /// </summary>
        public uint3 bounds;
        /// <summary>
        /// The start of the entity's profile within a global shared location, <see cref="TerrainGeneration.GenerationPreset.EntityHandle.entityProfileArray"/> 
        /// that contains the profiles for all entities. This should not be modified and will be assigned in runtime by the configuration of the entity registry.
        /// The size of the information read at this location is determined by <see cref="bounds"/>.
        /// </summary>
        [HideInInspector][UISetting(Ignore = true)][JsonIgnore]
        public uint profileStart;
    }
}
/// <summary>
/// An interface that all entities must implement to be used in the entity system. This interface
/// is called from a managed context and is used to populate an unmanaged virtual function table
/// that is used by the entity system to call the entity's functions at which point the <see cref="Entity">
/// unmanaged interface </see> will take over handling the entity.
/// </summary>
//Structures implementing IEntity will be placed in unmanaged memory so they can be used in Jobs
public interface IEntity{
    /// <summary> Presets any information shared by all instances of the entity. This is only called once per entity type within
    /// the <see cref="Config.GenerationSettings.Entities"> entity register </see> and is used to set up any shared readonly information.
    /// </summary> <remarks> For example, if the entity uses a state machine it can allocate function pointers to each state within the machine such that
    /// they may be referenced through an edge list. </remarks>
    /// <param name="Settings">The entity's settings, the concrete information known by the entity.</param>
    public abstract unsafe void Preset(IEntitySetting Settings);
    /// <summary> A callback to release any information set by <see cref="Preset"/>. Called once per entity type within the 
    /// <see cref="Config.GenerationSettings.Entities"> entity register </see> before the game is closed.  </summary>
    public abstract unsafe void Unset();
    /// <summary>
    /// Initializes the entity's instance. Called when creating an instance of the entity.
    /// The callee may preset any default values during this process but it <b>must</b> guarantee
    /// that the entity returned is fully populated (i.e. virtual functions all set).
    /// </summary>
    /// <param name="entity">The entity that contains it. This is copied to unmanaged memory by the callee; only this instance 
    /// should be copied as the system has already filled out all necessary metadata in it. </param>
    /// <param name="GCoord">The position in grid space the entity was placed at. </param>
    /// <returns>A pointer to an unmanaged <see cref="Entity"/> that wraps the <see cref="IEntity"/> with
    /// all virtual function pointers filled in. </returns>
    public abstract unsafe IntPtr Initialize(ref Entity entity, int3 GCoord);
    /// <summary>
    /// Deserializes the entity's instance. Some of the entity's information may be retrieved from serialization
    /// while others may need to be thrown away. This function is called when the entity is deserialized 
    /// in case the entity needs to reframe its information.
    /// </summary>
    /// <param name="entity">The entity that contains it. See <see cref="Initialize"/> for more info. </param>
    /// <param name="GCoord">The position in grid space the entity was placed at. </param>
    /// <returns>A pointer to an unmanaged <see cref="Entity"/>. See <see cref="Initialize"/> for more info. </returns>
    public abstract unsafe IntPtr Deserialize(ref Entity entity, out int3 GCoord);
}
/// <summary>
/// An interface for all type-specific settings used by entities. A setting itself does not need to 
/// define any members that has to be known externally so this is an empty contract and in effect no 
/// different than an <see cref="object"/>, but it offers clarity as to what the object is used for.
/// </summary>
public interface IEntitySetting{}

/// <summary>
/// A structure that contains the information stored when saving an entity. The system
/// then will convert between this format and the <see cref="Entity"/> when saving and loading
/// entities. 
/// </summary>
public struct EntitySerial{
    /// <summary> The type of the entity. This is the name of the entity within the <see cref="WorldConfig.Config.GenerationSettings.Entities"/> registry. 
    /// Upon deserialization, this type is used to cast <see cref="data"/> to before populating its data. An incorrect <see cref="type"/> will cause the entity
    /// to fail deserialization and be deleted. See <see cref="Utils.NSerializable.EntityConverter"/> for more information. </summary>
    public string type;
    /// <summary> The <see cref="Entity.Info.entityId"> guid </see> of the entity. As long as 
    /// the entity exists somewhere its guid does not change </summary>
    public string guid;
    /// <summary> The data of the entity, known by the entity itself. The <see cref="type"/> is used to serialize and deserialize this information. </summary>
    public Entity entity;
}

/// <summary>
/// A single condition that is considered when verifying an entity's <see cref="Entity.ProfileInfo"> profile </see>.
/// This is a condition that is tested against a point in space to verify that the entity can exist there. The 
/// condition may also define flags for how it should be evaluated when considering the entity's combined profile.
/// </summary>
[Serializable]
public struct ProfileE {
    /// <summary>
    /// A bitmask representing two conditions that a map entry must fall within to be considered a match. Similar
    /// to <see cref="Structure.StructureData.PointInfo"/>, divided into two-byte shorts where each short represents 
    /// a range defined by the bounds stored within the high and low byte. The high short is used for viscosity and
    /// the low short for density.
    /// </summary>
    public uint bounds;
    /// <summary>
    /// A bitmask representing the flags that should be used when evaluating the profile. The flags are used to determine
    /// how the condition should effect the overall validity of the profile. 
    /// </summary>
    public uint flags;
    /// <summary> Whether this condition must be true for the profile to be valid. 
    /// If this flag is set, the profile may only be valid if this condition is met. </summary>
    public readonly bool AndFlag => (flags & 0x1) != 0;
    /// <summary> Whether this condition may be ignored if another condition with the OrFlag is set. If no condition 
    /// sets this flag, the profile does not consider this. If multiple conditions set this flag, the profile will be valid
    /// only if one of the conditions within that group is met. </summary>
    public readonly bool OrFlag => (flags & 0x2) != 0;
    /// <summary> Whether this condition should be excluded when considering pathfinding the profile. In case animals(e.g. birds) may
    /// pathfind through locations that they can't generate on. This flag is ignored when placing the entity but is used to 
    /// ignore this condition when pathfinding. </summary>
    public readonly bool ExFlag => (flags & 0x4) != 0;
}

/// <summary> The job-safe representation of a <see cref="Guid"/>. Stores the data in a fixed byte array
/// that can be referenced in an unmanaged context. </summary>
[BurstCompile]
public struct JGuid{
    /// <summary> The raw data of the guid stored in a fixed byte array. </summary>
    [NativeDisableUnsafePtrRestriction]
    public unsafe fixed byte GuidData[16];

    /// <summary> Implicitly converts a <see cref="Guid"/> to a <see cref="JGuid"/>. </summary>
    /// <param name="guid">The GUID that is being set</param>
    public static implicit operator JGuid (Guid guid){
        JGuid jGuid = new JGuid();
        unsafe{
            byte* data = jGuid.GuidData;
            byte[] bytes = guid.ToByteArray();
            for(int i = 0; i < 16; i++) data[i] = bytes[i];
        }
        return jGuid;
    }
    /// <summary> Implicitly converts a <see cref="JGuid"/> to a <see cref="Guid"/>. </summary>
    /// <param name="jGuid">The guid stored in this object</param>
    public static implicit operator Guid (JGuid jGuid){
        Guid guid;
        unsafe{
            byte* data = jGuid.GuidData;
            byte[] bytes = new byte[16];
            for(int i = 0; i < 16; i++) bytes[i] = data[i];
            guid = new Guid(bytes);
        }
        return guid;
    }

    /// <summary> Implicitly converts a <see cref="string"/> to a <see cref="JGuid"/>. </summary>
    /// <param name="guid">The guid stored as a string that is being set</param>
    public static implicit operator JGuid (string guid){
        JGuid jGuid = new JGuid();
        unsafe{
            byte* data = jGuid.GuidData;
            byte[] bytes = Guid.Parse(guid).ToByteArray();
            for(int i = 0; i < 16; i++) data[i] = bytes[i];
        }
        return jGuid;
    }

    /// <summary> Implicitly converts a <see cref="JGuid"/> to a <see cref="string"/>. </summary>
    /// <param name="jGuid">The guid stored in this object</param>
    public static implicit operator string (JGuid jGuid){
        string guid;
        unsafe{
            byte* data = jGuid.GuidData;
            byte[] bytes = new byte[16];
            for(int i = 0; i < 16; i++) bytes[i] = data[i];
            guid = new Guid(bytes).ToString();
        }
        return guid;
    }    
}

/// <summary> A utility class to override serialization of <see cref="ProfileE"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ProfileE))]
public class MapDataDrawer : PropertyDrawer{
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
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
        
        flag = flags[0] ? flag | 0x1 : flag & 0xFFFFFFFE;
        flag = flags[1] ? flag | 0x2 : flag & 0xFFFFFFFD;
        flag = flags[2] ? flag | 0x4 : flag & 0xFFFFFFFB;
        flagProp.uintValue = flag;
    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 3;
    }
}
#endif
}


*/