using System;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;
using Arterra.Editor;
using Arterra.Engine.Rendering;
using Arterra.GamePlay;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class DizzinessEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_DizzinessStrength, Strength), 0f);
        private float _duration => math.max(Modifier.Get(mod, MSettings.Recieve_DizzinessDuration, Duration), 0f);

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new DizzinessEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_DizzinessStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_DizzinessDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if(self.Is(out DizzinessEffect dizziness)){
                DizzinessEffect d1 = dizziness; DizzinessEffect d2 = this;
                if (d1.Strength < d2.Strength) {
                    var d3 = d2; d2 = d1; d1 = d3; //swap
                }

                float Dur1 = d1.Duration - d1.progress;
                float Dur2 = d2.Duration - d2.progress;
                if (float.IsPositiveInfinity(Dur1) && float.IsPositiveInfinity(Dur2)) {
                    dizziness.Strength = d1.Strength;
                    dizziness.Duration = float.PositiveInfinity;
                }
                else {
                    if(Dur1 > Dur2) dizziness.Strength = d1.Strength;
                    else dizziness.Strength = math.lerp(d1.Strength, d2.Strength, Dur1 / math.max(Dur2, 0.0001f));
                    dizziness.Duration = math.max(Dur1, Dur2);
                }
                dizziness.progress = 0;
                return false;
            } return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            progress = 0f;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                DizzinessPass.SetActive(true);
            self.Register(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                DizzinessPass.SetActive(true);
            self.Register(this);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.Unregister(typeof(DizzinessEffect));
            if (PlayerHandler.data == null || self.info.entityId != PlayerHandler.data.info.entityId)
                return;
            DizzinessPass.SetActive(false);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;

            progress += self.DeltaTime;

            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId) {
                float duration = _duration;
                float envelope;
                if (float.IsPositiveInfinity(duration)) {
                    float ramp01 = math.saturate(progress);
                    envelope = math.smoothstep(0f, 1f, ramp01);
                }
                else {
                    duration = math.max(duration, 0.0001f);
                    float progress01 = math.saturate(progress / duration);
                    envelope = math.smoothstep(0f, 0.2f, progress01) * (1f - math.smoothstep(0.75f, 1f, progress01));
                }
                float normalized = 1f - math.exp(-_strength);
                float holdTime = math.max(self.DeltaTime, 0.0001f) * 2f;
                DizzinessPass.SetDizziness(normalized * envelope, holdTime);
            }

            if (progress > _duration)
                self.RemoveBehavior(((Behavior)this).Id);
        }
    }
}
