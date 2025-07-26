using System;
using UnityEngine;
using Unity.Mathematics;
using MapStorage;
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
    private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
    private static Catalogue<Authoring> itemInfo => Config.CURRENT.Generation.Items;
    public static Interaction settings => Config.CURRENT.GamePlay.Player.value.Interaction;
    private static PlayerStreamer.Player data => PlayerHandler.data;



    // Start is called before the first frame update
    public static void Initialize() {
        InputPoller.AddBinding(new InputPoller.ActionBind("Place", PlaceTerrain), "5.0::GamePlay");
        InputPoller.AddBinding(new InputPoller.ActionBind("Remove", RemoveTerrain), "5.0::GamePlay");
    }

    public static bool RayTestSolid(PlayerStreamer.Player data, out float3 hitPt){
        static uint RayTestSolid(int3 coord){ 
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity; 
        } 
        return CPUMapManager.RayCastTerrain(data.position, PlayerHandler.camera.forward, settings.ReachDistance, RayTestSolid, out hitPt);
    }

    public static bool RayTestLiquid(PlayerStreamer.Player data, out float3 hitPt){
        static uint RayTestLiquid(int3 coord){ 
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        return CPUMapManager.RayCastTerrain(data.position, PlayerHandler.camera.forward, settings.ReachDistance, RayTestLiquid, out hitPt);
    }

    private static void PlaceTerrain(float _){
        if (!PlayerHandler.active) return;
        bool rayHit = false;
        float3 hitPt = data.position + (float3)PlayerHandler.camera.forward * settings.ReachDistance;
        if (RayTestSolid(data, out float3 terrHit)) {
            hitPt = terrHit;
            rayHit = true;
        };
        
        if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var entity)){
            EntityInteract(entity);
            return;
        }

        if (!rayHit) return;
        if (InventoryController.Selected == null) return;
        Authoring selMat = InventoryController.SelectedSetting;
        if(selMat.MaterialName == null || !matInfo.Contains(selMat.MaterialName)) return;
        if (!selMat.IsSolid) return;

        PlayerHandler.data.animator.SetTrigger("IsPlacing");
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, (GCoord, speed) => HandleAddSolid(
            InventoryController.Selected,
            GCoord,
            speed * settings.DefaultTerraform.value.TerraformSpeed,
            out MapData _
        ), CallOnMapPlacing);
    }

    private static void RemoveTerrain(float _)
    {   
        if (!PlayerHandler.active) return;
        if (!RayTestSolid(data, out float3 hitPt)) return;
        if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.position, hitPt, PlayerHandler.data.info.entityId, out var entity)){
            return;
        }

        PlayerHandler.data.animator.SetTrigger("IsPlacing");
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius,
            RemoveSolidBareHand, CallOnMapRemoving);
    }
    
    /// <summary> Calls the default <see cref="MaterialData.OnPlacing"/> handle
    /// for the material being placed on behalf of the player and halts all terraforming 
    /// if an error occurs. </summary>
    /// <param name="GCoord">The coordinate in grid space of the material being placed</param>
    /// <returns>Whether or not to halt the current terraform process</returns>
    public static bool CallOnMapPlacing(int3 GCoord) {
        MapData map = CPUMapManager.SampleMap(GCoord);
        if (map.IsNull) return true;
        return matInfo.Retrieve(map.material).OnPlacing(GCoord, data);
    }

    /// <summary> Calls the default <see cref="MaterialData.OnRemoving"/> handle
    /// for the material being removed on behalf of the player and halts all terraforming 
    /// if an error occurs. </summary>
    /// <param name="GCoord">The coordinate in grid space of the material being removed</param>
    /// <returns>Whether or not to halt the current terraform process</returns>
    public static bool CallOnMapRemoving(int3 GCoord) {
        MapData map = CPUMapManager.SampleMap(GCoord);
        if (map.IsNull) return true;
        return matInfo.Retrieve(map.material).OnRemoving(GCoord, data);
    }

    public static int GetStaggeredDelta(float deltaDensity) {
        int remInvFreq = Mathf.CeilToInt(1 / math.frac(deltaDensity));
        int staggeredDelta = Mathf.FloorToInt(deltaDensity);
        return staggeredDelta + ((Time.frameCount % remInvFreq) == 0 ? 1 : 0);
    }

    public static int GetStaggeredDelta(int baseDensity, float deltaDensity, int maxDensity = 255) {
        int staggeredDelta = (deltaDensity > 0 ? 1 : -1) * GetStaggeredDelta(math.abs(deltaDensity));
        return Mathf.Abs(Mathf.Clamp(baseDensity + staggeredDelta, 0, maxDensity) - baseDensity);
    }

    public static bool HandleAddSolid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo){
        pointInfo = CPUMapManager.SampleMap(GCoord);
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return false;

        if (matItem == null)
            return false;
        Authoring setting = itemInfo.Retrieve(matItem.Index);
        if (!setting.IsSolid)
            return false;
        if (!matInfo.Contains(setting.MaterialName))
            return false;

        int selected = matInfo.RetrieveIndex(setting.MaterialName);
        int solidDensity = pointInfo.SolidDensity;
        if (solidDensity >= CPUMapManager.IsoValue && pointInfo.material != selected)
            return false;

        //If adding solid density, override water
        MapData delta = pointInfo;
        delta.density = GetStaggeredDelta(solidDensity, brushStrength);
        delta.density = InventoryController.RemoveStackable(delta.density, matItem.Index);
        delta.viscosity = delta.density;

        solidDensity += delta.density;
        delta.density = math.min(pointInfo.density + delta.density, 255) - pointInfo.density;
        delta.viscosity = math.min(pointInfo.viscosity + delta.viscosity, 255) - pointInfo.viscosity;
        pointInfo.density += delta.density;
        pointInfo.viscosity += delta.viscosity;
        if(solidDensity >= CPUMapManager.IsoValue)
            pointInfo.material = selected;
        matInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    public static bool HandleAddLiquid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo){
        pointInfo = CPUMapManager.SampleMap(GCoord);
        brushStrength *= Time.deltaTime;
        if(brushStrength == 0) return false;
        
        if (matItem == null)
            return false;
        Authoring setting = itemInfo.Retrieve(matItem.Index);
        if (!setting.IsLiquid)
            return false;
        if (!matInfo.Contains(setting.MaterialName))
            return false;

        int selected = matInfo.RetrieveIndex(setting.MaterialName);
        int liquidDensity = pointInfo.LiquidDensity;
        if (liquidDensity >= CPUMapManager.IsoValue && pointInfo.material != selected)
            return false;

        //If adding liquid density, only change if not solid
        MapData delta = pointInfo;
        delta.viscosity = 0;
        delta.density = GetStaggeredDelta(pointInfo.density, brushStrength);
        delta.density = InventoryController.RemoveStackable(delta.density, matItem.Index);

        pointInfo.density += delta.density;
        liquidDensity += delta.density;
        if(liquidDensity >= CPUMapManager.IsoValue) pointInfo.material = selected;
        matInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }
    
    private static bool RemoveSolidBareHand(int3 GCoord, float speed){
        MapData mapData = CPUMapManager.SampleMap(GCoord);
        int material = mapData.material; ToolTag tag = settings.DefaultTerraform;
        if (matInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, material, out object prop))
            tag = prop as ToolTag;
        return HandleRemoveSolid(ref mapData, GCoord, speed * tag.TerraformSpeed, tag.GivesItem);
    }

    public static bool HandleRemoveSolid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool ObtainMat = true) {
        brushStrength *= Time.deltaTime;
        if (brushStrength == 0) return false;

        MapData delta = pointInfo;
        int solidDensity = pointInfo.SolidDensity;
        delta.density = GetStaggeredDelta(solidDensity, -brushStrength);
        delta.viscosity = delta.density;
        
        pointInfo.viscosity -= delta.viscosity;
        pointInfo.density -= delta.density;
        IItem matItem = matInfo.Retrieve(pointInfo.material).OnRemoved(GCoord, delta);
        if (solidDensity >= CPUMapManager.IsoValue && ObtainMat) {
            if (matItem == null) return false;
            InventoryController.AddEntry(matItem);
            if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
        }
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    public static bool HandleRemoveLiquid(int3 GCoord, float brushStrength){
        brushStrength *= Time.deltaTime;
        if (brushStrength == 0) return false;

        MapData pointInfo = CPUMapManager.SampleMap(GCoord);
        MapData delta = pointInfo;
        int liquidDensity = pointInfo.LiquidDensity;
        delta.density = GetStaggeredDelta(liquidDensity, -brushStrength);
        delta.viscosity = 0;
        
        pointInfo.density -= delta.density;
        IItem matItem = matInfo.Retrieve(pointInfo.material).OnRemoved(GCoord, delta);
        if (liquidDensity >= CPUMapManager.IsoValue) {
            if (matItem == null) return false;
            InventoryController.AddEntry(matItem);
            if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
        }
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    private static void EntityInteract(WorldConfig.Generation.Entity.Entity target){
        if(!target.active) return;
        if(target is not IAttackable) return;
        IAttackable targEnt = target as IAttackable;
        if (!targEnt.IsDead) targEnt.Interact(data);
        else {
            IItem slot = targEnt.Collect(settings.PickupRate);
            InventoryController.AddEntry(slot);   
        }
    }
}
