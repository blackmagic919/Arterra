using System;
using System.Collections.Generic;
using Arterra.Editor;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Events;

namespace Arterra.Data.Entity.Behavior {

    [Serializable]
    public class MateRecognition : IBehaviorSetting{
        //Mates are entities that can breed with the entity, and the offspring they create
        public Option<List<Mate>> Mates;
        public Genetics.GeneFeature MateCost;

        public object Clone() {
            return new MateRecognition {
                Mates = Mates,
            };
        }

        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            AwarenessTable ??= new Dictionary<int, int>();
            Catalogue<Authoring> eReg = Config.CURRENT.Generation.Entities;

            if(Mates.value == null) return;
            for(int i = 0; i < Mates.value.Count; i++){
                int entityIndex = eReg.RetrieveIndex(Mates.value[i].MateType);
                AwarenessTable.TryAdd(entityIndex, i);
            }

            Genetics.AddGene(entityType, ref MateCost);
            for (int i = 0; i < Mates.value.Count; i++) {
                Mate mate = Mates.value[i];
                Genetics.AddGene(entityType, ref mate.AmountPerParent);
                Mates.value[i] = mate;
            } 
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

        [Serializable]
        public struct Consumable {
            [RegistryReference("Items")]
            public string EdibleType;
            public Genetics.GeneFeature Nutrition;
        }

        //Finds the most preferred mate it can see, then the closest one it prefers
        public bool FindPreferredMate(Entity self, float sightDist, out Entity entity) {
            entity = null; if (AwarenessTable == null) return false;
            if (Mates.value == null || Mates.value.Count == 0) return false;

            Entity cEntity = null; int pPref = -1;
            float closestDist = sightDist + 1;

            Dictionary<int, int> Awareness = AwarenessTable;
            Bounds bounds = new(self.position, 2 * new float3(sightDist));
            EntityManager.ESTree.Query(bounds, (Entity nEntity) => {
                if (nEntity == null) return;
                if (!Awareness.ContainsKey((int)nEntity.info.entityType)) return;
                if (nEntity.info.entityId == self.info.entityId) return;

                int preference = Awareness[(int)nEntity.info.entityType];
                if (!nEntity.Is(out IMateable mateable)) return;
                if (!mateable.CanMateWith(self)) return;
                if (cEntity != null) {
                    if (preference > pPref) return;
                    if (pPref == preference && Recognition.GetColliderDist(nEntity, self) >= closestDist)
                        return;
                }

                cEntity = nEntity;
                pPref = preference;
                closestDist = Recognition.GetColliderDist(nEntity, self);
            });
            entity = cEntity;
            return entity != null;
        }


        public bool MateWithEntity(Genetics genetics, Entity entity, ref Unity.Mathematics.Random random) {
            if (Mates.value == null) return false;
            if (AwarenessTable == null) return false;
            int index = (int)entity.info.entityType;
            if (!AwarenessTable.ContainsKey(index)) return false;
            if (!entity.Is(out IMateable mate)) return false;

            Mate ofsp = Mates.value[AwarenessTable[index]];
            float delta = genetics.Get(ofsp.AmountPerParent);
            int amount = Mathf.FloorToInt(delta) + (random.NextFloat() < math.frac(delta) ? 1 : 0);
            uint childIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex(ofsp.ChildType);

            for (int i = 0; i < amount; i++) {
                Entity child = Config.CURRENT.Generation.Entities.Retrieve((int)childIndex).Entity;
                EntityManager.CreateEntity((int3)entity.position, childIndex, child);
                if (!child.Is(out IMateable childM)) continue;

                childM.Genetics = genetics.CrossGenes(
                    mate.Genetics,
                    ofsp.GeneMutationRate,
                    childIndex,
                    ref random
                );
            }

            return true;
        }

        public bool CanMateWith(Entity entity) {
            if (Mates.value == null) return false;
            if (AwarenessTable == null) return false;
            int index = (int)entity.info.entityType;
            return AwarenessTable.ContainsKey(index);
        }
    }
    public class ReproductionBehavior : IBehavior, IMateable {
        [JsonIgnore] public MateRecognition settings;

        private BehaviorEntity.Animal self;
        private GeneticsBehavior genetics; 
        private VitalityBehavior vitality;

        [JsonIgnore]
        public Genetics Genetics {
            get => this.genetics.Genes;
            set => this.genetics.Genes = value;
        }

        public bool CanMateWith(Entity entity) {
            if (vitality.IsDead) return false;

            RefTuple<bool> CanMate = new (true);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_CanMate, self, entity, CanMate);
            if (!CanMate.Value) return false;
            return settings.CanMateWith(entity);
        }
        public void MateWith(Entity entity) {
            if (!CanMateWith(entity)) return;
            if (settings.MateWithEntity(genetics.Genes, entity, ref self.random))
                vitality.Damage(genetics.Genes.Get(settings.MateCost));
            self.eventCtrl.RaiseEvent(GameEvent.Entity_Mate, self, entity);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MateRecognition), new MateRecognition());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Reproduction Behavior Requires AnimalSettings to have MateRecognition");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Reproduction Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Reproduction Behavior Requires AnimalInstance to have VitalityBehavior");

            this.self = self;
            self.Register<IMateable>(this);
            self.Register(this);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Reproduction Behavior Requires Animal to have MateRecognition");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Reproduction Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Reproduction Behavior Requires AnimalInstance to have VitalityBehavior");

            this.self = self;
            self.Register<IMateable>(this);
            self.Register(this);
        }

        public void Disable() {
            this.self = null;
        }
    }
}