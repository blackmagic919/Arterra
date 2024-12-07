using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;
using System.Linq;
using Newtonsoft.Json;

[CreateAssetMenu(fileName = "Structure_Data", menuName = "Generation/Structure/Structure Data")]
public class StructureData : ScriptableObject
{
    public Option<Settings> settings;
    public Option<List<PointInfo>> map;
    [SerializeField]
    public Option<List<CheckPoint>> checks;
    public Option<List<string>> Materials;

    [JsonIgnore]
    public IEnumerable<PointInfo> SerializePoints{
        get{
            Registry<MaterialData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
            return map.value.Select(x => Serialize(x, reg.RetrieveIndex(Materials.value[x.material])));
        }
    }

    PointInfo Serialize(PointInfo x, int Index){
        x.material = Index;
        return x;
    }
    
    public void Initialize(){
        map.value ??= new List<PointInfo>((int)(settings.value.GridSize.x * settings.value.GridSize.y * settings.value.GridSize.z));
        checks.value ??= new List<CheckPoint>();
        Materials.value ??= new List<string>();
    }

    [System.Serializable]
    public struct CheckPoint
    {
        public float3 position;
        public CheckInfo checkInfo; //bool is not yet blittable in shader 5.0

        public CheckPoint(float3 position, CheckInfo checkInfo)
        {
            this.position = position;
            this.checkInfo = checkInfo;
        }
    }

    [System.Serializable] 
    public struct Settings{
        public uint3 GridSize;
        public int minimumLOD;
        [Range(0, 1)]
        public uint randThetaRot;
        [Range(0, 1)]
        public uint randPhiRot;
    }
    [System.Serializable]
    public struct CheckInfo{
        public uint data;
        public uint MinDensity{
            readonly get => data & 0xFF;
            set => data = (data & 0xFFFFFF00) | (value & 0xFF);
        }

        public uint MaxDensity{
            readonly get => (data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | ((value & 0xFF) << 8);
        }

        public uint MinViscosity{
            readonly get => (data >> 16) & 0xFF;
            set => data = (data & 0xFF00FFFF) | ((value & 0xFF) << 16);
        }

        public uint MaxViscosity{
            readonly get => (data >> 24) & 0xFF;
            set => data = (data & 0x00FFFFFF) | ((value & 0xFF) << 24);
        }
    }

    [System.Serializable]
    public struct PointInfo
    {
        public uint data;

        public bool preserve{ 
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        public int density
        {
            readonly get => (int)data & 0xFF;
            set => data = (data & 0xFFFFFF00) | ((uint)value & 0xFF);
        }

        public int viscosity
        {
            readonly get => (int)(data >> 8) & 0xFF;
            set => data = (data & 0xFFFF00FF) | (((uint)value & 0xFF) << 8);
        }

        public int material
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16);
        }
    }
}
#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(StructureData.PointInfo))]
public class StructPointDrawer : PropertyDrawer{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty dataProp = property.FindPropertyRelative("data");
        uint data = dataProp.uintValue;

        //bool isDirty = (data & 0x80000000) != 0;
        bool preserve = (data & 0x80000000) != 0;
        uint material = (data >> 16) & 0x7FFF;
        uint viscosity = (data >> 8) & 0xFF;
        uint density = data & 0xFF;

        Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        material = (uint)EditorGUI.IntField(rect, "Material", (int)material);
        rect.y += EditorGUIUtility.singleLineHeight;
        viscosity = (uint)EditorGUI.IntField(rect, "Viscosity", (int)viscosity);
        rect.y += EditorGUIUtility.singleLineHeight;
        density = (uint)EditorGUI.IntField(rect, "Density", (int)density);
        rect.y += EditorGUIUtility.singleLineHeight;
        preserve = EditorGUI.Toggle(rect, "Preserve", preserve);
        rect.y += EditorGUIUtility.singleLineHeight;
        //isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
        //rect.y += EditorGUIUtility.singleLineHeight;

        //data = (isDirty ? data | 0x80000000 : data & 0x7FFFFFFF);
        data = preserve ? data | 0x80000000 : data & 0x7FFFFFFF;
        data = (data & 0x8000FFFF) | (material << 16);
        data = (data & 0xFFFF00FF) | ((viscosity & 0xFF) << 8);
        data = (data & 0xFFFFFF00) | (density & 0xFF);

        dataProp.uintValue = data;
    }

    // Override this method to make space for the custom fields
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 4;
    }
}


[CustomPropertyDrawer(typeof(StructureData.CheckInfo))]
public class StructCheckDrawer : PropertyDrawer{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
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

        Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Density L"), new ("Density U") }, densityB);
        rect.y += EditorGUIUtility.singleLineHeight;
        EditorGUI.MultiIntField(rect, new GUIContent[] { new ("Viscosity L"), new ("Viscosity U") }, viscosityB);
        rect.y += EditorGUIUtility.singleLineHeight;


        data = (data & 0xFFFFFF00) | ((uint)densityB[0] & 0xFF);
        data = (data & 0xFFFF00FF) | (((uint)densityB[1] & 0xFF) << 8);
        data = (data & 0xFF00FFFF) | (((uint)viscosityB[0] & 0xFF) << 16);
        data = (data & 0x00FFFFFF) | (((uint)viscosityB[1] & 0xFF) << 24);
        dataProp.uintValue = data;
    }

    // Override this method to make space for the custom fields
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 2;
    }
}
#endif