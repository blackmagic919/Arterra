using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using Arterra.Engine.Terrain;
using Arterra.Utils;
using Arterra.Engine.Rendering;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.SharedResourceManager;
using UnityEngine.Rendering;

namespace Arterra.Core.Storage{
    /// <summary> A static centralized gateway for all CPU-side operations to access or attach resources capable of accessing
    /// map-related information stored on the GPU. Responsible for organizing managers which maintain the organization
    /// and integrity of structures facilitating map lookup on the GPU. </summary>
    public static class GPUMapManager {
        private static ComputeShader dictReplaceKey;
        private static ComputeShader transcribeMapInfo;
        private static ComputeShader multiMapTranscribe;
        private static ComputeShader simplifyMap;
        private static uint[] MapLookup;
        private static ChunkHandle[] HandleDict;
        private static ComputeBuffer _ChunkAddressDict;
        private static MemoryBufferHandler memorySpace;
        /// <summary> How far from the user processes on the GPU can lookup information about the world. The radius
        /// in chunk space of the perfect hash-map which enables random access lookups of map information on the GPU.  </summary>
        public static int numChunksRadius;
        /// <summary> Whether <see cref="GPUMapManager"/> has been initialized and calls to access GPU map information can be granted.
        /// Whether <see cref="Initialize"/> has been called since the start of the world instance. </summary>
        public static bool initialized = false;
        private static int numChunksAxis;
        private static int mapChunkSize;
        private static float lerpScale;

        /// <summary> Initializes <see cref="GPUMapManager"/> to manage storage and access of
        /// GPU-side map information based off the settings of the current world. </summary>
        public static void Initialize() {
            Release();
            Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
            dictReplaceKey = Resources.Load<ComputeShader>("Compute/MapData/ReplaceDictChunk");
            transcribeMapInfo = Resources.Load<ComputeShader>("Compute/MapData/TranscribeMapInfo");
            multiMapTranscribe = Resources.Load<ComputeShader>("Compute/MapData/MultiMapTranscriber");
            simplifyMap = Resources.Load<ComputeShader>("Compute/MapData/DensitySimplificator");

            lerpScale = rSettings.lerpScale;
            int BaseMapLength = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
            int MapDiameter = BaseMapLength + rSettings.MapExtendDist * 2;

            mapChunkSize = rSettings.mapChunkSize;
            numChunksRadius = (MapDiameter + MapDiameter % 2) / 2;
            numChunksAxis = numChunksRadius * 2;
            int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

            MapLookup = new uint[numChunks];
            HandleDict = new ChunkHandle[numChunks + 1];
            for (int i = 1; i < numChunks; i++) {
                HandleDict[i] = new(i - 1, i + 1);
            }
            HandleDict[numChunks] = new (numChunks - 1, 1);
            HandleDict[1].Prev = numChunks;
            HandleDict[0].Next = 1; //Free position

            _ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint) * 5, ComputeBufferType.Structured);
            GraphicsGeneration.SetBufferData(_ChunkAddressDict, Enumerable.Repeat(0u, numChunks * 5).ToArray());

            //This isn't an mathematical upper limit because we're not accounting for light map size and temporary
            //duplication but in practice, GetDepthOfDistance and GetMaxNodes always overestimate.
            int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
            int mapSize = numPoints + Arterra.Engine.Rendering.LightBaker.GetLightMapLength();
            int depth = OctreeTerrain.BalancedOctree.GetDepthOfDistance(numChunksRadius, rSettings.Balance, (uint)rSettings.MinChunkRadius);
            int memSize = (int)math.min(mapSize * OctreeTerrain.BalancedOctree.GetMaxNodes(depth, rSettings.Balance, rSettings.MinChunkRadius) * 1.5f, SystemInfo.maxGraphicsBufferSize/4.0f);
            Memory settings = ScriptableObject.CreateInstance<Memory>();
            settings.StorageSize = memSize;
            memorySpace = new MemoryBufferHandler(settings);

            SetGlobalWSCCoordHelper();

