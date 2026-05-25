using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {

    public class ChaseFriendsSetting : IBehaviorSetting {
        public const string AnimationParam = "IsWalking";
        public EntitySMTasks TaskName = EntitySMTasks.ChaseFriends;
        public EntitySMTasks OnReachTransition = EntitySMTasks.RandomPath;
        public float SearchFriendDist = 30;
        //Scales with affinity; chance = 1 - e^(-affinity * chaseProbability)
        public float ChaseFriendProbability = 0.04f;
        public float FightEnemyAffection = -12.5f;
        public EntitySMTasks ChaseEnemyState = EntitySMTasks.ChaseTarget;
         public object Clone() {
            return new ChaseFriendsSetting {
                TaskName = TaskName,
                SearchFriendDist = SearchFriendDist,
                ChaseFriendProbability = ChaseFriendProbability,
                FightEnemyAffection = FightEnemyAffection
            };
        }
    }

    public class ChaseFriendsBehavior : ISpeciesBehavior {
        private ChaseFriendsSetting settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private Modifier mod;
        private RelationsBehavior relations;
        private RunFromPredatorBehavior predator;
        private bool IsFriend;

        private float SearchFriendDist => Modifier.Get(mod, MSettings.SearchFriendDist, settings.SearchFriendDist);
        private float ChaseFriendProbability => Modifier.Get(mod, MSettings.ChaseFriendProbability, settings.ChaseFriendProbability);
        private float FightEnemyAffection => Modifier.Get(mod, MSettings.FightEnemyAffection, settings.FightEnemyAffection);
        private float WalkSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.WalkSpeed, movement.walkSpeed);
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            
            if (!path.pathFinder.hasPath) {
                float taskDur = manager.TaskDuration;
                if (manager.Transition(settings.OnReachTransition)) {
                    //count the follow friends time the same as the outer task
                    if (manager.TaskIndex != EntitySMTasks.Idle)
                        manager.TaskDuration = taskDur;
                } return;
            }

            if (IsFriend) {
                if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity friend))
                    path.pathFinder.hasPath = false;

                self.PathCollider.Follow(Movement.DynamicDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider, friend.origin,
                    MMove.MovementType(mmove, settings.TaskName)
                ), WalkSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);

                if (ColliderUpdateBehavior.GetColliderDist(self, friend) < manager.settings.ContactDistance)
                    path.pathFinder.hasPath = false;
            } else {
                self.PathCollider.Follow(Movement.StaticDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.TaskName)
                ), WalkSpeed, movement.rotSpeed, movement.acceleration, self.DeltaTime);
            }
        }

        private bool TransitionTo() {
            float searchRadius = SearchFriendDist;
            float prob = ChaseFriendProbability;
            int PathDist = movement.pathDistance;
            (bool hasFriend, bool hasEnemy) = relations.TryFindBestRelations(self, searchRadius, out (Entity e, float p) friend, out (Entity e, float p) enemy);

            if (hasFriend && TryToFollowFriend(friend.e, friend.p)) { //Friends are more important than enemies :)
                manager.TaskTarget = friend.e.info.rtEntityId;
                IsFriend = true;
            } else if (hasEnemy && TryToAvoidFriend(enemy.e, enemy.p)) {
                manager.TaskTarget = friend.e.info.rtEntityId;
                IsFriend = false;
            } else return false;

            bool TryToFollowFriend(Entity friend, float preference) {
                float chaseProb = 1 - math.exp(-preference * prob);
                if (self.random.NextFloat() > chaseProb) return false;
                if (friend.Is(out VitalityBehavior vit) && vit.IsDead) return false; 
                
                int3 destination = (int3)math.round(friend.origin) - self.PathCoord;
                byte[] nPath = PathFinder.FindPathOrApproachTarget(self.PathCoord, destination, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
                if (math.all(path.pathFinder.destination == self.PathCoord)) return false;
                return true;
            }

            bool TryToAvoidFriend(Entity enemy, float preference) {
                if (enemy.Is(out VitalityBehavior vit) && vit.IsDead) return false;
                if (TryAttackArchEnemy(enemy, preference)) return true;

                float avoidProb = 1 - math.exp(-preference * prob);
                if (self.random.NextFloat() > avoidProb) return false;

                float3 aim = math.normalizesafe(self.PathCoord - (int3)math.round(enemy.origin));
                byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref aim, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
                if (math.all(path.pathFinder.destination == self.PathCoord)) return false;
                return true;
            }

            bool TryAttackArchEnemy(Entity enemy, float preference) {
                if (preference > FightEnemyAffection)
                    return false;
                if (predator != null && predator.settings.Recognize((int)enemy.info.entityType))
                    return false;
                if (manager.Transition(settings.ChaseEnemyState)) 
                    manager.TaskTarget = enemy.info.rtEntityId;
                return true;
            }

            return true;
        }
        
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Relations, heirarchy.Count);
            //Deactivated unless IAttackable is implemented
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseFriendsSetting), new ChaseFriendsSetting());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have RideableStateSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            if (!self.Is(out predator)) predator = null;
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have RideableStateSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            if (!self.Is(out predator)) predator = null;
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
        
    }
}