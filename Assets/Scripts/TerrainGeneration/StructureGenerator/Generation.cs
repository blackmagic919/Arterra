using UnityEngine;

namespace WorldConfig.Generation.Structure{
/// <summary>
/// A collection of settings that describe how structures are generated. 
/// Any terrain not generated through noise-based generation should be created 
/// through a structure. See <seealso href="https://blackmagic919.github.io/AboutMe/2024/06/08/Structure%20Planning/">
/// here</seealso> for more information.
/// </summary>
[CreateAssetMenu(menuName = "Generation/Structure/Structure Settings")]
public class Generation : ScriptableObject
{
    /// <summary>
    /// A registry containing all structures that can be generated. 
    /// The number of structures that can be generated is limited by this registry.
    /// See <see cref="StructureData"/> for more info.
    /// </summary>
    public Registry<StructureData> StructureDictionary;
    /// <summary>
    /// The amount of structures that attempt to be placed per <see cref="TerrainGeneration.TerrainChunk.RealChunk"> Real Chunk </see>
    /// sampled. The higher this value, the more structures that will attempt to be placed per the same region of space resulting in 
    /// a higher density of structures.
    /// </summary> 
    /// <remarks>The amount of checks is solely space dependent meaning a chunk spanning a larger region in space will perform
    /// proportionately more checks. The amount of checks performed grows at a rate of <c> 8^depth * StructureChecksPerChunk</c>
    /// where <see cref="TerrainGeneration.TerrainChunk.depth">depth</see> is the size of a chunk of that layer within the octree.</remarks>
    [UISetting(Message = "Describes How Dense & Large Structures Generate At the Cost of Performance")]
    public int StructureChecksPerChunk;
    /// <summary>
    /// How quickly the amount of structure checks falls off with the size of the structure. The size of a structure
    /// is categorized by the <see cref="StructureData.Settings.minimumLOD"/> of the structure, which reflects the size in chunk space
    /// of the largest side-length of the structure. See <see href="https://blackmagic919.github.io/AboutMe/2024/06/08/Structure%20Planning/"> 
    /// here </see> for more information.
    /// </summary>
    [Range(1, 5)]
    public float LoDFalloff;
    /// <summary>
    /// The maximum LoD that a structure can generate from. The LoD of a structure is determined by <see cref="StructureData.Settings.minimumLOD"/>.
    /// Equivalently, describes the maximum side-length of a structure in chunk space that is supported. I.e. the maximum side-length any
    /// structure can take is (maxLoD * <see cref="Quality.Terrain.mapChunkSize"/>) without risking corrupted generation.
    /// </summary>
    public int maxLoD;
}}