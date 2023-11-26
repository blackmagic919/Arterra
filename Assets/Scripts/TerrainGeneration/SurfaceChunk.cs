using System;
using UnityEngine;
using static EndlessTerrain;
using Utils;
using static SurfaceChunk;

public class SurfaceChunk
{
    
    public LODMap[] LODMaps;
    LODInfo[] detailLevels;

    Vector2 position;
    int prevLODInd = -1;
    // Start is called before the first frame update
    public SurfaceChunk(MapCreator mapCreator, Vector2 coord, LODInfo[] detailLevels)
    {
        this.position = (coord * mapChunkSize - Vector2.one * (mapChunkSize / 2f));
        this.detailLevels = detailLevels;

        LODMaps = new LODMap[detailLevels.Length];
        for (int i = 0; i < detailLevels.Length; i++)
        {
            LODMaps[i] = new LODMap(mapCreator, this.position, detailLevels[i].LOD);
        }

        CreateChunk(0, LODMaps[0]);
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
            prevLODInd = lodInd;
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

    public class LODMap
    {
        MapCreator mapCreator;
        NoiseMaps noiseMaps;

        //Return values--height map and squash map
        public float[] heightMap = default;
        public float[] squashMap = default;
        public int[] biomeMap = default;

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
            mapCreator.ReleaseBuffers();

            hasChunk = true;
            UpdateCallback();
        }

        public void SimplifyMap(LODMap highDetailMap, Action UpdateCallback)
        {
            this.heightMap = mapCreator.SimplifyMap(highDetailMap.heightMap, highDetailMap.LOD, LOD, mapChunkSize);
            this.squashMap = mapCreator.SimplifyMap(highDetailMap.squashMap, highDetailMap.LOD, LOD, mapChunkSize);
            this.biomeMap = mapCreator.SimplifyMap(highDetailMap.biomeMap, highDetailMap.LOD, LOD, mapChunkSize);

            hasChunk = true;
            UpdateCallback();
        }
    }

    public struct NoiseMaps
    {
        public float[] continental;
        public float[] erosion;
        public float[] pvNoise;
        public float[] squash;
        public float[] temperature;
        public float[] humidity;
    }
}
