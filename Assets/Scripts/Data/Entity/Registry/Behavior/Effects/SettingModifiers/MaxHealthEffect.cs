using System;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using Arterra.Configuration.Gameplay;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class MaxHealthEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;
        [JsonProperty] private Guid maxHealthModifierId;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_MaxHealthStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_MaxHealthDuration, Duration);
        private float HealthMultiplier => 1f + _strength;

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;

            return new MaxHealthEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_MaxHealthStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_MaxHealthDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if (!self.Is(out Modifier _)) return false;
            if (self.Is(out MaxHealthEffect existing)) {
                existing.Strength = math.max(existing.Strength, Strength);
                existing.Duration = math.max(existing.Duration, Duration);
                existing.progress = 0f;
                return false;
            }
            return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: MaxHealth Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            progress = 0f;
            EnsureMaxHealthModifier();
            UpdateMaxHealthModifier();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: MaxHealth Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            EnsureMaxHealthModifier();
            UpdateMaxHealthModifier();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (mod != null && maxHealthModifierId != Guid.Empty)
                mod.RemoveModifier(MSettings.MaxHealth, maxHealthModifierId);

            maxHealthModifierId = Guid.Empty;
            self.Unregister(typeof(MaxHealthEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            UpdateMaxHealthModifier();
            progress += self.DeltaTime;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void EnsureMaxHealthModifier() {
            if (maxHealthModifierId != Guid.Empty && mod.TryGetModifier(maxHealthModifierId, out _)) return;

            SettingModifier healthModifier = new() {
                type = SettingModifier.MType.MultiplyPositive,
                value = HealthMultiplier,
            };
            mod.ApplyModifier(MSettings.MaxHealth, healthModifier);
            maxHealthModifierId = healthModifier.Id;
        }

        private void UpdateMaxHealthModifier() {
            if (maxHealthModifierId == Guid.Empty) return;
            if (!mod.TryGetModifier(maxHealthModifierId, out SettingModifier modifier)) return;
            modifier.value = HealthMultiplier;
        }
    }
}
