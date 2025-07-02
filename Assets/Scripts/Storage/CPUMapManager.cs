using System;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Rendering;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Burst;
using UnityEditor;
using TerrainGeneration;
using WorldConfig;

namespace MapStorage {
    /// <summary>A static centralized location for managing and storing all CPU-side map information,
    /// all operations accessing map information, and requesting the information from the GPU. </summary>
    /// <remark>Because the total amount of concurrent Real Chunks is known on game launch, the exact amount of memory
    /// necessary to store all mapdata at any given time may be preallocated and redistributed amongst chunks. This reduces
    /// strain on garbage handler and improves cache coherency of mapData lookups</remark>
    public static class CPUMapManager {
        /// <summary> The <see cref="TerrainChunk"/>s associated with the Map Information for each chunk index. See
        /// <see cref="TerrainChunk"/> for more information. </summary>
        public static TerrainChunk[] _ChunkManagers;
        /// <summary>A sectioned memory array that contains all map data for all real chunks successfully created. The exact offset for
        /// each chunk can be calculated by multiplying the <see cref="HashCoord">chunk index</see> by <see cref="numPoints"/>. </summary>
        public static NativeArray<MapData> SectionedMemory;
        /// <summary> A dictionary that maps the chunk index to the <see cref="ChunkMapInfo"/> for that chunk. 
        /// See <see cref="ChunkMapInfo"/> for more information.  </summary>
        public static NativeArray<ChunkMapInfo> AddressDict;
        /// <summary> The maximum number of real chunks along each axis that can be saved simultaneously with the game's 
        /// current settings. See <see cref="OctreeTerrain.Octree.GetAxisChunksDepth"/> to see how this is calculated. </summary>
        public static int numChunksAxis;
        /// <summary>The total number of real chunks that can be saved simultaneously 
        /// with the game's current settings. Equivalent to <see cref="numChunksAxis"/>^3. / </summary>
        public static int numChunks => numChunksAxis * numChunksAxis * numChunksAxis;
        /// <summary> The Real Integer IsoValue used in all map operations. Equivalent to the 
        /// <see cref="WorldConfig.Quality.Terrain.IsoLevel"/> * 0xFF (maximum density value). </summary>
        public static uint IsoValue;
        private static int numPoints;
        private static int mapChunkSize;
        private static float lerpScale;
        private static bool initialized = false;

        /// <summary> Initializes the CPU Map Manager with the current game settings. This will allocate all necessary memory
        /// and prepare the CPU Map Manager for use. If the CPU Map Manager has already been initialized,
        /// it will first release all previously allocated resources before reinitializing. </summary>
        public static void Initialize() {
            Release();
            WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
            mapChunkSize = rSettings.mapChunkSize;
            lerpScale = rSettings.lerpScale;
            IsoValue = (uint)Math.Round(rSettings.IsoLevel * 255.0f);

            int numPointsAxis = mapChunkSize;
            numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

            numChunksAxis = OctreeTerrain.Octree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);

            _ChunkManagers = new TerrainChunk[numChunks];
            SectionedMemory = new NativeArray<MapData>(numChunks * numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
            AddressDict = new NativeArray<ChunkMapInfo>(numChunks, Allocator.Persistent, NativeArrayOptions.ClearMemory);
            initialized = true;
        }

        /// <summary> Releases all resources allocated by the CPU Map Manager. This operation can
        /// only be performed if the CPU Map Manager has been initialized. This operation will 
        /// save all unsaved chunks to disk, and can be slow resultingly. </summary>
        public static void Release() {
            if (!initialized) return;
            initialized = false;

            SaveAllChunksSync();
            SectionedMemory.Dispose();
            AddressDict.Dispose();
        }

        static void SaveAllChunksSync() {
            for (int i = 0; i < _ChunkManagers.Length; i++) {
                SaveChunk(i);
            }
        }

