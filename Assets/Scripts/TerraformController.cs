using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using static CPUDensityManager;
using static OctreeTerrain;


[Serializable]
public class TerraformSettings : ICloneable{
    public int terraformRadius = 5;
    public float terraformSpeed = 4;
    public float maxTerraformDistance = 60;
    public int PickupRadius = 2;

    public float CursorSize = 2;
    public Color CursorColor;
    public bool ShowCursor = true;

    public object Clone(){
        return new TerraformSettings{
            terraformRadius = this.terraformRadius,
            terraformSpeed = this.terraformSpeed,
            maxTerraformDistance = this.maxTerraformDistance,
            PickupRadius = this.PickupRadius,
            CursorSize = this.CursorSize,
            CursorColor = this.CursorColor,
            ShowCursor = this.ShowCursor
        };
    }
}

public class TerraformController : UpdateTask
{
    private Registry<MaterialData> matInfo => WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
    private Registry<ItemAuthoring> itemInfo => WorldStorageHandler.WORLD_OPTIONS.Generation.Items;
    public TerraformSettings settings => WorldStorageHandler.WORLD_OPTIONS.GamePlay.Terraforming.value;
    public Func<float3> CursorPlace; //function describing where the cursor is placed
    public bool hasHit;
    public float3 hitPoint;
    public int IsoLevel;

    Material OverlayMaterial;
    Mesh SphereMesh;
    Transform cam;



    // Start is called before the first frame update
    public TerraformController()
    {
        cam = Camera.main.transform;
        active = true;

        IsoLevel = Mathf.RoundToInt(WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value.IsoLevel * 255);
        SetUpOverlay();

        InputPoller.AddStackPoll(new InputPoller.ActionBind("BASE", (float _) => CursorPlace = RayTestSolid), "CursorPlacement");
        InputPoller.AddBinding(new InputPoller.ActionBind("Pickup Item", PickupItems), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceTerrain), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveTerrain), "5.0::GamePlay");
        MainLoopUpdateTasks.Enqueue(this);
    }


    public override void Update(MonoBehaviour mono)
    {
        hitPoint = CursorPlace();
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
        rp.matProps.SetColor("_Color", settings.CursorColor);
        Matrix4x4 transform = math.mul(Matrix4x4.Translate(GSToWS(hitPoint)), Matrix4x4.Scale(settings.CursorSize * Vector3.one));
        Graphics.RenderMesh(rp, SphereMesh, 0, transform);
    }

    public float3 RayTestSolid(){
        static uint RayTestSolid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)pointInfo.viscosity; 
        } 
        float3 camPosGC = WSToGS(cam.position);
        hasHit = RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestSolid, out float3 hit);
        return hit;
    }

    public float3 RayTestLiquid(){
        static uint RayTestLiquid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        float3 camPosGC = WSToGS(cam.position);
        hasHit = RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestLiquid, out float3 hit);
        return hit;
    }

    public void PlaceTerrain(float _){
        if(!hasHit) return;
        if(InventoryController.Selected == null) return;
        ItemAuthoring selMat = InventoryController.SelectedSetting;
        if(selMat.MaterialName == null || !matInfo.Contains(selMat.MaterialName)) return;

        if(selMat.IsSolid) Terraform(hitPoint, settings.terraformRadius, HandleAddSolid);
        else Terraform(hitPoint, settings.terraformRadius, HandleAddLiquid);
    }

    public void RemoveTerrain(float _){
        if (!hasHit) return;
        CPUDensityManager.Terraform(hitPoint, settings.terraformRadius, HandleRemoveSolid);
    }

    public static int GetStaggeredDelta(float deltaDensity){
        int remInvFreq = Mathf.CeilToInt(1 / math.frac(deltaDensity));
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        return  staggeredDelta + math.frac(deltaDensity) == 0 ? 0 : 
                (Time.frameCount % remInvFreq) == 0 ? 1 : 0;
    }

    public static int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = GetStaggeredDelta(deltaDensity);
        return Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);
    }

    MapData HandleAddSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null || !InventoryController.SelectedSetting.IsSolid) 
            return pointInfo;

        int selected = matInfo.RetrieveIndex(InventoryController.SelectedSetting.MaterialName);
        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity < IsoLevel || pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(solidDensity, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity);

            solidDensity += deltaDensity;
            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            pointInfo.viscosity = math.min(pointInfo.viscosity + deltaDensity, 255);
            if(solidDensity >= IsoLevel) pointInfo.material = selected;
        }
        return pointInfo;
    }

    public MapData HandleAddLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null || !InventoryController.SelectedSetting.IsLiquid) 
            return pointInfo;

        int selected = matInfo.RetrieveIndex(InventoryController.SelectedSetting.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoLevel || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity);

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

            int itemIndex = itemInfo.RetrieveIndex(matInfo.Retrieve(pointInfo.material).SolidItem);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Index = itemIndex;
            nMaterial.AmountRaw = deltaDensity;

            InventoryController.AddEntry(nMaterial);
            deltaDensity -= nMaterial.AmountRaw;

            pointInfo.viscosity -= deltaDensity;
            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }

    MapData HandleRemoveLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);

            int itemIndex = itemInfo.RetrieveIndex(matInfo.Retrieve(pointInfo.material).LiquidItem);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Index = itemIndex;
            nMaterial.AmountRaw = deltaDensity;
            
            deltaDensity -= nMaterial.AmountRaw;
            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }

    private void PickupItems(float _){
        var eReg = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities;
        unsafe void OnEntityFound(UIntPtr entity){
            Entity* e = (Entity*)entity;
            if(e->info.entityType != eReg.RetrieveIndex("EntityItem")) return;
            if(!e->active) return;
            EItem.EItemEntity* item = (EItem.EItemEntity*)e->obj;
            if(item->isPickedUp) return;

            IItem slot = item->item.Slot;
            if(slot == null) return;

            InventoryController.AddEntry(slot);
            if(slot.AmountRaw != 0) return;
            item->isPickedUp = true; 
            EntityManager.AddHandlerEvent(() => EntityManager.ReleaseEntity(e->info.entityId));
        }

        STree.TreeNode.Bounds bounds = new STree.TreeNode.Bounds{
            Min = (int3)hitPoint - settings.PickupRadius,
            Max = (int3)hitPoint + settings.PickupRadius
        };

        EntityManager.ESTree.Query(bounds, OnEntityFound);
    }
}
