using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using Utils;


public class SurfaceChunk
{
    public BaseMap baseMap;

    Vector2 SCoord;
    Vector2 position;
    bool active = true;

    // Start is called before the first frame update
    public SurfaceChunk(MapCreator mapCreator, Vector2 coord)
    {
        this.SCoord = coord;
        this.position = coord * mapChunkSize - Vector2.one * (mapChunkSize / 2f);
        baseMap = new BaseMap(mapCreator, position);

        CreateChunk(0);
        Update();
    }

    public void Update()
    {
        lastUpdateSurfaceChunks.Enqueue(this);
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

    public void CreateChunk(int lodInd)
    {
        if (baseMap.hasChunk)
            return;
        
        if (!baseMap.hasRequestedChunk)
        {
            baseMap.hasRequestedChunk = true;
            timeRequestQueue.Enqueue(() => baseMap.GetChunk(), (int)priorities.planning);
        }
    }

    public void UpdateVisibility(Vector2 CSCoord, float maxRenderDistance)
    {
        Vector2 distance = SCoord - CSCoord;
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y)) <= maxRenderDistance;

        if (!visible)
            DestroyChunk();
    }

    public void DestroyChunk()
    {
        if (!active)
            return;

        active = false;

        baseMap.surfaceMap.Release();
        surfaceChunkDict.Remove(SCoord);
    }

    public class BaseMap
    {
        MapCreator mapCreator;
        NoiseMaps noiseMaps;

        //Return values--height map and squash map
        public SurfaceMap surfaceMap;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        Vector2 position;

        public BaseMap(MapCreator mapCreator, Vector2 position){
            this.mapCreator = mapCreator;
            this.position = position;

            noiseMaps = new NoiseMaps();
        }

        public void GetChunk()
        {
            ComputeBuffer heightMap = mapCreator.GenerateTerrainMaps(mapChunkSize, 0, position, out noiseMaps.continental, out noiseMaps.erosion, out noiseMaps.pvNoise);
            ComputeBuffer squashMap = mapCreator.GenerateSquashMap(mapChunkSize, 0, position, out noiseMaps.squash);
            mapCreator.GetBiomeNoises(mapChunkSize, 0, position, out noiseMaps.temperature, out noiseMaps.humidity);
            ComputeBuffer biomeMap = mapCreator.ConstructBiomes(mapChunkSize, 0, ref noiseMaps);
            mapCreator.ReleaseTempBuffers();

            this.surfaceMap = new SurfaceMap(heightMap, squashMap, biomeMap);
            hasChunk = true;
        }

        public SurfaceMap SimplifyMap(int LOD)
        {
            if(LOD == 0) //If base, just return the calculated map
                return new SurfaceMap(surfaceMap.heightMap, surfaceMap.squashMap, surfaceMap.biomeMap, false);
            
            ComputeBuffer heightMap = mapCreator.SimplifyMap(this.surfaceMap.heightMap, 0, LOD, mapChunkSize, true);
            ComputeBuffer squashMap = mapCreator.SimplifyMap(this.surfaceMap.squashMap, 0, LOD, mapChunkSize, true);
            ComputeBuffer biomeMap = mapCreator.SimplifyMap(this.surfaceMap.biomeMap, 0, LOD, mapChunkSize, false);
            return new SurfaceMap(heightMap, squashMap, biomeMap);
        }
    }

    public struct SurfaceMap{
        public ComputeBuffer heightMap;
        public ComputeBuffer squashMap;
        public ComputeBuffer biomeMap;
        public Queue<ComputeBuffer> bufferHandle;

        public SurfaceMap(ComputeBuffer heightMap, ComputeBuffer squashMap, ComputeBuffer biomeMap, bool handle = true){
            this.heightMap = heightMap;
            this.squashMap = squashMap;
            this.biomeMap = biomeMap;

            this.bufferHandle = new Queue<ComputeBuffer>();
            if(handle){bufferHandle.Enqueue(heightMap); bufferHandle.Enqueue(squashMap); bufferHandle.Enqueue(biomeMap);}
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
        public ComputeBuffer temperature;
        public ComputeBuffer humidity;
    }
}
