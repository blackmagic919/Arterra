using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeInfo")]
public class BiomeInfo : ScriptableObject
{
    public BiomeConditionsData BiomeConditions;

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

    public void OnValidate(){ BiomeConditions.Validate(); }

    [System.Serializable]
    public class DensityGrad
    {
        public AnimationCurve DensityCurve;
        public int upperLimit;
        public int lowerLimit;
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

    [System.Serializable]
    public class BiomeConditionsData
    {

        [Space(10)]
        [Range(0, 1)]
        public float TerrainStart;
        [Range(0, 1)]
        public float TerrainEnd;
        
        [Space(10)]
        [Range(0, 1)]
        public float ContinentalStart;
        [Range(0, 1)]
        public float ContinentalEnd;//

        [Range(0, 1)]
        public float ErosionStart;
        [Range(0, 1)]
        public float ErosionEnd;

        [Space(10)]
        [Range(0, 1)]
        public float SquashStart;
        [Range(0, 1)]
        public float SquashEnd;

        [Space(10)]
        [Range(0, 1)]
        public float AtmosphereStart;
        [Range(0, 1)]
        public float AtmosphereEnd;

        [Space(10)]
        [Range(0, 1)]
        public float HumidStart;
        [Range(0, 1)]
        public float HumidEnd;

        public void Validate()
        {
            ContinentalEnd = Mathf.Max(ContinentalStart, ContinentalEnd);
            ErosionEnd = Mathf.Max(ErosionStart, ErosionEnd);
            TerrainEnd = Mathf.Max(TerrainStart, TerrainEnd);
            SquashEnd = Mathf.Max(SquashStart, SquashEnd);
            AtmosphereEnd = Mathf.Max(AtmosphereStart, AtmosphereEnd);
            HumidEnd = Mathf.Max(HumidStart, HumidEnd);
        }

    }

}