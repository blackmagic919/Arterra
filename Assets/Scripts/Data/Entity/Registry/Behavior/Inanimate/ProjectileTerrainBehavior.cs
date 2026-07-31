using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ProjectileTerrainSettings : IBehaviorSetting {
        public GroundInteraction terrainInteraction;

        public object Clone() {
            return new ProjectileTerrainSettings {
                terrainInteraction = terrainInteraction
            };
        }

        public enum GroundInteraction {
            Stick,
            Destroy,
            Slide,
            Flop,
            Bounce,
        }
    }

    public class ProjectileTerrainBehavior : SpeciesBehavior {
        [JsonIgnore]
        public ProjectileTerrainSettings settings;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ProjectileTerrainSettings), new ProjectileTerrainSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileTerrainBehavior requires AnimalSettings to have ProjectileTerrainSettings");
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileTerrainBehavior requires AnimalSettings to have ProjectileTerrainSettings");
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Job) return;
            if (!CheckTerrainCollision(self)) return;
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_ProjectileHit, self, null);
        }

        private bool CheckTerrainCollision(BehaviorEntity.Animal self) {
            TerrainCollider collider = self.Collider;
            if (collider == null) return false;
            if (!TerrainCollider.SampleCollision((float3)self.transform.position, self.transform.size * 1.05f, EntityJob.cxt.mapContext, out float3 gDir))
                return false;

            float3 forward = math.normalizesafe(-gDir, math.forward(self.transform.rotation));
            if (math.lengthsq(forward) < 1e-8f) return false;

            switch (settings.terrainInteraction) {
                case ProjectileTerrainSettings.GroundInteraction.Flop:
                    self.transform.rotation = Quaternion.LookRotation(forward, math.up());
                    break;
                case ProjectileTerrainSettings.GroundInteraction.Stick:
                    self.transform.rotation = Quaternion.LookRotation(forward, math.up());
                    self.velocity = 0;
                    break;
                case ProjectileTerrainSettings.GroundInteraction.Destroy:
                    EntityManager.ReleaseEntity(self.info.entityId);
                    break;
                case ProjectileTerrainSettings.GroundInteraction.Bounce:
                    float3 dir = math.normalize(gDir);
                    float3 reflect = math.dot(self.velocity, dir) * dir;
                    self.velocity = self.velocity - 2 * (1 - TerrainCollider.BaseFriction) * reflect;
                    break;
                case ProjectileTerrainSettings.GroundInteraction.Slide:
                    break;
            }

            return true;
        }
    }
}
