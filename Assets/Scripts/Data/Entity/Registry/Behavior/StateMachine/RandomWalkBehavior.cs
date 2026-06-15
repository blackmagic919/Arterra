using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class RandomWalkStateSettings : IBehaviorSetting {
        public float AverageWalkTime = 20.0f;
        public float AveragWalkVariance = 0.1f;

        public EntitySMTasks TaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopWalkTransition = EntitySMTasks.Idle;
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
    }


    public class RandomWalkBehavior : SpeciesBehavior {
        private RandomWalkStateSettings settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager; 
        private PathFinderBehavior path;
        private Modifier mod;

        private float WalkSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.WalkSpeed, movement.walkSpeed);
        private float AverageWalkTime => Modifier.Get(mod, MSettings.AverageWalkTime, settings.AverageWalkTime);
        private float AverageWalkVariance => Modifier.Get(mod, MSettings.AverageWalkVariance, settings.AveragWalkVariance);
        
        public override void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (path.pathFinder.hasPath) {
                self.PathCollider.Follow(self, Movement.StaticDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.TaskName)
                ), WalkSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Walk);
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

            EntitySetting.ProfileInfo profile = MMove.Profile(mmove, settings.TaskName, self.settings);
            if (PathFinderBehavior.VerifyProfile(self.PathCoord + dP, profile, EntityJob.cxt)) {
                if(!path.FindPath(settings.TaskName, self.PathCoord, dP, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings),
                    EntityJob.cxt, out byte[] nPath)) return;
                path.SetPath(nPath);
            }
        }

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random, AverageWalkTime, AverageWalkVariance
            );
            return true;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RandomWalkStateSettings), new RandomWalkStateSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RandomWalk Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}