        /// <summary> Calculates the hash for a given chunk coordinate. The hash is a unique integer
        /// identifier for a chunk that is minimally dense and can be used to access the
        /// corresponding chunk in the <see cref="SectionedMemory"/> and <see cref="AddressDict"/>. </summary>
        /// <param name="CCoord">The Coordinate in Chunk Space of the chunk to be hashed</param>
        /// <returns>The unique minimally dense hash index.</returns>
        public static int HashCoord(int3 CCoord) {
            int3 hashCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            int hash = (hashCoord.x * numChunksAxis * numChunksAxis) + (hashCoord.y * numChunksAxis) + hashCoord.z;
            return hash;
        }

        private static void SaveChunk(int chunkHash) {
            if (!AddressDict[chunkHash].valid) return;
            int3 CCoord = AddressDict[chunkHash].CCoord;

            EntityManager.ReleaseChunkEntities(CCoord);
            if (!AddressDict[chunkHash].isDirty) return;
            ChunkPtr chunk = new ChunkPtr(SectionedMemory, chunkHash * numPoints);
            MapStorage.Chunk.SaveChunkToBinSync(chunk, CCoord);
        }

        /// <summary> Allocates a new chunk in the CPU Map Manager. This will setup handlers
        /// associating the <see cref="HashCoord">chunkIndex</see> and memory section within 
        /// <see cref="SectionedMemory"/> with the specific <see cref="TerrainChunk"/> and <see cref="ChunkMapInfo"/> 
        /// for that chunk. Also releases the previous chunk if it exists and saves it to disk if it is dirty.  </summary>
        /// <remarks> The previous chunk with the same <paramref name="CCoord"/> is guaranteed to be exactly 
        /// <see cref="numChunksAxis"/>+1 chunks away from the new chunk, so it is implied
        /// that it should be safe to deallocate if this chunk is to be loaded. </remarks>
        /// <param name="chunk">The Real Terrain Chunk at <i>CCoord</i></param>
        /// <param name="CCoord">The Coordinate in chunk space of the new chunk to be allocated.</param>
        public static void AllocateChunk(TerrainChunk chunk, int3 CCoord) {
            int chunkHash = HashCoord(CCoord);
            ChunkMapInfo prevChunk = AddressDict[chunkHash];
            ChunkMapInfo newChunk = new ChunkMapInfo {
                CCoord = CCoord,
                valid = true,
                isDirty = false
            };

            //Release Previous Chunk
            if (prevChunk.valid) SaveChunk(chunkHash);
            AddressDict[chunkHash] = newChunk;
            _ChunkManagers[chunkHash] = chunk;
        }

        internal unsafe static NativeArray<MapData> AccessChunk(int3 CCoord) {
            return AccessChunk(HashCoord(CCoord));
        }
        internal unsafe static NativeArray<MapData> AccessChunk(int chunkHash) {
            if (!AddressDict[chunkHash].valid) return default(NativeArray<MapData>);

            //Unsafe slice of code
            MapData* memStart = ((MapData*)NativeArrayUnsafeUtility.GetUnsafePtr(SectionedMemory)) + (chunkHash * numPoints);
            NativeArray<MapData> nativeSlice = NativeArrayUnsafeUtility.ConvertExistingDataToNativeArray<MapData>((void*)memStart, numPoints, Allocator.None);

#if ENABLE_UNITY_COLLECTIONS_CHECKS //Saftey handles don't exist in release mode
            AtomicSafetyHandle handle = AtomicSafetyHandle.Create(); // <-- instead of GetTempMemoryHandle
            AtomicSafetyHandle.UseSecondaryVersion(ref handle);
            NativeArrayUnsafeUtility.SetAtomicSafetyHandle(ref nativeSlice, handle);
#endif
            return nativeSlice;
        }

