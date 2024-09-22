using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static EndlessTerrain;

public class TerrainChunk
{
    public GameObject meshObject;
    public Vector3 origin;
    public int3 CCoord;
    public Bounds boundsOS;
    public SurfaceChunk surfaceMap;

    readonly MeshRenderer meshRenderer;
    readonly MeshFilter meshFilter;
    readonly List<LODInfo> detailLevels;
    GeneratorInfo Generator;

    public readonly float IsoLevel;
    public readonly int mapChunkSize;

    public int mapLoD;
    public int meshLoD;
    public ChunkStatus status;
    
    public struct ChunkStatus{
        byte data;
        //Don't ever set UpdateMap to true, use it to clear all map update flags
        public bool UpdateMap { readonly get => (data & 0x7) != 0; set => data = (byte)((data & 0xF8) | (value ? 0x7 : 0)); } 
        public bool CreateMap { readonly get => (data & 1) == 1; set => data = (byte)((data & 0xF8) | (value ? 1 : data & 0x6)); }
        public bool ShrinkMap { readonly get => (data & 2) == 2; set => data = (byte)((data & 0xF8) | (value ? 2 : data & 0x5)); }
        public bool SetMap { readonly get => (data & 4) == 4; set => data = (byte)((data & 0xF8) | (value ? 4 : data & 0x3)); }
        public bool UpdateMesh { readonly get => (data & 8) == 8; set => data = (byte)((data & 0xF7) | (value ? 8 : 0)); }
        public bool CanUpdateMesh { readonly get => (data & 16) == 16; set => data = (byte)((data & 0xEF) | (value ? 16 : 0)); }
        public ChunkStatus(byte data) {this.data = data;}
    }

    public readonly struct GeneratorInfo{
        public readonly ShaderGenerator GeoShaders;
        public readonly AsyncMeshReadback MeshReadback;
        public readonly MeshCreator MeshCreator;
        public readonly StructureCreator StructCreator;

        public GeneratorInfo(TerrainChunk terrainChunk)
        {
            this.MeshCreator = new MeshCreator();
            this.StructCreator = new StructureCreator();
            this.GeoShaders = new ShaderGenerator(terrainChunk.meshObject.transform, terrainChunk.boundsOS);
            this.MeshReadback = new AsyncMeshReadback(terrainChunk.meshObject.transform, terrainChunk.boundsOS);
        }

        public enum ReadbackMaterial{
            terrain = 0,
            water = 1,
        }
    }

    public TerrainChunk(int3 coord, Transform parent, SurfaceChunk surfaceChunk)
    {
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        this.detailLevels = rSettings.detailLevels.value;
        this.surfaceMap = surfaceChunk;
        this.IsoLevel = rSettings.IsoLevel;
        this.mapChunkSize = rSettings.mapChunkSize;

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.localScale = Vector3.one * rSettings.lerpScale;
        meshObject.transform.parent = parent;

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = WorldStorageHandler.WORLD_OPTIONS.ReadBackSettings.value.TerrainMats.ToArray();

        CCoord = coord;
        RecreateChunk();
    }

    private uint ChunkDist3D(float3 GPos){
        int3 GCoord = (int3)GPos;
        float3 cPt = math.clamp(GCoord, CCoord * mapChunkSize, (CCoord + 1) * mapChunkSize);
        float3 cDist = math.abs(math.floor((cPt - GCoord) / mapChunkSize + 0.5f));
        //We add 0.5 because normally this returns an odd number, but even numbers have better cubes
        return (uint)math.max(cDist.x, math.max(cDist.y, cDist.z)); 
        //can't add 0.5f here because of VERY complicated reasons
        //(basically it'll round up in both positive and negative making it impossible to contain only numchunk
    }

