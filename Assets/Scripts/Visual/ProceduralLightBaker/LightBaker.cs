using System;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain;
using Arterra.Utils;
using Arterra.Core;
using static Arterra.Core.Storage.SharedResourceManager;

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
            GraphicsResourceContext gpuContext = GraphicsGeneration;
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
            gpuContext.SetBufferData(DirtyShadowSubChunks, new uint2[] { 0 }, 0, DirtyQueueOffsets.QueueCount, 1);
            gpuContext.SetBufferData(DirtyObjectSubChunks, new uint2[] { 0 }, 0, DirtyQueueOffsets.QueueCount, 1);

            int mapSize = terrain.mapChunkSize * terrain.mapChunkSize * terrain.mapChunkSize;
            int mapFullSize = mapSize + (terrain.mapChunkSize + 3) * (terrain.mapChunkSize + 3) * 9;
            int lightMapSize = Mathf.CeilToInt(mapSize / 2.0f);
            int IsoValue = Mathf.RoundToInt(terrain.IsoLevel * 255.0f);
            SetGlobalLightSamplerData(IsoValue, mapFullSize);

            int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "DirtyObjectSubChunks", DirtyObjectSubChunks);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            kernel = LightSetupPrimer.FindKernel("PrimeQueue");
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "DirtyObjectSubChunks", DirtyObjectSubChunks);
            gpuContext.SetBuffer(LightSetupPrimer, kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);

            gpuContext.SetInts(LightSetupPrimer, "bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            gpuContext.SetInts(LightSetupPrimer, "bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            gpuContext.SetInt(LightSetupPrimer, "chunkLHOffset", mapFullSize + lightMapSize);
            gpuContext.SetInt(LightSetupPrimer, "ShadowUpdateCount", Settings.MaxShadowSubChunkUpdatesPerTick);
            gpuContext.SetInt(LightSetupPrimer, "ObjectUpdateCount", Settings.MaxObjectSubChunkUpdatesPerTick);
            gpuContext.SetInt(LightSetupPrimer, "QueueSize", SubChunkCount);

            kernel = ObjectLightShader.FindKernel("BakeLights");
            gpuContext.SetBuffer(ObjectLightShader, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(ObjectLightShader, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(ObjectLightShader, kernel, "DirtySubChunks", DirtyObjectSubChunks);
            gpuContext.SetBuffer(ObjectLightShader, kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            gpuContext.SetInts(ObjectLightShader, "bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            gpuContext.SetInts(ObjectLightShader, "bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            gpuContext.SetInt(ObjectLightShader, "QueueSize", SubChunkCount);
            gpuContext.SetInt(ObjectLightShader, "chunkLMOffset", mapFullSize);
            gpuContext.SetInt(ObjectLightShader, "chunkLHOffset", mapFullSize + lightMapSize);
            gpuContext.SetInt(ObjectLightShader, "subChunkSize", SubChunkSize);
            gpuContext.SetInt(ObjectLightShader, "subChunksAxis", Settings.SubChunkDivisions);
            gpuContext.SetInt(ObjectLightShader, "IsoLevel", IsoValue);
            gpuContext.SetInt(ObjectLightShader, "mapChunkSize", terrain.mapChunkSize); //as int
            gpuContext.SetInt(ObjectLightShader, "numPointsPerAxis", terrain.mapChunkSize); //as uint

            kernel = ShadowShader.FindKernel("BakeLights");
            gpuContext.SetBuffer(ShadowShader, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(ShadowShader, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(ShadowShader, kernel, "DirtySubChunks", DirtyShadowSubChunks);
            gpuContext.SetBuffer(ShadowShader, kernel, "CurUpdateSubChunks", SubChunkUpdateBuffer);
            gpuContext.SetInts(ShadowShader, "bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            gpuContext.SetInts(ShadowShader, "bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            gpuContext.SetInt(ShadowShader, "QueueSize", SubChunkCount);
            gpuContext.SetInt(ShadowShader, "chunkLMOffset", mapFullSize);
            gpuContext.SetInt(ShadowShader, "chunkLHOffset", mapFullSize + lightMapSize);
            gpuContext.SetInt(ShadowShader, "subChunkSize", SubChunkSize);
            gpuContext.SetInt(ShadowShader, "subChunksAxis", Settings.SubChunkDivisions);
            gpuContext.SetInt(ShadowShader, "IsoLevel", IsoValue);
            gpuContext.SetInt(ShadowShader, "mapChunkSize", terrain.mapChunkSize); //as int
            gpuContext.SetInt(ShadowShader, "numPointsPerAxis", terrain.mapChunkSize); //as uint

            kernel = ChunkLightPrimer.FindKernel("CopyHash");
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_DirectAddress", GPUMapManager.DirectAddress);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "DirtySubChunks", DirtyObjectSubChunks); //Object Light is DirtySubChunks
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);
            kernel = ChunkLightPrimer.FindKernel("CleanChunk");
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_MemoryBuffer", GPUMapManager.Storage);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_AddressDict", GPUMapManager.Address);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "_DirectAddress", GPUMapManager.DirectAddress);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "DirtySubChunks", DirtyObjectSubChunks);
            gpuContext.SetBuffer(ChunkLightPrimer, kernel, "DirtyShadowSubChunks", DirtyShadowSubChunks);

            gpuContext.SetInts(ChunkLightPrimer, "bCOUNT", new int[] { DirtyQueueOffsets.QueueCount, TickUpdateOffsets.ShadowCount, TickUpdateOffsets.ObjectCount });
            gpuContext.SetInts(ChunkLightPrimer, "bSTART", new int[] { DirtyQueueOffsets.QueueStart, TickUpdateOffsets.ShadowStart, TickUpdateOffsets.ObjectStart });
            gpuContext.SetInt(ChunkLightPrimer, "chunkLHOffset", mapFullSize + lightMapSize);
            gpuContext.SetInt(ChunkLightPrimer, "chunkLMOffset", mapFullSize);
            gpuContext.SetInt(ChunkLightPrimer, "subChunkSize", SubChunkSize);
            gpuContext.SetInt(ChunkLightPrimer, "subChunksAxis", Settings.SubChunkDivisions);
            gpuContext.SetInt(ChunkLightPrimer, "QueueSize", SubChunkCount);
            gpuContext.SetInt(ChunkLightPrimer, "numPointsPerAxis", terrain.mapChunkSize); //as uint
            gpuContext.SetInt(ChunkLightPrimer, "IsoLevel", IsoValue); //as int

            ArterraRuntime.MainLateUpdateTasks.Enqueue(new ArterraRuntime.IndirectUpdate(IterateLightUpdate));
        }

        public static void Release()
        {
            DirtyShadowSubChunks?.Dispose();
            DirtyObjectSubChunks?.Dispose();
            SubChunkUpdateBuffer?.Dispose();

            Shader.SetGlobalBuffer(ShaderIDProps.BakedLightChunkAddressDict, (ComputeBuffer)null);
            Shader.SetGlobalBuffer(ShaderIDProps.BakedLightChunkInfoBuffer, (ComputeBuffer)null);
            Shader.SetGlobalInteger(ShaderIDProps.BakedLightIsoLevel, 0);
            Shader.SetGlobalInteger(ShaderIDProps.BakedLightChunkLMOffset, 0);
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

        private static void SetGlobalLightSamplerData(int isoValue, int lightMapOffset)
        {
            Shader.EnableKeyword("NO_EDITORLIGHTING");
            Shader.SetGlobalBuffer(ShaderIDProps.BakedLightChunkAddressDict, GPUMapManager.Address);
            Shader.SetGlobalBuffer(ShaderIDProps.BakedLightChunkInfoBuffer, GPUMapManager.Storage);
            Shader.SetGlobalInteger(ShaderIDProps.BakedLightIsoLevel, isoValue);
            Shader.SetGlobalInteger(ShaderIDProps.BakedLightChunkLMOffset, lightMapOffset);
        }

        public static void RegisterChunk(int3 CCoord, int mapChunkSize, uint nAddress, int wSkipInc)
        {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int SubChunkAxis = mapChunkSize / Settings.SubChunkDivisions;
            int numSubChunks = SubChunkAxis * SubChunkAxis * SubChunkAxis;
            gpuContext.SetInts(ChunkLightPrimer, "CCoord", new int[] { CCoord.x, CCoord.y, CCoord.z });
            gpuContext.SetInt(ChunkLightPrimer, "numLightUnits", Mathf.CeilToInt(numSubChunks / 4.0f));
            gpuContext.SetInt(ChunkLightPrimer, "nChunkAddress", (int)nAddress);

            int kernel = ChunkLightPrimer.FindKernel("CopyHash");
            ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreads = Mathf.CeilToInt(numSubChunks / (4.0f * (float)threadGroupSize));
            gpuContext.Dispatch(ChunkLightPrimer, kernel, numThreads, 1, 1);

            kernel = ChunkLightPrimer.FindKernel("CleanChunk");
            gpuContext.SetInt(ChunkLightPrimer, "SkipInc", wSkipInc);
            ChunkLightPrimer.GetKernelThreadGroupSizes(kernel, out threadGroupSize, out _, out _);
            numThreads = Mathf.CeilToInt(mapChunkSize / (float)threadGroupSize);
            gpuContext.Dispatch(ChunkLightPrimer, kernel, numThreads, numThreads, (numThreads + 1) / 2);


            //int2[] address = {0};
            //uint[] LightMap = new uint[mapChunkSize * mapChunkSize * mapChunkSize / 2];
            //GPUMapManager.DirectAddress.GetData(address, 0, (int)nAddress, 1);
            //GPUMapManager.Storage.GetData(LightMap, 0, address[0].x + GetLightMapStart(), mapChunkSize * mapChunkSize * mapChunkSize / 2);

        }

        public static void IterateLightUpdate(MonoBehaviour mono)
        {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int kernel = LightSetupPrimer.FindKernel("PrimeSubchunks");
            LightSetupPrimer.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int maxUpdates = Math.Max(Settings.MaxShadowSubChunkUpdatesPerTick, Settings.MaxObjectSubChunkUpdatesPerTick);
            if (maxUpdates == 0) return;

            int numThreads = Mathf.CeilToInt(maxUpdates / (float)threadGroupSize);
            gpuContext.Dispatch(LightSetupPrimer, kernel, numThreads, 1, 1);

            kernel = LightSetupPrimer.FindKernel("PrimeQueue");
            gpuContext.Dispatch(LightSetupPrimer, kernel, 1, 1, 1);

            kernel = ShadowShader.FindKernel("BakeLights");
            ComputeBuffer args = gpuContext.Args.CountToArgs(ShadowShader, SubChunkUpdateBuffer, TickUpdateOffsets.ShadowCount, kernel);
            gpuContext.DispatchIndirect(ShadowShader, kernel, args);

            kernel = ObjectLightShader.FindKernel("BakeLights");
            args = gpuContext.Args.CountToArgs(ObjectLightShader, SubChunkUpdateBuffer, TickUpdateOffsets.ObjectCount, kernel);
            gpuContext.DispatchIndirect(ObjectLightShader, kernel, args);

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