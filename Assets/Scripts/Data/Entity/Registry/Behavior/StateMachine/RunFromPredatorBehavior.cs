using System;
using Arterra.Data.Entity;
using Arterra.Core.Events;
using Newtonsoft.Json;
using Unity.Mathematics;
using Arterra.Configuration;
using Arterra.Editor;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class RunFromPredatorSettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.RunFromPredator;
        public EntitySMTasks OverridableStates = EntitySMTasks.AttackTarget;
        public EntitySMTasks OnFinishRunning = EntitySMTasks.Idle;
        public Option<List<EntityWrapper>> Predators;

        [UISetting(Ignore = true)]
        [JsonIgnore]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;

        public object Clone() {
            return new RunFromPredatorSettings {
                TaskName = this.TaskName,
                OverridableStates = this.OverridableStates,
                OnFinishRunning = this.OnFinishRunning,
                Predators = this.Predators
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            AwarenessTable = new Dictionary<int, int>();
            Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;
            if (Predators.value == null || Predators.value.Count == 0) return;

            for(int i = 0; i < Predators.value.Count; i++){
                int entityIndex = eReg.RetrieveIndex(Predators.value[i].EntityType);
                AwarenessTable.TryAdd(entityIndex, i);
            }
        }

        public bool FindClosestPredator(Entity self, float sightDist, out Entity entity, RelationsBehavior relations = null) {
            entity = null; if (AwarenessTable == null) return false;
            if (Predators.value == null || Predators.value.Count == 0) return false;

            Entity cEntity = null; float closestDist = sightDist + 1;
            Dictionary<int, int> Awareness = AwarenessTable;
            Bounds bounds = new(self.position, 2 * new float3(sightDist));
            EntityManager.ESTree.Query(bounds, nEntity => {
                if (nEntity == null) return;
                if (nEntity.info.rtEntityId == self.info.rtEntityId) return;
                if (!Awareness.ContainsKey((int)nEntity.info.entityType)) return;
                if (nEntity.Is(out VitalityBehavior vit) && vit.IsDead) return;
                if (relations != null) {
                    float suppressThreshold = relations.settings.SuppressInstinctAffection;
                    if (relations.GetAffection(self.info.rtEntityId) > suppressThreshold) return;
                }

                float dist = ColliderUpdateBehavior.GetColliderDist(self, nEntity);
                if (dist >= closestDist) return;
                cEntity = nEntity;
                closestDist = dist;
            });
            entity = cEntity;
            return entity != null;
        }

        public bool Recognize(int index) => AwarenessTable.ContainsKey(index);

        [Serializable]
        public struct EntityWrapper {
            [RegistryReference("Entities")]
            public string EntityType;
        }
    }

    public class RunFromPredatorBehavior : IBehavior {
        [JsonIgnore]
        public RunFromPredatorSettings settings;
        private FleeBehaviorSettings flee;
        private Movement movement;
        private MMove mmove; //optional

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private GeneticsBehavior genetics;
        private RelationsBehavior relations;

        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex <= settings.OverridableStates) {
                DetectPredator();
                return;
            } else if (manager.TaskIndex != settings.TaskName) return;

            Movement.FollowStaticPath(MMove.Profile(mmove, settings.TaskName, self.settings),
                ref path.pathFinder, self.PathCollider,
                MMove.Speed(mmove, settings.TaskName, genetics.Genes, movement.runSpeed),
                movement.rotSpeed, movement.acceleration, MMove.MovementType(mmove, settings.TaskName));
            if (!path.pathFinder.hasPath) {
                manager.Transition(settings.OnFinishRunning);
                return;
            }
        }

        private void DetectPredator() {
            if (!settings.FindClosestPredator(self, genetics.Genes.Get(
                flee.detectDist), out Entity predator, relations))
                return;

            int PathDist = flee.fleeDist;
            float3 rayDir = self.position - predator.position;
            byte[] nPath = PathFinder.FindPathAlongRay(self.PathCoord, ref rayDir, PathDist + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.PathCoord, nPath, pLen);
            manager.TaskTarget = predator.info.rtEntityId;
            manager.Transition(settings.TaskName);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RunFromPredatorSettings), new RunFromPredatorSettings());
            heirarchy.TryAdd(typeof(FleeBehaviorSettings), new FleeBehaviorSettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!setting.Is(out flee))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have Flee");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord){
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!setting.Is(out flee))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalSettings to have Flee");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: RunFromPredator Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}