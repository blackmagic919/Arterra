using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.GamePlay.UI;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerBaseLogicHandler : SpeciesBehavior {
        private BehaviorEntity.Animal self;
        private InidcatorsBehavior indicator;
        private ColliderUpdateBehavior collider;
        private VitalityBehavior vitality;
        private PlayerInventoriesBehavior inventories;
        private TimedDecayBehavior decay;
        [JsonProperty] private bool respawned;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Vitality, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerInventories, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.TimedDecay, hierarchy.Count);
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) => Bind(self);
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) => Bind(self);

        private void Bind(BehaviorEntity.Animal self) {
            if (!self.Is(out decay))
                throw new Exception("Entity: PlayerBaseLogicHandler requires TimedDecayBehavior");
            if (!self.Is(out vitality))
                throw new Exception("Entity: PlayerBaseLogicHandler requires VitalityBehavior");
            if (!self.Is(out inventories))
                throw new Exception("Entity: PlayerBaseLogicHandler requires PlayerInventoriesBehavior");
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out indicator)) indicator = null;
            this.self = self;
            vitality.UseDefaultLoot = false;
            decay.IsDepleted = () => !inventories.HasItems;
            decay.CanRelease = () => respawned;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Respawn, OnRespawn);
            OnStartup();
        }

        private void OnRespawn(object source, object target, object context) => respawned = true;

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Death, OnDeath);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Respawn, OnRespawn);
            RemoveHandles();
            vitality.UseDefaultLoot = true;
            decay.IsDepleted = null;
            decay.CanRelease = null;
            this.self = null;
        }

        private readonly List<GameObject> ownerLayers = new();

        private void UpdateOwnerLayers() {
            int layer = LayerMask.NameToLayer(self.IsOwner && !vitality.IsDead ? "Self" : "Default");
            foreach (var obj in ownerLayers) if (obj != null) obj.layer = layer;
        }

        private void OnStartup() {
            int selfLayer = LayerMask.NameToLayer("Self");
            foreach (Transform child in self.controller.gameObject.GetComponentsInChildren<Transform>(true))
                if (child.gameObject.layer == selfLayer) ownerLayers.Add(child.gameObject);
            UpdateOwnerLayers();

            self.eventCtrl.AddEventHandler(GameEvent.Entity_Death, OnDeath);
            if (vitality.IsDead) return;
            Config.CURRENT.System.AddHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.AddHook("Gamemode:Invulnerability", ToggleInvulnerability);
            
            object intangibility = Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            object invulnerability = Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability;
            ToggleIntangibility(ref intangibility);
            ToggleInvulnerability(ref invulnerability);
            
        }

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            UpdateOwnerLayers();
            if (!self.IsOwner || indicator == null || vitality.IsDead) return;
            Config.CURRENT.System.RemoveHook("Statistics:Display", indicator.ToggleStatDisplay);
            ReMapStatDisplay(!Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability);
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (self.IsOwner) UnMapStatDisplay();
        }

        private void OnDeath(object source, object target, object context) {
            EntityManager.AddHandlerEvent(() => {
                if (self == null) return;
                RemoveHandles();
                if (self.IsOwner) UnMapStatDisplay();
            });
        }

        private void ReMapStatDisplay(bool active) {
            if (!self.IsOwner || !self.VisualActive || indicator == null) return;
            indicator.SetStatDisplay(active);
            if (!active) return;

            indicator.stats.transform.SetParent(GameUIManager.UIHandle.transform, false); 
            RectTransform rect = indicator.stats.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(-0.025f, 0); rect.anchorMax = new Vector2(0.4f, 0.15f);
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            rect.localScale = Vector3.one;
        }

        private void UnMapStatDisplay() {
            if (indicator == null || indicator.stats == null) return;
            if (self.controller.gameObject == null) return;
            if (!self.active) return;

            RectTransform rect = indicator.stats.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero; rect.offsetMax = Vector2.zero;
            indicator.stats.transform.SetParent(self.controller.gameObject.transform, false); 
        }

        private void ToggleIntangibility(ref object intangibility) {
            if (collider == null || (!self.IsAuthority && !self.IsOwner)) return;
            if (vitality.IsDead) return;
            bool IsTangible = !(bool)intangibility;
            if (IsTangible) collider.SetInteractionType(ColliderUpdateSettings.InteractType.Regular);
            else collider.SetInteractionType(ColliderUpdateSettings.InteractType.None);
        }

        private void ToggleInvulnerability(ref object invulnerability) {
            bool IsVulnerable = vitality.IsDead || !(bool)invulnerability;
            ReMapStatDisplay(IsVulnerable);

            if (!self.IsAuthority) return;
            if (!IsVulnerable) {
                self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, BlockDamage, EventHandlePriority.OverridePrimier);
                self.eventCtrl.AddEventHandler(GameEvent.Entity_FlushHealthDelta, BlockNegativeDelta, EventHandlePriority.OverridePrimier);
                self.eventCtrl.AddEventHandler(GameEvent.Entity_ExertHunger, BlockNegativeDelta, EventHandlePriority.OverridePrimier);
                self.eventCtrl.AddEventHandler(GameEvent.Entity_FlushHungerDelta, BlockNegativeDelta, EventHandlePriority.OverridePrimier);
            } if (IsVulnerable) RemoveInvulnerability();
        }

        private void RemoveHandles() {
            RemoveInvulnerability();
            Config.CURRENT.System.RemoveHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.RemoveHook("Gamemode:Invulnerability", ToggleInvulnerability);
        }

        private void RemoveInvulnerability() {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Damaged, BlockDamage);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_FlushHealthDelta, BlockNegativeDelta);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_ExertHunger, BlockNegativeDelta);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_FlushHungerDelta, BlockNegativeDelta);
        }

        private void BlockDamage(object srcId, object _, object cxt) {
            RefTuple<(float dmg, float3 kb)> info = (RefTuple<(float, float3)>)cxt;
            info.Value.dmg = 0;
            info.Value.kb = 0;
        }

        private void BlockNegativeDelta(object srcId, object _, object cxt) {
            RefTuple<float> info = (RefTuple<float>)cxt;
            info.Value = math.max(info.Value, 0);
        }
    }
}
