using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Configuration.Quality
{
/// <summary>
/// Settings controlling the detail of generated terrain and the frequency of its updates. 
/// </summary>
[CreateAssetMenu(menuName = "Generation/Settings Wrapper")]
public class Terrain : ScriptableObject
{
    /// <summary>
    /// The maximum amount of resource load that can be used by a single frame.
    /// The load for a task is specified by <see cref="Arterra.Engine.Terrain.OctreeTerrain.taskLoadTable"/>
    /// </summary>
    public int maxFrameLoad = 50; //GPU load
    /// <summary>
    /// The maximum distance in grid space the viewer needs to move from the previous position used to update the terrain
    /// to trigger the terrain to update itself. This is so that small movements over critical areas do not 
    /// cause repeated terrain updates.
    /// </summary>
    public int viewDistUpdate = 32;

    /// <summary>
    /// The maximum depth of the terrain octree; the amount of layers betweeen the smallest leaf node and the root.
    /// Given the size of the smallest leaf node is <see cref="MinChunkRadius"/>, the size of the root node is (<see cref="MinChunkRadius"/> * 2^<see cref="MaxDepth"/>).
    /// </summary>
    public int MaxDepth;
    
    /// <summary>
    /// The <see cref="Core.Terrain.OctreeTerrain.Octree.Node.GetMaxDist(Unity.Mathematics.int3)"> component distance </see>, 
    /// in terms of the chunk space away from  the viewer of the farthest chunk that will become a real chunk. 
    /// As real chunks define the interactive game environment, this effectively defines the size of the game 
    /// environment relative to the size of the environment chunk (chunk space).
    /// </summary>
    public int MinChunkRadius;

    /// <summary>
    /// The balance factor of the terrain octree. Balance factor refers to the maximum difference in depth between
    /// two nodes in the octree physically adjacent to each other in space. Accordingly the octree is said to be 
    /// (<see cref="Balance"/> + 1 : 1) balanced. A lower balance will result in a smoother LoD transition but a 
    /// larger amount of terrain chunks, decreasing performance.
    /// </summary>
    [Range(1, 8)]
    public int Balance;

    /// <summary>
    /// The minimum <see cref="Core.Terrain.OctreeTerrain.Octree.Node.GetMaxDist(Unity.Mathematics.int3)"> component distance </see> in 
    /// chunk space on top of <see cref="MinChunkRadius"/> away from the viewer of the farthest chunk that will be cached in <see cref="GPUDensityManager"/>. 
    /// Chunks not cached in <see cref="GPUDensityManager"/> are incapable of reflecting terrain changes and displaying atmospheric effects. Increasing this value
    /// thus increases the size of the atmosphere at the cost of more GPU memory usage. 
    /// </summary>
    [Range(1, 100)]
    public int MapExtendDist;

    /// <summary> The maximum depth within the terrain octree that structures will attempt to be generated. </summary>
    /// <remarks> The process of generating structures scales by a factor of 8 for every increasing depth level. As
    /// larger chunks can contain a larger amount of structures, the process becomes exponentially more expensive. </remarks>
    public int MaxStructureDepth; // >= 0 obviously

    /// <summary> The final scaling factor of the terrain; the scale between the grid space and the world space. </summary>
    [UISetting(Alias = "Terrain Scale")]
    public float lerpScale = 1f;

    /// <summary>
    /// The IsoValue of the terrain. The IsoValue is the density of the terrain that is considered the surface of the ground, 
    /// or the threshold of the surface. <see cref="IsoLevel"/> represents the IsoValue as a percentage of the difference between
    /// the maximum and minimum density of a map entry.
    /// </summary>
    [Range(0, 1)]
    public float IsoLevel;

    /// <summary>
    /// The size in grid space of the smallest node, a <see cref="Core.Terrain.TerrainChunk.RealChunk">Real Chunk</see>, in the terrain octree. 
    /// This is synonymous to the axis-amount of map entries sampled by any chunk regardless of depth.
    /// </summary>
    [UISetting(Ignore = true)]
    [Range(0, 64)]
    public int mapChunkSize = 64; //Number of cubes; Please don't change

    /// <summary>
    /// The depth within the terrain octree that will be used to organize chunk files into folders. A region
    /// folder concurrently holds a maximum of 2^<see cref="FileRegionDepth"/> chunk files, similar to an octree node
    /// of that depth.This is useful in expediting chunk-reading operations as described in <see cref="ChunkStorageManager"/>. 
    /// </summary> <remarks>Alteration of this value in a world with modified chunks may cause data to be lost.</remarks>
    public int FileRegionDepth = 5; //Number of chunks in a file region

    /// <summary>
    /// The total size of a chunk's transition voxel as a percentage of a chunk's normal voxel size. When a chunk
    /// borders another chunk of a different depth, the transition voxel is used to blend the two chunks together.
    /// If multiple transition layers are required, each layer will be smaller such that the total width of the transition
    /// is equal to <see cref="transitionWidth"/>.
    /// </summary>
    [Range(0, 1)]
    public float transitionWidth = 0.25f;

    private void OnEnable()
    {
        IsoLevel = Mathf.Clamp(IsoLevel, 0.00001f, 0.99999f);
    }
}
}