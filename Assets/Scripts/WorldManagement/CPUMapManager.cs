using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
#if UNITY_EDITOR
using UnityEditor;
using Arterra.Editor;
#endif
using System.Collections.Generic;
using System.Collections.Concurrent;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using Arterra.Utils;
using Arterra.Data.Entity;
using Arterra.Core.Network;

namespace Arterra.Core.Storage {
    /// <summary>A static centralized location for managing and storing all CPU-side map information,
    /// all operations accessing map information, and requesting the information from the GPU. </summary>
    /// <remarks>Chunk records are shared across player regions. Lifecycle changes run on Unity's main
    /// thread; background readers must finish before their chunk's final subscription is released.</remarks>
    public static class CPUMapManager {
        /// <summary> A dictionary that maps the chunk index to the <see cref="ChunkMapInfo"/> for that chunk. 
        /// This dictionary should be consistent and readonly in multithreaded environments. 
        /// See <see cref="ChunkMapInfo"/> for more information.  </summary>
        public static readonly ConcurrentDictionary<int3, ChunkMapInfo> ChunkData = new();
        /// <summary> Flags associated with the chunk. Unlike <see cref="ChunkData"/>. Primarily
        /// whether or not the chunk is dirty, meaning whether this chunk will be saved to disk 
        /// when unloaded from memory and whether any entry within its chunk's map 
        /// information has been modified. </summary>
        /// <remarks>This information is volatile and may be modified in a 
        /// multi-threaded environment and should not be expected to be consistent.</remarks>
        public static SharedLinkedList<ChunkMapInfo.Flags> ChunkFlags;
        /// <summary> The maximum number of real chunks along each axis that can be saved simultaneously with the game's 
        /// current settings. See <see cref="OctreeTerrain.BalancedOctree.GetAxisChunksDepth"/> to see how this is calculated. </summary>
        public static int numChunksAxis;
        /// <summary>The total number of real chunks that can be saved simultaneously 
        /// with the game's current settings. Equivalent to <see cref="numChunksAxis"/>^3. / </summary>
        public static int numChunks => numChunksAxis * numChunksAxis * numChunksAxis;
        /// <summary> The Real Integer IsoValue used in all map operations. Equivalent to the 
        /// <see cref="Configuration.Quality.Terrain.IsoLevel"/> * 0xFF (maximum density value). </summary>
        public static uint IsoValue;
        private static int numPoints;
        private static int mapChunkSize;
        private static float lerpScale;
        private static bool initialized = false;
        public static MapChangeAccumulator MapChanges { get; private set; }
        private static ArterraRuntime.IndirectUpdate mapChangeUpdate;
        private static readonly HashSet<Task> pendingReleases = new();
        private static event Action<int3> OnCreateChunk;
        private static event Action<int3> OnReleaseChunk;

        /// <summary> Initializes the CPU Map Manager with the current game settings. This will allocate all necessary memory
        /// and prepare the CPU Map Manager for use. If the CPU Map Manager has already been initialized,
        /// it will first release all previously allocated resources before reinitializing. </summary>
        public static void Initialize() {
            Release();
            Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
            mapChunkSize = rSettings.mapChunkSize;
            lerpScale = rSettings.lerpScale;
            IsoValue = (uint)Math.Round(rSettings.IsoLevel * 255.0f);

            int numPointsAxis = mapChunkSize;
            numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
            numChunksAxis = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
            
            ChunkFlags = new SharedLinkedList<ChunkMapInfo.Flags>(numChunks);
            MapChanges = new MapChangeAccumulator();
            mapChangeUpdate = new ArterraRuntime.IndirectUpdate(_ => MapChanges.FlushChangesTick());
            ArterraRuntime.MainLateUpdateTasks.Enqueue(mapChangeUpdate);
            initialized = true;
        }

        /// <summary>
        /// Synchronous teardown is allowed only after all loads finish. Use ReleaseAsync while
        /// GPU work has been cancelled and its final readbacks drained.
        /// </summary>
        public static void Release() {
            if (pendingReleases.Count != 0 || ChunkData.Values.Any(info => info.Generation.Ready != null && !info.Generation.Ready.IsCompleted))
                throw new InvalidOperationException("Await CPUMapManager.ReleaseAsync before shutting down graphics.");
            ReleaseAsync().GetAwaiter().GetResult();
        }

        /// <summary>Stop accepting chunks and cancel every pending load before releasing GPU pipelines.</summary>
        public static void StopGeneration() {
            initialized = false;
            if (mapChangeUpdate != null) mapChangeUpdate.Active = false;
            foreach (var chunk in ChunkData.Values) chunk.Generation.Cancel();
        }

