using System;
using UnityEngine;
using Unity.Collections;
using static EndlessTerrain;
using Unity.Mathematics;
using System.Threading;
using Utils;

public struct LODMesh
{
    TerrainChunk terrainChunk;
    SurfaceChunk.BaseMap surfaceMap;
    LODInfo[] detailLevels;

    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;
    MeshCreator meshCreator;
    StructureCreator structCreator;
    Action clearMesh;
    float IsoLevel;

    public LODMesh(TerrainChunk terrainChunk, GenerationResources generation, LODInfo[] detailLevels, float IsoLevel, Action clearMesh)
    {
        this.terrainChunk = terrainChunk;
        this.meshCreator = new MeshCreator(generation.meshCreator);
        this.structCreator = new StructureCreator(generation.meshCreator, generation.surfaceSettings);
        this.geoShaders = new ShaderGenerator(generation.geoSettings, terrainChunk.meshObject.transform, terrainChunk.boundsOS);
        this.meshReadback = new AsyncMeshReadback(generation.readbackSettings, terrainChunk.meshObject.transform, terrainChunk.boundsOS);
        this.surfaceMap = terrainChunk.surfaceMap;

        this.detailLevels = detailLevels;
        this.clearMesh = clearMesh;

        this.IsoLevel = IsoLevel;
    }

    public void PlanStructures(Action callback = null){
        structCreator.PlanStructuresGPU(this.terrainChunk.CCoord, this.terrainChunk.origin, mapChunkSize, IsoLevel);
        callback?.Invoke();
    }

    public void ReadMapData(int LoD, Action callback){ 
        LODMesh handle = this;

        //This code will be called on a background thread
        ChunkStorageManager.ReadChunkBin(this.terrainChunk.CCoord, LoD, (bool isComplete, CPUDensityManager.MapData[] chunk) => 
            timeRequestQueue.Enqueue(new GenTask{
                valid = () => handle.terrainChunk.active,
                task = () => OnReadComplete(isComplete, chunk),
                load = taskLoadTable[(int)priorities.generation],
            }));

        //This code will run on main thread
        void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk){
            if(!isComplete) handle.GenerateMap(LoD, callback);
            else handle.SetChunkData(LoD, 0, chunk, callback);
        }
    }
    public void SimplifyMap(int LOD, Action callback = null){
        GPUDensityManager.SimplifyChunk(this.terrainChunk.CCoord, LOD);
        callback?.Invoke();
    }

    void CopyDataToCPU(){
        int3 CCoord = this.terrainChunk.CCoord;
        CPUDensityManager.AllocateChunk(terrainChunk, CCoord, 
        (bool isComplete) => CPUDensityManager.BeginMapReadback(CCoord));
    }

    public void GenerateMap(int LOD, Action callback = null)
    {
        SurfaceChunk.SurfData surfData = surfaceMap.GetMap();

        meshCreator.GenerateBaseChunk(surfData, this.terrainChunk.origin, LOD, mapChunkSize, IsoLevel);
        structCreator.GenerateStrucutresGPU(mapChunkSize, LOD, IsoLevel);
        GPUDensityManager.SubscribeChunk(this.terrainChunk.CCoord, LOD, UtilityBuffers.GenerationBuffer);
        
        if(LOD == 0) CopyDataToCPU();
        
        meshCreator.ReleaseTempBuffers();
        callback?.Invoke();
    }

    public void CreateMesh(int LOD, Action<AsyncMeshReadback.SharedMeshInfo> UpdateCallback = null){
        meshCreator.GenerateMapData(this.terrainChunk.CCoord, IsoLevel, LOD, mapChunkSize);
        clearMesh();

        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        meshReadback.OffloadVerticesToGPU(bufferOffsets);
        meshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, bufferOffsets.dictStart, (int)ReadbackMaterial.terrain);
        meshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, bufferOffsets.dictStart, (int)ReadbackMaterial.water);
        meshReadback.BeginMeshReadback(UpdateCallback);

        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(meshReadback.settings.memoryBuffer, meshReadback.vertexHandle, meshReadback.triHandles[(int)ReadbackMaterial.terrain]);
        else
            geoShaders.ReleaseGeometry();

        meshCreator.ReleaseTempBuffers();
    }

    public void SetChunkData(int LOD, int offset, NativeArray<CPUDensityManager.MapData> mapData, Action callback = null){
        meshCreator.SetMapInfo(LOD, mapChunkSize, offset, ref mapData);
        GPUDensityManager.SubscribeChunk(this.terrainChunk.CCoord, LOD, UtilityBuffers.TransferBuffer, true);

        meshCreator.ReleaseTempBuffers();
        callback?.Invoke();
    }

    public void SetChunkData(int LOD, int offset, CPUDensityManager.MapData[] mapData, Action callback = null){
        meshCreator.SetMapInfo(LOD, mapChunkSize, offset, mapData);
        GPUDensityManager.SubscribeChunk(this.terrainChunk.CCoord, LOD, UtilityBuffers.TransferBuffer, true);
        if(LOD == 0) CopyDataToCPU();

        meshCreator.ReleaseTempBuffers();
        callback?.Invoke();
    }

    public void Release(){
        geoShaders.ReleaseGeometry(); //Release geoShader Geometry
        meshReadback.ReleaseAllGeometry(); //Release base geometry on GPU
        structCreator.ReleaseStructure(); //Release structure data
    }
}

public enum ReadbackMaterial{
    terrain = 0,
    water = 1,
}

[System.Serializable]
public struct LODInfo
{
    public int LOD;
    public float distanceThresh;
    public bool useForCollider;
    public bool useForGeoShaders;
}
