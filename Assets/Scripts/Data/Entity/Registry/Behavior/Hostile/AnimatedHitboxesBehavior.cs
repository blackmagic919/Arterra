using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class AnimatedHitboxesSettings : IBehaviorSetting {
        [JsonIgnore] public static string Name => "Animated Hitboxes";
        public Option<List<Hitbox>> Hitboxes;

        [Serializable]
        // collider.size is fixed in grid units. BoneOffset offsets the hitbox center
        // from the pillar pivot in grid units, along the pillar's rotated local axes.
        // Model scale does not change either setting.
        public class Hitbox {
            public TerrainCollider.Settings collider;
            public float3 BoneOffset;
            public string BonePath;
        }

        public object Clone() => new AnimatedHitboxesSettings { Hitboxes = Hitboxes };
    }

    // Animation is the sole pose authority. These appendages receive hits and
    // participate in entity collisions, but never steer, attack, grab or write bones.
    public class AnimatedHitboxesBehavior : SpeciesBehavior {
        [JsonProperty] private AnimatedHitbox[] hitboxes;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Animator, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.Vitality, hierarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) =>
            hierarchy.TryAdd(typeof(AnimatedHitboxesSettings), new AnimatedHitboxesSettings());

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 coord) => Bind(self, setting, false);
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 coord) => Bind(self, setting, true);

        private void Bind(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, bool restoring) {
            if (!setting.Is(out AnimatedHitboxesSettings config))
                throw new InvalidOperationException("Animated hitboxes require AnimatedHitboxesSettings.");
            self.controller.KeepAnimatorOnDisposeVisual = true;
            var entries = config.Hitboxes.value;
            hitboxes ??= new AnimatedHitbox[entries.Count];
            var root = self.controller.gameObject.transform;
            for (int i = 0; i < entries.Count; i++) {
                var entry = entries[i];
                var target = root.Find(entry.BonePath);
                if (target == null) throw new InvalidOperationException($"Animated hitbox target missing: {entry.BonePath}");
                var appendage = new HitboxAppendageSettings {
                    collider = entry.collider, BoneOffset = entry.BoneOffset, BonePath = entry.BonePath
                };
                if (restoring && hitboxes[i] != null)
                    hitboxes[i].Deserialize(self, appendage, root);
                else {
                    hitboxes[i] = new AnimatedHitbox(i);
                    hitboxes[i].Initialize(self, appendage, root);
                }
            }
        }

        private sealed class HitboxAppendageSettings : LeadHeadBehavior.AppendageSettings { }
        

        public override void Update(BehaviorEntity.Animal self) {
            if (hitboxes == null) return;
            if (self.context == BehaviorEntity.UpdateContext.Sync
                || (self.context == BehaviorEntity.UpdateContext.Visual && !self.IsAuthority))
                foreach (var hitbox in hitboxes) hitbox.SamplePose();
            if (self.context == BehaviorEntity.UpdateContext.Simulation)
                foreach (var hitbox in hitboxes) hitbox.Update();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (hitboxes == null) return;
            foreach (var hitbox in hitboxes) hitbox?.Disable();
        }

        public override void OnDrawGizmos(BehaviorEntity.Animal self) {
            if (hitboxes == null) return;
            Gizmos.color = Color.green;
            foreach (var hitbox in hitboxes) hitbox?.DrawGizmos();
        }

        private sealed class AnimatedHitbox : LeadHeadBehavior.Appendage {
            [JsonConstructor] public AnimatedHitbox() { }
            protected override Bounds SpatialBounds => new Bounds((float3)collider.transform.position + collider.transform.size / 2, collider.transform.size);
            [JsonIgnore] private float3 targetOrigin;
            [JsonIgnore] private bool hasPose;
            [JsonProperty] private int index;

            public AnimatedHitbox(int index) { this.index = index; } //Newtonsoft path

            public override void Initialize(BehaviorEntity.Animal animal, LeadHeadBehavior.AppendageSettings config, Transform root) {
                Transform bone = root.Find(config.BonePath) ?? root;
                float3 origin = TargetOrigin(bone, root, animal.position, config.BoneOffset, config.collider.size);
                collider = new TerrainCollider(config.collider, origin, $"AnimatedHitbox+{index}");

                base.Initialize(animal, config, root);
                BindTarget();
            }

            public override void Deserialize(BehaviorEntity.Animal animal, LeadHeadBehavior.AppendageSettings config, Transform root) {
                base.Deserialize(animal, config, root);
                BindTarget();
            }

            private void BindTarget() {
                if (Controller == null) throw new InvalidOperationException($"Animated hitbox target missing: {aSettings.BonePath}");
                // Reapply configured dimensions on restore as well as on initial creation.
                collider.transform.size = aSettings.collider.size;
                SamplePose();
                collider.SetGravityState("AnimatedHitbox", TerrainCollider.GravityPriority.Override, false);
            }

            private float3 TargetOrigin() =>
                TargetOrigin(Controller, self.controller.gameObject.transform, self.position, aSettings.BoneOffset, collider.transform.size);

            private static float3 TargetOrigin(Transform bone, Transform root, float3 entityPos, float3 offset, float3 size) =>
                CPUMapManager.WSToGSScale(bone.position - root.position)
                + entityPos + (float3)bone.TransformDirection((Vector3)offset) - size / 2;

            public void SamplePose() {
                targetOrigin = TargetOrigin();
                hasPose = true;
            }

            public override void UpdateJobSync() { } // Never move the animated object.

            public void DrawGizmos() {
                if (!active || self == null || collider == null) return;
                Gizmos.DrawWireCube(CPUMapManager.GSToWS(
                    (float3)collider.transform.position + collider.transform.size / 2),
                    collider.transform.size * 2);
                if (hasPose)
                    Gizmos.DrawSphere(CPUMapManager.GSToWS(targetOrigin + collider.transform.size / 2), 0.25f);
            }

            public override void Update() {
                if (!active || self == null || !hasPose || self.DeltaTime <= 0) return;
                // Implicit damped spring: add velocity toward the sampled pose and let
                // the collider integrate movement and resolve contacts normally.
                const float response = 30f;
                float dt = self.DeltaTime;
                float3 error = targetOrigin - (float3)collider.transform.position;
                float denominator = 1f + 2f * response * dt + response * response * dt * dt;
                collider.transform.velocity += dt * (response * response * error
                    - 2f * response * collider.transform.velocity
                    - response * response * dt * collider.transform.velocity) / denominator;
                collider.Update(self, baseFriction: 0);
                collider.EntityCollisionUpdate(self);
                UpdateSpatialBounds();
            }

        }
    }
}
