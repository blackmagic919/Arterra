using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class ProjectileFireSettings : IBehaviorSetting {
        public Option<List<AttackingState>> AttackingStates = new () {value = new () {
            new() { State = EntitySMTasks.ChaseTarget },
            new() { State = EntitySMTasks.ChasePreyEntity }
        }};

        [Serializable]
        public struct AttackingState {
            public EntitySMTasks State;
            public bool DirectRotation;
        }

        public Option<Stats> Projectile;
        public float BlindDist = 3;
        [JsonIgnore]
        [UIHidden]
        [HideInInspector]
        public Dictionary<EntitySMTasks, bool> AttackStates;

        [Serializable]
        public class Stats : ICloneable {
            public float ShotDelay = 0.25f;
            public bool CheckSightline = true;
            public float ChargeTime = 5;
            public Option<ProjectileTag> Projectile;
            public bool HasRangedAttack = true;
            public float MaxTargetAngle = 360f; // Degrees.

            public object Clone() {
                return new Stats {
                    ShotDelay = ShotDelay,
                    CheckSightline = CheckSightline,
                    ChargeTime = ChargeTime,
                    Projectile = Projectile,
                    HasRangedAttack = HasRangedAttack,
                    MaxTargetAngle = MaxTargetAngle
                };
            }
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting settings) {
            AttackStates = new Dictionary<EntitySMTasks, bool>();
            if (AttackingStates.value == null) return;
            foreach (var state in AttackingStates.value)
                AttackStates[state.State] = state.DirectRotation;
        }

        public object Clone() {
            return new ProjectileFireSettings {
                AttackingStates = AttackingStates,
                Projectile = Projectile,
                BlindDist = BlindDist
            };
        }
    }

    public class ProjectileFireBehavior : SpeciesBehavior {
        private ProjectileFireSettings settings;
        private StateMachineManagerBehavior manager;
        private AnimatedBehavior animated;
        private Movement movement;
        [JsonIgnore] private Modifier mod;

        private float3 fireDirection;
        private float chargeCooldown;
        private float shotProgress;
        public bool ShotInProgress;

        private float BlindDist => Modifier.Get(mod, MSettings.BlindDist, settings.BlindDist);
        private float ChargeTime => Modifier.Get(mod, MSettings.ChargeTime, settings.Projectile.value.ChargeTime);

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Simulation)
                CoreUpdate(self);
            if (self.context == BehaviorEntity.UpdateContext.Sync)
                ControllerUpdate(self);
        }

        public void CoreUpdate(BehaviorEntity.Animal self) {
            UpdateFire(self);
            if (!settings.AttackStates.TryGetValue(manager.TaskIndex, out bool directRotation)) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity entity)) return;
            if (directRotation && settings.Projectile.value.HasRangedAttack) {
                float3 direction = entity.head - self.position;
                if (math.lengthsq(direction) > 1E-10f)
                    self.Rotation = Quaternion.RotateTowards(self.Rotation,
                        Quaternion.LookRotation(math.normalizesafe(direction)), movement.rotSpeed * self.DeltaTime);
            }
            Fire(entity.head, self);
        }

        public void ControllerUpdate(BehaviorEntity.Animal self) {
            if (animated == null) return;
            Animator animator = animated.animator;
            animator.SetBool("IsShooting", ShotInProgress);
        }

        //This is some whirly logic where if you call fire on a loop, it will
        public bool Fire(float3 target, Entity self) {
            if (!settings.Projectile.value.HasRangedAttack) return false;
            if (ShotInProgress) return false;
            if (chargeCooldown > 0) return false;
            float3 direction = target - self.position;
            float distance = math.length(direction);
            if (!math.isfinite(distance) || distance <= 0f || distance < BlindDist) return false;
            fireDirection = direction;
            if (Vector3.Angle(self.Forward, fireDirection) > settings.Projectile.value.MaxTargetAngle)
                return false;
            
            if (settings.Projectile.value.CheckSightline) {
                if (Terraforming.RayCastTerrain(self.head, math.normalizesafe(fireDirection), 
                    distance, Terraforming.RayTestSolid, out float3 hit))
                    return false;
            } 
            ShotInProgress = true;
            shotProgress = settings.Projectile.value.ShotDelay;
            return true;
        }

        public void UpdateFire(BehaviorEntity.Animal parent) {
            chargeCooldown = math.max(chargeCooldown - parent.DeltaTime, 0);
            if (!ShotInProgress) return;
            shotProgress = math.max(shotProgress - parent.DeltaTime, 0);
            if (shotProgress > 0) return;
            settings.Projectile.value.Projectile.value.LaunchProjectile(parent, fireDirection);
            chargeCooldown = ChargeTime;
            ShotInProgress = false;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ProjectileFireSettings), new ProjectileFireSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


         public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalSettings to have ProjectileFireSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out animated)) animated = null;
            if (self.Is(out mod)) mod = null;

            chargeCooldown = ChargeTime;
            shotProgress = 0; ShotInProgress = false;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalSettings to have ProjectileFireSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out animated)) animated = null;
            if (self.Is(out mod)) mod = null;
        }
        
    }
}
