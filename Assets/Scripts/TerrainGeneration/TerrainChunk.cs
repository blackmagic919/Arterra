using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static OctreeTerrain;

public class TerrainChunk
{
    //Octree index
    public uint index;
    public bool active;

    public GameObject meshObject;
    public int3 origin;
    public int3 CCoord;
    public int size;
    public int depth;
    public Bounds boundsOS;
    public bool IsRealChunk => depth == 0;
    
    public GeneratorInfo Generator;
    public RenderSettings rSettings;
    public uint surfAddress;
    readonly MeshRenderer meshRenderer;
    readonly MeshFilter meshFilter;

    public readonly float IsoLevel;
    public readonly int mapChunkSize;

    public int mapSkipInc => 1 << depth;
    public int meshSkipInc;
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

    public TerrainChunk(Transform parent, int3 origin, int size, uint octreeIndex)
    {
        rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        this.IsoLevel = rSettings.IsoLevel;
        this.mapChunkSize = rSettings.mapChunkSize;
        this.origin = origin;
        this.size = size;
        this.index = octreeIndex;
        this.active = true;

        //This is ok because origin is guaranteed to be a multiple of mapChunkSize by the Octree
        CCoord = origin / rSettings.mapChunkSize;
        depth = math.floorlog2(size / rSettings.mapChunkSize);

        meshObject = new GameObject("Terrain Chunk");
        meshObject.transform.localScale = Vector3.one * rSettings.lerpScale * (1 << depth);
        meshObject.transform.parent = parent;
        meshObject.transform.position = (float3)(origin - (float3)Vector3.one * (rSettings.mapChunkSize / 2f)) * rSettings.lerpScale;

        boundsOS = new Bounds(Vector3.one * (rSettings.mapChunkSize / 2), Vector3.one * rSettings.mapChunkSize);

        meshFilter = meshObject.AddComponent<MeshFilter>();
        meshRenderer = meshObject.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterials = WorldStorageHandler.WORLD_OPTIONS.System.ReadBack.value.TerrainMats.ToArray();
        meshSkipInc = GetMeshSkip();

        status = new ChunkStatus{CreateMap = true, UpdateMesh = true, CanUpdateMesh = false};
        Generator = new GeneratorInfo(this);
        SetupChunk();
    }

    private int GetMeshSkip(){
        if(!IsRealChunk) return 1;
        if(!OctreeTerrain.IsBordering(ref OctreeTerrain.octree.nodes[index]))
            return 1;
        return 1 << rSettings.Balance;
    }

    private void SetupChunk(){
        RequestQueue.Enqueue(new GenTask{
            task = () => GetSurface(), 
            id = (int)priorities.planning,
            chunk = this,
        });
        RequestQueue.Enqueue(new GenTask{
            task = () => PlanStructures(), 
            id = (int)priorities.structure,
            chunk = this,
        });
    }

    public void VerifyChunk(){
        //These two functions are self-verifying, so they will only execute if necessary
        OctreeTerrain.SubdivideLeaf(index);
        OctreeTerrain.MergeSiblings(index);
        if(GetMeshSkip() != meshSkipInc){
            status.UpdateMesh = true;
            meshSkipInc = GetMeshSkip();
        }
    }

    public void OnChunkCreated(AsyncMeshReadback.SharedMeshInfo meshInfo)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        meshInfo.Release();
    }

    public void DestroyChunk()
    {
        if(!active) return;
        active = false;

        ReleaseChunk();
#if UNITY_EDITOR
       GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
    }

    public void ReflectChunk(){
        if(!active) return; //Can't reflect a chunk that hasn't been read yet
        status.SetMap = true;
        status.UpdateMesh = true;
    }

    public void ClearFilter(){ meshFilter.sharedMesh = null; }
    public virtual void Update(){}
    public virtual void GetSurface(){}
    public virtual void PlanStructures(Action callback = null){}

    public void ReleaseChunk(){
        Generator.GeoShaders?.ReleaseGeometry(); //Release geoShader Geometry
        Generator.MeshReadback?.ReleaseAllGeometry(); //Release base geometry on GPU
        Generator.StructCreator?.ReleaseStructure(); //Release structure data
        if(this.surfAddress != 0) GenerationPreset.memoryHandle.ReleaseMemory(surfAddress); //Release surface data
    }
}

