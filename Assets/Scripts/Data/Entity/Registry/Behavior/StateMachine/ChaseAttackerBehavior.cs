using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
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

    public class ChaseAttackerBehavior : SpeciesBehavior {
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

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            if (manager.TaskIndex != settings.TaskName) return;
            
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                manager.TaskTarget = Guid.Empty;
            else if (ColliderUpdateBehavior.GetColliderDist(self, target) > ChaseDistance)
                manager.TaskTarget = Guid.Empty;
            if (manager.TaskTarget == Guid.Empty) {
                path.pathFinder.hasPath = false;
                manager.Transition(settings.OnLostAttacker);
                return;
            }

            // Reaching the target does not require a path or an available search budget.
            if (ColliderUpdateBehavior.GetColliderDist(self, target) < manager.settings.ContactDistance
                && manager.Transition(settings.OnReachAttacker)) {
                path.pathFinder.hasPath = false;
                return;
            }

            if (!path.pathFinder.hasPath) {
                int PathDist = movement.pathDistance;
                int3 pathOrigin = path.GetPathOrigin(settings.TaskName);
                int3 destination = (int3)math.round(target.origin) - pathOrigin;
                if(!path.FindPathOrApproachTarget(settings.TaskName, pathOrigin, destination, PathDist + 1,
                    MMove.Profile(mmove, manager.TaskIndex, self.settings), EntityJob.cxt, out byte[] nPath)) {
                    return; // Search is throttled; this is not an unreachable result.
                }
                path.SetPath(pathOrigin, nPath);
            }

            self.PathCollider.Follow(self, Movement.DynamicDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings), 
                ref path.pathFinder, self.PathCollider, target.origin,
                MMove.MovementType(mmove, settings.TaskName)
            ), RunSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Run);
        }

        private void RespondToAttack(object clrId, object atkrId) {
            if (atkrId is not Guid attackerId) return;
            if (!EntityManager.TryGetEntity(attackerId, out Entity attacker)) return;
            if (attacker.info.rtEntityId == self.info.rtEntityId) return;
            if (relations != null && relations.GetAffection(attacker.info.rtEntityId)
                > relations.settings.SuppressInstinctAffection)
                    return;
            int entityType = (int)attacker.info.entityType;
            if (prey != null && prey.Recognize(entityType))
                manager.Transition(settings.TaskName);
            else if (predator != null && predator.Recognize(entityType))
                return;
            else if (flee != null && flee.FightAggressor)
                manager.Transition(settings.TaskName);
            if (manager.TaskIndex == settings.TaskName) 
                manager.TaskTarget = attacker.info.rtEntityId;
        }


        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseAttackerSettings), new ChaseAttackerSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
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
            
            if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
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
            
            if (self.IsAuthority) self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}
