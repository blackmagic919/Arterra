using System;
using UnityEngine;
using static EndlessTerrain;

public class LODMesh
{
    public SurfaceChunk.BaseMap surfaceMap;
    public LODInfo[] detailLevels;
    public Vector3 position;
    public Vector3 CCoord;

    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;
    GPUDensityManager densityManager;

    float IsoLevel;
    MeshCreator meshCreator;
    StructureCreator structCreator;

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
        meshCreator.GenerateBaseChunk(surfData, this.position, LOD, mapChunkSize, IsoLevel);
        structCreator.GenerateStrucutresGPU(mapChunkSize, LOD, IsoLevel);
        densityManager.SubscribeChunk(CCoord, LOD);
        
        meshCreator.ReleaseTempBuffers();
    }

    public void CreateMesh(int LOD, Action<MeshInfo, int> UpdateCallback){
        meshCreator.GenerateMapData(densityManager, this.CCoord, IsoLevel, LOD, mapChunkSize);

        meshReadback.OffloadVerticesToGPU(DensityGenerator.countInd.x, DensityGenerator.vertexStart_U);
        meshReadback.OffloadTrisToGPU(DensityGenerator.countInd.y, DensityGenerator.baseTriStart_U, DensityGenerator.dictStart_U, (int)ReadbackMaterial.terrain);
        meshReadback.OffloadTrisToGPU(DensityGenerator.countInd.z, DensityGenerator.waterTriStart_U, DensityGenerator.dictStart_U, (int)ReadbackMaterial.water);
        meshReadback.BeginMeshReadback(ret => UpdateCallback(ret, LOD));

        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(meshReadback.settings.memoryBuffer, meshReadback.vertexHandle, meshReadback.triHandles[(int)ReadbackMaterial.terrain]);
        else
            geoShaders.ReleaseGeometry();

        meshCreator.ReleaseTempBuffers();
    }

    public void SetChunkData(int LOD, ref TerrainChunk.MapData[] chunkData){
        meshCreator.SetMapInfo(LOD, mapChunkSize, chunkData);
        densityManager.SubscribeChunk(CCoord, LOD);

        meshCreator.ReleaseTempBuffers();
    }

    public void ComputeChunk(int LOD, Action<MeshInfo, int>  UpdateCallback){
        meshCreator.GenerateMapData(densityManager, this.CCoord, IsoLevel, LOD, mapChunkSize);
        
        meshReadback.OffloadVerticesToGPU(DensityGenerator.countInd.x, DensityGenerator.vertexStart_U);
        meshReadback.OffloadTrisToGPU(DensityGenerator.countInd.y, DensityGenerator.baseTriStart_U, DensityGenerator.dictStart_U, (int)ReadbackMaterial.terrain);
        meshReadback.OffloadTrisToGPU(DensityGenerator.countInd.z, DensityGenerator.waterTriStart_U, DensityGenerator.dictStart_U, (int)ReadbackMaterial.water);
        meshReadback.BeginMeshReadback(ret => UpdateCallback(ret, LOD));

        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(meshReadback.settings.memoryBuffer, meshReadback.vertexHandle, meshReadback.triHandles[(int)ReadbackMaterial.terrain]);
        else
            geoShaders.ReleaseGeometry();

        meshCreator.ReleaseTempBuffers();
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
