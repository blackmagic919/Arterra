using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Arterra.Core.Events;
using Unity.Mathematics;
using Arterra.Data.Item;
using Arterra.Utils;

namespace Arterra.Data.Entity.Behavior {
    public class ChasePreySettings : IBehaviorSetting {
        public EntitySMTasks TaskName = EntitySMTasks.ChasePreyEntity;
        public EntitySMTasks OnNotFoundTransition = EntitySMTasks.Idle;
        public EntitySMTasks OnReachPreyTransition = EntitySMTasks.AttackTarget;
        public PreyState DesiredPreyState;

        public Option<RangeSet<EntityWrapper>> Prey;
        public static bool HasPrey(ChasePreySettings val) => !(val == null 
        || val.Prey.value == null || val.Prey.value.AllowList.value == null
        || val.Prey.value.AllowList.value == null || val.Prey.value.AllowList.value.Count == 0);

        [Serializable]
        public struct EntityWrapper : IRangeBlock {
            [TagOrRegistryReference("Entities")]
            public TagOrRegistryReference EntityType;
            public IRangeBlock.Policy Policy;
            [JsonIgnore]
            public TagOrRegistryReference selection {
                readonly get => EntityType;
                set => EntityType = value;
            }
            [JsonIgnore]
            public IRangeBlock.Policy policy {
                readonly get => Policy;
                set => Policy = value;
            }
        }

        public object Clone() {
            return new ChasePreySettings(){
                TaskName = TaskName,
                OnNotFoundTransition = OnNotFoundTransition,
                OnReachPreyTransition = OnReachPreyTransition,
                DesiredPreyState = DesiredPreyState
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;
            if(Prey.value == null) return;
            Prey.value.Construct(eReg);
        }

