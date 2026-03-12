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
        public Genetics.GeneFeature ChaseDistance;

        public object Clone() {
            return new ChaseAttackerSettings {
                TaskName = this.TaskName,
                OnLostAttacker = this.OnLostAttacker,
                OnReachAttacker = this.OnReachAttacker,
                ChaseDistance = this.ChaseDistance
            };
        }
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref ChaseDistance);
        }
    }

    public class ChaseAttackerBehavior : IBehavior {
        [JsonIgnore]
        public ChaseAttackerSettings settings;
        private ChasePreySettings prey; //optional
        private FleeBehaviorSettings flee; //optional
        private Movement movement;
        private MMove mmove; //optional

        
        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;
        private RelationsBehavior relations;

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                manager.TaskTarget = Guid.Empty;
            else if (ColliderUpdateBehavior.GetColliderDist(self, target)
                > genetics.Genes.Get(settings.ChaseDistance))
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
            Movement.FollowDynamicPath(MMove.Profile(mmove, manager.TaskIndex, self.settings),
                ref path.pathFinder, self.PathCollider, target.origin,
                MMove.Speed(mmove, manager.TaskIndex, genetics.Genes, movement.runSpeed),
                movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, manager.TaskIndex)
            );

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
            if (prey != null && prey.Recognize((int)entity.info.entityType))
                manager.Transition(settings.TaskName);
            else if (flee != null && flee.FightAggressor)
                manager.Transition(settings.TaskName);
            if (manager.TaskIndex == settings.TaskName) 
                manager.TaskTarget = entity.info.rtEntityId;
        }


        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
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
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee)) flee = null;
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}