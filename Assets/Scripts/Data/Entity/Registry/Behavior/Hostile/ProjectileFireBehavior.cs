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
        public Option<List<EntitySMTasks>> AttackingStates = new () {value = new () {
            EntitySMTasks.ChaseTarget,
            EntitySMTasks.ChasePreyEntity
        }};

        public Stats Projectile;
        public float BlindDist = 3;
        [JsonIgnore]
        [UIHidden]
        [HideInInspector]
        public HashSet<EntitySMTasks> AttackStates;

        [Serializable]
        public class Stats {
            public float ShotDelay = 0.25f;
            public bool CheckSightline = true;
            public float ChargeTime = 5;
            public ProjectileTag Projectile;
            public bool HasRangedAttack = true;
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting settings) {
            AttackStates = new HashSet<EntitySMTasks>(AttackingStates.value);
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
        [JsonIgnore] private Modifier mod;

        private float3 fireDirection;
        private float chargeCooldown;
        private float shotProgress;
        public bool ShotInProgress;

        private float BlindDist => Modifier.Get(mod, MSettings.BlindDist, settings.BlindDist);
        private float ChargeTime => Modifier.Get(mod, MSettings.ChargeTime, settings.Projectile.ChargeTime);

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Simulation)
                CoreUpdate(self);
            if (self.context == BehaviorEntity.UpdateContext.Sync)
                ControllerUpdate(self);
        }

        public void CoreUpdate(BehaviorEntity.Animal self) {
            UpdateFire(self);
            if(!settings.AttackStates.Contains(manager.TaskIndex)) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity entity)) return;
            Fire(entity.head, self);
        }

        public void ControllerUpdate(BehaviorEntity.Animal self) {
            if (animated == null) return;
            Animator animator = animated.animator;
            animator.SetBool("IsShooting", ShotInProgress);
        }

        //This is some whirly logic where if you call fire on a loop, it will
        public bool Fire(float3 target, Entity self) {
            if (!settings.Projectile.HasRangedAttack) return false;
            if (ShotInProgress) return false;
            if (chargeCooldown > 0) return false;
            fireDirection = target - self.position;
            if (math.length(fireDirection) < BlindDist) return false;
            
            if (settings.Projectile.CheckSightline) {
                if (Terraforming.RayCastTerrain(self.head, math.normalizesafe(fireDirection), 
                    math.length(fireDirection), Terraforming.RayTestSolid, out float3 hit))
                    return false;
            } 
            ShotInProgress = true;
            shotProgress = settings.Projectile.ShotDelay;
            return true;
        }

        public void UpdateFire(BehaviorEntity.Animal parent) {
            chargeCooldown = math.max(chargeCooldown - parent.DeltaTime, 0);
            if (!ShotInProgress) return;
            shotProgress = math.max(shotProgress - parent.DeltaTime, 0);
            if (shotProgress > 0) return;
            settings.Projectile.Projectile.LaunchProjectile(parent, fireDirection);
            chargeCooldown = ChargeTime;
            ShotInProgress = false;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ProjectileFireSettings), new ProjectileFireSettings());
        }


         public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalSettings to have ProjectileFireSettings");
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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ProjectileFire Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out animated)) animated = null;
            if (self.Is(out mod)) mod = null;
        }
        
    }
}