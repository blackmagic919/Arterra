using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;
using System.Linq;
using Newtonsoft.Json;
using Unity.Burst;
using MapStorage;

namespace WorldConfig.Generation.Structure{
/// <summary>
/// A collection of settings that describe the contents of a structure
/// as well as shared metadata on its variants, and specifications on
/// how it should be generated in the world.
/// </summary>
[CreateAssetMenu(fileName = "Structure_Data", menuName = "Generation/Structure/Structure Data")]
public class StructureData : Category<StructureData>
{
    
    /// <summary>  See <see cref="StructureData.Settings"/> for more information. </summary>
    public Option<Settings> settings;
    /// <summary>
    /// The names of all materials within the external <see cref="Config.GenerationSettings.Materials"/> registry that
    /// are used in the structure. All entries that require references to a material may indicate the index within
    /// this list of the name of the material in the external registry.
    /// </summary>
    public Option<List<string>> Names;
    /// <summary>
    /// The map information contained by the structure. This is the list of map entries that 
    /// define the structure, linearly encoded through the dimensions specified in <see cref="Settings.GridSize"/>.
    /// The size of the map must be equal to the product of the dimensions specified in <see cref="Settings.GridSize"/>
    /// to avoid undefined behavior. The layout of each map entry also contains metadata on how it 
    /// should be placed in the world, see <see cref="PointInfo"/> for more information.
    /// </summary> <remarks> As structures need to define the entire grid space they occupy, it is recommended that 
    /// the size of structures be not too big to avoid excessive memory usage. </remarks>
    public Option<List<PointInfo>> map;
    /// <summary>
    /// The list of checks that need to be verified before a structure can be placed at a certain location.
    /// Each check samples the map to verify its information such that the time required to place a 
    /// structure grows linearly with the number of checks. See <see cref="CheckPoint"/> for more information.
    /// </summary>
    [SerializeField]
    public Option<List<CheckPoint>> checks;

    /// <summary> A getter property that deserializes the structure's map data by recoupling them with the current world's configuration. This involves
    /// retrieving the real indices of the materials within the external <see cref="WorldConfig.Config.GenerationSettings.Materials"/> registry. </summary>
    [JsonIgnore]
    public IEnumerable<PointInfo> SerializePoints{
        get{
            Catalogue<Material.MaterialData> reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            return map.value.Select(x => Serialize(x, reg.RetrieveIndex(Names.value[x.material])));
        }
    }
    private PointInfo Serialize(PointInfo x, int Index){
        x.material = Index;
        return x;
    }
    
    /// <summary> When modifiying a structure through the <see cref="DensityDeconstructor"/> editor, initializes the sturcture's map 
    /// to match the grid size of the structure. Additionally ensures that no elements of the structure are null. </summary>
    public void Initialize(){
        map.value ??= new List<PointInfo>((int)(settings.value.GridSize.x * settings.value.GridSize.y * settings.value.GridSize.z));
        checks.value ??= new List<CheckPoint>();
        Names.value ??= new List<string>();
    }

    /// <summary>
    /// A check that the structure must perform before it can be placed at a certain location
    /// in the world. A check involves sampling the map relative to its desired placement //
    /// location and orientation and verifying that the obtained map information is consistent
    /// with the set of requirements specified in <see cref="CheckInfo"/> .
    /// </summary>
    [System.Serializable]
    public struct CheckPoint
    {
        /// <summary>
        /// The offset in grid space from the structure's origin (the bottom left corner of the structure),
        /// of the map entry that the check will sample. This offset is subject to possible rotations 
        /// detailed by <see cref="Settings.randThetaRot"/> and <see cref="Settings.randPhiRot"/> whereby
        /// the location of the check will be rotated in the same manner as the structure's contents.
        /// </summary>
        public float3 position;
        /// <summary> The information that the sampled entry will be tested against. See <see cref="CheckInfo"/> for more information. </summary>
        public CheckInfo checkInfo; //bool is not yet blittable in shader 5.0

        /// <summary> Initializes a new check point. </summary>
        /// <param name="position">The offset from the structure origin of the check.</param>
        /// <param name="checkInfo">The requirements verified by the check.</param>
        public CheckPoint(float3 position, CheckInfo checkInfo)
        {
            this.position = position;
            this.checkInfo = checkInfo;
        }
    }

    /// <summary> The settings for a structure that describe its contents 
    /// and variations on how it can be generated. See <see hred="https://blackmagic919.github.io/AboutMe/2024/07/03/Structure-Placement/">
    /// here </see> for more information. </summary>
    [System.Serializable] 
    public struct Settings{
        /// <summary>
        /// The size of the structure in grid space that the structure occupies. The structure's <see cref="map">contents</see>
        /// must match these dimensions to avoid undefined behavior. Ignoring rotations, the structure contents will be 
        /// read with the third(z) component as the minor axis and the first(x) component as the major axis.
        /// </summary>
        public uint3 GridSize;
        /// <summary>
        /// The minimum level of detail that the structure can generate from. The minimum LoD is the size of the floor of the 
        /// largest side-length of the structure in chunk space. It must be less than or equal to <seealso cref="Generation.maxLoD"/>. 
        /// Larger structures will be generated less frequently by the nature of structure placement so a larger <see cref="minimumLOD"/>
        /// may inadvertently result in a lower density of structures. Incorrectly setting this value can result in corrupted generation. 
        /// </summary>
        public int minimumLOD;

