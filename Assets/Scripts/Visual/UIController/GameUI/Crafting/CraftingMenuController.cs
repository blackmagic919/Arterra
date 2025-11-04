using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;
using WorldConfig.Generation.Item;
using WorldConfig.Intrinsic;
using MapStorage;
using TMPro;
using TerrainGeneration;
using WorldConfig.Generation.Material;

public sealed class CraftingMenuController : PanelNavbarManager.INavPanel {
    public static CraftingMenuController instance;
    public CraftingRecipeSearch RecipeSearch;
    private Crafting settings;
    private KTree Recipe;
    private GameObject craftingMenu;
    private MapData[] craftingData;
    private Display Rendering;
    private IUpdateSubscriber eventTask;

    private int GridWidth;
    internal int AxisWidth => GridWidth + 1;
    internal int GridCount => AxisWidth * AxisWidth;
    private int2 GridSize => (int2)(float2)Rendering.crafting.Transform.sizeDelta / GridWidth;
    private int FitRecipe = -1;

    // Start is called before the first frame update
    public CraftingMenuController(Crafting settings) {
        this.settings = settings;
        craftingMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingMenu"), GameUIManager.UIHandle.transform);

        GridWidth = settings.GridWidth;
        int numMapTotal = GridCount * (settings.NumMaxSelections + 2);
        craftingData = new MapData[numMapTotal];
        Rendering.craftingBuffer = new ComputeBuffer(numMapTotal, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        Recipe.ConstructTree(settings.Recipes.Reg.Select(r => r.SerializeCopy()).ToArray(), dim: GridCount);
        RecipeSearch = new CraftingRecipeSearch(craftingMenu.transform);

        InitializeCraftingArea();
        InitializeSelections();
        craftingMenu.SetActive(false);

        instance?.Release();
        instance = this;
    }

    public void Release(){ Rendering.craftingBuffer.Release(); }

    public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
    public GameObject GetDispContent() => craftingMenu;

    public void Activate() {
        eventTask = new IndirectUpdate(Update);
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddContextFence("PlayerCraft", "3.5::Window", ActionBind.Exclusion.None);
            InputPoller.AddBinding(new ActionBind("Craft", CraftEntry), "PlayerCraft:CFT", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("CraftingGridPlace", AddMaterial), "PlayerCraft:PL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("CraftingGridRemove", RemoveMaterial), "PlayerCraft:RM", "3.5::Window");
        });

        Clear();
        UpdateDisplay();
        craftingMenu.SetActive(true);
        RecipeSearch.Activate();
    }

