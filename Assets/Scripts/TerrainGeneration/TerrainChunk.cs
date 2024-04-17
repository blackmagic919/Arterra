using System;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static EndlessTerrain;

public class TerrainChunk : ChunkData
{
    GameObject meshObject;
    Vector3 position;
    Vector3 CCoord;
    Bounds bounds;

    MeshRenderer meshRenderer;
    MeshFilter meshFilter;
    MeshCollider meshCollider;
    ShaderGenerator geoShaders;
    AsyncMeshReadback meshReadback;

    MeshCreator meshCreator;
    StructureCreator structCreator;

    readonly LODMesh LODMeshHandle;
    readonly LODInfo[] detailLevels;

    MapData[] storedMap = null;
    bool hasDensityMap = false;
    public bool active = true;

    float IsoLevel;
    int prevMeshLOD = -1;
    int prevMapLOD = -1;

    public TerrainChunk(Vector3 coord, float IsoLevel, Transform parent, SurfaceChunk surfaceChunk,  LODInfo[] detailLevels, GenerationResources generation)
    {
        CCoord = coord;
        position = coord * mapChunkSize - Vector3.one * (mapChunkSize / 2f); //Shift mesh so it is aligned with center
        bounds = new Bounds(position, Vector3.one * mapChunkSize);  
        this.IsoLevel = IsoLevel;
        this.detailLevels = detailLevels;
        this.meshCreator = new MeshCreator(generation.meshCreator);
        this.structCreator = new StructureCreator(generation.meshCreator, generation.surfaceSettings);

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.position = position * lerpScale;
        meshObject.transform.localScale = Vector3.one * lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshCollider = meshObject.AddComponent<MeshCollider>();
        meshRenderer.sharedMaterials = new Material[2] {generation.terrainMat, generation.waterMat};

        Bounds boundsOS = new Bounds(Vector3.one * (mapChunkSize / 2), Vector3.one * mapChunkSize);
        geoShaders = new ShaderGenerator(generation.geoSettings, meshObject.transform, boundsOS);
        meshReadback = new AsyncMeshReadback(generation.readbackSettings, meshObject.transform, boundsOS, meshFilter);

        LODMeshHandle = new LODMesh(meshCreator, structCreator, this.geoShaders, meshReadback, generation.densityDict, surfaceChunk.baseMap, detailLevels, this.position, this.CCoord, IsoLevel);
        
        //Plan Structures
        EndlessTerrain.GenTask structTask = new EndlessTerrain.GenTask(() => processEvent(() => structCreator.PlanStructuresGPU(this.CCoord, this.position, mapChunkSize, IsoLevel)), taskLoadTable[(int)Utils.priorities.structure]);
        timeRequestQueue.Enqueue(structTask, (int)Utils.priorities.structure);

        Update();
    }

