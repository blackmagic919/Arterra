using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Editor;

namespace Arterra.Data.Biome{

[CreateAssetMenu(menuName = "Generation/Biomes/Selection")][Serializable]
/// <summary> A catalogue of a group of biomes and their generation
/// behavior. Note, this does not contain placement information. </summary>
public class BiomeVariety : Category<BiomeVariety>{
    /// <summary> Generic setting shared by all biomes, see <see cref="Info"/> for more information. </summary>
    public Option<Info> info;
}

[Serializable]
public class BiomeSelection {
    public Catalogue<BiomeVariety> varieties;
    /// <summary> The conditions that define the placement of the biome. </summary>
    public BiomePlacement placement;
}
 
/// <summary>
/// The settings detailing all generation aspects controlled by a biome. All biomes
/// must always define these aspects as each respective system needs to know what to do
/// if a specific biome is encountered. 
/// </summary>
[Serializable]
public class Info
{
    /// <summary>
    /// The registry names of all entries referencing registries within <see cref="Info"/>. When an element such as
    /// a material, structure, or entry needs to reference an entry in an external registry, they can indicate the index
    /// within this list of the name of the entry within the registry that they are referencing. This allows for the biome
    /// module to be decoupled from the rest of the world's configuration. 
    /// </summary>
    public Option<List<string> > Names;

    /// <summary> A list containing the generation pattern of solid materials within the biome. This list is considered
    /// only if the density of the map entry is greater than <see cref="Quality.Terrain.IsoLevel"/>(i.e. undeground).
    /// </summary> <remarks> When considered, all materials within the list will attempt to be placed, meaning the time 
    /// complexity of material assignment is O(n) with respect to the number of materials in the list.</remarks>
    [Header("Material Layers")]
    public Option<List<Option<BMaterial> > > GroundMaterials = new();
    /// <summary> A list containing the generation pattern of non-solid materials within the biome. This list is considered
    /// only if the density of the map entry is less than <see cref="Quality.Terrain.IsoLevel"/>(i.e. above ground).
    /// </summary> <remarks> When considered, all materials within the list will attempt to be placed, meaning the time 
    /// complexity of material assignment is O(n) with respect to the number of materials in the list.</remarks>
    public Option<List<Option<BMaterial> > > SurfaceMaterials = new ();
    /// <summary>
    /// A list containing the generation pattern of liquid materials within the biome. This list is considered
    /// when the density of the map entry is less than <see cref="Quality.Terrain.IsoLevel"/>(i.e. above ground)
    /// and the point lies above the terrain surface and below the global water level, see <see cref="Generation.Map.waterHeight"/>
    /// for more information.</summary>
    public Option<List<Option<BMaterial> > > LiquidMaterials = new ();

    /// <summary> A list containing all structures that will attempt to generate within the biome. Anything terrain feature
    /// not a result of noise based generation should naturally be created through a structure. <see cref="Structure"/> 
    /// for more information.
    /// </summary>
    [Header("Structures")]
    public Option<List<Option<TerrainStructure> > > Structures = new ();

    /// <summary>
    /// A list containing all entites that will attempt to generate within the biome. Unlike structures and materials,
    /// entities will only be generate if the chunk is being generated for the first time <b>ever</b> since the world first
    /// was created. Otherwise, entities will be loaded from an entity chunk file. 
    /// </summary>
    [Header("Entities")]
    public Option<List<Option<EntityGen> > > Entities = new ();

    /// <summary> A list containing all structure systems that will attempt to generate within the biome. 
    /// Structure systems are collections of structures that form interconnected graphs, such as dungeons and labyrinths </summary>
    [Header("StructureSystems")]
    public Option<List<Option<BStructSystem> > > StructureSystems = new ();

    /// <summary> A getter property that deserializes all structures by recoupling them with the current world's configuration. This involves
    /// retrieving the real indices of the structures within the external <see cref="Config.GenerationSettings.Structures"/> registry. </summary>
    [JsonIgnore]
    public IEnumerable<TerrainStructure> StructureSerial{
        get{
            Catalogue<Structure.StructureData> reg = Config.CURRENT.Generation.Structures.value.StructureDictionary;
            return Structures.value.Select(x => Serialize(x.value, reg.RetrieveIndex(Names.value[x.value.Structure])));
        }
    }

