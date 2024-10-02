using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Unity.Mathematics;
using System.Linq;
using UnityEngine.Rendering;
using static CPUDensityManager;
using static PlayerHandler;
using Newtonsoft.Json;


[Serializable]
public class TerraformSettings : ICloneable{
    public int terraformRadius = 5;
    public float terraformSpeed = 4;
    public float maxTerraformDistance = 60;
    public int materialCapacity = 51000;
    public float minInvMatThresh = 1f;

    public float CursorSize = 2;
    public Vec4 CursorColor = new (0, 1, 0, 0.5f);
    public bool ShowCursor = true;

    public object Clone(){
        return new TerraformSettings{
            terraformRadius = this.terraformRadius,
            terraformSpeed = this.terraformSpeed,
            maxTerraformDistance = this.maxTerraformDistance,
            materialCapacity = this.materialCapacity,
            minInvMatThresh = this.minInvMatThresh
        };
    }
}

public class TerraformController
{
    private TerraformSettings settings;
    private bool shiftPressed = false;
    private bool hasHit;
    float3 hitPoint;

    int IsoLevel;

    Material OverlayMaterial;
    Mesh SphereMesh;
    Transform cam;



    // Start is called before the first frame update
    public TerraformController()
    {
        settings = WorldStorageHandler.WORLD_OPTIONS.GamePlay.value.Terraforming.value;
        cam = Camera.main.transform;

        Inventory.Initialize(GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/MaterialBar"), UIOrigin.UIHandle.transform));
        IsoLevel = Mathf.RoundToInt(WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value.IsoLevel * 255);
        SetUpOverlay();

        InputPoller.AddBinding(new InputPoller.Binding("Place Liquid", "GamePlay", InputPoller.BindPoll.Down, (_) => shiftPressed = true));
        InputPoller.AddBinding(new InputPoller.Binding("Place Liquid", "GamePlay", InputPoller.BindPoll.Up, (_) => shiftPressed = false));
        InputPoller.AddBinding(new InputPoller.Binding("Place Terrain", "GamePlay", InputPoller.BindPoll.Hold, PlaceTerrain));
        InputPoller.AddBinding(new InputPoller.Binding("Remove Terrain", "GamePlay", InputPoller.BindPoll.Hold, RemoveTerrain));
        InputPoller.AddBinding(new InputPoller.Binding("Next Material", "UI", InputPoller.BindPoll.Down, NextMaterial));
        InputPoller.AddBinding(new InputPoller.Binding("Previous Material", "UI", InputPoller.BindPoll.Down, PreviousMaterial));
    }


    public void Update()
    {
        RayTest();
        DrawOverlays();
    }

    void SetUpOverlay(){
        Shader shader = Shader.Find("Hidden/Internal-Colored");
        OverlayMaterial = new Material(shader);
        OverlayMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
        OverlayMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
        OverlayMaterial.SetInt("_Cull", (int)CullMode.Back);
        OverlayMaterial.SetInt("_ZTest", (int)CompareFunction.Less);
        OverlayMaterial.SetInt("_ZWrite", 1);

        float3[] positions = GenerateIcosphere();


        Vector3[] vertices = new Vector3[positions.Length];
        int[] triangles = new int[positions.Length];
        
        for (int i = 0; i < positions.Length; i++){
            vertices[i] = math.normalize(positions[i]);
            triangles[i] = i;
        }

        SphereMesh = new Mesh{
            vertices = vertices,
            triangles = triangles
        };
    }

    float3[] GenerateIcosphere(){
        float phi = (1.0f + math.sqrt(5.0f)) / 2.0f;
        float a = 1.0f;
        float b = 1.0f / phi;

        float3 v1  = new (0, b, -a);
        float3 v2  = new(b, a, 0);
        float3 v3  = new(-b, a, 0);
        float3 v4  = new(0, b, a);
        float3 v5  = new(0, -b, a);
        float3 v6  = new(-a, 0, b);
        float3 v7  = new(0, -b, -a);
        float3 v8  = new(a, 0, -b);
        float3 v9  = new(a, 0, b);
        float3 v10 = new(-a, 0, -b);
        float3 v11 = new(b, -a, 0);
        float3 v12 = new(-b, -a, 0);

        float3[] mesh = new float3[]{
            v3, v2, v1,
            v2, v3, v4,
            v6, v5, v4,
            v5, v9, v4,
            v8, v7, v1,
            v7, v10, v1,
            v12, v11, v5,
            v11, v12, v7,
            v10, v6, v3,
            v6, v10, v12, 
            v9, v8, v2,
            v8, v9, v11,
            v3, v6, v4,
            v9, v2, v4,
            v10, v3, v1,
            v2, v8, v1,
            v12, v10, v7,
            v8, v11, v7,
            v6, v12, v5,
            v11, v9, v5
        };

        for(int i = 0; i < 2; i++){//
            List<float3> newMesh = new List<float3>();
            for(int j = 0; j < mesh.Length; j += 3){
                v1 = mesh[j];
                v2 = mesh[j + 1];
                v3 = mesh[j + 2];

                v4 = (v1 + v2) / 2;
                v5 = (v2 + v3) / 2;
                v6 = (v3 + v1) / 2;

                newMesh.Add(v1);
                newMesh.Add(v4);
                newMesh.Add(v6);

                newMesh.Add(v4);
                newMesh.Add(v2);
                newMesh.Add(v5);

                newMesh.Add(v6);
                newMesh.Add(v5);
                newMesh.Add(v3);

                newMesh.Add(v4);
                newMesh.Add(v5);
                newMesh.Add(v6);
            }
            mesh = newMesh.ToArray();
        }
        return mesh;
    }

    void DrawOverlays(){
        if(!hasHit) return;
        if(!settings.ShowCursor) return;
        RenderParams rp = new (OverlayMaterial){
            worldBounds = new Bounds(GSToWS(hitPoint), 2 * settings.terraformRadius * Vector3.one),
            matProps = new MaterialPropertyBlock(),
            renderingLayerMask = 1,
        }; 
        rp.matProps.SetColor("_Color", settings.CursorColor.GetColor());
        Matrix4x4 transform = math.mul(Matrix4x4.Translate(GSToWS(hitPoint)), Matrix4x4.Scale(settings.CursorSize * Vector3.one));
        Graphics.RenderMesh(rp, SphereMesh, 0, transform);
    }

    void RayTest(){
        uint RayTestSolid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)pointInfo.viscosity; 
        }
        uint RayTestLiquid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }

        float3 camPosGC = WSToGS(cam.position);
        if(shiftPressed) hasHit = RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestLiquid, out hitPoint);
        else hasHit = RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestSolid, out hitPoint);
    }

    private void NextMaterial(float _){
        Inventory.NextMaterial();
        Inventory.Rendering.IsDirty = true;
    }

    private void PreviousMaterial(float _){
        Inventory.PreviousMaterial();
        Inventory.Rendering.IsDirty = true;
    }

    private void PlaceTerrain(float _){
        if (!hasHit) return;
        if(Inventory.selected.isSolid)
            CPUDensityManager.Terraform((int3)hitPoint, settings.terraformRadius, HandleAddSolid);
        else
            CPUDensityManager.Terraform((int3)hitPoint, settings.terraformRadius, HandleAddLiquid);
        Inventory.Rendering.IsDirty = true;
    }

    private void RemoveTerrain(float _){
        if (!hasHit) return;
        if(shiftPressed)
            CPUDensityManager.Terraform((int3)hitPoint, settings.terraformRadius, HandleRemoveLiquid);
        else 
            CPUDensityManager.Terraform((int3)hitPoint, settings.terraformRadius, HandleRemoveSolid);
        Inventory.Rendering.IsDirty = true;
    }

    int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        staggeredDelta += (deltaDensity % 1) == 0 ? 0 : (Time.frameCount % Mathf.CeilToInt(1 / (deltaDensity % 1))) == 0 ? 1 : 0;
        staggeredDelta = Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);

        return staggeredDelta;
    }

    MapData HandleAddSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selected = Inventory.selected.material;
        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity < IsoLevel || pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(solidDensity, brushStrength);
            deltaDensity = Inventory.RemoveMaterialFromInventory(deltaDensity);

            solidDensity += deltaDensity;
            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            pointInfo.viscosity = math.min(pointInfo.viscosity + deltaDensity, 255);
            if(solidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }

    MapData HandleAddLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int selected = Inventory.selected.material;
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = Inventory.RemoveMaterialFromInventory(deltaDensity);

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }



    MapData HandleRemoveSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength);
            deltaDensity = Inventory.AddMaterialToInventory(new MaterialInventory.InvMat{material = pointInfo.material, isSolid = true}, deltaDensity);

            pointInfo.viscosity -= deltaDensity;
            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }

    MapData HandleRemoveLiquid(CPUDensityManager.MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);
            deltaDensity = Inventory.AddMaterialToInventory(new MaterialInventory.InvMat{material = pointInfo.material, isSolid = false}, deltaDensity);

            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }
}

