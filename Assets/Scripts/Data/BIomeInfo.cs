using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeInfo")]
public class BiomeInfo : ScriptableObject
{
    public string BiomeName;

    [Header("Underground Generation")]
    [Range(0, 1)]
    [Tooltip("What type of generation to prefer, 0 = coarse, 1 = fine")]
    public float caveSize;
    [Range(0, 1)]
    [Tooltip("What shape is the generation, 0, 1 = circles, 0.5 = lines")]
    public float caveShape;
    [Tooltip("How frequent are caves, 0 = None, 1 = a lot")]
    public float caveFrequency;

    [Header("Material Layers")]
    public List<BMaterial> GroundMaterials;
    public List<BMaterial> SurfaceMaterials;

    [Header("Structures")]
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
    public struct BMaterial
    {
        public int materialIndex;

        [Range(0, 1)]
        [Tooltip("What type of generation to prefer, 0 = fine, 1 = coarse")]
        public float genSize;
        [Range(0, 1)]
        [Tooltip("What shape is the generation, 0, 1 = circles, 0.5 = lines")]
        public float genShape;

        public DensityFunc VerticalPreference;
    }

    [System.Serializable]
    public struct TerrainStructure
    {
        [Tooltip("Cumulative chance for structure point to turn into this structure")]
        public DensityFunc VerticalPreference;

        [Range(0, 1)]
        public float ChancePerStructurePoint;
        public uint structureIndex;
    }


}