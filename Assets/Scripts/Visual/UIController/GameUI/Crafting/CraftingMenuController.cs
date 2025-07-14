using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using WorldConfig;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Item;
using WorldConfig.Intrinsic;
using MapStorage;
using TMPro;

public sealed class CraftingMenuController : PanelNavbarManager.INavPanel {
    private Crafting settings;
    private KTree Recipe;
    private GameObject craftingMenu;
    private MapData[] craftingData;
    private Display Rendering;
    private UpdateTask eventTask;
    private RegistrySearchDisplay<CraftingAuthoring> RecipeSearch;

    private int GridWidth;
    private int AxisWidth => GridWidth + 1;
    private int GridCount => AxisWidth * AxisWidth;
    private int2 GridSize => (int2)(float2)Rendering.crafting.Transform.sizeDelta / GridWidth;
    private uint Fence = 0;
    private int FitRecipe = -1;

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

    // Start is called before the first frame update
    public CraftingMenuController(Crafting settings) {
        this.settings = settings;
        craftingMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Crafting/CraftingMenu"), GameUIManager.UIHandle.transform);
        
        GridWidth = settings.GridWidth;
        int numMapTotal = GridCount * (settings.NumMaxSelections + 1);
        craftingData = new MapData[numMapTotal];
        Rendering.craftingBuffer = new ComputeBuffer(numMapTotal, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        Recipe.ConstructTree(settings.Recipes.Reg.Select(r => r.Recipe).ToArray(), dim: GridCount);
        RecipeSearch = new RegistrySearchDisplay<CraftingAuthoring>(
            Registry<CraftingAuthoring>.FromCatalogue(Config.CURRENT.System.Crafting.value.Recipes),
            craftingMenu.transform.Find("SearchArea"),
            craftingMenu.transform.Find("SearchArea").Find("SearchBar").GetComponentInChildren<TMP_InputField>(),
            craftingMenu.transform.Find("SearchArea").Find("RecipeShelf").GetChild(0).GetChild(0),
            settings.MaxRecipeSearchDisplay
        );

        InitializeCraftingArea();
        InitializeSelections();
        craftingMenu.SetActive(false);
        Fence = 0;
    }

    public void Release(){ Rendering.craftingBuffer.Release(); }

    public Sprite GetNavIcon() => Config.CURRENT.Generation.Textures.Retrieve(settings.DisplayIcon).self;
    public GameObject GetDispContent() => craftingMenu;

    public void Activate() {
        eventTask = new IndirectUpdate(Update);
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(eventTask);
        InputPoller.AddKeyBindChange(() => {
            Fence = InputPoller.AddContextFence("3.0::Window", InputPoller.ActionBind.Exclusion.None);
            InputPoller.AddBinding(new InputPoller.ActionBind("Craft", CraftEntry), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Place", AddMaterial), "3.0::Window");
            InputPoller.AddBinding(new InputPoller.ActionBind("Remove", RemoveMaterial), "3.0::Window");
        });

        Clear();
        UpdateDisplay();
        craftingMenu.SetActive(true);
        RecipeSearch.Activate();
    }

    public void Deactivate() {
        eventTask.active = false;
        for (int i = 0; i < GridCount; i++) {
            if (craftingData[i].density == 0) continue;
            InventoryAddMapData(craftingData[i]);
        }
        InputPoller.AddKeyBindChange(() => InputPoller.RemoveContextFence(Fence, "3.0::Window"));
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
            grid.Display.color = new Color((i+1)/255.0f, 0, 0);
            Rendering.selections[i] = grid;
        }
    }

    private void UpdateDisplay(){
        if(!Rendering.IsDirty) return;
        Rendering.IsDirty = false;
        Rendering.craftingBuffer.SetData(craftingData);
    }

    // Update is called once per frame
    private void Update(MonoBehaviour mono)
    {
        if(!eventTask.active) return; 
        
        //Crafting Point Scaling
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            RectTransform craftingPoint = Rendering.corners[index];
            craftingPoint.transform.localScale = new Vector3(1, 1, 1) * math.lerp(1, settings.PointSizeMultiplier, influence);
        });

