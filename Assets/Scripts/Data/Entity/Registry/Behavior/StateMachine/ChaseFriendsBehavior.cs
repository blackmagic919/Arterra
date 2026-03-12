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
        public Genetics.GeneFeature SearchDistance = new () {mean = 30, var = 0.25f, geneWeight = 0.01f};
        //Scales with affinity; chance = 1 - e^(-affinity * chaseProbability)
        public Genetics.GeneFeature ChaseProbability = new () {mean = 0.04f, var = 0.75f, geneWeight = 0.05f};
        public Genetics.GeneFeature FightEnemyAffection = new () {mean = -12.5f, var = 0.25f, geneWeight = 0.025f};
        public EntitySMTasks ChaseEnemyState = EntitySMTasks.ChaseTarget;
         public object Clone() {
            return new ChaseFriendsSetting {
                TaskName = TaskName,
                SearchDistance = SearchDistance,
                ChaseProbability = ChaseProbability,
                FightEnemyAffection = FightEnemyAffection
            };
        }
    }

    public class ChaseFriendsBehavior : IBehavior {
        private ChaseFriendsSetting settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;
        private RelationsBehavior relations;
        private RunFromPredatorBehavior predator;
        private bool IsFriend;
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
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

                Movement.FollowDynamicPath(MMove.Profile(mmove, manager.TaskIndex, self.settings), 
                    ref path.pathFinder, self.PathCollider, friend.origin, 
                    MMove.Speed(mmove, manager.TaskIndex, genetics.Genes, movement.walkSpeed),
                    movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, manager.TaskIndex));
                
                if (ColliderUpdateBehavior.GetColliderDist(self, friend) < manager.settings.ContactDistance)
                    path.pathFinder.hasPath = false;
            } else {
                Movement.FollowStaticPath(MMove.Profile(mmove, manager.TaskIndex, self.settings),
                    ref path.pathFinder, self.PathCollider,
                    MMove.Speed(mmove, manager.TaskIndex, genetics.Genes, movement.walkSpeed),
                    movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, manager.TaskIndex));
            }
        }

        private bool TransitionTo() {
            float searchRadius = genetics.Genes.Get(settings.SearchDistance);
            float prob = genetics.Genes.Get(settings.ChaseProbability);
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
                if (preference > genetics.Genes.Get(settings.FightEnemyAffection))
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
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
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
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            if (!self.Is(out predator)) predator = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have RideableStateSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            if (!self.Is(out predator)) predator = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
        
    }
}