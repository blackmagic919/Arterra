using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Arterra.Config.Generation.Item;
using Arterra.Config.Generation.Material;
namespace Arterra.Config.Intrinsic.Furnace {
    /// <summary>
    /// A structure defining how a material is converted in a furnace.
    /// 
    /// This conversion can be used for input materials being smelted or output materials being produced.
    /// </summary>
    [Serializable]
    public struct FurnaceMaConversion {
        /// <summary>
        /// The name of the material.
        /// </summary>
        [RegistryReference("Items")]
        public int Index;

        /// <summary>
        /// The rate of amount per second at which the material is converted or being converted to.
        /// </summary>
        public float Rate;
    }

    [Serializable]
    [CreateAssetMenu(menuName = "Settings/Furnace/FurnaceFormula")]
    public class FurnaceFormula : Category<FurnaceFormula>, ISlot {
        public Option<List<string>> Names;

        public float StartTemperature;
        public float EndTemperature;
        public Option<List<FurnaceMaConversion>> Inputs;
        public Option<List<FurnaceMaConversion>> Outputs;
        public int Index(FurnaceMaConversion c) =>  Config.CURRENT.Generation.Items.RetrieveIndex(Names.value[c.Index]);
        public bool IsMatch(TagRegistry.Tags fuelTag, StackInventory fuelItems, StackInventory inputItems, StackInventory outputItems) {
            // Check input items matching
            if (Inputs.value.Count != inputItems.count) return false;
            var itemInfo = Config.CURRENT.Generation.Items;
            foreach (var input in Inputs.value) {
                if (!inputItems.TryGetKey(Index(input), out _))
                    return false;
            }

            // Check fuelTime temperature matching
            float totalTemp = 0; int count = 0;
            for(int i = 0; i < fuelItems.count; i++) {
                IItem item = fuelItems.PeekItem(i);
                if (item == null) continue;
                if (!itemInfo.GetMostSpecificTag(fuelTag, item.Index, out object prop))
                    continue;
                var combustible = prop as CombustibleTag;
                totalTemp += combustible.Temperature;
                count++;
            }
            float fuelTemperature = totalTemp / math.max(count, 1);
            if (fuelTemperature < StartTemperature) return false;
            if (fuelTemperature > EndTemperature) return false;


            // Check output items matching. The output either match with the formula or is empty
            if (outputItems.count > 0) {
                if (Outputs.value.Count != outputItems.count) return false;

                foreach (var output in Outputs.value) {
                    if (!outputItems.TryGetKey(Index(output), out _))
                    return false;
                }
            }

            return true;
        }

        public static FurnaceFormula GetMatchingFormula(TagRegistry.Tags fuelTag, StackInventory fuelItems, StackInventory inputItems, StackInventory outputItems) {
            var formulaInfo = Config.CURRENT.System.FurnaceFormulas;
            foreach (var formula in formulaInfo.Reg) {
                if (formula.IsMatch(fuelTag, fuelItems, inputItems, outputItems)) {
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
            Display = new ScrollingResultDisplay(parent, this);
        }

        /// <summary> Releases UI slot objects associated with this recipe. See <see cref="ISlot"/> for more information </summary>
        /// /// <param name="parent">The parent object containing the slot to be removed</param>
        public void ClearDisplay(Transform parent) {
            Display?.Release();
            Display = null;
        }

        private class ScrollingResultDisplay {
            private Image image;
            private FurnaceFormula formula;
            private int index;
            private bool active = false;
            private static Catalogue<TextureContainer> texInfo => Config.CURRENT.Generation.Textures;
            private static Catalogue<Authoring> itemInfo => Config.CURRENT.Generation.Items;
            public ScrollingResultDisplay(Transform parent, FurnaceFormula formula) {
                this.formula = formula;
                this.image = parent.transform.GetChild(0).GetComponentInChildren<Image>();
                this.index = 0;

                this.active = true;
                FurnaceMaConversion output = formula.Outputs.value[index];
                IItem resultItem = itemInfo.Retrieve(formula.Index(output)).Item;
                resultItem.Create(formula.Index(output), 0);
                image.sprite = texInfo.Retrieve(resultItem.TexIndex).self;
                image.color = new Color(1, 1, 1, 1);
                if (formula.Outputs.value.Count <= 1) return;
                Arterra.Core.Terrain.OctreeTerrain.MainCoroutines.Enqueue(UpdateRoutine());
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

                    index = (index + 1) % formula.Outputs.value.Count;
                    FurnaceMaConversion output = formula.Outputs.value[index];
                    IItem resultItem = itemInfo.Retrieve(formula.Index(output)).Item;
                    resultItem.Create(formula.Index(output), 0);
                    image.sprite = texInfo.Retrieve(resultItem.TexIndex).self;
                }
            }
        }
    }

