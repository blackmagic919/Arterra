using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.PlayerLoop;
using UnityEngine.UI;
using static CPUDensityManager;
using static PlayerHandler;

public class CraftingMenuController : UpdateTask
{
    private static CraftingMenuSettings settings;
    private static GameObject craftingArea;
    private static RectTransform craftingGrid;
    private static RectTransform[] craftingPoints;
    private static MapData[] craftingData;
    private static DisplaySettings Rendering;


    private static CraftingMenuController UpdatePanel;
    private static Queue<(string, uint)> Fences;

    private static int2 GridSize => (int2)(float2)craftingGrid.GetComponent<RectTransform>().sizeDelta / 3;

    [Serializable]
    public struct CraftingMenuSettings{
        public int CraftSpeed; //200
        public int PointSizeMultiplier; //2
        public uint CraftingIsoValue; //128
    }

    public struct DisplaySettings{
        public Image craftingDisplay;
        public ComputeBuffer craftingBuffer;
        public bool IsDirty;
    }
    // Start is called before the first frame update
    public static void Initialize()
    {
        settings = WorldStorageHandler.WORLD_OPTIONS.GamePlay.value.Crafting.value;
        craftingArea = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/CraftingGrid"), UIOrigin.UIHandle.transform);
        craftingGrid = craftingArea.GetComponent<RectTransform>();  
        Fences = new Queue<(string, uint)>();

        GameObject craftingPoint = Resources.Load<GameObject>("Prefabs/GameUI/CraftingPoint");
        craftingPoints = new RectTransform[16];
        craftingData = new MapData[16];
        
        for(int i = 0; i < 16; i++){
            craftingPoints[i] = GameObject.Instantiate(craftingPoint, craftingGrid).GetComponent<RectTransform>();
            craftingPoints[i].transform.Translate(new Vector3((i / 4) * GridSize.x, (i % 4) * GridSize.y, 0));
        }
        SetUpDisplay();
        craftingArea.SetActive(false);
        InputPoller.AddBinding(new InputPoller.Binding("Toggle Crafting", "Control", InputPoller.BindPoll.Down, Activate));
    }

    public static void Release(){
        Rendering.craftingBuffer.Release();
    }

    static void SetUpDisplay(){
        Rendering.craftingBuffer = new ComputeBuffer(16, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Dynamic);
        Rendering.craftingDisplay = craftingArea.GetComponent<Image>();
        Rendering.craftingDisplay.materialForRendering.SetBuffer("CraftingInfo", Rendering.craftingBuffer);
        Rendering.craftingDisplay.materialForRendering.SetInt("IsoValue", (int)settings.CraftingIsoValue);
        Rendering.IsDirty = false;
    }

    static void UpdateDisplay(){
        if(!Rendering.IsDirty) return;
        Rendering.craftingBuffer.SetData(craftingData);
    }


