using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Material;
using System.Collections.Concurrent;
using Unity.Jobs;
using Arterra.Configuration;
using Arterra.Data.Intrinsic;
using Arterra.Core.Storage;
using Arterra.GamePlay;
using System.Threading;
using System;

namespace Arterra.Data.Intrinsic{
    /// <summary>
    /// Settings controlling how updates to the terrain are performed 
    /// and how much load it is allotted. Terrain updates are point-operations
    /// applied to any map entry in <see cref="CPUMapManager"/>. Once a map entry is updated
    /// the correspondng material's unique update method will be called if it is defined.
    /// See <see cref="MaterialData.PropogateMaterialUpdate(int3, Unity.Mathematics.Random)"/> and
    /// <see cref="MaterialData.RandomMaterialUpdate(int3, Unity.Mathematics.Random)"/> for more information.
    /// </summary>
    [System.Serializable]
    public class TerrainUpdation : ICloneable{
        /// <summary> How many random points are chosen to be updated at random 
        /// per update cycle per chunk. Random Updates allow certain materials to 
        /// base behavior off stochastic sampling. Random Updates will only
        /// be added if the backlog of update points does not exceed 
        /// <see cref="MaximumTickUpdates"/> </summary>
        public int RandomUpdatesPerChunk = 50;
        /// <summary>
        /// The maximum number of updates that can be performed in a 
        /// single update cycle. Increasing this value may increase simulation
        /// speed of update-based-effects at the cost of performance.
        /// </summary>
        public int MaximumTickUpdates = 5000;
        /// <summary>
        /// The number of in-game-ticks that must pass before the next update cycle.
        /// An in-game tick is one update to Unity's FixedUpdate loop.
        /// </summary>
        public int UpdateTickDelay = 4;

        /// <summary> Clones the object </summary>
        /// <returns></returns>
        public object Clone() {
            return new TerrainUpdation {
                RandomUpdatesPerChunk = RandomUpdatesPerChunk,
                MaximumTickUpdates = MaximumTickUpdates,
                UpdateTickDelay = UpdateTickDelay
            };
        }
    }
}

namespace Arterra.Engine.Terrain{

    /// <summary>
    /// Terrain Update is a static system that handles updates to map entries within <see cref="CPUMapManager"/>. Whenever a
    /// map entry is modified(e.g. by a player, through an update, etc.) it should be updated in case it has a specific behavior.
    /// Materials(map entries) must all define a <see cref="MaterialData.PropogateMaterialUpdate(int3, Unity.Mathematics.Random)"/> and
    /// <see cref="MaterialData.RandomMaterialUpdate(int3, Unity.Mathematics.Random)"/> method that will be called when the map entry
    /// is updated.
    /// </summary>
    public static class TerrainUpdate {
        private static Catalogue<MaterialData> MaterialDictionary;
        private static ConcurrentQueue<int3> UpdateCoordinates; //GCoord
        private static TerrainUpdation settings;
        /*We have to keep track of the points we already 
        included so we don't include them again*/
        private static FlagList IncludedCoords;
        private static Manager Executor;
        private static int numPointsAxis;
        private static int UpdateTick;
        /// <summary>
        /// Initializes the Terrain Update System. Must be called before any updates are added.
        /// Allocates memory for the update system, which is fixed and cannot be resized.
        /// </summary>
        public static void Initialize() {
            settings = Config.CURRENT.System.TerrainUpdation.value;
            Configuration.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
            int numPointsChunk = rSettings.mapChunkSize;
            int numChunksAxis = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
            numPointsAxis = numChunksAxis * numPointsChunk;
            int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

            MaterialDictionary = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            UpdateCoordinates = new ConcurrentQueue<int3>();
            IncludedCoords = new FlagList(numPoints);
            Executor = new Manager();
            OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
            UpdateTick = 0;
        }

