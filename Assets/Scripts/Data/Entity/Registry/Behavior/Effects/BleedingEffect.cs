using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;
using Arterra.Core.Events;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class BleedingEffect : ITempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore]public string Icon => EffectIcon;

        private Modifier mod;
        private VitalityBehavior vit;
        
        [JsonProperty] private float progress;
        [JsonProperty] private float accDeltaTime;
        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_BleedingStrength, Strength), 0);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_BleedingDuration, Duration);
        public ITempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new BleedingEffect(){
                Strength = Modifier.Get(inflictMod, MSettings.Recieve_BleedingStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Recieve_BleedingDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public bool CanApply(BehaviorEntity.Animal self) {
            if(!self.Is(out vit)) return false;
            if(self.Is(out PoisonEffect p)) {
                p.Strength = math.max(p.Strength, Strength);
                p.Duration = math.max(p.Duration, Duration);
                return false;
            } return true;
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out vit))
                throw new System.Exception("Entity: Effector Poison Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
            accDeltaTime = 0;
            progress = 0;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out vit))
                throw new System.Exception("Entity: Effector Poison Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
        }

        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;
            accDeltaTime += self.DeltaTime;
            progress += self.DeltaTime;

            float damage = -accDeltaTime * _strength;
            if (vit.TakeDamage(damage, float3.zero)) accDeltaTime = 0;
            if (progress > _duration) self.Unregister(typeof(BleedingEffect));
        }
    }
}