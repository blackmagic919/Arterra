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
    public class NauseaEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;

        [JsonProperty] private float progress;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_NauseaStrength, Strength), 0f);
        private float _duration => math.max(Modifier.Get(mod, MSettings.Recieve_NauseaDuration, Duration), 0f);

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new NauseaEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_NauseaStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_NauseaDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if(self.Is(out NauseaEffect nausea)){
                NauseaEffect n1 = nausea; NauseaEffect n2 = this;
                if (n1.Strength < n2.Strength) {
                    var n3 = n2; n2 = n1; n1 = n3; //swap
                }

                float Dur1 = n1.Duration - n1.progress;
                float Dur2 = n2.Duration - n2.progress;
                if (float.IsPositiveInfinity(Dur1) && float.IsPositiveInfinity(Dur2)) {
                    nausea.Strength = n1.Strength;
                    nausea.Duration = float.PositiveInfinity;
                }
                else {
                    if(Dur1 > Dur2) nausea.Strength = n1.Strength;
                    else nausea.Strength = math.lerp(n1.Strength, n2.Strength, Dur1 / math.max(Dur2, 0.0001f));
                    nausea.Duration = math.max(Dur1, Dur2);
                }
                nausea.progress = 0;
                return false;
            } return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            progress = 0f;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                NauseaPass.SetActive(true);
            self.Register(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod)) mod = null;
            if (PlayerHandler.data != null && self.info.entityId == PlayerHandler.data.info.entityId)
                NauseaPass.SetActive(true);
            self.Register(this);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.Unregister(typeof(NauseaEffect));
            if (PlayerHandler.data == null || self.info.entityId != PlayerHandler.data.info.entityId)
                return;
            NauseaPass.SetActive(false);
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
                NauseaPass.SetNausea(normalized * envelope, 0.16f);
            }

            if (progress > _duration)
                self.RemoveBehavior(((Behavior)this).Id);
        }
    }
}