        private static int HashFlag(int3 GCoord) {
            int3 HCoord = ((GCoord % numPointsAxis) + numPointsAxis) % numPointsAxis;
            return HCoord.x * numPointsAxis * numPointsAxis + HCoord.y * numPointsAxis + HCoord.z;
        }
        /// <summary>
        /// Adds a map entry to the update queue. The map entry will be updated in the next fixed update if there
        /// is enough space for it, otherwise it is discarded. If the map entry already exists in the queue, it is ignored.
        /// </summary> <param name="GCoord">The grid space coordinate of the map entry. The caller should only call this with coordinates present within <see cref="CPUMapManager"/></param>
        public static void AddUpdate(int3 GCoord) {
            int flagIndex = HashFlag(GCoord);
            if (!IncludedCoords.SetFlag(flagIndex)) return;
            UpdateCoordinates.Enqueue(GCoord);
        }

        /// <summary>
        /// Update Task that is tied with the <see cref="OctreeTerrain.MainFixedUpdateTasks"/>. 
        /// It is responsible for updating the map entries within Unity's fixed update loop.
        /// </summary>
        public class Manager : IUpdateSubscriber {
            private bool active = false;
            /// <exclude />
            public bool Active {
                get => active;
                set => active = value;
            }
            /// <summary> Whether or not the job is currently running; whether the job
            /// currently has threads in the thread pool executing it. </summary>
            public bool dispatched = false;
            private JobHandle propHandle;
            private JobHandle randHandle;
            private PropogatedUpdates propogatedUpdates;
            private RandomUpdates randomUpdates;

            /// <summary>  Default constructor sets the manager to active. </summary>
            public Manager() {
                propogatedUpdates = new PropogatedUpdates();
                randomUpdates = new RandomUpdates();
                propogatedUpdates.seed = new Unity.Mathematics.Random((uint)GetHashCode());
                randomUpdates.seed = new Unity.Mathematics.Random((uint)GetHashCode() ^ 0x12345678u);
                randomUpdates.numUpdatesPerChunk = settings.RandomUpdatesPerChunk;
                int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
                randomUpdates.mapChunkSize = mapChunkSize;
                randomUpdates.numPointsChunk = mapChunkSize * mapChunkSize * mapChunkSize;
                dispatched = false;
                active = true;
            }

            /// <summary> Completes the asyncronous Job Operation. Blocks main thread
            /// until the job has completed. </summary>
            public bool Complete() {
                if (!dispatched) return true;
                if (!propHandle.IsCompleted || !randHandle.IsCompleted)
                    return false;
                propHandle.Complete();
                randHandle.Complete();
                dispatched = false;
                return true;
            }
            /// <summary>
            /// Updates the map entries in the update queue. 
            /// The map entries are updated in the order they were added.
            /// </summary> <param name="mono"><see cref="IUpdateSubscriber.Update"/> </param>
            public void Update(MonoBehaviour mono) {
                if (!PlayerHandler.active) return;
                UpdateTick = (UpdateTick + 1) % settings.UpdateTickDelay;
                if (UpdateTick != 0) return;
                if (!Complete()) return;
                propogatedUpdates.seed.NextUInt();
                randomUpdates.seed.NextUInt();

                int count = math.min(UpdateCoordinates.Count, settings.MaximumTickUpdates);
                propHandle = propogatedUpdates.Schedule(count, 64);
                randHandle = randomUpdates.Schedule(CPUMapManager.numChunks, 64);
                dispatched = true;
            }

            /// <summary> The Job that is responsible for processing in parallel
            /// all points whose updates have been propogated. </summary>
            public struct PropogatedUpdates : IJobParallelFor {
                /// <summary>
                /// Random values referencable by materials; Materials are processed in parallel,
                /// thus this isn't thread safe, but that adds to the randomness.
                /// </summary>
                public Unity.Mathematics.Random seed;
                /// <summary> Executes the point update in parallel. </summary>
                /// <param name="index">The index of the job-thread executing the task</param>
                public void Execute(int index) {
                    if (!UpdateCoordinates.TryDequeue(out int3 coord))
                        return;
                    IncludedCoords.ClearFlag(HashFlag(coord));

                    MapData mapData = CPUMapManager.SampleMap(coord);
                    if (!MaterialDictionary.Contains(mapData.material))
                        return; //Invalid Data
                    Unity.Mathematics.Random prng = new((seed.state ^ (uint)index) | 1u);
                    MaterialDictionary.Retrieve(mapData.material).PropogateMaterialUpdate(coord, prng);
                }
            }

