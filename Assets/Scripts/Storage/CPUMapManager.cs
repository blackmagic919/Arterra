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
using System.Collections.Generic;
using System.Linq;

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
        /// <summary> Optional meta-data map points in a chunk may contain. Certain
        /// map entries which require special meta data(e.g. chests/containers) should store
        /// their specific information in this dictionary, accessible via the linearly encoded
        /// sub-chunk index(MIndex) of the entry they're accessing. </summary>
        public static Dictionary<uint, object>[] MapMetaData;
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
            MapMetaData = new Dictionary<uint, object>[numChunks];
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
            MapMetaData = null;
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
            AddressDict[chunkHash].Dispose();
            int3 CCoord = AddressDict[chunkHash].CCoord;

            EntityManager.ReleaseChunkEntities(CCoord);
            if (!AddressDict[chunkHash].isDirty) return;
            ChunkPtr chunk = new ChunkPtr(MapMetaData[chunkHash],
                SectionedMemory, chunkHash * numPoints);
            Chunk.SaveChunkToBinAsync(chunk, CCoord);
        }

        /// <summary> Allocates a new chunk in the CPU Map Manager. This will setup handlers
        /// associating the <see cref="HashCoord">chunkIndex</see> and memory section within 
        /// <see cref="SectionedMemory"/> with the specific <see cref="TerrainChunk"/> and <see cref="ChunkMapInfo"/> 
        /// for that chunk. Also releases the previous chunk if it exists and saves it to disk if it is dirty.  </summary>
        /// <remarks> The previous chunk with the same <paramref name="CCoord"/> is guaranteed to be exactly 
        /// <see cref="numChunksAxis"/>+1 chunks away from the new chunk, so it is implied
        /// that it should be safe to deallocate if this chunk is to be loaded. </remarks>
        /// <param name="chunk">The Real Terrain Chunk at <i>CCoord</i></param>
        /// <param name="chunkMeta"> The optional map-entry specific meta data in its flattened storage form</param>
        /// <param name="CCoord">The Coordinate in chunk space of the new chunk to be allocated.</param>
        public static void AllocateChunk(
            TerrainChunk chunk,
            KeyValuePair<uint, object>[] chunkMeta,
            int3 CCoord
        ) {
            int chunkHash = HashCoord(CCoord);
            ChunkMapInfo prevChunk = AddressDict[chunkHash];
            ChunkMapInfo newChunk = new ChunkMapInfo(CCoord);

            //Release Previous Chunk
            if (prevChunk.valid) SaveChunk(chunkHash);
            MapMetaData[chunkHash] = chunkMeta?.ToDictionary(
                pair => pair.Key, 
                pair => pair.Value
            );

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

        /// <summary> Obtains the metadata of type <i>TMeta</i> at the location. If the metadata at this location
        /// does not exist or is not of type <i>TMeta</i>, this call will discard the previous information
        /// and return a new default <i>TMeta</i> object which is now stored at this location. </summary>
        /// <typeparam name="TMeta">The type of object to obtain; will delete any object not of 
        /// the requested type and replace it with a new default instance.</typeparam>
        /// <param name="GCoord">The coordinate in grid space of the map entry whose meta data is being queried</param>
        /// <param name="GetDefault">The callback to acquire a default instance of the object being stored if it does not
        /// currently exist at the requested location </param>
        /// <param name="ret">The meta data obtained at this location.</param>
        /// <returns>Whether or not the requested metaData could be obtained. If the metaData cannot be obtained
        /// it means the system is not capable of tracking it and the caller should not attempt to further use/create
        ///  the meta data.</returns>
        public static bool GetOrCreateMapMeta<TMeta>(int3 GCoord, Func<TMeta> GetDefault, out TMeta ret){
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            ret = default;

            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);

            MapMetaData[CIndex] ??= new Dictionary<uint, object>();
            if (!MapMetaData[CIndex].TryGetValue(PIndex, out object value)) {
                ret = GetDefault();
                MapMetaData[CIndex][PIndex] = ret;
                return true;
            }

            if (value is TMeta tVal)
                ret = tVal;
            else {
                ret = GetDefault();
                MapMetaData[CIndex][PIndex] = ret;
            } return true;
        }
        
        /// <summary>Obtains the metadata of type <i>TMeta</i> at the location only if there exists
        /// metadata of the requested type. Otherwise fails. Unlike <see cref="GetOrCreateMapMeta"/>,
        /// this operation will not create/modify existing metadata</summary>
        /// <typeparam name="TMeta">The type of object to obtain</typeparam>
        /// <param name="GCoord">The coordinate in grid space of the map entry whose meta data is being queried</param>
        /// <param name="ret">The meta data obtained at this location.</param>
        /// <returns>Whether or not there existed metaData of the requested type at the location.</returns>
        public static bool TryGetExistingMapMeta<TMeta>(int3 GCoord, out TMeta ret) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            ret = default;

            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);

            MapMetaData[CIndex] ??= new Dictionary<uint, object>();
            if (!MapMetaData[CIndex].TryGetValue(PIndex, out object value))
                return false;
            if (value is TMeta tVal)
                ret = tVal;
            else return false;
            return true;
        }

        /// <summary>Sets the metadata for a certain location or clears it. </summary>
        /// <typeparam name="TMeta">The type of object to set</typeparam>
        /// <param name="GCoord">The coordinate in grid space of the map entry whose meta data is being set</param>
        /// <param name="value">The meta data to set to this location. If this value is null, any existing
        /// metadata at this location will be removed.</param>
        /// <returns>If setting, whether the value was successfully set. If removing, whether any value
        /// was successfully removed</returns>
        public static bool SetExistingMapMeta<TMeta>(int3 GCoord, TMeta value) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);

            MapMetaData[CIndex] ??= new Dictionary<uint, object>();
            if (value == null) return MapMetaData[CIndex].Remove(PIndex);
            return MapMetaData[CIndex].TryAdd(PIndex, value);
        }

        /// <summary> Removes the meta data at the map entry at the specified 
        /// location if it exists. </summary>
        /// <param name="GCoord">The coordinate in grid space of the map entry whose 
        /// meta data is being removed.</param>
        /// <returns>Whether or not any object was removed. Regardless of the output, the caller is 
        /// guaranteed that there does not exist meta data  associated
        /// with this location anymore. </returns>
        public static bool ClearMapMetaData(int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

            int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
            ChunkMapInfo mapInfo = AddressDict[CIndex];
            if (!mapInfo.valid || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);

            MapMetaData[CIndex] ??= new Dictionary<uint, object>();
            if (!MapMetaData[CIndex].TryGetValue(PIndex, out object value))
                return false;

            return MapMetaData[CIndex].Remove(PIndex);
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
        /// <param name="handleTerraform">The callback that is given the map coordinate, how much to modify it, and
        /// is responsible for modifying the terrian and returning whether or not it was modified </param>
        /// <param name="handlePreTerraform">The optional callback that will be called for every map coordinate before
        /// any calls to <i>handleTerraform</i> is ever made, returning whether or not to terminate the terraform operation. </param>
        public static void Terraform(
            float3 tPointGS,
            int terraformRadius,
            Func<int3, float, bool> handleTerraform,
            Func<int3, bool> handlePreTerraform = null
        ) {
            int3 tPointGSInt = (int3)math.round(tPointGS);

            if (handlePreTerraform != null) {
                bool terminated = false;
                Utils.CustomUtility.OrderedLoop(terraformRadius, GCoord => {
                    if (terminated) return;
                    GCoord += tPointGSInt;
                    int3 dR = GCoord - tPointGSInt;
                    int sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius + 1, sqrDistWS);
                    terminated = handlePreTerraform.Invoke(GCoord);
                });
                if (terminated) return;
            }

            Utils.CustomUtility.OrderedLoop(terraformRadius, GCoord => {
                GCoord += tPointGSInt;
                int3 dR = GCoord - tPointGSInt;
                int sqrDistWS = dR.x * dR.x + dR.y * dR.y + dR.z * dR.z;
                float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius + 1, sqrDistWS);
                if (handleTerraform(GCoord, brushStrength))
                    TerrainUpdate.AddUpdate(GCoord);
            });
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
            ReflectNeighbors(CCoord, MCoord == 0);
        }

        private static void ReflectNeighbors(int3 CCoord, bool3 IsBordering) {
            for (int x = IsBordering.x ? -1 : 0 ; x <= 0; x++) {
            for (int y = IsBordering.y ? -1 : 0; y <= 0; y++) {
            for (int z = IsBordering.z ? -1 : 0; z <= 0; z++) {
                int3 NeighborCC = CCoord + new int3(x,y,z);
                int3 HCoord = ((NeighborCC % numChunksAxis) + numChunksAxis) % numChunksAxis;
                int CIndex = HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z;
                ChunkMapInfo mapInfo = AddressDict[CIndex];
                //Not available(currently readingback) || Out of Bounds
                if (!mapInfo.valid || math.any(mapInfo.CCoord != NeighborCC))
                    return;
                _ChunkManagers[CIndex].ReflectChunk();
            }}};
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

        /// <summary> The meta-data coordinating map-metadata storage, access,
        /// saving and readback of a chunk's map information. Associated via
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
            /// <summary> Initializes a new <see cref="ChunkMapInfo"/> for the specific
            /// chunk coordinate of the chunk. </summary>
            /// <param name="CCoord">The coordinate in chunk space of the chunk being managed</param>
            public ChunkMapInfo(int3 CCoord) {
                this.CCoord = CCoord;
                valid = true;
                isDirty = false;
            }

            /// <summary> Invalidates the chunk map data indicating to 
            /// processes that it should no longer be modified </summary>
            public void Dispose() => valid = false;
        }

        /// <summary> A wrapper structure containing a specific chunk's map data 
        /// that will be stored to disk </summary>
        public struct ChunkPtr {
            /// <summary> A native array containing the chunk's map information at
            /// the offset specified by <see cref="offset"/> </summary>
            public NativeArray<MapData> data;
            /// <summary> The array containing all map entry meta-data and the index
            /// identifying them as it will be represented in storage.
            /// See <see cref="MapMetaData"/> for more information. </summary>
            public KeyValuePair<uint, object>[] mapMeta;
            /// <summary> The offset within <see cref="data"/> where the map
            /// information of the chunk being stored starts. </summary>
            public int offset;
            /// <summary> A simple constructor creating a ChunkPtr wrapper </summary>
            /// /// <param name="metaData">The optional map-entry meta data to be stored. See 
            /// <see cref="MapMetaData"/> for more information</param>
            /// <param name="data">An native array containing the chunk's map information at <see cref="offset"/></param>
            /// <param name="offset">The offset within <see cref="data"/> where the chunk's map information begins</param>
            public ChunkPtr(Dictionary<uint, object> metaData, NativeArray<MapData> data, int offset) {
                this.data = data;
                this.offset = offset;
                mapMeta = metaData?.ToArray();
            }

            public ChunkPtr Copy(int Length)
            {
                ChunkPtr ret = new ChunkPtr
                {
                    mapMeta = mapMeta?.ToArray(),
                    data = new NativeArray<MapData>(Length, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    offset = 0
                };
                NativeArray<MapData>.Copy(data, offset, ret.data, 0, Length);
                return ret;
            }

            public void Dispose()
            {
                if (data.IsCreated)
                {
                    data.Dispose();
                }
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
