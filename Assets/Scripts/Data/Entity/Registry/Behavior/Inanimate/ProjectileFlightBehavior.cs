using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Burst.Intrinsics;
using Unity.Mathematics;
using UnityEngine;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ProjectileFlightSettings : IBehaviorSetting {
        public float MinDamagingSpeed;
        public float DamageMultiplier;
        public float KnockbackMultiplier;
        [Range(0, 1)]
        public float FrictionReduction = 1f;
        public EntityInteraction entityInteraction;

        public object Clone() {
            return new ProjectileFlightSettings {
                MinDamagingSpeed = MinDamagingSpeed,
                DamageMultiplier = DamageMultiplier,
                KnockbackMultiplier = KnockbackMultiplier,
                entityInteraction = entityInteraction,
                FrictionReduction = FrictionReduction,
            };
        }

        public enum EntityInteraction {
            Destroy,
            Penetrate,
            Ricochet,
        }
    }

    public class ProjectileFlightBehavior : SpeciesBehavior {
        [JsonIgnore]
        public ProjectileFlightSettings settings;

        [JsonProperty]
        public Guid ParentId;

        public static ProjectileFlightBehavior Build(Guid Parent) {
            return new ProjectileFlightBehavior() {ParentId = Parent};
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ProjectileFlightSettings), new ProjectileFlightSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileFlightBehavior requires AnimalSettings to have ProjectileFlightSettings");
            if (self.Is(out ColliderUpdateBehavior collider)) collider.MultiplyFrictionNonPersisted(1 - settings.FrictionReduction);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileFlightBehavior requires AnimalSettings to have ProjectileFlightSettings");
            if (self.Is(out ColliderUpdateBehavior collider)) collider.MultiplyFrictionNonPersisted(1 - settings.FrictionReduction);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Job) return;
            if (!CheckEntityRayCollision(self, self.position, self.velocity)) return;
        }

        private bool CheckEntityRayCollision(BehaviorEntity.Animal self, float3 startGS, float3 pVel) {
            float3 endGS = startGS + pVel * EntityJob.cxt.deltaTime;
            if (!EntityManager.ESTree.FindClosestAlongRay(startGS, endGS, self.info.rtEntityId, out Entity hitEntity, out _))
                return false;
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_ProjectileHit, self, hitEntity);
            if (!hitEntity.Is(out IAttackable atkEntity)) return false;

            float speed = math.length(pVel);
            float damage = speed * settings.DamageMultiplier;
            float3 knockback = self.velocity * settings.KnockbackMultiplier;

            if (!EntityManager.TryGetEntity(ParentId, out Entity attacker))
                EntityManager.TryGetEntity(self.info.rtEntityId, out attacker);

            if (speed > settings.MinDamagingSpeed) {
                if(attacker.info.entityId == self.info.rtEntityId)
                    AttackBehavior.RealAttack(attacker, hitEntity, damage, knockback);
                else AttackBehavior.RealAttackIndirect(attacker, hitEntity, damage, knockback);
            }

            switch (settings.entityInteraction) {
                case ProjectileFlightSettings.EntityInteraction.Ricochet:
                    float3 dir = startGS - hitEntity.position;
                    float3 reflect = math.dot(self.velocity, dir) * dir;
                    self.velocity = self.velocity - 2 * (1 - TerrainCollider.BaseFriction) * reflect;
                    break;
                case ProjectileFlightSettings.EntityInteraction.Destroy:
                    EntityManager.ReleaseEntity(self.info.entityId);
                    break;
                case ProjectileFlightSettings.EntityInteraction.Penetrate:
                    break;
            }

            return true;
        }
    }
}