        UpdateDisplay();
    }   

    private void EvaluateInfluence(Action<int2, float> callback){
        int2 origin = (int2)((float3)Rendering.crafting.Transform.position).xy - GridSize * GridWidth;
        float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
        int2 gridPos = (int2)math.clamp(relativePos, 0, GridWidth - 1);
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

    public bool CraftRecipe(out IItem result){
        result = null;
        if(FitRecipe == -1) return false;

        CraftingRecipe recipe = Recipe.Table[FitRecipe];
        for(int i = 0; i < GridCount; i++){
            if (recipe.entry.value[i].isDirty) continue;
            if (recipe.entry.value[i].IsGaseous) {
                if (craftingData[i].IsGaseous) continue;
                else return false;
            }

            if (recipe.entry.value[i].IsSolid && !craftingData[i].IsSolid)
                return false;
            if (recipe.entry.value[i].IsLiquid && !craftingData[i].IsLiquid)
                return false;
            if (craftingData[i].material != recipe.EntryMat(i)) return false;
        }

        int amount = 0;
        for(int i = 0; i < GridCount; i++){ amount += craftingData[i].density; }
        result = recipe.ResultItem;
        result.Create(recipe.ResultIndex, (int)math.min(math.round(recipe.result.Multiplier * amount), 0x7FFF));
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
        } target.Names = null;

        List<int> recipes = Recipe.QueryNearestLimit(target, settings.MaxRecipeDistance, settings.NumMaxSelections);
        FitRecipe = recipes.Count > 0 ? recipes[0] : -1;

        for(int r = 0; r < recipes.Count; r++){
            CraftingRecipe recipe = Recipe.Table[recipes[r]];
            for(int i = 0; i < GridCount; i++){
                craftingData[GridCount * (r + 1) + i] = recipe.EntrySerial(i);
            }
        }
        for(int i = 0; i < Rendering.selections.Length; i++){
            if(i < recipes.Count){
                if(!Rendering.selections[i].Object.activeSelf)
                    Rendering.selections[i].Object.SetActive(true);
            }else if(i >= recipes.Count && Rendering.selections[i].Object.activeSelf)
                Rendering.selections[i].Object.SetActive(false);
        }
    }

    public void Clear(){
        for(int i = 0; i < craftingData.Length; i++){
            craftingData[i] = new MapData();
        } Rendering.IsDirty = true; 
        for(int i = 0; i < Rendering.selections.Length; i++){
            Rendering.selections[i].Object.SetActive(false);
        } FitRecipe = -1;
    }
    
     private void CraftEntry(float _) {
        if (!CraftRecipe(out IItem result) || result == null)
            return;
        InventoryController.AddEntry(result);
        Clear();
    }


    private void InventoryAddMapData(MapData data) {
        var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        if (data.LiquidDensity != 0) {
            MapData lData = data; lData.viscosity = 0;
            IItem item = matInfo.Retrieve(lData.material).AcquireItem(data);
            InventoryController.AddEntry(item);
        }

        if (data.SolidDensity != 0) {
            IItem item = matInfo.Retrieve(data.material).AcquireItem(data);
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
        if(!matInfo.Contains(sItemSetting.MaterialName)) return pointInfo;
        int selected = matInfo.RetrieveIndex(sItemSetting.MaterialName);

        if(pointInfo.IsGaseous && pointInfo.material != selected){
            InventoryAddMapData(pointInfo);

            pointInfo.density = InventoryController.RemoveStackable(pointInfo.density);
            pointInfo.viscosity = sItemSetting.IsSolid ? pointInfo.density : 0;
            pointInfo.material = selected;
        }else if(pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = InventoryController.RemoveStackable(deltaDensity);

            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            if(sItemSetting.IsSolid) pointInfo.viscosity = pointInfo.density;
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

    public class RegistrySearchDisplay<T> where T : ISlot {
        private Registry<T> registry;
        private Transform SearchMenu;
        private TMP_InputField SearchInput;
        private Transform SelectionContainer;
        private T[] DisplaySlots;
        private int MaxSlotsDisplay;

        public RegistrySearchDisplay(
            Registry<T> registry,
            Transform SearchMenu,
            TMP_InputField SearchInput,
            Transform SelectionContainer,
            int MaxSlotsDisplay

        ) {
            this.registry = registry;
            this.SearchMenu = SearchMenu;
            this.SearchInput = SearchInput;
            this.SelectionContainer = SelectionContainer;
            this.MaxSlotsDisplay = math.min(MaxSlotsDisplay, registry.Count());
            SearchInput.onValueChanged.AddListener(ProcessSearchRequest);
            DisplaySlots = new T[MaxSlotsDisplay];
            SearchInput.DeactivateInputField();
        }

        public void Activate() {
            SearchInput.ActivateInputField();
            ProcessSearchRequest("");
            SearchMenu.gameObject.SetActive(true);
        }

        public void Deactivate() {
            ClearDisplay();
            SearchInput.DeactivateInputField();
            SearchMenu.gameObject.SetActive(false);
        }

        private void ProcessSearchRequest(string input) {
            List<int> closestEntries = FindClosestEntries(input);
            ClearDisplay();
            for (int i = 0; i < closestEntries.Count(); i++) {
                DisplaySlots[i] = registry.Retrieve(closestEntries[i]);
                DisplaySlots[i].AttachDisplay(SelectionContainer);
            }
        }

        private void ClearDisplay() {
            foreach (T slot in DisplaySlots) {
                if (slot == null) continue;
                slot.ClearDisplay(SelectionContainer);
            }
        }

        private List<int> FindClosestEntries(string input) {
            int[] entryDist = new int[registry.Count()];
            for (int i = 0; i < entryDist.Length; i++) {
                entryDist[i] = CalculateEditDistance(input, registry.RetrieveName(i));
            }

            List<int> sortedIndices = Enumerable.Range(0, registry.Count()).ToList();
            sortedIndices.Sort((a, b) => entryDist[a].CompareTo(entryDist[b]));
            return sortedIndices.GetRange(0, MaxSlotsDisplay).ToList();
        }
        private int CalculateEditDistance(string a, string b) {
            int[,] dp = new int[a.Length + 1, b.Length + 1];
            for (int i = a.Length; i >= 0; i--) dp[i, b.Length] = a.Length - i;
            for (int j = b.Length; j >= 0; j--) dp[a.Length, j] = b.Length - j;
            for (int i = a.Length - 1; i >= 0; i--) {
                for (int j = b.Length - 1; j >= 0; j--) {
                    if (a[i] == b[j]) dp[i, j] = dp[i + 1, j + 1];
                    else {
                        int best = dp[i + 1, j + 1];
                        best = math.min(best, dp[i + 1, j]);
                        best = math.min(best, dp[i, j + 1]);
                        dp[i, j] = best + 1;
                    }
                }
            }
            return dp[0, 0];
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
                if (recipes[i].Names.value.Count == 0) {
                    recipes[i].Names.value.Add("Void");
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
