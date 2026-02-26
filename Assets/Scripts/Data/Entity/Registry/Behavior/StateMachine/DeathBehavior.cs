

using System;
using System.Collections.Generic;
using Arterra.Data.Item;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class DeathSettings : IBehaviorSetting {
        public const string AnimationParam = "IsDead";
        public EntitySMTasks TaskName = EntitySMTasks.Death;
        public EntitySMTasks OnReviveTask = EntitySMTasks.Idle;
        public Genetics.GeneFeature DecompositionTime; //~300 seconds

        public object Clone() {
            return new DeathSettings {
                TaskName = TaskName,
                OnReviveTask = OnReviveTask,
                DecompositionTime = DecompositionTime
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting settings) {
            Genetics.AddGene(entityType, ref DecompositionTime);
        }
    }
    public class DeathBehavior : IBehavior {
        [JsonIgnore]
        public DeathSettings settings;

        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private VitalityBehavior vitality;
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) {
                if (!vitality.IsDead) return;
                manager.TaskDuration = genetics.Genes.Get(settings.DecompositionTime);
                manager.TaskIndex = settings.TaskName;
            };
            if (!vitality.IsDead) { //Bring back from the dead 
                manager.Transition(settings.OnReviveTask);
                return;
            }
            //Kill the entity
            if (manager.TaskDuration <= 0) EntityManager.ReleaseEntity(self.info.entityId);
        }

        private void OnCollectedFrom(object self, object collector, object cxt) {
            if (manager.TaskIndex != settings.TaskName) return;
            IItem item; float amount;
            (item, amount) = ((IItem, float))cxt;
            manager.TaskDuration -= amount;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(DeathSettings), new DeathSettings());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
            heirarchy.TryAdd(typeof(FindPlantBehaviorSettings), new FindPlantBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Death Behavior Requires AnimalSettings to have DeathSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Collect, OnCollectedFrom);
            manager.RegisterAnimation(settings.TaskName, DeathSettings.AnimationParam);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Death Behavior Requires AnimalSettings to have DeathSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Collect, OnCollectedFrom);
            manager.RegisterAnimation(settings.TaskName, DeathSettings.AnimationParam);
        }
    }
}