using System;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Core.Storage;
using Arterra.Configuration;
using Arterra.Data.Material;
using Arterra.Data.Item;
using Arterra.GamePlay.UI;
using Arterra.Core.Events;
using Arterra.Utils;
using Arterra.GamePlay.Interaction;

namespace Arterra.Configuration.Gameplay.Player {
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
    /// <summary> The radius around the view vector which will be searched when ray casting to determine
    /// where the view ray intersects the ground.  </summary>
    public float CylinderRadius = 1;
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


namespace Arterra.GamePlay{
    /// <summary>The manager responsible for controlling all player interactions with the world </summary>
    public static class PlayerInteraction {
        private static Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private static Catalogue<Authoring> itemInfo => Config.CURRENT.Generation.Items;
        private static Arterra.Configuration.Gameplay.Player.Interaction settings => Config.CURRENT.GamePlay.Player.value.Interaction;
        private static PlayerStreamer.Player data => PlayerHandler.data;

        /// <summary> Initializes the player's interactions by tying
        /// keybinds into the system. </summary>
        public static void Initialize() {
            InputPoller.AddBinding(new ActionBind("Place", PlaceTerrain), "PlayerInteraction::PL", "5.0::GamePlay");
            InputPoller.AddBinding(new ActionBind("Remove", RemoveTerrain), "PlayerInteraction::RM", "5.0::GamePlay");
        }

        /// <summary> Determines where the player's view vector intersects with the solid terrain if 
        /// under <see cref="Interaction.ReachDistance"/>  away. See <see cref="MinimalRecognition.RayTestSolid"/>
        /// for mode info. </summary>
        /// <param name="hitPt">If it does intersect, the first point it intersects in grid space</param>
        /// <returns>Whether or not the view ray intersects solid terrain</returns>
        public static bool RayTestSolid(out float3 hitPt) => MinimalRecognition.RayTestSolid(PlayerHandler.data, settings.ReachDistance, out hitPt);

        /// <summary> Determines where a cylinder of radius <see cref="Interaction.CylinderRadius"/> around the player's view vector
        /// intersects with the solid terrain if under <see cref="Interaction.ReachDistance"/> away. See <see cref="MinimalRecognition.CylinderTestSolid"/>
        /// for mode info. </summary>
        /// <param name="hitPt">If it does intersect, the first point it intersects in grid space</param>
        /// <returns>Whether or not the view ray intersects solid terrain</returns>
        public static bool CylinderTestSolid(out float3 hitPt) => MinimalRecognition.CylinderTestSolid(PlayerHandler.data, settings.ReachDistance, settings.CylinderRadius, out hitPt);
        /// <summary> Determines where the player's view vector intersects with the liquid/solid terrain if 
        /// under <see cref="Interaction.ReachDistance"/>  away. See <see cref="MinimalRecognition.RayTestLiquid"/>
        /// for mode info. </summary>
        /// <param name="hitPt">If it does intersect, the first point it intersects in grid space</param>
        /// <returns>Whether or not the view ray intersects liquid/solid terrain</returns>
        public static bool RayTestLiquid(out float3 hitPt) => MinimalRecognition.RayTestLiquid(PlayerHandler.data, settings.ReachDistance, out hitPt);

        private static bool RemoveSolidBareHand(int3 GCoord, float speed) {
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            int material = mapData.material; ToolTag tag = settings.DefaultTerraform;
            if (matInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, material, out object prop))
                tag = prop as ToolTag;
            return HandleRemoveSolid(ref mapData, GCoord, speed * tag.TerraformSpeed, tag.GivesItem);
        }

        private static void PlaceTerrain(float _) {
            if (!PlayerHandler.active) return;
            bool rayHit = false;
            float3 hitPt = data.head + PlayerHandler.data.Forward * settings.ReachDistance;
            if (RayTestSolid(out float3 terrHit)) {
                hitPt = terrHit;
                rayHit = true;
            };

            if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.head, hitPt, PlayerHandler.data.info.entityId, out var entity, out _)) {
                EntityInteract(entity);
                return;
            }

