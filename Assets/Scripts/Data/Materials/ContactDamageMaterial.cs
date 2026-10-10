using Arterra.Core.Storage;
using Arterra.Data.Entity.Behavior;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;

#pragma warning disable CS1591

namespace Arterra.Data.Material {
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/ContactDamageMaterial")]
    public class ContactDamageMaterial : MaterialBehavior {
        public float ContactDamage = 0.5f;
        public State state;
        public enum State {
            Solid, Liquid
        }

        public override void OnEntityTouchSolid(int3 GCoord, Entity.Entity entity) {
            if (entity == null) return;
            if (state != State.Solid) return;
            if (!entity.Is(out IVitality target)) return;
            entity.eventCtrl.EnqueueEvent(Arterra.Core.Events.GameEvent.Apply_Damage,
                entity.info.entityId, null, (ContactDamage, float3.zero));
        }

        public override void OnEntityTouchLiquid(int3 GCoord, Entity.Entity entity) {
            if (entity == null) return;
            if (state != State.Liquid) return;
            if (!entity.Is(out IVitality target)) return;
            entity.eventCtrl.EnqueueEvent(Arterra.Core.Events.GameEvent.Apply_Damage,
                entity.info.entityId, null, (ContactDamage, float3.zero));
        }
    }
}

#pragma warning restore CS1591