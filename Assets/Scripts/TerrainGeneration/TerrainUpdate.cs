using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.PlayerLoop;
using static CPUDensityManager;

namespace TerrainGeneration{

/// <summary>
/// Terrain Update is a static system that handles updates to map entries within <see cref="CPUDensityManager"/>. Whenever a
/// map entry is modified(e.g. by a player, through an update, etc.) it should be updated in case it has a specific behavior.
/// Materials(map entries) must all define an <see cref="MaterialData.UpdateMat"/> method that will be called when the map entry
/// is updated.
/// </summary>
public static class TerrainUpdate
{
    private static ConstrainedQueue<int3> UpdateCoordinates; //GCoord
    /*We have to keep track of the points we already 
    included so we don't include them again*/
    private static FlagList IncludedCoords;
    private static Manager Executor;
    private static MaterialData[] MaterialDictionary;

    const int MAX_UPDATE_COUNT = 5000; 
    const int UPDATE_FREQ = 4;
    private static int numPointsAxis;
    private static int UpdateTick;
    /// <summary>
    /// Initializes the Terrain Update System. Must be called before any updates are added.
    /// Allocates memory for the update system, which is fixed and cannot be resized.
    /// </summary>
    public static void Initialize(){
        RenderSettings rSettings = WorldOptions.CURRENT.Quality.Rendering.value;
        int numPointsChunk = rSettings.mapChunkSize;
        int numChunksAxis = OctreeTerrain.Octree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
        numPointsAxis = numChunksAxis * numPointsChunk;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        
        MaterialDictionary = WorldOptions.CURRENT.Generation.Materials.value.MaterialDictionary.SerializedData;
        UpdateCoordinates = new ConstrainedQueue<int3>(MAX_UPDATE_COUNT);
        IncludedCoords = new FlagList(numPoints);
        Executor = new Manager{active = false};
        UpdateTick = 0;
    }

    private static int HashFlag(int3 GCoord){
        int3 HCoord = ((GCoord % numPointsAxis) + numPointsAxis) % numPointsAxis;
        return HCoord.x * numPointsAxis * numPointsAxis + HCoord.y * numPointsAxis + HCoord.z;
    }
    /// <summary>
    /// Adds a map entry to the update queue. The map entry will be updated in the next fixed update if there
    /// is enough space for it, otherwise it is discarded. If the map entry already exists in the queue, it is ignored.
    /// </summary> <param name="GCoord">The grid space coordinate of the map entry. The caller should only call this with coordinates present within <see cref="CPUDensityManager"/></param>
    public static void AddUpdate(int3 GCoord){
        int flagIndex = HashFlag(GCoord);
        if(IncludedCoords.GetFlag(flagIndex)) return;
        if(!UpdateCoordinates.Enqueue(GCoord)) return;
        IncludedCoords.SetFlag(flagIndex, true);

        if(!Executor.active){
            Executor = new Manager();
            OctreeTerrain.MainFixedUpdateTasks.Enqueue(Executor);
        };
    }

    /// <summary>
    /// Update Task that is tied with the <see cref="OctreeTerrain.MainFixedUpdateTasks"/>. 
    /// It is responsible for updating the map entries within Unity's fixed update loop.
    /// </summary>
    public class Manager : UpdateTask{
        /// <summary>  Default constructor sets the manager to active. </summary>
        public Manager(){ active = true; }
        /// <summary>
        /// Updates the map entries in the update queue. 
        /// The map entries are updated in the order they were added.
        /// </summary> <param name="mono"><see cref="UpdateTask.Update"/> </param>
        public override void Update(MonoBehaviour mono){
            if(UpdateCoordinates.Length == 0) {
                this.active = false;
            }
            UpdateTick = (UpdateTick + 1) % UPDATE_FREQ;
            if(UpdateTick != 0) return;

            int count = UpdateCoordinates.Length;
            for(int i = 0; i < count; i++){
                int3 coord = UpdateCoordinates.Dequeue();
                IncludedCoords.SetFlag(HashFlag(coord), false);

                MapData mapData = SampleMap(coord);
                if(mapData.data == 0xFFFFFFFF) continue; //Invalid Data
                MaterialDictionary[mapData.material].UpdateMat(coord);
            }
        }
    }

    private struct ConstrainedQueue<T>{
        /*Consists of 2 LinkedList
            1. Circular Linked List Holding Allocated Nodes
            2. One-Way Linked List Tracking Free Nodes
        
        A queue with initial size and explicitly no resizing
        Also Explicitly One Memory Location*/

        public LListNode[] array;
        private int _length;
        public readonly int Length{get{return _length;}}
        public ConstrainedQueue(int length){
            //We Need Clear Memory Here
            array = new LListNode[length + 1];
            array[0].next = 1; //Head Node
            array[0].previous = 2; //Free Head Node

            //Make List Circularly Linked
            array[1].next = 1; 
            array[1].previous = 1;
            _length = 0;
        }

        public bool Enqueue(T node){
            if(_length >= array.Length - 2)
                return false; //Just Ignore it
            
            int freeNode = array[0].previous; //Free Head Node
            int nextNode = array[freeNode].next == 0 ? freeNode + 1 : array[freeNode].next;
            array[0].previous = nextNode;

            int tailNode = array[array[0].next].previous; //Tail Node
            nextNode = array[tailNode].next; // = array[0].next
            array[tailNode].next = freeNode;
            array[nextNode].previous = freeNode;
            array[freeNode].previous = tailNode;
            array[freeNode].next = nextNode;
            array[freeNode].value = node;
            _length++;

            return true;
        }

        public T Dequeue(){
            if(_length == 0)
                return default(T);
            
            int headNode = array[0].next; //Head Node
            int nextNode = array[headNode].next; //Head Node
            int prevNode = array[headNode].previous; //Tail Node
            array[prevNode].next = nextNode;
            array[nextNode].previous = prevNode;
            array[0].next = nextNode;

            int freeNode = array[0].previous;
            array[headNode].next = freeNode;
            array[0].previous = headNode;
            _length--;

            return array[headNode].value;
        }

        public struct LListNode{
            public int previous;
            public int next;
            public T value;
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
            int byteIndex = index / 8;
            int bitIndex = index % 8;
            return (flags[byteIndex] & (1 << bitIndex)) != 0;
        }

        public void SetFlag(int index, bool value){
            int byteIndex = index / 8;
            int bitIndex = index % 8;
            if(value) flags[byteIndex] |= (byte)(1 << bitIndex);
            else flags[byteIndex] &= (byte)~(1 << bitIndex);
        }
    }
}}
