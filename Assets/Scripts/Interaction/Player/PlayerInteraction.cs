using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using static CPUMapManager;
using WorldConfig;
using WorldConfig.Generation.Material;
using WorldConfig.Generation.Item;
using WorldConfig.Gameplay.Player;
using UnityEditor;

namespace WorldConfig.Gameplay.Player {
/// <summary>
/// Settings governing the user's ability to interact with the world.
/// This includes Terraforming, the act of changing the terrain map's information,
/// and interaction with entities and more.
/// </summary>
[Serializable]
public class Interaction : ICloneable{
    /// <summary> The radius, in grid space, of the spherical region around the user's
    /// cursor that will be modified when the user terraforms the terrain. </summary>
    public int TerraformRadius = 1;
    /// <summary>  The default information used for terrain terraforming if no tag is applied.
    /// See <see cref="ToolTag"/> for more information </summary>
    public Option<ToolTag> DefaultTerraform;
    /// <summary> The maximum distance, in grid space, that the user can terraform the terrain from.
    /// That is, the maximum distance the cursor can be from the user's camera for the user
    /// to be able to terraform around the cursor.  </summary>
    public float ReachDistance = 60;
    /// <summary> When picking up items, the speed at which resources from 
    /// <see cref="IAttackable"> entities that can be collected from </see> are collected. </summary>
    public float PickupRate = 0.5f;


    /// <summary> Clones the terraform settings. 
    /// Is necessary for modification through the
    /// <see cref="Option{T}"/> lazy config system. </summary>
    /// <returns>The duplicated terraform settings. </returns>
    public object Clone(){
        return new Interaction{
            TerraformRadius = this.TerraformRadius,
            DefaultTerraform = this.DefaultTerraform,
            ReachDistance = this.ReachDistance,
            PickupRate = this.PickupRate,
        };
    }
}
}

public static class PlayerInteraction
{
    private static Registry<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
    private static Registry<Authoring> itemInfo => Config.CURRENT.Generation.Items;
    public static Interaction settings => Config.CURRENT.GamePlay.Player.value.Interaction;
    private static PlayerStreamer.Player data => PlayerHandler.data;



