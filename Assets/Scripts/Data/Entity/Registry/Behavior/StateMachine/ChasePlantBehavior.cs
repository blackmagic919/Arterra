using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ChasePlantSettings : IBehaviorSetting {
        public const string AnimationParam = "IsRunning";
        public EntitySMTasks TaskName = EntitySMTasks.ChasePreyPlant;
        public EntitySMTasks OnNotFoundTransition = EntitySMTasks.Idle;
        public EntitySMTasks OnReachPreyTransition = EntitySMTasks.EatPlant;
        public Genetics.GeneFeature SearchDistance;
        
        public object Clone() {
            return new ChasePlantSettings(){
                TaskName = TaskName,
                OnNotFoundTransition = OnNotFoundTransition,
                OnReachPreyTransition = OnReachPreyTransition,
                SearchDistance = SearchDistance
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref SearchDistance);
        }
    }


    public class ChasePlantBehavior : IBehavior {
        private BehaviorEntity.Animal self;
        private ChasePlantSettings settings;
        private FindPlantBehaviorSettings findPlant;
        private HuntBehaviorSettings hunt;
        private Movement movement;

        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        public bool IsHunting;

        public bool BeginHunting() => IsHunting || (IsHunting = vitality.healthPercent < genetics.Genes.Get(hunt.HuntThreshold));
        public bool StopHunting() => !IsHunting || !(IsHunting = vitality.healthPercent < genetics.Genes.Get(hunt.StopHuntThreshold));

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            
            Movement.FollowStaticPath(self.settings.profile, ref path.pathFinder, ref self.collider,
                genetics.Genes.Get(movement.walkSpeed), movement.rotSpeed,
                movement.acceleration);
            if (path.pathFinder.hasPath) return;

            if (findPlant.FindPreferredPreyPlant((int3)math.round(self.position), genetics.Genes.GetInt(
                settings.SearchDistance), out int3 preyPos)
                && Recognition.GetColliderDist(self, preyPos)
                <= manager.settings.ContactDistance
            ) {
                if(manager.Transition(settings.OnReachPreyTransition)) {
                    manager.TaskPosition = preyPos;
                    return;
                }
            } 
            if (!FindPrey()) manager.Transition(settings.OnNotFoundTransition);
        }

        public bool FindPrey() {
            if (StopHunting()) return false;
            if(!findPlant.FindPreferredPreyPlant(
                (int3)math.round(self.position),
                genetics.Genes.GetInt(settings.SearchDistance),
                out int3 preyPos
            )) return false;

            byte[] nPath = PathFinder.FindPathOrApproachTarget(self.GCoord, preyPos - self.GCoord, movement.pathDistance, self.settings.profile,
                EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            float dist = Recognition.GetColliderDist(self, preyPos);

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(path.pathFinder.destination == self.GCoord)) {
                if (dist <= manager.settings.ContactDistance && manager.Transition(settings.OnReachPreyTransition)) {
                    manager.TaskPosition = preyPos;
                } return false;
            } return true;
        }

        public bool TransitionTo() {
            if (!BeginHunting()) return false;
            return FindPrey();
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChasePlantSettings), new ChasePlantSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
            heirarchy.TryAdd(typeof(FindPlantBehaviorSettings), new FindPlantBehaviorSettings());
            heirarchy.TryAdd(typeof(HuntBehaviorSettings), new HuntBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out findPlant))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have FindPlantBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Hunt");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ChasePlantSettings.AnimationParam);
            IsHunting = false;
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out findPlant))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have FindPlantBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Hunt");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ChasePlantSettings.AnimationParam);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}