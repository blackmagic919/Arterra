using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using static CPUMapManager;
using WorldConfig;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Item;
using WorldConfig.Gameplay;

namespace WorldConfig.Gameplay {
/// <summary>
/// Settings governing the user's ability to terraform the terrain.
/// Terraforming is the act of changing the terrain map's information
/// changing the terrain's apperance and creating new terrain features, 
/// </summary>
[Serializable]
public class Terraform : ICloneable{
    /// <summary> The radius, in grid space, of the spherical region around the user's
    /// cursor that will be modified when the user terraforms the terrain. </summary>
    public int terraformRadius = 5;
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float terraformSpeed = 4;
    /// <summary> The maximum distance, in grid space, that the user can terraform the terrain from.
    /// That is, the maximum distance the cursor can be from the user's camera for the user
    /// to be able to terraform around the cursor.  </summary>
    public float maxTerraformDistance = 60;
    /// <summary> When picking up items, the radius around the user's cursor in grid space that is checked for
    /// <see cref="EItem"> entity items </see> that can be picked up. </summary>
    public int PickupRadius = 2;

    /// <summary> If <see cref="ShowCursor"/> is true, the size of the cursor in world space. 
    /// The cursor is a sphere that is drawn along the ray in the direction the player is facing. </summary>
    public float CursorSize = 2;
    /// <summary> If <see cref="ShowCursor"/> is true, the color of the cursor. 
    /// The cursor is a sphere that is drawn along the ray in the direction the player is facing. </summary>
    public Color CursorColor;
    /// <summary> Whether or not to display a cursor when terraforming. The cursor is a sphere that is 
    /// drawn along the ray in the direction the player is facing. </summary>
    public bool ShowCursor = true;

    /// <summary> Clones the terraform settings. 
    /// Is necessary for modification through the
    /// <see cref="Option{T}"/> lazy config system. </summary>
    /// <returns>The duplicated terraform settings. </returns>
    public object Clone(){
        return new Terraform{
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
}

public class TerraformController : UpdateTask
{
    private Registry<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
    private Registry<Authoring> itemInfo => Config.CURRENT.Generation.Items;
    public Terraform settings => Config.CURRENT.GamePlay.Terraforming.value;
    public Func<float3> CursorPlace; //function describing where the cursor is placed
    public int IsoLevel;
    Transform cam;



    // Start is called before the first frame update
    public TerraformController()
    {
        cam = Camera.main.transform;
        active = true;

        IsoLevel = Mathf.RoundToInt(Config.CURRENT.Quality.Terrain.value.IsoLevel * 255);
        InputPoller.AddBinding(new InputPoller.ActionBind("Pickup Item", PickupItems), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceTerrain), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveTerrain), "5.0::GamePlay");
        TerrainGeneration.OctreeTerrain.MainLoopUpdateTasks.Enqueue(this);
    }

    public bool RayTestSolid(out float3 hitPt){
        static uint RayTestSolid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)pointInfo.viscosity; 
        } 
        float3 camPosGC = WSToGS(cam.position);
        return RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestSolid, out hitPt);
    }

    public bool RayTestLiquid(out float3 hitPt){
        static uint RayTestLiquid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        float3 camPosGC = WSToGS(cam.position);
        return RayCastTerrain(camPosGC, cam.forward, settings.maxTerraformDistance, RayTestLiquid, out hitPt);
    }

    public void PlaceTerrain(float _){
        
        if(!RayTestSolid(out float3 hitPt)) return;
        if(InventoryController.Selected == null) return;
        Authoring selMat = InventoryController.SelectedSetting;
        if(selMat.MaterialName == null || !matInfo.Contains(selMat.MaterialName)) return;

        if(selMat.IsSolid) Terraform(hitPt, settings.terraformRadius, HandleAddSolid);
        else Terraform(hitPt, settings.terraformRadius, HandleAddLiquid);
    }

    public void RemoveTerrain(float _){
        if (!RayTestSolid(out float3 hitPt)) return;
        CPUMapManager.Terraform(hitPt, settings.terraformRadius, HandleRemoveSolid);
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

    public MapData HandleAddSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null || !InventoryController.SelectedSetting.IsSolid) 
            return pointInfo;
        if(!matInfo.Contains(InventoryController.SelectedSetting.MaterialName))
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
        if(!matInfo.Contains(InventoryController.SelectedSetting.MaterialName))
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



    public MapData HandleRemoveSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength);

            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.SolidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
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

    public MapData HandleRemoveLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= IsoLevel){
            int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);
            
            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.LiquidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Index = itemIndex;
            nMaterial.AmountRaw = deltaDensity;
            
            deltaDensity -= nMaterial.AmountRaw;
            pointInfo.density -= deltaDensity;
        }
        return pointInfo;
    }

    private void PickupItems(float _){
        var eReg = Config.CURRENT.Generation.Entities;
        unsafe void OnEntityFound(WorldConfig.Generation.Entity.Entity entity){
            if(entity.info.entityType != eReg.RetrieveIndex("EntityItem")) return;
            if(!entity.active) return;
            EItem.EItemEntity item = (EItem.EItemEntity)entity;
            if(item.isPickedUp) return;

            IItem slot = item.item.Value;
            if(slot == null) return;

            InventoryController.AddEntry(slot);
            if(slot.AmountRaw != 0) return;
            item.isPickedUp = true; 
            EntityManager.ReleaseEntity(entity.info.entityId);
        }

        if(!RayTestSolid(out float3 hitPt)) return;
        EntityManager.STree.TreeNode.Bounds bounds = new (){
            Min = (int3)hitPt - settings.PickupRadius,
            Max = (int3)hitPt + settings.PickupRadius
        };

        EntityManager.ESTree.Query(bounds, OnEntityFound);
    }
}
