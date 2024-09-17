using System.Collections;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.PlayerLoop;
using static CPUDensityManager;

public static class TerrainUpdateManager
{
    public static ConstrainedQueue<int3> UpdateCoordinates; //GCoord
    /*We have to keep track of the points we already 
    included so we don't include them again*/
    public static FlagList IncludedCoords;
    public static TerrainUpdate Executor;
    private static List<Option<MaterialData>> MaterialDictionary;

    const int MAX_UPDATE_COUNT = 5000; //10k
    const int UPDATE_FREQ = 4;
    private static int numPointsAxis;
    private static int UpdateTick;
    public static void Initialize(){
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        int numPointsChunk = rSettings.mapChunkSize;
        int numChunksAxis = 2 * rSettings.detailLevels.value[0].chunkDistThresh;
        numPointsAxis = numChunksAxis * numPointsChunk;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;
        
        MaterialDictionary = WorldStorageHandler.WORLD_OPTIONS.Generation.value.Materials.value.MaterialDictionary.value;
        UpdateCoordinates = new ConstrainedQueue<int3>(MAX_UPDATE_COUNT);
        IncludedCoords = new FlagList(numPoints);
        Executor = new TerrainUpdate{active = false};
        UpdateTick = 0;
    }

    private static int HashFlag(int3 GCoord){
        int3 HCoord = ((GCoord % numPointsAxis) + numPointsAxis) % numPointsAxis;
        return HCoord.x * numPointsAxis * numPointsAxis + HCoord.y * numPointsAxis + HCoord.z;
    }

    public static void AddUpdate(int3 GCoord){
        int flagIndex = HashFlag(GCoord);
        if(IncludedCoords.GetFlag(flagIndex)) return;
        if(!UpdateCoordinates.Enqueue(GCoord)) return;
        IncludedCoords.SetFlag(flagIndex, true);

        if(!Executor.active){
            Executor = new TerrainUpdate();
            EndlessTerrain.MainFixedUpdateTasks.Enqueue(Executor);
        };
    }

    public class TerrainUpdate : UpdateTask{

        public TerrainUpdate(){
            active = true;
        }
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
                MaterialDictionary[mapData.material].value.UpdateMat(coord);
            }
        }
    }
}

public struct ConstrainedQueue<T>{
    /*
    Consists of 2 LinkedList
        1. Circular Linked List Holding Allocated Nodes
        2. One-Way Linked List Tracking Free Nodes
    
    A queue with initial size and explicitly no resizing
    Also Explicitly One Memory Location
    */

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

public struct FlagList{
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
