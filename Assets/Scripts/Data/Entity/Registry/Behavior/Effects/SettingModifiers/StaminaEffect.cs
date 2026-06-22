using System;
using Arterra.Configuration.Gameplay;
using Arterra.Core.Events;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class StaminaEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;
        private BehaviorEntity.Animal self;

        [JsonProperty] private float progress;
        [JsonProperty] private Guid starveRateModifierId;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_StaminaStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_StaminaDuration, Duration);

        // strength=1 => 0x starvation, strength>1 => negative factor (hunger recovery)
        private float StaminaMultiplier => 1f - _strength;

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;

            return new StaminaEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_StaminaStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_StaminaDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) => self.Is(out Modifier _);
        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Stamina Effect Requires Animal to have Modifier Behavior");

            this.self = self;
            self.Register(this);
            progress = 0f;

            EnsureStarveRateModifier();
            UpdateStarveRateModifier();
            HookExertionEvent();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod))
                throw new Exception("Entity: Stamina Effect Requires Animal to have Modifier Behavior");

            this.self = self;
            self.Register(this);

            EnsureStarveRateModifier();
            UpdateStarveRateModifier();
            HookExertionEvent();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            UnhookExertionEvent();

            if (mod != null && starveRateModifierId != Guid.Empty)
                mod.RemoveModifier(MSettings.StarveRate, starveRateModifierId);

            starveRateModifierId = Guid.Empty;
            this.self = null;
            self.Unregister(typeof(StaminaEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            UpdateStarveRateModifier();
            progress += self.DeltaTime;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void EnsureStarveRateModifier() {
            if (starveRateModifierId != Guid.Empty && mod.TryGetModifier(starveRateModifierId, out _)) return;

            SettingModifier modifier = new() {
                type = SettingModifier.MType.MultiplyNegative,
                value = StaminaMultiplier,
            };

            mod.ApplyModifier(MSettings.StarveRate, modifier);
            starveRateModifierId = modifier.Id;
        }

        private void UpdateStarveRateModifier() {
            if (starveRateModifierId == Guid.Empty) return;
            if (!mod.TryGetModifier(starveRateModifierId, out SettingModifier modifier)) return;
            modifier.value = StaminaMultiplier;
        }

        private void HookExertionEvent() {
            self?.eventCtrl.AddEventHandler(GameEvent.Entity_ExertHunger, HandleExertHunger);
        }

        private void UnhookExertionEvent() {
            self?.eventCtrl.RemoveEventHandler(GameEvent.Entity_ExertHunger, HandleExertHunger);
        }

        private void HandleExertHunger(object actor, object target, object cxt) {
            if (cxt is not RefTuple<float> deltaCtx) return;
            deltaCtx.Value *= deltaCtx.Value <= 0 ? StaminaMultiplier : (2 - StaminaMultiplier);
        }
    }
}
