using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using TerrainGeneration;
using WorldConfig;

public static class GPUMapManager
{
    private static ComputeShader dictReplaceKey;
    private static ComputeShader transcribeMapInfo;
    private static ComputeShader multiMapTranscribe;
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
        WorldConfig.Quality.Terrain rSettings = Config.CURRENT.Quality.Terrain.value;
        dictReplaceKey = Resources.Load<ComputeShader>("Compute/MapData/ReplaceDictChunk");
        transcribeMapInfo = Resources.Load<ComputeShader>("Compute/MapData/TranscribeMapInfo");
        multiMapTranscribe = Resources.Load<ComputeShader>("Compute/MapData/MultiMapTranscriber");
        simplifyMap = Resources.Load<ComputeShader>("Compute/MapData/DensitySimplificator");
        
        lerpScale = rSettings.lerpScale;
        int BaseMapLength = OctreeTerrain.Octree.GetAxisChunksDepth(0, rSettings.Balance, (uint)rSettings.MinChunkRadius);
        int MapDiameter = BaseMapLength + rSettings.MapExtendDist*2;

        mapChunkSize = rSettings.mapChunkSize;
        numChunksRadius = (MapDiameter + MapDiameter % 2)/2;
        numChunksAxis = numChunksRadius * 2;
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;

        MapLookup = new uint[numChunks];
        HandleDict = new uint2[numChunks + 1];
        for(uint i = 1; i < numChunks + 1; i++){
            HandleDict[i] = new(i - 1, (uint)((i + 1) % HandleDict.Length));
        } HandleDict[1].x = (uint)numChunks;
        HandleDict[0].y = 1; //Free position
        
        _ChunkAddressDict = new ComputeBuffer(numChunks, sizeof(uint) * 5, ComputeBufferType.Structured);
        _ChunkAddressDict.SetData(Enumerable.Repeat(0u, numChunks * 5).ToArray());
        memorySpace = GenerationPreset.memoryHandle;