        /// <summary>Force-detach chunks after generation cancellation and the final GPU drain.</summary>
        public static async Task ReleaseAsync() {
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: canceling generation.");
            StopGeneration();
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: completing terrain jobs.");
            TerrainUpdate.CompleteJobs();
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: completing entity jobs.");
            EntityManager.CompleteJobs();
            MapChanges?.Clear();
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: jobs complete; releasing chunk records.");
            var records = ChunkData.Values.ToArray();
            foreach (ChunkMapInfo info in records) 
                info.Generation.Subscribers.Clear();
            Task[] saves = pendingReleases.ToArray();
            Task[] releases = records.Select(info => ReleaseChunkIfUnusedAsync(info)).ToArray();
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: flushing deferred entity handlers.");
            EntityManager.FlushPendingHandlers();
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: awaiting {releases.Length} chunk releases and {saves.Length} pending releases/saves.");
            await Task.WhenAll(releases.Concat(saves));
            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Shutdown] CPU map: chunk releases and saves complete.");
        }

        /// <summary>Modulo hash for a bounded region; global storage uses full chunk coordinates.</summary>
        public static int HashCoord(int3 CCoord) {
            int3 hashCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
            return (hashCoord.x * numChunksAxis + hashCoord.y) * numChunksAxis + hashCoord.z;
        }

        /// <summary>Get or create the shared CPU allocation before starting generation.</summary>
        public static ChunkMapInfo GetOrCreateChunkInfo(int3 CCoord) {
            if (!initialized) throw new InvalidOperationException("CPU map storage is not initialized.");
            if (ChunkData.TryGetValue(CCoord, out ChunkMapInfo existing)) return existing;

            uint flagIndex = ChunkFlags.Enqueue(ChunkMapInfo.Flags.Clean);
            var chunk = new ChunkMapInfo(CCoord, (int)flagIndex, numPoints);
            if (ChunkData.TryAdd(CCoord, chunk)) {
                MapChanges.AddChunk(CCoord);
                OnCreateChunk?.Invoke(CCoord);
                return chunk;
            }

            chunk.Dispose();
            ChunkFlags.Remove(flagIndex);
            return ChunkData[CCoord];
        }

        /// <summary>Cancel and detach immediately; only saving and canceled-load cleanup remain asynchronous.</summary>
        internal static async Task ReleaseChunkIfUnusedAsync(ChunkMapInfo chunk) {
            if (chunk.Generation.Subscribers.Count != 0 ||
                !ChunkData.TryGetValue(chunk.CCoord, out ChunkMapInfo current) ||
                !ReferenceEquals(current.Generation, chunk.Generation)) return;

            var source = chunk.Generation.Source;
            Task loading = chunk.Generation.Loading ?? Task.CompletedTask;
            ChunkMapInfo offloaded = OffloadChunkData(chunk.CCoord);
            Task release = ReleaseOffloadedAsync(offloaded, source, loading);
            pendingReleases.Add(release);
            try { await release; }
            finally { pendingReleases.Remove(release); }

            static async Task ReleaseOffloadedAsync(ChunkMapInfo chunk, ChunkSource source, Task loading) {
                Task writes = Task.WhenAll(chunk.PendingWrites); Task save;

                try { save = source != null ? source.ReleaseAsync(chunk) : Task.CompletedTask; }
                catch (Exception exception) { save = Task.FromException(exception); }
                try {
                    await Task.WhenAll(save, loading, writes);
                } catch (OperationCanceledException) 
                when (writes.IsCanceled && save.IsCompletedSuccessfully && loading.IsCompletedSuccessfully) {
                    // Canceled readbacks complete only after the GPU has stopped writing.
                } finally { chunk.Dispose(); }
            }
        }

        /// <summary>Detach the CPU allocation without freeing it. The caller owns and must dispose the result.</summary>
        public static ChunkMapInfo OffloadChunkData(int3 coordinate) {
            if (!ChunkData.TryGetValue(coordinate, out ChunkMapInfo chunk)) return default;
            if (chunk.Generation.Subscribers.Count != 0)
                throw new InvalidOperationException("Cannot offload a chunk with subscribers.");
            chunk.Generation.Cancel();
            // GPU lifetime remains on the main thread; only CPU data travels to the writer.
            chunk.Generation.Entities?.Release(coordinate);
            chunk.Generation.Pipeline?.Release();
            ChunkData.TryRemove(coordinate, out _);
            MapChanges?.RemoveChunk(coordinate);
            OnReleaseChunk?.Invoke(coordinate);
            var flags = ChunkFlags[(uint)chunk.FlagIndex];
            ChunkFlags.Remove((uint)chunk.FlagIndex);
            ChunkMapInfo offloaded = chunk.Offload();
            offloaded.Generation.OffloadFlags = flags;
            return offloaded;
        }