        /// <summary> Performs a precise raycast on the IsoSurface represented through the underlying <i>density</i> information given by
        /// the callback with the specific <see cref="IsoValue"/> specified. A raycast determines whether a ray intersects with the IsoSurface
        /// and if-so at what point. This function does not use any generated lookup structures and operates on the raw <see cref="MapData"/>
        /// stored in <see cref="SectionedMemory"/>. </summary>
        /// <remarks> This function uses a 3D voxel line drawing algorithm provided here http://www.cse.yorku.ca/~amana/research/grid.pdf and it
        /// is linear in complexity with respect to <i>rayLength</i>. </remarks>
        /// <param name="oGS">The origin of the ray in Grid Space</param>
        /// <param name="rayDir">The direction of the ray.</param>
        /// <param name="rayLength">The maximum length of the ray</param>
        /// <param name="callback">A callback that returns a density value given an integer Grid Space Location of a map entry. </param>
        /// <param name="hit">If the ray intersects the IsoSurface, the position in GridSpace of the first intersection. </param>
        /// <returns>Whether or not the ray intersects the IsoSurface in less than <i>rayLength</i> distance from the rayOrigin(<i>oGS</i>)</returns>
        //Algorithm here -> http://www.cse.yorku.ca/~amana/research/grid.pdf
        public static bool RayCastTerrain(float3 oGS, float3 rayDir, float rayLength, Func<int3, uint> callback, out float3 hit) {
            int3 step = (int3)math.sign(rayDir);
            int3 GCoord = (int3)math.floor(oGS);
            int3 sPlane = math.max(step, 0);
            //If rayDir is negative, then GCoord is the next plane
            //Otherwise GCoord + sPlane is the next plane

            float3 tDelta = 1.0f / math.abs(rayDir); float3 tMax = tDelta;
            tMax.x *= rayDir.x >= 0 ? 1 - (oGS.x - GCoord.x) : (oGS.x - GCoord.x);
            tMax.y *= rayDir.y >= 0 ? 1 - (oGS.y - GCoord.y) : (oGS.y - GCoord.y);
            tMax.z *= rayDir.z >= 0 ? 1 - (oGS.z - GCoord.z) : (oGS.z - GCoord.z);

            uint density = 0;
            uint nDensity = density;
            hit = oGS;
            do {
                if (tMax.x < tMax.y) {
                    if (tMax.x < tMax.z) {
                        tMax.x += tDelta.x;
                        nDensity = GetRayPlaneIntersectionX(ref hit, rayDir, GCoord.x + sPlane.x, callback);
                        GCoord.x += step.x;
                    } else {
                        tMax.z += tDelta.z;
                        nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                        GCoord.z += step.z;
                    }
                } else {
                    if (tMax.y < tMax.z) {
                        tMax.y += tDelta.y;
                        nDensity = GetRayPlaneIntersectionY(ref hit, rayDir, GCoord.y + sPlane.y, callback);
                        GCoord.y += step.y;
                    } else {
                        tMax.z += tDelta.z;
                        nDensity = GetRayPlaneIntersectionZ(ref hit, rayDir, GCoord.z + sPlane.z, callback);
                        GCoord.z += step.z;
                    }
                }

                if (nDensity >= IsoValue) {
                    float t = Mathf.InverseLerp(density, nDensity, IsoValue);
                    hit = oGS * (1 - t) + hit * t;
                    return true;
                }

                density = nDensity;
                oGS = hit;
            } while (Mathf.Min(tMax.x, tMax.y, tMax.z) < rayLength);
            return false;
        }

        private static uint GetRayPlaneIntersectionX(ref float3 rayOrigin, float3 rayDir, int XPlane, Func<int3, uint> SampleMap) {
            float t = (XPlane - rayOrigin.x) / rayDir.x;
            rayOrigin = new(XPlane, rayOrigin.y + t * rayDir.y, rayOrigin.z + t * rayDir.z);
            return BilinearDensity(rayOrigin.y, rayOrigin.z, (int y, int z) => SampleMap(new int3(XPlane, y, z)));
        }

        private static uint GetRayPlaneIntersectionY(ref float3 rayOrigin, float3 rayDir, int YPlane, Func<int3, uint> SampleMap) {
            float t = (YPlane - rayOrigin.y) / rayDir.y;
            rayOrigin = new(rayOrigin.x + t * rayDir.x, YPlane, rayOrigin.z + t * rayDir.z);
            return BilinearDensity(rayOrigin.x, rayOrigin.z, (int x, int z) => SampleMap(new int3(x, YPlane, z)));
        }

