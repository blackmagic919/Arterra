
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using static Arterra.GamePlay.UI.CraftingMenuController;
using UnityEngine.EventSystems;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Intrinsic;
using Arterra.Data.Item;
using Arterra.Engine.Terrain;
using System.Linq;
using Arterra.GamePlay.Interaction;

namespace Arterra.GamePlay.UI {
    public class CraftingRecipeSearch {
        private Transform Menu;
        private Animator animator;
        private HoverMoveButton ToggleSearch;
        private Transform SearchContainer;
        private TMP_InputField SearchInput;
        private RegistrySearchDisplay<CraftingAuthoring> RecipeSearch;

        private Transform RecipeDisplay;
        private CraftingGrid RecipeGrid;
        private IndirectUpdate HighlightTask;
        private IngredientTable IngredientList;
        private CraftingRecipe ActiveRecipe;
        private AnimatorAwaitTask ClosingTask;
        private int PrevHighlightedItemSlot;

        public CraftingRecipeSearch(Transform craftingMenu, ref int GridIndex) {
            var settings = Config.CURRENT.System.Crafting.value;
            Menu = craftingMenu.transform.Find("SearchArea");
            SearchInput = Menu.Find("SearchBar").GetComponentInChildren<TMP_InputField>();
            SearchContainer = Menu.Find("RecipeShelf").GetChild(0).GetChild(0);
            RecipeDisplay = Menu.Find("RecipeDisplay");
            animator = Menu.GetComponent<Animator>();
            HighlightTask = null;

            Transform searchButton = craftingMenu.transform.Find("ExpandSearch");
            ToggleSearch = searchButton.GetComponent<HoverMoveButton>();
            ToggleSearch.AddClickListener(Toggle);

            Registry<CraftingAuthoring> registry = Registry<CraftingAuthoring>.FromCatalogue(Config.CURRENT.System.Crafting.value.Recipes);
            GridUIManager RecipeContainer = new GridUIManager(SearchContainer.gameObject,
                Indicators.RecipeSelections.Get,
                Indicators.RecipeSelections.Release,
                settings.MaxRecipeSearchDisplay
            );

            RecipeSearch = new RegistrySearchDisplay<CraftingAuthoring>(
                registry, Menu, SearchInput, RecipeContainer
            );
            Button prevButton = Menu.Find("PreviousPage").GetComponent<Button>();
            Button nextButton = Menu.Find("NextPage").GetComponent<Button>();
            RecipeSearch.AddPaginateButtons(prevButton, nextButton);
            RecipeGrid = new(RecipeDisplay.Find("RecipeGrid").gameObject, instance.GridWidth, GridIndex);
            Image img = RecipeGrid.display.Display;
            img.color = new Color(img.color.r, 1, 0);

            GridIndex++;
            Deactivate();
            Button ReturnButton = RecipeDisplay.Find("Return").GetComponent<Button>();
            ReturnButton.onClick.AddListener(() => DeactivateRecipeDisplay());
            Button MatchButton = RecipeGrid.display.Object.GetComponent<Button>();
            MatchButton.onClick.AddListener(() => {
                instance.MatchRecipe(ActiveRecipe);
                //Unity by default makes enter press the last pressed button causing
                //unexpected double clicking match effects.
                EventSystem.current.SetSelectedGameObject(null);
            });

            SearchInput.onValueChanged.AddListener(DeactivateRecipeDisplay);
        }

        public void CopyToBuffer(CraftingRecipe.Ingredient[] SharedData) {
            RecipeGrid.CopyToBuffer(SharedData);
        }

        private void Toggle() {
            if (Menu.gameObject.activeSelf) {
                Deactivate();
            } else Activate();
        }

