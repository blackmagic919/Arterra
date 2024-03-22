using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;

public class LODMesh
{
    public SurfaceChunk.BaseMap surfaceMap;
    public LODInfo[] detailLevels;
    public Vector3 position;
    public Vector3 CCoord;

    public Mesh baseMesh;
    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;
    GPUDensityManager densityManager;

    float IsoLevel;
    MeshCreator meshCreator;
    StructureCreator structCreator;

    public bool hasRequestedChunk = false;
    public bool depreceated = false;

    public LODMesh(MeshCreator meshCreator, StructureCreator structCreator, ShaderGenerator geoShaders, AsyncMeshReadback meshReadback, 
                GPUDensityManager densityManager, SurfaceChunk.BaseMap surfaceMap, LODInfo[] detailLevels, Vector3 position, Vector3 CCoord, float IsoLevel)
    {
        this.meshCreator = meshCreator;
        this.structCreator = structCreator;
        this.meshReadback = meshReadback;
        this.densityManager = densityManager;
        this.geoShaders = geoShaders;
        this.surfaceMap = surfaceMap;

        this.detailLevels = detailLevels;
        this.position = position;
        this.CCoord = CCoord;

        this.IsoLevel = IsoLevel;
    }

    public void GenerateMap(int LOD)
    {
        SurfaceChunk.SurfData surfData = surfaceMap.GetMap();
        ComputeBuffer baseMap = meshCreator.GenerateBaseChunk(surfData, this.position, LOD, mapChunkSize, IsoLevel);
        structCreator.GenerateStrucutresGPU(baseMap, mapChunkSize, LOD, IsoLevel);
        densityManager.SubscribeChunk(baseMap, CCoord, LOD);
        
        meshCreator.ReleaseTempBuffers();
    }

    public void CreateMesh(int LOD, Action UpdateCallback){
        ComputeBuffer sourceMesh = meshCreator.GenerateMapData(densityManager, this.CCoord, IsoLevel, LOD, mapChunkSize);
        meshReadback.OffloadMeshToGPU(sourceMesh, (int)ReadbackMaterial.terrain);
        if(detailLevels[LOD].useForCollider)
            meshReadback.BeginMeshReadback((int)ReadbackMaterial.terrain, ret => onMeshInfoRecieved(ret, UpdateCallback));
        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(sourceMesh, mapChunkSize, LOD);
        else
            geoShaders.ReleaseGeometry();

        meshCreator.ReleaseTempBuffers();
        hasRequestedChunk = false;
    }

    void onMeshInfoRecieved(MeshInfo meshInfo, Action UpdateCallback)
    {
        this.baseMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
        UpdateCallback();
    }

    public void ComputeChunk(int LOD, ref TerrainChunk.MapData[] chunkData, Action UpdateCallback)
    {
        ComputeBuffer chunkMap = meshCreator.SetMapInfo(LOD, mapChunkSize, chunkData);
        densityManager.SubscribeChunk(chunkMap, CCoord, LOD);
        
        ComputeBuffer sourceMesh = meshCreator.GenerateMapData(densityManager, this.CCoord, IsoLevel, LOD, mapChunkSize);

        meshReadback.OffloadMeshToGPU(sourceMesh, (int)ReadbackMaterial.terrain);
        if(detailLevels[LOD].useForCollider)
            meshReadback.BeginMeshReadback((int)ReadbackMaterial.terrain, ret => onMeshInfoRecieved(ret, UpdateCallback));
        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(sourceMesh, mapChunkSize, LOD);
        else
            geoShaders.ReleaseGeometry();

        meshCreator.ReleaseTempBuffers();
        hasRequestedChunk = false;
    }
}

public enum ReadbackMaterial
{
    terrain = 0,
    water = 1, //To be implemented
}

[System.Serializable]
public struct LODInfo
{
    public int LOD;
    public float distanceThresh;
    public bool useForCollider;
    public bool useForGeoShaders;
}