//This logic is beyond me XD, spent a while thinking of how to implement this
//Slightly more efficient than ElementAt

public struct MaterialInventory{
    public Dictionary<uint, int> inventory;
    public int totalMaterialAmount;
    public int materialCapacity;
    public int selectedPos;
    public InvMat selected;
    [JsonIgnore]
    public MaterialDisplay Rendering;
    public struct MaterialDisplay{
        public RectTransform panelRectTransform;
        public Image inventoryMat;
        public ComputeBuffer buffer;
        [Range(0, 1)]
        public float size;
        public bool IsDirty;
    }

    public MaterialInventory(int capacity){
        inventory = new Dictionary<uint, int>();
        materialCapacity = capacity;
        totalMaterialAmount = 0;
        selectedPos = 0;
        selected = new InvMat{isNull = true};
        Rendering = new MaterialDisplay();
    }

    public uint[] GetInventoryKeys
    {
        get{
            uint[] keys = new uint[inventory.Count];
            inventory.Keys.CopyTo(keys, 0);
            return keys;
        }
    }

    public int[] GetInventoryValues
    {
        get
        {
            int[] values = new int[inventory.Count];
            inventory.Values.CopyTo(values, 0);
            return values;
        }
    }

    public readonly void Release() { Rendering.buffer?.Release(); }
    public void Initialize(GameObject materialBar)
    {
        Rendering.buffer = new ComputeBuffer(100, sizeof(int) + sizeof(float));
        // Get the RectTransform component of the UI panel.
        Rendering.panelRectTransform = materialBar.GetComponent<RectTransform>();
        Rendering.inventoryMat = materialBar.transform.GetChild(0).GetComponent<Image>();
        Rendering.inventoryMat.materialForRendering.SetBuffer("MainInventoryMaterial", Rendering.buffer);
        Rendering.IsDirty = true;
        Update();
    }
    public void Update() //DO NOT DO THIS IN ONENABLE, Shader is compiled later than OnEnabled is called
    {
        if(!Rendering.IsDirty) return;
        Rendering.IsDirty = false;

        Rendering.size = (float)totalMaterialAmount / materialCapacity;
        Rendering.panelRectTransform.transform.localScale = new Vector3(Rendering.size, 1, 1);

        int totalmaterials_M = inventory.Count;
        Rendering.buffer.SetData(MaterialData.GetInventoryData(this), 0, 0, totalmaterials_M);
        Rendering.inventoryMat.materialForRendering.SetInt("MainMaterialCount", totalmaterials_M);
        Rendering.inventoryMat.materialForRendering.SetInt("selectedMat", selected.material);
        Rendering.inventoryMat.materialForRendering.SetFloat("InventorySize", Rendering.size);
    }


