using UnityEditor;
using UnityEngine;
using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Arterra.Config.Generation.Item;
using Unity.Mathematics;
using System.Runtime.InteropServices;
namespace Arterra.Config.Intrinsic{

    /// <summary> An authoring object for a crafting recipe allowing for it 
    /// to be created via Unity's Inspector. </summary>
    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Crafting/Recipe")]
    public class CraftingAuthoring : Category<CraftingAuthoring>, ISlot {
        /// <summary> A list of names within external registries used by entries referencing external resources. When an entry
        /// referes to an entry in an external registry it should refer to the index within this list of the name
        /// of the entry in the external registry. </summary>
        public Option<List<string>> Names;
        /// <summary> The recipe that is being authored. </summary>
        public Option<CraftingRecipe> Recipe;

        /// <summary> Controls how recipes are created as UI slots. See <see cref="ISlot"/> for more information </summary>
        /// <param name="parent">The parent object containing the new slot</param>
        public void AttachDisplay(Transform parent) {
            UnityEngine.UI.Image image = parent.transform.GetChild(0).GetComponentInChildren<UnityEngine.UI.Image>();

            CraftingRecipe recipe = SerializeCopy();
            IItem resultItem = recipe.ResultItem; resultItem.Create((int)recipe.result.Index, 0);
            image.sprite = Config.CURRENT.Generation.Textures.Retrieve(resultItem.TexIndex).self;
            image.color = new Color(1, 1, 1, 1);
        }

        /// <summary> Releases UI slot objects associated with this recipe. See <see cref="ISlot"/> for more information </summary>
        /// /// <param name="parent">The parent object containing the slot to be removed</param>
        public void ClearDisplay(Transform parent) {
            UnityEngine.UI.Image image = parent.transform.GetChild(0)?.GetComponentInChildren<UnityEngine.UI.Image>();
            if (image == null) return;
            image.sprite = null;
            image.color = new Color(0, 0, 0, 0);
        }

        public CraftingRecipe SerializeCopy() {
            Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;
            CraftingRecipe newRecipe = (CraftingRecipe)Recipe.value.Clone();
            SerializeIngredientList(newRecipe.materials.value);
            SerializeIngredientList(newRecipe.items.value);

            void SerializeIngredientList(List<CraftingRecipe.Ingredient> ings) {
                for (int i = 0; i < ings.Count; i++) {
                    var ingredient = ings[i];
                    if (Names.value == null || ingredient.Index >= Names.value.Count)
                        continue;
                    if (!itemInfo.Contains(Names.value[ingredient.Index]))
                        continue;
                    ingredient.Index = itemInfo.RetrieveIndex(Names.value[ingredient.Index]);
                    ings[i] = ingredient;
                }
            }

            if (!itemInfo.Contains(Names.value[(int)Recipe.value.result.Index]))
                Debug.Log(name);
            newRecipe.result.Index = (uint)itemInfo.RetrieveIndex(
                Names.value[(int)Recipe.value.result.Index]);
            return newRecipe;
        }
    }

    /// <summary> A recipe describing a configuration of materials on the crafting grid
    /// that will create a specific item. Modifying recipes create an unfair advantage
    /// or upend the difficulty progression. </summary>
    [Serializable]
    public class CraftingRecipe : ICloneable {
        /// <summary> The materials that must be matched to successfully craft this recipe. Materials
        /// correspond to a grid of points whose size is dictated by <see cref="Crafting.GridWidth"/>.
        /// This grid must match the player's crafting grid for the recipe to be considered
        /// craftable and the result to be obtained. </summary> 
        public Option<List<Ingredient>> materials;
        /// <summary> The items that must be matched to successfully craft this recipe. Items
        /// correspond to the slots between the grid lines dictated by <see cref="Crafting.GridWidth"/>.
        /// These slots must match the player's crafting slots for the recipe to be considered
        /// craftable and the result to be obtained. </summary> 
        public Option<List<Ingredient>> items;
        /// <summary> If the recipe can be crafted, the result that is given to the player if the recipe is crafted.
        /// <see cref="Result"/> for more information. </summary>
        public Result result;
        /// <summary> Whether or not the recipe can be extended for quantities less than the minimum required amount</summary>
        public bool NoSubUnitCreation;