    private void RecreateChunk(){
        //Recalculate CCoord
        int maxChunkDist = detailLevels[^1].chunkDistThresh;
        int numChunksAxis = maxChunkDist * 2;

        int3 vGCoord = (int3)(float3)viewerPosition;
        int3 vMCoord = ((vGCoord % mapChunkSize) + mapChunkSize) % mapChunkSize;
        int3 vOCoord = (vGCoord - vMCoord) / mapChunkSize - maxChunkDist + math.select(new int3(0), new int3(1), vMCoord > (mapChunkSize / 2)); 
        int3 HCoord = (((CCoord - vOCoord) % numChunksAxis) + numChunksAxis) % numChunksAxis;
        CCoord = vOCoord + HCoord;
        mapLoD = int.MaxValue;
        meshLoD = int.MinValue;

        ReleaseChunk(); ClearFilter();
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.value.Rendering.value;
        Vector3 position = CustomUtility.AsVector(CCoord) * rSettings.mapChunkSize;
        origin = position - Vector3.one * (rSettings.mapChunkSize / 2f);
        meshObject.transform.position = origin * rSettings.lerpScale;

        boundsOS = new Bounds(Vector3.one * (rSettings.mapChunkSize / 2), Vector3.one * rSettings.mapChunkSize);
        Generator = new GeneratorInfo(this);
        status = new ChunkStatus(0);

        int3 GCoord = (int3)(float3)viewerPosition;
        float3 cPt = math.clamp(GCoord, CCoord * mapChunkSize, (CCoord + 1) * mapChunkSize);
        float3 cDist = math.abs(cPt - GCoord) / mapChunkSize;

        //Plan Structures
        RequestQueue.Enqueue(new GenTask{
            task = () => PlanStructures(), 
            id = (int)priorities.structure,
        });
    }
    
    public void ValidateChunk(){
        float closestDist = ChunkDist3D(viewerPosition);
        if(closestDist >= detailLevels[^1].chunkDistThresh) {
            RecreateChunk();
            closestDist = ChunkDist3D(viewerPosition);
        }
        if(closestDist >= detailLevels[^1].chunkDistThresh) {
            Debug.Log("Crash");
            return;
        }

        int mapLoD = 0;
        int meshLoD = 0;
        for (int i = 0; i < detailLevels.Count; i++){
            if ( closestDist >= detailLevels[i].chunkDistThresh) mapLoD = i + 1;
            if ( closestDist + 1 >= detailLevels[i].chunkDistThresh) meshLoD = i + 1;
            else break;
        }

        if(mapLoD != this.mapLoD){
            status.CreateMap = true;
            status.ShrinkMap = mapLoD > this.mapLoD;
            this.mapLoD = mapLoD;
        }

        if(meshLoD != this.meshLoD && meshLoD < detailLevels.Count){
            status.UpdateMesh = true;
            status.CanUpdateMesh = false;
            this.meshLoD = meshLoD;
        };
    }

