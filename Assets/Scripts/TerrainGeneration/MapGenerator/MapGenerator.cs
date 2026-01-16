using Unity.Mathematics;
using UnityEngine;
using static UtilityBuffers;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Collections;
using Arterra.Configuration;
using Arterra.Core.Storage;

namespace Arterra.Core.Terrain.Map{
/// <summary> A manager unique for every terrain chunk responsible for creating 
/// and grouping and abstracting various types of instructions used to
/// create the final 3D terrain map and the visual mesh. </summary>
public struct Creator
{
    /// <summary> Populates the final 3D biome map for the chunk. This involves describing the
    /// biome type associated with each map entry within the chunk. Normally, this process is implicitly 
    /// done when generating the default map(see <see cref="GenerateBaseChunk"/>), but that
    /// process does not retain this information. </summary>
    /// <param name="offset">The offset in grid space of the origin of the chunk.</param>
    /// <param name="surfaceData">A handle indicating the surface information for the chunk. This
    /// handle will be used to find the surface data within a <see cref="Config.Quality.MemoryBufferHandler.Storage">
    /// storage buffer</see>. See <see cref="Surface.Creator.SurfaceMapAddress"/> for more info. </param>
    /// <param name="chunkSize">The size of a <see cref="TerrainChunk.RealChunk"/> in grid space</param>
    /// <param name="mapSkip">The distance in grid space between two adjacent samples in the biome map. 
    /// Equivalently the size relative to a <see cref="TerrainChunk.RealChunk"/>.</param>
    public void PopulateBiomes(float3 offset, uint surfaceData, int chunkSize, int mapSkip) => Generator.GenerateBiomeData(offset, surfaceData, chunkSize, mapSkip);
    /// <summary> Generates the base terrain map information for a chunk. This is the 3D map defined 
    /// by noise functions responsible for creating the surface and cave structures of the terrain
    /// as well as assigning materials to the generated map. </summary>
    /// <param name="offset"> The offset in grid space of the origin of the chunk.</param>
    /// <param name="surfaceData">handle indicating the surface information for the chunk. This
    /// handle will be used to find the surface data within a <see cref="Config.Quality.MemoryBufferHandler.Storage">
    /// storage buffer</see>. See <see cref="Surface.Creator.SurfaceMapAddress"/> for more info. </param>
    /// <param name="chunkSize">The size of a <see cref="TerrainChunk.RealChunk"/> in grid space</param>
    /// <param name="mapSkip">The distance in grid space between two adjacent samples in the biome map. 
    /// Equivalently the size relative to a <see cref="TerrainChunk.RealChunk"/>.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    public void GenerateBaseChunk(float3 offset, uint surfaceData, int chunkSize, int mapSkip, float IsoLevel) => Generator.GenerateBaseData(offset, surfaceData, chunkSize, mapSkip, IsoLevel);
    /// <summary> Compresses the map data of the chunk into its compacted form which is actually stored and recognized by
    /// most systems. During generation, the map data is stored in 12-bytes(4-bytes for each field) as certain atomic 
    /// operations only operate on this level. However most systems recognize a compacted 4-byte form of the map data. </summary>
    /// <param name="chunkSize">The axis size of the map to be compressed. The amount of entries to be compressed is (<paramref name="chunkSize"/>^3) </param>
    public void CompressMap(int chunkSize) => Generator.CompressMapData(chunkSize);
    /// <summary> Copies the map data from a linearly encoded chunk on the CPU to a 
    /// <see cref="UtilityBuffers.TransferBuffer">transfer buffer</see> accessible by GPU-based tasks.  </summary>
    /// <param name="numPointsAxis">The axis size of the map to be copied, the length of <paramref name="chunkData"/>
    /// should be greater than or equal to (<i>numPointsAxis</i>^3)</param>
    /// <param name="offset">The offset within <paramref name="chunkData"/> to begin copying the MapData.</param>
    /// <param name="chunkData">A managed array containing the linearly encoded map information for a chunk.</param>
    public void SetMapInfo(int numPointsAxis, int offset, MapData[] chunkData){
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        UtilityBuffers.TransferBuffer.SetData(chunkData, offset, 0, numPoints);
    }
    /// <summary> Copies the map data from a linearly encoded unmanaged chunk on the CPU to a 
    /// <see cref="UtilityBuffers.TransferBuffer">transfer buffer</see> accessible by GPU-based tasks.</summary>
    /// <param name="numPointsAxis">The axis size of the map to be copied, the length of <paramref name="chunkData"/>
    /// should be greater than or equal to (<i>numPointsAxis</i>^3)</param>
    /// <param name="offset">The offset within <paramref name="chunkData"/> to begin copying the MapData.</param>
    /// <param name="chunkData">A Unity unamanged array containing the linearly encoded map information for a chunk.</param>
    public void SetMapInfo(int numPointsAxis, int offset, ref NativeArray<MapData> chunkData)
    {
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        UtilityBuffers.TransferBuffer.SetData(chunkData, offset, 0, numPoints);
    }
    
