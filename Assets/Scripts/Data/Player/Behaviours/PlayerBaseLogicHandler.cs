using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.GamePlay.UI;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerBaseLogicHandler : SpeciesBehavior {
        private BehaviorEntity.Animal self;
        private InidcatorsBehavior indicator;
        private ColliderUpdateBehavior collider;
        private VitalityBehavior vit;
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out indicator)) indicator = null;
            if (!self.Is(out vit)) vit = null;
            this.self = self;
            OnStartup();
        }
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            if (!self.Is(out indicator)) indicator = null;
            if (!self.Is(out vit)) vit = null;
            this.self = self;
            OnStartup();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_Damaged, BlockDamage);
            Config.CURRENT.System.RemoveHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.RemoveHook("Gamemode:Invulnerability", ToggleInvulnerability);
            UnMapStatDisplay();
            self = null;
        }

        private void OnStartup() {
            Config.CURRENT.System.AddHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.AddHook("Gamemode:Invulnerability", ToggleInvulnerability);
            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Death, (_, _) => self.RemoveBehavior(Id));
            
            object intangibility = Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            object invulnerability = Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability;
            ToggleIntangibility(ref intangibility);
            ToggleInvulnerability(ref invulnerability);
            
            if (indicator == null) return;
            Config.CURRENT.System.RemoveHook("Statistics:Display", indicator.ToggleStatDisplay);
            ReMapStatDisplay(!(bool)invulnerability);
        }

        private void ReMapStatDisplay(bool active) {
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
            if (collider == null) return;
            bool IsTangible = !(bool)intangibility;
            if (IsTangible) collider.SetInteractionType(ColliderUpdateSettings.InteractType.Regular);
            else collider.SetInteractionType(ColliderUpdateSettings.InteractType.None);
        }

        private void ToggleInvulnerability(ref object invulnerability) {
            bool IsVulnerable = !(bool)invulnerability;
            ReMapStatDisplay(IsVulnerable);

            if (collider == null) return;
            if (!IsVulnerable) self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, BlockDamage, EventHandlePriority.OverridePrimier);
            if (IsVulnerable) self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_Damaged, BlockDamage);
        }

        private void BlockDamage(object self, object _, object cxt) {
            RefTuple<(float dmg, float3 kb)> info = (RefTuple<(float, float3)>)cxt;
            info.Value.dmg = 0;
            info.Value.kb = 0;
        }
    }
}