        /// <summary> Whether or not placement of the structure may include random rotations around the vertical(y)-axis. Rotations are
        /// possible by changing the axis and order the structure's contents are read when being placed in the world.
        /// </summary>
        [Range(0, 1)]
        public uint randYRot;
        /// <summary>
        /// Whether or not placement of the structure may include random rotations around the major horizontal(x)-axis. The horizontal
        /// axis that it is rotated upon may shift depending on the <see cref="randYRot"/>. Rotations are possible by changing 
        /// the axis and order the structure's contents are read when being placed in the world.
        /// </summary>
        [Range(0, 1)]
        public uint randXRot;
        /// <summary>
        /// Whether or not placement of the structure may include random rotations around the mino horizontal(z)-axis. The horizontal
        /// axis that it is rotated upon may shift depending on the <see cref="randYRot"/> and <see cref="randXRot"/>. Rotations 
        /// are possible by changing the axis and order the structure's contents are read when being placed in the world.
        /// </summary>
        [Range(0, 1)]
        public uint randZRot;
        
    }
    /// <summary>
    /// The requirements that are tested against a map entry sampled by a <see cref="CheckPoint"/>. The
    /// ranges of different measurements that the map entry must fall within to be considered valid. See
    /// <see href="https://blackmagic919.github.io/AboutMe/2024/06/16/Structure-Pruning/">here</see> for more info.
    /// </summary>
    [System.Serializable]
    public struct CheckInfo{
        /// <summary>
        /// The raw data of the bounds. Each range is defined by a 2-byte short whereby the bottom and top bounds of
        /// each range occupy the first and second byte of the short respectively. Bitmasking is used
        /// to retrieve the bounds of each range during generation.
        /// </summary>
        public uint data;
        /// <summary>
        /// The lower bound of the liquid density that the map entry must be above to be considered valid.
        /// The liquid density is defined by <see cref="CPUDensityManager.MapData.LiquidDensity"/>. 
        /// </summary>
        public uint MinLiquid{
            readonly get => data & 0xFF;
            set => data = (data & 0xFFFFFF00) | (value & 0xFF);
        }
        
        /// <summary>
        /// The upper bound of the liquid density that the map entry must be below to be considered valid.
        /// The liquid density is defined by <see cref="CPUDensityManager.MapData.LiquidDensity"/>.
        /// </summary>
        public uint MaxLiquid{
            readonly get => (data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | ((value & 0xFF) << 8);
        }

        /// <summary>
        /// The lower bound of the solid density that the map entry must be above to be considered valid.
        /// The solid density is defined by <see cref="CPUDensityManager.MapData.SolidDensity"/>.
        /// </summary>
        public uint MinSolid{
            readonly get => (data >> 16) & 0xFF;
            set => data = (data & 0xFF00FFFF) | ((value & 0xFF) << 16);
        }

        /// <summary>
        /// The upper bound of the solid density that the map entry must be below to be considered valid.
        /// The solid density is defined by <see cref="CPUDensityManager.MapData.SolidDensity"/>.
        /// </summary>
        public uint MaxSolid{
            readonly get => (data >> 24) & 0xFF;
            set => data = (data & 0x00FFFFFF) | ((value & 0xFF) << 24);
        }

        /// <summary>
        /// Determines whether or not the specified map entry satisifies the check's
        /// bounds. A map entry satisifes the check's bounds if its density and viscosity
        /// fall between the defined lower and upper limits of the check.
        /// </summary>
        /// <param name="pt">The map data which is tested against the bounds</param>
        /// <returns>Whether or not the <paramref name="pt"/> is within the bound</returns>
        [BurstCompile]
        public readonly bool Contains(in MapData pt){
            return pt.LiquidDensity >= (data & 0xFF) && pt.LiquidDensity <= ((data >> 8) & 0xFF) && 
               pt.SolidDensity >= ((data >> 16) & 0xFF) && pt.SolidDensity <= ((data >> 24) & 0xFF);
        }
        /// <summary> Whether or not the check's bounds are null, meaning that
        /// the check is always valid. Equivalent the min and max bounds being
        /// set to their maximum range. </summary>
        public bool IsNull => data == 0xFF00FF00;
    }

    /// <summary> A single map entry contained by the structure. Contains <see cref="CPUDensityManager.MapData">
    /// standard map information</see> as well as metadata on how it should be interpreted when the 
    /// structured is placed. </summary>
    [System.Serializable]
    public struct PointInfo
    {
        /// <summary>
        /// The raw map data. Map information is stored in sections of this 4 byte integer and may be
        /// retrieved through bit masking, like how it is done during structure generation.
        /// </summary>
        public uint data;

