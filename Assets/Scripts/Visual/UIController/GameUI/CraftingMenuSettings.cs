using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Item;

namespace WorldConfig.Intrinsic{

/// <summary>
/// Settings controlling the apperance and behavior of the crafting system. The crafting 
/// system allows the player to create items from materials enabling a wider variety of 
/// content and functionality within the game.
/// </summary>
[CreateAssetMenu(menuName = "Settings/Crafting")]
public class Crafting : ScriptableObject{
    /// <summary> The width of the square grid in terms of grid spaces. As recipe entries are stored on 
    /// grid corners, the amount of entries necessary to fill the grid is <c> (GridWidth + 1)^2 </c>. 
    /// If the grid width changes all recipes must be modified to refit the new grid size. </summary>
    public int GridWidth; //3
    /// <summary> How fast the user can place materials in the crafting grid, measured in change in density
    /// per frame. As placement of materials is a continuous process, the speed effects how quickly
    /// the player can craft items. </summary>
    public int CraftSpeed; //200
    /// <summary> When a user's cursor hovers closer to a grid corner, how large the grid corner point grows.
    /// This is used to help the user see where they are placing materials.  </summary>
    public int PointSizeMultiplier; //2
    /// <summary> The IsoValue that is used to create the visual representation of the crafted recipe. Though this is
    /// seperately configurable, it should generally be the same as <see cref="WorldConfig.Quality.Terrain.IsoLevel"/>
    /// to avoid confusion. </summary>
    public uint CraftingIsoValue; //128

    /// <summary> The maximum L1 distance between a recipe entry and the player's crafting grid
    /// for the recipe to be displayed in the crafting menu. The point is to show only the 
    /// closest recipes to the player's input to avoid cluttering the crafting menu. </summary>
    public int MaxRecipeDistance; //64
    /// <summary> The maximum amount of selections that will be displayed under the crafting grid.
    /// This is the maximum amount of recipes that will be shown in case too many recipes are
    /// within <see cref="MaxRecipeDistance"/> of the player's crafting grid. </summary>
    public int NumMaxSelections; //5
    /// <summary> The registry of all recipes that can be crafted within the game. See <see cref="Recipe"/> for more information. </summary>
    public Registry<Recipe> Recipes; 

    /// <summary>
    /// A recipe describing a configuration of materials on the crafting grid
    /// that will create a specific item. Modifying recipes create an unfair advantage
    /// or upend the difficulty progression. 
    /// </summary>
    [Serializable]
    public struct Recipe{
        /// <summary> A list of names within external registries used by entries referencing external resources. When an entry
        /// referes to an entry in an external registry it should refer to the index within this list of the name
        /// of the entry in the external registry. </summary>
        public Option<List<string>> Names;
        /// <summary> The entries that must be matched to successfully craft this recipe. The entries
        /// correspond to a grid of points whose size is dictated by <see cref="Crafting.GridWidth"/>.
        /// If this grid matches  the player's crafting grid, the recipe is considered
        /// craftable and the result may be obtained. No two recipes should be identical,
        /// although this is not enforced. </summary> <remarks>The <see cref="CPUMapManager.MapData.isDirty"/> flag
        /// is repurposed to indicate if an entry should be ignored when being matched. This is useful if a component
        /// of the recipe is not essential to its creation, such as any empty entries. </remarks>
        public Option<List<CPUMapManager.MapData> > entry;
        /// <summary> If the recipe can be crafted, the result that is given to the player if the recipe is crafted.
        /// <see cref="Result"/> for more information. </summary>
        public Result result;
        
        /// <summary> The index of the result item in the external <see cref="Config.GenerationSettings.Items"> item registry </see>.
        /// By default, the item stored in <see cref="Result.Index"/> is coupled to the <see cref="Names"/> list. This 
        /// property is used to recouple it with the item registry of the current world. </summary>
        [JsonIgnore]
        public readonly int ResultIndex{
            get {
                Registry<Authoring> reg = Config.CURRENT.Generation.Items;
                return reg.RetrieveIndex(Names.value[(int)result.Index]);
            }
        }

        /// <summary> The result item of the recipe. This is the actual item given to the player if the recipe is crafted.
        /// Obtained by retrieving the item indicated by <see cref="ResultIndex"/> from the item registry. </summary>
        [JsonIgnore]
        public readonly IItem ResultItem{
            get {
                Registry<Authoring> reg = Config.CURRENT.Generation.Items;
                return reg.Retrieve(Names.value[(int)result.Index]).Item;
            }
        }

        /// <summary> Obtains the map entry at a specific grid index in the recipe. Accesses the
        /// recipe's entry stored in <see cref="entry"/> at the specified index and
        /// deserializes(recouples) any external references to the map entry's material. </summary>
        /// <param name="Index">The index within <see cref="entry"/> of the entry that is retrieved</param>
        /// <returns>The deserialized map information of the recipe's entry at the specified <paramref name="Index"/>.</returns>
        public readonly CPUMapManager.MapData EntrySerial(int Index){
            Registry<MaterialData> reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            CPUMapManager.MapData p = entry.value[Index];
            if(!reg.Contains(Names.value[p.material])) return p;
            p.material = reg.RetrieveIndex(Names.value[p.material]);
            return p;
        }

        /// <summary> Obtains the material index of the map entry at a specific grid index in the recipe. 
        /// If the entry is dirty, the material is ignored as it is not part of the real recipe
        /// and is not used when testing whether the recipe is craftable. </summary>
        /// <param name="index">The index within <see cref="entry"/> of the entry whose material is retrieved</param>
        /// <returns>The material of the entry at the specified <paramref name="index"/></returns>
        public readonly int EntryMat(int index){
            if(entry.value[index].isDirty) return -1;
            if(Names.value == null) return entry.value[index].material;
            Registry<MaterialData> reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            return reg.RetrieveIndex(Names.value[entry.value[index].material]);
        }

        /// <summary> The result of a recipe. Indicates what is given to the player if the recipe is crafted. </summary>
        [Serializable]
        public struct Result{
            /// <summary>
            /// The bitmap containing the result's information. The low 2-bytes indicate the 
            /// <see cref="Index"/> and the high 2-bytes indicate the <see cref="Multiplier"/>.
            /// </summary>
            [HideInInspector] public uint data;
            /// <summary>
            /// The index within the <see cref="Names"> name registry </see> of the name of the entry
            /// within the external <see cref="Config.GenerationSettings.Items"> item registry </see> of the item
            /// that is given to the player if the recipe is crafted. 
            /// </summary>
            [JsonIgnore]
            public uint Index{
                readonly get => (data >> 16) & 0xFFFF;
                set => data = (data & 0x0000FFFF) | (value << 16);
            }
            /// <summary> When an item is crafted, the amount of the item that is created is taken by adding all the 
            /// amounts of materials used in creating the recipe and multiplying it by the multiplier. </summary>
            [JsonIgnore]
            public float Multiplier{
                readonly get => (data & 0xFFFF) / 0xFF;
                set => data = (data & 0xFFFF0000) | (((uint)math.round(value * 0xFF)) & 0xFFFF);
            }
        }
    }

#if UNITY_EDITOR
    /// <summary> A utility class to override serialization of <see cref="Recipe.Result"/> into a Unity Inspector format.
    /// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
    [CustomPropertyDrawer(typeof(Recipe.Result))]
    public class RecipeResultDrawer : PropertyDrawer{
        /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
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

        /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2;
        }
    }
#endif
}
}
