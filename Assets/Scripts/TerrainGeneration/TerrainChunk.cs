using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor.MPE;
using UnityEngine;
using Utils;
using static EndlessTerrain;

public class TerrainChunk
{
    GameObject meshObject;
    Vector3 position;
    Vector3 CCoord;
    Bounds bounds;

    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    MeshCollider meshCollider;
    MeshCreator meshCreator;
    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;

    readonly LODMesh LODMeshHandle;
    readonly LODInfo[] detailLevels;

    float[] storedDensity = null;
    int[] storedMaterial = null;
    bool hasDensityMap = false;
    public bool active = true;

    float IsoLevel;
    int prevLODInd = -1;

    public TerrainChunk(Vector3 coord, float IsoLevel, Transform parent, SurfaceChunk surfaceChunk,  LODInfo[] detailLevels, GenerationResources generation)
    {
        CCoord = coord;
        position = coord * mapChunkSize - Vector3.one * (mapChunkSize / 2f); //Shift mesh so it is aligned with center
        bounds = new Bounds(position, Vector3.one * mapChunkSize);  
        this.IsoLevel = IsoLevel;
        this.detailLevels = detailLevels;
        this.meshCreator = UnityEngine.Object.Instantiate(generation.meshCreator);

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.position = position * lerpScale;
        meshObject.transform.localScale = Vector3.one * lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshCollider = meshObject.AddComponent<MeshCollider>();
        geoShaders = meshObject.AddComponent<ShaderGenerator>();
        meshReadback = meshObject.AddComponent<AsyncMeshReadback>();
        meshRenderer.material = generation.mapMaterial;

        Bounds boundsOS = new Bounds(Vector3.one * (mapChunkSize / 2), Vector3.one * mapChunkSize);
        geoShaders.Initialize(generation.geoSettings, boundsOS);
        meshReadback.Initialize(generation.readbackSettings, boundsOS, new MeshFilter[] {meshFilter});

        LODMeshHandle = new LODMesh(meshCreator, this.geoShaders, meshReadback, generation.densityDict, surfaceChunk.baseMap, detailLevels, this.position, this.CCoord, IsoLevel);
        
        //Plan Structures
        timeRequestQueue.Enqueue(() => processEvent(() => meshCreator.PlanStructuresGPU(this.CCoord, this.position, mapChunkSize, IsoLevel)), (int)Utils.priorities.structure);

        Update();
    }

    public void TerraformChunk(Vector3 targetPosition, float terraformRadius, Func<Vector2, float, Vector2> handleTerraform)
    {
        if (!hasDensityMap)
        {
            SurfaceChunk.SurfaceMap maxSurfaceData = LODMeshHandle.surfaceMap.SimplifyMap(0);
            (storedDensity, storedMaterial) = meshCreator.GetChunkInfo(maxSurfaceData, this.position, this.CCoord, IsoLevel, mapChunkSize);
            maxSurfaceData.Release();
            
            hasDensityMap = true;
        }

        float worldScale = lerpScale;

        Vector3 targetPointLocal = meshObject.transform.worldToLocalMatrix.MultiplyPoint3x4(targetPosition);
        int closestX = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.x), mapChunkSize));
        int closestY = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.y), mapChunkSize));
        int closestZ = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.z), mapChunkSize));
        int localRadius = Mathf.CeilToInt((1.000f / worldScale) * terraformRadius);

        for (int x = -localRadius; x <= localRadius; x++)
        {
            for (int y = -localRadius; y <= localRadius; y++)
            {
                for (int z = -localRadius; z <= localRadius; z++)
                {
                    int3 vertPosition = new(closestX + x, closestY + y, closestZ + z);
                    if (Mathf.Max(vertPosition.x, vertPosition.y, vertPosition.z) > mapChunkSize)
                        continue;
                    if (Mathf.Min(vertPosition.x, vertPosition.y, vertPosition.z) < 0)
                        continue;

                    int index = CustomUtility.indexFromCoord(vertPosition.x, vertPosition.y, vertPosition.z, mapChunkSize + 1);

                    Vector3 dR = new Vector3(vertPosition.x, vertPosition.y, vertPosition.z) - targetPointLocal;
                    float sqrDistWS = worldScale * (dR.x * dR.x + dR.y * dR.y + dR.z * dR.z);

                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);

                    Vector2 ret = handleTerraform(new Vector2(storedMaterial[index], storedDensity[index]), brushStrength);
                    storedMaterial[index] = (int)ret.x;
                    storedDensity[index] = ret.y;
                }
            }
        }


        LODMeshHandle.depreceated = true;
        Update();
    }
    public void Update()
    {
        float closestDist = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
        
        int lodInd = 0;
        for (int i = 0; i < detailLevels.Length - 1; i++)
        {
            if (closestDist > detailLevels[i].distanceThresh)
                lodInd = i + 1;
            else
                break;
        }

        //Have to regenerate everytime because GPU can't store too many buffers
        if (lodInd != prevLODInd || LODMeshHandle.depreceated)
        {
            if (!LODMeshHandle.hasRequestedChunk)
            {
                prevLODInd = lodInd;
                LODMeshHandle.hasRequestedChunk = true;

                if (hasDensityMap)
                    timeRequestQueue.Enqueue(() => processEvent(() => LODMeshHandle.ComputeChunk(lodInd, ref storedDensity, ref storedMaterial, () => onChunkCreated(lodInd)), lodInd), (int)Utils.priorities.generation);
                else { 
                    timeRequestQueue.Enqueue(() => processEvent(() => LODMeshHandle.GenerateMap(lodInd), lodInd), (int)priorities.generation);
                    timeRequestQueue.Enqueue(() => processEvent(() => LODMeshHandle.CreateMesh(lodInd, () => onChunkCreated(lodInd)), lodInd), (int)priorities.mesh);
                }
            }
        }
        lastUpdateTerrainChunks.Enqueue(this);
    }

    public void processEvent(Action callback, int LOD = -1)
    {
        if (!active)
            return;
        if (LOD != -1 && LOD != prevLODInd)
            return;
        
        callback();
    }

    public void onChunkCreated(int lodInd)
    {
        LODMeshHandle.depreceated = false;

        meshFilter.mesh = LODMeshHandle.baseMesh;
        if (detailLevels[lodInd].useForCollider)
            meshCollider.sharedMesh = LODMeshHandle.baseMesh;
        if (!detailLevels[lodInd].useForGeoShaders)
            geoShaders.ReleaseGeometry();


        if (LODMeshHandle.depreceated) //was depreceated while chunk was regenerating
            timeRequestQueue.Enqueue(() => processEvent(Update), (int)Utils.priorities.generation);
        
    }

    public void UpdateVisibility(Vector3 CCCoord, float maxRenderDistance)
    {
        Vector3 distance = CCoord - CCCoord;
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y), Mathf.Abs(distance.z)) <= maxRenderDistance;

        if(!visible)
            DestroyChunk();
    }

    //We destroy the chunk to preserve RAM in both dictionary and scene
    public void DestroyChunk()
    {
        if (!active)
            return;

        active = false;

        //If currently Generating chunk (It's doubtful if this ever does anything)
        meshCreator.ReleaseTempBuffers();
        terrainChunkDict.Remove(CCoord);
#if UNITY_EDITOR
        GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
    }
}