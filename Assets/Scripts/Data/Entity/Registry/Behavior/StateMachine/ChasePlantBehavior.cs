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
        public float SearchPlantDistance;
        
        public object Clone() {
            return new ChasePlantSettings(){
                TaskName = TaskName,
                OnNotFoundTransition = OnNotFoundTransition,
                OnReachPreyTransition = OnReachPreyTransition,
                SearchPlantDistance = SearchPlantDistance
            };
        }
    }


    public class ChasePlantBehavior : ISpeciesBehavior {
        private BehaviorEntity.Animal self;
        private ChasePlantSettings settings;
        private FindPlantBehaviorSettings findPlant;
        private HuntBehaviorSettings hunt;
        private Movement movement;
        private MMove mmove; //optional

        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private VitalityBehavior vitality;
        private Modifier mod;
        public bool IsHunting;

        private float HuntThreshold => Modifier.Get(mod, MSettings.HuntThreshold, hunt.HuntThreshold);
        private float StopHuntThreshold => Modifier.Get(mod, MSettings.StopHuntThreshold, hunt.StopHuntThreshold);
        private int SearchPlantDist => Modifier.GetInt(mod, MSettings.SearchPlantDist, settings.SearchPlantDistance);
        public bool BeginHunting() => IsHunting || (IsHunting = vitality.healthPercent < HuntThreshold);
        public bool StopHunting() => !IsHunting || !(IsHunting = vitality.healthPercent < StopHuntThreshold);
        private float WalkSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.WalkSpeed, movement.walkSpeed);

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            self.PathCollider.Follow(Movement.StaticDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings), 
                ref path.pathFinder, self.PathCollider,
                MMove.MovementType(mmove, settings.TaskName)
            ), WalkSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
            if (path.pathFinder.hasPath) return;

            if (findPlant.FindPreferredPreyPlant((int3)math.round(self.position), SearchPlantDist, out int3 preyPos)
                && ColliderUpdateBehavior.GetColliderDist(self, preyPos)
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
                SearchPlantDist,
                out int3 preyPos
            )) return false;

            byte[] nPath = PathFinder.FindPathOrApproachTarget(self.PathCoord, preyPos - self.PathCoord, movement.pathDistance,
                MMove.Profile(mmove, settings.TaskName, self.settings),
                EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
            float dist = ColliderUpdateBehavior.GetColliderDist(self, preyPos);

            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(path.pathFinder.destination == self.PathCoord)) {
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
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
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
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePlant Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}