        public bool FindPreferredPreyEntity(Entity self, ConsumeBehaviorSettings cnsm, float sightDist, out Entity entity, RelationsBehavior relations = null){
            entity = null; 
            if( !HasPrey(this) && !ConsumeBehaviorSettings.HasEdibles(cnsm))
                return false;

            Entity cEntity = null; int pPref = -1;
            float closestDist = sightDist + 1;

            Bounds bounds = new (self.position, 2 * new float3(sightDist));
            EntityManager.ESTree.Query(bounds, (nEntity) => {
                if(nEntity == null) return;
                if(nEntity.info.rtEntityId == self.info.rtEntityId) return;
                if (relations != null) {
                    float suppressThreshold = relations.settings.SuppressInstinctAffection;
                    if (relations.GetAffection(nEntity.info.rtEntityId) > suppressThreshold) return;
                }

                if (!Prey.value.IsAllowListed((int)nEntity.info.entityType, out int preference)
                    && !TrySearchEntityItems(nEntity, cnsm, out preference))
                    return;
                
                if (DesiredPreyState != PreyState.Any) {
                    if (!nEntity.Is(out IAttackable atkEntity)) return;
                    if (atkEntity.IsDead && DesiredPreyState == PreyState.Alive) return;
                    if (!atkEntity.IsDead && DesiredPreyState == PreyState.Dead) return;
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

        private bool TrySearchEntityItems(Entity entity, ConsumeBehaviorSettings consumables, out int preference) {
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

        public bool Recognize(int index) => Prey.value.IsAllowListed(index, out _);

        public enum PreyState {
            Any = 0,
            Alive = 1, 
            Dead = 2,
        }
    }


    public class ChasePreyBehavior : SpeciesBehavior {
        [JsonIgnore] public ChasePreySettings settings;
        private Movement movement;
        private HuntBehaviorSettings hunt;
        private ConsumeBehaviorSettings consumables; //optional
        private MMove mmove; //optional


        private BehaviorEntity.Animal self;
        private StateMachineManagerBehavior manager;
        private PathFinderBehavior path;
        private VitalityBehavior vitality;
        private Modifier mod;
        private RelationsBehavior relations;
        private bool IsHunting;
        private float notFoundCooldown;
        private const float NotFoundRetryDelay = 0.25f;

        private float HuntThreshold => Modifier.Get(mod, MSettings.HuntThreshold, hunt.HuntThreshold);
        private float StopHuntThreshold => Modifier.Get(mod, MSettings.StopHuntThreshold, hunt.StopHuntThreshold);
        private float RunSpeed => MMove.Speed(mmove, settings.TaskName, mod, MSettings.RunSpeed, movement.runSpeed);
        public bool BeginHunting() => IsHunting || (IsHunting = vitality.healthPercent < HuntThreshold);
        public bool StopHunting() => !IsHunting || !(IsHunting = vitality.healthPercent < StopHuntThreshold);

        //Task 4
        public override void Update(BehaviorEntity.Animal self) {
            if (manager.TaskIndex != settings.TaskName) return;
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (notFoundCooldown > 0) notFoundCooldown -= self.DeltaTime;
            
            if (!settings.FindPreferredPreyEntity(self, consumables,
                movement.pathDistance, out Entity prey, relations)
            ) {
                if (notFoundCooldown <= 0) {
                    manager.Transition(settings.OnNotFoundTransition);
                    notFoundCooldown = NotFoundRetryDelay;
                }
                return;
            }
            notFoundCooldown = 0;

            self.PathCollider.Follow(self, Movement.DynamicDirect(
                MMove.Profile(mmove, settings.TaskName, self.settings), 
                ref path.pathFinder, self.PathCollider, prey.origin,
                MMove.MovementType(mmove, settings.TaskName)
            ), RunSpeed, movement.rotSpeed, self.DeltaTime, GameEvent.Action_Run);
            
            float preyDist = ColliderUpdateBehavior.GetColliderDist(self, prey);
            if (preyDist < manager.settings.ContactDistance && manager.Transition(settings.OnReachPreyTransition)) {
                manager.TaskTarget = prey.info.rtEntityId;
            } else if (!path.pathFinder.hasPath && !FindPrey(out bool Locked)) {
                if (notFoundCooldown <= 0) {
                    manager.Transition(settings.OnNotFoundTransition);
                    notFoundCooldown = NotFoundRetryDelay;
                }
            }
        }

        private bool FindPrey(out bool LockedOn) {
            LockedOn = false;
            if (StopHunting()) return false;
            if (!settings.FindPreferredPreyEntity(
                self, consumables,
                movement.pathDistance,
                out Entity prey, relations)
            ) return false;
            
            int PathDist = movement.pathDistance;
            int3 destination = (int3)math.round(prey.origin) - self.PathCoord;
            if (path.FindPathOrApproachTarget(settings.TaskName, self.PathCoord, destination, PathDist + 1,
                MMove.Profile(mmove, settings.TaskName, self.settings), EntityJob.cxt, out byte[] nPath)){
                path.SetPath(nPath);
                LockedOn = true;
                notFoundCooldown = 0;
            } else return true;

            float dist = ColliderUpdateBehavior.GetColliderDist(self, prey);
            //If it can't get to the prey and is currently at the closest position it can be
            if (math.all(path.pathFinder.destination == self.PathCoord)) {
                if (dist <= manager.settings.ContactDistance && manager.Transition(settings.OnReachPreyTransition)) {
                    manager.TaskTarget = prey.info.rtEntityId;
                } return false;
            } return true;
        }

        public bool TransitionTo() {
            if (!BeginHunting()) return false;
            return FindPrey(out bool Locked) && Locked;
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Pathfinding, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ChasePreySettings), new ChasePreySettings());
            heirarchy.TryAdd(typeof(Movement), new Movement());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
            heirarchy.TryAdd(typeof(HuntBehaviorSettings), new HuntBehaviorSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out consumables)) consumables = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            IsHunting = false;
            notFoundCooldown = 0;
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have RandomWalkState");
            if (!setting.Is(out movement))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Movement");
            if (!setting.Is(out consumables)) consumables = null;
            if (!setting.Is(out mmove)) mmove = null;
            if (!setting.Is(out hunt))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalSettings to have Hunt");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have StateMachineManager");
            if (!self.Is(out path))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have PathFinderBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: ChasePrey Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out relations)) relations = null;
            if (!self.Is(out mod)) mod = null;
            
            notFoundCooldown = 0;
            manager.RegisterTransition(settings.TaskName, TransitionTo);
            this.self = self;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}