using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Generation/GenerationHeight")]
public class GenerationHeightData : UpdatableData
{
    public List<BMaterial> Materials;
    public List<TerrainStructure> Structures;
    public int seed;

    [System.Serializable]
    public class DensityGrad
    {
        public AnimationCurve DensityCurve;
        public int upperLimit;
        public int lowerLimit;
    }

    [System.Serializable]
    public class BMaterial
    {
        public NoiseData generationNoise;
        public int materialIndex;

        public List<DensityGrad> VerticalPreference;
        public AnimationCurve generationPref;
    }

    [System.Serializable]
    public class TerrainStructure
    {
        public float baseFrequencyPerChunk;
        public List<DensityGrad> VerticalPreference;
        public StructureData structureData;
    }


}