        private static uint GetRayPlaneIntersectionZ(ref float3 rayOrigin, float3 rayDir, int ZPlane, Func<int3, uint> SampleMap) {
            float t = (ZPlane - rayOrigin.z) / rayDir.z;
            rayOrigin = new(rayOrigin.x + t * rayDir.x, rayOrigin.y + t * rayDir.y, ZPlane);
            return BilinearDensity(rayOrigin.x, rayOrigin.y, (int x, int y) => SampleMap(new int3(x, y, ZPlane)));
        }

        private static uint BilinearDensity(float x, float y, Func<int, int, uint> SampleMap) {
            int x0 = (int)Math.Floor(x); int x1 = x0 + 1;
            int y0 = (int)Math.Floor(y); int y1 = y0 + 1;

            uint c00 = SampleMap(x0, y0);
            uint c10 = SampleMap(x1, y0);
            uint c01 = SampleMap(x0, y1);
            uint c11 = SampleMap(x1, y1);

            float xd = x - x0;
            float yd = y - y0;

            float c0 = c00 * (1 - xd) + c10 * xd;
            float c1 = c01 * (1 - xd) + c11 * xd;
            return (uint)Math.Round(c0 * (1 - yd) + c1 * yd);
        }

        /// <summary> Provides a method for smooth terrain modification in an area around a point. Provides a callback
        /// providing the mapData of a specific map entry to be modified and the amount to modify for a smooth
        /// terraform. Specifically, in a circular region around <i>tPointGS</i> this amount falls off proportionate
        /// to distance from the center. </summary>
        /// <param name="tPointGS">The center in grid space of the circle that is terraformed</param>
        /// <param name="terraformRadius">The radius in grid space of the circle that is terraformed</param>
        /// <param name="handleTerraform">The callback that provides map information, how much to modify it, and expects 
        /// to be returned the new modified map data corresponding to the original map information. </param>
        public static void Terraform(float3 tPointGS, int terraformRadius, Func<MapData, float, MapData> handleTerraform) {
            int3 tPointGSInt = (int3)math.floor(tPointGS);

            for (int x = -terraformRadius; x <= terraformRadius + 1; x++) {
                for (int y = -terraformRadius; y <= terraformRadius + 1; y++) {
                    for (int z = -terraformRadius; z <= terraformRadius + 1; z++) {
                        int3 GCoord = tPointGSInt + new int3(x, y, z);

                        //Calculate Brush Strength
                        float3 dR = GCoord - tPointGS;
                        float sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                        float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);
                        MapData sample = SampleMap(GCoord);
                        if (sample.IsNull) continue; //Skip if null(invalid)
                        SetMap(handleTerraform(sample, brushStrength), GCoord);
                        TerrainUpdate.AddUpdate(GCoord);
                    }
                }
            }
        }

        /// <summary> Reads back the map information associated with the Chunk Coord(<i>CCoord</i>) from the GPU to the CPU.
        /// The map information should exist at the expected location in the GPU's map memory(<see cref="GPUMapManager._ChunkAddressDict"/>)
        /// and will be read to the location corresponding to the <see cref="HashCoord">chunk index</see> of this chunk coordinate within
        /// <see cref="SectionedMemory"/>. One should first allocate this region through <see cref="AllocateChunk"/> before calling this function. </summary>
        /// <param name="CCoord">The coordiante of the chunk in chunk space to be read back. Used in determining both where the information is read
        /// from in GPU memory and where it is written to in <see cref="SectionedMemory"/>. </param>
        public static void BeginMapReadback(int3 CCoord) { //Need a wrapper class to maintain reference to the native array
            int GPUChunkHash = GPUMapManager.HashCoord(CCoord);
            int CPUChunkHash = HashCoord(CCoord);

            AsyncGPUReadback.Request(GPUMapManager.Address, size: 8, offset: 20 * GPUChunkHash, ret => onChunkAddressRecieved(ret, CPUChunkHash));
        }

