using System;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;
using Arterra.Editor;
using Arterra.Engine.Rendering;
using Arterra.GamePlay;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
#pragma warning disable CS1591
    [Serializable]
    public class BlindnessEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_BlindnessStrength, Strength), 0f);
        private float _duration => math.max(Modifier.Get(mod, MSettings.Recieve_BlindnessDuration, Duration), 0f);

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new BlindnessEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_BlindnessStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_BlindnessDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if(self.Is(out BlindnessEffect blindness)){
                BlindnessEffect b1 = blindness; BlindnessEffect b2 = this;
                if (b1.Strength < b2.Strength) {
                    var b3 = b2; b2 = b1; b1 = b3; //swap
                }

                float dur1 = b1.Duration - b1.progress;
                float dur2 = b2.Duration - b2.progress;
                if (float.IsPositiveInfinity(dur1) && float.IsPositiveInfinity(dur2)) {
                    blindness.Strength = b1.Strength;
                    blindness.Duration = float.PositiveInfinity;
                }
                else {
                    if(dur1 > dur2) blindness.Strength = b1.Strength;
                    else blindness.Strength = math.lerp(b1.Strength, b2.Strength, dur1 / math.max(dur2, 0.0001f));
                    blindness.Duration = math.max(dur1, dur2);
                }
                blindness.progress = 0f;
                return false;
            } return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            progress = 0f;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                BlindnessPass.SetActive(true);
            self.Register(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                BlindnessPass.SetActive(true);
            self.Register(this);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.Unregister(typeof(BlindnessEffect));
            if (PlayerHandler.data == null || self.info.entityId != PlayerHandler.data.info.entityId)
                return;
            BlindnessPass.SetActive(false);
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
                BlindnessPass.SetBlindness(normalized * envelope, 1.0f, 10f, 0.16f);
            }

            if (progress > _duration)
                self.RemoveBehavior(((Behavior)this).Id);
        }
    }
}
