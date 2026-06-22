using System;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using Arterra.Configuration.Gameplay;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class SpeedEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;
        [JsonProperty] private Guid walkModifierId;
        [JsonProperty] private Guid runModifierId;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_SpeedStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_SpeedDuration, Duration);
        private float SpeedMultiplier => 1f + _strength;

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;

            return new SpeedEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_SpeedStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_SpeedDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) => self.Is(out Modifier _);

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Speed Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            progress = 0f;
            EnsureSpeedModifiers();
            UpdateSpeedModifiers();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Speed Effect Requires Animal to have Modifier Behavior");

            self.Register(this);
            EnsureSpeedModifiers();
            UpdateSpeedModifiers();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (mod != null) {
                if (walkModifierId != Guid.Empty)
                    mod.RemoveModifier(MSettings.WalkSpeed, walkModifierId);
                if (runModifierId != Guid.Empty)
                    mod.RemoveModifier(MSettings.RunSpeed, runModifierId);
            }

            walkModifierId = Guid.Empty;
            runModifierId = Guid.Empty;
            self.Unregister(typeof(SpeedEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            UpdateSpeedModifiers();
            progress += self.DeltaTime;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void EnsureSpeedModifiers() {
            EnsureModifier(MSettings.WalkSpeed, ref walkModifierId);
            EnsureModifier(MSettings.RunSpeed, ref runModifierId);
        }

        private void EnsureModifier(MSettings setting, ref Guid modifierId) {
            if (modifierId != Guid.Empty && mod.TryGetModifier(modifierId, out _)) return;

            SettingModifier speedModifier = new() {
                type = SettingModifier.MType.Multiply,
                value = SpeedMultiplier,
            };
            mod.ApplyModifier(setting, speedModifier);
            modifierId = speedModifier.Id;
        }

        private void UpdateSpeedModifiers() {
            UpdateModifier(walkModifierId);
            UpdateModifier(runModifierId);
        }

        private void UpdateModifier(Guid modifierId) {
            if (modifierId == Guid.Empty) return;
            if (!mod.TryGetModifier(modifierId, out SettingModifier modifier)) return;
            modifier.value = SpeedMultiplier;
        }
    }
}
