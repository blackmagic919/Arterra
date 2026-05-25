using System;
using Arterra.Data.Entity;
using Arterra.Core.Events;
using Newtonsoft.Json;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Arterra.Data.Entity.Behavior {
    public class RunFromAttackerSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.RunFromTarget;
        public EntitySMTasks OnLostAttacker = EntitySMTasks.Idle;

        public object Clone() {
            return new RunFromAttackerSettings {
                TaskName = this.TaskName,
                OnLostAttacker = this.OnLostAttacker,
            };
        }
    }

    public class RunFromAttackerBehavior : ISpeciesBehavior {
        [JsonIgnore]
        public RunFromAttackerSettings settings;
        private RunFromPredatorSettings predator; //Optional
        private ChasePreySettings prey; //optional
        private FleeBehaviorSettings flee;
        private Movement movement;
        private MMove mmove; //optional
        

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private RelationsBehavior relations;
        private Modifier mod;
        private float SearchEnemyDist => Modifier.Get(mod, MSettings.SearchEnemyDist, flee.detectDist);
        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                manager.TaskTarget = Guid.Empty;
            else if (ColliderUpdateBehavior.GetColliderDist(self, target) > SearchEnemyDist)
                manager.TaskTarget = Guid.Empty;
            if (manager.TaskTarget == Guid.Empty) {
                manager.Transition(settings.OnLostAttacker);
                return;
            }

            if (!path.pathFinder.hasPath) {
                int PathDist = flee.fleeDist;
                float3 rayDir = self.position - target.position;
                byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref rayDir, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
            }
            self.PathCollider.Follow(Movement.StaticDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings), 
                ref path.pathFinder, self.PathCollider,
                MMove.MovementType(mmove, settings.TaskName)
            ), RunSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
        }

        private void RespondToAttack(object caller, object attacker) {
            if (attacker == null) return;
            if (attacker is not Entity entity) return;
            if (entity.info.rtEntityId == self.info.rtEntityId) return;
            if (relations != null && relations.GetAffection(entity.info.rtEntityId)
                > relations.settings.SuppressInstinctAffection)
                return;
            int entityType = (int)entity.info.entityType;
            if (predator != null && predator.Recognize(entityType))
                manager.Transition(settings.TaskName);
            else if (prey != null && prey.Recognize(entityType))
                return;
            else if (!flee.FightAggressor)
                manager.Transition(settings.TaskName);
            if (manager.TaskIndex == settings.TaskName)
                manager.TaskTarget = entity.info.rtEntityId;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
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
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
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
            if (!setting.Is(out prey)) prey = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromAttacker Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            self.eventCtrl.AddContextlessEventHandler(GameEvent.Entity_Damaged, RespondToAttack);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}