//depth = 0
public class RealChunk : TerrainChunk{
    public RealChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex){}
    public override void Update()
    {
        if(status.CreateMap){
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            status.UpdateMap = false;
            ReadMapData(); 
        } else if(status.SetMap){
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{ 
                task = () => SetChunkData(),
                id = (int)priorities.generation,
                chunk = this
            });
        }

        if(status.UpdateMesh) {
            status.UpdateMesh = false;
            RequestQueue.Enqueue(new GenTask{ 
                task = () => status.CanUpdateMesh = true,
                id = (int)priorities.propogation,
                chunk = this
            });
        }
        if(status.CanUpdateMesh){
            status.CanUpdateMesh = false;
            RequestQueue.Enqueue(new GenTask{
                task = () => {CreateMesh(OnChunkCreated);}, 
                id = (int)priorities.mesh,
                chunk = this
            });
        }
    }
    public override void GetSurface()
    {
        SurfaceCreator.SampleSurfaceMaps(origin.xz, mapChunkSize, mapSkipInc);
        this.surfAddress = SurfaceCreator.StoreSurfaceMap(mapChunkSize);
    }

    public override void PlanStructures(Action callback = null){
        Generator.StructCreator.PlanStructuresGPU(CCoord, origin, mapChunkSize, IsoLevel);
        callback?.Invoke();
    }

    public void CopyMapToCPU(){
        uint entityAddress = EntityManager.PlanEntities(surfAddress, CCoord, mapChunkSize);
        EntityManager.BeginEntityReadback(entityAddress, CCoord);
        int3 CCoord_Captured = CCoord;
        CPUDensityManager.AllocateChunk(this, CCoord, (bool isComplete) => CPUDensityManager.BeginMapReadback(CCoord_Captured));
    }

    public void ReadMapData(Action callback = null){
        void SetChunkData(int offset, CPUDensityManager.MapData[] mapData, Action callback = null){
            Generator.MeshCreator.SetMapInfo(mapChunkSize, offset, mapData);
            GPUDensityManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.TransferBuffer);
            CopyMapToCPU();
        }

        void GenerateMap(Action callback = null)
        {
            DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
            Generator.MeshCreator.GenerateBaseChunk(origin, surfAddress, mapChunkSize, mapSkipInc, IsoLevel);
            Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize, mapSkipInc, bufferOffsets.rawMapStart, IsoLevel);
            Generator.MeshCreator.CompressMap(mapChunkSize);
            GPUDensityManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.GenerationBuffer, DensityGenerator.bufferOffsets.mapStart);
            CopyMapToCPU();
        }

        //This code will be called on a background thread
        ChunkStorageManager.ReadChunkBin(CCoord, (bool isComplete, CPUDensityManager.MapData[] chunk) => 
            RequestQueue.Enqueue(new GenTask{ //REMINDER: This queue should be locked
                task = () => OnReadComplete(isComplete, chunk),
                id = (int)priorities.generation,
                chunk = this,
            })
        );

        //This code will run on main thread
        void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk){
            if(!isComplete) GenerateMap(callback);
            else SetChunkData(0, chunk, callback);
            callback?.Invoke();
        }
    }

    public void CreateMesh( Action<AsyncMeshReadback.SharedMeshInfo> UpdateCallback = null){
        Generator.MeshCreator.GenerateMapData(CCoord, IsoLevel, meshSkipInc, mapChunkSize);
        ClearFilter();
        
        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;

        Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.terrain);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.water);
        Generator.MeshReadback.BeginMeshReadback(UpdateCallback);

        if (depth <= rSettings.MaxGeoShaderDepth)
            Generator.GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)GeneratorInfo.ReadbackMaterial.terrain]);
        else
            Generator.GeoShaders.ReleaseGeometry();
    }

    public void SetChunkData(Action callback = null){
        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        int offset = CPUDensityManager.HashCoord(CCoord) * numPoints;
        NativeArray<CPUDensityManager.MapData> mapData = CPUDensityManager.SectionedMemory;

        Generator.MeshCreator.SetMapInfo(mapChunkSize, offset, ref mapData);
        GPUDensityManager.RegisterChunkReal(CCoord,  depth, UtilityBuffers.TransferBuffer);
        callback?.Invoke();
    }

    public void SimplifyMap(int skipInc, Action callback = null){
        GPUDensityManager.SimplifyChunk(CCoord, skipInc);
        callback?.Invoke();
    }
}

