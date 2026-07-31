using System;
using Arterra.Configuration.Gameplay;
using Arterra.Core.Events;
using Arterra.Editor;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ShockedEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;
        private BehaviorEntity.Animal self;

        [JsonProperty] private float progress;
        [JsonIgnore] private GameObject particleInstance;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_ShockedStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_ShockedDuration, Duration);
        private float MoveMultiplier => math.clamp(1f - _strength, 0f, 1f);

        private string PlayerFenceName => self == null ? null : $"Shocked:MovementFence::{self.info.entityId}";

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;

            return new ShockedEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_ShockedStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_ShockedDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if (self.Is(out ShockedEffect existing)) {
                existing.Strength = math.max(existing.Strength, Strength);
                existing.Duration = math.max(existing.Duration, Duration);
                existing.progress = 0f;
                return false;
            }
            return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            self.Register(this);
            progress = 0f;

            HookMovementEvents();
            AttachParticle(self);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            self.Register(this);

            HookMovementEvents();
            AttachParticle(self);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            UnhookMovementEvents();

            if (particleInstance != null) {
                UnityEngine.Object.Destroy(particleInstance);
                particleInstance = null;
            }

            this.self = null;
            self.Unregister(typeof(ShockedEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            progress += self.DeltaTime;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void HookMovementEvents() {
            self?.eventCtrl.AddEventHandler(GameEvent.Action_Run, HandleMoveDelta);
            self?.eventCtrl.AddEventHandler(GameEvent.Action_Walk, HandleMoveDelta);
        }

        private void UnhookMovementEvents() {
            self?.eventCtrl.RemoveEventHandler(GameEvent.Action_Run, HandleMoveDelta);
            self?.eventCtrl.RemoveEventHandler(GameEvent.Action_Walk, HandleMoveDelta);
        }

        private void HandleMoveDelta(object actor, object target, object cxt) {
            if (cxt is not RefTuple<float3> moveDelta) return;
            moveDelta.Value *= MoveMultiplier;
        }

        private void AttachParticle(BehaviorEntity.Animal self) {
            if (particleInstance != null) return;
            Transform transform = self.controller.gameObject.transform;
            GameObject prefab = Resources.Load<GameObject>("Prefabs/GameUI/Effects/Lightning/Effect");
            if (prefab == null) return;

            particleInstance = UnityEngine.Object.Instantiate(prefab, transform);
            particleInstance.transform.localPosition = Vector3.zero;
            particleInstance.transform.localRotation = Quaternion.identity;

            Renderer renderer = transform.GetComponentInChildren<SkinnedMeshRenderer>();
            if (renderer == null) renderer = transform.GetComponentInChildren<MeshRenderer>();
            if (renderer == null) return;

            float area = EstimateArea(renderer.bounds.size);
            ParticleSystem[] systems = particleInstance.transform.GetComponentsInChildren<ParticleSystem>();
            foreach (ParticleSystem s in systems) {
                var shape = s.shape;
                if (renderer is SkinnedMeshRenderer skinned) {
                    shape.shapeType = ParticleSystemShapeType.SkinnedMeshRenderer;
                    shape.skinnedMeshRenderer = skinned;
                } else if (renderer is MeshRenderer meshRenderer) {
                    shape.shapeType = ParticleSystemShapeType.MeshRenderer;
                    shape.meshRenderer = meshRenderer;
                }

                var emission = s.emission;
                emission.rateOverTime = ScaleRate(emission.rateOverTime, area);
            }
        }

        private static float EstimateArea(Vector3 worldSize) {
            float volume = math.max(worldSize.x * worldSize.y * worldSize.z, 0.0001f);
            return math.max(Mathf.Pow(volume, 2f / 3f), 0.1f);
        }

        private static ParticleSystem.MinMaxCurve ScaleRate(ParticleSystem.MinMaxCurve rate, float scale) {
            rate.constant *= scale;
            rate.constantMin *= scale;
            rate.constantMax *= scale;
            rate.curveMultiplier *= scale;
            return rate;
        }
    }
}
