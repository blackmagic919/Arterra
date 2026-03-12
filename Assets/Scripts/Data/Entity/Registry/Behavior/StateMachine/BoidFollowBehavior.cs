using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Editor;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {

    public class BoidFollowSetting : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.RandomPath;
        public EntitySMTasks OnStopWalkTransition = EntitySMTasks.Idle;
        public EntitySMTasks OnPackAttackTransition = EntitySMTasks.ChaseTarget;
        public Option<List<EntitySMTasks> > OnSwitchPath;
        public Genetics.GeneFeature AverageFlightTime = new () {mean = 25, geneWeight = 0.1f, var = 0.5f}; //120
        public Genetics.GeneFeature AveragFlightVariance = new () {mean = 0.05f, geneWeight = 0.05f, var = 0.5f};
        public Genetics.GeneFeature SeperationWeight = new () {mean = 0.75f, geneWeight = 0.05f, var = 0.5f}; //0.75
        public Genetics.GeneFeature AlignmentWeight = new () {mean = 0.4f, geneWeight = 0.05f, var = 0.5f}; //0.5
        public Genetics.GeneFeature CohesionWeight = new () {mean = 0.3f, geneWeight = 0.05f, var = 0.75f}; //0.25
        public Genetics.GeneFeature SightDistance = new() {mean = 12, geneWeight = 0.1f, var = 0.3f};
        public float3 DirectionBias = float3.zero;
        [TagOrRegistryReference("Entities")]
        public TagOrRegistryReference FlockEntity;
        public Option<List<EntitySMTasks>> LeadTargetStates = new () {value = new () {
            EntitySMTasks.ChaseTarget, EntitySMTasks.ChasePreyEntity,
            EntitySMTasks.AttackTarget
        }};

        public Option<List<EntitySMTasks>> LeadBoidStates = new () {value = new () {
            EntitySMTasks.ChaseTarget, EntitySMTasks.AttackTarget,
            EntitySMTasks.ChasePreyPlant, EntitySMTasks.ChasePreyEntity,
            EntitySMTasks.AttackTarget, EntitySMTasks.FollowRider,
            EntitySMTasks.RunFromPredator, EntitySMTasks.RandomPath
        }};

        [JsonIgnore][UISetting(Ignore = true)][HideInInspector]
        public HashSet<EntitySMTasks> _LeadTargetStates;
        [JsonIgnore][UISetting(Ignore = true)][HideInInspector]
        public HashSet<EntitySMTasks> _LeadBoidStates;
        public int MaxSwarmSize = 8; //8
        public int PathDist = 3; //3
        
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref AverageFlightTime);
            Genetics.AddGene(entityType, ref AveragFlightVariance);
            Genetics.AddGene(entityType, ref SeperationWeight);
            Genetics.AddGene(entityType, ref AlignmentWeight);
            Genetics.AddGene(entityType, ref CohesionWeight);
            Genetics.AddGene(entityType, ref SightDistance);
            _LeadTargetStates = new HashSet<EntitySMTasks>(LeadTargetStates.value);
            _LeadBoidStates = new HashSet<EntitySMTasks>(LeadBoidStates.value);
        }
        public object Clone() {
            return new BoidFollowSetting {
                TaskName = TaskName,
                OnStopWalkTransition = OnStopWalkTransition,
                OnPackAttackTransition = OnPackAttackTransition,
                OnSwitchPath = OnSwitchPath,
                AverageFlightTime = AverageFlightTime,
                AveragFlightVariance = AveragFlightVariance,
                SeperationWeight = SeperationWeight,
                AlignmentWeight = AlignmentWeight,
                CohesionWeight = CohesionWeight,
                SightDistance = SightDistance,
                MaxSwarmSize = MaxSwarmSize,
                PathDist = PathDist,
                DirectionBias = DirectionBias
            };
        }

        public void CalculateBoidDirection(Entity self, Genetics genes, RelationsBehavior relations = null) {
            BoidDMtrx boidDMtrx = new() {
                SeperationDir = float3.zero,
                AlignmentDir = float3.zero,
                CohesionDir = float3.zero,
                weight = 0, count = 0, scount = 0,
            };

            float sightDist = genes.Get(SightDistance);
            float PackTargetWeight = 0;
            Guid PackTarget = Guid.Empty;
            Bounds seperation = new (self.transform.position, self.transform.size * 2.0f);
            var eReg = Config.CURRENT.Generation.Entities;
            void OnEntityFound(Entity nEntity) {
                if (nEntity == null) return;
                if (!FlockEntity.Is(nEntity, eReg)) return;
                if (!nEntity.Is(out IBoid nBoid)) return;
                float3 nBoidPos = nEntity.transform.position;
                float3 boidPos = self.transform.position;

                if (!nBoid.IsPartOfPack()) return;
                
                float affection = relations != null ? relations.GetAffection(nEntity.info.rtEntityId) + 1 : 1.0f;
                if (affection < 1) return; //meaning we are enemies with this boid (no follow)

                float3 offset = math.normalizesafe(nBoidPos - boidPos);
                float dist = math.length(offset);
                float weight = affection * math.exp(-(dist/sightDist));

                if (nBoid.HasPackTarget(out PackTarget) && weight > PackTargetWeight)
                    PackTargetWeight = weight;

                if (seperation.Contains(nBoidPos)) {
                    boidDMtrx.SeperationDir += -offset;
                    boidDMtrx.scount++;
                }
                
                boidDMtrx.AlignmentDir += nBoid.MoveDirection * weight;
                boidDMtrx.CohesionDir += offset * weight;
                boidDMtrx.weight += weight;
                boidDMtrx.count++;
            }

            EntityManager.ESTree.Query(new(self.origin,
                2 * new float3(sightDist)),
            OnEntityFound);

            if (boidDMtrx.weight == 0) return;
            if (boidDMtrx.scount > 0) boidDMtrx.SeperationDir /= boidDMtrx.scount;
            float3 influenceDir = float3.zero;
            IBoid boidSelf = self.As<IBoid>();

            if (PackTargetWeight > 0) boidSelf.SetPackTarget(PackTarget);
            else if (boidDMtrx.count > MaxSwarmSize) //the sign of seperation is flipped for this case
                influenceDir = genes.Get(SeperationWeight) * boidDMtrx.SeperationDir -
                genes.Get(CohesionWeight) * (boidDMtrx.CohesionDir / boidDMtrx.weight);
            else influenceDir = genes.Get(SeperationWeight) * boidDMtrx.SeperationDir  +
                genes.Get(AlignmentWeight) * (boidDMtrx.AlignmentDir / boidDMtrx.weight - boidSelf.MoveDirection) +
                genes.Get(CohesionWeight) * (boidDMtrx.CohesionDir / boidDMtrx.weight);
            boidSelf.MoveDirection = math.normalizesafe(boidSelf.MoveDirection + influenceDir);
        }


        struct BoidDMtrx {
            public float3 SeperationDir;
            public float3 AlignmentDir;
            public float3 CohesionDir;
            public float weight;
            public int count;
            public int scount;
        }

        public interface IBoid {
            public float3 MoveDirection{get; set;}
            public bool IsPartOfPack();
            public bool HasPackTarget(out Guid target);
            public void SetPackTarget(Guid Target);
        }

    }
    public class BoidFollowBehavior : IBehavior, BoidFollowSetting.IBoid {
        private BoidFollowSetting settings;
        private Movement movement;
        private MMove mmove; //optional

        
        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;
        private RelationsBehavior relations;

        public float3 moveDirection;
        public float3 MoveDirection{ get => moveDirection; set => moveDirection = value; }
        public bool HasPackTarget(out Guid target) {
            if (settings._LeadTargetStates.Contains(manager.TaskIndex)) {
                target = manager.TaskTarget;
                return true;
            }

            target = default;
            return false;
        }

        public void SetPackTarget(Guid target) {
            if (manager.TaskIndex != settings.TaskName) return;
            if(manager.Transition(settings.OnPackAttackTransition))
                manager.TaskTarget = target;
        }

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) {
                ReflectPathMoveDir();
                return;
            }

            settings.CalculateBoidDirection(self, genetics.Genes, relations);
            if (path.pathFinder.hasPath) {
                Movement.FollowStaticPath(MMove.Profile(mmove, manager.TaskIndex, self.settings),
                    ref path.pathFinder, self.PathCollider,
                    MMove.Speed(mmove, manager.TaskIndex, genetics.Genes, movement.walkSpeed),
                    movement.rotSpeed, movement.acceleration,
                    MMove.MovementType(mmove, manager.TaskIndex)); 
                return;  
            }
            
            byte[] nPath = PathFinder.FindPathAlongRay(
                self.PathCoord, ref moveDirection, settings.PathDist,
                MMove.Profile(mmove, manager.TaskIndex, self.settings), 
                EntityJob.cxt, out int pLen
            );

            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);

            // Blend flocking influences with pathfinding direction
            float3 pathFindDir = math.normalizesafe(path.pathFinder.destination - self.origin);
            moveDirection = math.normalizesafe(pathFindDir + settings.DirectionBias);
            if(MMove.MovementType(mmove, manager.TaskIndex) == Movement.FollowType.Planar)
                moveDirection.y = 0;

            if (manager.TaskDuration <= 0) manager.Transition(settings.OnStopWalkTransition);
            else if (settings.OnSwitchPath.value != null) {
                foreach(var trans in settings.OnSwitchPath.value) 
                    if(manager.Transition(trans)) return;
            }
        }

        private void ReflectPathMoveDir() {
            if (!settings._LeadBoidStates.Contains(manager.TaskIndex))
                return;
            if (!path.pathFinder.hasPath) return;
            moveDirection = math.normalizesafe(path.pathFinder.destination - self.position);
        }


        public bool IsPartOfPack() => settings._LeadBoidStates.Contains(manager.TaskIndex);

        private bool TransitionTo() {
            manager.TaskDuration = (float)CustomUtility.Sample(
                self.random,
                genetics.Genes.Get(settings.AverageFlightTime),
                genetics.Genes.Get(settings.AveragFlightVariance)
            );
            if (math.all(moveDirection == float3.zero))
                moveDirection = Movement.RandomDirection(ref self.random);
            return true;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(BoidFollowSetting), new BoidFollowSetting());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }


        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations)) relations = null;
            self.Register<BoidFollowSetting.IBoid>(this);
            moveDirection = Movement.RandomDirection(ref self.random);
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }


        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: BoidFollow Behavior Requires AnimalInstance to have PathfindingBehavior");
            if (!self.Is(out relations)) relations = null;
            self.Register<BoidFollowSetting.IBoid>(this);
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}