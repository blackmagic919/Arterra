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
    SpecialShaderData[] shaders;

    readonly LODMesh[] LODMeshes;
    readonly LODInfo[] detailLevels;

    float[] storedDensity = null;
    int[] storedMaterial = null;
    bool hasDensityMap = false;
    public bool active = true;

    float IsoLevel;
    int prevLODInd = -1;

    public TerrainChunk(Vector3 coord, float IsoLevel, Transform parent, List<SpecialShaderData> specialShaders,
                        SurfaceChunk surfaceChunk, MeshCreator meshCreator, Material material, LODInfo[] detailLevels)
    {
        CCoord = coord;
        position = (coord * mapChunkSize - Vector3.one * (mapChunkSize / 2f)); //Shift mesh so it is aligned with center
        bounds = new Bounds(position, Vector3.one * mapChunkSize);  
        this.IsoLevel = IsoLevel;
        this.detailLevels = detailLevels;
        this.meshCreator = meshCreator;

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.position = position * lerpScale;
        meshObject.transform.localScale = Vector3.one * lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshCollider = meshObject.AddComponent<MeshCollider>();
        meshRenderer.material = material;

        shaders = new SpecialShaderData[specialShaders.Count];
        for(int i = 0; i < specialShaders.Count; i++)
        {
            SpecialShaderData shaderData = specialShaders[i];
            SpecialShaderData instantiatedShaderData = new SpecialShaderData(shaderData);

            instantiatedShaderData.shader = meshObject.AddComponent(shaderData.shader.GetType()) as SpecialShader;
            instantiatedShaderData.shader.SetSettings(shaderData.settings);

            shaders[i] = instantiatedShaderData;
        }

        LODMeshes = new LODMesh[detailLevels.Length];
        for (int i = 0; i < detailLevels.Length; i++)
        {
            SpecialShaderData[] specialMaterialIndexes = specialShaders.Where((e) => i <= e.detailLevel).ToArray();
            LODMeshes[i] = new LODMesh(meshCreator, specialMaterialIndexes, surfaceChunk.LODMaps[i], detailLevels[i].LOD, this.position, CCoord, IsoLevel);
        }
        
        //Plan Structures
        timeRequestQueue.Enqueue(() => processEvent(() => meshCreator.PlanStructuresGPU(this.CCoord, this.position, mapChunkSize, IsoLevel)), (int)Utils.priorities.structure);
        //timeRequestQueue.Enqueue(() => processEvent(() => meshCreator.PlanStructures(surfaceChunk.LODMaps[0], this.CCoord, this.position, mapChunkSize, IsoLevel)), (int)Utils.priorities.structure);

        Update();
    }

    public void TerraformChunk(Vector3 targetPosition, float terraformRadius, Func<Vector2, float, Vector2> handleTerraform)
    {
        SurfaceChunk.LODMap maxSurfaceData = LODMeshes[0].surfaceData;
        if (!hasDensityMap)
        {
            (storedDensity, storedMaterial) = meshCreator.GetChunkInfo(maxSurfaceData, this.position, this.CCoord, IsoLevel, mapChunkSize);
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


        foreach (LODMesh mesh in LODMeshes)
        {
            mesh.depreceated = true;
        }

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
        LODMesh lodMesh = LODMeshes[lodInd];
        if (lodInd != prevLODInd || lodMesh.depreceated)
        {
            if (lodMesh.hasChunk && !lodMesh.depreceated)
                onChunkCreated(lodInd, lodMesh);
            else if (!lodMesh.hasRequestedChunk)
            {
                lodMesh.hasRequestedChunk = true;

                if (hasDensityMap)
                    timeRequestQueue.Enqueue(() => processEvent(() => lodMesh.ComputeChunk(ref storedDensity, ref storedMaterial, () => onChunkCreated(lodInd, lodMesh))), (int)Utils.priorities.generation);
                else
                    timeRequestQueue.Enqueue(() => processEvent(() => lodMesh.GetChunk(() => onChunkCreated(lodInd, lodMesh))), (int)priorities.generation);
            }
        }
        lastUpdateTerrainChunks.Enqueue(this);
    }

    public void processEvent(Action callback)
    {
        if (!active)
            return;
        
        callback();
    }

    public void onChunkCreated(int lodInd, LODMesh lodMesh)
    {
        lodMesh.hasRequestedChunk = false;
        lodMesh.depreceated = false;
        prevLODInd = lodInd;
        
        meshFilter.mesh = lodMesh.mesh;
        if (detailLevels[lodInd].useForCollider)
            meshCollider.sharedMesh = lodMesh.mesh;
        
        for(int i = 0; i < shaders.Length; i++)
        {
            SpecialShaderData shaderData = shaders[i];

            if (lodMesh.specialMeshes.TryGetValue(shaders[i].materialIndex, out Mesh mesh))
            {
                shaderData.shader.SetMesh(mesh);
                shaderData.shader.Render();
            }
            else
                shaderData.shader.Release();
        }
        
        if (lodMesh.depreceated) //was depreceated while chunk was regenerating
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

        meshCreator.ReleasePersistantBuffers();
        terrainChunkDict.Remove(CCoord);
#if UNITY_EDITOR
        GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
    }
}