        static unsafe void onChunkAddressRecieved(AsyncGPUReadbackRequest request, int chunkHash) {
            if (!initialized) return;
            uint2 memHandle = request.GetData<uint2>()[0];
            ChunkMapInfo destChunk = AddressDict[chunkHash];
            if (!destChunk.valid) return;

            int memAddress = (int)memHandle.x;
            int meshSkipInc = (int)memHandle.y;

            NativeArray<MapData> dest = AccessChunk(chunkHash);
            AsyncGPUReadback.RequestIntoNativeArray(ref dest, GPUMapManager.Storage, size: 4 * numPoints, offset: 4 * memAddress);

            /*Currently broken because safety checks operate on the object level (i.e. SectionedMemory can only be read to one at a time)

            NativeSlice<TerrainChunk.MapData> dest = SectionedMemory.Slice((int)destChunk.address, numPoints);
            AsyncGPUReadback.RequestIntoNativeSlice(ref dest, GPUMapManager.AccessStorage(), size: 4 * numPoints, offset: 4 * memAddress);*/
        }

        /// <summary> Converts from grid space to chunk space using integer mathematics. Chunk Space is a coordinate
        /// system that reserves a unique minimally dense integer coordinate for each Real Chunk. Specifically, gets 
        /// the chunk coordinate of the chunk containing this grid coordinate. </summary>
        /// <remarks> Importantly, this is NOT the rounded coordinate but more akin to a floor. </remarks>
        /// <param name="GCoord">The coordinate in grid space</param>
        /// <returns>The associated coordiante in chunk space </returns>
        public static int3 GSToCS(int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            return (GCoord - MCoord) / mapChunkSize;
        }
        /// <summary> Converts from world space to grid space. Grid space is the global coordinate system that reserves a unique
        /// minimally dense integer coordinate for each map entry in a RealChunk. This is the inverse of <see cref="GSToWS"/>.
        /// Conversion depends primarily on <see cref="lerpScale"/>. </summary>
        /// <param name="WSPos">The position in world space</param>
        /// <returns>The associated exact position in grid space</returns>
        public static float3 WSToGS(float3 WSPos) { return WSPos / lerpScale + mapChunkSize / 2; }
        /// <summary> Converts from grid space to world space. World space is the global coordinate system recognized
        /// by Unity's object and rendering system. This is the inverse of <see cref="WSToGS"/>.
        /// Conversion depends primarily on <see cref="lerpScale"/>.  </summary>
        /// <param name="GSPos">The position in grid space</param>
        /// <returns>The associated exact position in world space</returns>
        public static float3 GSToWS(float3 GSPos) { return (GSPos - mapChunkSize / 2) * lerpScale; }

        /// <summary> Assigns the mapData of a given GCoord if it is currently being managed by 
        /// <see cref="CPUMapManager"/>. Also notifies the corresponding chunk to reflect the 
        /// change visually and marks the chunk as dirty when storing to disk. </summary>
        /// <param name="data">The <see cref="MapData"/> that is written(assigned) </param>
        /// <param name="GCoord">The coordinate in grid space of the entry being assinged to</param>
        public static void SetMap(MapData data, int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

            int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            //Not available(currently readingback) || Out of Bounds
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return;

            //Update Handles
            SectionedMemory[CIndex * numPoints + PIndex] = data;
            var chunkMapInfo = AddressDict[CIndex];
            chunkMapInfo.isDirty = true;
            AddressDict[CIndex] = chunkMapInfo;
            _ChunkManagers[CIndex].ReflectChunk();
        }

        /// <summary> Retrieves the <see cref="MapData"/> associated with a grid coordinate if
        /// it is currently being managed by <see cref="CPUMapManager"/>.  </summary>
        /// <param name="GCoord">The coordinate in grid space of the <see cref="MapData"/> which is returned</param>
        /// <returns>The <see cref="MapData"/> at the given grid coordinate if tracked by <see cref="CPUMapManager"/>, 
        /// otherwise returns a <see cref="MapData"/> instance with material = 0x7FFF. </returns>
        public static MapData SampleMap(int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

            int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            //Not available(currently readingback) || Out of Bounds
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return new MapData { data = 0xFFFFFFFF };

            return SectionedMemory[CIndex * numPoints + PIndex];
        }

