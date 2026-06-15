using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Arterra.Core.Events;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class ChaseEnemySettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.ChasePreyEntity;
        public EntitySMTasks OnNotFoundTransition = EntitySMTasks.Idle;
        public EntitySMTasks OnReachEnemyTransition = EntitySMTasks.AttackTarget;
        public Option<List<StateSearch>> SearchChances = new () { value = new () {
            new () {SourceState = EntitySMTasks.Idle, SearchChance = 0.5f}
        }};

        public float SearchDistance = 10;
        public EnemyState DesiredEnemyState;

        [Serializable]
        public struct StateSearch {
            public EntitySMTasks SourceState;
            public float SearchChance;
        }
        

        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<EntitySMTasks, float> _SearchChances;

        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;
        public Option<List<EntityWrapper>> Enemy;

        [Serializable]
        public struct EntityWrapper {
            [RegistryReference("Entities")]
            public string EntityType;
        }

        public object Clone() {
            return new ChaseEnemySettings(){
                TaskName = TaskName,
                OnNotFoundTransition = OnNotFoundTransition,
                OnReachEnemyTransition = OnReachEnemyTransition,
                SearchDistance = SearchDistance,
                SearchChances = SearchChances,
                DesiredEnemyState = DesiredEnemyState
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;
            AwarenessTable = new Dictionary<int, int>();
            if(Enemy.value != null) {
                for(int i = 0; i < Enemy.value.Count; i++){
                    int entityIndex = eReg.RetrieveIndex(Enemy.value[i].EntityType);
                    AwarenessTable.TryAdd(entityIndex, i);
                }
            };

            _SearchChances = new Dictionary<EntitySMTasks, float>();
            if(SearchChances.value != null) {
                foreach(StateSearch s in SearchChances.value) {
                    StateSearch copy = s;
                    _SearchChances.TryAdd(s.SourceState, copy.SearchChance);
                }
            };
        }

        public bool FindPreferredEnemyEntity(Entity self, float sightDist, out Entity entity, RelationsBehavior relations = null){
            entity = null; if(AwarenessTable == null) return false;
            if(Enemy.value == null || Enemy.value.Count == 0)
                return false;

            Entity cEntity = null; int pPref = -1;
            float closestDist = sightDist + 1;

            Dictionary<int, int> Awareness = AwarenessTable;
            Bounds bounds = new (self.position, 2 * new float3(sightDist));
            EntityManager.ESTree.Query(bounds, (nEntity) => {
                if(nEntity == null) return;
                if(nEntity.info.rtEntityId == self.info.rtEntityId) return;
                if (relations != null) {
                    float suppressThreshold = relations.settings.SuppressInstinctAffection;
                    if (relations.GetAffection(self.info.rtEntityId) > suppressThreshold) return;
                }

                if (!Awareness.TryGetValue((int)nEntity.info.entityType, out int preference))
                    return;
                
                if (DesiredEnemyState != EnemyState.Any) {
                    if (!nEntity.Is(out IAttackable atkEntity)) return;
                    if (atkEntity.IsDead && DesiredEnemyState == EnemyState.Alive) return;
                    if (!atkEntity.IsDead && DesiredEnemyState == EnemyState.Dead) return;
                }
                
                if(cEntity != null){
                if(preference > pPref) return;
                if(preference == pPref && ColliderUpdateBehavior.GetColliderDist(nEntity, self) >= closestDist) return;
                }
                
                cEntity = nEntity;
                pPref = preference;
                closestDist = ColliderUpdateBehavior.GetColliderDist(nEntity, self);
            });
            entity = cEntity;
            return entity != null;
        }

        public bool Recognize(int index) => AwarenessTable.ContainsKey(index);

        public enum EnemyState {
            Any = 0,
            Alive = 1, 
            Dead = 2,
        }
    }


    public class ChaseEnemyBehavior : SpeciesBehavior {
        [JsonIgnore] public ChaseEnemySettings settings;
        private Movement movement;
        private MMove mmove; //optional


        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private Modifier mod;
        private RelationsBehavior relations;

        private float SearchDistance => Modifier.Get(mod, MSettings.SearchDistance, settings.SearchDistance);
        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);


        //Task 4
        public override void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (!settings.FindPreferredEnemyEntity(self,  SearchDistance, out Entity Enemy, relations)) {
                manager.Transition(settings.TaskName);
                return;
            }

            self.PathCollider.Follow(self, Movement.DynamicDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings),
                ref path.pathFinder, self.PathCollider, Enemy.origin,
                MMove.MovementType(mmove, settings.TaskName)
            ), RunSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Run);
            
            float EnemyDist = ColliderUpdateBehavior.GetColliderDist(self, Enemy);
            if (EnemyDist < manager.settings.ContactDistance && manager.Transition(settings.OnReachEnemyTransition)) {
                manager.TaskTarget = Enemy.info.rtEntityId;
            } else if (!path.pathFinder.hasPath && !FindEnemy()) {
                manager.Transition(settings.OnNotFoundTransition);
            }
        }

        private bool FindEnemy() {
            if (!settings.FindPreferredEnemyEntity(
                self, SearchDistance,
                out Entity Enemy, relations)
            ) return false;
            
            int PathDist = movement.pathDistance;
            int3 destination = (int3)math.round(Enemy.origin) - self.PathCoord;
            if(!path.FindPathOrApproachTarget(settings.TaskName, self.PathCoord, destination, PathDist + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out byte[] nPath))
                return false;
            path.SetPath(nPath);
            float dist = ColliderUpdateBehavior.GetColliderDist(self, Enemy);


            //If it can't get to the Enemy and is currently at the closest position it can be
            if (math.all(path.pathFinder.destination == self.PathCoord)) {
                if (dist <= manager.settings.ContactDistance && manager.Transition(settings.OnReachEnemyTransition)) {
                    manager.TaskTarget = Enemy.info.rtEntityId;
                } return false;
            } return true;
        }

        private float GetSearchChange() {
            if (!settings._SearchChances.TryGetValue(manager.TaskIndex, out float feature))
                return 1;
            return Modifier.Get(mod, MSettings.SearchChance, feature);
        }

        public bool TransitionTo() {
            if (self.random.NextFloat() > GetSearchChange())
                return false;
            return FindEnemy();
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChaseEnemySettings), new ChaseEnemySettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out mmove)) mmove = null;
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChaseEnemy Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}