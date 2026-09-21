using Unity.Mathematics;
using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Utils;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain.MeshGeneration {
public class Creator {
    public GraphicsContextId GraphicsContext;

    static ComputeShader dMeshGenerator;
    static ComputeShader transVoxelGenerator;
    static ComputeShader meshInfoCollector;

    static Creator() {
        dMeshGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/CMarchingCubes");
        meshInfoCollector = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/BaseMapCollector");
        transVoxelGenerator = Resources.Load<ComputeShader>("Compute/TerrainGeneration/BaseGeneration/MarchTransitionCells");
    }

    public Creator(GraphicsContextId graphicsContext = GraphicsContextId.Rendering) {
        GraphicsContext = graphicsContext;
    }

    public GraphicsResourceContext GetGraphicsContext() => Graphics(GraphicsContext);

    /// <summary> Generates the mesh for the <see cref="TerrainChunk.RealChunk"/> at the specified location. Involves retrieving
    /// the saved map information stored by <see cref="GPUMapManager.RegisterChunkReal(int3, int, ComputeBuffer, int)"/>
    /// and generating the mesh using the marching cubes algorithm. If an invalid chunk is passed, or one that does not
    /// have a saved map, the behavior for this function is not defined. </summary>
    /// <param name="CCoord">The coordinate in chunk space, of the <see cref="TerrainChunk.RealChunk"/> whose mesh is generated.</param>
    /// <param name="mapContext">The context whose scratch buffer contains the generated map.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk</param>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="GenerateTransition(uint, int, float)"/>
    /// for more info. </param>
    public void GenerateRealMesh(int3 CCoord, float IsoLevel, int chunkSize, uint neighborDepths){
        CollectRealMap(CCoord, chunkSize);
        GenerateMesh(chunkSize, IsoLevel);
        if(neighborDepths == 0) return;
        GenerateTransition(neighborDepths, chunkSize, IsoLevel);
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
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="GenerateTransition(uint, int, float)"/>
    /// for more info. </param>
    public void GenerateVisualMesh(int3 CCoord, int defAddress, float IsoLevel, int chunkSize, int depth, uint neighborDepths){
        CollectVisualMap(CCoord, defAddress, chunkSize, depth);
        GenerateMesh(chunkSize, IsoLevel);
        if(neighborDepths == 0) return;
        GenerateTransition(neighborDepths, chunkSize, IsoLevel);
    }
    /// <summary>  Generates the mesh for a <see cref="TerrainChunk.VisualChunk"><b>fake</b> visual chunk</see>. Because the map data is
    /// not stored with only the default map being recreated on demand, a <i>fake mesh</i> is created in the sense that it is
    /// not only non-interactable, but also cannot be changed within the context of the game. </summary>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk</param>
    /// <param name="neighborDepths">A bitmap describing the potential difference in depth between this chunk and its neighbors,
    /// used in generating transition information. See <see cref="OctreeTerrain.BalancedOctree.GetNeighborDepths(uint)"/> and <see cref="GenerateTransition(uint, int, float)"/>
    /// for more info.</param>
    public void GenerateFakeMesh(GraphicsResourceContext mapContext, float IsoLevel,
        int chunkSize, uint neighborDepths, Action onGenerated = null) {
        GraphicsResourceContext meshContext = GetGraphicsContext();
        if (mapContext.id == meshContext.id) {
            CreateFakeMesh();
            return;
        }

        Map.Creator.GeoGenOffsets bufferOffsets = Map.Creator.bufferOffsets;
        int mapSize = chunkSize + 3;
        int mapPointCount = mapSize * mapSize * mapSize;

        uint mapAddress = mapContext.Memory.AllocateMemoryDirect(mapPointCount, 1);
        if (mapAddress == 0) return;
        mapContext.Work.CopyToStorage(mapAddress, bufferOffsets.mapStart,
            mapPointCount);

        mapContext.Memory.RegisterRebind(mapAddress, mapStorage => {
            if (!mapContext.Memory.GetDirectAllocation(mapAddress, 1,
                out _, out _, out _, out int mapStart, out _))
                return;

            meshContext.PollGraphicsContext(mapContext, _ => {
                meshContext.Work.CopyBuffer(mapStorage, meshContext.Work.Scratch,
                    mapStart, bufferOffsets.mapStart, mapPointCount);

                mapContext.PollGraphicsContext(meshContext,
                    _ => mapContext.Memory.ReleaseMemory(mapAddress));

                CreateFakeMesh();
            });
        });

        void CreateFakeMesh() {
            GenerateMesh(chunkSize, IsoLevel);
            if (neighborDepths != 0)
                GenerateTransition(neighborDepths, chunkSize, IsoLevel);
            onGenerated?.Invoke();
        }
    }
    public static void PresetData(GraphicsContextId graphicsContext = GraphicsContextId.Rendering) {
        GraphicsResourceContext gpuContext = Graphics(graphicsContext);
        Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain;
        Map.Creator.GeoGenOffsets bufferOffsets = Map.Creator.bufferOffsets;
        //They're all the same buffer lol
        gpuContext.SetBuffer(dMeshGenerator, 0, "MapData", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(dMeshGenerator, 0, "vertexes", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(dMeshGenerator, 0, "triangles", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(dMeshGenerator, 0, "triangleDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(dMeshGenerator, 0, "counter", gpuContext.Work.Scratch);
        gpuContext.SetInts(dMeshGenerator, "counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});
        gpuContext.SetInt(dMeshGenerator, "meshSkipInc", 1); //we are only dealing with same size chunks in this model

        gpuContext.SetInt(dMeshGenerator, "bSTART_map", bufferOffsets.mapStart);
        gpuContext.SetInt(dMeshGenerator, "bSTART_dict", bufferOffsets.dictStart);
        gpuContext.SetInt(dMeshGenerator, "bSTART_verts", bufferOffsets.vertStart);
        gpuContext.SetInt(dMeshGenerator, "bSTART_baseT", bufferOffsets.baseTriStart);
        gpuContext.SetInt(dMeshGenerator, "bSTART_waterT", bufferOffsets.waterTriStart);

        gpuContext.SetBuffer(transVoxelGenerator, 0, "MapData", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(transVoxelGenerator, 0, "vertexes", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(transVoxelGenerator, 0, "triangles", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(transVoxelGenerator, 0, "triangleDict", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(transVoxelGenerator, 0, "counter", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(transVoxelGenerator, 0, "FaceProperty", gpuContext.Work.Transfer);
        gpuContext.SetInts(transVoxelGenerator, "counterInd", new int[3]{bufferOffsets.vertexCounter, bufferOffsets.baseTriCounter, bufferOffsets.waterTriCounter});

        gpuContext.SetInt(transVoxelGenerator, "bSTART_map", bufferOffsets.mapStart);
        gpuContext.SetInt(transVoxelGenerator, "bSTART_dict", bufferOffsets.dictStart);
        gpuContext.SetInt(transVoxelGenerator, "bSTART_verts", bufferOffsets.vertStart);
        gpuContext.SetInt(transVoxelGenerator, "bSTART_baseT", bufferOffsets.baseTriStart);
        gpuContext.SetInt(transVoxelGenerator, "bSTART_waterT", bufferOffsets.waterTriStart);


        int kernel = meshInfoCollector.FindKernel("CollectReal");
        gpuContext.SetBuffer(meshInfoCollector, kernel, "MapData", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(meshInfoCollector, kernel, "_MemoryBuffer", GPUMapManager.Storage);
        gpuContext.SetBuffer(meshInfoCollector, kernel, "_AddressDict", GPUMapManager.Address);
        kernel = meshInfoCollector.FindKernel("CollectVisual");
        gpuContext.SetBuffer(meshInfoCollector, kernel, "MapData", gpuContext.Work.Scratch);
        gpuContext.SetBuffer(meshInfoCollector, kernel, "_MemoryBuffer", GPUMapManager.Storage);
        gpuContext.SetBuffer(meshInfoCollector, kernel, "_AddressDict", GPUMapManager.Address);
        gpuContext.SetBuffer(meshInfoCollector, kernel, "_DirectAddress", GPUMapManager.DirectAddress);
        gpuContext.SetInt(meshInfoCollector, "bSTART_map", bufferOffsets.mapStart);

    }
    /// <summary> Collects the map data for a <see cref="TerrainChunk.RealChunk">real chunk</see>. Retrieves the map data stored in a hashmap by <see cref="GPUMapManager"/>
    /// and copies it into <see cref="GraphicsGeneration.Work.Scratch">working memory</see> where it can be accessed easier.
    /// Out-of-bound map information necessary for mesh generation is additionally copied; more accurately the first and last two
    /// entries of each axis of the map are retrieved from the stored map data submitted by neighboring chunks, discoverable through
    /// the hashmap managed by <see cref="GPUMapManager"/>. To avoid gaps, a real chunk that invokes this function should avoid bordering
    /// <see cref="TerrainChunk.VisualChunk"><b>fake</b> visual</see> chunks that are not saved at all and hence not discoverable. </summary>
    /// <param name="CCoord">The coordinate in chunk space of the origin of the chunk.</param>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk. Equivalent to the amount of entries per axis within the map saved for this chunk.
    /// The amount of points in the map retrieved by this function is (<paramref name="chunkSize"/>+3)^3</param>
    public void CollectRealMap(int3 CCoord, int chunkSize){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int fChunkSize = chunkSize + 3;
        gpuContext.SetInts(meshInfoCollector, ShaderIDProps.CCoord, new int[]{CCoord.x, CCoord.y, CCoord.z});
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.NumPointsPerAxis, fChunkSize);
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.MapChunkSize, chunkSize);

        int kernel = meshInfoCollector.FindKernel("CollectReal");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        gpuContext.Dispatch(meshInfoCollector, kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> Collects the map data for a <see cref="TerrainChunk.VisualChunk">normal visual chunk</see>. Retrieves the map data stored in a hashmap by <see cref="GPUMapManager"/>
    /// and copies it into <see cref="GraphicsRendering.Work.Scratch">working memory</see> where it can be accessed easier. Out-of-bound map information necessary for mesh
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
    public void CollectVisualMap(int3 CCoord, int defaultAddress, int chunkSize, int depth){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int fChunkSize = chunkSize + 3; int skipInc = 1 << depth;
        gpuContext.SetInts(meshInfoCollector, ShaderIDProps.CCoord, new int[]{CCoord.x, CCoord.y, CCoord.z});
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.NumPointsPerAxis, fChunkSize);
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.MapChunkSize, chunkSize);
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.DefaultAddress, defaultAddress);
        gpuContext.SetInt(meshInfoCollector, ShaderIDProps.SkipInc, skipInc);

        int kernel = meshInfoCollector.FindKernel("CollectVisual");
        meshInfoCollector.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(fChunkSize / (float)threadGroupSize);
        gpuContext.Dispatch(meshInfoCollector, kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    /// <summary> Generates the visual mesh for a chunk based off the map data stored in the <see cref="GraphicsGeneration.Work.Scratch">working buffer</see>.
    /// The mesh is generated using the marching cubes algorithm and is stored in a distributed form within the buffer in a way that avoids
    /// duplicated vertex information. Two seperate meshes are created for every chunk, one for the base terrain and one for liquids. </summary>
    /// <remarks>See <see href="https://paulbourke.net/geometry/polygonise/">here</see> to learn about marching cubes. </remarks>
    /// <param name="chunkSize">The resolution of the mesh generated for the chunk; the amount of cubes marched per axis of the chunk.</param>
    /// <param name="IsoLevel">The density of the surface of the terrain. See <see cref="Quality.Terrain.IsoLevel"/> for more info.</param>
    public void GenerateMesh(int chunkSize, float IsoLevel)
    {
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numCubesAxes = chunkSize;
        int numPointsAxes = numCubesAxes + 1;
        gpuContext.Work.ClearRange(gpuContext.Work.Scratch, 3, 0);

        gpuContext.SetFloat(dMeshGenerator, ShaderIDProps.IsoLevel, IsoLevel);
        gpuContext.SetInt(dMeshGenerator, ShaderIDProps.NumCubesPerAxis, numCubesAxes);
        gpuContext.SetInt(dMeshGenerator, ShaderIDProps.NumPointsPerAxis, numPointsAxes);

        dMeshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        gpuContext.Dispatch(dMeshGenerator, 0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    /// <summary> Generates the transition mesh for a chunk based off the map data stored in the <see cref="GraphicsGeneration.Work.Scratch">working buffer</see>
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
    public void GenerateTransition(uint neighborDepths, int chunkSize, float IsoLevel){
        GraphicsResourceContext gpuContext = GetGraphicsContext();
        int numCubesAxis = chunkSize;
        int numPointsAxis = numCubesAxis + 1;
        TransFaceInfo[] transFaces = GetNeighborFaces(neighborDepths, numPointsAxis);
        int numTransFaces = transFaces.Length; if(numTransFaces == 0) return;
        gpuContext.SetBufferData(
            gpuContext.Work.Transfer,
            transFaces, 0, 0, numTransFaces);

        int kernel = transVoxelGenerator.FindKernel("MarchTransition");
        gpuContext.SetFloat(transVoxelGenerator, ShaderIDProps.IsoLevel, IsoLevel);
        gpuContext.SetInt(transVoxelGenerator, ShaderIDProps.NumCubesPerAxis, numCubesAxis);
        gpuContext.SetInt(transVoxelGenerator, ShaderIDProps.NumPointsPerAxis, numPointsAxis);
        gpuContext.SetInt(transVoxelGenerator, ShaderIDProps.NumTransFaces, numTransFaces);
        transVoxelGenerator.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        //Only half the threads are used because each grid covers 2^2 faces
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxis / ((float)threadGroupSize * 2));
        gpuContext.Dispatch(transVoxelGenerator, kernel, numThreadsPerAxis, numThreadsPerAxis, numTransFaces);
    }

    private TransFaceInfo[] GetNeighborFaces(uint neighborDepths, int numPointsAxes){
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


}}
