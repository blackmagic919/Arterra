using System;
using Arterra.Data.Entity;
using Arterra.Core.Events;
using Newtonsoft.Json;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Arterra.Data.Entity.Behavior {
    public class RunFromAttackerSettings : IBehaviorSetting {
        public const string AnimationParam = "IsRunning";
        public EntitySMTasks TaskName = EntitySMTasks.RunFromTarget;
        public EntitySMTasks OverridableStates = EntitySMTasks.AttackTarget;
        public EntitySMTasks OnLostAttacker = EntitySMTasks.Idle;

        public object Clone() {
            return new RunFromAttackerSettings {
                TaskName = this.TaskName,
                OnLostAttacker = this.OnLostAttacker,
            };
        }
    }

    public class RunFromAttackerBehavior : IBehavior {
        [JsonIgnore]
        public RunFromAttackerSettings settings;
        private RunFromPredatorSettings predator; //Optional
        private FleeBehaviorSettings flee;
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
                > genetics.Genes.Get(flee.detectDist))
                manager.TaskTarget = Guid.Empty;
            if (manager.TaskTarget == Guid.Empty) {
                manager.Transition(settings.OnLostAttacker);
                return;
            }

            if (!path.pathFinder.hasPath) {
                int PathDist = flee.fleeDist;
                float3 rayDir = self.position - target.position;
                byte[] nPath = PathFinder.FindPathAlongRay(self.GCoord, ref rayDir, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref path.pathFinder, ref self.collider,
                genetics.Genes.Get(movement.runSpeed), movement.rotSpeed,
                movement.acceleration);
        }

        private void RespondToAttack(object caller, object attacker) {
            if (attacker == null) return;
            if (attacker is not Entity entity) return;
            if (entity.info.entityId == self.info.entityId) return;
            if (predator != null && predator.Recognize((int)entity.info.entityType))
                manager.TaskIndex = settings.TaskName;
            else if (!flee.FightAggressor)
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
            heirarchy.TryAdd(typeof(RunFromAttackerSettings), new RunFromAttackerSettings());
            heirarchy.TryAdd(typeof(FleeBehaviorSettings), new FleeBehaviorSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Flee");
            if (!setting.Is(out predator)) predator = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
            manager.RegisterAnimation(settings.TaskName, RunFromAttackerSettings.AnimationParam);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out flee))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalSettings to have Flee");
            if (!setting.Is(out predator)) predator = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            
            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
            manager.RegisterAnimation(settings.TaskName, RunFromAttackerSettings.AnimationParam);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}