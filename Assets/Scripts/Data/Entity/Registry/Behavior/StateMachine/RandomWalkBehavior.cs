using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arterra.Configuration;
using Arterra.Utils;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class RandomWalkStateSettings : IBehaviorSetting {
        public const string AnimationParam = "IsWalking";
        public Genetics.GeneFeature AverageWalkTime;
        public Genetics.GeneFeature AveragWalkVariance;

        public EntitySMTasks TaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopWalkTransition = EntitySMTasks.RandomPath;
        public Option<List<EntitySMTasks> > OnSwitchPath;

        public object Clone() {
            return new RandomWalkStateSettings(){
                AverageWalkTime = AverageWalkTime,
                AveragWalkVariance = AveragWalkVariance,
                TaskName = TaskName,
                OnStopWalkTransition = OnStopWalkTransition,
                OnSwitchPath = OnSwitchPath
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref AverageWalkTime);
            Genetics.AddGene(entityType, ref AveragWalkVariance);   
        }
    }


    public class RandomWalkBehavior : IBehavior {
        private RandomWalkStateSettings settings;
        private Movement movement;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager; 
        private GeneticsBehavior genetics;
        private PathFinderBehavior path;

        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(self.settings.profile, ref path.pathFinder, ref self.collider,
                    genetics.Genes.Get(movement.walkSpeed), movement.rotSpeed,
                    movement.acceleration);
                return;
            }
            
            if (manager.TaskDuration <= 0) manager.Transition(settings.OnStopWalkTransition);
            else if (settings.OnSwitchPath.value != null) {
                foreach(var trans in settings.OnSwitchPath.value) 
                    if(manager.Transition(trans)) return;
            }

            int PathDist = movement.pathDistance;
            int3 dP = new(
                self.random.NextInt(-PathDist, PathDist),
                self.random.NextInt(-PathDist, PathDist),
                self.random.NextInt(-PathDist, PathDist)
            );

            if (PathFinder.VerifyProfile(self.GCoord + dP, self.settings.profile, EntityJob.cxt)) {
                byte[] nPath = PathFinder.FindPath(self.GCoord, dP, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            }
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random,
                genetics.Genes.Get(settings.AverageWalkTime),
                genetics.Genes.Get(settings.AveragWalkVariance)
            );
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RandomWalkStateSettings), new RandomWalkStateSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, RandomWalkStateSettings.AnimationParam);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, RandomWalkStateSettings.AnimationParam);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}