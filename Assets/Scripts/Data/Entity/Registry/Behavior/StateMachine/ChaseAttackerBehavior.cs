using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ChaseAttackerSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.ChaseTarget;
        public EntitySMTasks OnLostAttacker = EntitySMTasks.Idle;
        public EntitySMTasks OnReachAttacker = EntitySMTasks.AttackTarget;
        public float ChaseDistance;

        public object Clone() {
            return new ChaseAttackerSettings {
                TaskName = this.TaskName,
                OnLostAttacker = this.OnLostAttacker,
                OnReachAttacker = this.OnReachAttacker,
                ChaseDistance = this.ChaseDistance
            };
        }
    }

    public class ChaseAttackerBehavior : ISpeciesBehavior {
        [JsonIgnore]
        public ChaseAttackerSettings settings;
        private RunFromPredatorSettings predator; //Optional
        private ChasePreySettings prey; //optional
        private FleeBehaviorSettings flee; //optional
        private Movement movement;
        private MMove mmove; //optional

        
        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private Modifier mod;
        private RelationsBehavior relations;

        public float ChaseDistance => Modifier.Get(mod, MSettings.ChaseDistance, settings.ChaseDistance);
        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                manager.TaskTarget = Guid.Empty;
            else if (ColliderUpdateBehavior.GetColliderDist(self, target) > ChaseDistance)
                manager.TaskTarget = Guid.Empty;
            if (manager.TaskTarget == Guid.Empty) {
                manager.Transition(settings.OnLostAttacker);
                return;
            }

            if (!path.pathFinder.hasPath) {
                int PathDist = movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.PathCoord;
                byte[] nPath = PathFinder.FindPathOrApproachTarget(self.PathCoord, destination, PathDist + 1,
                    MMove.Profile(mmove, manager.TaskIndex, self.settings), EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
            }

            self.PathCollider.Follow(Movement.DynamicDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings), 
                ref path.pathFinder, self.PathCollider, target.origin,
                MMove.MovementType(mmove, settings.TaskName)
            ), RunSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);

            if (ColliderUpdateBehavior.GetColliderDist(self, target) < manager.settings.ContactDistance) {
                manager.Transition(settings.OnReachAttacker);
                return;
            }
        }

        private void RespondToAttack(object caller, object attacker) {
            if (attacker == null) return;
            if (attacker is not Entity entity) return;
            if (entity.info.rtEntityId == self.info.rtEntityId) return;
            if (relations != null && relations.GetAffection(entity.info.rtEntityId)
                > relations.settings.SuppressInstinctAffection)
                    return;
            int entityType = (int)entity.info.entityType;
            if (prey != null && prey.Recognize(entityType))
                manager.Transition(settings.TaskName);
            else if (predator != null && predator.Recognize(entityType))
                return;
            else if (flee != null && flee.FightAggressor)
                manager.Transition(settings.TaskName);
            if (manager.TaskIndex == settings.TaskName) 
                manager.TaskTarget = entity.info.rtEntityId;
        }


        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseAttackerSettings), new ChaseAttackerSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee)) flee = null;
            if (!setting.Is(out predator)) predator = null;
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee)) flee = null;
            if (!setting.Is(out predator)) predator = null;
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}