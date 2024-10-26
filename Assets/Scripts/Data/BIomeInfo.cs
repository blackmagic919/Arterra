using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeInfo")]
public class BiomeInfo : ScriptableObject
{
    public Option<BiomeConditionsData> BiomeConditions;

    //This is the name register for this biome
    //As long as the names here are valid, the biome will
    //maintain its global register accesses
    public Option<List<string> > NameRegister;

    [Header("Underground Generation")]
    [Range(0, 1)]
    public float AtmosphereFalloff;

    [Header("Material Layers")]
    public Option<List<Option<BMaterial> > > GroundMaterials = new();
    public Option<List<Option<BMaterial> > > SurfaceMaterials = new ();

    [Header("Structures")]
    public Option<List<Option<TerrainStructure> > > Structures = new ();

    [Header("Entities")]
    public Option<List<Option<EntityGen> > > Entities = new ();

    public IEnumerable<TerrainStructure> StructureSerial{
        get{
            Registry<StructureData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Structures;
            return Structures.value.Select(x => Serialize(x.value, reg.RetrieveIndex(NameRegister.value[x.value.Structure])));
        }
    }

    public IEnumerable<EntityGen> EntitySerial{
        get{
            Registry<EntityAuthoring> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Entities;
            return Entities.value.Select(x => Serialize(x.value, reg.RetrieveIndex(NameRegister.value[x.value.Entity])));
        }
    }

    public IEnumerable<BMaterial> MaterialSerial(List<Option<BMaterial> > Materials){
        Registry<MaterialData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Materials.value.MaterialDictionary;
        return Materials.Select(x => Serialize(x.value, reg.RetrieveIndex(NameRegister.value[x.value.Material])));
    }

    TerrainStructure Serialize(TerrainStructure x, int Index){
        x.Structure = Index;
        return x;
    }
    EntityGen Serialize(EntityGen x, int Index){
        x.Entity = Index;
        return x;
    }

    BMaterial Serialize(BMaterial x, int Index){
        x.Material = Index;
        return x;
    }


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
        public int Material;

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
        public int Structure;
    }
    [Serializable]
    public struct EntityGen{
        public int Entity;
        [Range(0,1)]
        public float ChancePerCoord;
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