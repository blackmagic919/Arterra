using System;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain;
using Arterra.Utils;

namespace Arterra.Configuration.Quality
{
    /// <summary>
    /// Settings for the light baker, responsible for dynamically baking
    /// lighting in the world as the world changes. Light-baking is based off
    /// a procedurally rebaking system that propogates light divided into sub-chunks
    /// within a terrain chunk of the world.
    /// </summary>
    [Serializable]
    public struct LightBaker
    {
        /// <summary>
        /// The size of lighting sub-chunks relative to the size of the chunk.
        /// = <see cref="Arterra.Configuration.Quality.Terrain.mapChunkSize"/> / SubChunkSize
        /// </summary>
        public int SubChunkDivisions;
        /// <summary> The maximum number of object light
        /// sub-chunks that can be updated in a single tick.
        /// Constrains both time and memory usage of the light baker. 
        /// Recommended less than 2k </summary>
        public int MaxObjectSubChunkUpdatesPerTick;
        /// <summary> The maximum number of shadow sub-chunks
        /// that can be updated in a single tick.
        /// Constrains both time and memory usage of the light baker.
        /// Recommended less than 15k </summary>
        public int MaxShadowSubChunkUpdatesPerTick;
    }
}

namespace Arterra.Engine.Rendering
{
    public static class LightBaker
    {
        private static ComputeShader ChunkLightPrimer;
        private static ComputeShader LightSetupPrimer;
        private static ComputeShader ObjectLightShader;
        private static ComputeShader ShadowShader;
        private static ComputeBuffer DirtyShadowSubChunks;
        private static ComputeBuffer DirtyObjectSubChunks;
        private static ComputeBuffer SubChunkUpdateBuffer;
        private static BakeQueueOffsets DirtyQueueOffsets;
        private static UpdateQueueOffsets TickUpdateOffsets;
        private static Arterra.Configuration.Quality.LightBaker Settings => Config.CURRENT.Quality.Lighting;

        static LightBaker()
        { //That's a lot of Compute Shaders XD
            ChunkLightPrimer = Resources.Load<ComputeShader>("Compute/LightBaker/ChunkLightPrimer");
            LightSetupPrimer = Resources.Load<ComputeShader>("Compute/LightBaker/SubchunkPrimer");
            ObjectLightShader = Resources.Load<ComputeShader>("Compute/LightBaker/ObjectLightBaker");
            ShadowShader = Resources.Load<ComputeShader>("Compute/LightBaker/ShadowBaker");
        }