        /// <summary> The result item of the recipe. This is the actual item given to the player if the recipe is crafted.
        /// Obtained by retrieving the item indicated by <see cref="ResultIndex"/> from the item registry. </summary>
        [JsonIgnore]
        public IItem ResultItem {
            get {
                Catalogue<Authoring> reg = Config.CURRENT.Generation.Items;
                return reg.Retrieve((int)result.Index).Item;
            }
        }

        /// <summary>
        /// A structure representing the information
        /// of a single ingredient in a recipe.
        /// </summary>
        [Serializable] [StructLayout(LayoutKind.Sequential)]
        public struct Ingredient {
            /// <summary>  The index within the item registry of the item that needs to be placed here.
            /// This will be ignored if Amount is 0. </summary>
            [RegistryReference("Items")]
            public int Index;
            /// <summary> The amount of unit material that will be used to create a unit amount of 
            /// result. If <see cref="NoSubUnitCreation"/> is true, the amount that must be present
            /// to create the recipe. </summary>
            public float Amount;
            /// <exclude />
            [NonSerialized][HideInInspector]
            [JsonIgnore][UISetting(Ignore = true)]
            public uint flags;
        }


        /// <summary> Obtains the material index of the map entry at a specific grid index in the recipe. 
        /// If the entry is dirty, the material is ignored as it is not part of the real recipe
        /// and is not used when testing whether the recipe is craftable. </summary>
        /// <param name="index">The index within <see cref="entry"/> of the entry whose material is retrieved</param>
        /// <returns>The material of the entry at the specified <paramref name="index"/></returns>
        public double NormalInd(int index) {
            if (index < materials.value.Count) return GetNormalMatInd(index);
            return GetNormalItemInd(index - materials.value.Count);
        }

        private double GetNormalMatInd(int index) {
            Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;
            IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            Ingredient ing = materials.value[index];
            if (ing.Amount == 0) return 0;
            Authoring setting = itemInfo.Retrieve(ing.Index);
            if (setting is not PlaceableItem mSettings) return 0;
            if (!matInfo.Contains(mSettings.MaterialName)) return 0;
            return ((double)matInfo.RetrieveIndex(mSettings.MaterialName)) / matInfo.Count();
        }

        private double GetNormalItemInd(int index) {
            Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;
            IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            Ingredient ing = items.value[index];
            return ((double)ing.Index) / itemInfo.Count();
        }

        /// <summary> The result of a recipe. Indicates what is given to the player if the recipe is crafted. </summary>
        [Serializable]
        public struct Result {
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
            public uint Index {
                readonly get => (data >> 16) & 0xFFFF;
                set => data = (data & 0x0000FFFF) | (value << 16);
            }
            /// <summary> When an item is crafted, the amount of the item that is created is taken by adding all the 
            /// amounts of materials used in creating the recipe and multiplying it by the multiplier. </summary>
            [JsonIgnore]
            public float Multiplier {
                readonly get => (float)(data & 0xFFFF) / 0xFF;
                set => data = (data & 0xFFFF0000) | (((uint)math.round(value * 0xFF)) & 0xFFFF);
            }
        }

        public object Clone() {
            CraftingRecipe newRecipe = new CraftingRecipe();
            newRecipe.materials.value = new List<Ingredient>(materials.value);
            newRecipe.items.value = new List<Ingredient>(items.value);
            newRecipe.result = result;
            newRecipe.NoSubUnitCreation = NoSubUnitCreation;
            return newRecipe;
        }
    }

#if UNITY_EDITOR
    /// <summary> A utility class to override serialization of <see cref="Recipe.Result"/> into a Unity Inspector format.
    /// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
    [CustomPropertyDrawer(typeof(CraftingRecipe.Result))]
public class RecipeResultDrawer : PropertyDrawer{
    /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty dataProp = property.FindPropertyRelative("data");
        uint data = dataProp.uintValue;

        float multiplier = (data & 0xFFFF) / 255f;
        Rect rect = new (position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);
        
        RegistryReferenceDrawer.SetupRegistries();
        RegistryReferenceDrawer itemsDrawer = new RegistryReferenceDrawer {  BitMask = 0x7FFF, BitShift = 16 };
            itemsDrawer.DrawRegistryDropdown(rect, dataProp, new GUIContent("Items"), Config.TEMPLATE.Generation.Items);
        rect.y += EditorGUIUtility.singleLineHeight;
        
        multiplier = EditorGUI.FloatField(rect, "Multiplier", multiplier);
        rect.y += EditorGUIUtility.singleLineHeight;

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