        public static bool TryGetChunkInfo(int3 CCoord, out ChunkMapInfo info) => ChunkData.TryGetValue(CCoord, out info);

        /// <summary> Converts from grid space to chunk space hash and map space hash.
        /// See <see cref="GSToCS"/> for more information on chunk space.
        /// The map space hash is the offset of the map entry within the chunk. </summary>
        /// <param name="GCoord">The coordinate in grid space</param>
        /// <returns>Two integers, x: the chunk space has, y: the map space hash. </returns>
        public static int2 GSToHashes(int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int3 HCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;

            return new int2(
                HCoord.x * numChunksAxis * numChunksAxis + HCoord.y * numChunksAxis + HCoord.z,
                MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z
            );
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
        /// <summary> Converts a world space distance to grid space distance. Does not apply shifting like <see cref="WSToGS"/> </summary>
        /// <param name="WSPos">The distance in world space</param>
        /// <returns>The associated distance in grid space</returns>
        public static float3 WSToGSScale(float3 WSPos) { return WSPos / lerpScale; }

        /// <summary> Assigns the mapData of a given GCoord if it is currently being managed by 
        /// <see cref="CPUMapManager"/>. Also notifies the corresponding chunk to reflect the 
        /// change visually and marks the chunk as dirty when storing to disk. </summary>
        /// <param name="data">The <see cref="MapData"/> that is written(assigned) </param>
        /// <param name="GCoord">The coordinate in grid space of the entry being assinged to</param>
        /// <param name="PropogateUpdates">Whether or not to notify entries in the map of updation. 
        /// This should always be true unless it can cause infinite cascading updates </param>
        public static bool SetMap(MapData data, int3 GCoord, bool PropogateUpdates = true) {
            if (!NetworkManager.IsActingServer) return false;
            return WriteMap(data, GCoord, PropogateUpdates);
        }

        // ClientSource has already verified that these updates came from the connected server.
        internal static bool ApplyMapUpdate(MapData data, int3 GCoord) => WriteMap(data, GCoord, false);

        private static bool WriteMap(MapData data, int3 GCoord, bool PropogateUpdates) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return false;
            //Not available(currently readingback) || Out of Bounds
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord)) return false;

            //This write operation is thread save because mapData is 32-bit 
            //and cannot be internally corrupted by any write operation
            mapInfo.ChunkData[mapInfo.Offset + PIndex] = data;
            ChunkFlags[(uint)mapInfo.FlagIndex] = ChunkMapInfo.Flags.Dirty;
            MapChanges.TrackChange(CCoord, (uint)PIndex, data);
            if (!PropogateUpdates) return true;
            TerrainUpdate.AddUpdate(GCoord);
            for (int i = 0; i < 6; i++) {
                TerrainUpdate.AddUpdate(GCoord + Utils.CustomUtility.dP[i]);
            } return true;
        }

        /// <summary> Retrieves the <see cref="MapData"/> associated with a grid coordinate if
        /// it is currently being managed by <see cref="CPUMapManager"/>.  </summary>
        /// <param name="GCoord">The coordinate in grid space of the <see cref="MapData"/> which is returned</param>
        /// <returns>The <see cref="MapData"/> at the given grid coordinate if tracked by <see cref="CPUMapManager"/>, 
        /// otherwise returns a <see cref="MapData"/> instance with material = 0x7FFF. </returns>
        public static MapData SampleMap(int3 GCoord) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            int PIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return MapData.Null;
            //Not available(currently readingback) || Out of Bounds
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord))
                return MapData.Null;
            return mapInfo.ChunkData[mapInfo.Offset + PIndex];
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
        public static bool GetOrCreateMapMeta<TMeta>(int3 GCoord, Func<TMeta> GetDefault, out TMeta ret) {
            int3 MCoord = ((GCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
            int3 CCoord = (GCoord - MCoord) / mapChunkSize;
            ret = default;

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return false;
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);
            
            if (!mapInfo.MapMetaData.TryGetValue(PIndex, out object value)) {
                ret = GetDefault();
                mapInfo.MapMetaData[PIndex] = ret;
                ChunkFlags[(uint)mapInfo.FlagIndex] = ChunkMapInfo.Flags.Dirty;
                return true;
            }

            if (value is TMeta tVal)
                ret = tVal;
            else {
                ret = GetDefault();
                mapInfo.MapMetaData[PIndex] = ret;
                ChunkFlags[(uint)mapInfo.FlagIndex] = ChunkMapInfo.Flags.Dirty;
            }
            return true;
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
            ret = default;

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return false;
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);

            if (!mapInfo.MapMetaData.TryGetValue(PIndex, out object value))
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

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return false;
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord))
                return false;

            ChunkFlags[(uint)mapInfo.FlagIndex] = ChunkMapInfo.Flags.Dirty;
            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);
            if (value == null) return mapInfo.MapMetaData.TryRemove(PIndex, out _);
            if (!mapInfo.MapMetaData.TryAdd(PIndex, value))
                mapInfo.MapMetaData[PIndex] = value;
            return true;
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

            if (!ChunkData.TryGetValue(CCoord, out ChunkMapInfo mapInfo)) return false;
            if (!mapInfo.HasMap || math.any(mapInfo.CCoord != CCoord))
                return false;

            uint PIndex = (uint)(MCoord.x * mapChunkSize * mapChunkSize +
                                 MCoord.y * mapChunkSize + MCoord.z);
                                 
            if (!mapInfo.MapMetaData.TryGetValue(PIndex, out object value))
                return false;
                
            ChunkFlags[(uint)mapInfo.FlagIndex] = ChunkMapInfo.Flags.Dirty;
            return mapInfo.MapMetaData.TryRemove(PIndex, out _);
        }

        public static void AddOnCreateChunk(Action<int3> callback) => OnCreateChunk += callback;
        public static void RemoveOnCreateChunk(Action<int3> callback) => OnCreateChunk -= callback;

        public static void AddOnReleaseChunk(Action<int3> callback) => OnReleaseChunk += callback;
        public static void RemoveOnReleaseChunk(Action<int3> callback) => OnReleaseChunk -= callback;

        /// <summary>
        /// A value-type view of a shared chunk allocation. Every copy refers to the same Generation
        /// object, so progress, metadata and subscribers change without dictionary reassignment.
        /// </summary>
        public struct ChunkMapInfo {
            public readonly int3 CCoord;
            public NativeArray<MapData> ChunkData;
            // Shared by struct copies and the offloaded owner; modified on the main thread.
            public readonly HashSet<Task> PendingWrites;
            public ChunkGenerationDriver.GenerationState Generation;
            public readonly int FlagIndex;
            public readonly int Offset;
            public readonly int Length;
            private readonly bool ownsMemory;

            public State MapProgress {
                readonly get => Generation == null ? State.Disposed : (State)Volatile.Read(ref Generation.mapProgress);
                set {
                    Volatile.Write(ref Generation.mapProgress, (int)value);
                }
            }
            public State EntityProgress {
                readonly get => Generation == null ? State.Disposed : (State)Volatile.Read(ref Generation.entityProgress);
                set => Volatile.Write(ref Generation.entityProgress, (int)value);
            }
            public readonly bool HasMap => MapProgress == State.Finished;
            // Dirty CPU data supersedes the retained generation map.
            public readonly bool HasGraphicsMap => Generation?.Pipeline?.IsReady == true && Generation.Pipeline.HasRetainedMap && ChunkFlags[(uint)FlagIndex] == Flags.Clean;
            public readonly bool HasEntities => EntityProgress == State.Finished;
            public readonly Task Ready => Generation.Ready;
            public readonly ConcurrentDictionary<uint, object> MapMetaData => Generation.MapMetaData;

            public ChunkMapInfo(int3 CCoord, int flagIndex, int length, Allocator allocator = Allocator.Persistent,
                NativeArrayOptions options = NativeArrayOptions.UninitializedMemory) {
                this.CCoord = CCoord;
                this.ChunkData = new NativeArray<MapData>(length, allocator, options);
                this.FlagIndex = flagIndex;
                this.Offset = 0;
                this.Length = length;
                this.Generation = new ChunkGenerationDriver.GenerationState();
                this.PendingWrites = new HashSet<Task>();
                this.ownsMemory = true;
            }

            /// <summary>A view of caller-owned native memory; disposing the view does not free its owner's array.</summary>
            public ChunkMapInfo(int3 CCoord, int flagIndex, NativeArray<MapData> chunkData, int length, int offset = 0) {
                if (!chunkData.IsCreated || offset < 0 || length < 0 || offset > chunkData.Length - length)
                    throw new ArgumentException("Invalid chunk data range.", nameof(chunkData));
                this.CCoord = CCoord;
                this.ChunkData = chunkData;
                this.FlagIndex = flagIndex;
                this.Offset = offset;
                this.Length = length;
                this.Generation = new ChunkGenerationDriver.GenerationState();
                this.PendingWrites = new HashSet<Task>();
                this.ownsMemory = false;
            }

            internal readonly void CheckUnused() {
                if (Generation != null && (Generation.Subscribers.Count != 0 || (Ready != null && !Ready.IsCompleted)))
                    throw new InvalidOperationException("Cannot offload a chunk with subscribers or an outstanding load.");
            }

            /// <summary>Transfer CPU data without disposing it; invalidate existing views of the old record.</summary>
            public ChunkMapInfo Offload() {
                if (Generation == null || MapProgress == State.Disposed) { this = default; return default; }
                CheckUnused();
                ChunkMapInfo result = this;
                result.Generation = new ChunkGenerationDriver.GenerationState(Generation.MapMetaData) {
                    mapProgress = Generation.mapProgress, entityProgress = Generation.entityProgress
                };
                Volatile.Write(ref Generation.mapProgress, (int)State.Disposed);
                Volatile.Write(ref Generation.entityProgress, (int)State.Disposed);
                this = default;
                return result;
            }

            /// <summary>Dispose owned data and clear this view. Offloaded records have no GPU resources.</summary>
            public void Dispose() {
                if (Generation == null || MapProgress == State.Disposed) { this = default; return; }
                CheckUnused();
                MapProgress = State.Disposed;
                EntityProgress = State.Disposed;
                try {
                    Generation.Entities?.Release(CCoord);
                    Generation.Pipeline?.Release();
                }
                finally {
                    if (ownsMemory && ChunkData.IsCreated) ChunkData.Dispose();
                    this = default;
                }
            }

            public enum Flags : byte { Clean = 0, Dirty = 1 }
            public enum State { Pending = 0, InProgress = 1, Finished = 2, Failed = 3, Disposed = 4 }
        }

