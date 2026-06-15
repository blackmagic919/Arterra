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
    public class PoisonEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore]public string Icon => EffectIcon;

        private Modifier mod;
        private VitalityBehavior vit;
        
        [JsonProperty] private float progress;
        [JsonProperty] private float accDeltaTime;
        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_PoisonStrength, Strength), 0);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_PoisonDuration, Duration);
        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new PoisonEffect(){
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_PoisonStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_PoisonDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            return self.Is(out vit);
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out vit))
                throw new System.Exception("Entity: Effector Poison Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
            accDeltaTime = 0;
            progress = 0;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out vit))
                throw new System.Exception("Entity: Effector Poison Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;
            accDeltaTime += self.DeltaTime;
            progress += self.DeltaTime;
            
            float damage = (1 - math.exp(-accDeltaTime * _strength)) * vit.health;
            if (vit.TakeDamage(damage, float3.zero)) accDeltaTime = 0;
            if (progress > _duration) self.RemoveBehavior(((Behavior)this).Id);
        }
    }
}