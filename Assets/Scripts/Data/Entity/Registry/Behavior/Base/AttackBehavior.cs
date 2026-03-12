using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class AttackStats : IBehaviorSetting {
        public Genetics.GeneFeature AttackDistance;
        public Genetics.GeneFeature AttackDamage;
        public Genetics.GeneFeature AttackCooldown;
        public Genetics.GeneFeature KBStrength;
        public float AttackDuration;

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref AttackDistance);
            Genetics.AddGene(entityType, ref AttackDamage);
            Genetics.AddGene(entityType, ref AttackCooldown);
            Genetics.AddGene(entityType, ref KBStrength);
        }

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
    public class AttackBehavior : IBehavior {
        [JsonIgnore] public AttackStats settings;
        private GeneticsBehavior genetics; 
        private BehaviorEntity.Animal self;
        
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

        public void Update(BehaviorEntity.Animal self) {
            attackCooldown = math.max(attackCooldown - EntityJob.cxt.deltaTime, 0);
            if (!AttackInProgress) return;
            attackProgress = math.max(attackProgress - EntityJob.cxt.deltaTime, 0);
            if (attackProgress > 0) return;
            FlushAttack(self);
        }

        private void FlushAttack(Entity self) {
            AttackInProgress = false;
            attackCooldown = genetics.Genes.Get(settings.AttackCooldown);
            
            if (!EntityManager.TryGetEntity(AttackTarget, out Entity target))
                return;
            if (!target.Is(out IAttackable atkTarget)) return;
            if (ColliderUpdateBehavior.GetColliderDist(target, self)
                > genetics.Genes.Get(settings.AttackDistance))
                return;
            float damage = genetics.Genes.Get(settings.AttackDamage);
            float3 knockback = math.normalize(target.position - self.position) * genetics.Genes.Get(settings.KBStrength);
            RealAttack(self, atkTarget, damage, knockback);
        }

        public static void RealAttack(Entity self, IAttackable target, float damage, float3 knockback) {
            RefTuple<(float, float3)> cxt = (damage, knockback);
            self.eventCtrl.RaiseEvent(
                GameEvent.Entity_Attack,
                self, target, cxt
            ); (damage, knockback) = cxt.Value;
            EntityManager.AddHandlerEvent(() => target.TakeDamage(damage, knockback, self));
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(AttackStats), new AttackStats());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalSettings to have AttackStats");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalInstance to have GeneticsBehavior");

            attackProgress = settings.AttackDuration;
            attackCooldown = 0;
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalSettings to have AttackStats");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Attack Behavior Requires AnimalInstance to have GeneticsBehavior");
            
            this.self = self;
        }

        public void Disable() {
            this.self = null;
        }
    }
}