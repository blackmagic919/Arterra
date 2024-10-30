using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;

public static class GPUDensityManager
{
    private static ComputeShader dictReplaceKey;
    private static ComputeShader transcribeMapInfo;
    private static ComputeShader simplifyMap;
    private static uint[] MapLookup;
    private static uint2[] HandleDict;
    private static ComputeBuffer _ChunkAddressDict;
    private static GenerationPreset.MemoryHandle memorySpace;
    public static int numChunksRadius;
    public static int numChunksAxis;
    public static int mapChunkSize;
    private static float lerpScale;
    private const int pointStride4Byte = 1; //Only density for now
    public static bool initialized = false;

    public static void Initialize()
    {
        Release();
        RenderSettings rSettings = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        dictReplaceKey = Resources.Load<ComputeShader>("Compute/MapData/ReplaceDictChunk");
        transcribeMapInfo = Resources.Load<ComputeShader>("Compute/MapData/TranscribeMapInfo");
        simplifyMap = Resources.Load<ComputeShader>("Compute/MapData/DensitySimplificator");
        
        lerpScale = rSettings.lerpScale;
        int BaseMapLength = Octree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
        int MapDiameter = BaseMapLength + rSettings.MapExtendDist*2;

        mapChunkSize = rSettings.mapChunkSize;
        numChunksRadius = (MapDiameter + MapDiameter % 1)/2;
        numChunksAxis = numChunksRadius * 2;
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

        MapLookup = new uint[numChunks];
        HandleDict = new uint2[numChunks + 1];
        for(uint i = 1; i < numChunks + 1; i++){
            HandleDict[i] = new(i - 1, (uint)((i + 1) % HandleDict.Length));
        } HandleDict[1].x = (uint)numChunks;
        HandleDict[0].y = 1; //Free position

        _ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint) * 2, ComputeBufferType.Structured);
        _ChunkAddressDict.SetData(Enumerable.Repeat(0u, numChunks * 2).ToArray());
        memorySpace = GenerationPreset.memoryHandle;

        initialized = true;
    }
    public static void RegisterChunkVisual(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0){
        RegisterChunk(oCCoord, depth, (uint address) => TranscribeData(memorySpace.Storage, memorySpace.Address, mapData, address, rdOff, mapChunkSize+3, mapChunkSize, 1));
    }
    public static void RegisterChunkReal(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0){
        RegisterChunk(oCCoord, depth, (uint address) => TranscribeData(memorySpace.Storage, memorySpace.Address, mapData, address, rdOff, mapChunkSize, mapChunkSize));
    }

    //Origin Chunk Coord, Viewer Chunk Coord(where the map is centered), depth
    private static void RegisterChunk(int3 oCCoord, int depth, Action<uint> Transcribe){
        int3 eCCoord = oCCoord + (1 << depth); int3 cOff = oCCoord;
        int3 vCCoord = OctreeTerrain.ChunkPos;
        oCCoord = math.clamp(oCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        eCCoord = math.clamp(eCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        cOff = oCCoord - cOff;
        int3 dim = eCCoord - oCCoord;
        if(math.any(dim <= 0)) return; //We're not going to save it
        
        //Request Memory Address
        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        uint address = memorySpace.AllocateMemoryDirect(numPoints, 1);
        Transcribe(address);

        uint handleAddress = AllocateHandle();
        uint count = (uint)(dim.x * dim.y * dim.z);
        HandleDict[handleAddress] = new uint2(address, count);

        int3 dCoord; 
        for(dCoord.x = 0; dCoord.x < dim.x; dCoord.x++){
            for(dCoord.y = 0; dCoord.y < dim.y; dCoord.y++){
                for(dCoord.z = 0; dCoord.z < dim.z; dCoord.z++){
                    int hash = HashCoord(dCoord + oCCoord);
                    uint prevAdd = MapLookup[hash];
                    MapLookup[hash] = handleAddress;
                    if(prevAdd == 0) continue;
                    HandleDict[prevAdd].y--;
                    if(HandleDict[prevAdd].y > 0) continue;
                    //Release chunk
                    memorySpace.ReleaseMemory(HandleDict[prevAdd].x);
                    FreeHandle(prevAdd);
                }
            }
        }
        //Fill out Compute Buffer Memory Handles
        ReplaceAddress(memorySpace.Address, address, oCCoord, dim, cOff, depth);
    }

    private static uint AllocateHandle(){
        uint free = HandleDict[0].y;
        HandleDict[HandleDict[free].x].y = HandleDict[free].y;
        HandleDict[HandleDict[free].y].x = HandleDict[free].x;
        HandleDict[0].y = HandleDict[free].y;
        return free;
    }

    private static void FreeHandle(uint index){
        uint free = HandleDict[0].y;
        HandleDict[index] = new uint2(HandleDict[free].x, free);
        HandleDict[HandleDict[free].x].y = index;
        HandleDict[free].x = index;
        HandleDict[0].y = index;
    }

    public static int HashCoord(int3 CCoord){
        int3 hashCoord = ((CCoord % numChunksAxis) + numChunksAxis) % numChunksAxis;
        int hash = (hashCoord.x * numChunksAxis * numChunksAxis) + (hashCoord.y * numChunksAxis) + hashCoord.z;
        return hash;
    }

    public static ComputeBuffer Storage => memorySpace.Storage;
    public static ComputeBuffer Address => _ChunkAddressDict;

    public static void Release()
    {
        _ChunkAddressDict?.Release();
        initialized = false;
    }


    static void TranscribeData(ComputeBuffer memory, ComputeBuffer addressDict, ComputeBuffer mapData, uint address, int rStart, int readAxis, int writeAxis, int rdOff = 0)
    {
        transcribeMapInfo.SetBuffer(0, "_MemoryBuffer", memory);
        transcribeMapInfo.SetBuffer(0, "_AddressDict", addressDict);
        transcribeMapInfo.SetBuffer(0, "chunkData", mapData); 
        transcribeMapInfo.SetInt("addressIndex", (int)address);
        transcribeMapInfo.SetInt("bSTART_read", rStart);
        transcribeMapInfo.SetInt("sizeRdAxis", readAxis);
        transcribeMapInfo.SetInt("sizeWrAxis", writeAxis);
        transcribeMapInfo.SetInt("sqRdOffset", rdOff);

        int numPointsAxis = writeAxis;
        transcribeMapInfo.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
        transcribeMapInfo.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    static void ReplaceAddress(ComputeBuffer addressDict, uint address, int3 oCoord, int3 dimension, int3 offC, int depth)
    {
        dictReplaceKey.SetBuffer(0, "ChunkDict", _ChunkAddressDict);
        dictReplaceKey.SetBuffer(0, "_AddressDict", addressDict);
        dictReplaceKey.SetInt("addressIndex", (int)address);
        dictReplaceKey.SetInts("oCoord", new int[3] { oCoord.x, oCoord.y, oCoord.z });
        dictReplaceKey.SetInts("dimension", new int[3] { dimension.x, dimension.y, dimension.z });

        int skipInc = 1 << depth;
        int chunkInc = mapChunkSize / skipInc;
        int offset = ((offC.x * chunkInc & 0xFF) << 24) | ((offC.y * chunkInc & 0xFF) << 16) | ((offC.z * chunkInc & 0xFF) << 8);
        dictReplaceKey.SetInt("skipInc", skipInc);
        dictReplaceKey.SetInt("chunkInc", chunkInc);
        dictReplaceKey.SetInt("bOffset", offset);

        SetCCoordHash(dictReplaceKey);

        dictReplaceKey.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int3 numThreadsAxis = (int3)math.ceil((float3)dimension / threadGroupSize);
        dictReplaceKey.Dispatch(0, numThreadsAxis.x, numThreadsAxis.y, numThreadsAxis.z);
    }

    //Caller guarantees LoD is greater than the LoD of the chunk at CCoord
    public static void SimplifyChunk(int3 CCoord, int meshSkipInc){
        int numPointsAxis = mapChunkSize / meshSkipInc;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        uint address = memorySpace.AllocateMemoryDirect(numPoints, 1);
        
        //ReplaceAddress(memorySpace.Address, address, CCoord, meshSkipInc);
        SimplifyDataDirect(memorySpace.Storage, memorySpace.Address, address, CCoord, meshSkipInc);
        memorySpace.ReleaseMemory(address);
    }

    static void SimplifyDataDirect(ComputeBuffer memory, ComputeBuffer addressDict, uint address, int3 CCoord, int meshSkipInc){
        int numPointsAxis = mapChunkSize / meshSkipInc;

        simplifyMap.SetBuffer(0, "_MemoryBuffer", memory);
        simplifyMap.SetBuffer(0, "chunkAddressDict", _ChunkAddressDict);
        simplifyMap.SetBuffer(0, "_AddressDict", addressDict);
        simplifyMap.SetInt("addressIndex", (int)address);
        simplifyMap.SetInts("CCoord", new int[3] { CCoord.x, CCoord.y, CCoord.z });
        simplifyMap.SetInt("numPointsPerAxis", numPointsAxis);

        SetCCoordHash(simplifyMap);

        simplifyMap.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
        simplifyMap.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }


    public static void SetDensitySampleData(ComputeShader shader) {
        if(!initialized)
            return;

        SetCCoordHash(shader);
        SetWSCCoordHelper(shader);

        shader.SetBuffer(0, "_ChunkAddressDict", _ChunkAddressDict);
        shader.SetBuffer(0, "_ChunkInfoBuffer", memorySpace.Storage);
    }

    public static void SetDensitySampleData(Material material){
        if(!initialized)
            return;
            
        SetCCoordHash(material);
        SetWSCCoordHelper(material);

        material.SetBuffer("_ChunkAddressDict", _ChunkAddressDict);
        material.SetBuffer("_ChunkInfoBuffer", memorySpace.Storage);
    }

    static void SetWSCCoordHelper(ComputeShader shader) { 
        shader.SetFloat("lerpScale", lerpScale);
        shader.SetInt("mapChunkSize", mapChunkSize);
    }

    static void SetWSCCoordHelper(Material material) { 
        material.SetFloat("lerpScale", lerpScale);
        material.SetInt("mapChunkSize", mapChunkSize);
    }
    public static void SetCCoordHash(ComputeShader shader) { shader.SetInt("numChunksAxis", numChunksAxis); }
    public static void SetCCoordHash(Material material) { material.SetInt("numChunksAxis", numChunksAxis); }
    
    /*
    public static void BeginMapReadback(int3 CCoord, TerrainChunk dest){ //Need a wrapper class to maintain reference to the native array
        int chunkHash = HashCoord(CCoord);

        AsyncGPUReadback.Request(_ChunkAddressDict, size: 8, offset: 8 * chunkHash, ret => onChunkAddressRecieved(ret, dest));
    }

    static void onChunkAddressRecieved(AsyncGPUReadbackRequest request, TerrainChunk dest){
        uint2 memHandle = request.GetData<uint2>().ToArray()[0];

        int memAddress = (int)memHandle.x;
        int meshSkipInc = (int)memHandle.y;
        int numPointsAxis = mapChunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        if(dest.storedMap.map.IsCreated) dest.storedMap.map.Dispose();
        dest.storedMap.map = new NativeArray<TerrainChunk.MapData>(numPoints, Allocator.Persistent, NativeArrayOptions.UninitializedMemory);
        
        AsyncGPUReadback.RequestIntoNativeArray(ref dest.storedMap.map, memorySpace.AccessStorage(), size: 4 * pointStride4Byte * numPoints, offset: 4 * memAddress, (ret) => onChunkDataRecieved(dest));
    }

    static void onChunkDataRecieved(TerrainChunk dest){ 
        dest.storedMap.valid = true;
    }*/

}
