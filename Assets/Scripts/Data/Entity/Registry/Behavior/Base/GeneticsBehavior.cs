using Unity.Mathematics;
using Newtonsoft.Json;
using System.Collections.Generic;
using System;
using UnityEngine;
using Arterra.Configuration;

namespace Arterra.Data.Entity.Behavior {

    public class GeneticsSettings : IBehaviorSetting {
        [Serializable]
        public struct GeneFeature {
            public MSettings name;
            public float var;
            public float geneWeight;
        }
        
        public Option<List<GeneFeature>> Genes;
        public bool NormalizeWeights = true;
        public List<GeneFeature> Genetics => Genes.value;
        [JsonIgnore][HideInInspector][UISetting(Ignore = true)]
        public Dictionary<MSettings, int> GeneIndex;
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            GeneIndex = new Dictionary<MSettings, int>();
            Genes.value ??= new List<GeneFeature>();
            for(int i = 0; i < Genes.value.Count; i++) {
                GeneFeature gene = Genes.value[i];
                GeneIndex[gene.name] = i;
            }
            if (!NormalizeWeights) return;
            double totalWeight = 0;
            foreach(var feature in Genetics) {
                totalWeight += feature.var;
            } for (int i = 0; i < Genetics.Count; i++) {
                GeneFeature gene = Genetics[i];
                Genetics[i] = new GeneFeature {
                    name = gene.name,
                    var = gene.var,
                    geneWeight = (float)(gene.geneWeight/totalWeight),
                };
            }
        }

        public object Clone() {
            return new GeneticsSettings {
                Genes = this.Genes,
            };
        }
    }
    public class Genetics : SpeciesBehavior {
        private GeneticsSettings settings;
        [JsonProperty]
        private float[] _genes;
        [JsonProperty]
        private uint entityIndex;
        [JsonProperty]
        private bool initialized = false;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Modifiers, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(GeneticsSettings), new GeneticsSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Behavior Genetics requires GeenticsSettings object");
            if (initialized) return;
            this.entityIndex = self.info.entityType;

            int geneCount = settings.Genetics.Count;
            _genes = new float[geneCount];
            initialized = true;

            for (int i = 0; i < geneCount; i++) {
                _genes[i] = self.random.NextFloat(-1f, 1f);
            } NormalizeGenes(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Behavior Genetics requires GeenticsSettings object");
            entityIndex = self.info.entityType;
        }

        public Genetics() {
            initialized = false;
            _genes = null;
        }
        public Genetics(uint entityIndex, float[] genes) {
            this.entityIndex = entityIndex;
            this.initialized = true;
            _genes = genes;
        }

        public float GetRawGene(MSettings name) {
            if (!initialized) return 0;
            return math.clamp(_genes[settings.GeneIndex[name]], -1, 1);
        }
        public float Get(MSettings name, float baseValue) {
            if (!initialized) return 0;
            if (!settings.GeneIndex.TryGetValue(name, out int geneIndex))
                return baseValue;
            GeneticsSettings.GeneFeature feature = settings.Genetics[geneIndex];
            if (!float.IsFinite(baseValue)) return baseValue;
            float interp = math.clamp(_genes[geneIndex], -1, 1);
            return baseValue + baseValue * feature.var * interp;
        }


        public Genetics CrossGenes(Genetics mate, float mutationRate, uint offspringIndex, ref Unity.Mathematics.Random rng) {
            float[] parent1 = _genes; float[] parent2 = mate._genes;
            if (entityIndex == mate.entityIndex && entityIndex != offspringIndex)
                return new Genetics();
            else if (entityIndex != offspringIndex)
                parent1 = parent2;
            else if (mate.entityIndex != offspringIndex)
                parent2 = parent1;

            float[] childGenes = new float[settings.Genetics.Count];
            for (int i = 0; i < settings.Genetics.Count; i++) {
                //Dropout Inheritance
                if (rng.NextBool()) childGenes[i] = parent1[i];
                else childGenes[i] = parent2[i];
                //Mutation
                childGenes[i] += NextGaussian(mutationRate, ref rng);
            }

            Genetics child = new(entityIndex, childGenes);
            NormalizeGenes(child);
            return child;
        }

        private void NormalizeGenes(Genetics child) {
            double totalWeight = 0;
            double avgGeneStrength = 0;
            
            float[] genes = child._genes;
            for (int i = 0; i < genes.Length; i++) {
                totalWeight += settings.Genetics[i].geneWeight;
            }
            for (int i = 0; i < genes.Length; i++) {
                genes[i] = math.clamp(genes[i], -1, 1);
                avgGeneStrength += genes[i] * genes[i] * (settings.Genetics[i].geneWeight / totalWeight);
            }
            avgGeneStrength = math.sqrt(avgGeneStrength);
            //Only apply normalization if avg gene strength is abnormally high
            if (avgGeneStrength <= 0.5f) return;

            for (int i = 0; i < genes.Length; i++) {
                float magnitude = math.abs(genes[i]);
                float sign = math.sign(genes[i]);
                genes[i] = sign * magnitude *
                    (float)(1 - avgGeneStrength *
                    (settings.Genetics[i].geneWeight / totalWeight));
            }
        }

        private static float NextGaussian(float stdDev, ref Unity.Mathematics.Random rng) {
            double u1 = 1.0f - rng.NextFloat();
            double u2 = 1.0f - rng.NextFloat();
            // Box-Muller transform
            double randStdNormal = Math.Sqrt(-2.0f * Math.Log(u1)) *
                                Math.Sin(2.0f * Math.PI * u2);

            return (float)(stdDev * randStdNormal); // mean = 0
        }

    }
}

