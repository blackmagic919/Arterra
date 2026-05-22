

using System;
using System.Collections.Generic;
using Arterra.Data.Item;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class DeathSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.Death;
        public EntitySMTasks OnReviveTask = EntitySMTasks.Idle;
        public float DecompositionTime; //~300 seconds

        public object Clone() {
            return new DeathSettings {
                TaskName = TaskName,
                OnReviveTask = OnReviveTask,
                DecompositionTime = DecompositionTime
            };
        }
    }
    public class DeathBehavior : IBehavior {
        [JsonIgnore]
        public DeathSettings settings;

        private StateMachineManagerBehavior manager;
        private VitalityBehavior vitality;
        private Modifier mod;

        [JsonIgnore] public float DecompositionTime => Modifier.Get(mod, MSettings.DecompositionTime, settings.DecompositionTime);

        public void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (manager.TaskIndex != settings.TaskName) {
                if (!vitality.IsDead) return;
                manager.TaskDuration = DecompositionTime;
                manager.Transition(settings.TaskName);
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
            Action<IItem> collect; float amount;
            (collect, amount) = ((Action<IItem>, float))cxt;
            manager.TaskDuration -= amount;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(DeathSettings), new DeathSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Death Behavior Requires AnimalSettings to have DeathSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Collect, OnCollectedFrom);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Death Behavior Requires AnimalSettings to have DeathSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Death Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_Collect, OnCollectedFrom);
        }
    }
}