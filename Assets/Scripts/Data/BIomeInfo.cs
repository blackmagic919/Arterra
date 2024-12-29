using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Utils;

namespace Biome {

public class CInfo<TCond> : Info where TCond : IBiomeCondition{
    public Option<TCond> BiomeConditions;
    public void OnValidate(){ BiomeConditions.value.Validate(); }
}
[CreateAssetMenu(menuName = "Generation/Biomes/BiomeInfo")]
public class Info : ScriptableObject
{

    //This is the name register for this biome
    //As long as the names here are valid, the biome will
    //maintain its global register accesses
    public Option<List<string> > NameRegister;

    [Header("Material Layers")]
    public Option<List<Option<BMaterial> > > GroundMaterials = new();
    public Option<List<Option<BMaterial> > > SurfaceMaterials = new ();

    [Header("Structures")]
    public Option<List<Option<TerrainStructure> > > Structures = new ();

    [Header("Entities")]
    public Option<List<Option<EntityGen> > > Entities = new ();

    [JsonIgnore]
    public IEnumerable<TerrainStructure> StructureSerial{
        get{
            Registry<StructureData> reg = WorldStorageHandler.WORLD_OPTIONS.Generation.Structures;
            return Structures.value.Select(x => Serialize(x.value, reg.RetrieveIndex(NameRegister.value[x.value.Structure])));
        }
    }

    [JsonIgnore]
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
        public float frequency;
        public float multiplier;
        [Range(0, 1)]
        public float height;
    }

    [System.Serializable]
    public struct TerrainStructure
    {
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
}

public interface IBiomeCondition
{
    public abstract int GetDimensions();
    public abstract void GetBoundDimension(ref BDict.RegionBound bound);
    public abstract void SetNode(BDict.RegionBound bound, int biome);
    public abstract void Validate();
}
}