    /// <summary> A getter property that deserializes all structures by recoupling them with the current world's configuration. This involves
    /// retrieving the real indices of the structures within the external <see cref="Config.GenerationSettings.Structures"/> registry. </summary>
    [JsonIgnore]
    public IEnumerable<BStructSystem> StructureSystemSerial{
        get{
            Catalogue<Structure.Jigsaw.JigsawSystem> reg = Config.CURRENT.Generation.Structures.value.SystemDictionary;
            return StructureSystems.value.Select(x => Serialize(x.value, reg.RetrieveIndex(Names.value[x.value.StructureSystem])));
        }
    }

    /// <summary> A getter property that deserializes all entities by recoupling them with the current world's configuration. This involves
    /// retrieving the real indices of the entities within the external <see cref="Config.GenerationSettings.Entities"/> registry. </summary>
    [JsonIgnore]
    public IEnumerable<EntityGen> EntitySerial{
        get{
            Catalogue<Entity.Authoring> reg = Config.CURRENT.Generation.Entities;
            return Entities.value.Select(x => Serialize(x.value, reg.RetrieveIndex(Names.value[x.value.Entity])));
        }
    }

    /// <summary>
    /// Retrieves the deserialized version of a list of materials that are coupled through a reference to the <see cref="Names"/> 
    /// by recoupling them with the current world's configuration. This involves retrieving the real indices 
    /// of the materials within the external external <see cref="Config.GenerationSettings.Materials"/> registry.
    /// </summary>
    /// <param name="Materials">The list of materials that are coupled with <see cref="Names"/> that is to be decoupled. This is either 
    /// <see cref="GroundMaterials"/> or <see cref="SurfaceMaterials"/>. </param>
    /// <returns>An ordered collection of the recoupled with the external <see cref="Config.GenerationSettings.Materials"/> registry</returns>
    public IEnumerable<BMaterial> MaterialSerial(List<Option<BMaterial> > Materials){
        if(Materials == null) return null;
        Catalogue<Material.MaterialData> reg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        return Materials.Select(x => Serialize(x.value, reg.RetrieveIndex(Names.value[x.value.Material])));
    }

    TerrainStructure Serialize(TerrainStructure x, int Index){
        x.Structure = Index;
        return x;
    }