    public void Deactivate() {
        eventTask.Active = false;
        for (int i = 0; i < GridCount; i++) {
            if (craftingData[i].density == 0) continue;
            InventoryAddMapData(craftingData[i]);
        }
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence("PlayerCraft", "3.5::Window"));
        craftingMenu.SetActive(false);
        RecipeSearch.Deactivate();
    }

    private void InitializeCraftingArea() {
        Rendering.crafting = new Display.Grid(craftingMenu.transform.Find("CraftingGrid").gameObject);

        GameObject craftingPoint = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingPoint");
        Rendering.corners = new RectTransform[GridCount];

        for (int i = 0; i < GridCount; i++) {
            Rendering.corners[i] = GameObject.Instantiate(craftingPoint, Rendering.crafting.Transform).GetComponent<RectTransform>();
            Rendering.corners[i].transform.Translate(new Vector3((i / AxisWidth) * GridSize.x, (i % AxisWidth) * GridSize.y, 0));
        }

        Rendering.crafting.Display.materialForRendering.SetBuffer("CraftingInfo", Rendering.craftingBuffer);
        Rendering.crafting.Display.materialForRendering.SetInt("IsoValue", (int)CPUMapManager.IsoValue);
        Rendering.crafting.Display.materialForRendering.SetFloat("GridWidth", GridWidth);
        Rendering.crafting.Display.color = new Color(0, 0, 0);
        Rendering.IsDirty = false;
    }

    private void InitializeSelections(){
        GameObject SelectionArea = craftingMenu.transform.Find("CraftingHints").GetChild(0).GetChild(0).gameObject;
        GameObject SelectionGrid = Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingSelection");
        Rendering.selections = new Display.Grid[settings.NumMaxSelections];
        for(int i = 0; i < settings.NumMaxSelections; i++){
            Display.Grid grid = new(GameObject.Instantiate(SelectionGrid, SelectionArea.transform));
            grid.Display.color = new Color((i+2)/255.0f, 0, 0);
            Rendering.selections[i] = grid;
        }
    }

    internal void UpdateDisplay(){
        if(!Rendering.IsDirty) return;
        Rendering.IsDirty = false;
        Rendering.craftingBuffer.SetData(craftingData);
    }
    
    internal void ReflectGridRecipe(CraftingRecipe recipe, int gridIndex) {
        for(int i = 0; i < GridCount; i++)
            craftingData[GridCount * gridIndex + i] = recipe.entry.value[i];
    }

    // Update is called once per frame
    private void Update(MonoBehaviour mono) {
        if (!eventTask.Active) return;

        //Crafting Point Scaling
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            RectTransform craftingPoint = Rendering.corners[index];
            craftingPoint.transform.localScale = new Vector3(1, 1, 1) * math.lerp(1, settings.PointSizeMultiplier, influence);
        });

        UpdateDisplay();
    }   

    private void EvaluateInfluence(Action<int2, float> callback){
        Vector3[] corners = new Vector3[4];
        Rendering.crafting.Transform.GetWorldCorners(corners);
        int2 origin = new int2((int)corners[0].x, (int)corners[0].y);
        float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
        if (math.any(relativePos < 0) || math.any(relativePos >= instance.GridWidth))
            return;
        int2 gridPos = (int2)math.floor(relativePos);
        for(int i = 0; i < 4; i++){
            int2 corner = gridPos + new int2(i / 2, i % 2);
            float2 L1Dist =  1 - math.abs(relativePos - corner);
            float influence = math.clamp(L1Dist.x * L1Dist.y, 0, 1);
            callback(corner, influence);
        }
    }

    private void AddMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            UpdateCrafting(index, HandleAddConservative(craftingData[index], influence));
        });
    }

    private void RemoveMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            UpdateCrafting(index, HandleRemoveConservative(craftingData[index], influence));
        });
    }

    private bool CraftRecipe(){
        if(FitRecipe == -1) return false;

        CraftingRecipe recipe = Recipe.Table[FitRecipe];
        for(int i = 0; i < GridCount; i++){
            if (recipe.entry.value[i].isDirty) continue;
            
            if (recipe.entry.value[i].IsGaseous) {
                if (craftingData[i].IsGaseous) continue;
                else return false;
            } else if (recipe.entry.value[i].IsSolid && !craftingData[i].IsSolid)
                return false;
            else if (recipe.entry.value[i].IsLiquid && !craftingData[i].IsLiquid)
                return false;
            if (craftingData[i].material != recipe.EntryMat(i)) return false;
        }

        float accumulatedAmt = 0;
        for (int i = 0; i < GridCount; i++) { accumulatedAmt += craftingData[i].density; }
        IItem result = recipe.ResultItem;
        accumulatedAmt *= recipe.result.Multiplier;
        accumulatedAmt = math.max(accumulatedAmt / MapData.MaxDensity, recipe.MinQuantity);
        accumulatedAmt *= result.UnitSize;

        int totalAmount = Mathf.FloorToInt(accumulatedAmt);
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
    }

    private void UpdateCrafting(int index, MapData map){
        MapData oldMap = craftingData[index];
        craftingData[index] = map;
        if(oldMap.material != map.material ||
        math.sign(map.density - CPUMapManager.IsoValue) !=  
        math.sign(oldMap.density - CPUMapManager.IsoValue)){
            RefreshSelections();
        }
        Rendering.IsDirty = true;
        
    }

    private void RefreshSelections(){
        CraftingRecipe target = new CraftingRecipe{entry = new Option<List<MapData>>{ value = craftingData.ToList() }};
        for(int i = 0; i < craftingData.Length; i++){
            MapData p = target.entry.value[i];
            p.isDirty = craftingData[i].density < CPUMapManager.IsoValue;
            target.entry.value[i] = p;
        }

        List<int> recipes = Recipe.QueryNearestLimit(target, settings.MaxRecipeDistance, settings.NumMaxSelections);
        FitRecipe = recipes.Count > 0 ? recipes[0] : -1;

        for (int r = 0; r < recipes.Count; r++) 
            ReflectGridRecipe(Recipe.Table[recipes[r]], r+2);
            
        for(int i = 0; i < Rendering.selections.Length; i++){
            if(i < recipes.Count){
                if(!Rendering.selections[i].Object.activeSelf)
                    Rendering.selections[i].Object.SetActive(true);
            }else if(i >= recipes.Count && Rendering.selections[i].Object.activeSelf)
                Rendering.selections[i].Object.SetActive(false);
        }
    }

    private void Clear() {
        for (int i = 0; i < craftingData.Length; i++) {
            if (i == GridCount) i = 2 * GridCount; //skip the selection data
            craftingData[i] = new MapData();
        }
        Rendering.IsDirty = true;
        for (int i = 0; i < Rendering.selections.Length; i++) {
            Rendering.selections[i].Object.SetActive(false);
        }
        FitRecipe = -1;
    }
    
     private void CraftEntry(float _) {
        if (CraftRecipe())
            Clear();
    }


    private void InventoryAddMapData(MapData data) {
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        if (data.LiquidDensity != 0) {
            MapData lData = data; lData.viscosity = 0;
            IItem item = matInfo.Retrieve(lData.material).OnRemoved(int.MinValue, data);
            InventoryController.AddEntry(item);
        }

        if (data.SolidDensity != 0) {
            IItem item = matInfo.Retrieve(data.material).OnRemoved(int.MinValue, data);
            InventoryController.AddEntry(item);
        }
    }

    private int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        staggeredDelta += (deltaDensity % 1) == 0 ? 0 : (Time.frameCount % Mathf.CeilToInt(1 / (deltaDensity % 1))) == 0 ? 1 : 0;
        staggeredDelta = Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);
        return staggeredDelta;
    }

    private MapData HandleAddConservative(MapData pointInfo, float brushStrength){
        brushStrength *= settings.CraftSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null) return pointInfo;
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        Authoring sItemSetting = InventoryController.SelectedSetting;
        if (sItemSetting is not PlaceableItem mSettings) return pointInfo;
        if (!matInfo.Contains(mSettings.MaterialName)) return pointInfo;
        int selected = matInfo.RetrieveIndex(mSettings.MaterialName);

        if(pointInfo.IsGaseous && pointInfo.material != selected){
            InventoryAddMapData(pointInfo);

            pointInfo.density = InventoryController.RemoveStackable(pointInfo.density);
            pointInfo.viscosity = mSettings.IsSolid ? pointInfo.density : 0;
            pointInfo.material = selected;
        }else if(pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = InventoryController.RemoveStackable(deltaDensity);

            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            if(mSettings.IsSolid) pointInfo.viscosity = pointInfo.density;
            if(pointInfo.IsSolid || pointInfo.IsLiquid) pointInfo.material = selected;
        }
        return pointInfo;
    }

    private MapData HandleRemoveConservative(MapData pointInfo, float brushStrength){
        brushStrength *= settings.CraftSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        
        int deltaDensity = GetStaggeredDelta(pointInfo.density, -brushStrength);
        MapData rMap = pointInfo;
        rMap.density = deltaDensity;
        InventoryAddMapData(rMap);

        pointInfo.density -= deltaDensity;
        pointInfo.viscosity = math.min(pointInfo.viscosity, pointInfo.density);
        return pointInfo;
    }

    public struct Display {
        public Grid crafting;
        public Grid[] selections;
        public RectTransform[] corners;
        public ComputeBuffer craftingBuffer;
        public bool IsDirty;
        public struct Grid {
            public GameObject Object;
            public RectTransform Transform;
            public Image Display;
            public Grid(GameObject obj) {
                Object = obj;
                Transform = obj.GetComponent<RectTransform>();
                Display = obj.GetComponent<Image>();
            }
        }
    }

    public class CraftingRecipeSearch {
        private Transform Menu;
        private Transform SearchContainer;
        private TMP_InputField SearchInput;
        private RegistrySearchDisplay<CraftingAuthoring> RecipeSearch;

        private Transform RecipeDisplay;
        private Display.Grid RecipeGrid;
        private IndirectUpdate HighlightTask;


        public CraftingRecipeSearch(Transform craftingMenu) {
            var settings = Config.CURRENT.System.Crafting.value;
            Menu = craftingMenu.transform.Find("SearchArea");
            SearchInput = Menu.Find("SearchBar").GetComponentInChildren<TMP_InputField>();
            SearchContainer = Menu.Find("RecipeShelf").GetChild(0).GetChild(0);
            RecipeDisplay = Menu.Find("RecipeDisplay");
            HighlightTask = null;

            Registry<CraftingAuthoring> registry = Registry<CraftingAuthoring>.FromCatalogue(Config.CURRENT.System.Crafting.value.Recipes);
            GridUIManager RecipeContainer = new GridUIManager(SearchContainer.gameObject,
                Indicators.RecipeSelections.Get, settings.MaxRecipeSearchDisplay);
            RecipeSearch = new RegistrySearchDisplay<CraftingAuthoring>(
                registry, Menu, SearchInput, RecipeContainer
            );

            RecipeGrid = new(RecipeDisplay.Find("RecipeGrid").gameObject);
            RecipeGrid.Display.color = new Color(1 / 255.0f, 1, 0);
            SearchInput.onValueChanged.AddListener(DeactivateRecipeDisplay);
            DeactivateRecipeDisplay();
        }

        internal void Activate() {
            RecipeSearch.Activate();
            InputPoller.AddKeyBindChange(() => {
                InputPoller.AddBinding(new ActionBind("Select",
                    CraftingMenuSelect, ActionBind.Exclusion.None),
                    "PlayerCraft:SEL", "3.5::Window");
            });
        }

        internal void Deactivate() {
            DeactivateRecipeDisplay();
            RecipeSearch.Deactivate();
        }

        public void ActivateRecipeDisplay(CraftingRecipe recipe) {
            if (HighlightTask != null)
                HighlightTask.Active = false;
            HighlightTask = new IndirectUpdate(HighlightGridMaterial);
            OctreeTerrain.MainLoopUpdateTasks.Enqueue(HighlightTask);
            SearchContainer.gameObject.SetActive(false);
            RecipeDisplay.gameObject.SetActive(true);
            instance.ReflectGridRecipe(recipe, 1);

            HighlightGridMaterial(null);
            instance.Rendering.IsDirty = true;
            instance.UpdateDisplay();
        }

        public void DeactivateRecipeDisplay(string _ = null) {
            if(HighlightTask != null)
                HighlightTask.Active = false;
            HighlightTask = null;
            
            SearchContainer.gameObject.SetActive(true);
            RecipeDisplay.gameObject.SetActive(false);
        }

        private void CraftingMenuSelect(float _) {
            if (!SearchContainer.gameObject.activeSelf) return;
            if (!RecipeSearch.GridContainer.GetMouseSelected(out int index))
                return;
            ActivateRecipeDisplay(RecipeSearch.SlotEntries[index].SerializeCopy());
        }

        Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private void HighlightGridMaterial(MonoBehaviour _) {
            if (!GetMouseOverMaterial(out int material))
                material = -1;
            bool changed = false;
            for (int i = 0; i < instance.GridCount; i++) {
                if (instance.craftingData[instance.GridCount + i].material == material) {
                    //Update the visuals if the mouse-over material changes
                    changed |= !instance.craftingData[instance.GridCount + i].isDirty;
                    instance.craftingData[instance.GridCount + i].isDirty = true;
                } else {
                    changed |= instance.craftingData[instance.GridCount + i].isDirty;
                    instance.craftingData[instance.GridCount + i].isDirty = false;
                }
            }
            if (!changed) return;
            instance.Rendering.IsDirty = true;
            RecipeDisplay.Find("RecipeMaterial")
                .GetComponent<TextMeshProUGUI>().text = material >= 0 ?
                matInfo.RetrieveName(material) : "";
            instance.UpdateDisplay();
        }
        private bool GetMouseOverMaterial(out int material) {
            Vector3[] corners = new Vector3[4];
            RecipeGrid.Transform.GetWorldCorners(corners);
            int2 origin = new((int)corners[0].x, (int)corners[0].y);
            float2 GridSize = (float2)RecipeGrid.Transform.sizeDelta / instance.GridWidth;

            float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
            if (math.any(relativePos < 0) || math.any(relativePos >= instance.GridWidth)) {
                material = 0; return false;
            }
            int2 gridPos = (int2)math.floor(relativePos);
            MapData[] cornerInfo = new MapData[4];

            for (int i = 0; i < 4; i++) {
                int2 corner = gridPos + new int2(i / 2, i % 2);
                int index = corner.x * instance.AxisWidth + corner.y;
                cornerInfo[i] = instance.craftingData[instance.GridCount + index];
            }

            return DetermineMaterial(relativePos - gridPos, cornerInfo, out material);
        }
        

        private static bool DetermineMaterial(float2 d, MapData[] m, out int material) {
            static bool BilinearBlend(ref Span<int> c, float2 d, out int corner) {
                float c0 = c[0] * (1 - d.x) + c[2] * d.x;
                float c1 = c[1] * (1 - d.x) + c[3] * d.x;

                corner = 0;
                float density = c0 * (1 - d.y) + c1 * d.y;
                if (density < CPUMapManager.IsoValue) return false;

                float mDens = 0;
                for (int i = 0; i < 4; i++) {
                    int2 cn = new(i & 0x2, i & 0x1);
                    float cDens = (c[3 - i] - CPUMapManager.IsoValue) *
                        math.abs(d.x - cn.x) * math.abs(d.y - cn.y);
                    if (cDens < mDens) continue;
                    mDens = cDens;
                    corner = 3 - i;
                }
                return true;
            }
            
            //ensure we have a fraction < 1
            d = math.frac(d);
            material = -1;

            Span<int> c = stackalloc int[4] {
                m[0].LiquidDensity, m[1].LiquidDensity,
                m[2].LiquidDensity, m[3].LiquidDensity
            };

            if (BilinearBlend(ref c, d, out int corner) && !m[corner].IsNull) {
                material = m[corner].material;
                return true;   
            }

            c[0] = m[0].SolidDensity; c[1] = m[1].SolidDensity;
            c[2] = m[2].SolidDensity; c[3] = m[3].SolidDensity;

            if (BilinearBlend(ref c, d, out corner) && !m[corner].IsNull) {
                material = m[corner].material;
                return true;
            } else return false;
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
                while (recipes[i].entry.value.Count < dim) {
                    recipes[i].entry.value.Add(new MapData { data = 0 });
                }
                if (recipes[i].entry.value.Count > dim) {
                    recipes[i].entry.value = recipes[i].entry.value.Take(dim).ToList();
                }
                layer[i] = (i, recipes[i]);
            }
            head = BuildSubTree(layer, (uint)dim);
        }

        static int GetL1Dist(CraftingRecipe a, CraftingRecipe b) {
            int sum = 0;
            for (int i = 0; i < a.entry.value.Count; i++) {
                sum += math.abs(a.EntryMat(i) - b.EntryMat(i));
            }
            return sum;
        }

        public List<int> QueryNearestLimit(CraftingRecipe tg, int dist, int count) {
            List<int> ret = new List<int>();
            QueryNearest(tg, dist, (int recipe) => {
                ret.Add(recipe);
            });
            var table = Table;
            ret.Sort((a, b) => GetL1Dist(table[a], tg) - GetL1Dist(table[b], tg));
            return ret.Take(count).ToList();
        }

        private void QueryNearest(CraftingRecipe tg, int dist, Action<int> cb) {
            Queue<int> layer = new Queue<int>();
            layer.Enqueue(head);

            while (layer.Count > 0) {
                int node = layer.Dequeue();
                if (node == -1) continue;

                Node n = tree[node];
                CraftingRecipe recipe = Table[n.Split];
                int targetDist = tg.EntryMat(n.Axis) - recipe.EntryMat(n.Axis);
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
                HashSet<(int, uint)> dimLens = new HashSet<(int, uint)>();
                foreach ((int, CraftingRecipe) rep in layer) {
                    CraftingRecipe recipe = rep.Item2;
                    for (int i = 0; i < dim; i++) {
                        dimLens.Add((i, (uint)recipe.EntryMat(i)));
                    }
                }

                int[] dimCounts = new int[dim];
                foreach ((int dimension, uint material) in dimLens) { dimCounts[dimension]++; }
                return dimCounts.Max();
            }

            if (layer.Length <= 0) return -1;
            int maxDim = GetMaximumDimensions();
            Array.Sort(layer, (a, b) => a.Item2.EntryMat(maxDim) - b.Item2.EntryMat(maxDim));

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