    // Start is called before the first frame update
    public static void Initialize()
    {
        InputPoller.AddBinding(new InputPoller.ActionBind("Interact", PickupItems), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Place Terrain", PlaceTerrain), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Remove Terrain", RemoveTerrain), "5.0::GamePlay");
    }

    public static bool RayTestSolid(PlayerStreamer.Player data, out float3 hitPt){
        static uint RayTestSolid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)pointInfo.viscosity; 
        } 
        return RayCastTerrain(data.position, PlayerHandler.camera.forward, settings.ReachDistance, RayTestSolid, out hitPt);
    }

    public static bool RayTestLiquid(PlayerStreamer.Player data, out float3 hitPt){
        static uint RayTestLiquid(int3 coord){ 
            MapData pointInfo = SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        return RayCastTerrain(data.position, PlayerHandler.camera.forward, settings.ReachDistance, RayTestLiquid, out hitPt);
    }

    public static void PlaceTerrain(float _){
        PlayerHandler.data.animator.SetTrigger("IsPlacing");
        if(!RayTestSolid(data, out float3 hitPt)) return;
        if(EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var _))
            return;
        if(InventoryController.Selected == null) return;
        Authoring selMat = InventoryController.SelectedSetting;
        if(selMat.MaterialName == null || !matInfo.Contains(selMat.MaterialName)) return;
        
        if (selMat.IsSolid) Terraform(hitPt, settings.TerraformRadius, (MapData mapData, float speed) => HandleAddSolid(mapData, speed * settings.DefaultTerraform.value.TerraformSpeed));
        else Terraform(hitPt, settings.TerraformRadius, (MapData mapData, float speed) => HandleAddSolid(mapData, speed * settings.DefaultTerraform.value.TerraformSpeed));
    }

    public static void RemoveTerrain(float _)
    {
        PlayerHandler.data.animator.SetTrigger("IsPlacing");
        if (!RayTestSolid(data, out float3 hitPt)) return;
        if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var _))
            return;
        MapData RemoveSolidBareHand(MapData mapData, float speed){
            int material = mapData.material; ToolTag tag = settings.DefaultTerraform;
            if (matInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, material, out TagRegistry.IProperty prop))
                tag = prop as ToolTag;
            return HandleRemoveSolid(mapData, speed * tag.TerraformSpeed, tag.GivesItem);
        }
        
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveSolidBareHand);
    }

    public static int GetStaggeredDelta(float deltaDensity){
        int remInvFreq = Mathf.CeilToInt(1 / math.frac(deltaDensity));
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        return  staggeredDelta + ((Time.frameCount % remInvFreq) == 0 ? 1 : 0);
    }

    public static int GetStaggeredDelta(int baseDensity, float deltaDensity){
        int staggeredDelta = (deltaDensity > 0 ? 1 : -1) * GetStaggeredDelta(math.abs(deltaDensity));
        return Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, 255) - baseDensity);
    }

    public static MapData HandleAddSolid(MapData pointInfo, float brushStrength){
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null || !InventoryController.SelectedSetting.IsSolid) 
            return pointInfo;
        if(!matInfo.Contains(InventoryController.SelectedSetting.MaterialName))
            return pointInfo;

        int selected = matInfo.RetrieveIndex(InventoryController.SelectedSetting.MaterialName);
        int solidDensity = pointInfo.SolidDensity;
        if(solidDensity < IsoValue || pointInfo.material == selected){
            //If adding solid density, override water
            int deltaDensity = GetStaggeredDelta(solidDensity, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity);

            solidDensity += deltaDensity;
            pointInfo.density = math.min(pointInfo.density + deltaDensity, 255);
            pointInfo.viscosity = math.min(pointInfo.viscosity + deltaDensity, 255);
            if(solidDensity >= IsoValue) pointInfo.material = selected;
        }
        return pointInfo;
    }

    public static MapData HandleAddLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return pointInfo;
        if(InventoryController.Selected == null || !InventoryController.SelectedSetting.IsLiquid) 
            return pointInfo;
        if(!matInfo.Contains(InventoryController.SelectedSetting.MaterialName))
            return pointInfo;

        int selected = matInfo.RetrieveIndex(InventoryController.SelectedSetting.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if(liquidDensity < IsoValue || pointInfo.material == selected){
            //If adding liquid density, only change if not solid
            int deltaDensity = GetStaggeredDelta(pointInfo.density, brushStrength);
            deltaDensity = InventoryController.RemoveMaterial(deltaDensity);

            pointInfo.density += deltaDensity;
            liquidDensity += deltaDensity;
            if(liquidDensity >= IsoValue) pointInfo.material = selected;
        }
        return pointInfo;
    }



    public static MapData HandleRemoveSolid(MapData pointInfo, float brushStrength, bool ObtainMat = true){
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int solidDensity = pointInfo.SolidDensity;
        int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength);
        if(solidDensity >= IsoValue && ObtainMat){
            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.SolidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Create(itemIndex, deltaDensity);
            InventoryController.AddEntry(nMaterial);
            deltaDensity -= nMaterial.AmountRaw;
        }
        pointInfo.viscosity -= deltaDensity;
        pointInfo.density -= deltaDensity;
        return pointInfo;
    }

    public static MapData HandleRemoveLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = pointInfo.LiquidDensity;
        int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);
        if (liquidDensity >= IsoValue){
            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.LiquidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Create(itemIndex, deltaDensity);
            deltaDensity -= nMaterial.AmountRaw;
        }
        pointInfo.density -= deltaDensity;
        return pointInfo;
    }

    private static void PickupItems(float _){
        if(!RayTestSolid(data, out float3 hitPt)) hitPt = data.position + (float3)PlayerHandler.camera.forward * settings.ReachDistance;
        if(!EntityManager.ESTree.FindClosestAlongRay(data.position, hitPt, PlayerHandler.data.info.entityId, 
        out WorldConfig.Generation.Entity.Entity entity))
            return;
        
        if(!entity.active) return;
        if(entity is not IAttackable) return;
        IAttackable collectEntity = entity as IAttackable;
        if(!collectEntity.IsDead) return;
        IItem slot = collectEntity.Collect(settings.PickupRate);
        InventoryController.AddEntry(slot);
    }
}
