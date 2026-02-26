using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class RelationsBehaviorSettings : IBehaviorSetting {
        public Genetics.GeneFeature OnFedAffinity = new(){mean = 10f, geneWeight = 0.025f, var = 0.5f}; //per amount of 
        public Genetics.GeneFeature OnProtectAffinity = new(){mean = 2f, geneWeight = 0.025f, var = 0.75f};
        public Genetics.GeneFeature OnSaveAffinity = new(){mean = 20f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature OnAttackAffinity = new(){mean = -7.5f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature ForgetFalloff = new(){mean = 0.2f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature BaseForgetRate = new(){mean = 0.05f, geneWeight = 0.025f, var = 0.7f};
        //Forget rate = BaseForgetRate * e^(-ForgetFalloff * Affinity)
        public Option<List<EntitySMTasks>> EscapingTargetStates = new() {
            value = new List<EntitySMTasks> {
                EntitySMTasks.RunFromPredator,
                EntitySMTasks.RunFromTarget
            }
        };
        [HideInInspector][UISetting(Ignore = true)][JsonIgnore]
        public HashSet<EntitySMTasks> EscapeStatesSet;

        public object Clone() {
            return new RelationsBehaviorSettings {
                OnFedAffinity = OnFedAffinity,
                OnProtectAffinity = OnProtectAffinity,
                OnSaveAffinity = OnSaveAffinity,
                OnAttackAffinity = OnAttackAffinity,
                ForgetFalloff = ForgetFalloff,
                BaseForgetRate = BaseForgetRate,
                EscapingTargetStates = EscapingTargetStates,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref OnFedAffinity);
            Genetics.AddGene(entityType, ref OnProtectAffinity);
            Genetics.AddGene(entityType, ref OnSaveAffinity);
            Genetics.AddGene(entityType, ref OnAttackAffinity);
            Genetics.AddGene(entityType, ref ForgetFalloff);
            Genetics.AddGene(entityType, ref BaseForgetRate);

            if (EscapingTargetStates.value == null) {
                EscapeStatesSet = new HashSet<EntitySMTasks>();
                return;
            } else {
                EscapeStatesSet = new HashSet<EntitySMTasks>(EscapingTargetStates.value);
            }
        }
    }

    public class RelationsBehavior : IBehavior {
        [JsonIgnore] public RelationsBehaviorSettings settings;

        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        private StateMachineManagerBehavior manager;
        private Genetics Genes => genetics.Genes;


        public Dictionary<Guid, float> Relationships;
        private Guid HookedTarget;
        
        public void Update(BehaviorEntity.Animal self) {
            AttachHooksToAttacker();
            List<Guid> toRemove = null;
            List<(Guid id, float value)> toSet = null;
            foreach (var kv in Relationships) {
                Guid friend = kv.Key;
                float a = kv.Value;

                if (a == 0f) {
                    (toRemove ??= new List<Guid>()).Add(friend);
                    continue;
                }

                float rate = Genes.Get(settings.BaseForgetRate) * math.exp(-Genes.Get(settings.ForgetFalloff) * math.abs(a)); // per second
                float delta = rate * EntityJob.cxt.deltaTime; // always >= 0

                // Clamp so we don't overshoot past zero
                if (math.abs(a) <= delta) (toRemove ??= new List<Guid>()).Add(friend);
                else (toSet ??= new()).Add((friend, a - delta * math.sign(a)));
            }

            //If we want to keep track of whether we met and had a relation with this animal before
            //Delete this to remove block.
            if (toRemove != null)
                foreach (var id in toRemove)
                    Relationships.Remove(id);
            
            if (toRemove != null)
                foreach (var id in toRemove)
                    Relationships.Remove(id);
        }

        public float GetAffinity(Guid target) {
            if(!Relationships.TryGetValue(target, out float affinity))
                affinity = 0;
            return affinity;
        }

        public (bool, bool) TryFindBestRelations(Entity self, float radius, out (Entity e, float p) friend, out (Entity e, float p) enemy) {
            Bounds bounds = new (self.position, new float3(radius*2));
            (Entity e, float p) Friend = new (null, 0);
            (Entity e, float p) Enemy = new (null, 0);
            

            EntityManager.ESTree.Query(bounds, nEntity => {
                if(nEntity == null) return;
                if(nEntity.info.entityId == self.info.entityId) return;

                float preference = GetAffinity(nEntity.info.entityId);
                
                if(Friend.e == null || preference > Friend.p) {
                    Friend.e = nEntity;
                    Friend.p = preference;
                } if (Enemy.e == null || preference < Enemy.p) {
                    Enemy.e = nEntity;
                    Enemy.p = preference;
                }
            });

            friend = Friend;
            enemy = Enemy;
            return (
                friend.e != null && friend.p > 0,
                enemy.e != null && enemy.p < 0
            );
        }

        void RemoveAttackerHooks(Guid target) {
            if (!EntityManager.TryGetEntity(target, out Entity attacker)) return;
            EntityManager.AddHandlerEvent(() => attacker.eventCtrl.RemoveEventHandler(GameEvent.Entity_Damaged, OnPursuerAttacked));
            HookedTarget = Guid.Empty;
        }

        void AddAttackerHooks(Guid target) {
            if (!EntityManager.TryGetEntity(target, out Entity attacker)) return;
            EntityManager.AddHandlerEvent(() => attacker.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnPursuerAttacked));
            HookedTarget = target;
        }
        private void AttachHooksToAttacker() {
            if (settings.EscapeStatesSet.Contains(manager.TaskIndex)) {
                if (manager.TaskTarget == HookedTarget) return;
                RemoveAttackerHooks(HookedTarget);
                AddAttackerHooks(manager.TaskTarget);
            } else if(HookedTarget != Guid.Empty)
                RemoveAttackerHooks(HookedTarget);
        }

        public void OnFed(object self, object target, object cxt) {
            Entity feeder; float nutrRaw; float nutrition;
            (feeder, nutrRaw, nutrition) = ((RefTuple<(Entity, float, float)>)cxt).Value;
            if (feeder == null) return;
            float dAffinity = nutrition * Genes.Get(settings.OnFedAffinity);
            if (dAffinity == 0) return;

            if (!Relationships.TryAdd(feeder.info.entityId, dAffinity)) 
                Relationships[feeder.info.entityId] += dAffinity;
        }

        public void OnPursuerAttacked(object attacker, object savior, object cxt) {
            if (savior == null) return;
            if (savior is not Entity hero) return;

            float damage; float3 kb;
            (damage, kb) = (cxt as RefTuple<(float, float3)>).Value;
            float dAffinity = Genes.Get(settings.OnProtectAffinity) * damage;
            if (vitality != null && vitality.IsKillingBlow(damage))
                dAffinity += Genes.Get(settings.OnSaveAffinity);
            if (!Relationships.TryAdd(hero.info.entityId, dAffinity)) 
                Relationships[hero.info.entityId] += dAffinity;
        }

        public void OnAttacked(object self, object attacker, object cxt) {
            if (attacker == null) return;
            if (attacker is not Entity assailant) return;

            float damage; float3 kb;
            (damage, kb) = (cxt as RefTuple<(float, float3)>).Value;
            float dAffinity = Genes.Get(settings.OnAttackAffinity) * damage;
            if (!Relationships.TryAdd(assailant.info.entityId, dAffinity)) 
                Relationships[assailant.info.entityId] += dAffinity;
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RelationsBehaviorSettings), new RelationsBehaviorSettings());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalSettings to have RelationsBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have StateMachineManager");

            this.HookedTarget = Guid.Empty;
            Relationships = new Dictionary<Guid, float>();
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Fed, OnFed);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnAttacked);
            self.Register(this);
        }
        
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalSettings to have RelationsBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out manager))
                throw new System.Exception("Entity: Relations Behavior Requires AnimalInstance to have StateMachineManager");

            
            this.HookedTarget = Guid.Empty;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Fed, OnFed);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnAttacked);
            self.Register(this);
        }

        public void Disable() {
            RemoveAttackerHooks(HookedTarget);
        }
    }
}