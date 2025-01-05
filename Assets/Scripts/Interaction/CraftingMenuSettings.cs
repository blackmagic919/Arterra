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
        public readonly int ResultIndex{
            get {
                Registry<ItemAuthoring> reg = WorldOptions.CURRENT.Generation.Items;
                return reg.RetrieveIndex(Names.value[(int)result.Index]);
            }
        }

        [JsonIgnore]
        public readonly IItem ResultItem{
            get {
                Registry<ItemAuthoring> reg = WorldOptions.CURRENT.Generation.Items;
                return reg.Retrieve(Names.value[(int)result.Index]).Item;
            }
        }

        public readonly CPUDensityManager.MapData EntrySerial(int Index){
            Registry<MaterialData> reg = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;
            CPUDensityManager.MapData p = entry.value[Index];
            if(!reg.Contains(Names.value[p.material])) return p;
            p.material = reg.RetrieveIndex(Names.value[p.material]);
            return p;
        }
        public readonly int EntryMat(int index){
            if(entry.value[index].isDirty) return -1;
            if(Names.value == null) return entry.value[index].material;
            Registry<MaterialData> reg = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary;
            return reg.RetrieveIndex(Names.value[entry.value[index].material]);
        }

        [Serializable]
        public struct Result{
            [HideInInspector] public uint data;
            [JsonIgnore]
            public uint Index{
                readonly get => (data >> 16) & 0xFFFF;
                set => data = (data & 0x0000FFFF) | (value << 16);
            }
            [JsonIgnore]
            public float Multiplier{
                readonly get => (data & 0xFFFF) / 0xFF;
                set => data = (data & 0xFFFF0000) | (((uint)math.round(value * 0xFF)) & 0xFFFF);
            }
        }
    }

#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(Recipe.Result))]
    public class RecipeResultDrawer : PropertyDrawer{
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty dataProp = property.FindPropertyRelative("data");
            uint data = dataProp.uintValue;

            uint index = (data >> 16) & 0xFFFF;
            float multiplier = (data & 0xFFFF) / 255f;

            Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            index = (uint)EditorGUI.IntField(rect, "Index", (int)index);
            rect.y += EditorGUIUtility.singleLineHeight;
            multiplier = EditorGUI.FloatField(rect, "Multiplier", multiplier);
            rect.y += EditorGUIUtility.singleLineHeight;

            data = (data & 0x0000FFFF) | (index << 16);
            data = (data & 0xFFFF0000) | ((uint)Mathf.Round(multiplier * 255f) & 0xFFFF);

            dataProp.uintValue = data;
        }

        // Override this method to make space for the custom fields
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2;
        }
    }
#endif
}
