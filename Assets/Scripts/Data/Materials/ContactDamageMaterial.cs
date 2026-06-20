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

        public override void OnEntityTouchSolid(Entity.Entity entity) {
            if (entity == null) return;
            if (state != State.Solid) return;
            if (!entity.Is(out IAttackable target)) return;
            EntityManager.AddHandlerEvent(() => target.TakeDamage(ContactDamage, float3.zero, null));
        }

        public override void OnEntityTouchLiquid(Entity.Entity entity) {
            if (entity == null) return;
            if (state != State.Liquid) return;
            if (!entity.Is(out IAttackable target)) return;
            EntityManager.AddHandlerEvent(() => target.TakeDamage(ContactDamage, float3.zero, null));
        }
    }
}

#pragma warning restore CS1591