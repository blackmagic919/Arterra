using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static EndlessTerrain;
using Unity.Mathematics;

public static class GPUDensityManager
{
    private static ComputeShader dictReplaceKey;
    private static ComputeShader transcribeMapInfo;
    private static ComputeShader simplifyMap;
    private static ComputeBuffer _ChunkAddressDict;
    private static SegmentBinManager memorySpace;
    private static int numChunksAxis;
    private static int mapChunkSize;
    private static float lerpScale;
    private const int pointStride4Byte = 1; //Only density for now
    public static Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();//
    public static bool initialized = false;

    public static void Initialize(LODInfo[] detaiLevels, int mapChunkSize, float lerpScale)
    {
        Release();
        
        dictReplaceKey = Resources.Load<ComputeShader>("MapData/ReplaceDictChunk");
        transcribeMapInfo = Resources.Load<ComputeShader>("MapData/TranscribeMapInfo");
        simplifyMap = Resources.Load<ComputeShader>("MapData/DensitySimplificator");

        GPUDensityManager.lerpScale = lerpScale;

        GPUDensityManager.mapChunkSize = mapChunkSize;
        GPUDensityManager.numChunksAxis = 2 * (Mathf.RoundToInt(detaiLevels[^1].distanceThresh / mapChunkSize) + 1);
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

        GPUDensityManager._ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint) * 2, ComputeBufferType.Structured);
        GPUDensityManager._ChunkAddressDict.SetData(Enumerable.Repeat(0u, numChunks * 2).ToArray());
        GPUDensityManager.memorySpace = new SegmentBinManager(mapChunkSize, detaiLevels, pointStride4Byte);

        initialized = true;
    }

    public static int HashCoord(int3 CCoord){
        float xHash = CCoord.x < 0 ? numChunksAxis - (Mathf.Abs(CCoord.x) % numChunksAxis) : Mathf.Abs(CCoord.x) % numChunksAxis;
        float yHash = CCoord.y < 0 ? numChunksAxis - (Mathf.Abs(CCoord.y) % numChunksAxis) : Mathf.Abs(CCoord.y) % numChunksAxis;
        float zHash = CCoord.z < 0 ? numChunksAxis - (Mathf.Abs(CCoord.z) % numChunksAxis) : Mathf.Abs(CCoord.z) % numChunksAxis;

        int hash = ((int)xHash * numChunksAxis * numChunksAxis) + ((int)yHash * numChunksAxis) + (int)zHash;
        return hash;
    }

    public static ComputeBuffer AccessStorage(){
        return memorySpace.AccessStorage();
    }

    public static ComputeBuffer AccessAddresses(){
        return _ChunkAddressDict;
    }

    public static void Release()
    {
        _ChunkAddressDict?.Release();
        memorySpace?.Release();
        initialized = false;
    }

    public static void SubscribeChunk(int3 CCoord, int LOD, ComputeBuffer mapData, bool compressed = false)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxis = mapChunkSize / meshSkipInc;
        int numPoints = numPointsAxis * numPointsAxis * numPointsAxis;

        ComputeBuffer address = memorySpace.AllocateChunk(LOD);
        TranscribeData(memorySpace.AccessStorage(), address, mapData, numPoints, compressed);

        ReplaceAddress(address, CCoord, meshSkipInc);
        memorySpace.ReleaseChunk(address);
    }


    static void TranscribeData(ComputeBuffer memory, ComputeBuffer address, ComputeBuffer mapData, int numPoints, bool compressed)
    {
        if(compressed) transcribeMapInfo.EnableKeyword("COMPRESSED");
        else transcribeMapInfo.DisableKeyword("COMPRESSED");

        transcribeMapInfo.SetBuffer(0, "_MemoryBuffer", memory);
        transcribeMapInfo.SetBuffer(0, "chunkData", mapData);
        transcribeMapInfo.SetBuffer(0, "startAddress", address); 
        transcribeMapInfo.SetInt("numPoints", numPoints);

        transcribeMapInfo.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        transcribeMapInfo.Dispatch(0, numThreadsAxis, 1, 1);
    }

    static void ReplaceAddress(ComputeBuffer memoryAddress, int3 CCoord, int meshSkipInc)
    {
        dictReplaceKey.SetBuffer(0, "chunkAddressDict", _ChunkAddressDict);
        dictReplaceKey.SetBuffer(0, "nAddress", memoryAddress);
        dictReplaceKey.SetInts("CCoord", new int[3] { CCoord.x, CCoord.y, CCoord.z });
        dictReplaceKey.SetInt("meshSkipInc", meshSkipInc);

        SetCCoordHash(dictReplaceKey);

        dictReplaceKey.Dispatch(0, 1, 1, 1);
    }

    //Caller guarantees LoD is greater than the LoD of the chunk at CCoord
    public static void SimplifyChunk(int3 CCoord, int LOD){
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer address = memorySpace.AllocateChunk(LOD);
        
        ReplaceAddress(address, CCoord, meshSkipInc);
        SimplifyDataDirect(memorySpace.AccessStorage(), address, CCoord, meshSkipInc);
        memorySpace.ReleaseChunk(address);
    }

    static void SimplifyDataDirect(ComputeBuffer memory, ComputeBuffer oAddress, int3 CCoord, int meshSkipInc){
        int numPointsAxis = mapChunkSize / meshSkipInc;

        simplifyMap.SetBuffer(0, "_MemoryBuffer", memory);
        simplifyMap.SetBuffer(0, "chunkAddressDict", _ChunkAddressDict);
        simplifyMap.SetBuffer(0, "oAddress", oAddress);
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
        shader.SetBuffer(0, "_ChunkInfoBuffer", memorySpace.AccessStorage());
    }

    public static void SetDensitySampleData(Material material){
        if(!initialized)
            return;
            
        SetCCoordHash(material);
        SetWSCCoordHelper(material);

        material.SetBuffer("_ChunkAddressDict", _ChunkAddressDict);
        material.SetBuffer("_ChunkInfoBuffer", memorySpace.AccessStorage());
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
