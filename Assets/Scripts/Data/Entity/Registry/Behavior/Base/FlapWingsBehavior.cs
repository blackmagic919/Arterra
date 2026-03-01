using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior{
    public class FlapWingsBehaviorSettings: IBehaviorSetting {
        public string FlyingAnimatorParam = "IsFlying";
        public string FlapWingsAnimatorParam = "IsAscending";

        public object Clone() {
            return new FlapWingsBehaviorSettings {
                FlyingAnimatorParam = FlyingAnimatorParam,
                FlapWingsAnimatorParam = FlapWingsAnimatorParam
            };
        }
    }

    public class FlapWingsBehavior : IBehavior {
        private FlapWingsBehaviorSettings settings;
        private AnimatedBehavior animator;
        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if (animator.GetCurrentAnimation() != settings.FlyingAnimatorParam) return;
            if (self.velocity.y >= -1E-4f) animator.SetBool(settings.FlapWingsAnimatorParam, true);
            else animator.SetBool(settings.FlapWingsAnimatorParam, false);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Animator, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(FlapWingsBehaviorSettings), new FlapWingsBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlapWing Behavior Requires AnimalSettings to have FlapWingSettings");
            if (!self.Is(out animator))
                throw new System.Exception("Entity: FlapWing Behavior Requires AnimalInstance to have AnimatedBehavior");
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: FlapWing Behavior Requires AnimalSettings to have FlapWingSettings");
            if (!self.Is(out animator))
                throw new System.Exception("Entity: FlapWing Behavior Requires AnimalInstance to have AnimatedBehavior");
        }
    }
}