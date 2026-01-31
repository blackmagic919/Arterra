using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Arterra.Data.Item;
using Arterra.Data.Material;
using Arterra.Configuration;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using Arterra.Editor;

namespace Arterra.Data.Intrinsic.Mortar {
    /// <summary>
    /// A structure defining how a material is converted in a mortar.
    /// This conversion can be used for input materials being smelted or output materials being produced.
    /// </summary>
    [Serializable]
    public struct MortarMaConversion {
        /// <summary>
        /// The name of the material.
        /// </summary>
        [RegistryReference("Items")]
        public int Index;

        /// <summary>The rate of amount per second at which the material is converted or being converted to. </summary>
        public float Rate;
    }

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Mortar/MortarFormula")]
    public class MortarFormula : Category<MortarFormula>, ISlot {
        public Option<List<string>> Names;

        public Option<List<MortarMaConversion>> Inputs;
        public Option<List<MortarMaConversion>> Outputs;
        public int Index(MortarMaConversion c) =>  Config.CURRENT.Generation.Items.RetrieveIndex(Names.value[c.Index]);
        public bool IsMatch(StackInventory items) {
            // Check input items are present
            var itemInfo = Config.CURRENT.Generation.Items;
            foreach (var input in Inputs.value) {
                if (!items.TryGetKey(Index(input), out _))
                    return false;
            }

            foreach (int itemIndex in items.EntryDict.Keys) {
                if (Inputs.value.Any(m => Index(m) == itemIndex)) continue;
                if (Outputs.value.Any(m => Index(m) == itemIndex)) continue;
                return false; //We have some unexpected item
            }

            if (items.count >= items.stackLimit) return false;
            return true;
        }

        public static MortarFormula GetMatchingFormula(StackInventory items) {
            var formulaInfo = Config.CURRENT.System.MortarFormulas;
            foreach (var formula in formulaInfo.Reg) {
                if (formula.IsMatch(items)) {
                    return formula;
                }
            }
            return null;
        }

        private ScrollingResultDisplay Display;
        /// <summary> Controls how recipes are created as UI slots. See <see cref="ISlot"/> for more information </summary>
        /// <param name="parent">The parent object containing the new slot</param>
        public void AttachDisplay(Transform parent) {
            Display?.Release();
            Display = new ScrollingResultDisplay(parent, Outputs.value.Select(s => Index(s)).ToList());
        }

        /// <summary> Releases UI slot objects associated with this recipe. See <see cref="ISlot"/> for more information </summary>
        /// /// <param name="parent">The parent object containing the slot to be removed</param>
        public void ClearDisplay(Transform parent) {
            Display?.Release();
            Display = null;
        }

        private class ScrollingResultDisplay {
            private Image image;
            private List<int> outputs;
            private int index;
            private bool active = false;
            private static Catalogue<TextureContainer> texInfo => Config.CURRENT.Generation.Textures;
            private static Catalogue<Authoring> itemInfo => Config.CURRENT.Generation.Items;
            public ScrollingResultDisplay(Transform parent, List<int> outputs) {
                this.outputs = outputs;
                this.image = parent.transform.GetChild(0).GetComponentInChildren<Image>();
                this.index = 0;

                this.active = true;
                int output = outputs[index];
                IItem resultItem = itemInfo.Retrieve(output).Item;
                resultItem.Create(output, 0);
                image.sprite = texInfo.Retrieve(resultItem.TexIndex).self;
                image.color = new Color(1, 1, 1, 1);
                if (outputs.Count <= 1) return;
                Arterra.Engine.Terrain.OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());
            }

            public void Release() {
                if (image == null) return;
                image.sprite = null;
                image.color = new Color(0, 0, 0, 0);
                active = false;
            }

