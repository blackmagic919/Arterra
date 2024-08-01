using System;
using UnityEngine;
using Unity.Collections;
using static EndlessTerrain;
using Unity.Mathematics;
using System.Threading;
using Utils;
using System.Collections.Generic;

public struct LODMesh
{
    TerrainChunk terrainChunk;
    SurfaceChunk.BaseMap surfaceMap;
    List<LODInfo> detailLevels;

    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;
    MeshCreator meshCreator;
    StructureCreator structCreator;
    Action clearMesh;
    float IsoLevel;
    int mapChunkSize;

    public LODMesh(TerrainChunk terrainChunk, List<LODInfo> detailLevels, Action clearMesh)
    {
        this.terrainChunk = terrainChunk;
        this.meshCreator = new MeshCreator();
        this.structCreator = new StructureCreator();
        this.geoShaders = new ShaderGenerator(terrainChunk.meshObject.transform, terrainChunk.boundsOS);
        this.meshReadback = new AsyncMeshReadback(terrainChunk.meshObject.transform, terrainChunk.boundsOS);
        this.surfaceMap = terrainChunk.surfaceMap;

        this.detailLevels = detailLevels;
        this.clearMesh = clearMesh;

        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Rendering.value;
        this.IsoLevel = rSettings.IsoLevel;
        this.mapChunkSize = rSettings.mapChunkSize;
    }

    public void PlanStructures(Action callback = null){
        structCreator.PlanStructuresGPU(this.terrainChunk.CCoord, this.terrainChunk.origin, mapChunkSize, IsoLevel);
        callback?.Invoke();
    }

    public void ReadMapData(int LoD, Action callback){ 
        LODMesh handle = this;

        //This code will be called on a background thread
        ChunkStorageManager.ReadChunkBin(this.terrainChunk.CCoord, LoD, (bool isComplete, CPUDensityManager.MapData[] chunk) => 
            RequestQueue.Enqueue(new GenTask{ //REMINDER: This queue should be locked
                valid = () => handle.terrainChunk.active,
                task = () => OnReadComplete(isComplete, chunk),
                load = taskLoadTable[(int)priorities.generation],
            })
        );

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

    void CopyMapToCPU(){
        int3 CCoord = this.terrainChunk.CCoord;
        uint entityAddress = EntityManager.PlanEntities(surfaceMap.GetMap(), this.terrainChunk.CCoord, mapChunkSize);
        EntityManager.BeginEntityReadback(entityAddress, this.terrainChunk.CCoord);
        CPUDensityManager.AllocateChunk(terrainChunk, CCoord, 
        (bool isComplete) => CPUDensityManager.BeginMapReadback(CCoord));
    }


    public void GenerateMap(int LOD, Action callback = null)
    {
        meshCreator.GenerateBaseChunk(surfaceMap.GetMap(), this.terrainChunk.origin, LOD, mapChunkSize, IsoLevel);
        structCreator.GenerateStrucutresGPU(mapChunkSize, LOD, IsoLevel);
        GPUDensityManager.SubscribeChunk(this.terrainChunk.CCoord, LOD, UtilityBuffers.GenerationBuffer);
        if(LOD == 0) CopyMapToCPU();
        
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
            geoShaders.ComputeGeoShaderGeometry(meshReadback.vertexHandle, meshReadback.triHandles[(int)ReadbackMaterial.terrain]);
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
        if(LOD == 0) CopyMapToCPU();

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
    public int chunkDistThresh;
    public bool useForGeoShaders;
}
