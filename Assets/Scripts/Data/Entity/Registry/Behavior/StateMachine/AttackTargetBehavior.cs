
using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class AttackTargetSettings : IBehaviorSetting {
        public const string AnimationParam = "IsAttacking";
        public EntitySMTasks TaskName = EntitySMTasks.AttackTarget;
        public EntitySMTasks OnTargetDeath = EntitySMTasks.EatEntity;
        public EntitySMTasks OnSeperateTarget = EntitySMTasks.ChaseTarget;
        public EntitySMTasks OnLostTarget = EntitySMTasks.Idle;

        public object Clone() {
            return new AttackTargetSettings(){
                OnTargetDeath = OnTargetDeath,
                OnSeperateTarget = OnSeperateTarget,
                OnLostTarget = OnLostTarget,
                TaskName = TaskName
            };
        }
    }

    public class AttackTargetBehavior : IBehavior {
        private AttackTargetSettings settings;
        private Movement movement;

        private AttackBehavior attack;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target)) {
                manager.TaskTarget = Guid.Empty;
                manager.Transition(settings.OnLostTarget);
                return;
            }

            float preyDist = Recognition.GetColliderDist(self, target);
            if (preyDist > genetics.Genes.Get(attack.settings.AttackDistance)) {
                manager.Transition(settings.OnSeperateTarget);
                return;
            } if (!target.Is(out IAttackable atkTarget)) {
                manager.TaskTarget = Guid.Empty;
                manager.Transition(settings.OnLostTarget);
                return;
            }

            float3 atkDir = math.normalize(target.position - self.position); atkDir.y = 0;
            if (math.any(atkDir != 0)) self.collider.transform.rotation = Quaternion.RotateTowards(self.collider.transform.rotation,
            Quaternion.LookRotation(atkDir), movement.rotSpeed * EntityJob.cxt.deltaTime);

            if (atkTarget.IsDead) manager.Transition(settings.OnTargetDeath);
            else attack.Attack(target);
        } 

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Attack, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(AttackTargetSettings), new AttackTargetSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out attack))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have AttackBehavior");
            manager.RegisterAnimation(settings.TaskName, AttackTargetSettings.AnimationParam);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out attack))
                throw new System.Exception("Entity: AttackTarget Behavior Requires AnimalInstance to have AttackBehavior");
            manager.RegisterAnimation(settings.TaskName, AttackTargetSettings.AnimationParam);
        }
    }
}