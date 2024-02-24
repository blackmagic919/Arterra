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

    public bool hasRequestedChunk = false;
    public bool depreceated = false;

    public LODMesh(MeshCreator meshCreator, ShaderGenerator geoShaders, AsyncMeshReadback meshReadback, GPUDensityManager densityManager,
                   SurfaceChunk.BaseMap surfaceMap, LODInfo[] detailLevels, Vector3 position, Vector3 CCoord, float IsoLevel)
    {
        this.meshCreator = meshCreator;
        this.meshReadback = meshReadback;
        this.densityManager = densityManager;
        this.geoShaders = geoShaders;
        this.surfaceMap = surfaceMap;

        this.detailLevels = detailLevels;
        this.position = position;
        this.CCoord = CCoord;

        this.IsoLevel = IsoLevel;
    }

    public void GetChunk(int LOD, Action UpdateCallback)
    {
        SurfaceChunk.SurfaceMap surfaceLODMap = surfaceMap.SimplifyMap(LOD);

        ComputeBuffer density = meshCreator.GenerateDensity(surfaceLODMap, this.position, LOD, mapChunkSize, IsoLevel);
        ComputeBuffer material = meshCreator.GenerateMaterials(surfaceLODMap, this.position, LOD, mapChunkSize);
        densityManager.SubscribeChunk(density, material, CCoord, LOD);
        
        meshCreator.GenerateStrucutresGPU(density, material, mapChunkSize, LOD, IsoLevel);
        ComputeBuffer sourceMesh = meshCreator.GenerateMapData(density, material, IsoLevel, LOD, mapChunkSize);

        meshReadback.CreateReadbackMeshInfoTask(sourceMesh, (int)ReadbackMaterial.terrain, ret => onMeshInfoRecieved(ret, UpdateCallback));
        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(sourceMesh, mapChunkSize, LOD);
        
        surfaceLODMap.Release();
        meshCreator.ReleaseTempBuffers();
        hasRequestedChunk = false;

        UpdateCallback();
    }

    void onMeshInfoRecieved(EditorMesh.MeshInfo meshInfo, Action UpdateCallback)
    {
        this.baseMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt16);

        UpdateCallback();
    }

    public void ComputeChunk(int LOD, ref float[] density, ref int[] material, Action UpdateCallback)
    {
        ComputeBuffer densityBuffer; ComputeBuffer materialBuffer;
        (densityBuffer, materialBuffer) = meshCreator.SetMapInfo(LOD, mapChunkSize, density, material);
        densityManager.SubscribeChunk(densityBuffer, materialBuffer, CCoord, LOD);

        ComputeBuffer sourceMesh = meshCreator.GenerateMapData(densityBuffer, materialBuffer, IsoLevel, LOD, mapChunkSize);

        meshReadback.CreateReadbackMeshInfoTask(sourceMesh, (int)ReadbackMaterial.terrain, ret => onMeshInfoRecieved(ret, UpdateCallback));
        if (detailLevels[LOD].useForGeoShaders)
            geoShaders.ComputeGeoShaderGeometry(sourceMesh, mapChunkSize, LOD);

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