    /// <summary> Generates the mesh for the <see cref="TerrainChunk.RealChunk"/> at the specified location. Involves retrieving
    /// the saved map information stored by <see cref="GPUMapManager.RegisterChunkReal(int3, int, ComputeBuffer, int)"/>
    /// and generating the mesh using the marching cubes algorithm. If an invalid chunk is passed, or one that does not
    /// have a saved map, the behavior for this function is not defined. </summary>
    /// <param name="CCoord">The coordinate in chunk space, of the <see cref="TerrainChunk.RealChunk"/> whose mesh is generated.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk</param>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="Generator.GenerateTransition(uint, int, float)"/>
    /// for more info. </param>
    public void GenerateRealMesh(int3 CCoord, float IsoLevel, int chunkSize, uint neighborDepths){
        Generator.CollectRealMap(CCoord, chunkSize);
        Generator.GenerateMesh(chunkSize, IsoLevel);
        if(neighborDepths == 0) return;
        Generator.GenerateTransition(neighborDepths, chunkSize, IsoLevel);
    }
    /// <summary> Generates the mesh for a <see cref="TerrainChunk.VisualChunk"><b>normal</b> visual chunk </see> at the specified location. Normal
    /// visual chunks have stored map information through <see cref="GPUMapManager.RegisterChunkVisual(int3, int, ComputeBuffer, int)"/>, but
    /// because they can border fake chunks, they must also contain some default out-of-bound information in case they can't find it from 
    /// their neighbors. </summary>
    /// <param name="CCoord">The coordinate in chunk space of the origin of the chunk. </param>
    /// <param name="defAddress">The address within an <see cref="GPUMapManager.DirectAddress">indirect address buffer</see> 
    /// of the address of the base map information for the visual chunk. This includes dirty information belonging to the chunk
    /// as well as the default map for entries outside its own bounds but needed for mesh generation. </param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk</param>
    /// <param name="depth">The distance of the chunk from a leaf node within the <see cref="OctreeTerrain.BalancedOctree">chunk octree</see>. Identifies
    /// the size of the chunk relative to a <see cref="TerrainChunk.RealChunk"> real chunk </see>. See <see cref="TerrainChunk.depth"/> for more info.</param>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="Generator.GenerateTransition(uint, int, float)"/>
    /// for more info. </param>
    public void GenerateVisualMesh(int3 CCoord, int defAddress, float IsoLevel, int chunkSize, int depth, uint neighborDepths){
        Generator.CollectVisualMap(CCoord, defAddress, chunkSize, depth);
        Generator.GenerateMesh(chunkSize, IsoLevel);
        if(neighborDepths == 0) return;
        Generator.GenerateTransition(neighborDepths, chunkSize, IsoLevel);
    }
    /// <summary>  Generates the mesh for a <see cref="TerrainChunk.VisualChunk"><b>fake</b> visual chunk</see>. Because the map data is 
    /// not stored with only the default map being recreated on demand, a <i>fake mesh</i> is created in the sense that it is
    /// not only non-interactable, but also cannot be changed within the context of the game. </summary>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk</param>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="Generator.GenerateTransition(uint, int, float)"/>
    /// for more info.</param>
    public void GenerateFakeMesh(float IsoLevel, int chunkSize, uint neighborDepths) {
        Generator.GenerateMesh(chunkSize, IsoLevel);
        if(neighborDepths == 0) return;
        Generator.GenerateTransition(neighborDepths, chunkSize, IsoLevel);
    }
}

/// <summary> A static manager responsible for managing loading and access
/// of all compute-shaders used within the map and mesh generation process
/// of terrain generation. All instructions related to map/mesh
/// generation done by the GPU is streamlined from this module. </summary>
public static class Generator
{
    [Header("Terrain Generation Shaders")]
    static ComputeShader baseGenCompute;//
    static ComputeShader biomeGenCompute;//
    static ComputeShader mapCompressor;//
    static ComputeShader dMeshGenerator;//
    static ComputeShader transVoxelGenerator;//
    static ComputeShader meshInfoCollector;
    
    /// <summary> The offsets within the <see cref="UtilityBuffers.GenerationBuffer"> working buffer </see> of different 
    /// logical regions used for different tasks during the terrain generation process. See <see cref="GeoGenOffsets"/>
    /// for more information. </summary>
    public static GeoGenOffsets bufferOffsets;
    

