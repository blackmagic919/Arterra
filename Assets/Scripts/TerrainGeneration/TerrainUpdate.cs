using Unity.Mathematics;
using UnityEngine;
using static CPUMapManager;
using WorldConfig;
using WorldConfig.Generation.Material;
using System.Collections.Concurrent;
using Unity.Jobs;

namespace TerrainGeneration{

/// <summary>
/// Terrain Update is a static system that handles updates to map entries within <see cref="CPUMapManager"/>. Whenever a
/// map entry is modified(e.g. by a player, through an update, etc.) it should be updated in case it has a specific behavior.
/// Materials(map entries) must all define an <see cref="MaterialData.UpdateMat"/> method that will be called when the map entry
/// is updated.
/// </summary>
public static class TerrainUpdate
{
    private static Registry<MaterialData> MaterialDictionary;
    private static ConcurrentQueue<int3> UpdateCoordinates; //GCoord
    /*We have to keep track of the points we already 
    included so we don't include them again*/
    private static FlagList IncludedCoords;
    private static Manager Executor;

    const int RANDOM_UPDATE_COUNT = 100;
    const int MAX_UPDATE_COUNT = 5000; 
    const int UPDATE_FREQ = 4;
    private static int numPointsAxis;
    private static int UpdateTick;
    /// <summary>
    /// Initializes the Terrain Update System. Must be called before any updates are added.
    /// Allocates memory for the update system, which is fixed and cannot be resized.
    /// </summary>
    public static void Initialize(){
        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        int numPointsChunk = rSettings.mapChunkSize;
        int numChunksAxis = OctreeTerrain.Octree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
        numPointsAxis = numChunksAxis * numPointsChunk;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        
        MaterialDictionary = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
        UpdateCoordinates = new ConcurrentQueue<int3>();
        IncludedCoords = new FlagList(numPoints);
        Executor = new Manager{active = true};
        OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
        UpdateTick = 0;
    }

    private static int HashFlag(int3 GCoord){
        int3 HCoord = ((GCoord % numPointsAxis) + numPointsAxis) % numPointsAxis;
        return HCoord.x * numPointsAxis * numPointsAxis + HCoord.y * numPointsAxis + HCoord.z;
    }
    /// <summary>
    /// Adds a map entry to the update queue. The map entry will be updated in the next fixed update if there
    /// is enough space for it, otherwise it is discarded. If the map entry already exists in the queue, it is ignored.
    /// </summary> <param name="GCoord">The grid space coordinate of the map entry. The caller should only call this with coordinates present within <see cref="CPUMapManager"/></param>
    public static void AddUpdate(int3 GCoord){
        int flagIndex = HashFlag(GCoord);
        if(IncludedCoords.GetFlag(flagIndex)) return;
        UpdateCoordinates.Enqueue(GCoord); 
        IncludedCoords.SetFlag(flagIndex, true);
    }

    /// <summary>
    /// Adds a number of random points into <see cref="UpdateCoordinates"/> to be updated in the
    /// next update cycle. All points added are guaranteed to be within the <see cref="CPUMapManager"/>'s map--
    /// that is only map entries currently a part of a <see cref="TerrainChunk.RealChunk"/> can be sampled randomly.
    /// </summary>
    /// <param name="numUpdates">The number of updates to add</param>
    public static void AddRandomUpdates(int numUpdates){
        static int3 GetRandomPoint(){
            WorldConfig.Quality.Terrain s = Config.CURRENT.Quality.Terrain.value;
            float3 oGC = OctreeTerrain.ViewPosGS - (numPointsAxis / 2 + s.mapChunkSize / 2);
            float3 eGC = OctreeTerrain.ViewPosGS + (numPointsAxis / 2 - s.mapChunkSize / 2 - 1);
            int3 pos;
            
            pos.x = Mathf.RoundToInt(UnityEngine.Random.Range(oGC.x, eGC.x));
            pos.y = Mathf.RoundToInt(UnityEngine.Random.Range(oGC.y, eGC.y));
            pos.z = Mathf.RoundToInt(UnityEngine.Random.Range(oGC.z, eGC.z));
            return pos;
        }

        for(int i = 0; i < numUpdates; i++){
            AddUpdate(GetRandomPoint());
        }
    }