            if (!rayHit) return;
            if (InventoryController.Selected == null) return;
            Authoring selMat = InventoryController.SelectedSetting;
            if (selMat is not PlaceableItem setting) return;
            if (!setting.IsSolid || !matInfo.Contains(setting.MaterialName)) return;

            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_PlaceTerrain, PlayerHandler.data, null, hitPt);
            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, (GCoord, speed) => HandleAddSolid(
                InventoryController.Selected,
                GCoord,
                speed * settings.DefaultTerraform.value.TerraformSpeed,
                out MapData _
            ), CallOnMapPlacing);
        }

        private static void RemoveTerrain(float _) {
            if (!PlayerHandler.active) return;
            if (!CylinderTestSolid(out float3 hitPt)) return;
            if (EntityManager.ESTree.FindClosestAlongRay(PlayerHandler.data.head, hitPt, PlayerHandler.data.info.entityId, out var entity, out _)) {
                return;
            }

            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Action_RemoveTerrain, PlayerHandler.data, null, hitPt);
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

        /// <summary> Adds the solid material translation of an item to a specific position in the map
        /// if the item has a solid material translation, and that solid material can be placed 
        /// at the position conservatively.  </summary>
        /// <remarks> This method resolves all material handles relevant to this operation</remarks>
        /// <param name="matItem">The item which has a solid material translation</param>
        /// <param name="GCoord">The coordinate in grid space to place the material</param>
        /// <param name="brushStrength">The speed at which material will be placed</param>
        /// <param name="pointInfo">The existing map information at this location <see cref="CPUMapManager.SampleMap(int3)"/> </param>
        /// <returns>Whether or not <b>any</b>(not necessarily all) new material was successfully placed here as a result of this operation.</returns>
        public static bool HandleAddSolid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
            pointInfo = CPUMapManager.SampleMap(GCoord);
            brushStrength *= Time.deltaTime;
            if (brushStrength == 0) return false;
            if (!CanPlaceSolid(matItem, pointInfo, out int selected)) return false;

            int solidDensity = pointInfo.SolidDensity;
            //If adding solid density, override water
            int solidDelta = CustomUtility.GetStaggeredDelta(solidDensity, brushStrength);
            solidDelta = InventoryController.RemoveStackable(solidDelta, matItem.Index);
            solidDelta = math.min(pointInfo.viscosity + solidDelta, 255) - pointInfo.viscosity;

            //Remove previous liquid if there's any
            MapData delta = pointInfo;
            delta.viscosity = 0;
            delta.density = solidDelta + pointInfo.density
                - math.min(pointInfo.density + solidDelta, 255);

            MaterialInstance authoring = new (GCoord, pointInfo.material);
            if (delta.LiquidDensity != 0) {
                if (authoring.Authoring.OnRemoving(GCoord, PlayerHandler.data))
                    return false;
                //Don't collect liquid this way--discard it
                authoring.Authoring.OnRemoved(GCoord, delta);
                //Remove the liquid density to place the new solid density
                pointInfo.density -= delta.density;
            }

            delta.viscosity = solidDelta;
            delta.density = solidDelta;
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Entity_PlaceMaterial, PlayerHandler.data, authoring, delta);

            //Note: Add density before viscosity
            //since viscosity can push density causing overflow
            pointInfo.density += delta.density;
            pointInfo.viscosity += delta.viscosity;

            if (pointInfo.viscosity >= CPUMapManager.IsoValue)
                pointInfo.material = selected;
            authoring.Authoring.OnPlaced(GCoord, delta);
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        /// <summary> Adds the liquid material translation of an item to a specific position in the map
        /// if the item has a liquid material translation, and that liquid material can be placed 
        /// at the position conservatively.  </summary>
        /// <remarks> This method resolves all material handles relevant to this operation</remarks>
        /// <param name="matItem">The item which has a liquid material translation</param>
        /// <param name="GCoord">The coordinate in grid space to place the material</param>
        /// <param name="brushStrength">The speed at which material will be placed</param>
        /// <param name="pointInfo">The existing map information at this location <see cref="CPUMapManager.SampleMap(int3)"/> </param>
        /// <returns>Whether or not <b>any</b>(not necessarily all) new material was successfully placed here as a result of this operation.</returns>
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
            delta.density = CustomUtility.GetStaggeredDelta(pointInfo.density, brushStrength);
            delta.density = InventoryController.RemoveStackable(delta.density, matItem.Index);

            MaterialInstance authoring = new (GCoord, pointInfo.material);
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Entity_PlaceMaterial, PlayerHandler.data, authoring, delta);

            pointInfo.density += delta.density;
            liquidDensity += delta.density;
            if (liquidDensity >= CPUMapManager.IsoValue) pointInfo.material = selected;
            authoring.Authoring.OnPlaced(GCoord, delta);
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        /// <summary>Removes a specific amount of solid material from a certain position in the map. </summary>
        /// <param name="pointInfo">The existing map information at this location <see cref="CPUMapManager.SampleMap(int3)"/> </param>
        /// <param name="GCoord">The coordinate in grid space to remove materia</param>
        /// <param name="brushStrength">The speed at which material will be removed</param>
        /// <param name="ObtainMat">Whether or not the item translation of the solid material will attempt to be 
        /// added to the Player's Inventory. </param>
        /// <returns>Whether or not <b>any</b>(Not necessarily all) material was removed.</returns>
        public static bool HandleRemoveSolid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool ObtainMat = true) {
            brushStrength *= Time.deltaTime;
            if (brushStrength == 0) return false;

            MapData delta = pointInfo;
            int solidDensity = pointInfo.SolidDensity;
            delta.density = CustomUtility.GetStaggeredDelta(solidDensity, -brushStrength);
            delta.viscosity = delta.density;
            MaterialInstance authoring = new (GCoord, pointInfo.material);
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Entity_RemoveMaterial, PlayerHandler.data, authoring, delta);

            pointInfo.viscosity -= delta.viscosity;
            pointInfo.density -= delta.density;
            IItem matItem = authoring.Authoring.OnRemoved(GCoord, delta);
            if (solidDensity >= CPUMapManager.IsoValue && ObtainMat) {
                if (matItem == null) return false;
                InventoryController.AddEntry(matItem);
                if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
            }
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        /// <summary>Removes a specific amount of liquid material from a certain position in the map. </summary>
        /// <param name="pointInfo">The existing map information at this location <see cref="CPUMapManager.SampleMap(int3)"/> </param>
        /// <param name="GCoord">The coordinate in grid space to remove materia</param>
        /// <param name="brushStrength">The speed at which material will be removed</param>
        /// <param name="ObtainMat">Whether or not the item translation of the liquid material will attempt to be 
        /// added to the Player's Inventory. </param>
        /// <returns>Whether or not <b>any</b>(Not necessarily all) material was removed.</returns>
        public static bool HandleRemoveLiquid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool ObtainMat = true) {
            brushStrength *= Time.deltaTime;
            if (brushStrength == 0) return false;

            MapData delta = pointInfo;
            int liquidDensity = pointInfo.LiquidDensity;
            delta.density = CustomUtility.GetStaggeredDelta(liquidDensity, -brushStrength);
            delta.viscosity = 0;

            MaterialInstance authoring = new (GCoord, pointInfo.material);
            PlayerHandler.data.eventCtrl.RaiseEvent(GameEvent.Entity_RemoveMaterial, PlayerHandler.data, authoring, delta);

            pointInfo.density -= delta.density;
            IItem matItem = authoring.Authoring.OnRemoved(GCoord, delta);
            if (liquidDensity >= CPUMapManager.IsoValue && ObtainMat) {
                if (matItem == null) return false;
                InventoryController.AddEntry(matItem);
                if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
            }
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        private static void EntityInteract(Arterra.Data.Entity.Entity target) {
            if (!target.active) return;
            if (target is not IAttackable) return;
            IAttackable targEnt = target as IAttackable;
            if (!targEnt.IsDead) targEnt.Interact(data);
            else {
                IItem slot = targEnt.Collect(PlayerHandler.data, settings.PickupRate);
                InventoryController.AddEntry(slot);
            }
        }
    }
}