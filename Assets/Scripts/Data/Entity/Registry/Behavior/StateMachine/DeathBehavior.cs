using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class DeathSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.Death;
        public EntitySMTasks OnReviveTask = EntitySMTasks.Idle;

        public object Clone() {
            return new DeathSettings {
                TaskName = TaskName,
                OnReviveTask = OnReviveTask,
            };
        }
    }
    public class DeathBehavior : SpeciesBehavior {
        [JsonIgnore]
        public DeathSettings settings;

        private StateMachineManagerBehavior manager;
        private VitalityBehavior vitality;
        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            
            if (vitality.IsDead) {
                if (manager.TaskIndex != settings.TaskName) manager.Transition(settings.TaskName);
            } else if (manager.TaskIndex == settings.TaskName) manager.Transition(settings.OnReviveTask);
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.TimedDecay, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(DeathSettings), new DeathSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) => Bind(self, setting);
        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) => Bind(self, setting);

        private void Bind(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: DeathBehavior requires DeathSettings");
            if (!self.Is(out manager))
                throw new Exception("Entity: DeathBehavior requires StateMachineManagerBehavior");
            if (!self.Is(out vitality))
                throw new Exception("Entity: DeathBehavior requires VitalityBehavior");
        }
    }
}
