using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class RandomFlyStateSettings : IBehaviorSetting {
        public float AverageFlightTime = 75.0f;
        public float AverageFlightVariance = 0.1f;
        public float VerticalFlightFreedom = 0.3f;

        public EntitySMTasks TaskName = EntitySMTasks.DecidePath;
        public EntitySMTasks FollowTaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopFlyTransition = EntitySMTasks.ApproachSurface;
        public Option<List<EntitySMTasks> > OnSwitchPath;

        public object Clone() {
            return new RandomFlyStateSettings(){
                AverageFlightTime = AverageFlightTime,
                AverageFlightVariance = AverageFlightVariance,
                TaskName = TaskName,
                FollowTaskName = FollowTaskName,
                OnStopFlyTransition = OnStopFlyTransition,
                OnSwitchPath = OnSwitchPath
            };
        }
    }


    public class RandomFlyBehavior : SpeciesBehavior {
        private RandomFlyStateSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager; 
        private PathFinderBehavior path;
        private Modifier mod;

        private float RunSpeed => MMove.Speed(mmove, settings.FollowTaskName, mod, MSettings.RunSpeed, movement.runSpeed);
        private float AverageFlightTime => Modifier.Get(mod, MSettings.AverageFlightTime, settings.AverageFlightTime);
        private float AverageFlightVariance => Modifier.Get(mod, MSettings.AverageFlightVariance, settings.AverageFlightVariance);
        private float VerticalFlightFreedom => Modifier.Get(mod, MSettings.VerticalFlightFreedom, settings.VerticalFlightFreedom);

        
        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            if (manager.TaskIndex == settings.FollowTaskName) {
                FollowPath();
                return;
            }
            if (manager.TaskIndex != settings.TaskName) return;

            if (manager.TaskDuration <= 0) {
                if (manager.Transition(settings.OnStopFlyTransition)) return;
            } else if (settings.OnSwitchPath.value != null) {
                foreach (var transition in settings.OnSwitchPath.value) {
                    if (manager.Transition(transition)) return;
                }
            }

            float3 flightDir = math.normalizesafe(Movement.RandomDirection(ref self.random));
            flightDir.y *= VerticalFlightFreedom;

            int3 pathOrigin = path.GetPathOrigin(settings.TaskName);
            if(!path.FindPathAlongRay(settings.TaskName, pathOrigin, ref flightDir, movement.pathDistance,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out PathFinderBehavior.PathData nPath))
                return;
            path.SetPath(pathOrigin, nPath);
            if (path.pathFinder.hasPath) manager.Transition(settings.FollowTaskName);
        }

        private void FollowPath() {
            if (path.pathFinder.hasPath) {
                Movement.FollowStatic(self, self.PathCollider, ref path.pathFinder,
                    MMove.Profile(mmove, settings.FollowTaskName, self.settings),
                    RunSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Run,
                    MMove.MovementType(mmove, settings.FollowTaskName)
                );
            }

            if (!path.pathFinder.hasPath) manager.Transition(settings.TaskName);
        }

        private bool TransitionTo() {
            // Switching between deciding and following keeps the same wandering interval.
            if (manager.TaskIndex == settings.FollowTaskName || manager.TaskIndex == settings.TaskName) return true;
            path.pathFinder.hasPath = false;
            manager.TaskDuration = (float)CustomUtility.Sample(
                ref self.random, AverageFlightTime, AverageFlightVariance
            );
            return true;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RandomFlyStateSettings), new RandomFlyStateSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have RandomFlyState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.FollowTaskName, TransitionTo);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have RandomFlyState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterTransition(settings.FollowTaskName, TransitionTo);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}