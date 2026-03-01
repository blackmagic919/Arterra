using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class IdleStateSettings : IBehaviorSetting {
        public Genetics.GeneFeature AverageIdleTime;
        public Genetics.GeneFeature AverageIdleVariance;
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

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref AverageIdleTime);
            Genetics.AddGene(entityType, ref AverageIdleVariance);
        }
    }

    public class IdleStateBehavior : IBehavior {
        private IdleStateSettings settings;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (manager.TaskDuration <= 0) manager.Transition(settings.OnCompleteTransition);
            if (settings.CheckTransitions.value == null) return;
            foreach(EntitySMTasks tasks in settings.CheckTransitions.value) {
                if(manager.Transition(tasks)) return;
            }
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random,
                genetics.Genes.Get(settings.AverageIdleTime),
                genetics.Genes.Get(settings.AverageIdleVariance)
            );
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(IdleStateSettings), new IdleStateSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalSettings to have IdleStateSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have StateMachineManager");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalSettings to have IdleStateSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Idle Behavior Requires AnimalInstance to have StateMachineManager");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}