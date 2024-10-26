using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using static CPUDensityManager;
using static PlayerHandler;
using static CraftingMenuSettings;

public class CraftingMenuController : UpdateTask
{
    private static KTree Recipe;
    private static CraftingMenuSettings settings;
    private static GameObject craftingMenu;
    private static MapData[] craftingData;
    private static Display Rendering;
    public static int FitRecipe = -1;


    private static CraftingMenuController Instance;

    private static int GridWidth;
    private static int AxisWidth => GridWidth + 1;
    private static int GridCount => AxisWidth * AxisWidth;
    private static int2 GridSize => (int2)(float2)Rendering.crafting.Transform.sizeDelta / GridWidth;

    public struct Display{
        public Grid crafting;
        public Grid[] selections;
        public RectTransform[] corners;
        public ComputeBuffer craftingBuffer;
        public bool IsDirty;
        public struct Grid{
            public GameObject Object;
            public RectTransform Transform;
            public Image Display;
            public Grid(GameObject obj){
                Object = obj;
                Transform = obj.GetComponent<RectTransform>();
                Display = obj.GetComponent<Image>();
            }
        }
    }

    // Start is called before the first frame update
    public static void Initialize()
    {
        settings = WorldStorageHandler.WORLD_OPTIONS.GamePlay.Crafting.value;
        craftingMenu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/CraftingMenu"), UIOrigin.UIHandle.transform);
        GridWidth = settings.GridWidth;

        int numMapTotal = GridCount * (settings.NumMaxSelections + 1);
        craftingData = new MapData[numMapTotal];
        Rendering.craftingBuffer = new ComputeBuffer(numMapTotal, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);

        Recipe.ConstructTree(settings.Recipes.Reg.value.ToArray().Select(x => x.value.Value).ToArray());
        InitializeCraftingArea();
        InitializeSelections();
        craftingMenu.SetActive(false);
    }

    public static void Release(){
        Rendering.craftingBuffer.Release();
    }

    static void InitializeCraftingArea(){
        Rendering.crafting = new Display.Grid(craftingMenu.transform.Find("CraftingGrid").gameObject);

        GameObject craftingPoint = Resources.Load<GameObject>("Prefabs/GameUI/CraftingPoint");
        Rendering.corners = new RectTransform[GridCount];

        for(int i = 0; i < GridCount; i++){
            Rendering.corners[i] = GameObject.Instantiate(craftingPoint, Rendering.crafting.Transform).GetComponent<RectTransform>();
            Rendering.corners[i].transform.Translate(new Vector3((i / AxisWidth) * GridSize.x, (i % AxisWidth) * GridSize.y, 0));
        }

        Rendering.crafting.Display.materialForRendering.SetBuffer("CraftingInfo", Rendering.craftingBuffer);
        Rendering.crafting.Display.materialForRendering.SetInt("IsoValue", (int)settings.CraftingIsoValue);
        Rendering.crafting.Display.materialForRendering.SetFloat("GridWidth", GridWidth);
        Rendering.crafting.Display.color = new Color(0, 0, 0);
        Rendering.IsDirty = false;
    }

    static void InitializeSelections(){
        GameObject SelectionArea = craftingMenu.transform.GetChild(1).GetChild(0).GetChild(0).gameObject;
        GameObject SelectionGrid = Resources.Load<GameObject>("Prefabs/GameUI/CraftingSelection");
        Rendering.selections = new Display.Grid[settings.NumMaxSelections];
        for(int i = 0; i < settings.NumMaxSelections; i++){
            Display.Grid grid = new(GameObject.Instantiate(SelectionGrid, SelectionArea.transform));
            grid.Display.color = new Color((i+1)/255.0f, 0, 0);
            Rendering.selections[i] = grid;
        }
    }

    static void UpdateDisplay(){
        if(!Rendering.IsDirty) return;
        Rendering.IsDirty = false;
        Rendering.craftingBuffer.SetData(craftingData);
    }