        initialized = true;
    }
    public static int RegisterChunkVisual(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0){
        if(!IsChunkRegisterable(oCCoord, depth)) return -1;
        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        int numPointsFaces = (mapChunkSize+3) * (mapChunkSize+3) * 9;
        int mapSize = numPoints + numPointsFaces + LightBaker.GetLightMapLength();
        uint address = memorySpace.AllocateMemoryDirect(mapSize, 1);
        TranscribeMap(mapData, address, rdOff, mapChunkSize+3, mapChunkSize, 1);
        TranscribeEdgeFaces(mapData, address, rdOff, mapChunkSize+3, mapChunkSize+3, numPoints);
        uint handleAddress = AllocateHandle(); HandleDict[handleAddress] = new uint2(address, 0);
        LightBaker.RegisterChunk(oCCoord, mapChunkSize, address);

        RegisterChunk(oCCoord, depth, handleAddress);
        return (int)handleAddress;
    }
    public static int RegisterChunkReal(int3 oCCoord, int depth, ComputeBuffer mapData, int rdOff = 0){
        if(!IsChunkRegisterable(oCCoord, depth)) return -1;
        int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
        //This is unnecessary here, but we need to ensure it so that other systems(e.g. Lighting)
        //Have a consistent chunk size to buffer with
        int numPointsFaces = (mapChunkSize+3) * (mapChunkSize+3) * 9;
        int mapSize = numPoints + numPointsFaces + LightBaker.GetLightMapLength();
        uint address = memorySpace.AllocateMemoryDirect(mapSize, 1);
        TranscribeMap(mapData, address, rdOff, mapChunkSize, mapChunkSize);
        uint handleAddress = AllocateHandle(); HandleDict[handleAddress] = new uint2(address, 0);
        LightBaker.RegisterChunk(oCCoord, mapChunkSize, address, CopyMap: true);

        RegisterChunk(oCCoord, depth, handleAddress);
        return (int)handleAddress;
    }

    public static uint2 GetHandle(int handleAddress) => HandleDict[handleAddress];
    public static void SubscribeHandle(uint handleAddress) => HandleDict[handleAddress].y++;
    public static void UnsubscribeHandle(uint handleAddress){
        HandleDict[handleAddress].y--;
        if(HandleDict[handleAddress].y > 0) return; 
        //Release chunk if no one is subscribed
        memorySpace.ReleaseMemory(HandleDict[handleAddress].x);
        FreeHandle(handleAddress);
    }

    //Origin Chunk Coord, Viewer Chunk Coord(where the map is centered), depth
    private static void RegisterChunk(int3 oCCoord, int depth, uint handleAddress){
        int3 eCCoord = oCCoord + (1 << depth); int3 cOff = oCCoord;
        int3 vCCoord = TerrainGeneration.OctreeTerrain.ViewPosCS;
        oCCoord = math.clamp(oCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        eCCoord = math.clamp(eCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        cOff = oCCoord - cOff;
        int3 dim = eCCoord - oCCoord;
        if(math.any(dim <= 0)) return; //We're not going to save it
        
        //Request Memory Address
        uint count = (uint)(dim.x * dim.y * dim.z);
        HandleDict[handleAddress].y += count;

        int3 dCoord; 
        for(dCoord.x = 0; dCoord.x < dim.x; dCoord.x++){
        for(dCoord.y = 0; dCoord.y < dim.y; dCoord.y++){
        for(dCoord.z = 0; dCoord.z < dim.z; dCoord.z++){
            int hash = HashCoord(dCoord + oCCoord);
            uint prevAdd = MapLookup[hash];
            MapLookup[hash] = handleAddress;
            if(prevAdd == 0) continue;
            UnsubscribeHandle(prevAdd);
        }}}
        //Fill out Compute Buffer Memory Handles
        ReplaceAddress(memorySpace.Address, HandleDict[handleAddress].x, oCCoord, dim, cOff, depth);
    }

    public static bool IsChunkRegisterable(int3 oCCoord, int depth){
        int3 eCCoord = oCCoord + (1 << depth); 
        int3 vCCoord = TerrainGeneration.OctreeTerrain.ViewPosCS;
        oCCoord = math.clamp(oCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        eCCoord = math.clamp(eCCoord, vCCoord - numChunksRadius, vCCoord + numChunksRadius + 1);
        int3 dim = eCCoord - oCCoord;
        if(math.any(dim <= 0)) return false;
        else return true;
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
    public static ComputeBuffer DirectAddress => memorySpace.Address;

    public static void Release()
    {
        _ChunkAddressDict?.Release();
        initialized = false;
    }


    static void TranscribeMap(ComputeBuffer mapData, uint address, int rStart, int readAxis, int writeAxis, int rdOff = 0)
    {
        int kernel = transcribeMapInfo.FindKernel("TranscribeMap");
        transcribeMapInfo.SetBuffer(kernel, "_MemoryBuffer", memorySpace.Storage);
        transcribeMapInfo.SetBuffer(kernel, "_AddressDict", memorySpace.Address);
        transcribeMapInfo.SetBuffer(kernel, "chunkData", mapData); 
        transcribeMapInfo.SetInt("addressIndex", (int)address);
        transcribeMapInfo.SetInt("bSTART_read", rStart);
        transcribeMapInfo.SetInt("sizeRdAxis", readAxis);
        transcribeMapInfo.SetInt("sizeWrAxis", writeAxis);
        transcribeMapInfo.SetInt("offset", rdOff);

        int numPointsAxis = writeAxis;
        transcribeMapInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
        transcribeMapInfo.Dispatch(kernel, numThreadsAxis, numThreadsAxis, numThreadsAxis);
    }

    static void TranscribeEdgeFaces(ComputeBuffer mapData, uint address, int rStart, int readAxis, int writeAxis, int wrOff = 0){
        int kernel = transcribeMapInfo.FindKernel("TranscribeFaces");
        transcribeMapInfo.SetBuffer(kernel, "_MemoryBuffer",  memorySpace.Storage);
        transcribeMapInfo.SetBuffer(kernel, "_AddressDict", memorySpace.Address);
        transcribeMapInfo.SetBuffer(kernel, "chunkData", mapData); 
        transcribeMapInfo.SetInt("addressIndex", (int)address);
        transcribeMapInfo.SetInt("bSTART_read", rStart);

        transcribeMapInfo.SetInt("sizeRdAxis", readAxis);
        transcribeMapInfo.SetInt("sizeWrAxis", writeAxis);
        transcribeMapInfo.SetInt("offset", wrOff);

        int numPointsAxis = writeAxis;
        transcribeMapInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);
        transcribeMapInfo.Dispatch(kernel, numThreadsAxis, numThreadsAxis, 1);
    }

    public static void TranscribeMultiMap(ComputeBuffer mapData, int3 CCoord, int depth, int rStart = 0){
        int skipInc = 1 << depth;
        int numPointsPerAxis = mapChunkSize;

        multiMapTranscribe.SetBuffer(0, "_MemoryBuffer", memorySpace.Storage);
        multiMapTranscribe.SetBuffer(0, "_AddressDict", _ChunkAddressDict);
        multiMapTranscribe.SetBuffer(0, "MapData", mapData);
        multiMapTranscribe.SetInts("oCCoord", new int[3] { CCoord.x, CCoord.y, CCoord.z });
        multiMapTranscribe.SetInt("numPointsPerAxis", numPointsPerAxis);
        multiMapTranscribe.SetInt("bSTART_map", rStart);
        multiMapTranscribe.SetInt("mapChunkSize", mapChunkSize);
        multiMapTranscribe.SetInt("skipInc", skipInc);
        SetCCoordHash(multiMapTranscribe);

        multiMapTranscribe.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsPerAxis / (float)threadGroupSize);
        multiMapTranscribe.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
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
        int offset = ((offC.x * chunkInc & 0xFF) << 24) | ((offC.y * chunkInc & 0xFF) << 16) | ((offC.z * chunkInc & 0xFF) << 8) | (skipInc & 0xFF);
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