            /// <summary> The Job responsible for processing random updates in parallel. 
            /// Random updates are performed on a per-chunk basis, and each chunk
            /// is processed in its own job thread. </summary>
            public struct RandomUpdates : IJobParallelFor {
                /// <summary> Random values referencable by materials; Materials are processed in parallel,
                /// thus this isn't thread safe, but that adds to the randomness. /// </summary>
                public Unity.Mathematics.Random seed;
                /// <summary> The number of random updates to perform per one chunk. See <see cref="TerrainUpdation.RandomUpdatesPerChunk"/> for more information. </summary>
                public int numUpdatesPerChunk;
                /// <summary> The size of a map chunk, the number of points per axis in a map chunk. </summary>
                public int mapChunkSize;
                /// <summary> The amount of points total in a map chunk, equivalent to <see cref="mapChunkSize"/>^3. </summary>
                public int numPointsChunk;
                /// <summary> Executes the random update in parallel. Each job thread is responsible for
                /// processing a single chunk of the map. The chunk is identified by its index in the
                /// <see cref="CPUMapManager.AddressDict"/> dictionary. </summary>
                /// <param name="index"></param>
                public void Execute(int index) {
                    CPUMapManager.ChunkMapInfo info = CPUMapManager.AddressDict[index];
                    if (!info.valid) return;
                    Unity.Mathematics.Random prng = new((seed.state ^ (uint)index) | 1u);
                    int3 CCoord = info.CCoord;
                    int CIndex = CPUMapManager.HashCoord(CCoord);
                    for (uint i = 0; i < numUpdatesPerChunk; i++) {
                        //Generate a random point in the chunk
                        int3 MCoord = new(
                            (int)(prng.NextUInt() % mapChunkSize),
                            (int)(prng.NextUInt() % mapChunkSize),
                            (int)(prng.NextUInt() % mapChunkSize)
                        );
                        int MIndex = MCoord.x * mapChunkSize * mapChunkSize + MCoord.y * mapChunkSize + MCoord.z;
                        MapData mapData = CPUMapManager.SectionedMemory[CIndex * numPointsChunk + MIndex];
                        if (!MaterialDictionary.Contains(mapData.material)) {
                            Debug.LogError($"Encountered unknown material {mapData.material} at chunk {CCoord}");
                            return;
                        }
                        MaterialDictionary.Retrieve(mapData.material).RandomMaterialUpdate(CCoord * mapChunkSize + MCoord, prng);
                    }
                }
            }
        }

        private struct FlagList {
            public volatile int[] flags;
            public int length;
            public FlagList(int length) {
                this.length = length;
                int numFlags = Mathf.CeilToInt(length / 32.0f);
                flags = new int[numFlags];
            }

            public void ClearFlag(int index) {
                int intIndex = index / 32;
                int bitIndex = index % 32;
                //This also has to be interlocked because we might be holding an outdated member
                InterlockedAnd(ref flags[intIndex], ~(0x1 << bitIndex));
            }

            public bool SetFlag(int index) {
                int intIndex = index / 32;
                int bitIndex = index % 32;

                int ret = InterlockedOr(ref flags[intIndex], 0x1 << bitIndex);
                return ((ret >> bitIndex) & 0x1) == 0;
            }

            private static int InterlockedOr(ref int dest, int val) {
                int next = dest; int prev;
                do {
                    prev = next;
                    next = prev | val;
                    next = Interlocked.CompareExchange(ref dest, next, prev);
                } while (next != prev);
                return next;
            }

            private static int InterlockedAnd(ref int dest, int val) {
                int next = dest; int prev;
                do {
                    prev = next;
                    next = prev & val;
                    next = Interlocked.CompareExchange(ref dest, next, prev);
                } while (next != prev);
                return next;
            }
        }
    
}
}
