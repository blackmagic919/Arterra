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
        uint heightMapAddress;
        uint squashMapAddress;
        uint biomeMapAddress;
        uint atmosphereMapAddress;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        Vector2 position;

        public BaseMap(SurfaceCreatorSettings mapCreator, Vector2 position){
            this.mapCreator = new SurfaceCreator(mapCreator);
            this.position = position;
        }

        public void GetChunk()
        {
            SurfaceMap surfaceData = mapCreator.SampleSurfaceMaps(position, mapChunkSize, 0);
            
            this.heightMapAddress = mapCreator.StoreSurfaceMap(surfaceData.heightMap, mapChunkSize, 0, true);
            this.squashMapAddress = mapCreator.StoreSurfaceMap(surfaceData.squashMap, mapChunkSize, 0, true);
            this.biomeMapAddress = mapCreator.StoreSurfaceMap(surfaceData.biomeMap, mapChunkSize, 0, false);
            this.atmosphereMapAddress = mapCreator.StoreSurfaceMap(surfaceData.atmosphereMap, mapChunkSize, 0, true);

            mapCreator.ReleaseTempBuffers();
            hasChunk = true;
        }

        public SurfaceMap SimplifyMap(int LOD)
        {  
            ComputeBuffer heightMap = mapCreator.SimplifyMap((int)this.heightMapAddress, 0, LOD, mapChunkSize, true);
            ComputeBuffer squashMap = mapCreator.SimplifyMap((int)this.squashMapAddress, 0, LOD, mapChunkSize, true);
            ComputeBuffer biomeMap = mapCreator.SimplifyMap((int)this.biomeMapAddress, 0, LOD, mapChunkSize, false);
            ComputeBuffer atmosphereMap = mapCreator.SimplifyMap((int)this.atmosphereMapAddress, 0, LOD, mapChunkSize, true);
            return new SurfaceMap(heightMap, squashMap, biomeMap, atmosphereMap);
        }

        public void ReleaseSurfaceMap()
        {
            if(!hasChunk)
                return;
            
            mapCreator.settings.surfaceMemoryBuffer.ReleaseMemory(this.heightMapAddress);
            mapCreator.settings.surfaceMemoryBuffer.ReleaseMemory(this.squashMapAddress);
            mapCreator.settings.surfaceMemoryBuffer.ReleaseMemory(this.biomeMapAddress);
        }
    }

    public struct SurfaceMap{
        public ComputeBuffer heightMap;
        public ComputeBuffer squashMap;
        public ComputeBuffer biomeMap;
        public ComputeBuffer atmosphereMap;
        public Queue<ComputeBuffer> bufferHandle;

        public SurfaceMap(ComputeBuffer heightMap, ComputeBuffer squashMap, ComputeBuffer biomeMap, ComputeBuffer atmosphereMap, bool handle = true){
            this.heightMap = heightMap;
            this.squashMap = squashMap;
            this.biomeMap = biomeMap;
            this.atmosphereMap = atmosphereMap;

            this.bufferHandle = new Queue<ComputeBuffer>();
            if(handle){bufferHandle.Enqueue(heightMap); bufferHandle.Enqueue(squashMap); bufferHandle.Enqueue(biomeMap); bufferHandle.Enqueue(atmosphereMap);}
        }

        public void Release(){
            while(bufferHandle.Count > 0)
                bufferHandle.Dequeue().Release();
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
