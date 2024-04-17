using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using Utils;

public class SurfaceChunk : ChunkData
{
    public BaseMap baseMap;

    Vector2 SCoord;
    Vector2 position;
    bool active = true;

    // Start is called before the first frame update
    public SurfaceChunk(SurfaceCreatorSettings surfSettings, Vector2 coord)
    {
        this.SCoord = coord;
        this.position = coord * mapChunkSize - Vector2.one * (mapChunkSize / 2f);
        baseMap = new BaseMap(surfSettings, position);

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
            EndlessTerrain.GenTask surfChunkTask = new EndlessTerrain.GenTask(() => baseMap.GetChunk(), taskLoadTable[(int)priorities.planning]);
            timeRequestQueue.Enqueue(surfChunkTask, (int)priorities.planning);
        }
    }

    public override void UpdateVisibility(Vector3 CSCoord, float maxRenderDistance)
    {
        Vector2 distance = SCoord - new Vector2(CSCoord.x, CSCoord.z);
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

        public BaseMap(SurfaceCreatorSettings mapCreator, Vector2 position){
            this.mapCreator = new SurfaceCreator(mapCreator);
            this.position = position;
        }

        public void GetChunk()
        {
            mapCreator.SampleSurfaceMaps(position, mapChunkSize, 0);
            this.surfAddress = mapCreator.StoreSurfaceMap(mapChunkSize, 0);

            mapCreator.ReleaseTempBuffers();
            hasChunk = true;
        }

        public SurfData GetMap()
        {  
            return new SurfData(mapCreator.settings.surfaceMemoryBuffer.AccessStorage(), 
                                mapCreator.settings.surfaceMemoryBuffer.AccessAddresses(), 
                                this.surfAddress);
        }

        public void ReleaseSurfaceMap()
        {
            if(!hasChunk)
                return;
            
            mapCreator.settings.surfaceMemoryBuffer.ReleaseMemory(this.surfAddress);
        }
    }

    public struct SurfData{
        public ComputeBuffer Memory;
        public ComputeBuffer Addresses;
        public uint addressIndex;

        public SurfData(ComputeBuffer memory, ComputeBuffer addresses, uint addressIndex){
            this.Memory = memory;
            this.Addresses = addresses;
            this.addressIndex = addressIndex;
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
