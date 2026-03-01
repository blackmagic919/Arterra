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
        public Genetics.GeneFeature OnFedAffection = new(){mean = 8f, geneWeight = 0.025f, var = 0.5f}; //per amount of 
        public Genetics.GeneFeature OnProtectAffection = new(){mean = 8f, geneWeight = 0.025f, var = 0.75f};
        public Genetics.GeneFeature OnSaveAffection = new(){mean = 15f, geneWeight = 0.025f, var = 0.5f};

        public Genetics.GeneFeature OnMateAffection = new(){mean = 20f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature OnBetrayalAffection = new(){mean = -2.5f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature OnRivalMateAffection = new(){mean = -14f, geneWeight = 0.025f, var = 0.5f};

        public Genetics.GeneFeature OnAttackAffection = new(){mean = -18f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature ForgetFalloff = new(){mean = 0.2f, geneWeight = 0.025f, var = 0.5f};
        public Genetics.GeneFeature BaseForgetRate = new(){mean = 0.05f, geneWeight = 0.025f, var = 0.7f};
        public float SuppressInstinctAffection = 10.0f;

        public Genetics.GeneFeature GossipCooldown = new(){mean = 5f, geneWeight = 0.01f, var = 0.25f};
        public Genetics.GeneFeature GossipRadius = new(){mean = 8f, geneWeight = 0.01f, var = 0.5f};
        //Higher falloff means friends are more likely to gossip with one another
        public Genetics.GeneFeature GossipCloseness = new(){mean = 0.1f, geneWeight = 0.01f, var = 0.5f};
        // The influence on a friend's affection of the affection of our relation (factoring in closeness between us)
        public Genetics.GeneFeature GossipStrength = new(){mean = 0.2f, geneWeight = 0.01f, var = 0.75f};
        //The max amount of stuff that can be gossiped at one time
        public Genetics.GeneFeature GossipAmount = new(){mean = 2.5f, geneWeight = 0.01f, var = 0.5f};

        //Forget rate = BaseForgetRate * e^(-ForgetFalloff * Affection)
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
                OnFedAffection = OnFedAffection,
                OnProtectAffection = OnProtectAffection,
                OnSaveAffection = OnSaveAffection,
                OnMateAffection = OnMateAffection,
                OnAttackAffection = OnAttackAffection,
                ForgetFalloff = ForgetFalloff,
                BaseForgetRate = BaseForgetRate,
                EscapingTargetStates = EscapingTargetStates,
                SuppressInstinctAffection = SuppressInstinctAffection,
                GossipCooldown = GossipCooldown,
                GossipRadius = GossipRadius,
                GossipCloseness = GossipCloseness,
                GossipStrength = GossipStrength,
                GossipAmount = GossipAmount,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref OnFedAffection);
            Genetics.AddGene(entityType, ref OnProtectAffection);
            Genetics.AddGene(entityType, ref OnSaveAffection);
            Genetics.AddGene(entityType, ref OnMateAffection);
            Genetics.AddGene(entityType, ref OnAttackAffection);
            Genetics.AddGene(entityType, ref ForgetFalloff);
            Genetics.AddGene(entityType, ref GossipCooldown);
            Genetics.AddGene(entityType, ref GossipRadius);
            Genetics.AddGene(entityType, ref GossipCloseness);
            Genetics.AddGene(entityType, ref GossipStrength);
            Genetics.AddGene(entityType, ref GossipAmount);

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

        //Basically a list + dictionary to allow us to change the dictionary mid loop
        [JsonProperty] private IndirectDictionary<Guid, float> Relationships;
        [JsonProperty] private Guid HookedTarget;
        [JsonProperty] private Guid LastMate;
        //Keep track of how much we've gossiped about each of our friends to stop the runaway broadcaster effect
        [JsonProperty] private Dictionary<Guid, GossipTopic> Gossip;
        [JsonProperty] private float TimeSinceGossip;

        struct GossipTopic {
            public float TalkedAbout;
            public float TalkedTo;

            public GossipTopic(float talkedTo) {
                this.TalkedAbout = 0;
                this.TalkedTo = talkedTo;
            }
        }

        //Helper
        public float GetAffection(Guid target) {
            if(!Relationships.TryGetValue(target, out float Affection))
                Affection = 0;
            return Affection;
        }

        public void AddAffection(Guid target, float dAffection) {
            if (!Relationships.TryAdd(target, dAffection))
                Relationships[target] += dAffection;
        }

        public void Update(BehaviorEntity.Animal self) {
            AttachHooksToAttacker();
            GossipWithFriends(self);
            List<Guid> toRemove = null;
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
                else Relationships[friend] = a - delta * math.sign(a);
            }

            //If we want to keep track of whether we met and had a relation with this animal before
            //Delete this to remove block.
            if (toRemove != null) {
                foreach (var id in toRemove) {
                    Relationships.Remove(id);
                    Gossip.Remove(id);
                }
            }
        }

        // Gossip stops the runaway broadcaster. If A and B are stuck in a room talking about A's friend C
        // At some point A will run out of topics and A will shut up about C. However, it's possible
        // That B gains topics about C from talking to A, and then talks back to A about C, however dampening via 
        // closeness and gossip strength should mean this feedback effect diminishes and converges.
        private void GossipWithFriends(BehaviorEntity.Animal self) {
            TimeSinceGossip -= EntityJob.cxt.deltaTime;
            if (TimeSinceGossip > 0) return;
            TimeSinceGossip = Genes.Get(settings.GossipCooldown);
            float gossipRadius = Genes.Get(settings.GossipRadius);
            List<(Guid, float)> newTopics = FindNewTopics();

            Bounds bounds = new (self.position, new float3(gossipRadius*2));
            EntityManager.ESTree.Query(bounds, nEntity => {
                if (nEntity.info.entityId == self.info.entityId) return;

                float intAffection; float extAffection;
                if ((intAffection = GetAffection(nEntity.info.entityId)) <= 0) return;
                if (!nEntity.Is(out RelationsBehavior nRelations)) return;
                if ((extAffection = nRelations.GetAffection(self.info.entityId)) <= 0) return;
                float closeness = 1 - math.exp(-intAffection * extAffection * Genes.Get(settings.GossipCloseness));
                DiscussNewTopics(self, nEntity, newTopics, closeness);
                CatchUpWithFriend(self, nEntity, closeness);
            });

            void CatchUpWithFriend(Entity self, Entity friend, float closeness) {
                if(!Gossip.TryGetValue(friend.info.entityId, out GossipTopic lastGossip))
                    lastGossip.TalkedTo = 0;
                if (!friend.Is(out RelationsBehavior friendRelations)) return;

                float affection = math.max(Relationships[friend.info.entityId], 0);
                if (lastGossip.TalkedTo >= affection) {
                    lastGossip.TalkedTo = affection;
                    Gossip[friend.info.entityId] = lastGossip;
                    return;
                }
                float thirdPartyTopics = 0;
                foreach(var kv in Relationships) {
                    if (kv.Key == friend.info.entityId) continue;
                    thirdPartyTopics += math.abs(kv.Value);
                } if (thirdPartyTopics == 0) return;
                
                float talkAmount = math.min(affection - lastGossip.TalkedTo, Genes.Get(settings.GossipAmount));
                //scale by how much other people mean relative to how much everyone means to you
                talkAmount *= thirdPartyTopics / (thirdPartyTopics + affection); 
                
                foreach(var kv in Relationships) {
                    if (kv.Key == friend.info.entityId) continue;
                    float gossipAmount = talkAmount * (kv.Value / thirdPartyTopics) * closeness;
                    float dAffection = gossipAmount * Genes.Get(settings.GossipStrength);
                    friendRelations.AddAffection(kv.Key, dAffection);
                } 
                GossipTopic gossipData = Gossip[friend.info.entityId];
                gossipData.TalkedTo += talkAmount;
                Gossip[friend.info.entityId] = gossipData;
            }

            void DiscussNewTopics(Entity self, Entity friend, List<(Guid, float)> topics, float closeness) {
                if (topics == null) return;
                if (!friend.Is(out RelationsBehavior friendRelations)) return;
                foreach(var topic in topics) {
                    Guid subject = topic.Item1;
                    float gossipAmount = topic.Item2;
                    //brag about your friend to that friend lol
                    if (subject == friend.info.entityId) continue; 
                    gossipAmount = math.sign(gossipAmount) * math.min(math.abs(gossipAmount), Genes.Get(settings.GossipAmount));
                    gossipAmount *= closeness;
                    
                    float dAffection = gossipAmount * Genes.Get(settings.GossipStrength);
                    friendRelations.AddAffection(subject, dAffection);
                    
                    GossipTopic gossipData = Gossip[subject];
                    gossipData.TalkedAbout += gossipAmount;
                    Gossip[subject] = gossipData;
                }
            }

            List<(Guid, float)> FindNewTopics() {
                List<(Guid, float)> newTopics = null;
                foreach (var kv in Relationships) {
                    if(!Gossip.TryGetValue(kv.Key, out GossipTopic lastGossipValue)) {
                        Gossip[kv.Key] = new GossipTopic(0);
                        lastGossipValue.TalkedAbout = 0;
                    } float affection = kv.Value;
                    float discussedAmount = lastGossipValue.TalkedAbout;

                    float topic = 0;
                    if (affection < 0) {
                        if (discussedAmount <= affection) {
                            lastGossipValue.TalkedAbout = affection;
                            Gossip[kv.Key] = lastGossipValue;
                        } else topic = affection - discussedAmount; //negative
                    } else {
                         if (affection <= discussedAmount) {
                            lastGossipValue.TalkedAbout = affection;
                            Gossip[kv.Key] = lastGossipValue;
                         } else topic = affection - discussedAmount; //positive
                    }

                    if (topic != 0) (newTopics ??= new List<(Guid, float)>()).Add((kv.Key, topic));
                }
                return newTopics;
            }
        }

        public (bool, bool) TryFindBestRelations(Entity self, float radius, out (Entity e, float p) friend, out (Entity e, float p) enemy) {
            Bounds bounds = new (self.position, new float3(radius*2));
            (Entity e, float p) Friend = new (null, 0);
            (Entity e, float p) Enemy = new (null, 0);
            

            EntityManager.ESTree.Query(bounds, nEntity => {
                if(nEntity == null) return;
                if(nEntity.info.entityId == self.info.entityId) return;
                if (nEntity.Is(out VitalityBehavior vit) && vit.IsDead) return;

                float preference = GetAffection(nEntity.info.entityId);
                
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
            float dAffection = nutrition * Genes.Get(settings.OnFedAffection);
            if (dAffection == 0) return;

            AddAffection(feeder.info.entityId, dAffection);
        }

        public void OnPursuerAttacked(object attacker, object savior, object cxt) {
            if (savior == null) return;
            if (savior is not Entity hero) return;

            float damage; float3 kb;
            (damage, kb) = (cxt as RefTuple<(float, float3)>).Value;
            
            float rate = damage;
            if (hero.Is(out VitalityBehavior vitality))
                rate /= genetics.Genes.Get(vitality.stats.MaxHealth);
            else rate /= 10; //10 is base reduction

            float dAffection = Genes.Get(settings.OnProtectAffection) * rate;
            if (vitality != null && vitality.IsKillingBlow(damage))
                dAffection += Genes.Get(settings.OnSaveAffection);
            EntityManager.AddHandlerEvent(() => AddAffection(hero.info.entityId, dAffection));
        }

        public void OnAttacked(object self, object attacker, object cxt) {
            if (attacker == null) return;
            if (attacker is not Entity assailant) return;

            float damage; float3 kb;
            (damage, kb) = (cxt as RefTuple<(float, float3)>).Value;

            float rate = damage / genetics.Genes.Get(vitality.stats.MaxHealth);
            float dAffection = Genes.Get(settings.OnAttackAffection) * rate;

            AddAffection(assailant.info.entityId, dAffection);
            
        }

        public void OnMating(object src, object mate, object cxt) {
            if (mate == null) return;
            if (mate is not Entity partner) return;
            float dAffection = Genes.Get(settings.OnMateAffection);
            AddAffection(partner.info.entityId, dAffection);
            
            BetrayPreviousPartner();
            LastMate = partner.info.entityId;

            void BetrayPreviousPartner() {
                if (!EntityManager.TryGetEntity(LastMate, out Entity lastPartner)) return;
                if (lastPartner.info.entityId == partner.info.entityId) return; //Being faithful :)
                if (!lastPartner.Is(out RelationsBehavior lp)) return;
                if (src is not Entity self) return;
                if (lp.LastMate != self.info.entityId) return; //your partner cheated on you first :(
                EntityManager.AddHandlerEvent(() => {
                    //How you feel twoards your partner when they cheat on you
                    lp.AddAffection(self.info.entityId, Genes.Get(settings.OnBetrayalAffection));
                    //how you feel towards the one who netorared your partner
                    lp.AddAffection(partner.info.entityId, Genes.Get(settings.OnRivalMateAffection));
                });
            }
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.StateMachine, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(RelationsBehaviorSettings), new RelationsBehaviorSettings());
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

            this.LastMate = Guid.Empty;
            this.HookedTarget = Guid.Empty;
            Relationships = new IndirectDictionary<Guid, float>();
            Gossip = new Dictionary<Guid, GossipTopic>();
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Fed, OnFed);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, OnAttacked);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Mate, OnMating);
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
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Mate, OnMating);
        }

        public void Disable() {
            RemoveAttackerHooks(HookedTarget);
        }
    }
}