        /// <summary> Retrieves the <see cref="MapData.viscosity"/>/<see cref="MapData.SolidDensity"/> value of the 
        /// MapData associated with the given grid coordinate if it is currently being managed by <see cref="CPUMapManager"/>,
        /// otherwise returns 0xFF. </summary>
        /// <param name="GCoord">The coordinate in grid space of the MapData being queried. </param>
        /// <returns>The <see cref="MapData.viscosity"/>/<see cref="MapData.SolidDensity"/> of the associated <see cref="MapData"/>.</returns>
        public static int SampleTerrain(int3 GCoord) {
            MapData mapData = SampleMap(GCoord);
            return mapData.viscosity;
        }

        /// <summary> The meta-data coordinating access, saving and
        /// readback of a chunk's map information. Associated via
        /// the chunk index within <see cref="AddressDict"/> to a chunk's
        /// map data </summary>
        public struct ChunkMapInfo {
            /// <summary> The coordinate in chunk space of the chunk being stored.
            /// Undefined if <see cref="valid"/> is false </summary>
            public int3 CCoord;
            /// <summary> Whether the data region within <see cref="SectionedMemory"/> associated with 
            /// this object has contains real chunk data. This will be set as long as one chunk which 
            /// <see cref="HashCoord">hashes</see> to this object calls <see cref="AllocateChunk"/> </summary>
            public bool valid;
            /// <summary>Whether this chunk will be saved to disk when unloaded from memory. Whether 
            /// any entry within its chunk's map information has been modified. </summary>
            public bool isDirty;
        }
        
        /// <summary> A wrapper structure containing a specific chunk's map data 
        /// that will be stored to disk </summary>
        public struct ChunkPtr {
            /// <summary> A native array containing the chunk's map information at
            /// the offset specified by <see cref="offset"/> </summary>
            public NativeArray<MapData> data;
            /// <summary> The offset within <see cref="data"/> where the map
            /// information of the chunk being stored starts. </summary>
            public int offset;
            /// <summary> A simple constructor creating a ChunkPtr wrapper </summary>
            /// <param name="data">An native array containing the chunk's map information at <see cref="offset"/></param>
            /// <param name="offset">The offset within <see cref="data"/> where the chunk's map information begins</param>
            public ChunkPtr(NativeArray<MapData> data, int offset) {
                this.data = data;
                this.offset = offset;
            }
        }

        /// <summary> A wrapper structure containing all the unamanged handlers necessary
        /// to perform map lookups and modifications in an unamanged context. </summary>
        public unsafe struct MapContext {
            /// <summary> A pointer to the beginning of <see cref="SectionedMemory"/>, 
            /// which contains all map information actively tracked on the CPU and is interactive. </summary>
            [NativeDisableUnsafePtrRestriction]
            public MapData* MapData;
            /// <summary> A pointer to the beginning of <see cref="AddressDict"/> which maps from 
            /// a <see cref="HashCoord">chunk index</see> to <see cref="ChunkMapInfo"/> for that chunk. </summary>
            [NativeDisableUnsafePtrRestriction]
            public ChunkMapInfo* AddressDict;
            /// <summary> See <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> </summary>
            public int mapChunkSize;
            /// <summary> See <see cref="CPUMapManager.numChunksAxis"/> </summary>
            public int numChunksAxis;
            /// <summary> See <see cref="CPUMapManager.IsoValue"/> </summary>
            public int IsoValue;
        }

