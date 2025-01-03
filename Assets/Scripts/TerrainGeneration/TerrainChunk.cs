using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RBMeshes;
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

        status = new ChunkStatus{CreateMap = true, UpdateMesh = true, CanUpdateMesh = false};
        Generator = new GeneratorInfo(this);
        SetupChunk();
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

    public virtual void VerifyChunk(){
        if(!active) return;
        //These two functions are self-verifying, so they will only execute if necessary
        OctreeTerrain.SubdivideChunk(index);
        OctreeTerrain.MergeSiblings(index);
    }

    public void OnChunkCreated(SharedMeshInfo<TVert> meshInfo)
    {
        meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);;
        meshInfo.Release();
    }

    //Chunk Becomes a Zombie
    public void Kill()
    {
        if(!active) return;
        active = false;

        ReapChunk(index);
    }

    public void Destroy()
    {
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

    public virtual void ReleaseChunk(){
        Generator.GeoShaders?.ReleaseGeometry(); //Release geoShader Geometry
        Generator.MeshReadback?.ReleaseAllGeometry(); //Release base geometry on GPU
        Generator.StructCreator?.ReleaseStructure(); //Release structure data
        if(this.surfAddress != 0) GenerationPreset.memoryHandle.ReleaseMemory(surfAddress); //Release surface data
    }
}

