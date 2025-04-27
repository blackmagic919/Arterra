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
    public int terraformRadius = 5;
    /// <summary> The speed at which the user can terraform the terrain. As terraforming is a 
    /// continuous process, the speed is measured in terms of change in density per frame. </summary>
    public float terraformSpeed = 4;
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
            terraformRadius = this.terraformRadius,
            terraformSpeed = this.terraformSpeed,
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
        InputPoller.AddBinding(new InputPoller.ActionBind("Pickup Item", PickupItems), "5.0::GamePlay");
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

        if(selMat.IsSolid) Terraform(hitPt, settings.terraformRadius, HandleAddSolid);
        else Terraform(hitPt, settings.terraformRadius, HandleAddLiquid);
    }

    public static void RemoveTerrain(float _){
        PlayerHandler.data.animator.SetTrigger("IsPlacing");
        if (!RayTestSolid(data, out float3 hitPt)) return;
        if(EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var _))
            return;
        CPUMapManager.Terraform(hitPt, settings.terraformRadius, HandleRemoveSolid);
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
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
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
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
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



    public static MapData HandleRemoveSolid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int solidDensity = pointInfo.SolidDensity;
        int deltaDensity = GetStaggeredDelta(solidDensity, -brushStrength);
        if(solidDensity >= IsoValue){
            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.SolidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Index = itemIndex;
            nMaterial.AmountRaw = deltaDensity;

            InventoryController.AddEntry(nMaterial);
            deltaDensity -= nMaterial.AmountRaw;
        }
        pointInfo.viscosity -= deltaDensity;
        pointInfo.density -= deltaDensity;
        return pointInfo;
    }

    public static MapData HandleRemoveLiquid(MapData pointInfo, float brushStrength){
        brushStrength *= settings.terraformSpeed * Time.deltaTime;
        if(brushStrength == 0) return pointInfo;

        int liquidDensity = pointInfo.LiquidDensity;
        int deltaDensity = GetStaggeredDelta(liquidDensity, -brushStrength);
        if (liquidDensity >= IsoValue){
            MaterialData material = matInfo.Retrieve(pointInfo.material);
            string key = material.RetrieveKey(material.LiquidItem);
            if(!itemInfo.Contains(key)) return pointInfo;

            int itemIndex = itemInfo.RetrieveIndex(key);
            IItem nMaterial = itemInfo.Retrieve(itemIndex).Item;
            nMaterial.Index = itemIndex;
            nMaterial.AmountRaw = deltaDensity;
            deltaDensity -= nMaterial.AmountRaw;
        }
        pointInfo.density -= deltaDensity;
        return pointInfo;
    }

    private static void PickupItems(float _){
        if(!RayTestSolid(data, out float3 hitPt)) hitPt = data.positionGS + (float3)PlayerHandler.camera.forward * settings.ReachDistance;
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

    public static void DetectMapInteraction(float3 centerGS, Action<float> OnInSolid = null, Action<float> OnInLiquid = null, Action<float> OnInGas = null){
        static (float, float) TrilinearBlend(float3 posGS){
            //Calculate Density
            int x0 = (int)Math.Floor(posGS.x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(posGS.y); int y1 = y0 + 1;
            int z0 = (int)Math.Floor(posGS.z); int z1 = z0 + 1;

            uint c000 = SampleMap(new int3(x0, y0, z0)).data;
            uint c100 = SampleMap(new int3(x1, y0, z0)).data;
            uint c010 = SampleMap(new int3(x0, y1, z0)).data;
            uint c110 = SampleMap(new int3(x1, y1, z0)).data;
            uint c001 = SampleMap(new int3(x0, y0, z1)).data;
            uint c101 = SampleMap(new int3(x1, y0, z1)).data;
            uint c011 = SampleMap(new int3(x0, y1, z1)).data;
            uint c111 = SampleMap(new int3(x1, y1, z1)).data;

            float xd = posGS.x - x0;
            float yd = posGS.y - y0;
            float zd = posGS.z - z0;

            float c00 = (c000 & 0xFF) * (1 - xd) + (c100 & 0xFF) * xd;
            float c01 = (c001 & 0xFF) * (1 - xd) + (c101 & 0xFF) * xd;
            float c10 = (c010 & 0xFF) * (1 - xd) + (c110 & 0xFF) * xd;
            float c11 = (c011 & 0xFF) * (1 - xd) + (c111 & 0xFF) * xd;

            float c0 = c00 * (1 - yd) + c10 * yd;
            float c1 = c01 * (1 - yd) + c11 * yd;
            float density = c0 * (1 - zd) + c1 * zd;

            c000 = c000 >> 8 & 0xFF; c100 = c100 >> 8 & 0xFF;
            c010 = c010 >> 8 & 0xFF; c110 = c110 >> 8 & 0xFF;
            c001 = c001 >> 8 & 0xFF; c101 = c101 >> 8 & 0xFF;
            c011 = c011 >> 8 & 0xFF; c111 = c111 >> 8 & 0xFF;

            c00 = c000 * (1 - xd) + c100 * xd;
            c01 = c001 * (1 - xd) + c101 * xd;
            c10 = c010 * (1 - xd) + c110 * xd;
            c11 = c011 * (1 - xd) + c111 * xd;

            c0 = c00 * (1 - yd) + c10 * yd;
            c1 = c01 * (1 - yd) + c11 * yd;
            float viscosity = c0 * (1 - zd) + c1 * zd;
            return (density, viscosity);
        }

        (float density, float viscoity) = TrilinearBlend(centerGS);
        if(viscoity > IsoValue) OnInSolid?.Invoke(viscoity);
        else if(density - viscoity > IsoValue) OnInLiquid?.Invoke(density - viscoity);
        else OnInGas?.Invoke(density);
        
        //int3 coordGS = (int3)math.round(centerGS);
        //int material = SampleMap(coordGS).material;
    }
}
