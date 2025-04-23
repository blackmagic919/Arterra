using System;
using System.Collections;
using System.Collections.Generic;
using TerrainGeneration;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;

namespace WorldConfig.Quality{
    /// <summary>
    /// Settings for the light baker, responsible for dynamically baking
    /// lighting in the world as the world changes. Light-baking is based off
    /// a procedurally rebaking system that propogates light divided into sub-chunks
    /// within a terrain chunk of the world.
    /// </summary>
    [Serializable]
    public struct LightBaker{
        /// <summary>
        /// The size of lighting sub-chunks relative to the size of the chunk.
        /// = <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> / SubChunkSize
        /// </summary>
        public int SubChunkDivisions; 
        /// <summary> The maximum number of sub-chunks that can be updated in a single tick.
        /// Constrains both time and memory usage of the light baker. 
        // Recommended < 5k
        // </summary>
        public int MaxSubChunkUpdatesPerTick;
    }
}

public static class LightBaker
{
    private static ComputeShader ChunkLightPrimer;
    private static ComputeShader LightSetupPrimer;
    private static ComputeShader ObjectLightShader;
    private static ComputeBuffer DirtySubChunkDict;
    private static ComputeBuffer SubChunkUpdateBuffer;
    private static BakeQueueOffsets DirtyQueueOffsets;
    private static BakeQueueOffsets UpdateQueueOffsets;
    private static WorldConfig.Quality.LightBaker Settings => Config.CURRENT.Quality.Lighting;

    static LightBaker(){ //That's a lot of Compute Shaders XD
        ChunkLightPrimer = Resources.Load<ComputeShader>("Compute/LightBaker/ChunkLightPrimer");
        LightSetupPrimer = Resources.Load<ComputeShader>("Compute/LightBaker/SubchunkPrimer");
        ObjectLightShader = Resources.Load<ComputeShader>("Compute/LightBaker/ObjectLightBaker");
    }

