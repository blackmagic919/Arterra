using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ChaseAttackerSettings : IBehaviorSetting {
        public const string AnimationParam = "IsRunning";
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

        
        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                manager.TaskTarget = Guid.Empty;
            else if (Recognition.GetColliderDist(self, target)
                > genetics.Genes.Get(settings.ChaseDistance))
                manager.TaskTarget = Guid.Empty;
            if (manager.TaskTarget == Guid.Empty) {
                manager.Transition(settings.OnLostAttacker);
                return;
            }

            if (!path.pathFinder.hasPath) {
                int PathDist = movement.pathDistance;
                int3 destination = (int3)math.round(target.origin) - self.GCoord;
                byte[] nPath = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            }
            Movement.FollowDynamicPath(self.settings.profile, ref path.pathFinder, ref self.collider, target.origin,
                genetics.Genes.Get(movement.runSpeed), movement.rotSpeed, movement.acceleration);
            if (Recognition.GetColliderDist(self, target) < manager.settings.ContactDistance) {
                manager.Transition(settings.OnReachAttacker);
                return;
            }
        }

        private void RespondToAttack(object caller, object attacker) {
            if (attacker == null) return;
            if (attacker is not Entity entity) return;
            if (entity.info.entityId == self.info.entityId) return;
            if (prey != null && prey.Recognize((int)entity.info.entityType))
                manager.TaskIndex = settings.TaskName;
            else if (flee != null && flee.FightAggressor)
                manager.TaskIndex = settings.TaskName;
            if (manager.TaskIndex == settings.TaskName) 
                manager.TaskTarget = entity.info.entityId;
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
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            manager.RegisterAnimation(settings.TaskName, ChaseAttackerSettings.AnimationParam);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee)) flee = null;
            if (!setting.Is(out prey)) prey = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddContextlessEventHandler(Arterra.Core.Events.GameEvent.Entity_Damaged, RespondToAttack);
            manager.RegisterAnimation(settings.TaskName, ChaseAttackerSettings.AnimationParam);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}