public class LODMesh
{
    public Mesh mesh;
    public Dictionary<int, Mesh> specialMeshes;
    public SpecialShaderData[] specialShaderData;
    public SurfaceChunk.LODMap surfaceData;
    public Vector3 position;

    EditorMesh.ChunkData chunkData;

    Vector3 CCoord;
    float IsoLevel;
    MeshCreator meshCreator;

    public bool hasChunk = false;
    public bool hasRequestedChunk = false;
    public bool depreceated = false;
    int LOD;

    //Temporary

    public LODMesh(MeshCreator meshCreator, SpecialShaderData[] specialShaderData, SurfaceChunk.LODMap surfaceData, int LOD, Vector3 position, Vector3 CCoord, float IsoLevel)
    {
        this.LOD = LOD;
        this.CCoord = CCoord;
        this.position = position;
        this.meshCreator = meshCreator;
        this.surfaceData = surfaceData;
        this.specialShaderData = specialShaderData;

        this.IsoLevel = IsoLevel;
    }


    public void GetChunk(Action UpdateCallback)
    {
        meshCreator.GenerateDensity(surfaceData, this.position, LOD, mapChunkSize, IsoLevel);
        //meshCreator.GenerateStructures(this.CCoord, IsoLevel, LOD, mapChunkSize);
        meshCreator.GenerateMaterials(surfaceData, this.position, LOD, mapChunkSize);
        meshCreator.GenerateStrucutresGPU(mapChunkSize, LOD, IsoLevel);

        this.chunkData = meshCreator.GenerateMapData(IsoLevel, LOD, mapChunkSize);
        this.specialMeshes = meshCreator.CreateSpecialMeshes(specialShaderData, chunkData.meshData);
        meshCreator.ReleaseTempBuffers();

        this.mesh = this.chunkData.GenerateMesh();

        hasChunk = true;
        UpdateCallback();
    }

    public void ComputeChunk(ref float[] density, ref int[] material, Action UpdateCallback)
    {
        meshCreator.SetMapInfo(LOD, mapChunkSize, density, material);
        this.chunkData = meshCreator.GenerateMapData(IsoLevel, LOD, mapChunkSize);
        this.specialMeshes = meshCreator.CreateSpecialMeshes(specialShaderData, chunkData.meshData);    
        meshCreator.ReleaseTempBuffers();

        this.mesh = this.chunkData.GenerateMesh();

        hasChunk = true;
        UpdateCallback();
    }
}

[System.Serializable]
public struct LODInfo
{
    public int LOD;
    public float distanceThresh;
    public bool useForCollider;
}