#if UNITY_EDITOR
        [CustomPropertyDrawer(typeof(MapData))]
        internal class MapDataDrawer : PropertyDrawer {
            public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
                SerializedProperty dataProp = property.FindPropertyRelative("data");
                uint data = dataProp.uintValue;

                //bool isDirty = (data & 0x80000000) != 0;
                int[] values = new int[2]{
                    (int)((data >> 8) & 0xFF),
                    (int)(data & 0xFF)
                };
                bool isDirty = (data & 0x80000000) != 0;

                Rect rect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

                RegistryReferenceDrawer.SetupRegistries();
                RegistryReferenceDrawer materialDrawer = new RegistryReferenceDrawer { BitMask = 0x7FFF, BitShift = 16 };
                materialDrawer.DrawRegistryDropdown(rect, dataProp, new GUIContent("Material"),
                    Config.TEMPLATE.Generation.Materials.value.MaterialDictionary);
                rect.y += EditorGUIUtility.singleLineHeight;
                EditorGUI.MultiIntField(rect, new GUIContent[] { new("Viscosity"), new("Density") }, values);
                rect.y += EditorGUIUtility.singleLineHeight;
                isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
                rect.y += EditorGUIUtility.singleLineHeight;
                //isDirty = EditorGUI.Toggle(rect, "Is Dirty", isDirty);
                //rect.y += EditorGUIUtility.singleLineHeight;

                //data = (isDirty ? data | 0x80000000 : data & 0x7FFFFFFF);
                data = (data & 0xFFFF00FF) | (((uint)values[0] & 0xFF) << 8);
                data = (data & 0xFFFFFF00) | ((uint)values[1] & 0xFF);
                data = isDirty ? data | 0x80000000 : data & 0x7FFFFFFF;

                dataProp.uintValue = (data & 0x8000FFFF) | (dataProp.uintValue & 0x7FFF0000);
            }

            // Override this method to make space for the custom fields
            public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
                return EditorGUIUtility.singleLineHeight * 3;
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
        /// registry of the <see cref="Configuration.Generation.Material"/> responsible for controling the apperance 
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

        public static MapData Null => new (){data = 0xFFFFFFFF };

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
        public int _material {
            readonly get => (int)((data >> 16) & 0x7FFF);
            set => data = (data & 0x8000FFFF) | (uint)((value & 0x7FFF) << 16);
        }

        /// <summary> The maximum value for <see cref="density"/> supported by the game </summary>
        public static int MaxDensity => 0xFF;
        /// <summary> The maximum value for <see cref="viscosity"/> supported by the game </summary>
        public static int MaxViscosity => 0xFF;
    }
}
