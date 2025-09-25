using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using Unity.Mathematics;

public class Genetics {
    [Serializable]
    public struct GeneFeature {
        public float mean;
        public float var;
        public float geneWeight;
        [HideInInspector]
        [NonSerialized]
        public int geneIndex;
    }
    private static Dictionary<uint, List<GeneFeature>> EntityGenetics;
    [JsonProperty]
    private readonly float[] _genes;
    [JsonProperty]
    private readonly uint entityIndex;
    public static void ClearGeneology() => EntityGenetics?.Clear();
    public static void AddGene(uint entityIndex, ref GeneFeature gene) {
        EntityGenetics ??= new Dictionary<uint, List<GeneFeature>>();
        if (!EntityGenetics.TryGetValue(entityIndex, out List<GeneFeature> genes)) {
            genes = new List<GeneFeature>();
            EntityGenetics[entityIndex] = genes;
        }
        gene.geneIndex = genes.Count;
        genes.Add(gene);
    }

    public Genetics() { }
    public Genetics(uint entityIndex, float[] genes) {
        this.entityIndex = entityIndex;
        _genes = genes;
    }
    public Genetics(uint entityIndex, ref Unity.Mathematics.Random rng) {
        if (EntityGenetics == null)
            throw new Exception("Entity Genetics Not Initialized");
        if (!EntityGenetics.ContainsKey(entityIndex))
            throw new Exception($"Entity index {entityIndex} not found");

        this.entityIndex = entityIndex;
        int geneCount = EntityGenetics[entityIndex].Count;
        _genes = new float[geneCount];

        for (int i = 0; i < geneCount; i++) {
            _genes[i] = rng.NextFloat(-1f, 1f);
        }

        NormalizeGenes();
    }

    public float GetRawGene(GeneFeature gene) {
        if (_genes == null) return gene.mean;
        return math.clamp(_genes[gene.geneIndex], -1, 1);
    }
    public float Get(GeneFeature gene) {
        if (_genes == null) return gene.mean;
        if (!float.IsFinite(gene.mean)) return gene.mean;
        float interp = math.clamp(_genes[gene.geneIndex], -1, 1);
        return gene.mean + gene.mean * gene.var * interp;
    }

    public int GetInt(GeneFeature gene) {
        return Mathf.RoundToInt(Get(gene));
    }

    public Genetics CrossGenes(Genetics mate, float mutationRate, uint offspringIndex, ref Unity.Mathematics.Random rng) {
        List<GeneFeature> geneTemplate = EntityGenetics[entityIndex];
        float[] parent1 = _genes; float[] parent2 = mate._genes;
        if (entityIndex == mate.entityIndex && entityIndex != offspringIndex)
            return new Genetics(offspringIndex, ref rng);
        else if (entityIndex != offspringIndex)
            parent1 = parent2;
        else if (mate.entityIndex != offspringIndex)
            parent2 = parent1;

        float[] childGenes = new float[geneTemplate.Count];
        for (int i = 0; i < geneTemplate.Count; i++) {
            //Dropout Inheritance
            if (rng.NextBool()) childGenes[i] = parent1[i];
            else childGenes[i] = parent2[i];
            //Mutation
            childGenes[i] += NextGaussian(mutationRate, ref rng);
        }

        Genetics child = new(entityIndex, childGenes);
        child.NormalizeGenes();
        return child;
    }

    private void NormalizeGenes() {
        List<GeneFeature> geneTemplate = EntityGenetics[entityIndex];
        double totalWeight = 0;
        double avgGeneStrength = 0;

        for (int i = 0; i < _genes.Length; i++) {
            totalWeight += geneTemplate[i].geneWeight;
        }
        for (int i = 0; i < _genes.Length; i++) {
            _genes[i] = math.clamp(_genes[i], -1, 1);
            avgGeneStrength += _genes[i] * _genes[i] * (geneTemplate[i].geneWeight / totalWeight);
        }
        avgGeneStrength = math.sqrt(avgGeneStrength);
        //Only apply normalization if avg gene strength is abnormally high
        if (avgGeneStrength <= 0.5f) return;

        for (int i = 0; i < _genes.Length; i++) {
            float magnitude = math.abs(_genes[i]);
            float sign = math.sign(_genes[i]);
            _genes[i] = sign * magnitude *
                (float)(1 - avgGeneStrength *
                (geneTemplate[i].geneWeight / totalWeight));
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


