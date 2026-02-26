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
        public Genetics.GeneFeature ChaseProbability = new () {mean = 0.1f, var = 0.75f, geneWeight = 0.05f};
        public object Clone() {
            return new ChaseFriendsSetting {
                TaskName = TaskName,
                SearchDistance = SearchDistance,
                ChaseProbability = ChaseProbability
            };
        }
    }

    public class ChaseFriendsBehavior : IBehavior {
        private ChaseFriendsSetting settings;
        private Movement movement;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;
        private RelationsBehavior relations;
        
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(self.settings.profile, ref path.pathFinder, ref self.collider,
                    genetics.Genes.Get(movement.walkSpeed), movement.rotSpeed,
                    movement.acceleration);
                return;
            }

            manager.Transition(settings.OnReachTransition);
        }

        private bool TransitionTo() {
            float searchRadius = genetics.Genes.Get(settings.SearchDistance);
            float prob = genetics.Genes.Get(settings.ChaseProbability);
            int PathDist = movement.pathDistance;
            (bool hasFriend, bool hasEnemy) = relations.TryFindBestRelations(self, searchRadius, out (Entity e, float p) friend, out (Entity e, float p) enemy);

            if (hasFriend) { //Friends are more important than enemies :)
                float chaseProb = 1 - math.exp(-friend.p * prob);
                if (self.random.NextFloat() > chaseProb) return false;
                
                int3 destination = (int3)math.round(friend.e.origin) - self.GCoord;
                byte[] nPath = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
                if (math.all(path.pathFinder.destination == self.GCoord)) return false;
            } else if (hasEnemy) {
                float avoidProb = 1 - math.exp(-enemy.p * prob);
                if (self.random.NextFloat() > avoidProb) return false;

                float3 aim = math.normalizesafe(self.GCoord - (int3)math.round(enemy.e.origin));
                byte[] nPath = PathFinder.FindPathAlongRay(self.GCoord, ref aim, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            } else return false;

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
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ChaseFriendsSetting.AnimationParam);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have RideableStateSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations))
                throw new System.Exception("Entity: ChaseFriends Behavior Requires AnimalInstance to have RelationsBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            manager.RegisterAnimation(settings.TaskName, ChaseFriendsSetting.AnimationParam);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
        
    }
}