    static Generator(){ //That's a lot of Compute Shaders XD
        baseGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/ChunkDataGen");
        biomeGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/FullBiomeSampler");
        mapCompressor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MapCompressor");
        dMeshGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/CMarchingCubes");
        meshInfoCollector = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/BaseMapCollector");
        transVoxelGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MarchTransitionCells");

        indirectCountToArgs = Resources.Load<ComputeShader>("Compute/Utility/CountToArgs");
    }

    /// <summary>  Presets all compute-shaders used through map and base mesh generation by acquiring 
    /// them and binding any constant values(information derived from the world's settings that 
    /// won't change until the world is unloaded) to them. Referenced by
    /// <see cref="SystemProtocol.Startup"/> </summary>
    public static void PresetData(){
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        Configuration.Generation.Map mesh = Config.CURRENT.Generation.Terrain.value;

        //Set Marching Cubes Data
        int numPointsAxes = rSettings.mapChunkSize;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), rSettings.Balance, 0);
        
        baseGenCompute.SetBuffer(0, "_SurfAddressDict", GenerationPreset.memoryHandle.Address);
        baseGenCompute.SetInt("caveFreqSampler", mesh.CaveFrequencyIndex);
        baseGenCompute.SetInt("caveSizeSampler", mesh.CaveSizeIndex);
        baseGenCompute.SetInt("caveShapeSampler", mesh.CaveShapeIndex);
        baseGenCompute.SetInt("coarseCaveSampler", mesh.CoarseTerrainIndex);
        baseGenCompute.SetInt("fineCaveSampler", mesh.FineTerrainIndex);
        baseGenCompute.SetInt("coarseMatSampler", mesh.CoarseMaterialIndex);
        baseGenCompute.SetInt("fineMatSampler", mesh.FineMaterialIndex);

        baseGenCompute.SetFloat("heightSFalloff", mesh.heightFalloff);
        baseGenCompute.SetFloat("atmoStrength", mesh.atmosphereFalloff);
        baseGenCompute.SetFloat("waterHeight", mesh.waterHeight);

        baseGenCompute.SetBuffer(0, "BiomeMap", UtilityBuffers.GenerationBuffer);
        baseGenCompute.SetBuffer(0, "BaseMap", UtilityBuffers.GenerationBuffer);
        baseGenCompute.SetInt("bSTART_map", bufferOffsets.rawMapStart);
        baseGenCompute.SetInt("bSTART_biome", bufferOffsets.biomeMapStart);

        biomeGenCompute.SetBuffer(0, "_SurfAddressDict", GenerationPreset.memoryHandle.Address);
        biomeGenCompute.SetInt("caveSizeSampler", mesh.CaveSizeIndex);
        biomeGenCompute.SetInt("caveShapeSampler", mesh.CaveShapeIndex);
        biomeGenCompute.SetInt("caveFreqSampler", mesh.CaveFrequencyIndex);

        biomeGenCompute.SetBuffer(0, "BiomeMap", UtilityBuffers.GenerationBuffer);
        biomeGenCompute.SetInt("bSTART_biome", bufferOffsets.biomeMapStart);
        biomeGenCompute.SetFloat("waterHeight", mesh.waterHeight);

        mapCompressor.SetBuffer(0, "rawData", UtilityBuffers.GenerationBuffer);
        mapCompressor.SetBuffer(0, "chunkData", UtilityBuffers.GenerationBuffer);
        mapCompressor.SetInt("bSTART_raw", bufferOffsets.rawMapStart);
        mapCompressor.SetInt("bSTART_chunk", bufferOffsets.mapStart);
        
