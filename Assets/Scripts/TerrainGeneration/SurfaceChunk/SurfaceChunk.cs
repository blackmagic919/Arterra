using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using Utils;
using Unity.Mathematics;

public class SurfaceChunk
{
    public BaseMap baseMap;

    int2 SCoord;
    Vector2 position;
    bool active = true;

    // Start is called before the first frame update
    public SurfaceChunk(int2 coord)
    {
        this.SCoord = coord;
        RecreateChunk();
    }

    private uint ChunkDist2D(float2 GPos){
        int2 GCoord = (int2)GPos;
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        float2 cPt = math.clamp(GCoord, SCoord * rSettings.mapChunkSize, (SCoord + 1) * rSettings.mapChunkSize);
        float2 cDist = math.abs(math.floor((cPt - GCoord) / rSettings.mapChunkSize + 0.5f));
        //We add 0.5 because normally this returns an odd number, but even numbers have better cubes
        return (uint)math.max(cDist.x, cDist.y);
    }

    public void ValidateChunk(){
        float closestDist = ChunkDist2D(((float3)viewerPosition).xz);

        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        if(closestDist >= rSettings.detailLevels.value[^1].chunkDistThresh) {
            RecreateChunk();
            return;
        };
    }

    private void RecreateChunk(){
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        int maxChunkDist = rSettings.detailLevels.value[^1].chunkDistThresh;
        int numChunksAxis = maxChunkDist * 2;
        
        int2 vGCoord = (int2)((float3)viewerPosition).xz;
        int2 vMCoord = ((vGCoord % rSettings.mapChunkSize) + rSettings.mapChunkSize) % rSettings.mapChunkSize;
        int2 vOCoord = (vGCoord - vMCoord) / rSettings.mapChunkSize - maxChunkDist + math.select(new int2(0), new int2(1), vMCoord > (rSettings.mapChunkSize / 2)); 
        int2 HCoord = (((SCoord - vOCoord) % numChunksAxis) + numChunksAxis) % numChunksAxis;
        SCoord = vOCoord + HCoord;

        baseMap?.ReleaseSurfaceMap();
        this.position = CustomUtility.AsVector(SCoord) * rSettings.mapChunkSize - Vector2.one * (rSettings.mapChunkSize / 2f);
        baseMap = new BaseMap(position);

        CreateBaseChunk();
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
        EndlessTerrain.GenTask surfChunkTask = new EndlessTerrain.GenTask{
            task = () => baseMap.GetChunk(), 
            id = (int)priorities.planning
        };
        RequestQueue.Enqueue(surfChunkTask);
    }

    public void DestroyChunk()
    {
        if (!active)
            return;

        active = false;
        baseMap.ReleaseSurfaceMap();
    }

    public class BaseMap
    {
        SurfaceCreator mapCreator;

        //Return values--height map and squash map
        uint surfAddress;

        public bool hasChunk = false;

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

}
