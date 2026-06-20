using System;
using Arterra.Configuration;
using Arterra.Configuration.Gameplay;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class BurningEffect : TempBehavior, IEffect {
        public float Strength;
        public float Duration;
        [RegistryReference("Textures")]
        public string EffectIcon;
        [JsonIgnore] public string Icon => EffectIcon;

        private Modifier mod;
        private VitalityBehavior vit;

        [JsonProperty] private float progress;
        [JsonProperty] private float accDeltaTime;
        [JsonIgnore] private GameObject particleInstance;

        private float _strength => math.max(Modifier.Get(mod, MSettings.Recieve_BurningStrength, Strength), 0f);
        private float _duration => Modifier.Get(mod, MSettings.Recieve_BurningDuration, Duration);

        public override TempBehavior Create(BehaviorEntity.Animal self = null) {
            if (self == null || !self.Is(out Modifier inflictMod))
                inflictMod = null;
            return new BurningEffect {
                Strength = Modifier.Get(inflictMod, MSettings.Inflict_BurningStrength, Strength),
                Duration = Modifier.Get(inflictMod, MSettings.Inflict_BurningDuration, Duration),
                EffectIcon = EffectIcon,
            };
        }

        public override bool CanApply(BehaviorEntity.Animal self) {
            if (!self.Is(out vit)) return false;
            if (self.Is(out BurningEffect b)) {
                b.Strength = math.max(b.Strength, Strength);
                b.Duration = math.max(b.Duration, Duration);
                return false;
            }
            return true;
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!self.Is(out vit))
                throw new Exception("Entity: Burning Effect Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
            self.Register(this);
            accDeltaTime = 0f;
            progress = 0f;
            AttachParticle(self);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!self.Is(out vit))
                throw new Exception("Entity: Burning Effect Requires Animal to have Vitality Behavior");
            if (!self.Is(out mod)) mod = null;
            self.Register(this);
            AttachParticle(self);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (particleInstance != null) {
                UnityEngine.Object.Destroy(particleInstance);
                particleInstance = null;
            }
            self.Unregister(typeof(BurningEffect));
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;

            accDeltaTime += self.DeltaTime;
            progress += self.DeltaTime;

            float damage = accDeltaTime * _strength;
            if (vit.TakeDamage(damage, float3.zero)) accDeltaTime = 0f;
            if (progress > _duration) self.RemoveBehavior(Id);
        }

        private void AttachParticle(BehaviorEntity.Animal self) {
            if (particleInstance != null) return;
            Transform transform = self.controller.gameObject.transform;
            GameObject prefab = Resources.Load<GameObject>("Prefabs/GameUI/Effects/Fire");
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