        //They're all the same buffer lol
        dMeshGenerator.SetBuffer(0, "MapData", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        dMeshGenerator.SetInts("counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});
        dMeshGenerator.SetInt("meshSkipInc", 1); //we are only dealing with same size chunks in this model

        dMeshGenerator.SetInt("bSTART_map", bufferOffsets.mapStart);
        dMeshGenerator.SetInt("bSTART_dict", bufferOffsets.dictStart);
        dMeshGenerator.SetInt("bSTART_verts", bufferOffsets.vertStart);
        dMeshGenerator.SetInt("bSTART_baseT", bufferOffsets.baseTriStart);
        dMeshGenerator.SetInt("bSTART_waterT", bufferOffsets.waterTriStart);

        transVoxelGenerator.SetBuffer(0, "MapData", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "vertexes", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "triangles", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "triangleDict", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        transVoxelGenerator.SetBuffer(0, "FaceProperty", UtilityBuffers.TransferBuffer);
        transVoxelGenerator.SetInts("counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});

        transVoxelGenerator.SetInt("bSTART_map", bufferOffsets.mapStart);
        transVoxelGenerator.SetInt("bSTART_dict", bufferOffsets.dictStart);
        transVoxelGenerator.SetInt("bSTART_verts", bufferOffsets.vertStart);
        transVoxelGenerator.SetInt("bSTART_baseT", bufferOffsets.baseTriStart);
        transVoxelGenerator.SetInt("bSTART_waterT", bufferOffsets.waterTriStart);


        int kernel = meshInfoCollector.FindKernel("CollectReal");
        meshInfoCollector.SetBuffer(kernel, "MapData", UtilityBuffers.GenerationBuffer);
        meshInfoCollector.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        meshInfoCollector.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
        kernel = meshInfoCollector.FindKernel("CollectVisual");
        meshInfoCollector.SetBuffer(kernel, "MapData", UtilityBuffers.GenerationBuffer);
        meshInfoCollector.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        meshInfoCollector.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
        meshInfoCollector.SetBuffer(kernel, "_DirectAddress", GPUMapManager.DirectAddress);
        meshInfoCollector.SetInt("bSTART_map", bufferOffsets.mapStart);
    }

    /// <summary>Initializes just the basic buffer offsets within <see cref="bufferOffsets"/> to support map based mesh generation. </summary>
    public static void MinimalInitialize() {
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        //Set Marching Cubes Data
        int numPointsAxes = rSettings.mapChunkSize;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), rSettings.Balance, 0);
    }

    /// <summary> See <see cref="Creator.GenerateBaseChunk(float3, uint, int, int, float)"/> for info. </summary>
    public static void GenerateBaseData( Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip, float IsoLevel)
    {
        ComputeBuffer source = GenerationPreset.memoryHandle.GetBlockBuffer(surfaceData);
        baseGenCompute.SetBuffer(0, ShaderIDProps.SurfaceMemoryBuffer, source);

        baseGenCompute.SetFloat(ShaderIDProps.IsoLevel, IsoLevel);
        baseGenCompute.SetInt(ShaderIDProps.SurfaceAddress, (int)surfaceData);
        baseGenCompute.SetInt(ShaderIDProps.NumPointsPerAxis, numPointsPerAxis);
        
        SetSampleData(baseGenCompute, offset, mapSkip);

        baseGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        baseGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> See <see cref="Creator.PopulateBiomes(float3, uint, int, int)"/> for info. </summary>
    public static void GenerateBiomeData(Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip){
        ComputeBuffer source = GenerationPreset.memoryHandle.GetBlockBuffer(surfaceData);
        biomeGenCompute.SetBuffer(0, ShaderIDProps.SurfaceMemoryBuffer, source);
        biomeGenCompute.SetInt(ShaderIDProps.NumPointsPerAxis, numPointsPerAxis);
        biomeGenCompute.SetInt(ShaderIDProps.SurfaceAddress, (int)surfaceData);
        SetSampleData(biomeGenCompute, offset, mapSkip);

        biomeGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        biomeGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> See <see cref="Creator.CompressMap(int)"/> for info. </summary>
    public static void CompressMapData(int chunkSize){
        int numPointsAxes = chunkSize;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        mapCompressor.SetInt(ShaderIDProps.NumPoints, numPoints);
        mapCompressor.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        mapCompressor.Dispatch(0, numThreadsAxis, 1, 1);
    }

    /// <summary> Collects the map data for a <see cref="TerrainChunk.RealChunk">real chunk</see>. Retrieves the map data stored in a hashmap by <see cref="GPUMapManager"/> 
    /// and copies it into <see cref="UtilityBuffers.GenerationBuffer">working memory</see> where it can be accessed easier.
    /// Out-of-bound map information necessary for mesh generation is additionally copied; more accurately the first and last two
    /// entries of each axis of the map are retrieved from the stored map data submitted by neighboring chunks, discoverable through 
    /// the hashmap managed by <see cref="GPUMapManager"/>. To avoid gaps, a real chunk that invokes this function should avoid bordering 
    /// <see cref="TerrainChunk.VisualChunk"><b>fake</b> visual</see> chunks that are not saved at all and hence not discoverable. </summary>
    /// <param name="CCoord">The coordinate in chunk space of the origin of the chunk.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk.
    /// The amount of points in the map retrieved by this function is (<paramref name="chunkSize"/>+3)^3</param>
    public static void CollectRealMap(int3 CCoord, int chunkSize){
        int fChunkSize = chunkSize + 3;
        meshInfoCollector.SetInts(ShaderIDProps.CCoord, new int[]{CCoord.x, CCoord.y, CCoord.z});
        meshInfoCollector.SetInt(ShaderIDProps.NumPointsPerAxis, fChunkSize);
        meshInfoCollector.SetInt(ShaderIDProps.MapChunkSize, chunkSize);
        GPUMapManager.SetCCoordHash(meshInfoCollector);

        int kernel = meshInfoCollector.FindKernel("CollectReal");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        meshInfoCollector.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> Collects the map data for a <see cref="TerrainChunk.VisualChunk">normal visual chunk</see>. Retrieves the map data stored in a hashmap by <see cref="GPUMapManager"/> 
    /// and copies it into <see cref="UtilityBuffers.GenerationBuffer">working memory</see> where it can be accessed easier. Out-of-bound map information necessary for mesh 
    /// generation is additionally copied; more accurately the first and last two entries of each axis of the map are retrieved from the stored map data submitted by 
    /// neighboring chunks, discoverable through the hashmap managed by <see cref="GPUMapManager"/>. Because a normal visual chunk can border fake visual chunks which 
    /// are only capable of reflecting the readonly default map information, each normal visual chunk also contains neighboring default map information which it may copy when
    /// collecting if it cannot discover its neighbors. </summary>
    /// <param name="CCoord">The coordinate in chunk space of the origin of the chunk.</param>
    /// <param name="defaultAddress">The address within an <see cref="GPUMapManager.DirectAddress">indirect address buffer</see> 
    /// of the address of the base map information for the visual chunk. This includes dirty information belonging to the chunk
    /// as well as the default map for entries outside its own bounds. </param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk.
    /// The amount of points in the map retrieved by this function is (<paramref name="chunkSize"/>+3)^3</param>
    /// <param name="depth">The distance of the chunk from a leaf node within the <see cref="OctreeTerrain.BalancedOctree">chunk octree</see>. Identifies
    /// the distance between samples in a map of the resolution defined by <i>depth</i>.</param>
    public static void CollectVisualMap(int3 CCoord, int defaultAddress, int chunkSize, int depth){
        int fChunkSize = chunkSize + 3; int skipInc = 1 << depth;
        meshInfoCollector.SetInts(ShaderIDProps.CCoord, new int[]{CCoord.x, CCoord.y, CCoord.z});
        meshInfoCollector.SetInt(ShaderIDProps.NumPointsPerAxis, fChunkSize);
        meshInfoCollector.SetInt(ShaderIDProps.MapChunkSize, chunkSize);
        meshInfoCollector.SetInt(ShaderIDProps.DefaultAddress, defaultAddress);
        meshInfoCollector.SetInt(ShaderIDProps.SkipInc, skipInc);
        GPUMapManager.SetCCoordHash(meshInfoCollector);

        int kernel = meshInfoCollector.FindKernel("CollectVisual");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        meshInfoCollector.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }
    
    /// <summary> Generates the visual mesh for a chunk based off the map data stored in the <see cref="UtilityBuffers.GenerationBuffer">working buffer</see>.
    /// The mesh is generated using the marching cubes algorithm and is stored in a distributed form within the buffer in a way that avoids
    /// duplicated vertex information. Two seperate meshes are created for every chunk, one for the base terrain and one for liquids. </summary>
    /// <remarks>See <see href="https://paulbourke.net/geometry/polygonise/">here</see> to learn about marching cubes. </remarks>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk; the amount of cubes marched per axis of the chunk.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    public static void GenerateMesh(int chunkSize, float IsoLevel)
    {
        int numCubesAxes = chunkSize;
        int numPointsAxes = numCubesAxes + 1;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 3, 0);

        dMeshGenerator.SetFloat(ShaderIDProps.IsoLevel, IsoLevel);
        dMeshGenerator.SetInt(ShaderIDProps.NumCubesPerAxis, numCubesAxes);
        dMeshGenerator.SetInt(ShaderIDProps.NumPointsPerAxis, numPointsAxes);

        dMeshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        dMeshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    /// <summary> Generates the transition mesh for a chunk based off the map data stored in the <see cref="UtilityBuffers.GenerationBuffer">working buffer</see>
    /// and the resolution of the neighboring chunks that the current chunk is to blend with. The transition mesh is generated using the <see href="https://transvoxel.org/">
    /// transvoxel algorithm </see> which allows for smooth transitions between chunks of exactly twice the resolution. This function layers multiple transition
    /// meshes to allow for transitions between chunks of any power of 2 difference in resolution, thus supporting any octree <see cref="Quality.Terrain.Balance">
    /// balance factor</see>. </summary>
    /// <remarks> The time complexity of this function is O(m*n^2) where n is the resolution of the chunk and 
    /// m the number of transition faces necessary to blend between a chunk and all of its neighbors. </remarks>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="Generator.GenerateTransition(uint, int, float)"/>
    /// for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the transition face; the amount of cubes marched per axis of the face.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    public static void GenerateTransition(uint neighborDepths, int chunkSize, float IsoLevel){
        int numCubesAxis = chunkSize;
        int numPointsAxis = numCubesAxis + 1;
        TransFaceInfo[] transFaces = GetNeighborFaces(neighborDepths, numPointsAxis);
        int numTransFaces = transFaces.Length; if(numTransFaces == 0) return;
        UtilityBuffers.TransferBuffer.SetData(transFaces, 0, 0, numTransFaces);

        int kernel = transVoxelGenerator.FindKernel("MarchTransition");
        transVoxelGenerator.SetFloat(ShaderIDProps.IsoLevel, IsoLevel);
        transVoxelGenerator.SetInt(ShaderIDProps.NumCubesPerAxis, numCubesAxis);
        transVoxelGenerator.SetInt(ShaderIDProps.NumPointsPerAxis, numPointsAxis);
        transVoxelGenerator.SetInt(ShaderIDProps.NumTransFaces, numTransFaces);
        transVoxelGenerator.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        //Only half the threads are used because each grid covers 2^2 faces
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxis / ((float)threadGroupSize * 2));
        transVoxelGenerator.Dispatch(kernel, numThreadsPerAxis, numThreadsPerAxis, numTransFaces);
    }

    private static TransFaceInfo[] GetNeighborFaces(uint neighborDepths, int numPointsAxes){
        int dictSizeBase = numPointsAxes * numPointsAxes * numPointsAxes * 3;
        int dictSizeFace = numPointsAxes * numPointsAxes * 2;

        float transWidth = Config.CURRENT.Quality.Terrain.value.transitionWidth;
        List<TransFaceInfo> transFaces = new List<TransFaceInfo>();
        for(int n = 0; n < 3; n++){
            uint nDepth = (neighborDepths >> (8 * n)) & 0x7F;
            bool isUpper = ((neighborDepths >> (8 * n)) & 0x80) != 0;
            for(int i = 0; i < nDepth; i++){
                TransFaceInfo faceInfo = new ();
                faceInfo.transWidth = transWidth / nDepth;
                faceInfo.transStart = (nDepth-i) * faceInfo.transWidth;
                faceInfo.dictStart = (uint)((transFaces.Count + n) * dictSizeFace + dictSizeBase);

                faceInfo.Align((uint)((isUpper ? 3 : 0) + n));
                faceInfo.SkipInc((uint)(1 << i));
                faceInfo.MergeFace(i == 0);
                faceInfo.IsEnd(i == nDepth - 1);
                transFaces.Add(faceInfo);
            }
        } return transFaces.ToArray();
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TransFaceInfo{
        public float transWidth;
        public float transStart;
        public uint dictStart;
        public uint data;

        public void Align(uint value){
            data = (data & 0xFFFFFF00) | (value & 0xFF);
        }
        public void SkipInc(uint value){
            data = (data & 0xFFFF00FF) | ((value & 0xFF) << 8);
        }
        public void IsEnd(bool value) {
            data = (data & 0x7FFFFFFF) | (value ? 0x80000000 : 0);
        }
        public void MergeFace(bool value){
            data = (data & 0xBFFFFFFF) | (value ? 0x40000000u : 0);
        }
    }

    /// <summary> Responsible for segmenting a fixed sized <see cref="GenerationBuffer"> working memory </see> buffer
    /// into regions for different purposes within the map and mesh generation process. Only regions that can
    /// exist simultaneously need to occupy exclusive regions, otherwise the memory may be reused. See
    /// <see cref="BufferOffsets"/> for more info. </summary>
    /// <remarks>The locations specified within this structure is relative to the size of the objects that will be occupying
    /// them and not based off a universal atomic unit. </remarks>
    public struct GeoGenOffsets : BufferOffsets{
        /// <summary> The location storing the amount of vertices in the generated mesh. </summary>
        public int vertexCounter;
        /// <summary> The location storing the amount of base terrain triangles in the generated mesh. </summary>
        public int baseTriCounter;
        /// <summary>The location storing the amount of liquid terrain triangles in the generated mesh. </summary>
        public int waterTriCounter;
        /// <summary> The location storing the <see cref="Creator.CompressMap(int)">uncompressed</see> map data for the chunk.
        /// This is never used simultaneously with the compressed map data stored in at <see cref="rawMapStart"/> and thus
        /// can occupy the same region. </summary>
        public int mapStart;
        /// <summary> The location storing the compressed map data for the chunk. This is the map data recognized by most systems. </summary>
        public int rawMapStart;
        /// <summary> The location storing the biome map data for the chunk when the biome map is <see cref="Creator.PopulateBiomes(float3, uint, int, int)">explicitly queried</see>. </summary>
        public int biomeMapStart;
        /// <summary> The location of the vertex dictionary used during mesh generation. The vertex dictionary is a perfect hash map that references
        /// where in the <see cref="vertStart">vertex buffer</see> the vertex data shared by multiple triangles is stored. </summary>
        public int dictStart;
        /// <summary> The location of the vertex buffer created during mesh generation. </summary>
        public int vertStart;
        /// <summary>The location of the base terrain triangles(index buffer) created during mesh generation.</summary>
        public int baseTriStart;
        /// <summary>The location of the liquid terrain triangles(index buffer) created during mesh generation.</summary>
        public int waterTriStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the Map and Mesh generator. 
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}}
        /// <summary> The end of the buffer region that is used by the Map and Mesh generator. 
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}

        private const int VERTEX_STRIDE_WORD = 3 * 2 + 2;
        private const int TRI_STRIDE_WORD = 3;
        private const int RAW_MAP_WORD = 3;

        /// <summary> Creates a new division scheme of working memory based on the maximum size of the map and mesh
        /// that can be generated.  An increased resolution of the map and mesh, or an increased amount of vertex data, 
        /// will require more working memory allocated for the map generator. The caller should make sure this does not 
        /// exceed the capacity of the buffer. </summary>
        /// <param name="GridSize">The amount of samples per axis exclusively bounded by the map. The amount of 
        /// cubes marched along each dimension when generating a mesh. For a cubic chunk, all components of the vector 
        /// should be equivalent.</param>
        /// <param name="chunkBalance">The balance factor of the octree; indicates the maximum amount of transition 
        /// faces a chunk can request. See <see cref="Quality.Terrain.Balance"/> for more info. </param>
        /// <param name="bufferStart">The start of the region within working memory the structure generator may utilize. See 
        /// <see cref="BufferOffsets.bufferStart"/> for more info. </param>
        /// <param name="VertexStride">The size of the vertex data for one vertex, in units of 4-bytes.</param>
        public GeoGenOffsets(int3 GridSize, int chunkBalance, int bufferStart, int VertexStride = VERTEX_STRIDE_WORD){
            this.offsetStart = bufferStart;
            vertexCounter = bufferStart; baseTriCounter = bufferStart + 1; waterTriCounter = bufferStart + 2;
            int numOfPoints = GridSize.x * GridSize.y * GridSize.z;
            int numOfPointsDict = (GridSize.x + 1) * (GridSize.y + 1) * (GridSize.z + 1);
            int numOfPointsOOB = (GridSize.x + 3) * (GridSize.y + 3) * (GridSize.z + 3);
            int numOfTris = (GridSize.x - 1) * (GridSize.y - 1) * (GridSize.z - 1) * 5;
            //Transition voxel dictionary
            numOfPointsDict += (GridSize.x + 1) * (GridSize.y + 1) * 3 * (chunkBalance + 1);
            
            //This is cached map, only used for visual chunks, real chunks
            //have their maps stored in the GPUMapManager
            mapStart = bufferStart + 3;
            int mapEnd_W = mapStart + numOfPointsOOB;
            rawMapStart = Mathf.CeilToInt((float)mapEnd_W / RAW_MAP_WORD); 
            biomeMapStart = (rawMapStart + numOfPointsOOB) * RAW_MAP_WORD;

            dictStart = mapEnd_W;
            int dictEnd_W = dictStart + numOfPointsDict * TRI_STRIDE_WORD;

            vertStart = Mathf.CeilToInt((float)dictEnd_W / VertexStride);
            int vertexEnd_W = vertStart * VertexStride + (numOfPoints * 3) * VertexStride;

            baseTriStart = Mathf.CeilToInt((float)vertexEnd_W / TRI_STRIDE_WORD);
            int baseTriEnd_W = baseTriStart * TRI_STRIDE_WORD + numOfTris * TRI_STRIDE_WORD;

            waterTriStart = Mathf.CeilToInt((float)baseTriEnd_W / TRI_STRIDE_WORD);
            int waterTriEnd_W = waterTriStart * TRI_STRIDE_WORD + numOfTris * TRI_STRIDE_WORD;

            this.offsetEnd = waterTriEnd_W;
        }
    }
}}
/*
public static void SimplifyMaterials(int chunkSize, int meshSkipInc, int[] materials, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int totalPointsAxes = chunkSize + 1;
    int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
    ComputeBuffer completeMaterial = new ComputeBuffer(totalPoints, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
    completeMaterial.SetData(materials);
    bufferHandle.Enqueue(completeMaterial);

    densitySimplification.EnableKeyword("USE_INT");
    densitySimplification.SetInt("meshSkipInc", meshSkipInc);
    densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
    densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
    densitySimplification.SetBuffer(0, "points_full", completeMaterial);
    densitySimplification.SetBuffer(0, "points", pointBuffer);

    densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
}*/

