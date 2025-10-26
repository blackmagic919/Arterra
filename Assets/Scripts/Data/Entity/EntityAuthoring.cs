using System;
using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using UnityEditor;
using System.Collections.Generic;
using WorldConfig.Generation.Structure;
namespace WorldConfig.Generation.Entity{
/// <summary>
/// A generic contract that ensures that all entities contain a certain
/// set of properties and methods necessary for the system to function.
/// </summary>
public abstract class Authoring : Category<Authoring>{
    /// <summary> A reference to the entity that contains the actual instance of the entity. See <see cref="Entity"/> for more information</summary>
    public virtual Entity Entity{get; }
    /// <summary> A reference to the readonly shared settings that all instances of this entity uses. See <see cref="EntitySetting"/> for more information</summary>
    public virtual EntitySetting Setting{get; set;}
    /// <summary> A reference to the controller responsible for displaying the entity. The controller is the visual gameobject
    /// representing the entity whose display is managed by Unity. </summary>
    [UISetting(Ignore = true)][JsonIgnore]
    public Option<GameObject> Controller;
    /// <summary> A list of points defining the profile of the entity, the list is linearly encoded through the dimensions
    /// specified in <see cref="EntitySetting.ProfileInfo"/>. See <see cref="ProfileE"/> for more information </summary>
    public Option<List<ProfileE>> Profile;
}


/// <summary>
/// A generic entity that defines a virtual function table that all entities must implement,
/// as well as some metadata used by the system to manage the entity. All entities in the game
/// must inherit from this class and implement the virtual functions to be used by the system.
/// </summary>
public abstract class Entity: IRegistered{
    /// <summary> Information about the entity instance that is required of every instance for the system to function. See <see cref="Info"/> for more information. </summary>
    public Info info; 
    /// <summary> Whether or not the entity is active. This is the flag set to indicate to the system that
    /// the entity has been released. Once this flag is set to false, it cannot be set to true without risking race
    /// conditions and undefined behavior. </summary>
    public bool active;

    /// <summary> The entity's virtual update function. This will be called every game tick within a Unity Job to 
    /// perform computational-heavy tasks related to the entity. Only the Entity in question is provided mutual
    /// exclusivity. Accessing any external resources(e.g. creating an entity) needs to resynchronize using <see cref="EntityManager.AddHandlerEvent(Action)"/>
    /// </summary>
    public abstract void Update();
    /// <summary> A callback when the entity is disabled. This will be called whenever the system attempts to destroy an 
    /// entity and should be used to release any resources tied with it. An entity should assume it is destroyed after 
    /// processing this callback. </summary> 
   public abstract void Disable();
    /// <summary>
    /// Initializes the entity's instance. Called when creating an instance of the entity.
    /// The callee may preset any default values during this process but it <b>must</b> guarantee
    /// that the entity returned is fully populated (i.e. virtual functions all set).
    /// </summary>
    /// <param name="setting">The setting of the entity. Specific to the authoring entry it's instantiated from. </param>
    /// <param name="controller">The controller responsible for displaying the entity. Passed from <see cref="Authoring.Controller"/> </param>
    /// <param name="GCoord">The position in grid space the entity was placed at. </param>
    public abstract void Initialize(EntitySetting setting, GameObject controller, float3 GCoord);
    /// <summary>
    /// Deserializes the entity's instance. Some of the entity's information may be retrieved from serialization
    /// while others may need to be thrown away. This function is called when the entity is deserialized 
    /// in case the entity needs to reframe its information.
    /// </summary>
    /// <param name="setting">The setting of the entity. Specific to the authoring entry it's instantiated from. </param>
    ///  <param name="controller">The controller responsible for displaying the entity. Passed from <see cref="Authoring.Controller"/> </param>
    /// <param name="GCoord">The position in grid space the entity was placed at. </param>
    public abstract void Deserialize(EntitySetting setting, GameObject controller, out int3 GCoord);
    /// <summary> The transform of the entity used for positioning and collision detection. </summary>
    [JsonIgnore]
    public abstract ref TerrainCollider.Transform transform { get; }
    /// <summary>A single line property for retrieving and setting the entity's position in grid space. Most entities 
    /// require knowledge of other entity's positions; however entities that aren't spatially bound may not fulfill 
    /// this contract if it no system requires its location.  </summary>
    [JsonIgnore] 
    public float3 position {
        get => transform.position + transform.size / 2;
        set => transform.position = value - transform.size / 2;
    }
    /// <summary>A single line property for retrieving and setting the entity's origin in grid space.
    /// Unlike <see cref="position"/>, the origin is the lowest point within the object's collider while
    /// the position describes the center of the collider. </summary>
    [JsonIgnore] 
    public ref float3 origin => ref transform.position;
    /// <summary>A single line property for retrieving and setting the entity's velocity. Most entities
    /// require knowledge of other entity's velocities; however entities that aren't spatially bound
    /// may not fulfill this contract if it no system requires its velocity. </summary>
    [JsonIgnore]
    public ref float3 velocity => ref transform.velocity;
    [JsonIgnore]
    public virtual Quaternion Facing => transform.rotation;
    [JsonIgnore]
    public float3 Forward => Facing * Vector3.forward;
    [JsonIgnore]
    public float3 Right => Facing * Vector3.right;
    /// <summary>
    /// A callback to draw any gizmos that the entity may need to draw. This is only for 
    /// debugging purposes in UnityEditor to draw any annotations related to entities. This will
    /// not be called in a build.
    /// </summary>
    public virtual void OnDrawGizmos(){}
    
