using System;
using UnityEngine;
using static EndlessTerrain;
using Utils;
using static SurfaceChunk;

public class SurfaceChunk
{
    
    public LODMap[] LODMaps;
    LODInfo[] detailLevels;
    MapCreator mapCreator;

    Vector2 SCoord;
    Vector2 position;
    bool active = true;

    // Start is called before the first frame update
    public SurfaceChunk(MapCreator mapCreator, Vector2 coord, LODInfo[] detailLevels)
    {
        this.SCoord = coord;
        this.position = (coord * mapChunkSize - Vector2.one * (mapChunkSize / 2f));
        this.detailLevels = detailLevels;
        this.mapCreator = mapCreator;

        LODMaps = new LODMap[detailLevels.Length];
        for (int i = 0; i < detailLevels.Length; i++)
        {
            LODMaps[i] = new LODMap(mapCreator, this.position, detailLevels[i].LOD);
        }

        CreateChunk(0, LODMaps[0]);
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

    public void CreateChunk(int lodInd, LODMap lodMap)
    {
        if (lodMap.hasChunk)
        {
            PropogateDetail(lodMap, lodInd+1);
        }
        else if (!lodMap.hasRequestedChunk)
        {
            lodMap.hasRequestedChunk = true;
            timeRequestQueue.Enqueue(() => lodMap.GetChunk(() => CreateChunk(lodInd, lodMap)), (int)priorities.planning);
        }
    }

    public void PropogateDetail(LODMap baseLOD, int lodInd)
    {
        if (lodInd >= detailLevels.Length)
            return;

        LODMap lodMap = LODMaps[lodInd];
        if (lodMap.hasChunk)
            return;
        else
            timeRequestQueue.Enqueue(() => lodMap.SimplifyMap(baseLOD, () => PropogateDetail(baseLOD, lodInd + 1)), (int)priorities.planning);
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

        mapCreator.ReleasePersistantBuffers();

        surfaceChunkDict.Remove(SCoord);
    }

    public class LODMap
    {
        MapCreator mapCreator;
        NoiseMaps noiseMaps;

        //Return values--height map and squash map
        public ComputeBuffer heightMap = default;
        public ComputeBuffer squashMap = default;
        public ComputeBuffer biomeMap = default;

        public bool hasChunk = false;
        public bool hasRequestedChunk = false;

        Vector2 position;
        int LOD;

        public LODMap(MapCreator mapCreator, Vector2 position, int LOD){
            this.mapCreator = mapCreator;
            this.position = position;
            this.LOD = LOD;

            noiseMaps = new NoiseMaps();
        }

        public void GetChunk(Action UpdateCallback)
        {
            this.heightMap = mapCreator.GenerateTerrainMaps(mapChunkSize, LOD, position, out noiseMaps.continental, out noiseMaps.erosion, out noiseMaps.pvNoise);
            this.squashMap = mapCreator.GenerateSquashMap(mapChunkSize, LOD, position, out noiseMaps.squash);
            mapCreator.GetBiomeNoises(mapChunkSize, LOD, position, out noiseMaps.temperature, out noiseMaps.humidity);
            this.biomeMap = mapCreator.ConstructBiomes(mapChunkSize, LOD, ref noiseMaps);
            mapCreator.ReleaseTempBuffers();

            hasChunk = true;
            UpdateCallback();
        }

        public void SimplifyMap(LODMap highDetailMap, Action UpdateCallback)
        {
            this.heightMap = mapCreator.SimplifyMap(highDetailMap.heightMap, highDetailMap.LOD, LOD, mapChunkSize, true);
            this.squashMap = mapCreator.SimplifyMap(highDetailMap.squashMap, highDetailMap.LOD, LOD, mapChunkSize, true);
            this.biomeMap = mapCreator.SimplifyMap(highDetailMap.biomeMap, highDetailMap.LOD, LOD, mapChunkSize, false);

            hasChunk = true;
            UpdateCallback();
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
