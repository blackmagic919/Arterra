using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
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

    LODMesh[] LODMeshes;
    LODInfo[] detailLevels;

    System.Action<bool> UpdateCallback;

    float[] storedDensity = null;
    float[] storedMaterial = null;
    bool hasDensityMap = false;
    bool compeltedRequest = false;

    float IsoLevel;
    int surfaceMaxDepth;
    int prevLODInd = -1;

    public TerrainChunk(Vector3 coord, int size, int surfaceMaxDepth, float IsoLevel, Transform parent, List<SpecialShaderData> specialShaders,
                        MeshCreator meshCreator, Material material, LODInfo[] detailLevels, Action<bool> UpdateCallback)
    {
        CCoord = coord;
        position = (coord * size - Vector3.one * (size / 2f)); //Shift mesh so it is aligned with center
        bounds = new Bounds(position, Vector3.one * size);
        this.IsoLevel = IsoLevel;
        this.surfaceMaxDepth = surfaceMaxDepth;
        this.detailLevels = detailLevels;
        this.UpdateCallback = UpdateCallback;
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
            LODMeshes[i] = new LODMesh(meshCreator, specialMaterialIndexes, detailLevels[i].LOD, this.position, CCoord, surfaceMaxDepth, IsoLevel);
        }

        //Plan Structures
        timeRequestQueue.Enqueue(() => meshCreator.PlanStructures(this.CCoord, this.position, mapChunkSize, surfaceMaxDepth, IsoLevel));

        SetVisible(false);
        Update();
    }

    public void TerraformChunk(Vector3 targetPosition, float terraformRadius, Func<Vector2, float, Vector2> handleTerraform)
    {
        if (!hasDensityMap)
        {
            storedDensity = meshCreator.GetDensity(IsoLevel, surfaceMaxDepth, this.position, this.CCoord);
            meshCreator.GenerateStructures(this.CCoord, IsoLevel, 0, mapChunkSize, out storedMaterial);
            hasDensityMap = true;
        }

        float worldScale = lerpScale;

        Vector3 targetPointLocal = meshObject.transform.worldToLocalMatrix.MultiplyPoint3x4(targetPosition);
        int closestX = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.x), mapChunkSize));
        int closestY = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.y), mapChunkSize));
        int closestZ = Mathf.Max(0, Mathf.Min(Mathf.RoundToInt(targetPointLocal.z), mapChunkSize));
        int localRadius = Mathf.CeilToInt((1.000f / worldScale) * terraformRadius);

        meshCreator.GetFocusedMaterials(localRadius, new Vector3(closestX, closestY, closestZ), this.position, ref storedMaterial);

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

                    int index = Utility.indexFromCoord(vertPosition.x, vertPosition.y, vertPosition.z, mapChunkSize + 1);

                    Vector3 dR = new Vector3(vertPosition.x, vertPosition.y, vertPosition.z) - targetPointLocal;
                    float sqrDistWS = worldScale * (dR.x * dR.x + dR.y * dR.y + dR.z * dR.z);


                    float brushStrength = 1.0f - Mathf.InverseLerp(0, terraformRadius * terraformRadius, sqrDistWS);

                    Vector2 ret = handleTerraform(new Vector2(storedMaterial[index], storedDensity[index]), brushStrength);
                    storedMaterial[index] = ret.x;
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
        bool visible = closestDist <= renderDistance;

        if (visible)
        {
            int lodInd = 0;

            for (int i = 0; i < detailLevels.Length - 1; i++)
            {
                if (closestDist > detailLevels[i].distanceThresh)
                    lodInd = i + 1;
                else
                    break;
            }

            LODMesh lodMesh = LODMeshes[lodInd];
            if (lodInd != prevLODInd || lodMesh.depreceated)
            {
                if (lodMesh.depreceated && compeltedRequest)
                {
                    compeltedRequest = false;
                    lodMesh.hasChunk = false;
                    lodMesh.hasRequestedChunk = false;
                    lodMesh.depreceated = false;
                    prevLODInd = -1;
                }

                CreateChunk(lodInd, lodMesh, closestDist);
            }
            lastUpdateChunks.Enqueue(this);
        }
        SetVisible(visible);
    }

    public void CreateChunk(int lodInd, LODMesh lodMesh, float closestDist)
    {
        if (lodMesh.hasChunk)
        {
            prevLODInd = lodInd;
            meshFilter.mesh = lodMesh.mesh;
            compeltedRequest = true;
            if (detailLevels[lodInd].useForCollider)
                meshCollider.sharedMesh = lodMesh.mesh;

            for(int i = 0; i < shaders.Length; i++)
            {
                SpecialShaderData shaderData = shaders[i];

                if (lodMesh.specialMeshes.TryGetValue(shaders[i].materialIndex, out Mesh mesh))
                {
                    shaderData.shader.SetMesh(mesh);
                    shaderData.shader.Enable();
                }
                else
                    shaderData.shader.Disable();
            }

            if (lodMesh.depreceated) //was depreceated while chunk was regenerating
               timeRequestQueue.Enqueue(Update);

            UpdateCallback(true);
        }
        else if (!lodMesh.hasRequestedChunk)
        {
            lodMesh.hasRequestedChunk = true;

            if (hasDensityMap)
                timeRequestQueue.Enqueue(() => lodMesh.ComputeChunk(ref storedDensity, ref storedMaterial, () => CreateChunk(lodInd, lodMesh, closestDist)));
            else
                timeRequestQueue.Enqueue(() => lodMesh.GetChunk(() => CreateChunk(lodInd, lodMesh, closestDist)));

            UpdateCallback(false);
        }
    }

    public void UpdateVisibility(Vector3 CCCoord, float maxRenderDistance)
    {
        Vector3 distance = CCoord - CCCoord;
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y), Mathf.Abs(distance.z)) <= maxRenderDistance;
        SetVisible(visible);
    }

    public void SetVisible(bool visible)
    {
        if (visible == meshObject.activeSelf)
            return;
        meshObject.SetActive(visible);
    }
    
    public bool isVisible() { return meshObject.activeSelf; }
}