/*
public static ComputeBuffer GenerateTerrain(int chunkSize, int meshSkipInc, SurfaceChunk.SurfData surfaceData, int coarseCave, int fineCave, Vector3 offset, float IsoValue, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;
    
    ComputeBuffer densityMap = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(densityMap);

    terrainNoiseCompute.SetBuffer(0, "points", densityMap);
    terrainNoiseCompute.SetBuffer(0, "_SurfMemoryBuffer", surfaceData.Memory);
    terrainNoiseCompute.SetBuffer(0, "_SurfAddressDict", surfaceData.Addresses);
    terrainNoiseCompute.SetInt("surfAddress", (int)surfaceData.addressIndex);

    terrainNoiseCompute.SetInt("coarseSampler", coarseCave);
    terrainNoiseCompute.SetInt("fineSampler", fineCave);

    terrainNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
    terrainNoiseCompute.SetFloat("meshSkipInc", meshSkipInc);
    terrainNoiseCompute.SetFloat("chunkSize", chunkSize);
    terrainNoiseCompute.SetFloat("offsetY", offset.y);
    terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);
    SetSampleData(terrainNoiseCompute, offset, chunkSize, meshSkipInc);

    terrainNoiseCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

    return densityMap;
}

public static ComputeBuffer GenerateNoiseMap(ComputeShader shader, Vector3 offset, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(density);

    shader.SetBuffer(0, "points", density);
    shader.SetInt("numPointsPerAxis", numPointsAxes);
    SetSampleData(shader, offset, chunkSize, meshSkipInc);

    shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    shader.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    return density;
}

public static ComputeBuffer GenerateNoiseMap(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(density);

    rawNoiseSampler.SetBuffer(0, "points", density);
    rawNoiseSampler.SetInt("numPointsPerAxis", numPointsAxes);
    SetNoiseData(rawNoiseSampler, chunkSize, meshSkipInc, noiseData, offset);

    rawNoiseSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    rawNoiseSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    return density;
}*/

