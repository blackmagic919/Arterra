
using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Data.Item;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class ConsumeEntitySettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.EatEntity;
        public EntitySMTasks OnFinishedEating = EntitySMTasks.Idle;
        public EntitySMTasks OnSeperateTarget = EntitySMTasks.ChaseTarget;
        public EntitySMTasks OnLostTarget = EntitySMTasks.Idle;

        public object Clone() {
            return new ConsumeEntitySettings(){
                OnFinishedEating = OnFinishedEating,
                OnSeperateTarget = OnSeperateTarget,
                OnLostTarget = OnLostTarget,
                TaskName = TaskName,
            };
        }
    }

    public class ConsumeEntityBehavior : IBehavior {
        public ConsumeEntitySettings settings;
        public ConsumeBehaviorSettings consume;
        private Movement movement;

        private StateMachineManagerBehavior manager;
        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target)) {
                manager.TaskTarget = Guid.Empty;
                manager.Transition(settings.OnLostTarget);
                return;
            }

            float preyDist = Recognition.GetColliderDist(self, target);
            if (preyDist > manager.settings.ContactDistance) {
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

            if (!atkTarget.IsDead) manager.Transition(settings.OnSeperateTarget);
            else ConsumeTarget(atkTarget, self);
        } 

        public void ConsumeTarget(IAttackable target, BehaviorEntity.Animal self) {
            EntityManager.AddHandlerEvent(() => {
                IItem item = target.Collect(self, consume.ConsumptionRate);
                if (item != null && consume.CanConsume(genetics.Genes, item, out float nutrition)) {
                    vitality.Heal(nutrition);
                }
                if (vitality.healthPercent >= 1) {
                    manager.Transition(settings.OnFinishedEating);
                }
            });
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ConsumeEntitySettings), new ConsumeEntitySettings());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have ConsumeEntitySettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have VitalityBehavior");
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have ConsumeEntitySettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ConsumeTarget Behavior Requires AnimalInstance to have VitalityBehavior");
        }
    }
}