    public void TerraformChunk(Vector3 targetPosition, float terraformRadius, Func<TerrainChunk.MapData, float, TerrainChunk.MapData> handleTerraform)
    {
        if (!hasDensityMap)
        {
            SurfaceChunk.SurfData maxSurfaceData = LODMeshHandle.surfaceMap.GetMap();
            storedMap = meshCreator.GetChunkInfo(structCreator, maxSurfaceData, this.position, IsoLevel, mapChunkSize);
            
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
                    storedMap[index] = handleTerraform(storedMap[index], brushStrength);
                }
            }
        }

        //Immediately regenerate the chunk to provide immediate feedback
        GenTask mapDataTask = new GenTask(() => processMap(() => LODMeshHandle.SetChunkData(0, ref storedMap), 0), taskLoadTable[(int)Utils.priorities.generation]);
        EndlessTerrain.GenTask computeTask = new EndlessTerrain.GenTask(() => processMesh(() => LODMeshHandle.ComputeChunk(0, onChunkCreated), 0), taskLoadTable[(int)Utils.priorities.mesh]);
        
        timeRequestQueue.Enqueue(mapDataTask, (int)Utils.priorities.generation);
        timeRequestQueue.Enqueue(computeTask, (int)Utils.priorities.mesh);
    }
    public void Update()
    {
        float closestDist = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
        
        int meshLoD = 0;
        int mapLoD = 0;
        for (int i = 0; i < detailLevels.Length - 1; i++)
        {
            if (closestDist > detailLevels[i].distanceThresh)
                meshLoD = i + 1;
            if ((closestDist-mapChunkSize) > detailLevels[i].distanceThresh)
                mapLoD = i + 1;
            else
                break;
        }

        //Map may have different resolution than mesh--solve out-of-bound normals etc.
        if(mapLoD != prevMapLOD){
            prevMapLOD = mapLoD;
            if(hasDensityMap){
                EndlessTerrain.GenTask mapDataTask = new EndlessTerrain.GenTask(() => processMap(() => LODMeshHandle.SetChunkData(mapLoD, ref storedMap), mapLoD), taskLoadTable[(int)Utils.priorities.generation]);
                timeRequestQueue.Enqueue(mapDataTask, (int)Utils.priorities.generation);
            } else {
                EndlessTerrain.GenTask mapDataTask = new EndlessTerrain.GenTask(() => processMap(() => LODMeshHandle.GenerateMap(mapLoD), mapLoD), taskLoadTable[(int)Utils.priorities.generation]);
                timeRequestQueue.Enqueue(mapDataTask, (int)priorities.generation);
            }
        }

        //Have to regenerate everytime because mesh takes up too much memory
        if (meshLoD != prevMeshLOD){
            prevMeshLOD = meshLoD;
            if (hasDensityMap){
                EndlessTerrain.GenTask computeTask = new EndlessTerrain.GenTask(() => processMesh(() => LODMeshHandle.ComputeChunk(meshLoD, onChunkCreated), meshLoD), taskLoadTable[(int)Utils.priorities.mesh]);
                timeRequestQueue.Enqueue(computeTask, (int)Utils.priorities.mesh);
            }else { 
                EndlessTerrain.GenTask meshGenTask = new EndlessTerrain.GenTask(() => processMesh(() => LODMeshHandle.CreateMesh(meshLoD, onChunkCreated), meshLoD), taskLoadTable[(int)Utils.priorities.mesh]);
                timeRequestQueue.Enqueue(meshGenTask, (int)priorities.mesh);
            }
        }

        lastUpdateChunks.Enqueue(this);
    }

    public void onChunkCreated(MeshInfo meshInfo, int lodInd)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        if(detailLevels[lodInd].useForCollider){
            meshCollider.sharedMesh = meshInfo.GetSubmesh(0, UnityEngine.Rendering.IndexFormat.UInt32);
        }
    }

    public override void UpdateVisibility(Vector3 CCCoord, float maxRenderDistance)
    {
        Vector3 distance = CCoord - CCCoord;
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y), Mathf.Abs(distance.z)) <= maxRenderDistance;

        if(!visible)
            DestroyChunk();
    }

    //We destroy the chunk to preserve RAM in both dictionary and scene
    public override void DestroyChunk()
    {
        if (!active)
            return;

        active = false;

        geoShaders.ReleaseGeometry(); //Release geoShader Geometry
        meshReadback.ReleaseAllGeometry(); //Release base geometry on GPU
        structCreator.ReleaseStructure(); //Release structure data
        terrainChunkDict.Remove(CCoord);
#if UNITY_EDITOR
        GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
    }
    
    public struct MapData
    {
        public float density;
        public float viscosity;
        public int material;
    }


    public void processEvent(Action callback)
    {
        if (!active)
            return;
        
        callback();
    }

    void processMap(Action callback, int LOD = -1)
    {
        if (!active)
            return;
        if (LOD != -1 && LOD != prevMapLOD)
            return;
        
        callback();
    }

    void processMesh(Action callback, int LOD = -1)
    {
        if (!active)
            return;
        if (LOD != -1 && LOD != prevMeshLOD)
            return;
        
        callback();
    }
}