    public void NextMaterial(){
        selectedPos++;
        selectedPos %= inventory.Count;
        selected = new InvMat{key = this.inventory.ElementAt(selectedPos).Key};
    }

    public void PreviousMaterial(){
        selectedPos--;
        if(selectedPos < 0) selectedPos = inventory.Count - 1;
        if(selectedPos >= 0) selected = new InvMat{key = this.inventory.ElementAt(selectedPos).Key};
        else selected.isNull = true;
    }

    public void ClearSmallMaterials(float threshold){
        uint[] keys = GetInventoryKeys;
        int[] values = GetInventoryValues;
        for(int i = 0; i < keys.Length; i++){
            if(values[i] < threshold){
                totalMaterialAmount -= values[i];
                inventory.Remove(keys[i]);
                if(selected.key == keys[i]) PreviousMaterial();
            }
        }
    }

    public int AddMaterialToInventory(InvMat materialIndex, int delta)
    {
        if(delta == 0) return 0;
        delta = Mathf.Min(totalMaterialAmount + delta, materialCapacity) - totalMaterialAmount;
        uint key = materialIndex.key;

        if (inventory.ContainsKey(key))
            inventory[key] += delta;
        else{
            inventory.Add(key, delta);
            if(selected.isNull) selected = materialIndex;
        }

        totalMaterialAmount += delta;
        return delta;
    }

    public int RemoveMaterialFromInventory(int delta)
    {
        if(delta == 0) return 0;
        delta = totalMaterialAmount - Mathf.Max(totalMaterialAmount - delta, 0);
        uint key = selected.key;

        if (inventory.ContainsKey(key)) {
            int amount = inventory[key];
            delta = amount - Mathf.Max(amount - delta, 0);
            inventory[key] -= delta;

            if (inventory[key] == 0){
                inventory.Remove(key);
                PreviousMaterial();
            }
        }
        else
            delta = 0;

        totalMaterialAmount -= delta;
        return delta;
    }

    public struct InvMat{
        public uint key;
        public int material {
            get => (int)(key & 0x7FFFFFFF);
            set => key = (uint)(value & 0x7FFFFFFF) | 0x80000000;
        }
        public bool isSolid {
            readonly get => (key & 0x80000000) != 0;
            set => key = value ? key | 0x80000000 : key & 0x7FFFFFFF;
        }

        public bool isNull{
            readonly get => key == 0xFFFFFFFF;
            set => key = value ? 0xFFFFFFFF : key;
        }
    }

    struct MaterialData
        {
            public uint index;
            public float percentage;

            public static MaterialData[] GetInventoryData(MaterialInventory inventory){
                uint[] indexes = inventory.GetInventoryKeys;
                int[] amounts = inventory.GetInventoryValues;
                int totalMaterials = inventory.inventory.Count;

                MaterialData[] materialInfo = new MaterialData[totalMaterials+1];
                materialInfo[0].percentage = 0;
                for (int i = 1; i <= totalMaterials; i++)
                {
                    materialInfo[i-1].index = indexes[i-1];
                    materialInfo[i].percentage = ((float)amounts[i-1]) / inventory.totalMaterialAmount + materialInfo[i - 1].percentage;
                }
                return materialInfo;
            }
        }
}