    public class FurnaceRecipeSearch {
        private Transform Menu;
        private Animator animator;
        private TMP_InputField SearchInput;
        private Transform SearchContainer;
        private HoverMoveButton ToggleSearch;
        private RegistrySearchDisplay<FurnaceFormula> RecipeSearch;
        private Transform RecipeDisplay;
        private FurnaceFormula ActiveRecipe = null;
        private AnimatorAwaitTask ClosingTask;
        private StackInventory[] FurnaceInvs;
        private StackInventory[] InvDisplays;
        private TextMeshProUGUI FuelTemp;
        private IngredientTable IngredientList;
        public FurnaceRecipeSearch(Transform furnaceMenu, StackInventory[] FurnaceInvs, int numFormulas) {
            this.FurnaceInvs = FurnaceInvs;
            Menu = furnaceMenu.transform.Find("SearchArea");
            SearchInput = Menu.Find("SearchBar").GetComponentInChildren<TMP_InputField>();
            SearchContainer = Menu.Find("RecipeShelf").GetChild(0).GetChild(0);
            RecipeDisplay = Menu.Find("RecipeDisplay");
            animator = Menu.GetComponent<Animator>();

            Transform searchButton = furnaceMenu.transform.Find("ExpandSearch");
            ToggleSearch = searchButton.GetComponent<HoverMoveButton>();
            ToggleSearch.AddClickListener(Toggle);


            Registry<FurnaceFormula> registry = Registry<FurnaceFormula>.FromCatalogue(Config.CURRENT.System.FurnaceFormulas);
            GridUIManager RecipeContainer = new GridUIManager(SearchContainer.gameObject,
                Indicators.RecipeSelections.Get,
                Indicators.RecipeSelections.Release,
                numFormulas
            );
            
            RecipeSearch = new RegistrySearchDisplay<FurnaceFormula>(
                registry, Menu, SearchInput, RecipeContainer
            );
            Button prevButton = Menu.Find("PreviousPage").GetComponent<Button>();
            Button nextButton = Menu.Find("NextPage").GetComponent<Button>();
            RecipeSearch.AddPaginateButtons(prevButton, nextButton);
            Deactivate();

            Button ReturnButton = RecipeDisplay.Find("Return").GetComponent<Button>();
            ReturnButton.onClick.AddListener(() => DeactivateRecipeDisplay());
            SearchInput.onValueChanged.AddListener(DeactivateRecipeDisplay);

            var invInput = new StackInventory(6);
            var invOutput = new StackInventory(3, 0);
            InvDisplays = new StackInventory[] { invInput, invOutput };
            Transform displayFurnace = RecipeDisplay.transform.Find("Furnace");
            FuelTemp = displayFurnace.Find("Fuel").GetComponent<TextMeshProUGUI>();
            GameObject center = displayFurnace.Find("Input").gameObject;
            GameObject right = displayFurnace.Find("Output").gameObject;
            InvDisplays[(int)InvIndex.Input].InitializeHorizontalDisplay(center, false);
            InvDisplays[(int)InvIndex.Output].InitializeVerticalDisplay(right, true);
            Button MatchButton = displayFurnace.GetComponent<Button>();
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
                    "MAT::FurnaceSearch:SEL", "3.5::Window");
            });
        }


        public void Deactivate() {
            ToggleSearch.Unlock();
            animator.SetBool("IsOpen", false);
            ClosingTask?.Disable();
            ClosingTask = new AnimatorAwaitTask(animator, "ClosedAnim", () => {
                //Does nothing if not bound :)
                InputPoller.AddKeyBindChange(() => InputPoller.RemoveBinding("MAT::FurnaceSearch:SEL", "3.5::Window"));
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

        public void ActivateRecipeDisplay(FurnaceFormula template) {
            SearchContainer.gameObject.SetActive(false);
            RecipeDisplay.gameObject.SetActive(true);
            ActiveRecipe = template;

            float norm = DisplayNormalizationFactor(ActiveRecipe);
            RefreshInventory(InvDisplays[(int)InvIndex.Input], ActiveRecipe.Inputs, ActiveRecipe, norm);
            RefreshInventory(InvDisplays[(int)InvIndex.Output], ActiveRecipe.Outputs, ActiveRecipe, norm);
            FuelTemp.text = $"{ActiveRecipe.StartTemperature}° - {ActiveRecipe.EndTemperature}°";
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

        private void RefreshInventory(StackInventory inv, List<FurnaceMaConversion> conv, FurnaceFormula formula, float norm) {
            var itemInfo = Config.CURRENT.Generation.Items;
            inv.Clear();

            foreach(FurnaceMaConversion c in conv) {
                IItem item = itemInfo.Retrieve(formula.Index(c)).Item;
                int createAmount = (int)math.max(math.round(c.Rate * item.UnitSize * norm), 1);
                item.Create(formula.Index(c), createAmount);
                inv.AddEntry(item, out _);
            } 
        }

        private float DisplayNormalizationFactor(FurnaceFormula formula) {
            float minOutput = float.MaxValue;
            foreach(FurnaceMaConversion c in formula.Outputs.value) {
                minOutput = math.min(minOutput, c.Rate);
            } return 1 / minOutput;
        }

        public void MatchRecipe(FurnaceFormula recipe) {
            if (recipe == null) return;
            Dictionary<int, int> Deficit = new Dictionary<int, int>();
            Dictionary<int, int> Surplus = new Dictionary<int, int>();
            Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;

            uint offset = 0;
            float normalization = DisplayNormalizationFactor(recipe);
            bool alreadyMatch = true; float maxBatchPossible = float.MaxValue;
            uint tempCapacity = FurnaceInvs[0].capacity + FurnaceInvs[1].capacity + FurnaceInvs[2].capacity;
            InventoryController.Inventory tempInv = new InventoryController.Inventory(2 * (int)tempCapacity);
            FurnaceInvs[0].CopyTo(tempInv, 0, 0); offset += FurnaceInvs[0].capacity;
            FurnaceInvs[1].CopyTo(tempInv, 0, (int)offset); offset += FurnaceInvs[0].capacity;
            
            AccumulateRequiredIngredients(FurnaceInvs[(int)InvIndex.Input], recipe.Inputs.value);
            AccumulateInvSurplus(FurnaceInvs[(int)InvIndex.Output]);
            AccumulateInvSurplus(FurnaceInvs[(int)InvIndex.Fuel]);
            FurnaceInvs[0].Clear(); FurnaceInvs[1].Clear();

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

            PlaceRequiredIngredients(FurnaceInvs[(int)InvIndex.Input], recipe.Inputs.value);

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

            void AccumulateRequiredIngredients(InventoryController.Inventory inv, List<FurnaceMaConversion> ingList) {
                AccumulateInvSurplus(inv);
                for (int i = 0; i < ingList.Count; i++) {
                    FurnaceMaConversion ing = ingList[i];
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

            void PlaceRequiredIngredients(InventoryController.Inventory dest, List<FurnaceMaConversion> ingList) {
                for (int i = 0; i < ingList.Count; i++) {
                    FurnaceMaConversion ing = ingList[i];
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

    public enum InvIndex {
        Input = 0,
        Output = 1,
        Fuel = 2,
    }

}