    /// <summary>
    /// Update Task that is tied with the <see cref="OctreeTerrain.MainFixedUpdateTasks"/>. 
    /// It is responsible for updating the map entries within Unity's fixed update loop.
    /// </summary>
    public class Manager : UpdateTask{
        /// <summary> Whether or not the job is currently running; whether the job
        /// currently has threads in the thread pool executing it. </summary>
        public bool dispatched = false;
        private JobHandle handle;
        private Context cxt;
        /// <summary>  Default constructor sets the manager to active. </summary>
        public Manager(){ 
            cxt = new Context();
            cxt.seed = new Unity.Mathematics.Random((uint)GetHashCode());
            dispatched = false;
            active = true; 
            
        }

        /// <summary> Completes the asyncronous Job Operation. Blocks main thread
        /// until the job has completed. </summary>
        public bool Complete(){
            if(!dispatched) return true;
            if(!handle.IsCompleted) return false; 
            handle.Complete();
            dispatched = false;
            return true;
        }
        /// <summary>
        /// Updates the map entries in the update queue. 
        /// The map entries are updated in the order they were added.
        /// </summary> <param name="mono"><see cref="UpdateTask.Update"/> </param>
        public override void Update(MonoBehaviour mono){
            UpdateTick = (UpdateTick + 1) % UPDATE_FREQ;
            if(UpdateTick != 0) return;
            cxt.seed.NextUInt();
            if(!Complete()) return;

            if(UpdateCoordinates.Count < MAX_UPDATE_COUNT){
                AddRandomUpdates(RANDOM_UPDATE_COUNT);
            } if(UpdateCoordinates.Count == 0) {
                return;
            } 

            int count = math.min(UpdateCoordinates.Count, MAX_UPDATE_COUNT);
            handle = cxt.Schedule(count, 128);
            dispatched = true;
        }

        /// <summary> The Job that is responsible for processing in parallel
        /// all points that have been updated. </summary>
        public struct Context: IJobParallelFor{
            /// <summary>
            /// Random values referencable by materials; Materials are processed in parallel,
            /// thus this isn't thread safe, but that adds to the randomness.
            /// </summary>
            public Unity.Mathematics.Random seed;
            /// <summary> Executes the point update in parallel. </summary>
            /// <param name="index">The index of the job-thread executing the task</param>
            public void Execute(int index){
                if(!UpdateCoordinates.TryDequeue(out int3 coord))
                    return;
                IncludedCoords.SetFlag(HashFlag(coord), false);

                MapData mapData = SampleMap(coord);
                if(!MaterialDictionary.Contains(mapData.material)) 
                    return; //Invalid Data
                Unity.Mathematics.Random prng = new ((seed.state ^ (uint)index) | 1u);
                MaterialDictionary.Retrieve(mapData.material).UpdateMat(coord, prng);
            }
        }
    }

    private struct FlagList{
        public byte[] flags;
        public int length;
        public FlagList(int length){
            this.length = length;
            int numFlags = Mathf.CeilToInt(length / 8.0f);
            flags = new byte[numFlags];
        }

        public bool GetFlag(int index){
            bool isSet = false;
            lock(flags){
                int byteIndex = index / 8;
                int bitIndex = index % 8;
                isSet = (flags[byteIndex] & (1 << bitIndex)) != 0;
            }
            return isSet;
        }

        public void SetFlag(int index, bool value){
            lock(flags){
                int byteIndex = index / 8;
                int bitIndex = index % 8;
                if(value) flags[byteIndex] |= (byte)(1 << bitIndex);
                else flags[byteIndex] &= (byte)~(1 << bitIndex);
            }
        }
    }
}}
