using System;
using System.Collections.Generic;
using Arterra.Data.Item;
using Arterra.Data.Structure;
using Arterra.Editor;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class ConsumeMaterialSettings : IBehaviorSetting {
        public const string AnimationParam = "IsEating";
        public EntitySMTasks TaskName = EntitySMTasks.EatPlant;
        public EntitySMTasks OnFinishedEating = EntitySMTasks.Idle;
        public EntitySMTasks OnLostTarget = EntitySMTasks.ChasePreyPlant;

        public object Clone() {
            return new ConsumeEntitySettings(){
                OnFinishedEating = OnFinishedEating,
                OnLostTarget = OnLostTarget,
                TaskName = TaskName,
            };
        }

        [Serializable]
        public struct Plant{
            [RegistryReference("Materials")]
            public string Material;
            [RegistryReference("Materials")]
            //If null, gradually removes it
            public string Replacement;
            public StructureData.CheckInfo Bounds;
        }

    }

    public class ConsumeMaterialBehavior : IBehavior {
        public ConsumeMaterialSettings settings;
        public ConsumeBehaviorSettings consume;
        private FindPlantBehaviorSettings findPlant;

        private StateMachineManagerBehavior manager;
        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!findPlant.CanConsume(manager.TaskPosition)) {
                manager.Transition(settings.OnLostTarget);
                return;
            }

            if (manager.TaskDuration > 0) return;

            IItem item = findPlant.ConsumePlant(self, manager.TaskPosition);
            if (item != null && consume.CanConsume(genetics.Genes, item, out float nutrition))
                vitality?.Heal(nutrition);
        } 

        public bool TransitionTo() {
            manager.TaskDuration = 1 / math.max(consume.ConsumptionRate, 0.0001f);
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ConsumeMaterialSettings), new ConsumeMaterialSettings());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
            heirarchy.TryAdd(typeof(FindPlantBehaviorSettings), new FindPlantBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have ConsumeEntitySettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!setting.Is(out findPlant))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have FindPlant");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have VitalityBehavior");
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ConsumeMaterialSettings.AnimationParam);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have ConsumeEntitySettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!setting.Is(out findPlant))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalSettings to have FindPlant");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ConsumeMaterial Behavior Requires AnimalInstance to have VitalityBehavior");
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ConsumeMaterialSettings.AnimationParam);
        }
    }
}