    // Update is called once per frame
    public override void Update(MonoBehaviour mono)
    {
        if(!active) return; 
        
        //Crafting Point Scaling
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * 4 + corner.y;
            RectTransform craftingPoint = craftingPoints[index];
            craftingPoint.transform.localScale = new Vector3(1, 1, 1) * math.lerp(1, settings.PointSizeMultiplier, influence);
        });

        UpdateDisplay();
    }   

    private static void EvaluateInfluence(Action<int2, float> callback){
        int2 origin = (int2)((float3)craftingGrid.position).xy - GridSize * 3;
        float2 relativePos = (((float3)Input.mousePosition).xy - origin) / GridSize;
        int2 gridPos = (int2)math.clamp(relativePos, 0, 2);
        for(int i = 0; i < 4; i++){
            int2 corner = gridPos + new int2(i / 2, i % 2);
            float2 L1Dist =  1 - math.abs(relativePos - corner);
            float influence = math.clamp(L1Dist.x * L1Dist.y, 0, 1);
            callback(corner, influence);
        }
    }

    private static void AddMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * 4 + corner.y;
            craftingData[index] = HandleAddConservative(craftingData[index], influence);
            Inventory.Rendering.IsDirty = true;
            Rendering.IsDirty = true;
        });
    }

    private static void RemoveMaterial(float _){
        EvaluateInfluence((int2 corner, float influence) => {
            int index = corner.x * 4 + corner.y;
            craftingData[index] = HandleRemoveConservative(craftingData[index], influence);
            Inventory.Rendering.IsDirty = true;
            Rendering.IsDirty = true;
        });
    }

    static void Activate(float _){
        UpdatePanel = new CraftingMenuController{active = true};
        EndlessTerrain.MainLoopUpdateTasks.Enqueue(UpdatePanel);
        InputPoller.AddKeyBindChange(() =>{
            Fences.Enqueue(("Control", InputPoller.AddContextFence("Control")));
            Fences.Enqueue(("GamePlay", InputPoller.AddContextFence("GamePlay")));
            InputPoller.AddBinding(new InputPoller.Binding("Toggle Crafting", "Control", InputPoller.BindPoll.Down, Deactivate));
            InputPoller.AddBinding(new InputPoller.Binding("Place Terrain", "GamePlay", InputPoller.BindPoll.Hold, AddMaterial));
            InputPoller.AddBinding(new InputPoller.Binding("Remove Terrain", "GamePlay", InputPoller.BindPoll.Hold, RemoveMaterial));
        });

        craftingData = new MapData[16];
        for(int i = 0; i < 16; i++){
            craftingData[i] = new MapData();
        } UpdateDisplay();//
        craftingArea.SetActive(true);
        InputPoller.SetCursorLock(false);
    }

    static void Deactivate(float _){
        UpdatePanel.active = false;
        InputPoller.AddKeyBindChange(() =>{
            while(Fences.Count > 0){
                var (context, fence) = Fences.Dequeue();
                InputPoller.RemoveContextFence(fence, context);
            }
        });

        for(int i = 0; i < 16; i++){
            Inventory.AddMaterialToInventory(new MaterialInventory.InvMat{
                material = craftingData[i].material, 
                isSolid = craftingData[i].viscosity != 0
            }, craftingData[i].density);
            Inventory.Rendering.IsDirty = true;
        }

        craftingArea.SetActive(false);
        InputPoller.SetCursorLock(!false);
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

        MaterialInventory.InvMat selected = Inventory.selected;
        if(pointInfo.IsGaseous && pointInfo.material != selected.material){
            Inventory.AddMaterialToInventory(new MaterialInventory.InvMat{
                material = pointInfo.material, 
                isSolid = pointInfo.viscosity != 0}, 
            pointInfo.density);

            pointInfo.density = Inventory.RemoveMaterialFromInventory(pointInfo.density);
            pointInfo.viscosity = selected.isSolid ? pointInfo.density : 0;
            pointInfo.material = selected.material;
        }else if(pointInfo.material == selected.material){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = Inventory.RemoveMaterialFromInventory(deltaDensity);

            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            if(Inventory.selected.isSolid) pointInfo.viscosity = pointInfo.density;
            if(pointInfo.IsSolid || pointInfo.IsLiquid) pointInfo.material = selected.material;
        }
        return pointInfo;
    }

    static MapData HandleRemoveConservative(MapData pointInfo, float brushStrength){
        brushStrength *= settings.CraftSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int deltaDensity = GetStaggeredDelta(pointInfo.density, -brushStrength);
        deltaDensity = Inventory.AddMaterialToInventory(new MaterialInventory.InvMat{
            material = pointInfo.material, 
            isSolid = pointInfo.viscosity != 0}, 
        deltaDensity);

        pointInfo.density -= deltaDensity;
        pointInfo.viscosity = math.min(pointInfo.viscosity, pointInfo.density);
        return pointInfo;
    }
}
