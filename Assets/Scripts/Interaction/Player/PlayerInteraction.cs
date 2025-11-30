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

public static class PlayerInteraction {
    private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
    private static Catalogue<Authoring> itemInfo => Config.CURRENT.Generation.Items;
    public static Interaction settings => Config.CURRENT.GamePlay.Player.value.Interaction;
    private static PlayerStreamer.Player data => PlayerHandler.data;



    // Start is called before the first frame update
    public static void Initialize() {
        InputPoller.AddBinding(new ActionBind("Place", PlaceTerrain), "PlayerInteraction::PL", "5.0::GamePlay");
        InputPoller.AddBinding(new ActionBind("Remove", RemoveTerrain), "PlayerInteraction::RM", "5.0::GamePlay");
    }

    public static bool RayTestSolid<T>(T entity, out float3 hitPt) where T : WorldConfig.Generation.Entity.Entity, IAttackable {
        static uint RayTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }
        return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, settings.ReachDistance, RayTestSolid, out hitPt);
    }

    public static bool CylinderTestSolid<T>(T entity, out float3 hitPt) where T : WorldConfig.Generation.Entity.Entity, IAttackable {
        static uint CylinderTestSolid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)pointInfo.viscosity;
        }
        const float radius = 1;
        if (RayTestSolid(entity, out hitPt) && math.lengthsq(hitPt - entity.head) < radius * radius * 4)
            return true;
        return CPUMapManager.CylinderCastTerrain(entity.head + 2 * radius * entity.Forward,
            entity.Forward, radius, settings.ReachDistance, CylinderTestSolid, out hitPt);
    }

    public static bool RayTestLiquid<T>(T entity, out float3 hitPt) where T : WorldConfig.Generation.Entity.Entity, IAttackable {
        static uint RayTestLiquid(int3 coord) {
            MapData pointInfo = CPUMapManager.SampleMap(coord);
            return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
        }
        return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, settings.ReachDistance, RayTestLiquid, out hitPt);
    }

    private static void PlaceTerrain(float _) {
        if (!PlayerHandler.active) return;
        bool rayHit = false;
        float3 hitPt = data.head + PlayerHandler.data.Forward * settings.ReachDistance;
        if (RayTestSolid(data, out float3 terrHit)) {
            hitPt = terrHit;
            rayHit = true;
        };

        if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.head, hitPt, PlayerHandler.data.info.entityId, out var entity)) {
            EntityInteract(entity);
            return;
        }

        if (!rayHit) return;
        if (InventoryController.Selected == null) return;
        Authoring selMat = InventoryController.SelectedSetting;
        if (selMat is not PlaceableItem setting) return;
        if (!setting.IsSolid || !matInfo.Contains(setting.MaterialName)) return;

        PlayerHandler.data.Play("PlaceTerrain");
        CPUMapManager.Terraform(hitPt, settings.TerraformRadius, (GCoord, speed) => HandleAddSolid(
            InventoryController.Selected,
            GCoord,
            speed * settings.DefaultTerraform.value.TerraformSpeed,
            out MapData _
        ), CallOnMapPlacing);
    }

    private static void RemoveTerrain(float _) {
        if (!PlayerHandler.active) return;
        if (!CylinderTestSolid(data, out float3 hitPt)) return;
        if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.head, hitPt, PlayerHandler.data.info.entityId, out var entity)) {
            return;
        }

        PlayerHandler.data.Play("RemoveTerrain");
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

    private static bool CanPlaceSolid(IItem selected, MapData pointInfo, out int matIndex) {
        matIndex = 0;
        if (selected == null)
            return false;
        Authoring setting = itemInfo.Retrieve(selected.Index);
        if (setting is not PlaceableItem mSettings)
            return false;
        if (!mSettings.IsSolid || !matInfo.Contains(mSettings.MaterialName))
            return false;
        int solidDensity = pointInfo.SolidDensity;
        matIndex = matInfo.RetrieveIndex(mSettings.MaterialName);
        if (solidDensity >= CPUMapManager.IsoValue &&
            pointInfo.material != matIndex)
            return false;
        if (solidDensity == MapData.MaxViscosity)
            return false;
        return true;
    }

    private static bool CanPlaceLiquid(IItem selected, MapData pointInfo, out int matIndex) {
        matIndex = 0;
        if (selected == null)
            return false;
        Authoring setting = itemInfo.Retrieve(selected.Index);
        if (setting is not PlaceableItem mSettings)
            return false;
        if (!mSettings.IsLiquid || !matInfo.Contains(mSettings.MaterialName))
            return false;
        int liquidDensity = pointInfo.LiquidDensity;
        matIndex = matInfo.RetrieveIndex(mSettings.MaterialName);
        if (liquidDensity >= CPUMapManager.IsoValue &&
            pointInfo.material != matIndex)
            return false;
        if (liquidDensity == MapData.MaxDensity)
            return false;
        return true;
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

    public static bool HandleAddSolid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
        pointInfo = CPUMapManager.SampleMap(GCoord);
        brushStrength *= Time.deltaTime;
        if (brushStrength == 0) return false;
        if (!CanPlaceSolid(matItem, pointInfo, out int selected)) return false;

        int solidDensity = pointInfo.SolidDensity;
        //If adding solid density, override water
        int solidDelta = GetStaggeredDelta(solidDensity, brushStrength);
        solidDelta = InventoryController.RemoveStackable(solidDelta, matItem.Index);
        solidDelta = math.min(pointInfo.viscosity + solidDelta, 255) - pointInfo.viscosity;

        //Remove previous liquid if there's any
        MapData delta = pointInfo;
        delta.viscosity = 0;
        delta.density = solidDelta + pointInfo.density
            - math.min(pointInfo.density + solidDelta, 255);

        MaterialData authoring;
        if (delta.LiquidDensity != 0) {
            authoring = matInfo.Retrieve(pointInfo.material);
            if (authoring.OnRemoving(GCoord, PlayerHandler.data))
                return false;
            //Don't collect liquid this way--discard it
            authoring.OnRemoved(GCoord, delta);
            //Remove the liquid density to place the new solid density
            pointInfo.density -= delta.density;
        }

        delta.viscosity = solidDelta;
        delta.density = solidDelta;
        //Note: Add density before viscosity
        //since viscosity can push density causing overflow
        pointInfo.density += delta.density;
        pointInfo.viscosity += delta.viscosity;

        if (pointInfo.viscosity >= CPUMapManager.IsoValue)
            pointInfo.material = selected;
        matInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    public static bool HandleAddLiquid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
        pointInfo = CPUMapManager.SampleMap(GCoord);
        brushStrength *= Time.deltaTime;
        if (brushStrength == 0) return false;
        if (!CanPlaceLiquid(matItem, pointInfo, out int selected))
            return false;

        int liquidDensity = pointInfo.LiquidDensity;
        //If adding liquid density, only change if not solid
        MapData delta = pointInfo;
        delta.viscosity = 0;
        delta.density = GetStaggeredDelta(pointInfo.density, brushStrength);
        delta.density = InventoryController.RemoveStackable(delta.density, matItem.Index);

        pointInfo.density += delta.density;
        liquidDensity += delta.density;
        if (liquidDensity >= CPUMapManager.IsoValue) pointInfo.material = selected;
        matInfo.Retrieve(pointInfo.material).OnPlaced(GCoord, delta);
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    private static bool RemoveSolidBareHand(int3 GCoord, float speed) {
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

    public static bool HandleRemoveLiquid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool ObtainMat = true) {
        brushStrength *= Time.deltaTime;
        if (brushStrength == 0) return false;

        MapData delta = pointInfo;
        int liquidDensity = pointInfo.LiquidDensity;
        delta.density = GetStaggeredDelta(liquidDensity, -brushStrength);
        delta.viscosity = 0;

        pointInfo.density -= delta.density;
        IItem matItem = matInfo.Retrieve(pointInfo.material).OnRemoved(GCoord, delta);
        if (liquidDensity >= CPUMapManager.IsoValue && ObtainMat) {
            if (matItem == null) return false;
            InventoryController.AddEntry(matItem);
            if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
        }
        CPUMapManager.SetMap(pointInfo, GCoord);
        return true;
    }

    private static void EntityInteract(WorldConfig.Generation.Entity.Entity target) {
        if (!target.active) return;
        if (target is not IAttackable) return;
        IAttackable targEnt = target as IAttackable;
        if (!targEnt.IsDead) targEnt.Interact(data);
        else {
            IItem slot = targEnt.Collect(settings.PickupRate);
            InventoryController.AddEntry(slot);
        }
    }

}
