using System;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using Arterra.Configuration.Gameplay;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class RegenerationEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;
        [JsonProperty] private Guid regenModifierId;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_RegenerationStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_RegenerationDuration, Duration);
        private float RegenDelta => 1 + _strength;

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;

            return new RegenerationEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_RegenerationStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_RegenerationDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) => self.Is(out Modifier _);
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Regeneration Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            progress = 0f;
            EnsureRegenModifier();
            UpdateRegenModifier();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Regeneration Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            EnsureRegenModifier();
            UpdateRegenModifier();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (mod != null && regenModifierId != Guid.Empty)
                mod.RemoveModifier(MSettings.NaturalRegen, regenModifierId);
            regenModifierId = Guid.Empty;
            self.Unregister(typeof(RegenerationEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            UpdateRegenModifier();
            progress += self.DeltaTime;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void EnsureRegenModifier() {
            if (regenModifierId != Guid.Empty && mod.TryGetModifier(regenModifierId, out _)) return;

            SettingModifier regenModifier = new() {
                type = SettingModifier.MType.MultiplyPositive,
                value = RegenDelta,
            };
            mod.ApplyModifier(MSettings.NaturalRegen, regenModifier);
            regenModifierId = regenModifier.Id;
        }

        private void UpdateRegenModifier() {
            if (regenModifierId == Guid.Empty) return;
            if (!mod.TryGetModifier(regenModifierId, out SettingModifier modifier)) return;
            modifier.value = RegenDelta;
        }
    }
}