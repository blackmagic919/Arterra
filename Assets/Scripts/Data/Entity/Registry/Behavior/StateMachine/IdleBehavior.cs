using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class IdleStateSettings : IBehaviorSetting {
        public float AverageIdleTime;
        public float AverageIdleVariance;
        public EntitySMTasks TaskName = EntitySMTasks.Idle;
        public EntitySMTasks OnCompleteTransition = EntitySMTasks.RandomPath;
        public Option<List<EntitySMTasks> > CheckTransitions;

        public object Clone() {
            return new IdleStateSettings(){
                AverageIdleTime = AverageIdleTime,
                AverageIdleVariance = AverageIdleVariance,
                TaskName = TaskName,
                OnCompleteTransition = OnCompleteTransition,
                CheckTransitions = CheckTransitions
            };
        }
    }

    public class IdleStateBehavior : SpeciesBehavior {
        private IdleStateSettings settings;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private Modifier mod;

        private float AverageIdleTime => Modifier.Get(mod, MSettings.AverageIdleTime, settings.AverageIdleTime);
        private float AverageIdleVariance => Modifier.Get(mod, MSettings.AverageIdleVariance, settings.AverageIdleVariance);
        public override void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (manager.TaskDuration <= 0) manager.Transition(settings.OnCompleteTransition);
            if (settings.CheckTransitions.value == null) return;
            foreach(EntitySMTasks tasks in settings.CheckTransitions.value) {
                if(manager.Transition(tasks)) return;
            }
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(self.random, AverageIdleTime, AverageIdleVariance);
            return true;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(IdleStateSettings), new IdleStateSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalSettings to have IdleStateSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalSettings to have IdleStateSettings");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}