        /// <summary> Whether or not the map entry should be copied exactly as it is when being placed. The default
        /// behavior is to take the maximum density or viscosity between the structure and the map entry it is
        /// replacing; this is to allow for smoother transitions between structures and terrain. If multiple structures
        /// contain multiple overlaping points set to preserve, the maximum density and viscosity will be taken only of those 
        /// specific points.</summary> <remarks>The highest bit of <see cref="data"/> is used as this flag</remarks>
        public bool preserve{ 
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        /// <summary> The density of the map entry. Must be less than or equal to <c>255</c>. 
        /// Occupies the lowest byte of <see cref="data"/>. </summary>
        public int density
        {
            readonly get => (int)data & 0xFF;
            set => data = (data & 0xFFFFFF00) | ((uint)value & 0xFF);
        }

        /// <summary> The viscosity of the map entry. Must always be less than or equal to <see cref="density"/>. 
        /// Occupies the second lowest byte of <see cref="data"/>. </summary>
        public int viscosity
        {
            readonly get => (int)(data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | (((uint)value & 0xFF) << 8);
        }

        /// <summary> The index of the name of the material within <see cref="Materials"/>. When used during terrain generation,
        /// this value is recoupled with the external <see cref="Config.GenerationSettings.Materials"/> registry by 
        /// retrieving the index of the material name within the registry.  </summary>
        public int material
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16);
        }
    }
}

/// <summary> A utility class to override serialization of <see cref="StructureData.PointInfo"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(StructureData.PointInfo))]
public class StructPointDrawer : PropertyDrawer{
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty dataProp = property.FindPropertyRelative("data");
        uint data = dataProp.uintValue;

        //bool isDirty = (data & 0x80000000) != 0;
        bool preserve = (data & 0x80000000) != 0;
        uint viscosity = (data >> 8) & 0xFF;
        uint density = data & 0xFF;

        Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        EditorGUI.LabelField(rect, label);//
        rect.y += EditorGUIUtility.singleLineHeight;

        RegistryReferenceDrawer.SetupRegistries();
        RegistryReferenceDrawer materialDrawer = new RegistryReferenceDrawer { BitMask = 0x7FFF, BitShift = 16 };
            materialDrawer.DrawRegistryDropdown(rect, dataProp, new GUIContent("Material"),
                Config.TEMPLATE.Generation.Materials.value.MaterialDictionary);
        rect.y += EditorGUIUtility.singleLineHeight;

        viscosity = (uint)EditorGUI.IntField(rect, "Viscosity", (int)viscosity);
        rect.y += EditorGUIUtility.singleLineHeight;
        density = (uint)EditorGUI.IntField(rect, "Density", (int)density);
        rect.y += EditorGUIUtility.singleLineHeight;
        preserve = EditorGUI.Toggle(rect, "Preserve", preserve);
        rect.y += EditorGUIUtility.singleLineHeight;

        //data = (isDirty ? data | 0x80000000 : data & 0x7FFFFFFF);
        data = preserve ? data | 0x80000000 : data & 0x7FFFFFFF;
        data = (data & 0xFFFF00FF) | ((viscosity & 0xFF) << 8);
        data = (data & 0xFFFFFF00) | (density & 0xFF);

        dataProp.uintValue = (data & 0x8000FFFF) | (dataProp.uintValue & 0x7FFF0000);
    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 5;
    }
}

/// <summary> A utility class to override serialization of <see cref="StructureData.CheckInfo"/> into a Unity Inspector format.
/// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
[CustomPropertyDrawer(typeof(StructureData.CheckInfo))]
public class StructCheckDrawer : PropertyDrawer{
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

        SerializedProperty dataProp = property.FindPropertyRelative("data");
        uint data = dataProp.uintValue;

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

        EditorGUI.MultiIntField(rect, new GUIContent[] { new("Liquid L"), new("Liquid U") }, densityB);
        rect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.MultiIntField(rect, new GUIContent[] { new("Solid L"), new("Solid U") }, viscosityB);
        rect.y += EditorGUIUtility.singleLineHeight;


        data = (data & 0xFFFFFF00) | ((uint)densityB[0] & 0xFF);
        data = (data & 0xFFFF00FF) | (((uint)densityB[1] & 0xFF) << 8);
        data = (data & 0xFF00FFFF) | (((uint)viscosityB[0] & 0xFF) << 16);
        data = (data & 0x00FFFFFF) | (((uint)viscosityB[1] & 0xFF) << 24);
        dataProp.uintValue = data;
    }

    /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        bool isExpanded = _foldouts.TryGetValue(property.propertyPath, out bool val) && val;
        return isExpanded ? EditorGUIUtility.singleLineHeight * 3 : EditorGUIUtility.singleLineHeight;
    }
}
#endif
}