        public static void Initialize()
        {
            Arterra.Configuration.Quality.Terrain terrain = Config.CURRENT.Quality.Terrain;
            int NumChunks = OctreeTerrain.BalancedOctree.GetMaxNodes(terrain.MaxDepth, terrain.Balance, terrain.MinChunkRadius);
            int SubChunkCount = Settings.SubChunkDivisions * Settings.SubChunkDivisions * Settings.SubChunkDivisions;
            int SubChunkSize = terrain.mapChunkSize / Settings.SubChunkDivisions;
            SubChunkCount *= NumChunks;

            DirtyShadowSubChunks = new ComputeBuffer(SubChunkCount + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
            DirtyObjectSubChunks = new ComputeBuffer(SubChunkCount + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
            SubChunkUpdateBuffer = new ComputeBuffer(Settings.MaxObjectSubChunkUpdatesPerTick + Settings.MaxShadowSubChunkUpdatesPerTick + 1, sizeof(uint) * 2, ComputeBufferType.Structured);
            DirtyQueueOffsets = new BakeQueueOffsets(0, SubChunkCount);
            TickUpdateOffsets = new UpdateQueueOffsets(0, Settings.MaxShadowSubChunkUpdatesPerTick, Settings.MaxObjectSubChunkUpdatesPerTick);
            DirtyShadowSubChunks.SetData(new uint2[] { 0 }, 0, DirtyQueueOffsets.QueueCount, 1);
            DirtyObjectSubChunks.SetData(new uint2[] { 0 }, 0, DirtyQueueOffsets.QueueCount, 1);

            int mapSize = terrain.mapChunkSize * terrain.mapChunkSize * terrain.mapChunkSize;
            int mapFullSize = mapSize + (terrain.mapChunkSize + 3) * (terrain.mapChunkSize + 3) * 9;
            int lightMapSize = Mathf.CeilToInt(mapSize / 2.0f);
            int IsoValue = Mathf.RoundToInt(terrain.IsoLevel * 255.0f);

            int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
            LightSetupPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            LightSetupPrimer.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            LightSetupPrimer.SetBuffer(kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            LightSetupPrimer.SetBuffer(kernel, "DirtyObjectSubChunks", DirtyObjectSubChunks);
            LightSetupPrimer.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            kernel = LightSetupPrimer.FindKernel("PrimeQueue");
            LightSetupPrimer.SetBuffer(kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            LightSetupPrimer.SetBuffer(kernel, "DirtyObjectSubChunks", DirtyObjectSubChunks);
            LightSetupPrimer.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);

            LightSetupPrimer.SetInts("bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            LightSetupPrimer.SetInts("bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            LightSetupPrimer.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
            LightSetupPrimer.SetInt("ShadowUpdateCount", Settings.MaxShadowSubChunkUpdatesPerTick);
            LightSetupPrimer.SetInt("ObjectUpdateCount", Settings.MaxObjectSubChunkUpdatesPerTick);
            LightSetupPrimer.SetInt("QueueSize", SubChunkCount);
            GPUMapManager.SetCCoordHash(LightSetupPrimer);

            kernel = ObjectLightShader.FindKernel("BakeLights");
            ObjectLightShader.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            ObjectLightShader.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            ObjectLightShader.SetBuffer(kernel, "DirtySubChunks", DirtyObjectSubChunks);
            ObjectLightShader.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            ObjectLightShader.SetInts("bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            ObjectLightShader.SetInts("bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            ObjectLightShader.SetInt("QueueSize", SubChunkCount);
            ObjectLightShader.SetInt("chunkLMOffset", mapFullSize);
            ObjectLightShader.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
            ObjectLightShader.SetInt("subChunkSize", SubChunkSize);
            ObjectLightShader.SetInt("subChunksAxis", Settings.SubChunkDivisions);
            ObjectLightShader.SetInt("IsoLevel", IsoValue);
            ObjectLightShader.SetInt("mapChunkSize", terrain.mapChunkSize); //as int
            ObjectLightShader.SetInt("numPointsPerAxis", terrain.mapChunkSize); //as uint
            GPUMapManager.SetCCoordHash(ObjectLightShader);

            kernel = ShadowShader.FindKernel("BakeLights");
            ShadowShader.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            ShadowShader.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            ShadowShader.SetBuffer(kernel, "DirtySubChunks", DirtyShadowSubChunks);
            ShadowShader.SetBuffer(kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            ShadowShader.SetInts("bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            ShadowShader.SetInts("bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            ShadowShader.SetInt("QueueSize", SubChunkCount);
            ShadowShader.SetInt("chunkLMOffset", mapFullSize);
            ShadowShader.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
            ShadowShader.SetInt("subChunkSize", SubChunkSize);
            ShadowShader.SetInt("subChunksAxis", Settings.SubChunkDivisions);
            ShadowShader.SetInt("IsoLevel", IsoValue);
            ShadowShader.SetInt("mapChunkSize", terrain.mapChunkSize); //as int
            ShadowShader.SetInt("numPointsPerAxis", terrain.mapChunkSize); //as uint
            GPUMapManager.SetCCoordHash(ShadowShader);

            kernel = ChunkLightPrimer.FindKernel("CopyHash");
            ChunkLightPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            ChunkLightPrimer.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            ChunkLightPrimer.SetBuffer(kernel, "_DirectAddress", GPUMapManager.DirectAddress);
            ChunkLightPrimer.SetBuffer(kernel, "DirtySubChunks", DirtyObjectSubChunks); //Object Light is DirtySubChunks
            ChunkLightPrimer.SetBuffer(kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            kernel = ChunkLightPrimer.FindKernel("CleanChunk");
            ChunkLightPrimer.SetBuffer(kernel, "_MemoryBuffer", GPUMapManager.Storage);
            ChunkLightPrimer.SetBuffer(kernel, "_AddressDict", GPUMapManager.Address);
            ChunkLightPrimer.SetBuffer(kernel, "_DirectAddress", GPUMapManager.DirectAddress);
            ChunkLightPrimer.SetBuffer(kernel, "DirtySubChunks", DirtyObjectSubChunks);
            ChunkLightPrimer.SetBuffer(kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);

            ChunkLightPrimer.SetInts("bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            ChunkLightPrimer.SetInts("bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            ChunkLightPrimer.SetInt("chunkLHOffset", mapFullSize + lightMapSize);
            ChunkLightPrimer.SetInt("chunkLMOffset", mapFullSize);
            ChunkLightPrimer.SetInt("subChunkSize", SubChunkSize);
            ChunkLightPrimer.SetInt("subChunksAxis", Settings.SubChunkDivisions);
            ChunkLightPrimer.SetInt("QueueSize", SubChunkCount);
            ChunkLightPrimer.SetInt("numPointsPerAxis", terrain.mapChunkSize); //as uint
            ChunkLightPrimer.SetInt("IsoLevel", IsoValue); //as int
            GPUMapManager.SetCCoordHash(ChunkLightPrimer);

            OctreeTerrain.MainLateUpdateTasks.Enqueue(new IndirectUpdate(IterateLightUpdate));
        }

        public static void Release()
        {
            DirtyShadowSubChunks?.Dispose();
            DirtyObjectSubChunks?.Dispose();
            SubChunkUpdateBuffer?.Dispose();
        }

        public static int GetLightMapLength()
        {
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            int mapSize = mapChunkSize * mapChunkSize * mapChunkSize;
            int SubChunkAxis = mapChunkSize / Settings.SubChunkDivisions;
            int SubChunkCount = SubChunkAxis * SubChunkAxis * SubChunkAxis;
            return Mathf.CeilToInt(mapSize / 2.0f) + Mathf.CeilToInt(SubChunkCount / 4.0f);
        }

        public static int GetLightMapStart()
        {
            int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            int mapSize = mapChunkSize * mapChunkSize * mapChunkSize;
            int mapFullSize = mapSize + (mapChunkSize + 3) * (mapChunkSize + 3) * 9;
            return mapFullSize;
        }

        public static void SetupLightSampler(Material mat)
        {
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

        public static void SetupLightSampler(ComputeShader shad, int kernel)
        {
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

        public static void RegisterChunk(int3 CCoord, int mapChunkSize, uint nAddress, int wSkipInc)
        {
            int SubChunkAxis = mapChunkSize / Settings.SubChunkDivisions;
            int numSubChunks = SubChunkAxis * SubChunkAxis * SubChunkAxis;
            ChunkLightPrimer.SetInts("CCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });
            ChunkLightPrimer.SetInt("numLightUnits", Mathf.CeilToInt(numSubChunks / 4.0f));
            ChunkLightPrimer.SetInt("nChunkAddress", (int)nAddress);

            int kernel = ChunkLightPrimer.FindKernel("CopyHash");
            ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreads = Mathf.CeilToInt(numSubChunks / (4.0f * (float)threadGroupSize));
            ChunkLightPrimer.Dispatch(kernel, numThreads, 1, 1);

            kernel = ChunkLightPrimer.FindKernel("CleanChunk");
            ChunkLightPrimer.SetInt("SkipInc", wSkipInc);
            ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out threadGroupSize, out _, out _);
            numThreads = Mathf.CeilToInt(mapChunkSize / (float)threadGroupSize);
            ChunkLightPrimer.Dispatch(kernel, numThreads, numThreads, (numThreads + 1) / 2);


            //int2[] address = {0};
            //uint[] LightMap = new uint[mapChunkSize * mapChunkSize * mapChunkSize / 2];
            //GPUMapManager.DirectAddress.GetData(address, 0, (int)nAddress, 1);
            //GPUMapManager.Storage.GetData(LightMap, 0, address[0].x + GetLightMapStart(), mapChunkSize * mapChunkSize * mapChunkSize / 2);

        }

        public static void IterateLightUpdate(MonoBehaviour mono)
        {
            int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
            LightSetupPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int maxUpdates = Math.Max(Settings.MaxShadowSubChunkUpdatesPerTick, Settings.MaxObjectSubChunkUpdatesPerTick);
            if (maxUpdates == 0) return;

            int numThreads = Mathf.CeilToInt(maxUpdates / (float)threadGroupSize);
            LightSetupPrimer.Dispatch(kernel, numThreads, 1, 1);

            kernel = LightSetupPrimer.FindKernel("PrimeQueue");
            LightSetupPrimer.Dispatch(kernel, 1, 1, 1);

            kernel = ShadowShader.FindKernel("BakeLights");
            ComputeBuffer args = UtilityBuffers.CountToArgs(ShadowShader, SubChunkUpdateBuffer, TickUpdateOffsets.ShadowCount, kernel);
            ShadowShader.DispatchIndirect(kernel, args);

            kernel = ObjectLightShader.FindKernel("BakeLights");
            args = UtilityBuffers.CountToArgs(ObjectLightShader, SubChunkUpdateBuffer, TickUpdateOffsets.ObjectCount, kernel);
            ObjectLightShader.DispatchIndirect(kernel, args);

            //int2[] count = new int2[4];
            //DirtyObjectSubChunks.GetData(count, 0, 0, 4);
            //Debug.Log("Count: " + count[0].xy);
        }

        public struct BakeQueueOffsets : BufferOffsets
        {
            /// <summary> The index of the element tracking the amount of items in the queue. </summary>
            public int QueueCount;
            /// <summary> The index of the element tracking the start of the queue. </summary>
            public int QueueStart;
            private int offsetStart; private int offsetEnd;
            /// <summary> The start of the buffer region that is used by the Map & Mesh generator. 
            /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
            public int bufferStart { get { return offsetStart; } }
            /// <summary> The end of the buffer region that is used by the Map & Mesh generator. 
            /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
            public int bufferEnd { get { return offsetEnd; } }

            public BakeQueueOffsets(int start, int size)
            {
                offsetStart = start;
                QueueCount = start;
                QueueStart = QueueCount + 1;
                offsetEnd = QueueCount + size;
            }
        }

        public struct UpdateQueueOffsets : BufferOffsets
        {
            /// <summary> The index of the element tracking the amount of items in the queue. </summary>
            public int ShadowCount;
            /// <summary> The index of the element tracking the start of the queue. </summary>
            public int ShadowStart;
            /// <summary> The index of the element tracking the amount of object light subchunks in the queue. </summary>
            public int ObjectCount;
            /// <summary> The index of the element tracking the start of the object light subchunk queue. </summary>
            public int ObjectStart;
            private int offsetStart; private int offsetEnd;
            /// <summary> The start of the buffer region that is used by the Map & Mesh generator. 
            /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
            public int bufferStart { get { return offsetStart; } }
            /// <summary> The end of the buffer region that is used by the Map & Mesh generator. 
            /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
            public int bufferEnd { get { return offsetEnd; } }

            public UpdateQueueOffsets(int start, int shadSize, int objSize)
            {
                offsetStart = start;
                ShadowCount = start;
                ShadowStart = ShadowCount + 1;
                ObjectCount = ShadowStart + shadSize;
                ObjectStart = ObjectCount + 1;
                offsetEnd = ObjectStart + objSize;
            }
        }
    }
}