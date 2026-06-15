using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {

    public class ChaseFriendsSetting : IBehaviorSetting {
        public const string AnimationParam = "IsWalking";
        public EntitySMTasks TaskName = EntitySMTasks.ChaseFriends;
        public EntitySMTasks OnReachTransition = EntitySMTasks.RandomPath;
        //Scales with affinity; chance = 1 - e^(-affinity * chaseProbability)
        public float ChaseFriendProbability = 0.04f;
        public float FightEnemyAffection = -12.5f;
        public EntitySMTasks ChaseEnemyState = EntitySMTasks.ChaseTarget;
         public object Clone() {
            return new ChaseFriendsSetting {
                TaskName = TaskName,
                ChaseFriendProbability = ChaseFriendProbability,
                FightEnemyAffection = FightEnemyAffection
            };
        }
    }

    public class ChaseFriendsBehavior : SpeciesBehavior {
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

        private float ChaseFriendProbability => Modifier.Get(mod, MSettings.ChaseFriendProbability, settings.ChaseFriendProbability);
        private float FightEnemyAffection => Modifier.Get(mod, MSettings.FightEnemyAffection, settings.FightEnemyAffection);
        private float WalkSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.WalkSpeed, movement.walkSpeed);
        
        public override void Update(BehaviorEntity.Animal self) {
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

                self.PathCollider.Follow(self, Movement.DynamicDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider, friend.origin,
                    MMove.MovementType(mmove, settings.TaskName)
                ), WalkSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Walk);

                if (ColliderUpdateBehavior.GetColliderDist(self, friend) < manager.settings.ContactDistance)
                    path.pathFinder.hasPath = false;
            } else {
                self.PathCollider.Follow(self, Movement.StaticDirect(
                    MMove.Profile(mmove, settings.TaskName, self.settings), 
                    ref path.pathFinder, self.PathCollider,
                    MMove.MovementType(mmove, settings.TaskName)
                ), WalkSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Walk);
            }
        }

        private bool TransitionTo() {
            float searchRadius = movement.pathDistance;
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
                if(!path.FindPathOrApproachTarget(settings.TaskName, self.PathCoord, destination, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out byte[] nPath))
                    return false;
                path.SetPath(nPath);

                if (math.all(path.pathFinder.destination == self.PathCoord)) return false;
                return true;
            }

            bool TryToAvoidFriend(Entity enemy, float preference) {
                if (enemy.Is(out VitalityBehavior vit) && vit.IsDead) return false;
                if (TryAttackArchEnemy(enemy, preference)) return true;

                float avoidProb = 1 - math.exp(-preference * prob);
                if (self.random.NextFloat() > avoidProb) return false;

                float3 aim = math.normalizesafe(self.PathCoord - (int3)math.round(enemy.origin));
                if(!path.FindPathAlongRay(settings.TaskName, self.PathCoord, ref aim, PathDist + 1,
                    MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out byte[] nPath))
                    return false;
                path.SetPath(nPath);
                
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
        
        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Relations, heirarchy.Count);
            //Deactivated unless IAttackable is implemented
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseFriendsSetting), new ChaseFriendsSetting());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
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

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
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

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
        
    }
}