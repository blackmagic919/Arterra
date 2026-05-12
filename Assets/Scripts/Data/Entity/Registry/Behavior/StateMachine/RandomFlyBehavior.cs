using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class RandomFlyStateSettings : IBehaviorSetting {
        public float AverageFlightTime = 75.0f;
        public float AverageFlightVariance = 0.1f;
        public float VerticalFlightFreedom = 0.3f;

        public EntitySMTasks TaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopFlyTransition = EntitySMTasks.ApproachSurface;
        public Option<List<EntitySMTasks> > OnSwitchPath;

        public object Clone() {
            return new RandomFlyStateSettings(){
                AverageFlightTime = AverageFlightTime,
                AverageFlightVariance = AverageFlightVariance,
                TaskName = TaskName,
                OnStopFlyTransition = OnStopFlyTransition,
                OnSwitchPath = OnSwitchPath
            };
        }
    }


    public class RandomFlyBehavior : IBehavior {
        private RandomFlyStateSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager; 
        private PathFinderBehavior path;
        private Modifier mod;

        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);
        private float AverageFlightTime => Modifier.Get(mod, MSettings.AverageFlightTime, settings.AverageFlightTime);
        private float AverageFlightVariance => Modifier.Get(mod, MSettings.AverageFlightVariance, settings.AverageFlightVariance);
        private float VerticalFlightFreedom => Modifier.Get(mod, MSettings.VerticalFlightFreedom, settings.VerticalFlightFreedom);

        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;

            if (path.pathFinder.hasPath) {
                self.PathCollider.Follow(Movement.StaticDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.TaskName)
                ), RunSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
                return;
            }
            
            if (manager.TaskDuration <= 0) manager.Transition(settings.OnStopFlyTransition);
            else if (settings.OnSwitchPath.value != null) {
                foreach(var trans in settings.OnSwitchPath.value) 
                    if(manager.Transition(trans)) return;
            }

            float3 flightDir = math.normalizesafe(Movement.RandomDirection(ref self.random));
            flightDir.y *= VerticalFlightFreedom;

            byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref flightDir, movement.pathDistance,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random, AverageFlightTime, AverageFlightVariance
            );
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
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
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomFly Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
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
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}