            initialized = true;
        }

        /// <summary> Releases all resources used by <see cref="GPUMapManager"/> to manage and track
        /// map information on the GPU. Call this once on cleanup when the world is unloaded  </summary>
        public static void Release() {
            memorySpace?.Release();
            _ChunkAddressDict?.Release();
            Shader.SetGlobalFloat(ShaderIDProps.WorldLerpScale, 0.0f);
            Shader.SetGlobalInteger(ShaderIDProps.WorldMapChunkSize, 0);
            Shader.SetGlobalInteger(ShaderIDProps.ChunkMapNumChunksPerAxis, 0);
            initialized = false;
        }

        public static bool CanAccessChunk(int handle, GraphicsResourceContext graphicsContext) {
            if (HandleDict[handle].viewerId == GraphicsContextId.None) return true;
            if (HandleDict[handle].viewerId == graphicsContext.id) return true;
            if (HandleDict[handle].fence.passed) return true;
            return false;
        }

        public static bool TryBindChunkOperation(int handle, GraphicsResourceContext graphicsContext) {
            if (!CanAccessChunk(handle, graphicsContext)) return false;
            HandleDict[handle].viewerId = graphicsContext.id;
            HandleDict[handle].fence = graphicsContext.Commands.CreateGraphicsFence(
                GraphicsFenceType.AsyncQueueSynchronisation,
                SynchronisationStageFlags.AllGPUOperations
            );
            return true;
        }

        /// <summary>Checks whether every stored map intersecting a half-open grid-space
        /// region is accessible from the supplied graphics context.</summary>
        /// <param name="MinGCoord">The inclusive minimum coordinate in grid space.</param>
        /// <param name="MaxGCoord">The exclusive maximum coordinate in grid space.</param>
        /// <param name="graphicsContext">The context requesting access.</param>
        /// <returns>Whether every stored map in the region is accessible.</returns>
        public static bool TryAccessRegionWithGraphics(int3 MinGCoord, int3 MaxGCoord,
            GraphicsResourceContext graphicsContext) {
            if (!initialized || graphicsContext == null || math.any(MaxGCoord <= MinGCoord))
                return false;

            int3 minCCoord = OctreeTerrain.BalancedOctree.FloorCCoord(
                MinGCoord, mapChunkSize);
            int3 maxCCoord = OctreeTerrain.BalancedOctree.FloorCCoord(MaxGCoord - 1,
                mapChunkSize) + 1;
            int3 viewerCoord = OctreeTerrain.ViewPosCS;
            minCCoord = math.max(minCCoord, viewerCoord - numChunksRadius);
            maxCCoord = math.min(maxCCoord, viewerCoord + numChunksRadius + 1);

            for (int x = minCCoord.x; x < maxCCoord.x; x++) {
            for (int y = minCCoord.y; y < maxCCoord.y; y++) {
            for (int z = minCCoord.z; z < maxCCoord.z; z++) {
                int handle = (int)MapLookup[HashCoord(new int3(x, y, z))];
                if (handle == 0) continue;
                if (!CanAccessChunk(handle, graphicsContext)) return false;
            }}}
            return true;
        }

        /// <summary>Records completion fences for a region after its GPU operation has been recorded.</summary>
        public static bool TryBindRegionOperation(int3 MinGCoord, int3 MaxGCoord,
            GraphicsResourceContext graphicsContext) {
            if (!TryAccessRegionWithGraphics(MinGCoord, MaxGCoord, graphicsContext)) return false;
            int3 minCCoord = OctreeTerrain.BalancedOctree.FloorCCoord(
                MinGCoord, mapChunkSize);
            int3 maxCCoord = OctreeTerrain.BalancedOctree.FloorCCoord(MaxGCoord - 1,
                mapChunkSize) + 1;
            int3 viewerCoord = OctreeTerrain.ViewPosCS;
            minCCoord = math.max(minCCoord, viewerCoord - numChunksRadius);
            maxCCoord = math.min(maxCCoord, viewerCoord + numChunksRadius + 1);

            HashSet<int> handles = new();
            for (int x = minCCoord.x; x < maxCCoord.x; x++) {
            for (int y = minCCoord.y; y < maxCCoord.y; y++) {
            for (int z = minCCoord.z; z < maxCCoord.z; z++) {
                int handle = (int)MapLookup[HashCoord(new int3(x, y, z))];
                if (handle != 0) handles.Add(handle);
            }}}
            foreach (int handle in handles)
                if (!TryBindChunkOperation(handle, graphicsContext)) return false;
            return true;
        }

        /// <summary>
        /// Registers a visual chunk by copying its map information from working memory to long-term GPU memory,
        /// updating the lookup structure(s) and garbage collecting(permanently) any map information made unaccessible.
        /// The hashmap structure is scaled to the size of a real chunk (chunk space) and thus a visual chunk
        /// <see cref="TerrainChunk.VisualChunk"/> overlaps multiple entries in the hasmap and will require writing
        /// to multiple entires. Thus this operation has a time complexity of O((2^depth)^3).
        /// </summary>
        /// <param name="oCCoord">The coordinate of the origin(lowest coordinate) of the visual chunk in chunk space. </param>
        /// <param name="depth">The <see cref="TerrainChunk.depth">depth</see> of the chunk; indicates the size of the chunk. </param>
        /// <param name="mapData">The working buffer currently containing the map information of the chunk.</param>
        /// <param name="rdOff">The offset within <i>mapData</i> where the map information of the chunk begins.</param>
        /// <returns>An integer representing a handle to the chunk given by the internal chunk reference manager
        /// capable of being used to directly query the stored map information. </returns>
        public static int RegisterChunkVisual(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0) {
            if (!IsChunkRegisterable(oCCoord, depth)) return -1;
            int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
            int numPointsFaces = (mapChunkSize + 3) * (mapChunkSize + 3) * 9;
            int mapSize = numPoints + numPointsFaces + Arterra.Engine.Rendering.LightBaker.GetLightMapLength();
            uint address = memorySpace.AllocateMemoryDirect(mapSize, 1);
            TranscribeMap(mapData, address, rdOff, mapChunkSize + 3, mapChunkSize, 1);
            TranscribeEdgeFaces(mapData, address, rdOff, mapChunkSize + 3, mapChunkSize + 3, numPoints);
            uint handleAddress = AllocateHandle(); HandleDict[handleAddress] = new ((int)address, 0);
            Arterra.Engine.Rendering.LightBaker.RegisterChunk(oCCoord, mapChunkSize, address, 1 << depth);

            RegisterChunk(oCCoord, depth, handleAddress);
            return (int)handleAddress;
        }

        /// <summary> Registers a real chunk by copying its map information from working memory to long-term GPU memory,
        /// updating the lookup structure(s) and garbage collecting(permanently) any map information made unaccessible.
        /// The hashmap structure is scaled to the size of a real chunk (chunk space) and thus this will only replace
        /// and garbage collect at most one previously tracked chunk. </summary>
        /// <param name="oCCoord">The coordinate of the real chunk in chunk space. </param>
        /// <param name="depth">The <see cref="TerrainChunk.depth">depth</see> of the chunk. This must be 0. </param>
        /// <param name="mapData">The working buffer currently containing the map information of the chunk.</param>
        /// <param name="rdOff">The offset within <i>mapData</i> where the map information of the chunk begins.</param>
        /// <returns>An integer representing a handle to the chunk given by the internal chunk reference manager
        /// capable of being used to directly query the stored map information. </returns>
        public static int RegisterChunkReal(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0) {
            if (!IsChunkRegisterable(oCCoord, depth)) return -1;
            int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
            //This is unnecessary here, but we need to ensure it so that other systems(e.g. Lighting)
            //Have a consistent chunk size to buffer with
            int numPointsFaces = (mapChunkSize + 3) * (mapChunkSize + 3) * 9;
            int mapSize = numPoints + numPointsFaces + Engine.Rendering.LightBaker.GetLightMapLength();
            uint address = memorySpace.AllocateMemoryDirect(mapSize, 1);
            TranscribeMap(mapData, address, rdOff, mapChunkSize, mapChunkSize);
            uint handleAddress = AllocateHandle(); HandleDict[handleAddress] = new ((int)address, 0);
            Engine.Rendering.LightBaker.RegisterChunk(oCCoord, mapChunkSize, address, 1 << depth);

            //uint2[] addBuff = new uint2[1];
            //memorySpace.Address.GetData(addBuff, 0, (int)address, 1);
            //if(addBuff[0].x == 0 || addBuff[0].y == 0) Debug.Log(addBuff[0]);

            RegisterChunk(oCCoord, depth, handleAddress);
            return (int)handleAddress;
        }
        /// <summary> Obtains the reference information associated with this handle. </summary>
        /// <param name="handleAddress">The handle returned from <see cref="RegisterChunkReal"/> or <see cref="RegisterChunkVisual"/></param>
        /// <returns>Two integers: 1. the address within <see cref="DirectAddress">the direct unmanaged memory
        /// address buffer</see> of the address within <see cref="Storage"/> of the stored chunk information, and
        /// 2. the amount of references held by either the hasmap or <see cref="SubscribeHandle">manually</see>.</returns>
        public static ChunkHandle GetHandle(int handleAddress) => HandleDict[handleAddress];
        public static int GetChunkHandle(int3 coordinate) => (int)MapLookup[HashCoord(coordinate)];
        /// <summary>Prevents the stored map information from being garbage collected even if other chunks completely
        /// replace it in the hashmap making it unaccessible. Increments the amount of references held to the
        /// map information pointed to by <i>handleAddress</i> by one. </summary>
        /// <param name="handleAddress">The handle returned from <see cref="RegisterChunkReal"/> or <see cref="RegisterChunkVisual"/></param>
        public static void SubscribeHandle(uint handleAddress) => HandleDict[handleAddress].Next++;
        /// <summary> Returns one subscription allowing the stored map information to be garbage collected again if it is completely
        /// replaced in the lookup hashmap and all subscriptions are returned. Do not call this function without first calling
        /// <see cref="SubscribeHandle"/> or severe undefined behavior may occur. This operation may trigger garbage collection
        /// of the map information if it is the last reference held. Specifically, decrements the amount of references held to the
        /// map information pointed to by <i>handleAddress</i> by one. </summary>
        /// <param name="handleAddress">The handle returned from <see cref="RegisterChunkReal"/> or <see cref="RegisterChunkVisual"/></param>
        public static void UnsubscribeHandle(int handleAddress, GraphicsContextId caller = GraphicsContextId.Generation) {
            HandleDict[handleAddress].Subscribers--;
            if (HandleDict[handleAddress].Subscribers > 0) return;

            uint address = (uint)HandleDict[handleAddress].Address;
            GraphicsResourceContext generation = GraphicsGeneration;
            GraphicsResourceContext rendering = GraphicsRendering;
            var owner = memorySpace;
            // First let publication finish. Future draws then resolve the new map.
            // Fence rendering afterwards to include existing fog/light readers too.
            rendering.PollGraphicsContext(generation, _ => {
                if (!initialized || memorySpace != owner) return;
                generation.PollGraphicsContext(rendering, _ => {
                    if (!initialized || memorySpace != owner) return;
                    owner.ReleaseMemory(address);
                    FreeHandle(handleAddress);
                });
            });
        }

        //Origin Chunk Coord, Viewer Chunk Coord(where the map is centered), depth
        private static void RegisterChunk(int3 oCCoord, int depth, uint handleAddress) {
            int3 eCCoord = oCCoord + (1 << depth); int3 cOff = oCCoord;
            int3 vCCoord = OctreeTerrain.ViewPosCS;
            oCCoord = math.clamp(oCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
            eCCoord = math.clamp(eCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
            cOff = oCCoord - cOff;
            int3 dim = eCCoord - oCCoord;
            if (math.any(dim <= 0)) return; //We're not going to save it

            //Request Memory Address
            int count = dim.x * dim.y * dim.z;
            HandleDict[handleAddress].Subscribers += count;

            List<int> replacedHandles = new();
            int3 dCoord;
            for (dCoord.x = 0; dCoord.x < dim.x; dCoord.x++) {
                for (dCoord.y = 0; dCoord.y < dim.y; dCoord.y++) {
                    for (dCoord.z = 0; dCoord.z < dim.z; dCoord.z++) {
                        int hash = HashCoord(dCoord + oCCoord);
                        uint prevAdd = MapLookup[hash];
                        MapLookup[hash] = handleAddress;
                        if (prevAdd == 0) continue;
                        replacedHandles.Add((int)prevAdd);
                    }
                }
            }
            //Fill out Compute Buffer Memory Handles
            ReplaceAddress(memorySpace.Address, (uint)HandleDict[handleAddress].Address, oCCoord, dim, cOff, depth);
            TryBindChunkOperation((int)handleAddress, GraphicsGeneration);
            foreach (int previous in replacedHandles) UnsubscribeHandle(previous);
        }

        /// <summary> Whether or not the chunk will be stored and tracked by <see cref="GPUMapManager"/> if
        /// <see cref="RegisterChunkVisual"/> were to be called on it. Chunks that are too far away from the viewer
        /// will not be tracked. </summary>
        /// <param name="oCCoord">The coordinate of the origin(lowest coordinate) of the visual chunk in chunk space.</param>
        /// <param name="depth">The <see cref="TerrainChunk.depth">depth</see> of the chunk; indicates the size of the chunk.</param>
        /// <returns></returns>
        public static bool IsChunkRegisterable(int3 oCCoord, int depth) {
            int3 eCCoord = oCCoord + (1 << depth);
            int3 vCCoord = OctreeTerrain.ViewPosCS;
            oCCoord = math.clamp(oCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
            eCCoord = math.clamp(eCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
            int3 dim = eCCoord - oCCoord;
            if (math.any(dim <= 0)) return false;
            else return true;
        }

        private static uint AllocateHandle() {
            int free = HandleDict[0].Next;
            HandleDict[HandleDict[free].Prev].Next = HandleDict[free].Next;
            HandleDict[HandleDict[free].Next].Prev = HandleDict[free].Prev;
            HandleDict[0].Next = HandleDict[free].Next;
            return (uint)free;
        }

        private static void FreeHandle(int index) {
            int free = HandleDict[0].Next;
            HandleDict[index] = new (HandleDict[free].Prev, free);
            HandleDict[HandleDict[free].Prev].Next = index;
            HandleDict[free].Prev = index;
            HandleDict[0].Next = index;
        }

        /// <summary> Gets the hashed index of the given chunk space coordinate within the GPU lookup hashmap.
        /// This method is a perfect has in that a unique index is given for all coordinates
        /// within <see cref="numChunksRadius"/> distance of the coordinate. </summary>
        /// <param name="CCoord">A coordinate in chunk space</param>
        /// <returns>The associated hash index</returns>
        public static int HashCoord(int3 CCoord) {
            int3 hashCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            int hash = (hashCoord.x * numChunksAxis * numChunksAxis) + (hashCoord.y * numChunksAxis) + hashCoord.z;
            return hash;
        }

        /// <summary> The buffer containing all map information managed and tracked by <see cref="GPUMapManager"/>. </summary>
        public static ComputeBuffer Storage => memorySpace.Storage;
        /// <summary> The buffer containing the hashmap structure used to facilitate GPU-side map lookup </summary>
        public static ComputeBuffer Address => _ChunkAddressDict;
        /// <summary> The buffer containing the raw unstructured list of addresses to each chunk's map information. </summary>
        public static GraphicsBuffer DirectAddress => memorySpace.Address;


        static void TranscribeMap(ComputeBuffer mapData, uint address, int rStart, int readAxis, int writeAxis, int rdOff = 0) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int kernel = transcribeMapInfo.FindKernel("TranscribeMap");
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.MemoryBuffer, memorySpace.Storage);
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.AddressDict, memorySpace.Address);
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.ChunkData, mapData);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.AddressIndex, (int)address);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.StartRead, rStart);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.SizeReadAxis, readAxis);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.SizeWriteAxis, writeAxis);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.Offset, rdOff);

            int numPointsAxis = writeAxis;
            transcribeMapInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
            gpuContext.Dispatch(transcribeMapInfo, kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
        }

        static void TranscribeEdgeFaces(ComputeBuffer mapData, uint address, int rStart, int readAxis, int writeAxis, int wrOff = 0) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int kernel = transcribeMapInfo.FindKernel("TranscribeFaces");
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.MemoryBuffer, memorySpace.Storage);
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.AddressDict, memorySpace.Address);
            gpuContext.SetBuffer(transcribeMapInfo, kernel, ShaderIDProps.ChunkData, mapData);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.AddressIndex, (int)address);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.StartRead, rStart);

            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.SizeReadAxis, readAxis);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.SizeWriteAxis, writeAxis);
            gpuContext.SetInt(transcribeMapInfo, ShaderIDProps.Offset, wrOff);

            int numPointsAxis = writeAxis;
            transcribeMapInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
            gpuContext.Dispatch(transcribeMapInfo, kernel, numThreadsAxis, numThreadsAxis, 1);
        }

        /// <summary> Performs a selective replacing of a stored chunk's map information with sparse chunk information
        /// sourced from a working buffer. A stored chunk's MapData will only be replaced if the information from the replacing
        /// entry in the working buffer <see cref="Arterra.Core.Storage.MapData.isDirty">is dirty </see>, otherwise it is ignored.
        /// The caller should ensure that <see cref="RegisterChunkVisual"/> or <see cref="RegisterChunkReal"/> has been called
        /// on the provide chunk coordinate(<i>CCoord</i>) beforehand such that there exists a stored chunk map. </summary>
        /// <param name="mapData">The working buffer currently containing the new replacing sparse map information.</param>
        /// <param name="CCoord">The coordinate of the origin(lowest coordinate) of the chunk in chunk space; determins which
        /// stored chunk to replace.</param>
        /// <param name="depth">The <see cref="TerrainChunk.depth">depth</see> of the chunk; this must match the depth given when
        /// registering the chunk</param>
        /// <param name="rStart">The offset within <i>mapData</i> where the sparse map information of the chunk begins.</param>
        public static void TranscribeMultiMap(ComputeBuffer mapData, int3 CCoord, int depth, int rStart = 0) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int skipInc = 1 << depth;
            int numPointsPerAxis = mapChunkSize;

            gpuContext.SetBuffer(multiMapTranscribe, 0, ShaderIDProps.MemoryBuffer, memorySpace.Storage);
            gpuContext.SetBuffer(multiMapTranscribe, 0, ShaderIDProps.AddressDict, _ChunkAddressDict);
            gpuContext.SetBuffer(multiMapTranscribe, 0, ShaderIDProps.ChunkData, mapData);
            gpuContext.SetInts(multiMapTranscribe, ShaderIDProps.OriginCCoord, new int[3] { CCoord.x, CCoord.y, CCoord.z });
            gpuContext.SetInt(multiMapTranscribe, ShaderIDProps.NumPointsPerAxis, numPointsPerAxis);
            gpuContext.SetInt(multiMapTranscribe, ShaderIDProps.StartMap, rStart);
            gpuContext.SetInt(multiMapTranscribe, ShaderIDProps.MapChunkSize, mapChunkSize);
            gpuContext.SetInt(multiMapTranscribe, ShaderIDProps.SkipInc, skipInc);

            multiMapTranscribe.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
            gpuContext.Dispatch(multiMapTranscribe, 0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
            // Called immediately after registration, before a collector can acquire it.
            if (!TryBindRegionOperation(CCoord * mapChunkSize,
                (CCoord + skipInc) * mapChunkSize, gpuContext))
                throw new System.InvalidOperationException("Map write was recorded while another context owns its region.");
        }

        static void ReplaceAddress(GraphicsBuffer addressDict, uint address, int3 oCoord, int3 dimension, int3 offC, int depth) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            gpuContext.SetBuffer(dictReplaceKey, 0, ShaderIDProps.ChunkAddressDict, _ChunkAddressDict);
            gpuContext.SetBuffer(dictReplaceKey, 0, ShaderIDProps.AddressDict, addressDict);
            gpuContext.SetInt(dictReplaceKey, ShaderIDProps.AddressIndex, (int)address);
            gpuContext.SetInts(dictReplaceKey, ShaderIDProps.OriginCCoord, new int[3] { oCoord.x, oCoord.y, oCoord.z });
            gpuContext.SetInts(dictReplaceKey, ShaderIDProps.Dimension, new int[3] { dimension.x, dimension.y, dimension.z });

            int skipInc = 1 << depth;
            int chunkInc = mapChunkSize / skipInc;
            int offset = ((offC.x * chunkInc & 0xFF) << 24) | ((offC.y * chunkInc & 0xFF) << 16) | ((offC.z * chunkInc & 0xFF) << 8) | (skipInc & 0xFF);
            gpuContext.SetInt(dictReplaceKey, ShaderIDProps.ChunkIncrement, chunkInc);
            gpuContext.SetInt(dictReplaceKey, ShaderIDProps.StartOffset, offset);

            dictReplaceKey.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int3 numThreadsAxis = (int3)math.ceil((float3)dimension / threadGroupSize);
            gpuContext.Dispatch(dictReplaceKey, 0, numThreadsAxis.x, numThreadsAxis.y, numThreadsAxis.z);
        }

        /// <summary> Shrinks the size of a stored chunk's map by
        /// selectively downsampling the existing stored chunk's map. </summary>
        /// <param name="CCoord">The coordinate of the origin(lowest coordinate) of the chunk in
        /// chunk space; determins which stored chunk to replace.</param>
        /// <param name="depth">The <see cref="TerrainChunk.depth">depth</see> of the new chunk; determines
        /// the new downsampled size of the map. Caller must guarantee that this is no less than the previous size
        /// of the chunk. </param>
        public static void SimplifyChunk(int3 CCoord, int depth) {
            int numPointsAxis = mapChunkSize / (1 << depth);
            int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

            uint address = memorySpace.AllocateMemoryDirect(numPoints, 1);

            //ReplaceAddress(memorySpace.Address, address, CCoord, meshSkipInc);
            SimplifyDataDirect(memorySpace.Storage, memorySpace.Address, address, CCoord, 1 << depth);
            memorySpace.ReleaseMemory(address);
        }

        static void SimplifyDataDirect(ComputeBuffer memory, GraphicsBuffer addressDict, uint address, int3 CCoord, int meshSkipInc) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            int numPointsAxis = mapChunkSize / meshSkipInc;

            gpuContext.SetBuffer(simplifyMap, 0, ShaderIDProps.MemoryBuffer, memory);
            gpuContext.SetBuffer(simplifyMap, 0, ShaderIDProps.ChunkAddressDict, _ChunkAddressDict);
            gpuContext.SetBuffer(simplifyMap, 0, ShaderIDProps.AddressDict, addressDict);
            gpuContext.SetInt(simplifyMap, ShaderIDProps.AddressIndex, (int)address);
            gpuContext.SetInts(simplifyMap, ShaderIDProps.CCoord, new int[3] { CCoord.x, CCoord.y, CCoord.z });
            gpuContext.SetInt(simplifyMap, ShaderIDProps.NumPointsPerAxis, numPointsAxis);

            simplifyMap.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
            int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
            gpuContext.Dispatch(simplifyMap, 0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
        }

        /// <summary> Attaches all resources necessary to sample map information through
        /// the <see cref="Address">lookup constructs</see> managed by
        /// <see cref="GPUMapManager"/> to a compute shader. </summary>
        /// <param name="shader">The compute shader requesting map information. Caller
        /// should ensure this shader is able to recieve the bindings attached here. </param>
        public static void SetDensitySampleData(ComputeShader shader) {
            GraphicsResourceContext gpuContext = GraphicsGeneration;
            if (!initialized)
                return;

            gpuContext.SetBuffer(shader, 0, ShaderIDProps.ChunkAddressDict, _ChunkAddressDict);
            gpuContext.SetBuffer(shader, 0, ShaderIDProps.ChunkInfoBuffer, memorySpace.Storage);
        }

        /// <summary> Attaches all resources necessary to sample map information through
        /// the <see cref="Address">lookup constructs</see> managed by
        /// <see cref="GPUMapManager"/> to a material. </summary>
        /// <param name="material">The material requesting map information. Caller
        /// should ensure this material is able to recieve the bindings attached here. </param>
        public static void SetDensitySampleData(Material material) {
            if (!initialized)
                return;

            material.SetBuffer(ShaderIDProps.ChunkAddressDict, _ChunkAddressDict);
            material.SetBuffer(ShaderIDProps.ChunkInfoBuffer, memorySpace.Storage);
        }

        static void SetGlobalWSCCoordHelper() {
            Shader.SetGlobalFloat(ShaderIDProps.WorldLerpScale, lerpScale);
            Shader.SetGlobalInteger(ShaderIDProps.WorldMapChunkSize, mapChunkSize);
            Shader.SetGlobalInteger(ShaderIDProps.ChunkMapNumChunksPerAxis, numChunksAxis);
        }

        public struct ChunkHandle {
            public GraphicsContextId viewerId;
            public GraphicsFence fence;
            private int _address;
            private int _subscribers;
            public int Address {readonly get => _address; set => _address = value; }
            public int Subscribers {readonly get => _subscribers; set => _subscribers = value; }

            public int Prev {readonly get => _address; set => _address = value; }
            public int Next {readonly get => _subscribers; set => _subscribers = value; }

            public ChunkHandle(int p, int n) {
                _address = p;
                _subscribers = n;
                fence = default;
                viewerId = GraphicsContextId.None;
            }
        }

    }
}