    public static void Initialize()
    {
        WorldConfig.Quality.Terrain terrain = Config.CURRENT.Quality.Terrain;
        int NumChunks = OctreeTerrain.Octree.GetMaxNodes(terrain.MaxDepth, terrain.Balance, terrain.MinChunkRadius);
        int SubChunkCount = Settings.SubChunkDivisions * Settings.SubChunkDivisions * Settings.SubChunkDivisions;
        int SubChunkSize = terrain.mapChunkSize / Settings.SubChunkDivisions;
        SubChunkCount *= NumChunks;

        DirtySubChunkDict = new ComputeBuffer(SubChunkCount + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
        SubChunkUpdateBuffer = new ComputeBuffer(Settings.MaxSubChunkUpdatesPerTick + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
        DirtyQueueOffsets = new BakeQueueOffsets(0, SubChunkCount);
        UpdateQueueOffsets = new BakeQueueOffsets(0, Settings.MaxSubChunkUpdatesPerTick);
        DirtySubChunkDict.SetData(new uint2[]{0}, 0, DirtyQueueOffsets.QueueStart, 1);
        SubChunkUpdateBuffer.SetData(new uint2[]{0}, 0, UpdateQueueOffsets.QueueStart, 1);

        int mapSize = terrain.mapChunkSize * terrain.mapChunkSize * terrain.mapChunkSize;
        int mapFullSize = mapSize + (terrain.mapChunkSize + 3) * (terrain.mapChunkSize + 3) * 9;
        int lightMapSize = Mathf.CeilToInt(mapSize / 2.0f);
        int IsoValue = Mathf.RoundToInt(terrain.IsoLevel * 255.0f);

        int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
        LightSetupPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        LightSetupPrimer.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
        LightSetupPrimer.SetBuffer(kernel, "DirtySubChunks", DirtySubChunkDict);
        LightSetupPrimer.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
        kernel = LightSetupPrimer.FindKernel("PrimeQueue");
        LightSetupPrimer.SetBuffer(kernel, "DirtySubChunks", DirtySubChunkDict);
        LightSetupPrimer.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);

        LightSetupPrimer.SetInts("bCOUNT", new int[]{DirtyQueueOffsets.QueueCount, UpdateQueueOffsets.QueueCount});
        LightSetupPrimer.SetInts("bSTART", new int[]{DirtyQueueOffsets.QueueStart, UpdateQueueOffsets.QueueStart});
        LightSetupPrimer.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
        LightSetupPrimer.SetInt("UpdateCount", Settings.MaxSubChunkUpdatesPerTick);
        LightSetupPrimer.SetInt("QueueSize", SubChunkCount);
        GPUMapManager.SetCCoordHash(LightSetupPrimer);

        kernel = ObjectLightShader.FindKernel("BakeLights");
        ObjectLightShader.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        ObjectLightShader.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
        ObjectLightShader.SetBuffer(kernel, "DirtySubChunks", DirtySubChunkDict);
        ObjectLightShader.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
        ObjectLightShader.SetInts("bCOUNT", new int[]{DirtyQueueOffsets.QueueCount, UpdateQueueOffsets.QueueCount});
        ObjectLightShader.SetInts("bSTART", new int[]{DirtyQueueOffsets.QueueStart, UpdateQueueOffsets.QueueStart});
        ObjectLightShader.SetInt("QueueSize", SubChunkCount);
        ObjectLightShader.SetInt("chunkLMOffset", mapFullSize);
        ObjectLightShader.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
        ObjectLightShader.SetInt("subChunkSize", SubChunkSize);
        ObjectLightShader.SetInt("subChunksAxis", Settings.SubChunkDivisions);
        ObjectLightShader.SetInt("IsoLevel", IsoValue);
        ObjectLightShader.SetInt("mapChunkSize", terrain.mapChunkSize); //as int
        ObjectLightShader.SetInt("numPointsPerAxis", terrain.mapChunkSize); //as uint
        GPUMapManager.SetCCoordHash(ObjectLightShader);

        kernel = ChunkLightPrimer.FindKernel("CopyHash");
        ChunkLightPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        ChunkLightPrimer.SetBuffer(kernel, "_ChunkAddressDict", GPUMapManager.Address);
        ChunkLightPrimer.SetBuffer(kernel, "_DirectAddress", GPUMapManager.DirectAddress);
        ChunkLightPrimer.SetBuffer(kernel, "DirtySubChunks", DirtySubChunkDict);
        kernel = ChunkLightPrimer.FindKernel("CleanChunk");
        ChunkLightPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
        ChunkLightPrimer.SetBuffer(kernel, "_ChunkAddressDict", GPUMapManager.Address);
        ChunkLightPrimer.SetBuffer(kernel, "_DirectAddress", GPUMapManager.DirectAddress);
        ChunkLightPrimer.SetBuffer(kernel, "DirtySubChunks", DirtySubChunkDict);

        ChunkLightPrimer.SetInt("bCOUNT", DirtyQueueOffsets.QueueCount);
        ChunkLightPrimer.SetInt("bSTART", DirtyQueueOffsets.QueueStart);
        ChunkLightPrimer.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
        ChunkLightPrimer.SetInt("chunkLMOffset", mapFullSize);
        ChunkLightPrimer.SetInt("subChunkSize", SubChunkSize);
        ChunkLightPrimer.SetInt("subChunksAxis", Settings.SubChunkDivisions);
        ChunkLightPrimer.SetInt("QueueSize", SubChunkCount);
        GPUMapManager.SetCCoordHash(ChunkLightPrimer);

        OctreeTerrain.MainLateUpdateTasks.Enqueue(new IndirectUpdate(IterateLightUpdate));
    }

    public static void Release(){
        DirtySubChunkDict?.Dispose();
        SubChunkUpdateBuffer?.Dispose();
    }

    public static int GetLightMapLength(){
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int mapSize = mapChunkSize * mapChunkSize * mapChunkSize;
        int SubChunkAxis = mapChunkSize / Settings.SubChunkDivisions;
        int SubChunkCount = SubChunkAxis * SubChunkAxis * SubChunkAxis;
        return Mathf.CeilToInt(mapSize / 2.0f) + Mathf.CeilToInt(SubChunkCount / 32.0f);
    }

    public static int GetLightMapStart(){
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int mapSize = mapChunkSize * mapChunkSize * mapChunkSize;
        int mapFullSize = mapSize + (mapChunkSize + 3) * (mapChunkSize + 3) * 9;
        return mapFullSize;
    }

    public static void SetupLightSampler(Material mat){
        var tSettings = Config.CURRENT.Quality.Terrain.value;
        int IsoValue = Mathf.RoundToInt(tSettings.IsoLevel * 255.0f);
        mat.SetBuffer("_ChunkAddressDict", GPUMapManager.Address);
        mat.SetBuffer("_ChunkInfoBuffer", GPUMapManager.Storage);
        mat.SetInt("chunkLMOffset", GetLightMapStart());
        mat.SetInt("IsoLevel", IsoValue);
        mat.SetInt("mapChunkSize", tSettings.mapChunkSize);
        mat.SetFloat("lerpScale", tSettings.lerpScale);
        GPUMapManager.SetCCoordHash(mat);
    }

    public static void SetupLightSampler(ComputeShader shad, int kernel){
        var tSettings = Config.CURRENT.Quality.Terrain.value;
        int IsoValue = Mathf.RoundToInt(tSettings.IsoLevel * 255.0f);
        shad.SetBuffer(kernel, "_ChunkAddressDict", GPUMapManager.Address);
        shad.SetBuffer(kernel, "_ChunkInfoBuffer", GPUMapManager.Storage);
        shad.SetInt("chunkLMOffset", GetLightMapStart());
        shad.SetInt("IsoLevel", IsoValue);
        shad.SetInt("mapChunkSize", tSettings.mapChunkSize);
        shad.SetFloat("lerpScale", tSettings.lerpScale);
        GPUMapManager.SetCCoordHash(shad);
    }

    public static void RegisterChunk(int3 CCoord, int mapChunkSize, uint nAddress, bool CopyMap = false){
        int SubChunkAxis = mapChunkSize / Settings.SubChunkDivisions;
        int numSubChunks = SubChunkAxis * SubChunkAxis * SubChunkAxis;
        ChunkLightPrimer.SetInts("CCoord", new int[]{CCoord.x, CCoord.y, CCoord.z});
        ChunkLightPrimer.SetInt("numLightUnits", Mathf.CeilToInt(numSubChunks / 32.0f));
        ChunkLightPrimer.SetInt("nChunkAddress", (int)nAddress);
        ChunkLightPrimer.SetInt("CopyMap", CopyMap ? 1 : 0);

        int kernel = ChunkLightPrimer.FindKernel("CopyHash");
        ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreads = Mathf.CeilToInt(numSubChunks / (32.0f * (float)threadGroupSize));
        ChunkLightPrimer.Dispatch(kernel, numThreads, 1, 1);

        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        //Don't copy chunk info
        ChunkLightPrimer.SetInt("numLightUnits", Mathf.CeilToInt(numPoints / 2.0f));
        kernel = ChunkLightPrimer.FindKernel("CleanChunk");
        ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out threadGroupSize, out _, out _);
        numThreads = Mathf.CeilToInt(numPoints / (2.0f * (float)threadGroupSize));
        ChunkLightPrimer.Dispatch(kernel, numThreads, 1, 1);

        //int2[] address = {0};
        //uint[] LightMap = new uint[mapChunkSize * mapChunkSize * mapChunkSize / 2];
        //GPUMapManager.DirectAddress.GetData(address, 0, (int)nAddress, 1);
        //GPUMapManager.Storage.GetData(LightMap, 0, address[0].x + GetLightMapStart(), mapChunkSize * mapChunkSize * mapChunkSize / 2);

    }

