using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using Utils;
using Unity.Mathematics;

public class SurfaceChunk : ChunkData
{
    public BaseMap baseMap;

    int2 SCoord;
    Vector2 position;
    bool active = true;

    // Start is called before the first frame update
    public SurfaceChunk(int2 coord)
    {
        this.SCoord = coord;
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        this.position = CustomUtility.AsVector(coord) * rSettings.mapChunkSize - Vector2.one * (rSettings.mapChunkSize / 2f);
        baseMap = new BaseMap(position);

        CreateBaseChunk();
        Update();
    }

    public void Update()
    {
        lastUpdateChunks.Enqueue(this);
    }

    /*y
     *^ 5      1      6
     *|    _ _ _ _ _ 
     *|   |         |
     *| 4 |         |  2
     *|   |         |
     *|   |_ _ _ _ _|
     *|         
     *| 8      3      7
     *+----------------->x
     */

    public void CreateBaseChunk()
    {
        if (baseMap.hasChunk)
            return;
        
        if (!baseMap.hasRequestedChunk)
        {
            baseMap.hasRequestedChunk = true;
            EndlessTerrain.GenTask surfChunkTask = new EndlessTerrain.GenTask{
                valid = () => this.active,
                task = () => baseMap.GetChunk(), 
                load = taskLoadTable[(int)priorities.planning]
            };
            RequestQueue.Enqueue(surfChunkTask);
        }
    }

    public override void UpdateVisibility(int3 CSCoord, float maxRenderDistance)
    {
        int2 distance = SCoord - new int2(CSCoord.x, CSCoord.z);
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y)) <= maxRenderDistance;

        if (!visible)
            DestroyChunk();
    }

    public override void DestroyChunk()
    {
        if (!active)
            return;

        active = false;
        baseMap.ReleaseSurfaceMap();
        surfaceChunkDict.Remove(SCoord);
    }

    public class BaseMap
    {
        SurfaceCreator mapCreator;

        //Return values--height map and squash map
        uint surfAddress;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        Vector2 position;

        public BaseMap(Vector2 position){
            this.mapCreator = new SurfaceCreator();
            this.position = position;
        }

        public void GetChunk()
        {
            mapCreator.SampleSurfaceMaps(position, 0);
            this.surfAddress = mapCreator.StoreSurfaceMap(0);
            hasChunk = true;
        }

        public uint GetMap(){  return surfAddress; }

        public void ReleaseSurfaceMap()
        {
            if(!hasChunk)
                return;
            hasChunk = false;
            
            GenerationPreset.memoryHandle.ReleaseMemory(this.surfAddress);
        }
    }

    public struct NoiseMaps
    {
        public ComputeBuffer continental;
        public ComputeBuffer erosion;
        public ComputeBuffer pvNoise;
        public ComputeBuffer squash;
        public ComputeBuffer atmosphere;
        public ComputeBuffer humidity;
    }
}
