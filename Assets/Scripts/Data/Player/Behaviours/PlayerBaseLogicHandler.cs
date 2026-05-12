using Arterra.Configuration;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class PlayerBaseLogicHandler : IBehavior {
        private ColliderUpdateBehavior collider;
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            OnStartup();
        }
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out collider)) collider = null;
            OnStartup();
        }

        private void OnStartup() {
            Config.CURRENT.System.GameplayModifyHooks.TrySet("Gamemode:Intangibility", ToggleIntangibility);
            object tangibility = !Config.CURRENT.GamePlay.Gamemodes.value.Intangiblity;
            ToggleIntangibility(ref tangibility);
        }

        private void ToggleIntangibility(ref object tangibility) {
            if (collider == null) return;
            bool IsTangible = (bool)tangibility;
            if (IsTangible) collider.SetInteractionType(ColliderUpdateSettings.InteractType.Regular);
            else collider.SetInteractionType(ColliderUpdateSettings.InteractType.None);
        }
    }
}