//depth = 0
public class RealChunk : TerrainChunk{
    private bool IsBordering = false;
    public RealChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex){
        IsBordering = OctreeTerrain.IsBordering(ref octree.nodes[index]);
    }

    public override void VerifyChunk(){
        base.VerifyChunk();
        if(!IsBordering) return;
        IsBordering = OctreeTerrain.IsBordering(ref octree.nodes[index]);
        if(IsBordering) return;
        status.UpdateMesh = true;
    }

    public override void Update()
    {
        base.Update();
        if(status.CreateMap){
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{
                task =() => ReadMapData(),
                id = (int)priorities.generation,
                chunk = this
            });
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
    
    public void ReadMapData(Action callback = null){
        //This code will be called on a background thread
        ChunkStorageManager.ReadbackInfo info = ChunkStorageManager.ReadChunkInfo(CCoord);

        if(info.map != null){ //if the chunk has saved map data
            Generator.MeshCreator.SetMapInfo(mapChunkSize, 0, info.map);
            GPUDensityManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.TransferBuffer);
            if(info.entities == null) Generator.MeshCreator.PopulateBiomes(origin, surfAddress, mapChunkSize, mapSkipInc);
        } else { //Otherwise create new data
            DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
            Generator.MeshCreator.GenerateBaseChunk(origin, surfAddress, mapChunkSize, mapSkipInc, IsoLevel);
            Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize, mapSkipInc, bufferOffsets.rawMapStart, IsoLevel);
            Generator.MeshCreator.CompressMap(mapChunkSize);
            GPUDensityManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.GenerationBuffer, DensityGenerator.bufferOffsets.mapStart);
        }

        EntityManager.ReleaseChunkEntities(CCoord);//Clear previous chunk's entities
        if(info.entities != null) { //If the chunk has saved entities
            EntityManager.DeserializeEntities(info.entities, CCoord);
        }else { //Otherwise create new entities
            uint entityAddress = EntityManager.PlanEntities(DensityGenerator.bufferOffsets.biomeMapStart, CCoord, mapChunkSize);
            EntityManager.BeginEntityReadback(entityAddress, CCoord);
        }

        //Copy real chunks to CPU
        int3 CCoord_Captured = CCoord;
        CPUDensityManager.AllocateChunk(this, CCoord, () => CPUDensityManager.BeginMapReadback(CCoord_Captured));
        callback?.Invoke();
    }

    public void CreateMesh( Action<SharedMeshInfo<TVert>> UpdateCallback = null){
        Generator.MeshCreator.GenerateRealMesh(CCoord, IsoLevel, mapChunkSize);
        ClearFilter();
        ReapChunk(index);
        
        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;

        Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)GeneratorInfo.ReadbackMaterial.terrain);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)GeneratorInfo.ReadbackMaterial.water);
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
    private int mapHandle;
    public VisualChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex){
        sOrigin = origin - mapSkipInc;
        sChunkSize = mapChunkSize + 3;
        mapHandle = -1;
    }

    public override void Update()
    {
        base.Update();
        if(status.CreateMap){
            //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
            //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
            status.UpdateMap = false;
            RequestQueue.Enqueue(new GenTask{
                task =() => ReadMapData(),
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
        //Set chunk data will only be used if info is modified, which 
        //it shouldn't be for visual chunks
        
        //Visual chunks cannot independently update their mesh because
        //their map data may not be cached, so it must be done immediately
    }

    public override void ReleaseChunk(){
        //if we are still holding onto the map handle, release it
        if(mapHandle != -1) GPUDensityManager.UnsubscribeHandle((uint)mapHandle);
        base.ReleaseChunk();
    }
    
    public override void GetSurface()
    {
        SurfaceCreator.SampleSurfaceMaps(sOrigin.xz, sChunkSize, mapSkipInc);
        this.surfAddress = SurfaceCreator.StoreSurfaceMap(sChunkSize);
    }
    public override void PlanStructures(Action callback = null){
        if(depth > rSettings.MaxStructureDepth) return;
        Generator.StructCreator.PlanStructuresGPU(CCoord, origin, mapChunkSize, IsoLevel, depth);
        callback?.Invoke();
    }

    private void GenerateDefaultMap(){
        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        Generator.MeshCreator.GenerateBaseChunk(sOrigin, surfAddress, sChunkSize, mapSkipInc, IsoLevel);
        if(depth <= rSettings.MaxStructureDepth)
            Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize+1, mapSkipInc, bufferOffsets.rawMapStart, IsoLevel, sChunkSize, 1);
        Generator.MeshCreator.CompressMap(sChunkSize);
    }
    
    public void ReadMapData(Action<SharedMeshInfo<TVert>> callback = null){

        if(!GPUDensityManager.IsChunkRegisterable(CCoord, depth)) { callback?.Invoke(null); return; }
        GenerateDefaultMap();

        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        if(mapHandle != -1) GPUDensityManager.UnsubscribeHandle((uint)mapHandle);
        mapHandle = GPUDensityManager.RegisterChunkVisual(CCoord, depth, UtilityBuffers.GenerationBuffer, bufferOffsets.mapStart);
        if(mapHandle == -1) return; 

        CPUDensityManager.MapData[] info = ChunkStorageManager.ReadVisualChunkMap(CCoord, depth);
        Generator.MeshCreator.SetMapInfo(mapChunkSize, 0, info);
        GPUDensityManager.TranscribeMultiMap(UtilityBuffers.TransferBuffer, CCoord, depth);
        //Subscribe once more so the chunk can't be released while we hold its handle
        GPUDensityManager.SubscribeHandle((uint)mapHandle);
    }


    private void CreateMesh(Action<SharedMeshInfo<TVert> > UpdateCallback = null){
        if(mapHandle == -1){
            GenerateDefaultMap(); //We need the default mesh to be immediately in the buffer
            Generator.MeshCreator.GenerateFakeMesh(IsoLevel, mapChunkSize);
        } else {
            int directAddress = (int)GPUDensityManager.GetHandle(mapHandle).x;
            Generator.MeshCreator.GenerateVisualMesh(CCoord, directAddress, IsoLevel, mapChunkSize, depth);
            GPUDensityManager.UnsubscribeHandle((uint)mapHandle); 
            mapHandle = -1;
        }

        ReapChunk(index); 
        ClearFilter();
        
        DensityGenerator.GeoGenOffsets bufferOffsets = DensityGenerator.bufferOffsets;
        Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)GeneratorInfo.ReadbackMaterial.terrain);
        Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)GeneratorInfo.ReadbackMaterial.water);
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
