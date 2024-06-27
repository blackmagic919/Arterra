using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeInfo")]
public class BiomeInfo : ScriptableObject
{
    public Option<BiomeConditionsData> BiomeConditions;

    [Header("Underground Generation")]
    [Range(0, 1)]
    public float AtmosphereFalloff;

    [Header("Material Layers")]
    public Option<List<Option<BMaterial> > > GroundMaterials;
    public Option<List<Option<BMaterial> > > SurfaceMaterials;

    [Header("Structures")]
    public Option<List<Option<TerrainStructure> > > Structures;

    public void OnValidate(){ BiomeConditions.value.Validate(); }

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
    public struct BiomeConditionsData
    {

        [Space(10)]
        [Range(0, 1)]
        public float TerrainStart;
        [Range(0, 1)]
        public float TerrainEnd;

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
        public float CaveFreqStart;
        [Range(0, 1)]
        public float CaveFreqEnd;

        [Space(10)]
        [Range(0, 1)]
        public float CaveSizeStart;
        [Range(0, 1)]
        public float CaveSizeEnd;

        [Space(10)]
        [Range(0, 1)]
        public float CaveShapeStart;
        [Range(0, 1)]
        public float CaveShapeEnd;

        public void Validate()
        {
            ErosionEnd = Mathf.Max(ErosionStart, ErosionEnd);
            TerrainEnd = Mathf.Max(TerrainStart, TerrainEnd);
            SquashEnd = Mathf.Max(SquashStart, SquashEnd);
            CaveFreqEnd = Mathf.Max(CaveFreqStart, CaveFreqEnd);
            CaveSizeEnd = Mathf.Max(CaveSizeStart, CaveSizeEnd);
            CaveShapeEnd = Mathf.Max(CaveShapeStart, CaveShapeEnd);
        }

    }

}