        /// <summary> Same as <see cref="SampleMap(int3)"/> except in an unmanaged context.
        /// The context information normally sourced by <see cref="CPUMapManager"/> must
        /// instead be passed in through a <see cref="MapContext"/>.  </summary>
        /// <param name="GCoord">The coordinate in grid space of the point being retrieved</param>
        /// <param name="context">The map context necessary to perform this operation. See <see cref="MapContext"/> for more info. </param>
        /// <returns>The <see cref="MapData"/> associated with the grid coordinate if it is currently tracked by <see cref="CPUMapManager"/>.</returns>
        [BurstCompile]
        public unsafe static MapData SampleMap(in int3 GCoord, in MapContext context) {
            int3 MCoord = ((GCoord % context.mapChunkSize) + context.mapChunkSize) % context.mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / context.mapChunkSize;
            int3 HCoord = ((CCoord % context.numChunksAxis) + context.numChunksAxis) % context.numChunksAxis;

            int PIndex = MCoord.x * context.mapChunkSize * context.mapChunkSize + MCoord.y * context.mapChunkSize + MCoord.z;
            int CIndex = HCoord.x * context.numChunksAxis * context.numChunksAxis + HCoord.y * context.numChunksAxis + HCoord.z;
            int numPoints = context.mapChunkSize * context.mapChunkSize * context.mapChunkSize;
            ChunkMapInfo mapInfo = context.AddressDict[CIndex];
            if (!mapInfo.valid) return new MapData { data = 0xFFFFFFFF };//Not available(currently readingback)
            if (math.any(mapInfo.CCoord != CCoord)) return new MapData { data = 0xFFFFFFFF }; //Out of bounds

            return context.MapData[CIndex * numPoints + PIndex];
        }

        /// <summary> Same as <see cref="SampleTerrain(int3)"/> except in an unmanaged context.
        /// The context information normally sourced by <see cref="CPUMapManager"/> must
        /// instead be passed in through a <see cref="MapContext"/>.  </summary>
        /// <param name="GCoord">The coordinate in grid space of the point whose SolidDensity is being retrieved</param>
        /// <param name="context">The map context necessary to perform this operation. See <see cref="MapContext"/> for more info. </param>
        /// <returns>The <see cref="MapData.SolidDensity"/> associated with the grid coordinate if it is currently tracked by <see cref="CPUMapManager"/>.</returns>
        [BurstCompile]
        public static int SampleTerrain(in int3 GCoord, in MapContext context) {
            MapData mapData = SampleMap(GCoord, context);
            return mapData.viscosity;
        }

#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(MapData))]
        internal class MapDataDrawer : PropertyDrawer {
            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
                SerializedProperty dataProp = property.FindPropertyRelative("data");
                uint data = dataProp.uintValue;

                //bool isDirty = (data & 0x80000000) != 0;
                int[] values = new int[3]{
                (int)((data >> 16) & 0x7FFF),
                (int)((data >> 8) & 0xFF),
                (int)(data & 0xFF)
            };
                bool isDirty = (data & 0x80000000) != 0;

                Rect rect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

                EditorGUI.MultiIntField(rect, new GUIContent[] { new("Material"), new("Viscosity"), new("Density") }, values);
                rect.y += EditorGUIUtility.singleLineHeight;
                isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
                rect.y += EditorGUIUtility.singleLineHeight;
                //isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
                //rect.y += EditorGUIUtility.singleLineHeight;

                //data = (isDirty ? data | 0x80000000 : data & 0x7FFFFFFF);
                data = (data & 0x8000FFFF) | ((uint)values[0] << 16);
                data = (data & 0xFFFF00FF) | (((uint)values[1] & 0xFF) << 8);
                data = (data & 0xFFFFFF00) | ((uint)values[2] & 0xFF);
                data = isDirty ? data | 0x80000000 : data & 0x7FFFFFFF;

                dataProp.uintValue = data;
            }

            // Override this method to make space for the custom fields
            public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
                return EditorGUIUtility.singleLineHeight * 2;
            }
        }