    /// <summary>
    /// Settings for a structure that is required for the systems governing how entities are identified
    /// and co-exist in the world. An incorrect setting can lead to not only invalidation of the entity
    /// but corruption of the entire entity system. This may be read from within the entity's type
    /// but should not be modified except by the <see cref="EntityManager"> entity system </see>
    /// </summary>
    [System.Serializable]
    public struct Info{
        /// <summary> The unique identifier of the entity that remains the same throughout the entity's life cycle, regardless of if it's serialized
        /// or saved to disk. </summary>
        public Guid entityId;
        /// <summary> The index of the entity's name within the <see cref="WorldConfig.Config.GenerationSettings.Entities"/> registry. This is guaranteed to be different
        /// for two different types of entities and can be used to test such. If the entity is serialized, this should be decoupled and recoupled upon deserialization. </summary>
        public uint entityType;
    }

    /// <summary>
    /// Gets the <see cref="WorldConfig.Generation.Entity">registry</see> containing all entities when the game is loaded. Used to serialize
    /// and deserialize entities to json. See <see cref="IRegistered.GetRegistry"/> for more info.
    /// </summary>
    /// <returns>The Registry containing all entities within the game</returns>
    public IRegister GetRegistry() => Config.CURRENT.Generation.Entities;
    /// <summary> Gets the index within the <see cref="WorldConfig.Generation.Entity">entity registry</see> of the current
    /// entity's name. Equivalent to <see cref="Info.entityType"/>. See <see cref="IRegistered.Index"/> for more info. </summary>
    public int Index{
        get => (int)info.entityType;
        set => info.entityType = (uint)value;
    }
}

/// <summary> An interface for all authoring-specific settings used by entities. A setting itself does not need to 
/// define any members that has to be known externally so this is an empty contract and in effect no 
/// different than an <see cref="object"/>, but it offers clarity as to what the object is used for. Explicitly
/// must be managed class so instances do individually waste memory on settings. </summary>
public abstract class EntitySetting{
    /// <summary> The profile information of the entity. See <see cref="ProfileInfo"/> for more information. </summary>
    [JsonIgnore]
    public ProfileInfo profile;
    /// <summary> The actual dimensions of the entity used for collisions and hit-box detection </summary>
    public TerrainCollider.Settings collider;
    /// <summary> Presets any information shared by all instances of the entity. This is only called once per entity type within
    /// the <see cref="Config.GenerationSettings.Entities"> entity register </see> and is used to set up any shared readonly information. </summary>
    /// <param name="entityType">The index of the entity type within the <see cref="WorldConfig.Config.GenerationSettings.Entities"/> registry.</param>
    /// <remarks> For example, if the entity uses a state machine it can allocate function pointers to each state within the machine such that
    /// they may be referenced through an edge list. </remarks>
    public virtual void Preset(uint entityType){
        //Preset any default values here
    }
    /// <summary> A callback to release any information set by <see cref="Preset"/>. Called once per entity type within the 
    /// <see cref="Config.GenerationSettings.Entities"> entity register </see> before the game is closed.  </summary>
    public virtual void Unset(){
        //Unset preset values here
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
        [NonSerialized][HideInInspector][UISetting(Ignore = true)][JsonIgnore]
        public uint profileStart;
    }
}


/// <summary>
/// A single condition that is considered when verifying an entity's <see cref="EntitySetting.ProfileInfo"> profile </see>.
/// This is a condition that is tested against a point in space to verify that the entity can exist there. The 
/// condition may also define flags for how it should be evaluated when considering the entity's combined profile.
/// </summary>
[Serializable]
public struct ProfileE {
    /// <summary>
    /// A bitmask representing two conditions that a map entry must fall within to be considered a match. Divided
    /// into two-byte shorts where each short represents a range defined by the bounds stored within the high and 
    /// low byte. The high short is used for viscosity and the low short for density. See <see cref="StructureData.CheckInfo"/>
    /// for more information.
    /// </summary>
    public StructureData.CheckInfo bounds;
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


/// <summary> A utility class to override serialization of <see cref="ProfileE"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(ProfileE))]
public class MapDataDrawer : PropertyDrawer{
    private static readonly Dictionary<string, bool> _foldouts = new();
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        string path = property.propertyPath;
        if (!_foldouts.ContainsKey(path))
            _foldouts[path] = false;

        _foldouts[path] = EditorGUI.Foldout(
            new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight),
            _foldouts[path], label, true); // triangle on left, label is clickable

        if (!_foldouts[path]) return;
        
        SerializedProperty boundProp = property.FindPropertyRelative("bounds").FindPropertyRelative("data");
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
        
        Rect rect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        rect.y += EditorGUIUtility.singleLineHeight;
        
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Liquid L"), new ("Liquid U") }, densityB);
        rect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Solid L"), new ("Solid U") }, viscosityB);
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
        bool isExpanded = _foldouts.TryGetValue(property.propertyPath, out bool val) && val;
        return isExpanded ? EditorGUIUtility.singleLineHeight * 4 : EditorGUIUtility.singleLineHeight;
    }
}
#endif
}


