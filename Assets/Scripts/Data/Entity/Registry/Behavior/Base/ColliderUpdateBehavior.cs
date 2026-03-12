using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arterra.Configuration;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ColliderUpdateSettings : IBehaviorSetting{
        public InteractType interactType;
        public bool UseGravity = true;
        public enum InteractType {
            Regular,
            NoEntity,
            NoGround,
            None,
            NoUpdate,
        }

        public object Clone() {
            return new ColliderUpdateSettings {
                interactType = interactType,
                UseGravity = UseGravity
            };
        }
    }
    public class ColliderUpdateBehavior : IBehavior, IMultiCollider {
        [JsonIgnore] public ColliderUpdateSettings settings;
        public ColliderUpdateSettings.InteractType Interaction;
        public HashSet<Guid> IgnoredEntities;
        public TerrainCollider collider;

        [JsonIgnore] public TerrainCollider Collider{ get => collider; }
        [JsonIgnore] public TerrainCollider PathCollider{ get => collider; }

        public void Update(BehaviorEntity.Animal self) {
            switch (Interaction) {
                case ColliderUpdateSettings.InteractType.Regular:
                    collider.Update(self);
                    collider.EntityCollisionUpdate(self, IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.NoEntity:
                    collider.Update(self);
                    break;
                case ColliderUpdateSettings.InteractType.NoGround:
                    collider.Update(self, tangible: false);
                    collider.EntityCollisionUpdate(self, IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.None:
                    collider.Update(self, tangible: false);
                    break;
                default:
                    break;
            }
        }

        public void SetInteractionType(ColliderUpdateSettings.InteractType type) => Interaction = type;
        public void ResetInteractionType() => Interaction = settings.interactType;

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ColliderUpdateSettings), new ColliderUpdateSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");

            this.collider = new TerrainCollider(setting.collider, GCoord);
            this.collider.useGravity = settings.UseGravity;
            self.Register<IMultiCollider>(this);
            ResetInteractionType();
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            
            GCoord = (int3)math.floor(Collider.transform.position);
            this.collider.useGravity = settings.UseGravity;
            self.Register<IMultiCollider>(this);
            ResetInteractionType();
        }

        public static float GetColliderDist(Entity a, Entity b) {
            if (a == null || b == null) return float.PositiveInfinity;
            var reg = Config.CURRENT.Generation.Entities;
            Bounds aBounds = new Bounds(a.position, a.transform.size);
            Bounds bBounds = new Bounds(b.position, b.transform.size);
            if (aBounds.Intersects(bBounds)) return 0;
            Vector3 aMin = aBounds.min, aMax = aBounds.max;
            Vector3 bMin = bBounds.min, bMax = bBounds.max;
            float dx = Mathf.Max(0, Mathf.Max(aMin.x - bMax.x, bMin.x - aMax.x));
            float dy = Mathf.Max(0, Mathf.Max(aMin.y - bMax.y, bMin.y - aMax.y));
            float dz = Mathf.Max(0, Mathf.Max(aMin.z - bMax.z, bMin.z - aMax.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float GetColliderDist(TerrainCollider.Transform a, TerrainCollider.Transform b) {
            var reg = Config.CURRENT.Generation.Entities;
            Bounds aBounds = new (a.position, a.size);
            Bounds bBounds = new (b.position, b.size);
            if (aBounds.Intersects(bBounds)) return 0;
            Vector3 aMin = aBounds.min, aMax = aBounds.max;
            Vector3 bMin = bBounds.min, bMax = bBounds.max;
            float dx = Mathf.Max(0, Mathf.Max(aMin.x - bMax.x, bMin.x - aMax.x));
            float dy = Mathf.Max(0, Mathf.Max(aMin.y - bMax.y, bMin.y - aMax.y));
            float dz = Mathf.Max(0, Mathf.Max(aMin.z - bMax.z, bMin.z - aMax.z));
            return Mathf.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        public static float GetColliderDist(Entity entity, float3 point) {
            var reg = Config.CURRENT.Generation.Entities;
            Bounds aBounds = new Bounds(entity.position, entity.transform.size);
            float3 nPoint = aBounds.ClosestPoint(point);
            return math.distance(nPoint, point);
        }
    }
}