public class LODMesh
{
    public Mesh mesh;
    public Dictionary<int, Mesh> specialMeshes;
    public SpecialShaderData[] specialShaderData;
    public Vector3 position;
    Vector3 CCoord;

    //Noise Data recieved by 
    MapGenerator.ChunkData chunkData;

    int surfaceMaxDepth;
    float IsoLevel;
    MeshCreator meshCreator;

    public bool hasChunk = false;
    public bool hasRequestedChunk = false;
    public bool excludeGen = false;
    public bool depreceated = false;

    System.Action UpdateCallback;
    int LOD;

    //Temporary

    public LODMesh(MeshCreator meshCreator, SpecialShaderData[] specialShaderData, int LOD, Vector3 position, Vector3 CCoord, int surfaceMaxDepth, float IsoLevel)
    {
        this.LOD = LOD;
        this.CCoord = CCoord;
        this.position = position;
        this.meshCreator = meshCreator;
        this.specialShaderData = specialShaderData;

        this.surfaceMaxDepth = surfaceMaxDepth;
        this.IsoLevel = IsoLevel;
    }


    public void GetChunk(Action UpdateCallback)
    {
        meshCreator.GenerateDensity(this.position, LOD, surfaceMaxDepth, mapChunkSize, IsoLevel);
        meshCreator.GenerateStructures(this.CCoord, IsoLevel, LOD, mapChunkSize);
        this.chunkData = meshCreator.GenerateMapData(IsoLevel, this.position, LOD, mapChunkSize);
        this.specialMeshes = meshCreator.CreateSpecialMeshes(specialShaderData, this.chunkData);
        meshCreator.ReleaseBuffers();

        this.mesh = this.chunkData.GenerateMesh(this.chunkData.meshData);

        hasChunk = true;
        UpdateCallback();
    }

    public void ComputeChunk(ref float[] density, ref float[] material, Action UpdateCallback)
    {
        meshCreator.SetDensity(LOD, mapChunkSize, density, material);
        this.chunkData = meshCreator.GenerateMapData(IsoLevel, this.position, LOD, mapChunkSize);
        this.specialMeshes = meshCreator.CreateSpecialMeshes(specialShaderData, this.chunkData);
        meshCreator.ReleaseBuffers();

        this.mesh = this.chunkData.GenerateMesh(this.chunkData.meshData);

        hasChunk = true;
        UpdateCallback();
    }

    public MapGenerator.ChunkData GetChunkData()
    {
        return this.chunkData;
    }
}

[System.Serializable]
public struct LODInfo
{
    public int LOD;
    public float distanceThresh;
    public bool useForCollider;
}
