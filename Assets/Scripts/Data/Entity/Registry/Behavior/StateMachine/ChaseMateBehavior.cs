using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Editor;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ChaseMateStateSettings : IBehaviorSetting {
        public const string Animation1Param = null;
        public const string Animation2Param = "IsWalking";
        public const string Animation3Param = "IsCuddling";
        public EntitySMTasks Task1Name = EntitySMTasks.FindMate;
        public EntitySMTasks Task2Name = EntitySMTasks.ChaseMate;
        public EntitySMTasks Task3Name = EntitySMTasks.Reproduce;
        public EntitySMTasks OnNotFoundTransition = EntitySMTasks.RandomPath;
        public EntitySMTasks OnFinishMateTransition = EntitySMTasks.RandomPath;
        public Genetics.GeneFeature MateThreshold;
        public float PregnacyLength;

        public Genetics.GeneFeature SearchDistance;
        public Option<List<EntitySMTasks> > OnSwitchPath;

        public object Clone() {
            return new ChaseMateStateSettings(){
                Task1Name = Task1Name,
                Task2Name = Task2Name,
                Task3Name = Task3Name,
                OnNotFoundTransition = OnNotFoundTransition,
                OnFinishMateTransition = OnFinishMateTransition,
                OnSwitchPath = OnSwitchPath,
                PregnacyLength = PregnacyLength
            };
        }

        [Serializable]
        public struct Mate {
            [RegistryReference("Entities")]
            public string MateType;
            [RegistryReference("Entities")]
            public string ChildType;
            public Genetics.GeneFeature AmountPerParent;
            public float GeneMutationRate;
        }


        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref SearchDistance);
            Genetics.AddGene(entityType, ref MateThreshold);
        }
    }


    public class ChaseMateBehavior : IBehavior {
        private ChaseMateStateSettings settings;
        private Movement movement;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private GeneticsBehavior genetics;
        private VitalityBehavior vitality;
        private PathFinderBehavior path;
        private ReproductionBehavior reproduction;

        public bool BeginMate() => vitality.healthPercent >= genetics.Genes.Get(settings.MateThreshold);
        public bool StopMating() => vitality.healthPercent < genetics.Genes.Get(settings.MateThreshold);

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex == settings.Task1Name)
                FindMate();
            else if (manager.TaskIndex == settings.Task2Name)
                ChaseMate();
            else if (manager.TaskIndex == settings.Task3Name)
                Reproduce();
        }
        
        private void FindMate() {
            if (StopMating()|| !reproduction.settings.FindPreferredMate(self,
                genetics.Genes.Get(settings.SearchDistance), out Entity mate)
            ) {
                manager.Transition(settings.OnNotFoundTransition);
                return;
            } else if (settings.OnSwitchPath.value != null) {
                foreach(var trans in settings.OnSwitchPath.value) 
                    if(manager.Transition(trans)) return;
            }

            int PathDist = movement.pathDistance;
            int3 destination = (int3)math.round(mate.origin) - self.GCoord;
            byte[] nPath = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            manager.TaskIndex = settings.Task2Name;
        }

        private void ChaseMate() {//I feel you man
            if (!reproduction.settings.FindPreferredMate(self,
                genetics.Genes.Get(settings.SearchDistance), out Entity mate)
            ) {
                manager.Transition(settings.OnNotFoundTransition);
                return;
            }

            Movement.FollowDynamicPath(self.settings.profile, ref path.pathFinder, ref self.collider, mate.origin,
                genetics.Genes.Get(movement.walkSpeed), movement.rotSpeed, movement.acceleration);
            float mateDist = Recognition.GetColliderDist(self, mate);
            if (mateDist < manager.settings.ContactDistance) {
                EntityManager.AddHandlerEvent(() => mate.As<IMateable>().MateWith(self));
                reproduction.MateWith(mate);
                return;
            }
            if (!path.pathFinder.hasPath) {
                manager.TaskIndex = settings.Task1Name;
                return;
            }
        }


        private void Reproduce() {
            if (manager.TaskDuration > 0) return;
            manager.Transition(settings.OnFinishMateTransition);
        } 

        public bool TransitionTo() => BeginMate();
        private void TransitionToMate(object source, object target, object _) {
            manager.TaskDuration = settings.PregnacyLength;
            manager.TaskIndex = settings.Task3Name;
        }
        private void TestMate(object source, object target, object allow) {
            ((RefTuple<bool>)allow).Value &= manager.TaskIndex < settings.Task1Name;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Reproduction, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseMateStateSettings), new ChaseMateStateSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out reproduction))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have ReproductionBehavior");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have VitalityBehavior");
            
            manager.RegisterTransition(settings.Task1Name, TransitionTo);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_CanMate, TestMate);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Mate, TransitionToMate);
            manager.RegisterAnimation(settings.Task1Name, ChaseMateStateSettings.Animation1Param);
            manager.RegisterAnimation(settings.Task2Name, ChaseMateStateSettings.Animation2Param);
            manager.RegisterAnimation(settings.Task3Name, ChaseMateStateSettings.Animation3Param);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalSettings to have Movement");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out reproduction))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have ReproductionBehavior");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChaseMate Behavior Requires AnimalInstance to have VitalityBehavior");
            
            manager.RegisterTransition(settings.Task1Name, TransitionTo);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_CanMate, TestMate);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Mate, TransitionToMate);
            manager.RegisterAnimation(settings.Task1Name, ChaseMateStateSettings.Animation1Param);
            manager.RegisterAnimation(settings.Task2Name, ChaseMateStateSettings.Animation2Param);
            manager.RegisterAnimation(settings.Task3Name, ChaseMateStateSettings.Animation3Param);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}