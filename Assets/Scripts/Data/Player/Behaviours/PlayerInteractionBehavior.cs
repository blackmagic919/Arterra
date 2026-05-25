using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Core.Storage;
using Arterra.Data.Entity;
using ItemAuthoring = Arterra.Data.Item.Authoring;
using Arterra.Data.Material;
using Arterra.Data.Item;
using Arterra.GamePlay;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// Settings governing the player's interaction with entities and terrain,
    /// including terraforming and melee interactions.
    /// </summary>
    [Serializable]
    public class PlayerInteractionSettings : IBehaviorSetting {
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Interaction";
        /// <summary>
        /// Radius, in grid space, of the spherical region modified during terrain edits.
        /// </summary>
        public int TerraformRadius = 1;
        /// <summary>
        /// Default terraform behavior used when no material-specific tag overrides it.
        /// </summary>
        public Option<ToolTag> DefaultTerraform;
        /// <summary>
        /// Maximum distance (in grid space) for interaction ray checks.
        /// </summary>
        public float ReachDistance = 8f;
        /// <summary>
        /// Radius used by cylinder casts around the view ray when removing terrain.
        /// </summary>
        public float CylinderRadius = 1f;
        /// <summary>
        /// Collection speed when looting dead entities.
        /// </summary>
        public float PickupRate = 1.5f;
        public float AttackDamage = 2f;
        public float AttackFrequency = 0.25f;
        public float KnockBackStrength = 10f;

        public object Clone() {
            return new PlayerInteractionSettings {
                TerraformRadius = TerraformRadius,
                DefaultTerraform = DefaultTerraform,
                ReachDistance = ReachDistance,
                CylinderRadius = CylinderRadius,
                PickupRate = PickupRate,
                AttackDamage = AttackDamage,
                AttackFrequency = AttackFrequency,
                KnockBackStrength = KnockBackStrength,
            };
        }

        public static PlayerInteractionSettings GetSingleton() {
            if (!Config.CURRENT.GamePlay.PlayerSettings.value.Is(out PlayerInteractionSettings interaction))
                throw new System.Exception("Expected player to have terraform settings");
            return interaction;
        }
    }

    /// <summary>
    /// Controls player interaction with world entities and map materials.
    /// </summary>
    public class PlayerInteractionBehavior : ISpeciesBehavior {
        [JsonIgnore] public PlayerInteractionSettings settings;

        private bool hasBindings;
        private float attackCooldown;
        private BehaviorEntity.Animal self;
        private VitalityBehavior vit;

        private Catalogue<MaterialData> matInfo => Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        private Catalogue<ItemAuthoring> itemInfo => Config.CURRENT.Generation.Items;
        private bool IsActive => (!vit?.IsDead) ?? true;

        [JsonIgnore]
        /// <summary>Current cooldown before another attack can be issued.</summary>
        public float AttackCooldown {
            get => attackCooldown;
            set => attackCooldown = math.max(value, 0);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(PlayerInteractionSettings), new PlayerInteractionSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerInteractionBehavior requires PlayerInteractionSettings");
            if (!self.Is(out vit)) vit = null;
            self.Register(this);
            attackCooldown = 0;
            this.self = self;

            if (!IsActive) return;
            BindInput();
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: PlayerInteractionBehavior requires PlayerInteractionSettings");
            if (!self.Is(out vit)) vit = null;
            self.Register(this);
            this.self = self;

            if (!IsActive) return;
            BindInput();
        }

        public void Disable(BehaviorEntity.Animal self) {
            UnbindInput();
            this.self = null;
        }

        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Fixed)
                return;
            attackCooldown = math.max(attackCooldown - self.DeltaTime, 0);
        }

        public bool RayTestSolid<T>(T entity, float reach, out float3 hitPt) where T : Entity {
            static uint RaySolid(int3 coord) {
                MapData pointInfo = CPUMapManager.SampleMap(coord);
                return (uint)pointInfo.viscosity;
            }

            return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, reach, RaySolid, out hitPt);
        }

        public bool RayTestLiquid<T>(T entity, float reach, out float3 hitPt) where T : Entity {
            static uint RayLiquid(int3 coord) {
                MapData pointInfo = CPUMapManager.SampleMap(coord);
                return (uint)Mathf.Max(pointInfo.viscosity, pointInfo.density - pointInfo.viscosity);
            }
            return CPUMapManager.RayCastTerrain(entity.head, entity.Forward, reach, RayLiquid, out hitPt);
        }

        public bool CylinderTestSolid<T>(T entity, float reach, float radius, out float3 hitPt) where T : Entity {
            static uint CylinderSolid(int3 coord) {
                MapData pointInfo = CPUMapManager.SampleMap(coord);
                return (uint)pointInfo.viscosity;
            }

            if (RayTestSolid(entity, reach, out hitPt) && math.lengthsq(hitPt - entity.head) < radius * radius * 4)
                return true;
            return CPUMapManager.CylinderCastTerrain(entity.head + 2 * radius * entity.Forward,
                entity.Forward, radius, reach, CylinderSolid, out hitPt);
        }

        // Static facades keep existing item/UI callers decoupled from the legacy PlayerInteraction class.
        public bool RayTestSolid(out float3 hitPt) {
            float reach = settings.ReachDistance;
            return RayTestSolid(self, reach, out hitPt);
        }

        public bool RayTestLiquid(out float3 hitPt) {
            float reach = settings.ReachDistance;
            return RayTestLiquid(self, reach, out hitPt);
        }

        public bool CylinderTestSolid(out float3 hitPt) {
            float reach = settings.ReachDistance;
            float radius = settings.CylinderRadius;
            return CylinderTestSolid(self, reach, radius, out hitPt);
        }

        private bool RayTestSolidCurrent(out float3 hitPt) => RayTestSolid(self, settings.ReachDistance, out hitPt);
        private bool CylinderTestSolidCurrent(out float3 hitPt) => CylinderTestSolid(self, settings.ReachDistance, settings.CylinderRadius, out hitPt);

        private string AttackEntityName => $"PlayerInteraction:ATK::{self.info.entityId}"; 
        private string PlaceTerrainName => $"PlayerInteraction:PL::{self.info.entityId}"; 
        private string RemoveTerrainName => $"PlayerInteraction:RM::{self.info.entityId}"; 
        private void BindInput() {
            if (hasBindings) return;
            hasBindings = true;

            InputPoller.AddBinding(new ActionBind("Attack", AttackEntity), AttackEntityName, "5.0::GamePlay");
            InputPoller.AddBinding(new ActionBind("Place", PlaceTerrain), PlaceTerrainName, "5.0::GamePlay");
            InputPoller.AddBinding(new ActionBind("Remove", RemoveTerrain), RemoveTerrainName, "5.0::GamePlay");
        }

        private void UnbindInput() {
            if (!hasBindings) return;
            hasBindings = false;

            InputPoller.RemoveBinding(AttackEntityName, "5.0::GamePlay");
            InputPoller.RemoveBinding(PlaceTerrainName, "5.0::GamePlay");
            InputPoller.RemoveBinding(RemoveTerrainName, "5.0::GamePlay");
        }

        private void AttackEntity(float _) {
            if (!IsActive) return;
            if (self == null || !self.active) return;
            if (attackCooldown > 0) return;
            attackCooldown = settings.AttackFrequency;

            float3 hitPt = self.head + self.Forward * settings.ReachDistance;
            if (RayTestSolid(self, settings.ReachDistance, out float3 terrHit)) {
                hitPt = terrHit;
            }

            if (!EntityManager.ESTree.FindClosestAlongRay(self.head, hitPt, self.info.rtEntityId, out Entity entity, out _)) {
                return;
            }

            if (!entity.Is(out IAttackable attackable)) return;
            float3 knockback = math.normalize(entity.position - self.head) * settings.KnockBackStrength;
            float damage = settings.AttackDamage;

            RefTuple<(float, float3)> cxt = (damage, knockback);
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_Attack, self, entity, cxt);
            (damage, knockback) = cxt.Value;

            EntityManager.AddHandlerEvent(() => attackable.TakeDamage(damage, knockback, self));
        }

        private bool RemoveSolidBareHand(int3 GCoord, float speed) {
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            int material = mapData.material;
            ToolTag tag = settings.DefaultTerraform;
            if (matInfo.GetMostSpecificTag(TagRegistry.Tags.BareHand, material, out object prop))
                tag = prop as ToolTag;
            return HandleRemoveSolid(ref mapData, GCoord, speed * tag.TerraformSpeed, tag.GivesItem);
        }

        private void PlaceTerrain(float _) {
            if (!IsActive) return;
            if (!PlayerHandler.active) return;

            bool rayHit = false;
            float3 hitPt = self.head + self.Forward * settings.ReachDistance;
            if (RayTestSolidCurrent(out float3 terrHit)) {
                hitPt = terrHit;
                rayHit = true;
            }

            if (EntityManager.ESTree.FindClosestAlongRay(self.head, hitPt, self.info.rtEntityId, out var entity, out _)) {
                EntityInteract(entity, InventoryController.Selected);
                InventoryController.TryClearSelected();
                return;
            }

            if (!rayHit) return;
            if (InventoryController.Selected == null) return;
            ItemAuthoring selMat = InventoryController.SelectedSetting;
            if (selMat is not PlaceableItem setting) return;
            if (!setting.IsSolid || !matInfo.Contains(setting.MaterialName)) return;

            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Action_PlaceTerrain, self, null, hitPt);
            CPUMapManager.Terraform(hitPt, settings.TerraformRadius,
                (GCoord, speed) => HandleAddSolidInternal(
                    InventoryController.Selected,
                    GCoord,
                    speed * settings.DefaultTerraform.value.TerraformSpeed,
                    out MapData _
                ),
                CallOnMapPlacing
            );
        }

        private void RemoveTerrain(float _) {
            if (!IsActive) return;
            if (!PlayerHandler.active) return;
            if (!CylinderTestSolidCurrent(out float3 hitPt)) return;

            if (EntityManager.ESTree.FindClosestAlongRay(self.head, hitPt, self.info.rtEntityId, out var entity, out _)) {
                return;
            }

            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Action_RemoveTerrain, self, null, hitPt);
            CPUMapManager.Terraform(hitPt, settings.TerraformRadius, RemoveSolidBareHand, CallOnMapRemoving);
        }

        private bool CanPlaceSolid(IItem selected, MapData pointInfo, out int matIndex) {
            matIndex = 0;
            if (selected == null) return false;
            ItemAuthoring setting = itemInfo.Retrieve(selected.Index);
            if (setting is not PlaceableItem mSettings) return false;
            if (!mSettings.IsSolid || !matInfo.Contains(mSettings.MaterialName)) return false;

            int solidDensity = pointInfo.SolidDensity;
            matIndex = matInfo.RetrieveIndex(mSettings.MaterialName);
            if (solidDensity >= CPUMapManager.IsoValue && pointInfo.material != matIndex)
                return false;
            if (solidDensity == MapData.MaxViscosity)
                return false;
            return true;
        }

        private bool CanPlaceLiquid(IItem selected, MapData pointInfo, out int matIndex) {
            matIndex = 0;
            if (selected == null) return false;
            ItemAuthoring setting = itemInfo.Retrieve(selected.Index);
            if (setting is not PlaceableItem mSettings) return false;
            if (!mSettings.IsLiquid || !matInfo.Contains(mSettings.MaterialName)) return false;

            int liquidDensity = pointInfo.LiquidDensity;
            matIndex = matInfo.RetrieveIndex(mSettings.MaterialName);
            if (liquidDensity >= CPUMapManager.IsoValue && pointInfo.material != matIndex)
                return false;
            if (liquidDensity == MapData.MaxDensity)
                return false;
            return true;
        }

        public bool HandleAddSolidInternal(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
            pointInfo = CPUMapManager.SampleMap(GCoord);
            brushStrength *= self.DeltaTime;
            if (brushStrength == 0) return false;
            if (!CanPlaceSolid(matItem, pointInfo, out int selected)) return false;

            int solidDensity = pointInfo.SolidDensity;
            // If adding solid density, overflow into/replace existing liquid density.
            int solidDelta = CustomUtility.GetStaggeredDelta(solidDensity, brushStrength);
            solidDelta = InventoryController.RemoveStackable(solidDelta, matItem.Index);
            solidDelta = math.min(pointInfo.viscosity + solidDelta, 255) - pointInfo.viscosity;

            // Remove previous liquid contribution before applying new solid delta.
            MapData delta = pointInfo;
            delta.viscosity = 0;
            delta.density = solidDelta + pointInfo.density - math.min(pointInfo.density + solidDelta, 255);

            MaterialInstance authoring = new(GCoord, pointInfo.material);
            if (delta.LiquidDensity != 0) {
                if (authoring.Authoring.OnRemoving(GCoord, self))
                    return false;
                authoring.Authoring.OnRemoved(GCoord, delta);
                pointInfo.density -= delta.density;
            }

            delta.viscosity = solidDelta;
            delta.density = solidDelta;
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_PlaceMaterial, self, authoring, delta);

            // Add density before viscosity since viscosity can force density overflow.
            pointInfo.density += delta.density;
            pointInfo.viscosity += delta.viscosity;

            if (pointInfo.viscosity >= CPUMapManager.IsoValue)
                pointInfo.material = selected;
            authoring.Authoring.OnPlaced(GCoord, delta);
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        public bool HandleAddLiquidInternal(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
            pointInfo = CPUMapManager.SampleMap(GCoord);
            brushStrength *= self.DeltaTime;
            if (brushStrength == 0) return false;
            if (!CanPlaceLiquid(matItem, pointInfo, out int selected)) return false;

            int liquidDensity = pointInfo.LiquidDensity;
            // Liquid placement never writes viscosity; it only affects density.
            MapData delta = pointInfo;
            delta.viscosity = 0;
            delta.density = CustomUtility.GetStaggeredDelta(pointInfo.density, brushStrength);
            delta.density = InventoryController.RemoveStackable(delta.density, matItem.Index);

            MaterialInstance authoring = new(GCoord, pointInfo.material);
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_PlaceMaterial, self, authoring, delta);

            pointInfo.density += delta.density;
            liquidDensity += delta.density;
            if (liquidDensity >= CPUMapManager.IsoValue) pointInfo.material = selected;
            authoring.Authoring.OnPlaced(GCoord, delta);
            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        public bool HandleRemoveSolidInternal(ref MapData pointInfo, int3 GCoord, float brushStrength, bool obtainMat = true) {
            brushStrength *= self.DeltaTime;
            if (brushStrength == 0) return false;

            MapData delta = pointInfo;
            int solidDensity = pointInfo.SolidDensity;
            delta.density = CustomUtility.GetStaggeredDelta(solidDensity, -brushStrength);
            delta.viscosity = delta.density;
            MaterialInstance authoring = new(GCoord, pointInfo.material);
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_RemoveMaterial, self, authoring, delta);

            pointInfo.viscosity -= delta.viscosity;
            pointInfo.density -= delta.density;
            IItem matItem = authoring.Authoring.OnRemoved(GCoord, delta);
            if (solidDensity >= CPUMapManager.IsoValue && obtainMat) {
                if (matItem == null) return false;
                InventoryController.AddEntry(matItem);
                if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
            }

            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        public bool HandleRemoveLiquidInternal(ref MapData pointInfo, int3 GCoord, float brushStrength, bool obtainMat = true) {
            brushStrength *= self.DeltaTime;
            if (brushStrength == 0) return false;

            MapData delta = pointInfo;
            int liquidDensity = pointInfo.LiquidDensity;
            delta.density = CustomUtility.GetStaggeredDelta(liquidDensity, -brushStrength);
            delta.viscosity = 0;

            MaterialInstance authoring = new(GCoord, pointInfo.material);
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_RemoveMaterial, self, authoring, delta);

            pointInfo.density -= delta.density;
            IItem matItem = authoring.Authoring.OnRemoved(GCoord, delta);
            if (liquidDensity >= CPUMapManager.IsoValue && obtainMat) {
                if (matItem == null) return false;
                InventoryController.AddEntry(matItem);
                if (matItem.AmountRaw != 0) InventoryController.DropItem(matItem);
            }

            CPUMapManager.SetMap(pointInfo, GCoord);
            return true;
        }

        public bool CallOnMapPlacing(int3 GCoord) {
            MapData map = CPUMapManager.SampleMap(GCoord);
            if (map.IsNull) return true;
            Catalogue<MaterialData> mat = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            return mat.Retrieve(map.material).OnPlacing(GCoord, self);
        }

        public bool CallOnMapRemoving(int3 GCoord) {
            MapData map = CPUMapManager.SampleMap(GCoord);
            if (map.IsNull) return true;
            Catalogue<MaterialData> mat = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            return mat.Retrieve(map.material).OnRemoving(GCoord, self);
        }

        public bool HandleAddSolid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
            return HandleAddSolidInternal(matItem, GCoord, brushStrength, out pointInfo);
        }

        public bool HandleAddLiquid(IItem matItem, int3 GCoord, float brushStrength, out MapData pointInfo) {
            return HandleAddLiquidInternal(matItem, GCoord, brushStrength, out pointInfo);
        }

        public bool HandleRemoveSolid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool obtainMat = true) {
            return HandleRemoveSolidInternal(ref pointInfo, GCoord, brushStrength, obtainMat);
        }

        public bool HandleRemoveLiquid(ref MapData pointInfo, int3 GCoord, float brushStrength, bool obtainMat = true) {
            return HandleRemoveLiquidInternal(ref pointInfo, GCoord, brushStrength, obtainMat);
        }

        private void EntityInteract(Entity target, IItem held) {
            if (!target.active) return;
            if (!target.Is(out IAttackable targEnt)) return;

            if (!targEnt.IsDead) {
                targEnt.Interact(self, held);
            } else {
                targEnt.Collect(
                    self,
                    InventoryController.AddEntry, 
                    settings.PickupRate
                );
            }
        }
    }
}