//depth >= 1
public class VisualChunk : TerrainChunk{
    private int3 sOrigin;
    private int sChunkSize; 
    public VisualChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex){
        sOrigin = origin - mapSkipInc;
        sChunkSize = mapChunkSize + 3;
    }

    public override void Update()
    {
        if(status.CreateMap){
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{
                task = () => {ReadMapData(OnChunkCreated);}, 
                id = (int)priorities.visual,
                chunk = this,
            });
        } 
        //Set chunk data will only be used if info is modified, which 
        //it shouldn't be for visual chunks
        
        //Visual chunks cannot independently update their mesh because
        //their map data may not be cached, so it must be done immediately
    }
    
    public override void GetSurface()
    {
        SurfaceCreator.SampleSurfaceMaps(sOrigin.xz, sChunkSize, mapSkipInc);
        this.surfAddress = SurfaceCreator.StoreSurfaceMap(sChunkSize);
    }
    public override void PlanStructures(Action callback = null){
        //Implement this later
    }
    
    public void ReadMapData(Action<AsyncMeshReadback.SharedMeshInfo> callback = null){
        void SetChunkData(int offset, CPUDensityManager.MapData[] mapData, Action<AsyncMeshReadback.SharedMeshInfo> callback){
            Generator.MeshCreator.SetMapInfo(sChunkSize, offset, mapData);
        }

        void GenerateMap(Action<AsyncMeshReadback.SharedMeshInfo> callback)
        {
            Generator.MeshCreator.GenerateBaseChunk(sOrigin, surfAddress, sChunkSize, mapSkipInc, IsoLevel);
            //Generator.StructCreator.GenerateStrucutresGPU(sChunkSize, mapSkipInc, IsoLevel);
            Generator.MeshCreator.CompressMap(sChunkSize);
            GPUDensityManager.RegisterChunkVisual(CCoord, depth, UtilityBuffers.GenerationBuffer, DensityGenerator.bufferOffsets.mapStart);
            CreateMeshImmediate(callback);
        }

        //This code will be called on a background thread
        ChunkStorageManager.ReadChunkBin(CCoord, (bool isComplete, CPUDensityManager.MapData[] chunk) => 
            RequestQueue.Enqueue(new GenTask{  
                task = () => OnReadComplete(isComplete, chunk),
                id = (int)priorities.generation,
                chunk = this,
            })
        );

        //This code will run on main thread
        void OnReadComplete(bool isComplete, CPUDensityManager.MapData[] chunk){
            //if(isComplete) SetChunkData(0, chunk, callback);
            GenerateMap(callback);
        }
    }

    private void CreateMeshImmediate(Action<AsyncMeshReadback.SharedMeshInfo> UpdateCallback = null){
        Generator.MeshCreator.GenerateMapDataInPlace(IsoLevel, meshSkipInc, mapChunkSize);
        ClearFilter();
        
        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.terrain);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, bufferOffsets.dictStart, (int)GeneratorInfo.ReadbackMaterial.water);
        Generator.MeshReadback.BeginMeshReadback(UpdateCallback);

        if (depth <= rSettings.MaxGeoShaderDepth)
            Generator.GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)GeneratorInfo.ReadbackMaterial.terrain]);
        else
            Generator.GeoShaders.ReleaseGeometry();
    }
}

[System.Serializable]
public struct LODInfo
{
    public int chunkDistThresh;
    public bool useForGeoShaders;
}