    BStructSystem Serialize(BStructSystem x, int Index){
        x.StructureSystem = Index;
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

    /// <summary>
    /// The settings for the generation pattern of a single material within a biome. 
    /// Each BMaterial describes a list of preferences, and the material placed is determined
    /// by the material with the closest match to its desired preference. This exact layout
    /// is copied to the GPU and used to generate the terrain.
    /// </summary>
    [Serializable]
    public struct BMaterial
    {
        
        /// <summary> The index of the name of the material within the <see cref="Names"/>.
        /// Once recoupled, this will point to the real index within the external 
        /// <see cref="Config.GenerationSettings.Materials"/> registry. </summary>
        [RegistryReference("Materials", "/info/value/Names")]
        public int Material;
        /// <summary>
        /// The preferred material size that the material should be generated at. The material size
        /// blends between the match closeness of <see cref="genShape"/> with <see cref="Map.CoarseMaterialNoise"/>
        /// and <see cref="Map.FineMaterialNoise"/> respectively.
        /// </summary>
        [Range(0, 1)]
        [Tooltip("What type of generation to prefer, 0 = fine, 1 = coarse")]
        public float genSize;
        /// <summary>
        /// The preferred shape of the material. The match closeness of the material is determined by the 
        /// distance of the noise parameter <see cref="Map.CoarseMaterialNoise"/> or <see cref="Map.FineMaterialNoise"/> to
        /// the value of <see cref="genShape"/>.
        /// </summary>
        [Range(0, 1)]
        [Tooltip("What shape is the generation, 0, 1 = circles, 0.5 = lines")]
        public float genShape;
        /// <summary>
        /// The frequency of the material. This is used to falloff the match closeness such that a frequency
        /// of 0 will result in a match closeness of 0 for all noise values (meaning it is never placed), 
        /// and a frequency of 1 will result in a linear match closeness.
        /// </summary>
        [Range(0, 1)]
        public float frequency;
        /// <summary>
        /// If the biome is a <see cref="SBiomeInfo"/>, describes the preferred height relative to the influence range
        /// that the material should be generated at. An influence height of 0 will result in the material being generated
        /// on the surface while an influence height of 1 will result in the material being generated at either the top or
        /// bottom of the biome depending on whether it's a <see cref="SurfaceMaterials">surface</see> or <see cref="GroundMaterials">ground</see> material.
        /// If the biome is a <see cref="CBiomeInfo"/>, this value is ignored.
        /// </summary>
        [Range(0, 1)]
        public float height;
    }

    /// <summary> The settings for the generation pattern of a single structure system within a biome.  </summary>
    [Serializable]
    public struct BStructSystem {
        /// <summary> The index of the name of the structure system within the <see cref="Names"/>.
        /// Once recoupled, this will point to the real index within the external 
        /// <see cref="Structure.Generation.SystemDictionary"/> registry. </summary>
        [RegistryReference("StructureSystem", "/info/value/Names")]
        public int StructureSystem;
        /// <summary> Same as <see cref="BMaterial.genSize"/> </summary>
        [Range(0, 1)]
        [Tooltip("What type of generation to prefer, 0 = fine, 1 = coarse")]
        public float genSize;
        /// <summary> Same as <see cref="BMaterial.genShape"/> </summary>
        [Range(0, 1)]
        [Tooltip("What shape is the generation, 0, 1 = circles, 0.5 = lines")]
        public float genShape;
        /// <summary> Same as <see cref="BMaterial.frequency"/> </summary>
        [Range(0, 1)]
        public float frequency;
       /// <summary> Same as <see cref="BMaterial.height"/> </summary>
        [Range(0, 1)]
        public float height;
    }

    /// <summary> The settings for the generation pattern of a single structure within a biome. </summary>
    [System.Serializable]
    public struct TerrainStructure
    {
        /// <summary> The chance that a randomly sampled point chooses to generate the current structure. The total number
        /// of points sampled per chunk is determined by the <see cref="Generation.Structure.Generation.StructureChecksPerChunk"/>.
        /// </summary> 
        /// <remarks>This chance is <b>dependent</b> on the chance of previous entries before it within <see cref="Info.Structures"/>.
        /// If the first structure's <see cref="ChancePerStructurePoint"/> is 1 (100%), then no other structures will be able to 
        /// generate as all structure points will attempt to place the first structure. </remarks>
        [Range(0, 1)]
        public float ChancePerStructurePoint;
        /// <summary> The index of the name within the <see cref="Names"/>, of the structure within 
        /// the external registry <see cref="Config.GenerationSettings.Structures"/> </summary>
        [RegistryReference("Structures", "/info/value/Names")]
        public int Structure;
    }
    /// <summary> The settings for the generation pattern of a single entity within a biome. </summary>
    [Serializable]
    public struct EntityGen{
        /// <summary> The index of the name within the <see cref="Names"/>, of the entity within 
        /// the external registry <see cref="Config.GenerationSettings.Entities"/> </summary>
        [RegistryReference("Entities", "/info/value/Names")]
        public int Entity;
        /// <summary> The chance that the entity will be spawned at a given coordinate <b>for every coordinate</b> it
        /// can generate at within a chunk. Whether an entity can generate at a coodrinate is determined by
        /// the <see cref="ProfileE">profile</see> of the entity. </summary>
        /// <remarks>It is recommended that this is a very small number as it is possible for an extremely large
        /// amount of coordinates to match the entity's <see cref="ProfileE">profile</see>. </remarks>
        [Range(0,1)]
        public float ChancePerCoord;
    }
}}