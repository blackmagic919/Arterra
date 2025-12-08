using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Intrinsic;
using TerrainGeneration;


public sealed class CraftingMenuController : PanelNavbarManager.INavPanel {
    public static CraftingMenuController instance;
    private Crafting settings;
    public CraftingRecipeSearch RecipeSearch;
    private KTree Recipe;
    private GameObject craftingMenu;
    public Display Rendering;
    private IUpdateSubscriber eventTask;
    private ComputeBuffer craftingBuffer;
    private int TotalGrids;

    public int GridWidth;
    internal int AxisWidth => GridWidth + 1;
    internal int GridCount => AxisWidth * AxisWidth;
    private int2 GridSize => (int2)(float2)(Rendering.crafting.display.Transform.sizeDelta
        * Rendering.crafting.display.Transform.lossyScale) / GridWidth;
    private int FitRecipe = -1;

    // Start is called before the first frame update
    public CraftingMenuController(Crafting settings) {
        this.settings = settings;
        craftingMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingMenu"), GameUIManager.UIHandle.transform);
        GridWidth = settings.GridWidth; TotalGrids = 0;
        instance?.Release();
        instance = this;

        Rendering = new Display(craftingMenu, settings.NumMaxSelections, GridWidth, ref TotalGrids);
        RecipeSearch = new CraftingRecipeSearch(craftingMenu.transform, ref TotalGrids);
        craftingBuffer = new ComputeBuffer(TotalGrids * GridCount, sizeof(uint)*2 + sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        Rendering.InitializeSelectionPts(GridWidth, GridSize);

        Recipe.ConstructTree(settings.Recipes.Reg.Select(r => r.SerializeCopy()).ToArray(), dim: GridCount);
        craftingMenu.SetActive(false);

        Material mat = Rendering.crafting.display.Display.materialForRendering;
        mat.SetBuffer("CraftingInfo", craftingBuffer);
        mat.SetFloat("GridWidth", GridWidth);
    }

    public void Release() { craftingBuffer.Release(); }

    public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
    public GameObject GetDispContent() => craftingMenu;

    public void Activate() {
        eventTask = new IndirectUpdate(Update);
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddContextFence("PlayerCraft", "3.5::Window", ActionBind.Exclusion.None);
            InputPoller.AddBinding(new ActionBind("Craft", CraftEntry), "PlayerCraft:CFT", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Deselect",
                DeselectDrag, ActionBind.Exclusion.None), "PlayerCraft:DS", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Select",
                Select, ActionBind.Exclusion.None), "PlayerCraft:SEL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectPartial", SelectPartial),  "PlayerCraft:SELP", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectAll", SelectAll), "PlayerCraft:SELA", "3.0::AllWindow");
        });

        Clear();
        Refresh();
        UpdateDisplay();
        craftingMenu.SetActive(true);
        Rendering.InitializeDisplay(GridWidth);
    }

    public void Deactivate() {
        eventTask.Active = false;
        Clear(InventoryController.AddEntry);
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveContextFence("PlayerCraft", "3.5::Window");
            InputPoller.RemoveBinding("PlayerCraft:SELA", "3.0::AllWindow");
        });
        Rendering.ReleaseDisplay();
        craftingMenu.SetActive(false);
        RecipeSearch.Deactivate();
    }

    private bool GetMouseSelected(out IInventory inv, out int index) {
        if (InventoryController.Cursor.IsHolding) {
            //Check if item is material
            IItem cursor = InventoryController.Cursor.Item;
            Authoring setting = Config.CURRENT.Generation.Items.Retrieve(cursor.Index);
            if (setting is not PlaceableItem mSettings) return GetMouseItemSelect(out inv, out index);
            IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            if (!matInfo.Contains(mSettings.MaterialName)) return GetMouseItemSelect(out inv, out index);
            return GetMouseMatSelect(out inv, out index);
        } else {
            if (GetMouseItemSelect(out inv, out index)) {
                if (inv.PeekItem(index) != null) return true;
            } return GetMouseMatSelect(out inv, out index);
        }

        bool GetMouseMatSelect(out IInventory inv, out int index) {
            inv = Rendering.MainGridMatItems; index = -1;
            Vector3[] corners = new Vector3[4];
            Rendering.crafting.display.Transform.GetWorldCorners(corners);
            int2 origin = new int2((int)corners[0].x, (int)corners[0].y);
            float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
            if (math.any(relativePos < 0) || math.any(relativePos >= instance.GridWidth))
                return false;
            int2 gridPos = (int2)math.round(relativePos);
            index = gridPos.x + gridPos.y * AxisWidth;
            return true;
        }
        
        bool GetMouseItemSelect(out IInventory inv, out int index) {
            inv = Rendering.crafting.NonMatInventory;
            return Rendering.crafting.NonMatInventory.Display.GetMouseSelected(out index);
        }
    }
    private void Select(float _ = 0) {
        if (!InventoryController.Select(GetMouseSelected)) return;
        InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        Refresh();
    }

    private void DeselectDrag(float _ = 0) {
        if (!InventoryController.DeselectDrag(GetMouseSelected)) return;
        InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        Refresh();
    }
    private void SelectPartial(float _ = 0) {
        if (!InventoryController.SelectPartial(GetMouseSelected)) return;
        InputPoller.SuspendKeybindPropogation("SelectPartial", ActionBind.Exclusion.ExcludeLayer);
        Refresh();
    }
    private void SelectAll(float _ = 0) {
        InventoryController.SelectAll(Rendering.MainGridMatItems);
        InventoryController.SelectAll(Rendering.crafting.NonMatInventory);
        Refresh();
    }

    // Update is called once per frame
    private void Update(MonoBehaviour mono) {
        if (!eventTask.Active) return;

        //Crafting Point Scaling
        EvaluateInfluence((corner, influence) => {
            int index = corner.x + corner.y * AxisWidth;
            RectTransform craftingPoint = Rendering.corners[index];
            craftingPoint.transform.localScale = new Vector3(1, 1, 1) * math.lerp(1, settings.PointSizeMultiplier, influence);
        });

        UpdateDisplay();
    }

    internal void UpdateDisplay() {
        if (!Rendering.IsDirty) return;
        Rendering.IsDirty = false;
        CraftingRecipe.Ingredient[] ingredients = new CraftingRecipe.Ingredient[TotalGrids * GridCount];
        Rendering.CopyToBuffer(ingredients);
        RecipeSearch.CopyToBuffer(ingredients);
        craftingBuffer.SetData(ingredients);
    }

    private void EvaluateInfluence(Action<int2, float> callback) {
        Vector3[] corners = new Vector3[4];
        Rendering.crafting.display.Transform.GetWorldCorners(corners);
        int2 origin = new int2((int)corners[0].x, (int)corners[0].y);
        float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
        if (math.any(relativePos < 0) || math.any(relativePos >= instance.GridWidth))
            return;
        int2 gridPos = (int2)math.floor(relativePos);
        for (int i = 0; i < 4; i++) {
            int2 corner = gridPos + new int2(i / 2, i % 2);
            float2 L1Dist = 1 - math.abs(relativePos - corner);
            float influence = math.clamp(L1Dist.x * L1Dist.y, 0, 1);
            callback(corner, influence);
        }
    }

    private void CraftEntry(float _) {
        CraftRecipe();
        Refresh();
    }

    private void Refresh() {
        Rendering.crafting.RefreshInventoryGrid(Rendering.MainGridMatItems);
        CraftingRecipe target = Rendering.SerializeToRecipe();
        List<int> recipes = Recipe.QueryNearestLimit(target, settings.MaxRecipeDistance, settings.NumMaxSelections);
        FitRecipe = recipes.Count > 0 ? recipes[0] : -1;

        for (int r = 0; r < recipes.Count; r++) {
            Rendering.selections[r].RefreshGridWithRecipe(Recipe.Table[recipes[r]], GridWidth);
        }

        for (int i = 0; i < Rendering.selections.Length; i++) {
            CraftingGrid grid = Rendering.selections[i];
            if (i < recipes.Count) {
                if (!grid.display.Object.activeSelf)
                    grid.display.Object.SetActive(true);
            } else if (i >= recipes.Count && Rendering.selections[i].display.Object.activeSelf) {
                grid.ReleaseDisplay();
                grid.display.Object.SetActive(false);
            }
        }
        Rendering.IsDirty = true;
    }

    private void Clear(Action<IItem> ReleaseItem = null) {
        InventoryController.Inventory inv = Rendering.MainGridMatItems;
        for (int i = 0; i < inv.Info.Length; i++) {
            ReleaseItem?.Invoke(inv.Info[i]);
            inv.RemoveEntry(i);
        }
        Rendering.IsDirty = true;
        inv = Rendering.crafting.NonMatInventory;
        for (int i = 0; i < inv.Info.Length; i++) {
            ReleaseItem?.Invoke(inv.Info[i]);
            inv.RemoveEntry(i);
        }
        Rendering.IsDirty = true;

        for (int i = 0; i < Rendering.selections.Length; i++) {
            Rendering.selections[i].display.Object.SetActive(false);
        }
        FitRecipe = -1;
    }


    private void FlashIndicator() {
        CraftingRecipe recipe = Recipe.Table[FitRecipe];
        int2 offset = Rendering.GetNormalizationOffset();
        GetInsufficientEntries(Rendering.MainGridMatItems.Info, recipe.materials.value, AxisWidth,
        index => {
            Animator anim = Rendering.corners[index].GetComponent<Animator>();
            anim.SetTrigger("Flash");
        });
        GetInsufficientEntries(Rendering.crafting.NonMatInventory.Info, recipe.items.value, GridWidth,
        index => {
            GameObject slot = Rendering.crafting.NonMatInventory.Display.Slots[index];
            Animator anim = slot.transform.parent.GetComponent<Animator>();
            anim.SetTrigger("Flash");
        });
        

        void GetInsufficientEntries(IItem[] inv, List<CraftingRecipe.Ingredient> recipe, int side, Action<int> OnFindEntry) {
            for (int x = 0; x < side; x++) {
            for (int y = 0; y < side; y++) {
                int index = x + y * side;
                CraftingRecipe.Ingredient ingred = recipe[index];
                index = (x + offset.x) + (y + offset.y) * side;
                IItem placedItem = index >= inv.Length ? null : inv[index];
                if (placedItem == null) continue;
                if (ingred.Amount <= 0) continue;
                float unitAmt = ((float)placedItem.AmountRaw) / placedItem.UnitSize;
                if (unitAmt / ingred.Amount < 1) OnFindEntry.Invoke(index);
            }}
        }
    }

    private bool CraftRecipe() {
        if (FitRecipe == -1) return false;
        CraftingRecipe recipe = Recipe.Table[FitRecipe];
        int2 offset = Rendering.GetNormalizationOffset();
        if (!VerifyIngredientList(Rendering.MainGridMatItems.Info,
            recipe.materials.value, offset, AxisWidth, out float minMat))
            return false;
        if (!VerifyIngredientList(Rendering.crafting.NonMatInventory.Info,
            recipe.items.value, offset, GridWidth, out float minItem))
            return false;
        float minIngred = math.min(minMat, minItem);

        if (recipe.NoSubUnitCreation && minIngred < 1) {
            //Indicate to user which ingredients are insufficient
            FlashIndicator();
            return false;
        }

        //Clear grid of that amount
        ConsumeIngredientList(Rendering.MainGridMatItems, recipe.materials.value,
            offset, AxisWidth, minIngred);
        ConsumeIngredientList(Rendering.crafting.NonMatInventory, recipe.items.value,
            offset, GridWidth, minIngred);

        IItem result = recipe.ResultItem;
        minIngred *= recipe.result.Multiplier * result.UnitSize;

        int totalAmount = Mathf.FloorToInt(minIngred);
        int itemCount = Mathf.CeilToInt((float)totalAmount / result.StackLimit);
        int amount = math.min(totalAmount, result.StackLimit);
        for (int i = 0; i < itemCount; i++) {
            IItem item = (IItem)result.Clone();
            item.Create((int)recipe.result.Index, amount);
            InventoryController.AddEntry(item);

            totalAmount -= amount;
            amount = math.min(totalAmount, result.StackLimit);
        }
        return true;

        static bool VerifyIngredientList(IItem[] inv, List<CraftingRecipe.Ingredient> recipe, int2 offset, int side, out float minIngred) {
            minIngred = float.MaxValue;
            for (int x = 0; x < side; x++) {
            for (int y = 0; y < side; y++) {
                int index = x + y * side;
                CraftingRecipe.Ingredient ingred = recipe[index];
                index = (x + offset.x) + (y + offset.y) * side;
                IItem placedItem = index >= inv.Length ? null : inv[index];
                if (placedItem == null || placedItem.AmountRaw == 0) {
                    if (ingred.Amount <= 0) continue;
                    else return false;
                } else if (ingred.Amount <= 0) return false;
                else if (placedItem.Index != ingred.Index) return false;
                float unitAmt = ((float)placedItem.AmountRaw) / placedItem.UnitSize;
                minIngred = math.min(minIngred, unitAmt / ingred.Amount);
            }}
            return true;
        }

        static void ConsumeIngredientList(InventoryController.Inventory inv, List<CraftingRecipe.Ingredient> recipe, int2 offset, int side, float minIngred) {
            for (int x = 0; x < side; x++) {
            for (int y = 0; y < side; y++) {
                int index = x + y * side;
                CraftingRecipe.Ingredient ingred = recipe[index];
                index = (x + offset.x) + (y + offset.y) * side;
                IItem placedItem = index >= inv.capacity ? null : inv.Info[index];
                if (ingred.Amount <= 0) continue;
                if (placedItem == null || placedItem.AmountRaw == 0) continue;
                int delta = Mathf.CeilToInt(placedItem.UnitSize * minIngred);
                inv.RemoveStackableSlot(index, delta);
            }}
        }
    }
    
    public void MatchRecipe(CraftingRecipe recipe) {
        if (recipe == null) return;
        Dictionary<int, int> Deficit = new Dictionary<int, int>();
        Dictionary<int, int> Surplus = new Dictionary<int, int>();
        Catalogue<Authoring> itemInfo = Config.CURRENT.Generation.Items;

        bool alreadyMatch = true; float maxBatchPossible = float.MaxValue;
        //Twice combined capacity so we don't overflow
        uint tempCapacity = Rendering.MainGridMatItems.capacity + Rendering.crafting.NonMatInventory.capacity;
        InventoryController.Inventory tempInv = new InventoryController.Inventory(2 * (int)tempCapacity);
        int itemOffset = (int)Rendering.MainGridMatItems.capacity;
        Rendering.MainGridMatItems.CopyTo(tempInv, 0, 0);
        Rendering.crafting.NonMatInventory.CopyTo(tempInv, 0, itemOffset);
        Rendering.MainGridMatItems.Clear(); Rendering.crafting.NonMatInventory.Clear();
        AccumulateRequiredIngredients(recipe.materials.value, 0);
        AccumulateRequiredIngredients(recipe.items.value, itemOffset);

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

        PlaceRequiredIngredients(Rendering.MainGridMatItems, recipe.materials.value);
        PlaceRequiredIngredients(Rendering.crafting.NonMatInventory, recipe.items.value);
        Refresh(); UpdateDisplay();
        
        //Dispose of temporaryInventory
        for(int i = 0; i < tempInv.capacity; i++) {
            IItem item = tempInv.Info[i];
            if (item == null) continue;
            item = item.Clone() as IItem;
            InventoryController.AddEntry(item);
            tempInv.RemoveEntry(i);
        }

        void AccumulateRequiredIngredients(List<CraftingRecipe.Ingredient> ingList, int offset) {
            for (int i = 0; i < ingList.Count; i++) {
                CraftingRecipe.Ingredient ing = ingList[i];
                IItem item = tempInv.Info[offset + i];

                //Add Surplus
                if (item != null) {
                    if (Surplus.ContainsKey(item.Index)) {
                        Surplus[item.Index] += item.AmountRaw;
                    } else Surplus[item.Index] = item.AmountRaw;
                }

                if (ing.Amount <= 0)
                    alreadyMatch &= item == null;
                else {
                    //Init so we can query valid UnitSize
                    if (item == null || item.Index != ing.Index) {
                        item = itemInfo.Retrieve(ing.Index).Item;
                        item.Create(ing.Index, 0);
                    }

                    int required = Mathf.CeilToInt(ing.Amount * item.UnitSize);
                    alreadyMatch &= item.AmountRaw >= required; //Not possible if Index != ing.Index
                    maxBatchPossible = math.min(item.StackLimit / required, maxBatchPossible);
                    if (Deficit.ContainsKey(item.Index)) Deficit[ing.Index] += required;
                    else Deficit[ing.Index] = required;
                }
            }
        }

        void PlaceRequiredIngredients(InventoryController.Inventory dest, List<CraftingRecipe.Ingredient> ingList) {
            for (int i = 0; i < ingList.Count; i++) {
                CraftingRecipe.Ingredient ing = ingList[i];
                if (ing.Amount <= 0) continue;
                IItem item = itemInfo.Retrieve(ing.Index).Item;
                item.Create(ing.Index, 0);
                float req = ing.Amount * item.UnitSize;
                int amount = Mathf.CeilToInt(Surplus[ing.Index] * req / Deficit[ing.Index]);
                tempInv.RemoveStackableKey(ing.Index, amount, item => {
                    if (dest.Info[i] == null) dest.AddEntry(item, i);
                    else dest.Info[i].AmountRaw += item.AmountRaw;
                });
            }   
        }
    }
    

    public struct Display {
        public CraftingGrid crafting;
        public CraftingGrid[] selections;
        public RectTransform[] corners;
        public InventoryController.Inventory MainGridMatItems;
        public bool IsDirty;
        public Display(GameObject menu, int NumMaxSelections, int GridWidth, ref int GridIndex) {
            int GridCount = (GridWidth + 1) * (GridWidth + 1);
            MainGridMatItems = new InventoryController.Inventory(GridCount);
            MainGridMatItems.AddCallbacks(CraftingGrid.GetCraftingCxt, CraftingGrid.GetCraftingCxt);
            crafting = new CraftingGrid(menu.transform.Find("CraftingGrid").gameObject, GridWidth, GridIndex);
            GridIndex++;

            GameObject SelectionArea = menu.transform.Find("CraftingHints").GetChild(0).GetChild(0).gameObject;
            GameObject SelectionGrid = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingSelection");
            selections = new CraftingGrid[NumMaxSelections];
            for (int i = 0; i < NumMaxSelections; i++) {
                selections[i] = new(
                    GameObject.Instantiate(SelectionGrid, SelectionArea.transform),
                    GridWidth, GridIndex + i
                );
            }
            GridIndex += NumMaxSelections;

            corners = new RectTransform[GridCount];
            IsDirty = false;
        }

        public int2 GetNormalizationOffset() {
            int2 minOffset = instance.AxisWidth;
            for(int x = 0; x < instance.AxisWidth; x++) {
            for(int y = 0; y < instance.AxisWidth; y++) {
                int index = x + y * instance.AxisWidth;
                if(MainGridMatItems.Info[index] == null) continue;
                minOffset = math.min(minOffset, new (x, y));
            }}
            for(int x = 0; x < instance.GridWidth; x++) {
            for(int y = 0; y < instance.GridWidth; y++) {
                int index = x + y * instance.GridWidth;
                if(crafting.NonMatInventory.Info[index] == null) continue;
                minOffset = math.min(minOffset, new (x, y));
            }}
            return minOffset;
        }
        
        public CraftingRecipe SerializeToRecipe() {
            static CraftingRecipe.Ingredient[] SerializeIngArray(InventoryController.Inventory inv, int2 offset, int side) {
                CraftingRecipe.Ingredient[] ingArray = new CraftingRecipe.Ingredient[inv.capacity];
                for (int x = offset.x; x < side; x++) {
                for (int y = offset.y; y < side; y++) {
                    int index = x + y * side;
                    IItem item = inv.Info[index];
                    if (item == null) continue;
                    index = (x - offset.x) + (y - offset.y) * side;
                    ingArray[index].Amount = CraftingGrid.SmoothSplitLerp(
                        item.AmountRaw, item.UnitSize, item.StackLimit);
                    ingArray[index].Index = item.Index;
                }}
                return ingArray;
            }
            int2 offset = GetNormalizationOffset();
            CraftingRecipe.Ingredient[] matGrid = SerializeIngArray(MainGridMatItems, offset, instance.AxisWidth);
            CraftingRecipe.Ingredient[] itemGrid = SerializeIngArray(crafting.NonMatInventory, offset, instance.GridWidth);
            return new CraftingRecipe {
                materials = new Option<List<CraftingRecipe.Ingredient>> { value = matGrid.ToList() },
                items = new Option<List<CraftingRecipe.Ingredient>> { value = itemGrid.ToList() }
            };
        }

        public void InitializeSelectionPts(int GridWidth, int2 GridSize) {
            int AxisWidth = GridWidth + 1;
            GameObject craftingPoint = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingPoint");
            for (int i = 0; i < corners.Length; i++) {
                corners[i] = GameObject.Instantiate(craftingPoint, crafting.display.Transform).GetComponent<RectTransform>();
                corners[i].transform.Translate(new Vector3((i % AxisWidth) * GridSize.x, (i / AxisWidth) * GridSize.y, 0));
            }
        }

        public void CopyToBuffer(CraftingRecipe.Ingredient[] SharedData) {
            crafting.CopyToBuffer(SharedData);
            foreach (CraftingGrid g in selections) g.CopyToBuffer(SharedData);
        }

        public void InitializeDisplay(int GridWidth) {
            crafting.InitializeDisplay(GridWidth);
            foreach (CraftingGrid selection in selections) selection.InitializeDisplay(GridWidth);
        }

        public void ReleaseDisplay() {
            crafting.ReleaseDisplay();
            foreach (CraftingGrid selection in selections) selection.ReleaseDisplay();
        }
    }

    public class CraftingGrid {
        private int gridIndex;
        public Grid display;
        public CraftingRecipe.Ingredient[] GridData;
        public InventoryController.Inventory NonMatInventory;
        internal static ItemContext GetCraftingCxt(ItemContext cxt) => cxt.SetupScenario(PlayerHandler.data, ItemContext.Scenario.ActivePlayerCraftingGrid);
        public CraftingGrid(GameObject parent, int GridWidth, int index) {
            int AxisWidth = GridWidth + 1;
            NonMatInventory = new InventoryController.Inventory(GridWidth * GridWidth);
            NonMatInventory.AddCallbacks(GetCraftingCxt, GetCraftingCxt);
            GridData = new CraftingRecipe.Ingredient[AxisWidth * AxisWidth];
            display = new Grid(parent, index);
            gridIndex = index;
        }
        internal static float SmoothSplitLerp(float x, float A, float B) {
            if (x <= A) return 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x / A));
            else return 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - A) / (B - A)));
        }

        public void RefreshInventoryGrid(InventoryController.Inventory MatInv) {
            if (MatInv == null) return;
            for (int i = 0; i < MatInv.capacity; i++) {
                IItem item = MatInv.Info[i];
                GridData[i] = new CraftingRecipe.Ingredient { Index = 0, Amount = 0 };

                if (item == null) continue;
                Authoring setting = Config.CURRENT.Generation.Items.Retrieve(item.Index);
                if (setting is not PlaceableItem mSettings) continue;
                IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                if (!matInfo.Contains(mSettings.MaterialName)) continue;
                GridData[i].Amount = SmoothSplitLerp(item.AmountRaw, item.UnitSize, item.StackLimit);
                GridData[i].Index = matInfo.RetrieveIndex(mSettings.MaterialName);
            }
        }

        public void RefreshGridWithRecipe(CraftingRecipe recipe, int GridWidth) {
            if (recipe == null || recipe.materials.value == null) return;
            List<CraftingRecipe.Ingredient> ingredients = recipe.materials.value;
            for (int i = 0; i < ingredients.Count(); i++) {
                CraftingRecipe.Ingredient ing = ingredients[i];
                GridData[i] = new CraftingRecipe.Ingredient { Index = 0, Amount = 0 };

                Authoring setting = Config.CURRENT.Generation.Items.Retrieve(ing.Index);
                IItem item = setting.Item;
                if (setting is not PlaceableItem mSettings) continue;
                IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                if (!matInfo.Contains(mSettings.MaterialName)) continue;
                GridData[i].Amount = SmoothSplitLerp(ing.Amount * item.UnitSize, item.UnitSize, item.StackLimit);
                GridData[i].Index = matInfo.RetrieveIndex(mSettings.MaterialName);
            }

            ingredients = recipe.items.value;
            ReleaseDisplay();
            NonMatInventory = new InventoryController.Inventory(ingredients.Count());
            NonMatInventory.AddCallbacks(GetCraftingCxt, GetCraftingCxt);
            for (int i = 0; i < ingredients.Count(); i++) {
                CraftingRecipe.Ingredient ing = ingredients[i];
                Authoring setting = Config.CURRENT.Generation.Items.Retrieve(ing.Index);
                IItem item = setting.Item;
                int AmountRaw = Mathf.FloorToInt(ing.Amount * item.UnitSize);
                if (AmountRaw <= 0) continue;
                item.Create(ing.Index, AmountRaw);
                NonMatInventory.AddEntry(item, i);
            }

            InitializeDisplay(GridWidth);
        }

        public void CopyToBuffer(CraftingRecipe.Ingredient[] SharedData) {
            GridData.CopyTo(SharedData, gridIndex * GridData.Length);
        }

        public void InitializeDisplay(int GridWidth) {
            GameObject Root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Inventory"), display.Object.transform);
            GameObject GridContent = Root.transform.GetChild(0).GetChild(0).gameObject;
            GridUIManager Display = new GridUIManager(GridContent,
                Indicators.TransparentSlots.Get,
                frame => {
                    ResizeSlotRect(frame, Vector2.one);
                    Indicators.TransparentSlots.Release(frame.transform.parent.gameObject);
                },
                (int)NonMatInventory.capacity,
                Root,
                Obj => {
                    GameObject frame = Obj.transform.GetChild(0).gameObject;
                    ResizeSlotRect(frame, Vector2.one * 0.75f);
                    return frame;
                }
            );
            
            NonMatInventory.InitializeDisplay(Display);
            NonMatInventory.Display.Grid.cellSize = display.Transform.sizeDelta / GridWidth;
            NonMatInventory.Display.Grid.spacing = float2.zero;
            NonMatInventory.Display.ChangeAlginment(GridLayoutGroup.Corner.LowerLeft);
        }

        public void ReleaseDisplay() => NonMatInventory?.ReleaseDisplay();

        public static void ResizeSlotRect(GameObject slot, Vector2 size) {
            RectTransform tnsf = slot.GetComponent<RectTransform>();
            tnsf.anchorMin = Vector2.one * 0.5f - size/2.0f;
            tnsf.anchorMax = Vector2.one * 0.5f + size/2.0f;
            tnsf.offsetMin = Vector2.zero;
            tnsf.offsetMax = Vector2.zero;
        }

        public struct Grid {
            public GameObject Object;
            public RectTransform Transform;
            public Image Display;
            public Grid(GameObject obj, int gridIndex) {
                Object = obj;
                Transform = obj.GetComponent<RectTransform>();
                Display = obj.GetComponent<Image>();
                Display.color = new Color(gridIndex / 255.0f, 0, 0);
            }
        }
    }

    public struct KTree {
        public CraftingRecipe[] Table;
        public List<Node> tree;
        private int head;
        public struct Node {
            public int Split;
            public int Left;
            public int Right;
            public byte Axis;
        }

        public void ConstructTree(CraftingRecipe[] recipes, int dim = 16) {
            Table = recipes;
            tree = new List<Node>();
            (int, CraftingRecipe)[] layer = new (int, CraftingRecipe)[recipes.Length];
            for (int i = 0; i < recipes.Length; i++) {
                //Ensure recipes are valid for given config; Recipe is Value Type so this is not a reference
                while (recipes[i].materials.value.Count < dim) {
                    recipes[i].materials.value.Add(new CraftingRecipe.Ingredient { Index = -1, Amount = 0 });
                }
                if (recipes[i].materials.value.Count > dim) {
                    recipes[i].materials.value = recipes[i].materials.value.Take(dim).ToList();
                }
                layer[i] = (i, recipes[i]);
            }
            head = BuildSubTree(layer, (uint)dim);
        }

        static double GetL1Dist(CraftingRecipe a, CraftingRecipe b) {
            double sum = 0;
            for (int i = 0; i < a.materials.value.Count; i++) {
                sum += math.abs(a.NormalInd(i) - b.NormalInd(i));
            }
            return sum;
        }

        public List<int> QueryNearestLimit(CraftingRecipe tg, float dist, int count) {
            List<int> ret = new List<int>();
            QueryNearest(tg, dist, (int recipe) => {
                ret.Add(recipe);
            });
            var table = Table;
            ret.Sort((a, b) => (int)math.sign(GetL1Dist(table[a], tg) - GetL1Dist(table[b], tg)));
            return ret.Take(count).ToList();
        }

        private void QueryNearest(CraftingRecipe tg, float dist, Action<int> cb) {
            Queue<int> layer = new Queue<int>();
            layer.Enqueue(head);

            while (layer.Count > 0) {
                int node = layer.Dequeue();
                if (node == -1) continue;

                Node n = tree[node];
                CraftingRecipe recipe = Table[n.Split];
                double targetDist = tg.NormalInd(n.Axis) - recipe.NormalInd(n.Axis);
                if (math.abs(targetDist) <= dist) {
                    if (GetL1Dist(recipe, tg) <= dist)
                        cb(n.Split);
                    layer.Enqueue(n.Left);
                    layer.Enqueue(n.Right);
                } else {
                    int next = targetDist >= 0 ? n.Right : n.Left;
                    layer.Enqueue(next);
                }
            }
        }
        private int BuildSubTree((int, CraftingRecipe)[] layer, uint dim) {
            int GetMaximumDimensions() {
                HashSet<(int, double)> dimLens = new HashSet<(int, double)>();
                foreach ((int, CraftingRecipe) rep in layer) {
                    CraftingRecipe recipe = rep.Item2;
                    for (int i = 0; i < dim; i++) {
                        dimLens.Add((i, recipe.NormalInd(i)));
                    }
                }

                int[] dimCounts = new int[dim];
                foreach ((int dimension, double material) in dimLens) { dimCounts[dimension]++; }
                return dimCounts.Max();
            }

            if (layer.Length <= 0) return -1;
            int maxDim = GetMaximumDimensions();
            Array.Sort(layer, (a, b) => (int)math.sign(a.Item2.NormalInd(maxDim) - b.Item2.NormalInd(maxDim)));

            int mid = layer.Length / 2;
            tree.Add(new Node {
                Split = layer[mid].Item1,
                Axis = (byte)maxDim,
                Left = BuildSubTree(layer.Take(mid).ToArray(), dim),
                Right = BuildSubTree(layer.Skip(mid + 1).ToArray(), dim),
            });
            return tree.Count - 1;
        }
    }

}