        internal void Activate() {
            ClosingTask?.Disable();
            RecipeSearch.Activate();
            animator.SetBool("IsOpen", true);
            ToggleSearch.Lock();
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Select",
                    CraftingMenuSelect, ActionBind.Exclusion.None),
                    "PlayerCraftSearch:SEL", "3.5::Window");
            });
        }

        internal void Deactivate() {
            ToggleSearch.Unlock();
            animator.SetBool("IsOpen", false);
            ClosingTask?.Disable();
            ClosingTask = new AnimatorAwaitTask(animator, "ClosedAnim", () => {
                //Does nothing if not bound :)
                InputPoller.AddKeyBindChange(() => InputPoller.RemoveBinding("PlayerCraftSearch:SEL", "3.5::Window"));
                DeactivateRecipeDisplay();
                RecipeSearch.Deactivate();
                Menu.gameObject.SetActive(false);
            }); ClosingTask.Invoke();
        }

        public void ActivateRecipeDisplay(CraftingAuthoring template) {
            if (HighlightTask != null)
                HighlightTask.Active = false;
            if (template == null) return;

            ActiveRecipe = template.SerializeCopy();
            List<(int, float)> ing = ActiveRecipe.items.value.Concat(ActiveRecipe.materials.value).Select(i => (i.Index, i.Amount)).ToList();
            this.IngredientList = new IngredientTable(RecipeDisplay.Find("IngredientTable"), ing);

            HighlightTask = new IndirectUpdate(HighlightGrid);
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(HighlightTask);
            RecipeGrid.RefreshGridWithRecipe(ActiveRecipe, instance.GridWidth);
            SearchContainer.gameObject.SetActive(false);
            RecipeDisplay.gameObject.SetActive(true);
            Transform title = RecipeDisplay.Find("RecipeMaterial");
            title.GetComponent<TextMeshProUGUI>().text = template.Name;

            HighlightGrid(null);
            PrevHighlightedItemSlot = -1;
            instance.Rendering.IsDirty = true;
        }

        public void DeactivateRecipeDisplay(string _ = null) {
            if (HighlightTask != null)
                HighlightTask.Active = false;
            HighlightTask = null;

            ActiveRecipe = null;
            PrevHighlightedItemSlot = -1;
            this.IngredientList?.ReleaseIngredientList();
            SearchContainer.gameObject.SetActive(true);
            RecipeDisplay.gameObject.SetActive(false);
        }

        private void CraftingMenuSelect(float _) {
            if (!SearchContainer.gameObject.activeSelf) return;
            if (!RecipeSearch.GridContainer.GetMouseSelected(out int index))
                return;
            ActivateRecipeDisplay(RecipeSearch.SlotEntries[index]);
        }

        private void HighlightGrid(MonoBehaviour _) {
            if (HighlightMouseMaterial())
                UnHighlightPrevItem();
            else if (!HighlightMouseItem()) {
                IngredientList.UnselectIngredient();
                UnHighlightPrevItem();
            }
            ;
        }


        private bool HighlightMouseItem() {
            InventoryController.Inventory inv = RecipeGrid.NonMatInventory;
            if (!inv.Display.GetMouseSelected(out int index))
                return false;
            if (inv.Info[index] == null) return false;
            GameObject slot = inv.Display.Slots[index];
            CraftingGrid.ResizeSlotRect(slot, Vector2.one);

            if (PrevHighlightedItemSlot == index) return true;
            UnHighlightPrevItem();
            IngredientList.SelectIngredient(ActiveRecipe.items.value[index].Index);
            PrevHighlightedItemSlot = index;
            return true;
        }

        private void UnHighlightPrevItem() {
            if (PrevHighlightedItemSlot == -1) return;
            InventoryController.Inventory inv = RecipeGrid.NonMatInventory;
            GameObject slot = inv.Display.Slots[PrevHighlightedItemSlot];
            CraftingGrid.ResizeSlotRect(slot, Vector2.one * 0.75f);
            PrevHighlightedItemSlot = -1;
        }

        private bool HighlightMouseMaterial() {
            bool GetMouseOverMaterial(out int slotIndex) {
                Vector3[] corners = new Vector3[4];
                RecipeGrid.display.Transform.GetWorldCorners(corners);
                int2 origin = new((int)corners[0].x, (int)corners[0].y);
                float2 worldSize = (Vector2)(corners[2] - corners[0]);
                float2 GridSize = worldSize / instance.GridWidth;

                slotIndex = -1;
                float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
                if (math.any(relativePos < 0) || math.any(relativePos >= instance.GridWidth)) {
                    return false;
                }
                int2 gridPos = (int2)math.round(relativePos);
                if (math.distancesq(gridPos, relativePos) > 0.5f * 0.5f) return false;
                int index = gridPos.x + gridPos.y * instance.AxisWidth;
                if (RecipeGrid.GridData[index].Amount <= 0) return false;
                slotIndex = index;
                return true;
            }

            int material = -1;
            if (GetMouseOverMaterial(out int slotIndex))
                material = RecipeGrid.GridData[slotIndex].Index;
            bool changed = false;
            for (int i = 0; i < instance.GridCount; i++) {
                bool IsTarget = RecipeGrid.GridData[i].Index == material;
                //Update the visuals if the mouse-over material changes
                changed |= ((RecipeGrid.GridData[i].flags & 0x1) != 0) ^ IsTarget;
                RecipeGrid.GridData[i].flags = IsTarget ? 1u : 0;
            }
            if (!changed) return material != -1;
            if (material != -1) IngredientList.SelectIngredient(ActiveRecipe.materials.value[slotIndex].Index);
            instance.Rendering.IsDirty = true;
            return material != -1;
        }
    }

    public class IngredientTable {
        private Dictionary<int, GameObject> ItemToIngredient;
        private Transform Table;
        private bool Highlighted;
        public IngredientTable(Transform Table, List<(int ind, float amt)> ingList) {
            this.Table = Table;
            CreatIngredientList(ingList);
            Highlighted = false;
        }
        private void CreatIngredientList(List<(int ind, float amt)> ingList) {
            ItemToIngredient = new Dictionary<int, GameObject>();
            GameObject IngredientSlot = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/Ingredient");
            foreach ((int ind, float amt) ing in ingList) {
                if (ing.amt <= 0) continue;
                if (ItemToIngredient.ContainsKey(ing.ind)) continue;
                GameObject NewSlot = GameObject.Instantiate(IngredientSlot, Table, false);
                InstantiateIngredient(ing, NewSlot);
                ItemToIngredient.Add(ing.ind, NewSlot);
            }
            SegmentedUIEditor.ForceLayoutRefresh(Table);
        }

        static void SetTextStyle(Transform slot, FontStyles fontStyle) {
            TextMeshProUGUI Text = slot.Find("Name").GetComponent<TextMeshProUGUI>();
            Text.fontStyle = fontStyle;
            Text = slot.Find("Description").GetComponent<TextMeshProUGUI>();
            Text.fontStyle = fontStyle;
        }

        public void SelectIngredient(int index) {
            UnselectIngredient();

            if (!ItemToIngredient.TryGetValue(index, out GameObject slot)) return;
            slot.transform.SetSiblingIndex(0);
            SetTextStyle(slot.transform, FontStyles.Bold);
            SegmentedUIEditor.ForceLayoutRefresh(slot.transform);
            Highlighted = true;
        }

        public void UnselectIngredient() {
            if (!Highlighted) return;
            Highlighted = false;
            Transform prevSel = Table.GetChild(0);
            if (prevSel != null) SetTextStyle(prevSel, FontStyles.Normal);
        }

        public void ReleaseIngredientList() {
            if (ItemToIngredient == null) return;
            foreach (GameObject slot in ItemToIngredient.Values)
                GameObject.Destroy(slot);
            ItemToIngredient = null;
        }

        private void InstantiateIngredient((int ind, float amt) ing, GameObject Slot) {
            var itemInfo = Config.CURRENT.Generation.Items;
            var texInfo = Config.CURRENT.Generation.Textures;
            Transform Icon = Slot.transform.Find("Icon");
            Authoring template = itemInfo.Retrieve(ing.ind);
            IItem item = template.Item;
            item.Create(ing.ind, Mathf.CeilToInt(ing.amt * item.UnitSize));
            Sprite tex = texInfo.Retrieve(item.TexIndex).self;
            Icon.GetComponent<Image>().sprite = tex;
            TextMeshProUGUI Text = Slot.transform.Find("Name").GetComponent<TextMeshProUGUI>();
            Text.text = template.Name;
            Text = Slot.transform.Find("Description").GetComponent<TextMeshProUGUI>();
            Text.text = template.Description;
        }
    }
}