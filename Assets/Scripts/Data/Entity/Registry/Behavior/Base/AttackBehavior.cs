using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class AttackStats : IBehaviorSetting {
        public float AttackDistance;
        public float AttackDamage;
        public float AttackCooldown;
        public float KBStrength;
        public float AttackDuration;

        public object Clone() {
            return new AttackStats{
                AttackDistance = AttackDistance,
                AttackDamage = AttackDamage,
                AttackCooldown = AttackCooldown,
                KBStrength = KBStrength,
                AttackDuration = AttackDuration
            };
        }
    }
    public class AttackBehavior : SpeciesBehavior {
        [JsonIgnore] public AttackStats settings;
        private BehaviorEntity.Animal self;
        private Modifier mods; 
        
        public float attackProgress;
        public float attackCooldown;
        //So an entity cannot attack immediately
        public bool AttackInProgress;
        public Guid AttackTarget;


        public bool Attack(Entity target) {
            if (AttackInProgress) return false;
            if (attackCooldown > 0) return false;
            if (!target.Is<IAttackable>()) return false;
            RefTuple<float> cxt = settings.AttackDuration;
            self.eventCtrl.RaiseEvent(
                GameEvent.Entity_ReadyAttack,
                self, target, cxt
            ); 
            attackProgress = cxt.Value;
            AttackTarget = target.info.entityId;
            AttackInProgress = true;
            return true;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            attackCooldown = math.max(attackCooldown - self.DeltaTime, 0);
            if (!AttackInProgress) return;
            attackProgress = math.max(attackProgress - self.DeltaTime, 0);
            if (attackProgress > 0) return;
            FlushAttack(self);
        }

        private void FlushAttack(Entity self) {
            AttackInProgress = false;
            attackCooldown = Modifier.Get(mods, MSettings.AttackCooldown, settings.AttackCooldown);
            
            if (!EntityManager.TryGetEntity(AttackTarget, out Entity target))
                return;
            if (ColliderUpdateBehavior.GetColliderDist(target, self)
                > Modifier.Get(mods, MSettings.AttackDistance, settings.AttackDistance))
                return;
            float damage = Modifier.Get(mods, MSettings.AttackDamage, settings.AttackDamage);
            float3 knockback = math.normalize(target.position - self.position) 
                * Modifier.Get(mods, MSettings.KBStrength, settings.KBStrength);
            RealAttack(self, target, damage, knockback);
        }

        public static void RealAttack(Entity self, Entity target, float damage, float3 knockback) {
            if (!target.Is(out IAttackable atkTarget)) return;
            RefTuple<(float, float3)> cxt = (damage, knockback);
            self.eventCtrl.RaiseEvent(
                GameEvent.Entity_Attack,
                self, target, cxt
            ); (damage, knockback) = cxt.Value;
            EntityManager.AddHandlerEvent(() => atkTarget.TakeDamage(damage, knockback, self));
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(AttackStats), new AttackStats());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalSettings to have AttackStats");
            if (!self.Is(out mods)) mods = null;

            attackProgress = settings.AttackDuration;
            attackCooldown = 0;
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalSettings to have AttackStats");
            if (!self.Is(out mods))  mods = null;
            
            this.self = self;
        }

        public void Disable() {
            this.self = null;
        }
    }
}