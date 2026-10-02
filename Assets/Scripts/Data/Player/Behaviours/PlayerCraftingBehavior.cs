using System;
using Newtonsoft.Json;
using Arterra.GamePlay;
using Arterra.Data.Entity.Behavior;
using Arterra.Data.Player;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Data.Intrinsic;
using Arterra.Engine.Terrain;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using Arterra.Core.Events;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Data.Entity.Behavior {
    public sealed class PlayerCraftingBehavior : SpeciesBehavior, PanelNavbarManager.INavPanel {
        public Inventory Materials;
        public Inventory Items;
        private KTree Recipe;

        public override void AddBehaviorDependencies(System.Collections.Generic.Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.PlayerInventories, hierarchy.Count);
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            this.self = self;
            self.Register(this);
            self.Is(out inventories);

            settings = Config.CURRENT.System.Crafting;
            Materials ??= new Inventory(AxisWidth * AxisWidth);
            Items ??= new Inventory(GridWidth * GridWidth);
            Recipe.ConstructTree(settings.Recipes.Reg.Select(r => r.SerializeCopy()).ToArray(),
                AxisWidth * AxisWidth, GridWidth * GridWidth);

            Materials.AddCallbacks(SetupContext, SetupContext);
            Items.AddCallbacks(SetupContext, SetupContext);
            Materials.ReapplyHandles();
            Items.ReapplyHandles();
            if (!self.IsAuthority) return;
            self.eventCtrl.AddEventHandler(GameEvent.Apply_Craft, HandleCraft);
            self.eventCtrl.AddEventHandler(GameEvent.Apply_MatchRecipe, HandleMatchRecipe);
            self.eventCtrl.AddEventHandler(GameEvent.Apply_ClearCrafting, HandleClear);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) =>
            Initialize(self, setting, GCoord);

        public override void Disable(BehaviorEntity.Animal self) {
            DisposeVisual(self);
            Materials?.UnapplyHandles();
            Items?.UnapplyHandles();
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_Craft, HandleCraft);
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_MatchRecipe, HandleMatchRecipe);
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_ClearCrafting, HandleClear);
        }

        private ItemContext SetupContext(ItemContext context) =>
            context.SetupScenario(self, ItemContext.Scenario.ActivePlayerCraftingGrid);

        private void HandleCraft(object source, object target, object context) {
            CraftRecipe(inventories, self, out int insufficientRecipe);
            if (insufficientRecipe >= 0) self.eventCtrl.RaiseEvent(GameEvent.Entity_CraftInsufficient,
                self.info.rtEntityId, null, insufficientRecipe);
        }

        private void HandleMatchRecipe(object source, object target, object context) {
            if (context is int recipe) MatchRecipeInternal(recipe, inventories);
        }

        private void HandleClear(object source, object target, object context) => Clear(inventories.AddItem);

        private BehaviorEntity.Animal self;
        private PlayerInventoriesBehavior inventories;
        private Crafting settings;
        [JsonIgnore] public CraftingRecipeSearch RecipeSearch;

        private GameObject craftingMenu;
        [JsonIgnore] public Display Rendering;

        private ComputeBuffer craftingBuffer;
        private int TotalGrids;

        [JsonIgnore] public int GridWidth => settings.GridWidth;
        internal int AxisWidth => GridWidth + 1;
        internal int GridCount => AxisWidth * AxisWidth;
        private int2 GridSize => (int2)(float2)(Rendering.crafting.display.Transform.sizeDelta
            * Rendering.crafting.display.Transform.lossyScale) / GridWidth;
        private int FitRecipe = -1;

        // Start is called before the first frame update
        public override void InitializeVisual(BehaviorEntity.Animal self) {
            if (!self.IsOwner || craftingMenu != null) return;
            craftingMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingMenu"), GameUIManager.UIHandle.transform);
            TotalGrids = 0;

            Rendering = new Display(craftingMenu, settings.NumMaxSelections, GridWidth, ref TotalGrids);
            RecipeSearch = new CraftingRecipeSearch(this, craftingMenu.transform, ref TotalGrids);
            craftingBuffer = new ComputeBuffer(TotalGrids * GridCount, sizeof(uint) * 2 + sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
            Rendering.InitializeSelectionPts(GridWidth, GridSize);

            BindInventories();
            craftingMenu.SetActive(false);

            UnityEngine.Material mat = Rendering.crafting.display.Display.materialForRendering;
            mat.SetBuffer("CraftingInfo", craftingBuffer);
            mat.SetFloat("GridWidth", GridWidth);
            PanelNavbarManager.Add(this, "Crafting");
        }

        public void Release() => DisposeVisual(self);

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (craftingMenu == null) return;
            if (craftingMenu.activeSelf) Deactivate();
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_CraftInsufficient, FlashIndicator);
            PanelNavbarManager.Remove("Crafting", release: false);
            RecipeSearch?.Release();
            craftingBuffer?.Release();
            craftingBuffer = null;
            GameObject.Destroy(craftingMenu);
            craftingMenu = null;
        }

        public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
        public GameObject GetDispContent() => craftingMenu;

        public void Activate() {
            self.eventCtrl.AddEventHandler(GameEvent.Entity_CraftInsufficient, FlashIndicator);
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Action_OpenCrafting, self.info.rtEntityId, null);
            InputPoller.AddContextFence("PlayerCraft", "3.5::Window", ActionBind.Exclusion.None);
            InputPoller.AddBinding(new ActionBind("Craft", CraftEntry, policy: ActionBind.Policy.Down),
                "PlayerCraft:CFT", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Deselect",
                DeselectDrag, ActionBind.Exclusion.None), "PlayerCraft:DS", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Select",
                Select, ActionBind.Exclusion.None, ActionBind.Policy.Down),
                "PlayerCraft:SEL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectPartial", SelectPartial, policy: ActionBind.Policy.Down),  "PlayerCraft:SELP", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectAll", SelectAll, policy: ActionBind.Policy.Down), "PlayerCraft:SELA", "3.0::AllWindow");

            BindInventories();
            craftingMenu.SetActive(true);
            Rendering.InitializeDisplay(GridWidth);
            Refresh();
            UpdateDisplay();
        }

        public void Deactivate() {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_CraftInsufficient, FlashIndicator);
            Request(GameEvent.Apply_ClearCrafting);
            InputPoller.RemoveContextFence("PlayerCraft", "3.5::Window");
            InputPoller.RemoveBinding("PlayerCraft:SELA", "3.0::AllWindow");

            Rendering.ReleaseDisplay();
            craftingMenu.SetActive(false);
            RecipeSearch.Deactivate();
        }

        private bool SendInteraction(PlayerInventoriesBehavior.InventoryAction action) {
            if (!Rendering.crafting.NonMatInventory.Display.GetMouseSelected(out int itemSlot)) itemSlot = -1;
            // The server chooses the material corner or item slot using its current cursor.
            Vector3[] corners = new Vector3[4];
            Rendering.crafting.display.Transform.GetWorldCorners(corners);
            int2 origin = new int2((int)corners[0].x, (int)corners[0].y);
            float2 relative = (((float3)Input.mousePosition).xy - origin) / GridSize;
            int materialSlot = -1;
            if (math.all(relative >= 0) && math.all(relative < GridWidth)) {
                int2 point = (int2)math.round(relative);
                materialSlot = point.x + point.y * AxisWidth;
            }
            if (materialSlot < 0 && itemSlot < 0) return false;

            inventories.RequestInteraction(PlayerInventoriesBehavior.InventoryKind.CraftingMaterials,
                new PlayerInventoriesBehavior.InventoryInteraction {
                    Action = action, Slot = materialSlot, AlternateSlot = itemSlot, CraftingTarget = true
                });
            return true;
        }

        private void Select(float _ = 0) {
            if (SendInteraction(PlayerInventoriesBehavior.InventoryAction.Select))
                InputPoller.SuspendKeybindPropogation("Select", ActionBind.Exclusion.ExcludeLayer);
        }

        private void DeselectDrag(float _ = 0) {
            if (SendInteraction(PlayerInventoriesBehavior.InventoryAction.DeselectDrag))
                InputPoller.SuspendKeybindPropogation("Deselect", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectPartial(float _ = 0) {
            if (SendInteraction(PlayerInventoriesBehavior.InventoryAction.SelectPartial))
                InputPoller.SuspendKeybindPropogation("SelectPartial", ActionBind.Exclusion.ExcludeLayer);
        }

        private void SelectAll(float _ = 0) {
            inventories.SelectAll(PlayerInventoriesBehavior.InventoryKind.CraftingMaterials);
            inventories.SelectAll(PlayerInventoriesBehavior.InventoryKind.CraftingItems);
            Refresh();
        }

        // Update is called once per frame
        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Visual || !self.IsOwner
                || craftingMenu == null || !craftingMenu.activeSelf) return;

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
            GraphicsGeneration.SetBufferData(craftingBuffer, ingredients);
        }

        private void EvaluateInfluence(Action<int2, float> callback) {
            Vector3[] corners = new Vector3[4];
            Rendering.crafting.display.Transform.GetWorldCorners(corners);
            int2 origin = new int2((int)corners[0].x, (int)corners[0].y);
            float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
            if (math.any(relativePos < 0) || math.any(relativePos >= GridWidth))
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
            Request(GameEvent.Apply_Craft);
            Refresh();
        }

        public void Refresh() {
            Rendering.IsDirty = true;
            if (craftingMenu == null || !craftingMenu.activeSelf) return;
            Rendering.crafting.RefreshInventoryGrid(Rendering.MainGridMatItems);
            List<int> recipes = FindRecipes(settings.NumMaxSelections);
            FitRecipe = recipes.Count > 0 ? recipes[0] : -1;

            for (int r = 0; r < recipes.Count; r++) {
                Rendering.selections[r].RefreshGridWithRecipe(GetRecipe(recipes[r]), GridWidth);
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

        private void BindInventories() {
            Rendering.MainGridMatItems = Materials;
            Rendering.crafting.NonMatInventory = Items;
        }

        private void Request(GameEvent type, object context = null) {
            var player = self;
            player.eventCtrl.RaiseEvent(type, player.info.rtEntityId, null, context);
        }

        private void FlashIndicator(object source, object target, object context) {
            if (context is not int recipeIndex) return;
            CraftingRecipe recipe = GetRecipe(recipeIndex);
            int2 offset = GetNormalizationOffset();
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
                    }
                }
            }
        }

        public List<int> FindRecipes(int count) =>
            Recipe.QueryNearestLimit(SerializeToRecipe(), settings.MaxRecipeDistance, count);

        public CraftingRecipe GetRecipe(int index) => Recipe.Table[index];

        public void Clear(Action<IItem> returnItem) {
            ReturnInventory(Materials);
            ReturnInventory(Items);

            void ReturnInventory(Inventory inventory) {
                for (int i = 0; i < inventory.Capacity; i++) {
                    IItem item = inventory.PeekItem(i);
                    if (item == null) continue;
                    item = (IItem)item.Clone();
                    inventory.RemoveEntry(i);
                    returnItem(item);
                }
            }
        }

        public int2 GetNormalizationOffset() {
            int2 minOffset = AxisWidth;
            for (int x = 0; x < AxisWidth; x++) {
                for (int y = 0; y < AxisWidth; y++) {
                    int index = x + y * AxisWidth;
                    if (Materials.Info[index] == null) continue;
                    minOffset = math.min(minOffset, new(x, y));
                }
            }
            for (int x = 0; x < GridWidth; x++) {
                for (int y = 0; y < GridWidth; y++) {
                    int index = x + y * GridWidth;
                    if (Items.Info[index] == null) continue;
                    minOffset = math.min(minOffset, new(x, y));
                }
            }
            return minOffset;
        }

        public CraftingRecipe SerializeToRecipe() {
            static CraftingRecipe.Ingredient[] SerializeIngArray(Inventory inv, int2 offset, int side) {
                CraftingRecipe.Ingredient[] ingArray = new CraftingRecipe.Ingredient[inv.capacity];
                for (int x = offset.x; x < side; x++) {
                    for (int y = offset.y; y < side; y++) {
                        int index = x + y * side;
                        IItem item = inv.Info[index];
                        if (item == null) continue;
                        index = (x - offset.x) + (y - offset.y) * side;
                        ingArray[index].Amount = PlayerCraftingBehavior.CraftingGrid.SmoothSplitLerp(
                            item.AmountRaw, item.UnitSize, item.StackLimit);
                        ingArray[index].Index = item.Index;
                    }
                }
                return ingArray;
            }
            int2 offset = GetNormalizationOffset();
            CraftingRecipe.Ingredient[] matGrid = SerializeIngArray(Materials, offset, AxisWidth);
            CraftingRecipe.Ingredient[] itemGrid = SerializeIngArray(Items, offset, GridWidth);
            return new CraftingRecipe {
                materials = new Option<List<CraftingRecipe.Ingredient>> { value = matGrid.ToList() },
                items = new Option<List<CraftingRecipe.Ingredient>> { value = itemGrid.ToList() }
            };
        }

        private bool CraftRecipe(PlayerInventoriesBehavior owner, IEventControlled ec, out int insufficientRecipe) {
            insufficientRecipe = -1;
            var matches = FindRecipes(1);
            int FitRecipe = matches.Count == 0 ? -1 : matches[0];
            if (FitRecipe == -1) return false;
            CraftingRecipe recipe = Recipe.Table[FitRecipe];
            int2 offset = GetNormalizationOffset();
            if (!VerifyIngredientList(Materials.Info,
                recipe.materials.value, offset, AxisWidth, out float minMat))
                return false;
            if (!VerifyIngredientList(Items.Info,
                recipe.items.value, offset, GridWidth, out float minItem))
                return false;
            float minIngred = math.min(minMat, minItem);

            if (recipe.NoSubUnitCreation && minIngred < 1) {
                insufficientRecipe = FitRecipe;

                return false;
            }

            //Clear grid of that amount
            ConsumeIngredientList(Materials, recipe.materials.value,
                offset, AxisWidth, minIngred);
            ConsumeIngredientList(Items, recipe.items.value,
                offset, GridWidth, minIngred);

            IItem result = recipe.ResultItem;
            minIngred *= recipe.result.Multiplier * result.UnitSize;

        int totalAmount = Mathf.FloorToInt(minIngred);
        int itemCount = Mathf.CeilToInt((float)totalAmount / result.StackLimit);
        int amount = math.min(totalAmount, result.StackLimit);
        for (int i = 0; i < itemCount; i++) {
            IItem item = (IItem)result.Clone();
            item.Create((int)recipe.result.Index, amount);
            ec?.RaiseEvent(GameEvent.Action_CraftItem, ec is Arterra.Data.Entity.Entity entity ? entity.info.rtEntityId : ec, item);
            owner.AddItem(item);

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
                    }
                }
                return true;
            }

            static void ConsumeIngredientList(IInventory inv, List<CraftingRecipe.Ingredient> recipe, int2 offset, int side, float minIngred) {
                for (int x = 0; x < side; x++) {
                    for (int y = 0; y < side; y++) {
                        int index = x + y * side;
                        CraftingRecipe.Ingredient ingred = recipe[index];
                        index = (x + offset.x) + (y + offset.y) * side;
                        IItem placedItem = index >= inv.Capacity ? null : inv.PeekItem(index);
                        if (ingred.Amount <= 0) continue;
                        if (placedItem == null || placedItem.AmountRaw == 0) continue;
                        int delta = Mathf.CeilToInt(placedItem.UnitSize * minIngred);
                        inv.RemoveStackableSlot(index, delta);
                    }
                }
            }
        }

        private void MatchRecipeInternal(int recipeIndex, PlayerInventoriesBehavior owner) {
            if ((uint)recipeIndex >= Recipe.Table.Length) return;
            CraftingRecipe recipe = Recipe.Table[recipeIndex];
            Dictionary<int, int> Deficit = new Dictionary<int, int>();
            Dictionary<int, int> Surplus = new Dictionary<int, int>();
            Catalogue<Arterra.Data.Item.Authoring> itemInfo = Config.CURRENT.Generation.Items;

            bool alreadyMatch = true; float maxBatchPossible = float.MaxValue;
            //Twice combined capacity so we don't overflow
            uint tempCapacity = Materials.capacity + Items.capacity;
            Inventory tempInv = new Inventory(2 * (int)tempCapacity);
            int itemOffset = (int)Materials.capacity;
            Materials.CopyTo(tempInv, 0, 0);
            Items.CopyTo(tempInv, 0, itemOffset);
            Materials.Clear(); Items.Clear();
            AccumulateRequiredIngredients(recipe.materials.value, 0);
            AccumulateRequiredIngredients(recipe.items.value, itemOffset);

            //Aquire items from inventory
            foreach (int item in Deficit.Keys) {
                if (!Surplus.TryGetValue(item, out int surplus))
                    surplus = 0;

                int required = Deficit[item];
                if (alreadyMatch) required = Mathf.FloorToInt(required * maxBatchPossible);
                if (surplus > required) continue;
                surplus += owner.RemoveStackable(required - surplus, item, item => tempInv.AddStackable(item));
                Surplus[item] = surplus;
            }

            PlaceRequiredIngredients(Materials, recipe.materials.value);
            PlaceRequiredIngredients(Items, recipe.items.value);

            //Dispose of temporaryInventory
            for (int i = 0; i < tempInv.capacity; i++) {
                IItem item = tempInv.Info[i];
                if (item == null) continue;
                item = item.Clone() as IItem;
                owner.AddItem(item);
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

            void PlaceRequiredIngredients(Inventory dest, List<CraftingRecipe.Ingredient> ingList) {
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

        public void MatchRecipe(int recipeIndex) => Request(GameEvent.Apply_MatchRecipe, recipeIndex);

        public struct Display {
            public CraftingGrid crafting;
            public CraftingGrid[] selections;
            public RectTransform[] corners;
            public Inventory MainGridMatItems;
            public bool IsDirty;
            public Display(GameObject menu, int NumMaxSelections, int GridWidth, ref int GridIndex) {
                int GridCount = (GridWidth + 1) * (GridWidth + 1);
                MainGridMatItems = new Inventory(GridCount);
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
            public Inventory NonMatInventory;
            internal static ItemContext GetCraftingCxt(ItemContext cxt) => cxt.SetupScenario(null, ItemContext.Scenario.ActivePlayerCraftingGrid);
            public CraftingGrid(GameObject parent, int GridWidth, int index) {
                int AxisWidth = GridWidth + 1;
                NonMatInventory = new Inventory(GridWidth * GridWidth);
                NonMatInventory.AddCallbacks(GetCraftingCxt, GetCraftingCxt);
                GridData = new CraftingRecipe.Ingredient[AxisWidth * AxisWidth];
                display = new Grid(parent, index);
                gridIndex = index;
            }
            internal static float SmoothSplitLerp(float x, float A, float B) {
                if (x <= A) return 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(x / A));
                else return 0.5f + 0.5f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((x - A) / (B - A)));
            }

            public void RefreshInventoryGrid(Inventory MatInv) {
                if (MatInv == null) return;
                for (int i = 0; i < MatInv.capacity; i++) {
                    IItem item = MatInv.Info[i];
                    GridData[i] = new CraftingRecipe.Ingredient { Index = 0, Amount = 0 };

                    if (item == null) continue;
                    Arterra.Data.Item.Authoring setting = Config.CURRENT.Generation.Items.Retrieve(item.Index);
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

                    Arterra.Data.Item.Authoring setting = Config.CURRENT.Generation.Items.Retrieve(ing.Index);
                    IItem item = setting.Item;
                    if (setting is not PlaceableItem mSettings) continue;
                    IRegister matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
                    if (!matInfo.Contains(mSettings.MaterialName)) continue;
                    GridData[i].Amount = SmoothSplitLerp(ing.Amount * item.UnitSize, item.UnitSize, item.StackLimit);
                    GridData[i].Index = matInfo.RetrieveIndex(mSettings.MaterialName);
                }

                ingredients = recipe.items.value;
                ReleaseDisplay();
                NonMatInventory = new Inventory(ingredients.Count());
                NonMatInventory.AddCallbacks(GetCraftingCxt, GetCraftingCxt);
                for (int i = 0; i < ingredients.Count(); i++) {
                    CraftingRecipe.Ingredient ing = ingredients[i];
                    Arterra.Data.Item.Authoring setting = Config.CURRENT.Generation.Items.Retrieve(ing.Index);
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
                tnsf.anchorMin = Vector2.one * 0.5f - size / 2.0f;
                tnsf.anchorMax = Vector2.one * 0.5f + size / 2.0f;
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

            public void ConstructTree(CraftingRecipe[] recipes, int matDim = 16, int itemDim = 9) {
                Table = recipes;
                tree = new List<Node>();
                (int, CraftingRecipe)[] layer = new (int, CraftingRecipe)[recipes.Length];
                for (int i = 0; i < recipes.Length; i++) {
                    //Ensure recipes are valid for given config; Recipe is Value Type so this is not a reference
                    while (recipes[i].materials.value.Count < matDim) {
                        recipes[i].materials.value.Add(new CraftingRecipe.Ingredient { Index = -1, Amount = 0 });
                    } while (recipes[i].items.value.Count < itemDim) {
                        recipes[i].items.value.Add(new CraftingRecipe.Ingredient { Index = -1, Amount = 0 });
                    }

                    if (recipes[i].materials.value.Count > matDim) {
                        recipes[i].materials.value = recipes[i].materials.value.Take(matDim).ToList();
                    }
                    if (recipes[i].items.value.Count > itemDim) {
                        recipes[i].items.value = recipes[i].items.value.Take(itemDim).ToList();
                    }
                    layer[i] = (i, recipes[i]);
                }
                head = BuildSubTree(layer, (uint)(matDim + itemDim));
            }

            static double GetL1Dist(CraftingRecipe a, CraftingRecipe b) {
                double sum = 0;
                int dim = a.materials.value.Count + a.items.value.Count;
                for (int i = 0; i < dim; i++) {
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
}