    public void Update()
    {
        if(status.CreateMap){
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            status.UpdateMap = false;
            ReadMapData(mapLoD); 
        }
        else if (status.ShrinkMap){
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{ 
                task = () => SimplifyMap(mapLoD),
                id =(int)priorities.generation,
            });
        } else if(status.SetMap){
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{ 
                task = () => SetChunkData(),
                id = (int)priorities.generation,
            });
        }

        if(status.UpdateMesh) {
            status.UpdateMesh = false;
            RequestQueue.Enqueue(new GenTask{ 
                task = () => status.CanUpdateMesh = true,
                id = (int)priorities.propogation,
            });
        }
        if(status.CanUpdateMesh){
            status.CanUpdateMesh = false;
            RequestQueue.Enqueue(new GenTask{
                task = () => {CreateMesh(meshLoD, OnChunkCreated);}, 
                id = (int)priorities.mesh
            });
        }
    }


    private void OnChunkCreated(AsyncMeshReadback.SharedMeshInfo meshInfo)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        meshInfo.Release();
    }

    public void DestroyChunk()
    {
        ReleaseChunk();
#if UNITY_EDITOR
       GameObject.DestroyImmediate(meshObject);
#else
        Destroy(meshObject);
#endif
    }

    public void ReflectChunk(){
        if(mapLoD != 0) return; //Can't reflect a chunk that hasn't been read yet
        status.SetMap = true;
        status.UpdateMesh = true;
    }

    private void ClearFilter(){ meshFilter.sharedMesh = null; }
    
    public void PlanStructures(Action callback = null){
        Generator.StructCreator.PlanStructuresGPU(CCoord, origin, mapChunkSize, IsoLevel);
        callback?.Invoke();
    }

    void CopyMapToCPU(){
        uint entityAddress = EntityManager.PlanEntities(surfaceMap.baseMap.GetMap(), CCoord, mapChunkSize);
        EntityManager.BeginEntityReadback(entityAddress, CCoord);
        int3 CCoord_Captured = CCoord;
        CPUDensityManager.AllocateChunk(this, CCoord, (bool isComplete) => CPUDensityManager.BeginMapReadback(CCoord_Captured));
    }

    public void ReadMapData(int LoD, Action callback = null){
        void SetChunkData(int LOD, int offset, CPUDensityManager.MapData[] mapData, Action callback = null){
            Generator.MeshCreator.SetMapInfo(LOD, mapChunkSize, offset, mapData);
            GPUDensityManager.SubscribeChunk(CCoord, LOD, UtilityBuffers.TransferBuffer, true);
            if(LOD == 0) CopyMapToCPU();
        }

        void GenerateMap(int LOD, Action callback = null)
        {
            Generator.MeshCreator.GenerateBaseChunk(surfaceMap.baseMap.GetMap(), origin, LOD, mapChunkSize, IsoLevel);
            Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize, LOD, IsoLevel);
            GPUDensityManager.SubscribeChunk(CCoord, LOD, UtilityBuffers.GenerationBuffer);
            if(LOD == 0) CopyMapToCPU();
        }

        //This code will be called on a background thread
        ChunkStorageManager.ReadChunkBin(CCoord, LoD, (bool isComplete, CPUDensityManager.MapData[] chunk) => 
            RequestQueue.Enqueue(new GenTask{ //REMINDER: This queue should be locked
                task = () => OnReadComplete(isComplete, chunk),
                id = (int)priorities.generation,
            })
        );

        //This code will run on main thread
        void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk){
            if(!isComplete) GenerateMap(LoD, callback);
            else SetChunkData(LoD, 0, chunk, callback);
            callback?.Invoke();
        }
    }

    public void SimplifyMap(int LoD, Action callback = null){
        GPUDensityManager.SimplifyChunk(CCoord, LoD);
        callback?.Invoke();
    }

    public void CreateMesh(int LoD, Action<AsyncMeshReadback.SharedMeshInfo> UpdateCallback = null){
        Generator.MeshCreator.GenerateMapData(CCoord, IsoLevel, LoD, mapChunkSize);
        ClearFilter();

        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.terrain);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.water);
        Generator.MeshReadback.BeginMeshReadback(UpdateCallback);

        if (detailLevels[LoD].useForGeoShaders)
            Generator.GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)GeneratorInfo.ReadbackMaterial.terrain]);
        else
            Generator.GeoShaders.ReleaseGeometry();
    }

    public void SetChunkData(Action callback = null){
        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        int offset = CPUDensityManager.HashCoord(CCoord) * numPoints;
        NativeArray<CPUDensityManager.MapData> mapData = CPUDensityManager.SectionedMemory;

        Generator.MeshCreator.SetMapInfo(0, mapChunkSize, offset, ref mapData);
        GPUDensityManager.SubscribeChunk(CCoord, 0, UtilityBuffers.TransferBuffer, true);
        callback?.Invoke();
    }

    public void ReleaseChunk(){
        Generator.GeoShaders?.ReleaseGeometry(); //Release geoShader Geometry
        Generator.MeshReadback?.ReleaseAllGeometry(); //Release base geometry on GPU
        Generator.StructCreator?.ReleaseStructure(); //Release structure data
    }
}

[System.Serializable]
public struct LODInfo
{
    public int chunkDistThresh;
    public bool useForGeoShaders;
}