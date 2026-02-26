using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Arterra.Data.Entity;
using Unity.Mathematics;
using Arterra.Data.Item;
using Arterra.Utils;

namespace Arterra.Data.Entity.Behavior {
    public class ChasePreySettings : IBehaviorSetting {
        public const string AnimationParam = "IsRunning";
        public EntitySMTasks TaskName = EntitySMTasks.ChasePreyEntity;
        public EntitySMTasks OnNotFoundTransition = EntitySMTasks.Idle;
        public EntitySMTasks OnReachPreyTransition = EntitySMTasks.AttackTarget;
        public PreyState DesiredPreyState;
        public Genetics.GeneFeature SearchDistance;
        

        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;
        public Option<List<EntityWrapper>> Prey;
        public bool HasEntityPrey => Prey.value != null && Prey.value.Count > 0;

        [Serializable]
        public struct EntityWrapper {
            [RegistryReference("Entities")]
            public string EntityType;
        }

        public object Clone() {
            return new ChasePreySettings(){
                TaskName = TaskName,
                OnNotFoundTransition = OnNotFoundTransition,
                OnReachPreyTransition = OnReachPreyTransition,
                SearchDistance = SearchDistance,
                DesiredPreyState = DesiredPreyState
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Catalogue<Arterra.Data.Entity.Authoring> eReg = Config.CURRENT.Generation.Entities;
            AwarenessTable = new Dictionary<int, int>();
            if(Prey.value == null) return;

            for(int i = 0; i < Prey.value.Count; i++){
                int entityIndex = eReg.RetrieveIndex(Prey.value[i].EntityType);
                AwarenessTable.TryAdd(entityIndex, i);
            }

            Genetics.AddGene(entityType, ref SearchDistance);
        }

        public bool FindPreferredPreyEntity(Entity self, ConsumeBehaviorSettings cnsm, float sightDist, out Entity entity){
            entity = null; if(AwarenessTable == null) return false;
            if((Prey.value == null || Prey.value.Count == 0)
                && (cnsm.Edibles.value == null || cnsm.Edibles.value.Count == 0))
                return false;

            Entity cEntity = null; int pPref = -1;
            float closestDist = sightDist + 1;

            Dictionary<int, int> Awareness = AwarenessTable;
            Bounds bounds = new (self.position, 2 * new float3(sightDist));
            EntityManager.ESTree.Query(bounds, (nEntity) => {
                if(nEntity == null) return;
                if(nEntity.info.entityId == self.info.entityId) return;

                if (!Awareness.TryGetValue((int)nEntity.info.entityType, out int preference)
                    && !TrySearchEntityItems(nEntity, cnsm, Awareness, out preference))
                    return;
                
                if (DesiredPreyState != PreyState.Any) {
                    if (!nEntity.Is(out IAttackable atkEntity)) return;
                    if (atkEntity.IsDead && DesiredPreyState == PreyState.Alive) return;
                    if (!atkEntity.IsDead && DesiredPreyState == PreyState.Dead) return;
                }
                
                if(cEntity != null){
                if(preference > pPref) return;
                if(preference == pPref && Recognition.GetColliderDist(nEntity, self) >= closestDist) return;
                }
                
                cEntity = nEntity;
                pPref = preference;
                closestDist = Recognition.GetColliderDist(nEntity, self);
            });
            entity = cEntity;
            return entity != null;
        }

        private bool TrySearchEntityItems(Entity entity, ConsumeBehaviorSettings consumables, Dictionary<int, int> awareness, out int preference) {
            preference = default;
            if (consumables == null) return false;
            if (!entity.Is(out IEntitySearchItem itemHolder)) return false;

            IItem[] items = itemHolder.GetItems();
            if (items == null) return false;
            foreach(IItem item in items) {
                if (item == null) continue;
                if(consumables.CanConsume(item.Index, out preference))
                    return true;
            } return false;
        }

        public bool Recognize(int index) => AwarenessTable.ContainsKey(index);

        public enum PreyState {
            Any = 0,
            Alive = 1, 
            Dead = 2,
        }
    }


    public class ChasePreyBehavior : IBehavior {
        private ChasePreySettings settings;
        private Movement movement;
        private HuntBehaviorSettings hunt;
        private ConsumeBehaviorSettings consumables;

        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        private bool IsHunting;

        public bool BeginHunting() => IsHunting || (IsHunting = vitality.healthPercent < genetics.Genes.Get(hunt.HuntThreshold));
        public bool StopHunting() => !IsHunting || !(IsHunting = vitality.healthPercent < genetics.Genes.Get(hunt.StopHuntThreshold));

        //Task 4
        public void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (!settings.FindPreferredPreyEntity(self, consumables,
                genetics.Genes.Get(settings.SearchDistance), out Entity prey)
            ) {
                manager.TaskIndex = settings.TaskName;
                return;
            }
            Movement.FollowDynamicPath(self.settings.profile, ref path.pathFinder, ref self.collider, prey.origin,
                genetics.Genes.Get(movement.runSpeed), movement.rotSpeed,
                movement.acceleration);
            
            float preyDist = Recognition.GetColliderDist(self, prey);
            if (preyDist < manager.settings.ContactDistance && manager.Transition(settings.OnReachPreyTransition)) {
                manager.TaskTarget = prey.info.entityId;
            } else if (!path.pathFinder.hasPath && !FindPrey()) {
                manager.Transition(settings.OnNotFoundTransition);
            }
        }

        private bool FindPrey() {
            if (StopHunting()) return false;
            if (!settings.FindPreferredPreyEntity(
                self, consumables,
                genetics.Genes.Get(settings.SearchDistance),
                out Entity prey)
            ) return false;
            
            int PathDist = movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.GCoord;
            byte[] nPath = PathFinder.FindPathOrApproachTarget(self.GCoord, destination, PathDist + 1, self.settings.profile, EntityJob.cxt, out int pLen);
            path.pathFinder = new PathFinder.PathInfo(self.GCoord, nPath, pLen);
            float dist = Recognition.GetColliderDist(self, prey);


            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(path.pathFinder.destination == self.GCoord)) {
                if (dist <= manager.settings.ContactDistance && manager.Transition(settings.OnReachPreyTransition)) {
                    manager.TaskTarget = prey.info.entityId;
                } return false;
            } return true;
        }

        public bool TransitionTo() {
            if (!BeginHunting()) return false;
            return FindPrey();
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChasePreySettings), new ChasePreySettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
            heirarchy.TryAdd(typeof(HuntBehaviorSettings), new HuntBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out consumables))
                consumables = null;
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have VitalityBehavior");
            
            IsHunting = false;
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out consumables))
                consumables = null;
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have VitalityBehavior");
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}