    // Update is called once per frame
    public override void Update(MonoBehaviour mono)
    {
        if(!active) return; 
        
        //Crafting Point Scaling
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            RectTransform craftingPoint = Rendering.corners[index];
            craftingPoint.transform.localScale = new Vector3(1, 1, 1) * math.lerp(1, settings.PointSizeMultiplier, influence);
        });

        UpdateDisplay();
    }   

    private static void EvaluateInfluence(Action<int2, float> callback){
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

    public static void AddMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            UpdateCrafting(index, HandleAddConservative(craftingData[index], influence));
        });
    }

    public static void RemoveMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * AxisWidth + corner.y;
            UpdateCrafting(index, HandleRemoveConservative(craftingData[index], influence));
        });
    }

    public static bool CraftRecipe(out InventoryController.Inventory.Slot result){
        result = new InventoryController.Inventory.Slot();
        if(FitRecipe == -1) return false;

        Recipe recipe = Recipe.Table[FitRecipe];
        for(int i = 0; i < GridCount; i++){
            int material = craftingData[i].material;
            if(craftingData[i].density < settings.CraftingIsoValue)
                material = -1;
            if(material != recipe.EntryMat(i)) 
                return false;
        }
        if(recipe.result.IsItem){
            result = new InventoryController.Inventory.Slot{
                IsItem = true,
                Index = recipe.ResultMat,
                AmountRaw = (int)math.round(recipe.result.Multiplier)
            };
        } else {
            int amount = 0;
            for(int i = 0; i < GridCount; i++){ amount += craftingData[i].density; }
            result = new InventoryController.Inventory.Slot{
                IsItem = false,
                IsSolid = recipe.result.IsSolid,
                Index = recipe.ResultMat,
                AmountRaw = (int)math.min(math.round(recipe.result.Multiplier * amount), 0x7FFF)
            };
        }
        return true;
    }

    private static void UpdateCrafting(int index, MapData map){
        MapData oldMap = craftingData[index];
        craftingData[index] = map;
        if(oldMap.material != map.material ||
        math.sign(map.density - settings.CraftingIsoValue) !=  
        math.sign(oldMap.density - settings.CraftingIsoValue)){
            RefreshSelections();
        }
        Rendering.IsDirty = true;
        
    }

    private static void RefreshSelections(){
        Recipe target = new Recipe{entry = new Option<List<MapData>>{ value = craftingData.ToList() }};
        for(int i = 0; i < craftingData.Length; i++){
            MapData p = target.entry.value[i];
            p.isDirty = craftingData[i].density < settings.CraftingIsoValue;
            target.entry.value[i] = p;
        } target.Names = null;

        List<int> recipes = Recipe.QueryNearestLimit(target, settings.MaxRecipeDistance, settings.NumMaxSelections);
        FitRecipe = recipes.Count > 0 ? recipes[0] : -1;

        for(int r = 0; r < recipes.Count; r++){
            Recipe recipe = Recipe.Table[recipes[r]];
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

    public static void Clear(){
        for(int i = 0; i < craftingData.Length; i++){
            craftingData[i] = new MapData();
        } Rendering.IsDirty = true; 
        for(int i = 0; i < Rendering.selections.Length; i++){
            Rendering.selections[i].Object.SetActive(false);
        } FitRecipe = -1;
    }

    public static void Activate(){
        Instance = new CraftingMenuController{active = true};
        EndlessTerrain.MainLoopUpdateTasks.Enqueue(Instance);

        Clear();
        UpdateDisplay();//
        craftingMenu.SetActive(true);
    }

    public static void Deactivate(){
        Instance.active = false;
        for(int i = 0; i < GridCount; i++){
            InventoryController.AddMaterial(new InventoryController.Inventory.Slot{
                IsItem = false,
                IsSolid = craftingData[i].viscosity != 0,
                Index = craftingData[i].material,
                AmountRaw = craftingData[i].density
            });
        }

        craftingMenu.SetActive(false);
    }

    static int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        staggeredDelta += (deltaDensity % 1) == 0 ? 0 : (Time.frameCount % Mathf.CeilToInt(1 / (deltaDensity % 1))) == 0 ? 1 : 0;
        staggeredDelta = Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);
        return staggeredDelta;
    }

    
    static MapData HandleAddConservative(MapData pointInfo, float brushStrength){
        brushStrength *= settings.CraftSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        InventoryController.Inventory.Slot selected = InventoryController.Selected;
        if(pointInfo.IsGaseous && pointInfo.material != selected.Index){
            InventoryController.AddMaterial(new InventoryController.Inventory.Slot{
                IsItem = false,
                IsSolid = pointInfo.viscosity != 0,
                Index = pointInfo.material,
                AmountRaw = pointInfo.density
            });

            pointInfo.density = InventoryController.RemoveMaterial(pointInfo.density);
            pointInfo.viscosity = selected.IsSolid ? pointInfo.density : 0;
            pointInfo.material = (int)selected.Index;
        }else if(pointInfo.material == selected.Index){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity);

            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            if(InventoryController.Selected.IsSolid) pointInfo.viscosity = pointInfo.density;
            if(pointInfo.IsSolid || pointInfo.IsLiquid) pointInfo.material = (int)selected.Index;
        }
        return pointInfo;
    }

    static MapData HandleRemoveConservative(MapData pointInfo, float brushStrength){
        brushStrength *= settings.CraftSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int deltaDensity = GetStaggeredDelta(pointInfo.density, -brushStrength);
        int newDens = InventoryController.AddMaterial(new InventoryController.Inventory.Slot{
            IsItem = false,
            IsSolid = pointInfo.viscosity != 0,
            Index = pointInfo.material,
            AmountRaw = deltaDensity
        });
        deltaDensity = newDens;

        pointInfo.density -= deltaDensity;
        pointInfo.viscosity = math.min(pointInfo.viscosity, pointInfo.density);
        return pointInfo;
    }

    public struct KTree{
        public Recipe[] Table;
        public List<Node> tree;
        private int head;
        public struct Node{
            public int Split;
            public int Left;
            public int Right;
            public byte Axis;
        }

        public void ConstructTree(Recipe[] recipes, int dim = 16){
            Table = recipes;
            tree = new List<Node>();
            (int, Recipe)[] layer = new (int, Recipe)[recipes.Length];
            for(int i = 0; i < recipes.Length; i++){
                layer[i] = (i, recipes[i]);
            }
            head = BuildSubTree(layer, (uint)dim);
        }

        static int GetL1Dist(Recipe a, Recipe b){
            int sum = 0;
            for(int i = 0; i < a.entry.value.Count; i++){
                sum += math.abs(a.EntryMat(i) - b.EntryMat(i));
            }
            return sum;
        }

        public List<int> QueryNearestLimit(Recipe tg, int dist, int count){
            List<int> ret = new List<int>();
            QueryNearest(tg, dist, (int recipe) => {
                ret.Add(recipe);
            });
            var table = Table;
            ret.Sort((a, b) => GetL1Dist(table[a], tg) - GetL1Dist(table[b], tg));
            return ret.Take(count).ToList();
        }

        private void QueryNearest(Recipe tg, int dist, Action<int> cb){
            Queue<int> layer = new Queue<int>();
            layer.Enqueue(head);

            while(layer.Count > 0){
                int node = layer.Dequeue();
                if(node == -1) continue;

                Node n = tree[node];
                Recipe recipe = Table[n.Split];
                int targetDist = tg.EntryMat(n.Axis) - recipe.EntryMat(n.Axis);
                if(math.abs(targetDist) <= dist){
                    if(GetL1Dist(recipe, tg) <= dist) 
                        cb(n.Split);
                    layer.Enqueue(n.Left);
                    layer.Enqueue(n.Right);
                } else {
                    int next = targetDist >= 0 ? n.Right : n.Left;
                    layer.Enqueue(next);
                }
            }
        }
        private int BuildSubTree((int, Recipe)[] layer, uint dim){
            int GetMaximumDimensions(){
                HashSet<(int, uint)> dimLens = new HashSet<(int, uint)>();
                foreach((int, Recipe) rep in layer){
                    Recipe recipe = rep.Item2;
                    for(int i = 0; i < dim; i++){
                        dimLens.Add((i, (uint)recipe.EntryMat(i)));
                    }
                }

                int[] dimCounts = new int[dim];
                foreach((int dimension, uint material) in dimLens){ dimCounts[dimension]++; }
                return dimCounts.Max();
            }

            if(layer.Length <= 0) return -1;
            int maxDim = GetMaximumDimensions();
            Array.Sort(layer, (a, b) => a.Item2.EntryMat(maxDim) - b.Item2.EntryMat(maxDim));
            
            int mid = layer.Length / 2;
            tree.Add(new Node{
                Split = layer[mid].Item1,
                Axis = (byte)maxDim,
                Left = BuildSubTree(layer.Take(mid).ToArray(), dim),
                Right = BuildSubTree(layer.Skip(mid+1).ToArray(), dim),
            });
            return tree.Count - 1;
        }
    }

}