    public static void IterateLightUpdate(MonoBehaviour mono){
        int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
        LightSetupPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreads = Mathf.CeilToInt(Settings.MaxSubChunkUpdatesPerTick / (float)threadGroupSize);
        LightSetupPrimer.Dispatch(kernel, numThreads, 1, 1);

        kernel = LightSetupPrimer.FindKernel("PrimeQueue");
        LightSetupPrimer.Dispatch(kernel, 1, 1, 1);
        
        kernel = ObjectLightShader.FindKernel("BakeLights");
        ComputeBuffer args = UtilityBuffers.CountToArgs(ObjectLightShader, SubChunkUpdateBuffer, UpdateQueueOffsets.QueueCount, kernel);
        ObjectLightShader.DispatchIndirect(kernel, args);

        //int4[] count = new int4[2];
        //SubChunkUpdateBuffer.GetData(count);
        //Debug.Log("Count: " + count[0].x);
        //Debug.Log("HashInfo: " + count[0].zw);

    }

    public struct BakeQueueOffsets : BufferOffsets{
        /// <summary> The index of the element tracking the amount of items in the queue. </summary>
        public int QueueCount;
        /// <summary> The index of the element tracking the start of the queue. </summary>
        public int QueueStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the Map & Mesh generator. 
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}}
        /// <summary> The end of the buffer region that is used by the Map & Mesh generator. 
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}

        public BakeQueueOffsets(int start, int size){
            offsetStart = start;
            QueueCount = start;
            QueueStart = QueueCount + 1;
            offsetEnd = QueueCount + size;
        }
    }
}