#endif
    }

    /// <summary> Akin to a "voxel" or "block", The 'map information' of a single point in the world. 
    /// This is the minimal atmoic amount of information necessary to represent any information about
    /// the terrain/world in-game. In particular, describes the identity and terrain shape of a 
    /// unique integer coordinate in grid space. </summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct MapData {
        /// <summary> The raw 4 byte bitmap containing the natural packed representation of a <see cref="MapData"/> entry. </summary>
        [HideInInspector] public uint data;
        /// <summary>Whether the <see cref="MapData"/> refers to a special <i>Null</i> entry which is often
        /// returned when an invalid/no entry should be returned </summary>
        public bool IsNull => data == 0xFFFFFFFF;
        /// <summary> Whether the map entry has been modified while on the CPU. Whether the information
        /// cannot be recalculated by the generation pipeline and needs to be saved. </summary>
        /// <remarks> Represented by the highest bit in <see cref="data"/> </remarks>
        public bool isDirty {
            readonly get => (data & 0x80000000) != 0;
            //Should not edit, but some functions need to
            set => data = value ? data | 0x80000000 : data & 0x7FFFFFFF;
        }

        /// <summary> How much total material is contained in the location of 
        /// this <see cref="MapData"/>. Affects the shape of the terrain.  </summary>
        /// <remarks> Represented by the lowest byte in <see cref="data"/></remarks>
        public int density {
            readonly get => (int)data & 0xFF;
            set {
                data = (data & 0xFFFF00FF) | ((uint)math.min(viscosity, value & 0xFF) << 8);
                data = (data & 0xFFFFFF00) | ((uint)value & 0xFF) | 0x80000000;
            }
        }

        /// <summary> How much total solid material is contained in the location 
        /// of this <see cref="MapData"/>. Directly affects the shape of the solid terrain.  </summary>
        /// /// <remarks> Represented by the second lowest byte in <see cref="data"/></remarks>
        public int viscosity {
            readonly get => (int)(data >> 8) & 0xFF;
            set {
                data = (data & 0xFFFFFF00) | ((uint)math.max(density, value & 0xFF));
                data = (data & 0xFFFF00FF) | (((uint)value & 0xFF) << 8) | 0x80000000;
            }
        }

        /// <summary> The identity of this <see cref="MapData"/>. The index within the <see cref="Config.GenerationSettings.Materials"/>
        /// registry of the <see cref="WorldConfig.Generation.Material"/> responsible for controling the apperance 
        /// and behavior of this <see cref="MapData"/>  </summary>
        public int material {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16) | 0x80000000;
        }

        /// <summary> How much total solid material is contained in the location 
        /// of this <see cref="MapData"/>. A clearer alias for <see cref="viscosity"/> </summary>
        public readonly int SolidDensity {
            get => viscosity;
        }
        
        /// <summary> How much liquid material is contained in the location of this <see cref="MapData"/>.
        /// Directly effects the shape of liquid surfaces. Equivalent to <i>(density - viscosity)</i> </summary>
        public readonly int LiquidDensity {
            get => density - viscosity;
        }

        /// <summary> Whether this <see cref="MapData"/> represents a point that is underground. Whether
        /// <see cref="SolidDensity"/> is greater than <see cref="CPUMapManager.IsoValue"/>. </summary>
        public readonly bool IsSolid {
            get => SolidDensity >= CPUMapManager.IsoValue;
        }

        /// <summary> Whether this <see cref="MapData"/> represents a point that is underwater. Whether
        /// <see cref="LiquidDensity"/> is greater than <see cref="CPUMapManager.IsoValue"/>. </summary>
        public readonly bool IsLiquid {
            get => LiquidDensity >= CPUMapManager.IsoValue;
        }

        /// <summary> Whether this <see cref="MapData"/> represents a point that is neither underwater nor underground.
        /// Whether both <see cref="IsSolid"/> and <see cref="IsLiquid"/> is false. </summary>
        public readonly bool IsGaseous {
            get => !IsSolid && !IsLiquid;
        }

        /// <summary>A backdoor method to modify the material for serialization 
        /// without marking the entry as dirty(<see cref="isDirty"/>). </summary>
        public int _material 
        {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16);
        }
    }
}
