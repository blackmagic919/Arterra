using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arterra.Configuration;
using Arterra.Utils;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class RandomFlyStateSettings : IBehaviorSetting {
        public Genetics.GeneFeature AverageFlyTime = new(){mean = 75.0f, var = 0.5f, geneWeight = 0.1f };
        public Genetics.GeneFeature AveragFlyVariance = new(){mean = 0.1f, var = 0.5f, geneWeight = 0.1f };
        public Genetics.GeneFeature VerticalFreedom = new(){mean = 0.3f, var = 0.5f, geneWeight = 0.1f };

        public EntitySMTasks TaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopFlyTransition = EntitySMTasks.ApproachSurface;
        public Option<List<EntitySMTasks> > OnSwitchPath;

        public object Clone() {
            return new RandomFlyStateSettings(){
                AverageFlyTime = AverageFlyTime,
                AveragFlyVariance = AveragFlyVariance,
                TaskName = TaskName,
                OnStopFlyTransition = OnStopFlyTransition,
                OnSwitchPath = OnSwitchPath
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref AverageFlyTime);
            Genetics.AddGene(entityType, ref AveragFlyVariance);
            Genetics.AddGene(entityType, ref VerticalFreedom);
        }
    }


    public class RandomFlyBehavior : IBehavior {
        private RandomFlyStateSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager; 
        private GeneticsBehavior genetics;
        private PathFinderBehavior path;

        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(MMove.Profile(mmove, settings.TaskName, self.settings),
                    ref path.pathFinder, self.PathCollider,
                    MMove.Speed(mmove, settings.TaskName, genetics.Genes, movement.runSpeed),
                    movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, settings.TaskName));
                return;
            }
            
            if (manager.TaskDuration <= 0) manager.Transition(settings.OnStopFlyTransition);
            else if (settings.OnSwitchPath.value != null) {
                foreach(var trans in settings.OnSwitchPath.value) 
                    if(manager.Transition(trans)) return;
            }

            float3 flightDir = math.normalizesafe(Movement.RandomDirection(ref self.random));
            flightDir.y *= genetics.Genes.Get(settings.VerticalFreedom);

            byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref flightDir, movement.pathDistance,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random,
                genetics.Genes.Get(settings.AverageFlyTime),
                genetics.Genes.Get(settings.AveragFlyVariance)
            );
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RandomFlyStateSettings), new RandomFlyStateSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have RandomFlyState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have RandomFlyState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}