/*
public static ComputeBuffer GenerateCaveNoise(SurfaceChunk.SurfData surfaceData, Vector3 offset, int coarseSampler, int fineSampler, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer caveDensity = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(caveDensity);
    
    baseCaveGenerator.SetBuffer(0, "_SurfMemoryBuffer", surfaceData.Memory);
    baseCaveGenerator.SetBuffer(0, "_SurfAddressDict", surfaceData.Addresses);
    baseCaveGenerator.SetInt("surfAddress", (int)surfaceData.addressIndex);

    baseCaveGenerator.SetInt("coarseSampler", coarseSampler);
    baseCaveGenerator.SetInt("fineSampler", fineSampler);
    baseCaveGenerator.SetInt("numPointsPerAxis", numPointsAxes);
    SetSampleData(baseCaveGenerator, offset, chunkSize, meshSkipInc);

    baseCaveGenerator.SetBuffer(0, "densityMap", caveDensity);

    baseCaveGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    baseCaveGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    
    return caveDensity;
}*/

/*
public ComputeBuffer GetAdjacentDensity(GPUMapManager densityManager, Vector3 CCoord, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    ComputeBuffer neighborDensity = new ComputeBuffer(numPointsAxes * numPointsAxes * 6, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(neighborDensity);

    neighborDensitySampler.SetBuffer(0, "_MemoryBuffer", densityManager.AccessStorage());
    neighborDensitySampler.SetBuffer(0, "_AddressDict", densityManager.AccessAddresses());
    densityManager.SetCCoordHash(neighborDensitySampler);

    neighborDensitySampler.SetInts("CCoord", new int[] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
    neighborDensitySampler.SetInt("numPointsPerAxis", numPointsAxes);
    neighborDensitySampler.SetInt("meshSkipInc", meshSkipInc);
    neighborDensitySampler.SetBuffer(0, "nDensity", neighborDensity);

    neighborDensitySampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    neighborDensitySampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);
    return neighborDensity;
}*/