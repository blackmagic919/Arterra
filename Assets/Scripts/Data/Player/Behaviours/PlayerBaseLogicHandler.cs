using Arterra.Configuration;
using Arterra.Core.Events;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerBaseLogicHandler : IBehavior {
        private BehaviorEntity.Animal self;
        private ColliderUpdateBehavior collider;
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            this.self = self;
            OnStartup();
        }
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            this.self = self;
            OnStartup();
        }

        public void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_Damaged, BlockDamage);
            Config.CURRENT.System.RemoveHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.RemoveHook("Gamemode:Invulnerability", ToggleInvulnerability);
        }

        private void OnStartup() {
            Config.CURRENT.System.AddHook("Gamemode:Intangibility", ToggleIntangibility);
            Config.CURRENT.System.AddHook("Gamemode:Invulnerability", ToggleInvulnerability);
            
            object intangibility = Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            object invulnerability = Config.CURRENT.GamePlay.Gamemodes.value.Invulnerability;
            ToggleIntangibility(ref intangibility);
            ToggleInvulnerability(ref invulnerability);
        }

        private void ToggleIntangibility(ref object intangibility) {
            if (collider == null) return;
            bool IsTangible = !(bool)intangibility;
            if (IsTangible) collider.SetInteractionType(ColliderUpdateSettings.InteractType.Regular);
            else collider.SetInteractionType(ColliderUpdateSettings.InteractType.None);
        }

        private void ToggleInvulnerability(ref object invulnerability) {
            if (collider == null) return;
            bool IsVulnerable = !(bool)invulnerability;
            if (!IsVulnerable) self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Damaged, BlockDamage);
            if (IsVulnerable) self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_Damaged, BlockDamage);
            
        }

        private void BlockDamage(object self, object _, object cxt) {
            RefTuple<(float dmg, float3 kb)> info = (RefTuple<(float, float3)>)cxt;
            info.Value.dmg = 0;
            info.Value.kb = 0;
        }
    }
}