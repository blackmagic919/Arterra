using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class StepBackSetting : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.StepBackFromEntity;
        public EntitySMTasks TestProximityState = EntitySMTasks.AttackTarget;
        public EntitySMTasks OnLostTarget = EntitySMTasks.RandomPath;
        public EntitySMTasks OnSteppedBack = EntitySMTasks.ChaseTarget;
        public Genetics.GeneFeature BlindDist = new () {mean = 3, var = 0.25f, geneWeight = 0.025f};
        public int PathDist = 5;
        public object Clone() {
            return new StepBackSetting {
                TaskName = TaskName
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref BlindDist);
        }
    }

    public class StepBackBehavior : IBehavior {
        private StepBackSetting settings;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private PathFinderBehavior path;

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;

            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target)) {
                manager.Transition(settings.OnLostTarget);
                return;
            }

            if (ColliderUpdateBehavior.GetColliderDist(self, target) > genetics.Genes.Get(settings.BlindDist)) {
                manager.Transition(settings.OnSteppedBack);
                return;
            }

            if (!path.pathFinder.hasPath) {
                if (ColliderUpdateBehavior.GetColliderDist(self, target) > genetics.Genes.Get(settings.BlindDist)) {
                    manager.Transition(settings.OnSteppedBack);
                    return;
                }
                
                int PathDist = settings.PathDist;
                float3 rayDir = math.normalizesafe(self.PathCoord - target.position);
                byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref rayDir, PathDist + 1, 
                    MMove.Profile(mmove, settings.TaskName, self.settings),
                    EntityJob.cxt, out int pLen);
                path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
            }
            Movement.FollowStaticPath(self.settings.profile, ref path.pathFinder, self.PathCollider,
                MMove.Speed(mmove, settings.TaskName, genetics.Genes, movement.walkSpeed),
                movement.rotSpeed, movement.acceleration, 
                MMove.MovementType(mmove, settings.TaskName)
            );
        }

        public bool TransitionToAttack() {
            if (!EntityManager.TryGetEntity(manager.TaskTarget, out Entity target))
                return true;
            if (ColliderUpdateBehavior.GetColliderDist(target, self)  >= genetics.Genes.Get(settings.BlindDist))
                return true;
            manager.Transition(settings.TaskName);
            return false;
        }

        public void TestAttackAtDistance(object src, object trgt, object cxt) {
            if (trgt == null || src == null) return;
            Entity target = trgt as Entity;
            if (ColliderUpdateBehavior.GetColliderDist(self, target) >= genetics.Genes.Get(settings.BlindDist)) return;
            (cxt as RefTuple<(float dmg, float3 kb)>).Value.dmg = 0; //no damage if not at attack dist
            if (manager.TaskIndex == settings.TestProximityState) manager.Transition(settings.TaskName);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(StepBackSetting), new StepBackSetting());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalSettings to have StepBackSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have PathFinderBehavior");
            manager.RegisterTransition(settings.TestProximityState, TransitionToAttack);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, TestAttackAtDistance);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalSettings to have StepBackSettings");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: StepBack Behavior Requires AnimalInstance to have PathFinderBehavior");
            manager.RegisterTransition(settings.TestProximityState, TransitionToAttack);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, TestAttackAtDistance);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            self = null;
        }

    }
}