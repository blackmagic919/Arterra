using Unity.Mathematics;
using UnityEngine;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Unity.Collections;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Utils;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain.Map{
public class Creator
{
    public GraphicsContextId GraphicsContext;

    static ComputeShader baseGenCompute;
    static ComputeShader biomeGenCompute;
    static ComputeShader mapCompressor;

    public static GeoGenOffsets bufferOffsets;

    static Creator() {
        baseGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/ChunkDataGen");
        biomeGenCompute = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/FullBiomeSampler");
        mapCompressor = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MapCompressor");
    }

    public Creator(GraphicsContextId graphicsContext = GraphicsContextId.Generation) {
        GraphicsContext = graphicsContext;
    }

    public GraphicsResourceContext GetGraphicsContext() => Graphics(GraphicsContext);

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
    public void PopulateBiomes(float3 offset, uint surfaceData, int chunkSize, int mapSkip) => GenerateBiomeData(offset, surfaceData, chunkSize, mapSkip);
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
    public void GenerateBaseChunk(float3 offset, uint surfaceData, int chunkSize, int mapSkip, float IsoLevel) => GenerateBaseData(offset, surfaceData, chunkSize, mapSkip, IsoLevel);
    /// <summary> Compresses the map data of the chunk into its compacted form which is actually stored and recognized by
    /// most systems. During generation, the map data is stored in 12-bytes(4-bytes for each field) as certain atomic
    /// operations only operate on this level. However most systems recognize a compacted 4-byte form of the map data. </summary>
    /// <param name="chunkSize">The axis size of the map to be compressed. The amount of entries to be compressed is (<paramref name="chunkSize"/>^3) </param>
    public void CompressMap(int chunkSize) => CompressMapData(chunkSize);
    /// <summary> Copies the map data from a linearly encoded chunk on the CPU to a
    /// <see cref="GraphicsGeneration.Work.Transfer">transfer buffer</see> accessible by GPU-based tasks.  </summary>
    /// <param name="numPointsAxis">The axis size of the map to be copied, the length of <paramref name="chunkData"/>
    /// should be greater than or equal to (<i>numPointsAxis</i>^3)</param>
    /// <param name="offset">The offset within <paramref name="chunkData"/> to begin copying the MapData.</param>
    /// <param name="chunkData">A managed array containing the linearly encoded map information for a chunk.</param>
    public void SetMapInfo(int numPointsAxis, int offset, MapData[] chunkData){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        gpuContext.SetBufferData(gpuContext.Work.Transfer, chunkData, offset, 0, numPoints);
    }
    /// <summary> Copies the map data from a linearly encoded unmanaged chunk on the CPU to a
    /// <see cref="GraphicsGeneration.Work.Transfer">transfer buffer</see> accessible by GPU-based tasks.</summary>
    /// <param name="numPointsAxis">The axis size of the map to be copied, the length of <paramref name="chunkData"/>
    /// should be greater than or equal to (<i>numPointsAxis</i>^3)</param>
    /// <param name="offset">The offset within <paramref name="chunkData"/> to begin copying the MapData.</param>
    /// <param name="chunkData">A Unity unamanged array containing the linearly encoded map information for a chunk.</param>
    public void SetMapInfo(int numPointsAxis, int offset, ref NativeArray<MapData> chunkData)
    {
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        gpuContext.SetBufferData(gpuContext.Work.Transfer, chunkData, offset, 0, numPoints);
    }
    /// <summary>  Presets all compute-shaders used through map and base mesh generation by acquiring
    /// them and binding any constant values(information derived from the world's settings that
    /// won't change until the world is unloaded) to them. Referenced by
    /// <see cref="SystemProtocol.Startup"/> </summary>
    public static void PresetData(GraphicsContextId graphicsContext = GraphicsContextId.Generation){
        GraphicsResourceContext gpuContext = Graphics(graphicsContext);
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        Data.Generation.Map mesh = Config.CURRENT.Generation.Terrain.value;

        //Set Marching Cubes Data
        int numPointsAxes = rSettings.mapChunkSize;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), rSettings.Balance, 0);

        gpuContext.SetBuffer(baseGenCompute, 0, "_SurfAddressDict", gpuContext.Memory.Address);
        gpuContext.SetInt(baseGenCompute, "caveFreqSampler", mesh.CaveFrequencyIndex);
        gpuContext.SetInt(baseGenCompute, "caveSizeSampler", mesh.CaveSizeIndex);
        gpuContext.SetInt(baseGenCompute, "caveShapeSampler", mesh.CaveShapeIndex);
        gpuContext.SetInt(baseGenCompute, "coarseCaveSampler", mesh.CoarseTerrainIndex);
        gpuContext.SetInt(baseGenCompute, "fineCaveSampler", mesh.FineTerrainIndex);
        gpuContext.SetInt(baseGenCompute, "coarseMatSampler", mesh.CoarseMaterialIndex);
        gpuContext.SetInt(baseGenCompute, "fineMatSampler", mesh.FineMaterialIndex);

        gpuContext.SetFloat(baseGenCompute, "heightSFalloff", mesh.heightFalloff);
        gpuContext.SetFloat(baseGenCompute, "atmoStrength", mesh.atmosphereFalloff);
        gpuContext.SetFloat(baseGenCompute, "waterHeight", mesh.waterHeight);

        gpuContext.SetBuffer(baseGenCompute, 0, "BiomeMap", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(baseGenCompute, 0, "BaseMap", gpuContext.Work.Scratch);
        gpuContext.SetInt(baseGenCompute, "bSTART_map", bufferOffsets.rawMapStart);
        gpuContext.SetInt(baseGenCompute, "bSTART_biome", bufferOffsets.biomeMapStart);

        gpuContext.SetBuffer(biomeGenCompute, 0, "_SurfAddressDict", gpuContext.Memory.Address);
        gpuContext.SetInt(biomeGenCompute, "caveSizeSampler", mesh.CaveSizeIndex);
        gpuContext.SetInt(biomeGenCompute, "caveShapeSampler", mesh.CaveShapeIndex);
        gpuContext.SetInt(biomeGenCompute, "caveFreqSampler", mesh.CaveFrequencyIndex);

        gpuContext.SetBuffer(biomeGenCompute, 0, "BiomeMap", gpuContext.Work.Scratch);
        gpuContext.SetInt(biomeGenCompute, "bSTART_biome", bufferOffsets.biomeMapStart);
        gpuContext.SetFloat(biomeGenCompute, "waterHeight", mesh.waterHeight);

        gpuContext.SetBuffer(mapCompressor, 0, "rawData", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(mapCompressor, 0, "chunkData", gpuContext.Work.Scratch);
        gpuContext.SetInt(mapCompressor, "bSTART_raw", bufferOffsets.rawMapStart);
        gpuContext.SetInt(mapCompressor, "bSTART_chunk", bufferOffsets.mapStart);
    }

    /// <summary>Initializes just the basic buffer offsets within <see cref="bufferOffsets"/> to support map based mesh generation. </summary>
    public void MinimalInitialize() {
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        //Set Marching Cubes Data
        int numPointsAxes = rSettings.mapChunkSize;
        bufferOffsets = new GeoGenOffsets(new int3(numPointsAxes, numPointsAxes, numPointsAxes), rSettings.Balance, 0);
    }

    /// <summary> See <see cref="Creator.GenerateBaseChunk(float3, uint, int, int, float)"/> for info. </summary>
    public void GenerateBaseData( Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip, float IsoLevel)
    {
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        ComputeBuffer source = gpuContext.Memory.GetBlockBuffer(surfaceData);
        gpuContext.SetBuffer(baseGenCompute, 0, ShaderIDProps.SurfaceMemoryBuffer, source);

        gpuContext.SetFloat(baseGenCompute, ShaderIDProps.IsoLevel, IsoLevel);
        gpuContext.SetInt(baseGenCompute, ShaderIDProps.SurfaceAddress, (int)surfaceData);
        gpuContext.SetInt(baseGenCompute, ShaderIDProps.NumPointsPerAxis, numPointsPerAxis);

        gpuContext.Work.SetSampleData(baseGenCompute, offset, mapSkip);

        baseGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        gpuContext.Dispatch(baseGenCompute, 0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> See <see cref="Creator.PopulateBiomes(float3, uint, int, int)"/> for info. </summary>
    public void GenerateBiomeData(Vector3 offset, uint surfaceData, int numPointsPerAxis, int mapSkip){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        ComputeBuffer source = gpuContext.Memory.GetBlockBuffer(surfaceData);
        gpuContext.SetBuffer(biomeGenCompute, 0, ShaderIDProps.SurfaceMemoryBuffer, source);
        gpuContext.SetInt(biomeGenCompute, ShaderIDProps.NumPointsPerAxis, numPointsPerAxis);
        gpuContext.SetInt(biomeGenCompute, ShaderIDProps.SurfaceAddress, (int)surfaceData);
        gpuContext.Work.SetSampleData(biomeGenCompute, offset, mapSkip);

        biomeGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        gpuContext.Dispatch(biomeGenCompute, 0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> See <see cref="Creator.CompressMap(int)"/> for info. </summary>
    public void CompressMapData(int chunkSize){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numPointsAxes = chunkSize;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        gpuContext.SetInt(mapCompressor, ShaderIDProps.NumPoints, numPoints);
        mapCompressor.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        gpuContext.Dispatch(mapCompressor, 0, numThreadsAxis, 1, 1);
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
        /// <summary> The location storing the <see cref="CompressMap(int)">uncompressed</see> map data for the chunk.
        /// This is never used simultaneously with the compressed map data stored in at <see cref="rawMapStart"/> and thus
        /// can occupy the same region. </summary>
        public int mapStart;
        /// <summary> The location storing the compressed map data for the chunk. This is the map data recognized by most systems. </summary>
        public int rawMapStart;
        /// <summary> The location storing the biome map data for the chunk when the biome map is <see cref="PopulateBiomes(float3, uint, int, int)">explicitly queried</see>. </summary>
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
    GraphicsGeneration.SetBufferData(completeMaterial, materials);
    bufferHandle.Enqueue(completeMaterial);

    densitySimplification.EnableKeyword("USE_INT");
    GraphicsGeneration.SetInt(densitySimplification, "meshSkipInc", meshSkipInc);
    GraphicsGeneration.SetInt(densitySimplification, "totalPointsPerAxis", totalPointsAxes);
    GraphicsGeneration.SetInt(densitySimplification, "pointsPerAxis", numPointsAxes);
    GraphicsGeneration.SetBuffer(densitySimplification, 0, "points_full", completeMaterial);
    GraphicsGeneration.SetBuffer(densitySimplification, 0, "points", pointBuffer);

    densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    GraphicsGeneration.Dispatch(densitySimplification, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
}*/

/*
public static ComputeBuffer GenerateTerrain(int chunkSize, int meshSkipInc, SurfaceChunk.SurfData surfaceData, int coarseCave, int fineCave, Vector3 offset, float IsoValue, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer densityMap = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(densityMap);

    GraphicsGeneration.SetBuffer(terrainNoiseCompute, 0, "points", densityMap);
    GraphicsGeneration.SetBuffer(terrainNoiseCompute, 0, "_SurfMemoryBuffer", surfaceData.Memory);
    GraphicsGeneration.SetBuffer(terrainNoiseCompute, 0, "_SurfAddressDict", surfaceData.Addresses);
    GraphicsGeneration.SetInt(terrainNoiseCompute, "surfAddress", (int)surfaceData.addressIndex);

    GraphicsGeneration.SetInt(terrainNoiseCompute, "coarseSampler", coarseCave);
    GraphicsGeneration.SetInt(terrainNoiseCompute, "fineSampler", fineCave);

    GraphicsGeneration.SetInt(terrainNoiseCompute, "numPointsPerAxis", numPointsAxes);
    GraphicsGeneration.SetFloat(terrainNoiseCompute, "meshSkipInc", meshSkipInc);
    GraphicsGeneration.SetFloat(terrainNoiseCompute, "chunkSize", chunkSize);
    GraphicsGeneration.SetFloat(terrainNoiseCompute, "offsetY", offset.y);
    GraphicsGeneration.SetFloat(terrainNoiseCompute, "IsoLevel", IsoValue);
    SetSampleData(terrainNoiseCompute, offset, chunkSize, meshSkipInc);

    terrainNoiseCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    GraphicsGeneration.Dispatch(terrainNoiseCompute, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

    return densityMap;
}

public static ComputeBuffer GenerateNoiseMap(ComputeShader shader, Vector3 offset, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(density);

    GraphicsGeneration.SetBuffer(shader, 0, "points", density);
    GraphicsGeneration.SetInt(shader, "numPointsPerAxis", numPointsAxes);
    SetSampleData(shader, offset, chunkSize, meshSkipInc);

    shader.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    GraphicsGeneration.Dispatch(shader, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    return density;
}

public static ComputeBuffer GenerateNoiseMap(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer density = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(density);

    GraphicsGeneration.SetBuffer(rawNoiseSampler, 0, "points", density);
    GraphicsGeneration.SetInt(rawNoiseSampler, "numPointsPerAxis", numPointsAxes);
    SetNoiseData(rawNoiseSampler, chunkSize, meshSkipInc, noiseData, offset);

    rawNoiseSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    GraphicsGeneration.Dispatch(rawNoiseSampler, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    return density;
}*/

/*
public static ComputeBuffer GenerateCaveNoise(SurfaceChunk.SurfData surfaceData, Vector3 offset, int coarseSampler, int fineSampler, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle){
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

    ComputeBuffer caveDensity = new ComputeBuffer(numPoints, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(caveDensity);

    GraphicsGeneration.SetBuffer(baseCaveGenerator, 0, "_SurfMemoryBuffer", surfaceData.Memory);
    GraphicsGeneration.SetBuffer(baseCaveGenerator, 0, "_SurfAddressDict", surfaceData.Addresses);
    GraphicsGeneration.SetInt(baseCaveGenerator, "surfAddress", (int)surfaceData.addressIndex);

    GraphicsGeneration.SetInt(baseCaveGenerator, "coarseSampler", coarseSampler);
    GraphicsGeneration.SetInt(baseCaveGenerator, "fineSampler", fineSampler);
    GraphicsGeneration.SetInt(baseCaveGenerator, "numPointsPerAxis", numPointsAxes);
    SetSampleData(baseCaveGenerator, offset, chunkSize, meshSkipInc);

    GraphicsGeneration.SetBuffer(baseCaveGenerator, 0, "densityMap", caveDensity);

    baseCaveGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
    GraphicsGeneration.Dispatch(baseCaveGenerator, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

    return caveDensity;
}*/

/*
public ComputeBuffer GetAdjacentDensity(GPUMapManager densityManager, Vector3 CCoord, int chunkSize, int meshSkipInc, ref Queue<ComputeBuffer> bufferHandle)
{
    int numPointsAxes = chunkSize / meshSkipInc + 1;
    ComputeBuffer neighborDensity = new ComputeBuffer(numPointsAxes * numPointsAxes * 6, sizeof(float), ComputeBufferType.Structured);
    bufferHandle.Enqueue(neighborDensity);

    GraphicsGeneration.SetBuffer(neighborDensitySampler, 0, "_MemoryBuffer", densityManager.AccessStorage());
    GraphicsGeneration.SetBuffer(neighborDensitySampler, 0, "_AddressDict", densityManager.AccessAddresses());

    GraphicsGeneration.SetInts(neighborDensitySampler, "CCoord", new int[] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
    GraphicsGeneration.SetInt(neighborDensitySampler, "numPointsPerAxis", numPointsAxes);
    GraphicsGeneration.SetInt(neighborDensitySampler, "meshSkipInc", meshSkipInc);
    GraphicsGeneration.SetBuffer(neighborDensitySampler, 0, "nDensity", neighborDensity);

    neighborDensitySampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
    int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

    GraphicsGeneration.Dispatch(neighborDensitySampler, 0, numThreadsPerAxis, numThreadsPerAxis, 1);
    return neighborDensity;
}*/
