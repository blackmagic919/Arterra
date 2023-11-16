using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeInfo")]
public class BiomeInfo : UpdatableData
{
    public string BiomeName;
    public List<BMaterial> Materials;
    public List<TerrainStructure> Structures;
    [Range(0, 1)]
    [Tooltip("The percentage of structure points considered to generate a structure")]
    public float structurePointDensity;

    [HideInInspector]
    float[] _StucturePrefixSum;

    public int seed;

    [System.Serializable]
    public class DensityGrad
    {
        public AnimationCurve DensityCurve;
        public int upperLimit;
        public int lowerLimit;
    }

    void EnsureUnit()
    {
        float totalPercentage = 0;
        foreach(TerrainStructure structure in Structures) { totalPercentage += structure.ChancePerStructurePoint; };
        if (totalPercentage == 0) return;
        foreach (TerrainStructure structure in Structures) { structure.ChancePerStructurePoint /= totalPercentage; };
    }

    protected override void OnValidate()
    {
        EnsureUnit();
        _StucturePrefixSum = new float[Structures.Count+1];
        for (int i = 1; i <= Structures.Count; i++) {
            _StucturePrefixSum[i] = _StucturePrefixSum[i-1] + Structures[i-1].ChancePerStructurePoint;
        }
    }


    public TerrainStructure GetStructure(float percentage)
    {
        int index = CustomUtility.BinarySearch(percentage, ref _StucturePrefixSum, (float val, float target) => target.CompareTo(val));
        return Structures[index];
    }

    [System.Serializable]
    public struct DensityFunc
    {
        public int lowerLimit;
        public int upperLimit;
        public int center;

        public float multiplier;
        public float power;

        public readonly float GetDensity(float y)
        {
            y = Mathf.Clamp(y, lowerLimit, upperLimit);

            float percent = y > center ?
                1-Mathf.InverseLerp(center, upperLimit, y) :
                Mathf.InverseLerp(lowerLimit, center, y);

            return Mathf.Pow(percent, power) * multiplier;
        }
    }

    [System.Serializable]
    public class BMaterial
    {
        public int materialIndex;

        [Range(0, 1)]
        [Tooltip("What type of generation to prefer, 0 = coarse, 1 = fine")]
        public float genNoiseSize;
        [Range(0, 1)]
        [Tooltip("What shape is the generation, 0, 1 = circles, 0.5 = lines")]
        public float genNoiseShape;

        public DensityFunc VerticalPreference;
    }

    [System.Serializable]
    public class TerrainStructure
    {
        [Range(0, 1)]
        [Tooltip("Cumulative chance for structure point to turn into this structure")]
        public float ChancePerStructurePoint = 0;
        public DensityFunc VerticalPreference;
        public StructureData structureData;
    }


}