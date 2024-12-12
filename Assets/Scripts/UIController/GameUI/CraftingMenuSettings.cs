using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

[CreateAssetMenu(menuName = "Settings/Crafting")]
public class CraftingMenuSettings : ScriptableObject{
    public int GridWidth; //3
    public int CraftSpeed; //200
    public int PointSizeMultiplier; //2
    public uint CraftingIsoValue; //128

    public int MaxRecipeDistance; //64
    public int NumMaxSelections; //5
    public Registry<Recipe> Recipes; 

    [Serializable]
    public struct Recipe{
        public Option<List<string>> Names;
        public Option<List<CPUDensityManager.MapData> > entry;
        public Result result;
        
        [JsonIgnore]
        public readonly int ResultMat{
            get {
                Registry<MaterialData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
                return reg.RetrieveIndex(Names.value[(int)result.Index]);
            }
        }

        public readonly CPUDensityManager.MapData EntrySerial(int Index){
            Registry<MaterialData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
            CPUDensityManager.MapData p = entry.value[Index];
            p.material = reg.RetrieveIndex(Names.value[p.material]);
            return p;
        }
        public readonly int EntryMat(int index){
            if(entry.value[index].isDirty) return -1;
            if(Names.value == null) return entry.value[index].material;
            Registry<MaterialData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
            return reg.RetrieveIndex(Names.value[entry.value[index].material]);
        }

        [Serializable]
        public struct Result{
            [HideInInspector] public uint data;
            [JsonIgnore]
            public bool IsItem{
                readonly get => (data & 0x80000000) != 0;
                set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
            }
            [JsonIgnore]
            public bool EntryType{
                readonly get => (data & 0x40000000) != 0;
                set => data = value ? data | 0x40000000 : data & 0xBFFFFFFF;
            }
            [JsonIgnore]
            public uint Index{
                readonly get => (data >> 15) & 0x7FFF;
                set => data = (data & 0xC0007FFF) | (value << 15);
            }
            [JsonIgnore]
            public float Multiplier{
                readonly get => (data & 0x7FFF) / 0xFF;
                set => data = (data & 0xFFFF8000) | (((uint)math.round(value * 0xFF)) & 0x7FFF);
            }
            [JsonIgnore]
            public bool IsSolid => EntryType;
            [JsonIgnore]            
            public bool IsUnstackable => EntryType;
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(Recipe.Result))]
    public class RecipeResultDrawer : PropertyDrawer{
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty dataProp = property.FindPropertyRelative("data");
            uint data = dataProp.uintValue;

            bool isItem = (data & 0x80000000) != 0;
            uint index = (data >> 15) & 0x7FFF;
            float multiplier = (data & 0x7FFF) / 255f;
            bool isSolid = (data & 0x40000000) != 0;

            Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            index = (uint)EditorGUI.IntField(rect, "Index", (int)index);
            rect.y += EditorGUIUtility.singleLineHeight;
            multiplier = EditorGUI.FloatField(rect, "Multiplier", multiplier);
            rect.y += EditorGUIUtility.singleLineHeight;
            isSolid = EditorGUI.Toggle(rect, "Is Solid", isSolid);
            rect.y += EditorGUIUtility.singleLineHeight;
            isItem = EditorGUI.Toggle(rect, "Is Item", isItem);
            rect.y += EditorGUIUtility.singleLineHeight;

            data = (isItem ? data | 0x80000000 : data & 0x7FFFFFFF);
            data = (data & 0xC0007FFF) | (index << 15);
            data = (data & 0xFFFF8000) | ((uint)Mathf.Round(multiplier * 255f) & 0x7FFF);
            data = (isSolid ? data | 0x40000000 : data & 0xBFFFFFFF);

            dataProp.uintValue = data;
        }

        // Override this method to make space for the custom fields
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 4;
        }
    }
#endif
}
