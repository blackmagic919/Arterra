using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static EndlessTerrain;

public class TerrainChunk : ChunkData
{
    public GameObject meshObject;
    public Vector3 origin;
    public Vector3 position;
    public int3 CCoord;
    public Bounds bounds;
    public Bounds boundsOS;
    public SurfaceChunk.BaseMap surfaceMap;

    readonly MeshRenderer meshRenderer;
    readonly MeshFilter meshFilter;
    readonly MeshCollider meshCollider;

    readonly LODMesh LODMeshHandle;
    readonly LODInfo[] detailLevels;

    public bool active = true;
    public int prevMapLOD = int.MaxValue;
    int prevMeshLOD = int.MaxValue;
    float closestDist;

    public TerrainChunk(int3 coord, float IsoLevel, Transform parent, SurfaceChunk surfaceChunk,  LODInfo[] detailLevels)
    {
        CCoord = coord;
        position = CustomUtility.AsVector(coord) * mapChunkSize;
        origin = position - Vector3.one * (mapChunkSize / 2f); //Shift mesh so it is aligned with center
        bounds = new Bounds(origin, Vector3.one * mapChunkSize);  
        this.detailLevels = detailLevels;
        this.surfaceMap = surfaceChunk.baseMap;

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.position = origin * lerpScale;
        meshObject.transform.localScale = Vector3.one * lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshCollider = meshObject.AddComponent<MeshCollider>();
        meshRenderer.sharedMaterials = WorldStorageHandler.WORLD_OPTIONS.ReadBackSettings.value.TerrainMats.ToArray();

        boundsOS = new Bounds(Vector3.one * (mapChunkSize / 2), Vector3.one * mapChunkSize);
        LODMeshHandle = new LODMesh(this, detailLevels, IsoLevel, ClearFilter);
        
        //Plan Structures
        timeRequestQueue.Enqueue(new EndlessTerrain.GenTask{
            valid = () => this.active,
            task = () => LODMeshHandle.PlanStructures(Update), 
            load = taskLoadTable[(int)Utils.priorities.structure]
        });
    }

    public void Update()
    {
        closestDist = Mathf.Sqrt(bounds.SqrDistance(viewerPosition));
        
        int mapLoD = 0;
        for (int i = 0; i < detailLevels.Length; i++)
            if ( closestDist > detailLevels[i].distanceThresh) mapLoD = i + 1;
        //Map may have different resolution than mesh--solve out-of-bound normals etc. 

        if(mapLoD != prevMapLOD && mapLoD < detailLevels.Length){
            if(prevMapLOD <= mapLoD) {
                timeRequestQueue.Enqueue(new EndlessTerrain.GenTask{ 
                    valid = () => this.prevMapLOD == mapLoD,
                    task = () => LODMeshHandle.SimplifyMap(mapLoD, onMapGenerated),
                    load = taskLoadTable[(int)Utils.priorities.generation],
                });
            } else LODMeshHandle.ReadMapData(mapLoD, onMapGenerated); 
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            prevMapLOD = mapLoD;
        }
        else timeRequestQueue.Enqueue(new EndlessTerrain.GenTask{
            valid = () => this.prevMapLOD == mapLoD,
            task = onMapGenerated,
            load = 0,
        });

        lastUpdateChunks.Enqueue(this);
    }

    float FarthestDist(Vector3 point)
    {
        Vector3 minC = bounds.min; Vector3 maxC = bounds.max;
        Vector3[] corners = new Vector3[8]
        {
            minC,
            new (minC.x, minC.y, maxC.z),
            new (minC.x, maxC.y, minC.z),
            new (minC.x, maxC.y, maxC.z),
            new (maxC.x, minC.y, minC.z),
            new (maxC.x, minC.y, maxC.z),
            new (maxC.x, maxC.y, minC.z),
            maxC
        };

        float maxDistance = 0.0f;
        for (int i = 0; i < 8; i++) 
            maxDistance = Mathf.Max(Vector3.Distance(point, corners[i]), maxDistance);

        return maxDistance;
    }


    private void onMapGenerated(){ 
        int meshLoD = prevMapLOD;
        float farthestDistance = FarthestDist(viewerPosition);
        if (farthestDistance > detailLevels[meshLoD].distanceThresh) meshLoD++;

        if(meshLoD != prevMeshLOD && meshLoD < detailLevels.Length){
            prevMeshLOD = meshLoD;

            timeRequestQueue.Enqueue(new EndlessTerrain.GenTask{
                valid = () => this.prevMeshLOD == meshLoD,
                task = () => {LODMeshHandle.CreateMesh(meshLoD, onChunkCreated);}, 
                load = taskLoadTable[(int)Utils.priorities.mesh]
            });
        }
    }

    private void onChunkCreated(AsyncMeshReadback.SharedMeshInfo meshInfo)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        if(detailLevels[prevMeshLOD].useForCollider){
            meshCollider.sharedMesh = meshInfo.GetSubmesh(0, UnityEngine.Rendering.IndexFormat.UInt32);
        }
        meshInfo.Release();
    }

    public override void UpdateVisibility(int3 CCCoord, float maxRenderDistance)
    {
        int3 distance = CCoord - CCCoord;
        bool visible = Mathf.Max(Mathf.Abs(distance.x), Mathf.Abs(distance.y), Mathf.Abs(distance.z)) <= maxRenderDistance;

        if(!visible) DestroyChunk();
    }

    //We destroy the chunk to preserve RAM in both dictionary and scene
    public override void DestroyChunk()
    {
        if (!active) return;
        active = false;
        prevMapLOD = -1;
        prevMeshLOD = -1;

        LODMeshHandle.Release();
        terrainChunkDict.Remove(CCoord);
#if UNITY_EDITOR
        GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
    }

    public Vector3 WorldToLocal(Vector3 worldPos){ return meshObject.transform.worldToLocalMatrix.MultiplyPoint3x4(worldPos); }
    public Vector3 WorldToLocalDir(Vector3 worldDir){ return meshObject.transform.InverseTransformDirection(worldDir); }
    public Vector3 LocalToWorld(Vector3 localPos){ return meshObject.transform.localToWorldMatrix.MultiplyPoint3x4(localPos); }
    public bool GetRayIntersect(Ray rayOS, out float dist){ return boundsOS.IntersectRay(rayOS, out dist); }
    
    public void RecalculateChunkImmediate(int offset, ref NativeArray<CPUDensityManager.MapData> mapData){
        LODMeshHandle.SetChunkData(0, offset, mapData, () =>{
            LODMeshHandle.CreateMesh(0, onChunkCreated);
        });
    }

    private void ClearFilter(){ meshFilter.sharedMesh = null; }

}