            private IEnumerator UpdateRoutine() {
                while (true) {
                    yield return new WaitForSeconds(1.0f); // Update every second
                    if (image == null) yield break;
                    if (!active) yield break;

                    index = (index + 1) % outputs.Count;
                    int output = outputs[index];
                    IItem resultItem = itemInfo.Retrieve(output).Item;
                    resultItem.Create(output, 0);
                    image.sprite = texInfo.Retrieve(resultItem.TexIndex).self;
                }
            }
        }
    }

    public class MortarRecipeSearch {
        private Transform Menu;
        private Animator animator;
        private TMP_InputField SearchInput;
        private Transform SearchContainer;
        private HoverMoveButton ToggleSearch;
        private RegistrySearchDisplay<MortarFormula> RecipeSearch;
        private Transform RecipeDisplay;
        private MortarFormula ActiveRecipe = null;
        private AnimatorAwaitTask ClosingTask;
        private StackInventory MortarInv;
        private StackInventory[] InvDisplays;
        private IngredientTable IngredientList;
        public MortarRecipeSearch(MortarMaterial settings, Transform mortarMenu, StackInventory MortarInv, int numFormulas) {
            this.MortarInv = MortarInv;
            Menu = mortarMenu.transform.Find("SearchArea");
            SearchInput = Menu.Find("SearchBar").GetComponentInChildren<TMP_InputField>();
            SearchContainer = Menu.Find("RecipeShelf").GetChild(0).GetChild(0);
            RecipeDisplay = Menu.Find("RecipeDisplay");
            animator = Menu.GetComponent<Animator>();

            Transform searchButton = mortarMenu.transform.Find("ExpandSearch");
            ToggleSearch = searchButton.GetComponent<HoverMoveButton>();
            ToggleSearch.AddClickListener(Toggle);


            Registry<MortarFormula> registry = Registry<MortarFormula>.FromCatalogue(Config.CURRENT.System.MortarFormulas);
            GridUIManager RecipeContainer = new GridUIManager(SearchContainer.gameObject,
                Indicators.RecipeSelections.Get,
                Indicators.RecipeSelections.Release,
                numFormulas
            );
            
            RecipeSearch = new RegistrySearchDisplay<MortarFormula>(
                registry, Menu, SearchInput, RecipeContainer
            );
            Button prevButton = Menu.Find("PreviousPage").GetComponent<Button>();
            Button nextButton = Menu.Find("NextPage").GetComponent<Button>();
            RecipeSearch.AddPaginateButtons(prevButton, nextButton);
            Deactivate();

            Button ReturnButton = RecipeDisplay.Find("Return").GetComponent<Button>();
            ReturnButton.onClick.AddListener(() => DeactivateRecipeDisplay());
            SearchInput.onValueChanged.AddListener(DeactivateRecipeDisplay);

            var invInput = new StackInventory(settings.MaxSlotCount);
            var invOutput = new StackInventory(settings.MaxSlotCount, 0);
            InvDisplays = new StackInventory[] { invInput, invOutput };
            Transform displayMortar = RecipeDisplay.transform.Find("Mortar");
            GameObject center = displayMortar.Find("Input").gameObject;
            GameObject right = displayMortar.Find("Output").gameObject;
            InvDisplays[(int)FurnaceMaterial.InvIndex.Input].InitializeHorizontalDisplay(center, false);
            InvDisplays[(int)FurnaceMaterial.InvIndex.Output].InitializeVerticalDisplay(right, true);
            Button MatchButton = displayMortar.GetComponent<Button>();
            MatchButton.onClick.AddListener(() => {
                MatchRecipe(ActiveRecipe);
                EventSystem.current.SetSelectedGameObject(null);
            });
        }

        private void Toggle() {
            if (Menu.gameObject.activeSelf) {
                Deactivate();
            } else Activate();
        }

        public void Activate() {
            ClosingTask?.Disable();
            RecipeSearch.Activate();
            animator.SetBool("IsOpen", true);
            ToggleSearch.Lock();
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Select",
                    FormulaMenuSelect, ActionBind.Exclusion.None),
                    "MAT::MortarSearch:SEL", "3.5::Window");
            });
        }


        public void Deactivate() {
            ToggleSearch.Unlock();
            animator.SetBool("IsOpen", false);
            ClosingTask?.Disable();
            ClosingTask = new AnimatorAwaitTask(animator, "ClosedAnim", () => {
                //Does nothing if not bound :)
                InputPoller.AddKeyBindChange(() => InputPoller.RemoveBinding("MAT::MortarSearch:SEL", "3.5::Window"));
                DeactivateRecipeDisplay();
                RecipeSearch.Deactivate();
                Menu.gameObject.SetActive(false);
            }); ClosingTask.Invoke();
        }

        private void FormulaMenuSelect(float _) {
            if (!SearchContainer.gameObject.activeSelf) return;
            if (!RecipeSearch.GridContainer.GetMouseSelected(out int index))
                return;
            ActivateRecipeDisplay(RecipeSearch.SlotEntries[index]);
        }

        public void ActivateRecipeDisplay(MortarFormula template) {
            if (template == null) return;
            SearchContainer.gameObject.SetActive(false);
            RecipeDisplay.gameObject.SetActive(true);
            ActiveRecipe = template;

            float norm = DisplayNormalizationFactor(ActiveRecipe);
            RefreshInventory(InvDisplays[(int)FurnaceMaterial.InvIndex.Input], ActiveRecipe.Inputs, ActiveRecipe, norm);
            RefreshInventory(InvDisplays[(int)FurnaceMaterial.InvIndex.Output], ActiveRecipe.Outputs, ActiveRecipe, norm);
            List<(int, float)> ing = ActiveRecipe.Inputs.value.Concat(
                ActiveRecipe.Outputs.value).Select(i => (ActiveRecipe.Index(i), i.Rate * norm))
                .ToList();
            this.IngredientList = new IngredientTable(RecipeDisplay.Find("IngredientTable"), ing);
        }

        public void DeactivateRecipeDisplay(string _ = null) {
            SearchContainer.gameObject.SetActive(true);
            RecipeDisplay.gameObject.SetActive(false);
            this.IngredientList?.ReleaseIngredientList();
            ActiveRecipe = null;
        }

        private void RefreshInventory(StackInventory inv, List<MortarMaConversion> conv, MortarFormula formula, float norm) {
            var itemInfo = Config.CURRENT.Generation.Items;
            inv.Clear();

            foreach(MortarMaConversion c in conv) {
                IItem item = itemInfo.Retrieve(formula.Index(c)).Item;
                int createAmount = (int)math.max(math.round(c.Rate * item.UnitSize * norm), 1);
                item.Create(formula.Index(c), createAmount);
                inv.AddEntry(item, out _);
            } 
        }

        private float DisplayNormalizationFactor(MortarFormula formula) {
            float minOutput = float.MaxValue;
            foreach(MortarMaConversion c in formula.Outputs.value) {
                minOutput = math.min(minOutput, c.Rate);
            } return 1 / minOutput;
        }

        public void MatchRecipe(MortarFormula recipe) {
            if (recipe == null) return;
            Dictionary<int, int> Deficit = new Dictionary<int, int>();
            Dictionary<int, int> Surplus = new Dictionary<int, int>();
            Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;

            uint offset = 0;
            float normalization = DisplayNormalizationFactor(recipe);
            bool alreadyMatch = true; float maxBatchPossible = float.MaxValue;
            InventoryController.Inventory tempInv = new InventoryController.Inventory(2 * (int)MortarInv.capacity);
            MortarInv.CopyTo(tempInv, 0, 0); offset += MortarInv.capacity;
            
            AccumulateRequiredIngredients(MortarInv, recipe.Inputs.value);
            MortarInv.Clear();

            //Aquire items from inventory
            foreach (int item in Deficit.Keys) {
                if (!Surplus.TryGetValue(item, out int surplus))
                    surplus = 0;

                int required = Deficit[item];
                if (alreadyMatch) required = Mathf.FloorToInt(required * maxBatchPossible);
                if (surplus > required) continue;
                surplus += InventoryController.RemoveStackable(required - surplus, item,
                    OnRemoved: item => tempInv.AddStackable(item));
                Surplus[item] = surplus;
            }

            PlaceRequiredIngredients(MortarInv, recipe.Inputs.value);

            //Dispose of temporaryInventory
            for(int i = 0; i < tempInv.capacity; i++) {
                IItem item = tempInv.Info[i];
                if (item == null) continue;
                item = item.Clone() as IItem;
                InventoryController.AddEntry(item);
                tempInv.RemoveEntry(i);
            }

            void AccumulateInvSurplus(InventoryController.Inventory inv) {
                for (int i = 0; i < inv.Capacity; i++) {
                    IItem item = inv.PeekItem(i);

                    if (item != null) {
                        if (Surplus.ContainsKey(item.Index)) {
                            Surplus[item.Index] += item.AmountRaw;
                        } else Surplus[item.Index] = item.AmountRaw;
                    }
                }
            }

            void AccumulateRequiredIngredients(InventoryController.Inventory inv, List<MortarMaConversion> ingList) {
                AccumulateInvSurplus(inv);
                for (int i = 0; i < ingList.Count; i++) {
                    MortarMaConversion ing = ingList[i];
                    IItem item = inv.PeekItem(i);
                    int index = recipe.Index(ing);

                    if (ing.Rate <= 0)
                        alreadyMatch &= item == null;
                    else {
                        //Init so we can query valid UnitSize
                        if (item == null || item.Index != index) {
                            item = itemInfo.Retrieve(index).Item;
                            item.Create(index, 0);
                        }

                        int required = Mathf.CeilToInt(ing.Rate * normalization * item.UnitSize);
                        alreadyMatch &= item.AmountRaw >= required; //Not possible if Index != ing.Index
                        maxBatchPossible = math.min(item.StackLimit / required, maxBatchPossible);
                        if (Deficit.ContainsKey(item.Index)) Deficit[index] += required;
                        else Deficit[index] = required;
                    }
                }
            }

            void PlaceRequiredIngredients(InventoryController.Inventory dest, List<MortarMaConversion> ingList) {
                for (int i = 0; i < ingList.Count; i++) {
                    MortarMaConversion ing = ingList[i];
                    int index = recipe.Index(ing);

                    if (ing.Rate <= 0) continue;
                    IItem item = itemInfo.Retrieve(index).Item;
                    item.Create(index, 0);
                    float req = ing.Rate * normalization * item.UnitSize;
                    int amount = Mathf.CeilToInt(Surplus[index] * req / Deficit[index]);
                    tempInv.RemoveStackableKey(index, amount, item => {
                        if (dest.PeekItem(i) == null) dest.AddEntry(item, i);
                        else dest.Info[i].AmountRaw += item.AmountRaw;
                    });
                }   
            }
        }
    }
}