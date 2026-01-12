using UnityEngine;
using Unity.Mathematics;
using static Arterra.Experimental.BehaviorEntity;
using System;

namespace Arterra.Experimental {

    public class EntityColliderSetting : BehaviorConfig {
        public TerrainCollider.Settings collider;
        public override Type Behavior => typeof(EntityCollider);
    }

    [Serializable]
    public class EntityCollider : EntityBehavior, IEntityTransform {

        [HideInInspector]
        [UISetting(Ignore = true)]
        public TerrainCollider collider;
        public ref TerrainCollider.Transform transform => ref collider.transform;

        public override void Initialize(BehaviorConfig config, Instance self, float3 GCoord) {
            collider = new TerrainCollider(((EntityColliderSetting)config).collider, GCoord);
            self.RegisterInterface(typeof(IEntityTransform), this);
        }

        public override void Deserialize(BehaviorConfig config, Instance self) {
            self.RegisterInterface(typeof(IEntityTransform), this);
        }

        public override void Update(Instance self) {
            collider.Update(self);
        }
    }
}