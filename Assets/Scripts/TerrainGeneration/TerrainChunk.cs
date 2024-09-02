using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
    public Bounds boundsOS;
    public SurfaceChunk.BaseMap surfaceMap;

    readonly MeshRenderer meshRenderer;
    readonly MeshFilter meshFilter;
    readonly MeshCollider meshCollider;

    readonly LODMesh LODMeshHandle;
    readonly List<LODInfo> detailLevels;

    public bool active = true;
    public int prevMapLOD = int.MaxValue;
    int prevMeshLOD = int.MaxValue;
    float closestDist;

    public TerrainChunk(int3 coord, Transform parent, SurfaceChunk surfaceChunk)
    {
        CCoord = coord;
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        position = CustomUtility.AsVector(coord) * rSettings.mapChunkSize;
        origin = position - Vector3.one * (rSettings.mapChunkSize / 2f); //Shift mesh so it is aligned with center
        this.detailLevels = rSettings.detailLevels.value;
        this.surfaceMap = surfaceChunk.baseMap;

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.position = origin * rSettings.lerpScale;
        meshObject.transform.localScale = Vector3.one * rSettings.lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = WorldStorageHandler.WORLD_OPTIONS.ReadBackSettings.value.TerrainMats.ToArray();

        boundsOS = new Bounds(Vector3.one * (rSettings.mapChunkSize / 2), Vector3.one * rSettings.mapChunkSize);
        LODMeshHandle = new LODMesh(this, detailLevels, ClearFilter);
        
        //Plan Structures
        RequestQueue.Enqueue(new GenTask{
            valid = () => this.active,
            task = () => LODMeshHandle.PlanStructures(Update), 
            load = taskLoadTable[(int)priorities.structure]
        });
    }

    private uint ChunkDist3D(float3 GCoord){
        int chunkSize = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value.mapChunkSize;
        float3 cPt = math.clamp(GCoord, CCoord * chunkSize, (CCoord + 1) * chunkSize);
        float3 cDist = math.abs(cPt - GCoord);
        //We add 0.5 because normally this returns an odd number, but even numbers have better cubes
        return (uint)math.floor(math.max(cDist.x, math.max(cDist.y, cDist.z)) / chunkSize + 0.5f);
    }

    public void Update()
    {
        closestDist = ChunkDist3D(viewerPosition);

        int mapLoD = 0;
        for (int i = 0; i < detailLevels.Count; i++)
            if ( closestDist >= detailLevels[i].chunkDistThresh) mapLoD = i + 1;

        if(mapLoD != prevMapLOD && mapLoD < detailLevels.Count){
            if(prevMapLOD <= mapLoD) {
                RequestQueue.Enqueue(new EndlessTerrain.GenTask{ 
                    valid = () => this.prevMapLOD == mapLoD,
                    task = () => LODMeshHandle.SimplifyMap(mapLoD, onMapGenerated),
                    load = taskLoadTable[(int)Utils.priorities.generation],
                });
            } else {
                LODMeshHandle.ReadMapData(mapLoD, onMapGenerated); 
            }
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            prevMapLOD = mapLoD;
        }
        else RequestQueue.Enqueue(new EndlessTerrain.GenTask{
            valid = () => this.prevMapLOD == mapLoD,
            task = onMapGenerated,
            load = 0,
        });

        lastUpdateChunks.Enqueue(this);
    }


    private void onMapGenerated(){ 
        int meshLoD = prevMapLOD;
        float farthestDistance = closestDist + 1;
        if (farthestDistance >= detailLevels[meshLoD].chunkDistThresh) meshLoD++;

        if(meshLoD != prevMeshLOD && meshLoD < detailLevels.Count){
            prevMeshLOD = meshLoD;

            RequestQueue.Enqueue(new EndlessTerrain.GenTask{
                valid = () => this.prevMeshLOD == meshLoD,
                task = () => {LODMeshHandle.CreateMesh(meshLoD, onChunkCreated);}, 
                load = taskLoadTable[(int)Utils.priorities.mesh]
            });
        }
    }

    private void onChunkCreated(AsyncMeshReadback.SharedMeshInfo meshInfo)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        meshInfo.Release();

        //BakeMesh(meshInfo.GetSubmesh(0, UnityEngine.Rendering.IndexFormat.UInt32));
        //I'm proud to say we're no longer relying on Unity's mesh collider
    }

    private void BakeMesh(Mesh mesh){
        if(mesh == null) return;
        int meshID = mesh.GetInstanceID();
        Task.Run(() => {
            Physics.BakeMesh(meshID, false);
            RequestQueue.Enqueue(new EndlessTerrain.GenTask{
                valid = () => this.active,
                task = () => meshCollider.sharedMesh = mesh,
                load = 0
            });
        });
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

    public void RecalculateChunkImmediate(int offset, ref NativeArray<CPUDensityManager.MapData> mapData){
        if(!active) return;
        LODMeshHandle.SetChunkData(0, offset, mapData, () =>{
            LODMeshHandle.CreateMesh(0, onChunkCreated);
        });
    }

    private void ClearFilter(){ meshFilter.sharedMesh = null; }

}