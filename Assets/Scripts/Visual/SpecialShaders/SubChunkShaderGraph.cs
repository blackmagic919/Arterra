using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Config;
using Arterra.Config.Quality;
using Arterra.Core.Terrain;
using Arterra.Core.Terrain.Readback;

public class SubChunkShaderGraph{
    public TerrainChunk parent;
    public FixedOctree tree;
    private static List<GeoShader> shaders => rSettings.Categories.Reg;
    private static GeoShaderSettings rSettings => Config.CURRENT.Quality.GeoShaders.value;
    private static ComputeShader geoSizeCounter;
    private static ComputeShader filterGeometry;
    private static ComputeShader sizePrefixSum;
    private static ComputeShader geoSizeCalculator;
    private static ComputeShader geoTranscriber;
    private static ComputeShader shaderDrawArgs;
    private static ComputeShader geoInfoLoader;
    private static ComputeShader subChunkInfo;
    private static GeoShaderOffsets offsets;
    private static LogicalBlockBuffer SortedSubChunks;
    private IndirectUpdate executor;
    private BaseGeoHandle baseHandle;
    private static int SubChunkSizeOS;
    private static int SubChunksPerAxis;
    private static int NumSubChunks => SubChunksPerAxis * SubChunksPerAxis * SubChunksPerAxis;
    const int GEO_TRI_STRIDE = 3*3;
    const int GEN_TRI_STRIDE = 3*3+1;
    const int TRI_STRIDE_WORD = 3;
    public static void PresetData() {
        geoSizeCounter = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderMatSizeCounter");
        filterGeometry = Resources.Load<ComputeShader>("Compute/GeoShader/FilterShaderGeometry");
        sizePrefixSum = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderPrefixConstructor");
        geoSizeCalculator = Resources.Load<ComputeShader>("Compute/GeoShader/GeometryMemorySize");
        geoTranscriber = Resources.Load<ComputeShader>("Compute/GeoShader/TranscribeGeometry");
        shaderDrawArgs = Resources.Load<ComputeShader>("Compute/GeoShader/GeoDrawArgs");
        geoInfoLoader = Resources.Load<ComputeShader>("Compute/GeoShader/GeoInfoLoader");
        subChunkInfo = Resources.Load<ComputeShader>("Compute/GeoShader/SubChunkInfo");

        int maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int maxSubChunkDepth = FixedOctree.GetMaxDepth(rSettings.levels.value);
        offsets = new GeoShaderOffsets(maxChunkSize, rSettings.Categories.Count(),
            maxSubChunkDepth, 0);
        
        SubChunksPerAxis = 1 << maxSubChunkDepth;
        SubChunkSizeOS = maxChunkSize / SubChunksPerAxis;

        Arterra.Config.Quality.Terrain terrain = Config.CURRENT.Quality.Terrain;
        int numChunksAxis = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(rSettings.MaxGeoShaderDepth, terrain.Balance, (uint)terrain.MinChunkRadius);
        int numChunks = numChunksAxis * numChunksAxis * numChunksAxis;
        SortedSubChunks = new LogicalBlockBuffer(GraphicsBuffer.Target.Structured, numChunks * 2, sizeof(uint) * (NumSubChunks + 1));

        int kernel = geoInfoLoader.FindKernel("GetBaseSize");
        geoInfoLoader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetInt("bCOUNT_tri", offsets.baseGeoCounter);
        geoInfoLoader.SetInt("bCOUNT_offset", offsets.baseGeoOffset);
        geoInfoLoader.SetInt("triStride", TRI_STRIDE_WORD);
        kernel = geoInfoLoader.FindKernel("GetSubChunkSize");
        geoInfoLoader.SetBuffer(kernel, "SubChunkPrefix", SortedSubChunks.Get());
        geoInfoLoader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);

        kernel = subChunkInfo.FindKernel("SetSubChunkDetail");
        subChunkInfo.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        subChunkInfo.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        kernel = subChunkInfo.FindKernel("CollectSubChunkSizes");
        subChunkInfo.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        subChunkInfo.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        kernel = subChunkInfo.FindKernel("SetSubChunkAddress");
        subChunkInfo.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        subChunkInfo.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        subChunkInfo.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);
        kernel = subChunkInfo.FindKernel("ConstructPrefixSizes");
        subChunkInfo.SetBuffer(kernel, "SubChunkPrefix", SortedSubChunks.Get());
        subChunkInfo.SetInt("numSubChunks", NumSubChunks);
        kernel = subChunkInfo.FindKernel("SetGlobalDetail");
        subChunkInfo.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);

        kernel = geoSizeCounter.FindKernel("CountShaderSizes");
        geoSizeCounter.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoSizeCounter.SetBuffer(kernel, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        geoSizeCounter.SetBuffer(kernel, "shaderIndexOffset", UtilityBuffers.GenerationBuffer);
        geoSizeCounter.SetInt("bSTART_scount", offsets.matSizeCStart);
        geoSizeCounter.SetInt("bSTART_tri", offsets.triIndDictStart);
        geoSizeCounter.SetInt("bCOUNT_base", offsets.baseGeoCounter);
        geoSizeCounter.SetInt("bCOUNT_offset", offsets.baseGeoOffset);
        kernel = geoSizeCounter.FindKernel("CountSubChunkSizes");
        geoSizeCounter.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoSizeCounter.SetBuffer(kernel, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        geoSizeCounter.SetBuffer(kernel, "SubChunkPrefix", SortedSubChunks.Get());
        geoSizeCounter.SetInt("sChunkSize", SubChunkSizeOS);
        geoSizeCounter.SetInt("sChunksPerAxis", SubChunksPerAxis);

        sizePrefixSum.SetBuffer(0, "shaderCountOffset", UtilityBuffers.GenerationBuffer);
        sizePrefixSum.SetInt("bSTART_scount", offsets.matSizeCStart);

        kernel = filterGeometry.FindKernel("FilterShader");
        filterGeometry.SetBuffer(kernel, "filteredIndicies", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "shaderPrefix", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetInt("bSTART_scount", offsets.matSizeCStart);
        filterGeometry.SetInt("bSTART_tri", offsets.triIndDictStart);
        filterGeometry.SetInt("bCOUNT_base", offsets.baseGeoCounter);
        filterGeometry.SetInt("bCOUNT_offset", offsets.baseGeoOffset);
        filterGeometry.SetInt("bSTART_sort", offsets.fBaseGeoStart);

        kernel = filterGeometry.FindKernel("FilterSubChunks");
        filterGeometry.SetBuffer(kernel, "filteredGeometry", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(kernel, "SubChunkPrefix", SortedSubChunks.Get());
        filterGeometry.SetInt("sChunkSize", SubChunkSizeOS);
        filterGeometry.SetInt("sChunksPerAxis", SubChunksPerAxis);

        kernel = geoTranscriber.FindKernel("Transcribe");
        geoTranscriber.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_oGeo", offsets.shadGeoStart);

        kernel = geoTranscriber.FindKernel("BatchTranscribe");
        geoTranscriber.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);

        kernel = geoTranscriber.FindKernel("TranscribeSortedBase");
        geoTranscriber.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "SortedTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bCOUNT_base", offsets.baseGeoCounter);
        geoTranscriber.SetInt("bSTART_sort", offsets.fBaseGeoStart);

        kernel = geoSizeCalculator.FindKernel("GetPrefixSize");
        geoSizeCalculator.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetBuffer(kernel, "prefixSizes", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bCOUNT_write", offsets.baseGeoCounter);
        kernel = geoSizeCalculator.FindKernel("CountSubChunkSizes");
        geoSizeCalculator.SetBuffer(kernel, "prefixSizes", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bSTART_oGeo", offsets.shadGeoStart);
        geoSizeCalculator.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);

        kernel = shaderDrawArgs.FindKernel("FromPrefix");
        shaderDrawArgs.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.DrawArgs.Get());
        kernel = shaderDrawArgs.FindKernel("FromSubChunks");
        shaderDrawArgs.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        shaderDrawArgs.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.DrawArgs.Get());

        for (int i = 0; i < rSettings.Categories.Reg.Count; i++) {
            GeoShader shader = rSettings.Categories.Retrieve(i);
            shader.PresetData(
                offsets.fBaseGeoStart, offsets.matSizeCStart + i,
                offsets.baseGeoCounter, offsets.shadGeoStart, i
            );
            Material mat = shader.GetMaterial();
            LightBaker.SetupLightSampler(mat);
        }
    }

    public static void PresetSubChunkInfo(ComputeShader shader) {
        shader.SetInt("sChunkSize", SubChunkSizeOS);
        shader.SetInt("sChunksPerAxis", SubChunksPerAxis);
        shader.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);
        shader.SetBuffer(0, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
    }
    

    public void Update(MonoBehaviour _) {
        tree?.ForEachActiveChunk(chunk => chunk.Update()); //Update
        tree?.VerifyChunks();
    }

    public static void Unset(){
        SortedSubChunks.Destroy();
        foreach (GeoShader shader in rSettings.Categories.Reg){
            shader.Release();
        }
    }

    public SubChunkShaderGraph(TerrainChunk parent) {
        this.parent = parent;
        this.tree = new FixedOctree(this, rSettings.levels);
        this.tree.Initialize(parent.origin + parent.size / 2);
    }

    public void Release() {
        this.tree?.ForEachChunk(chunk => chunk.Destroy());
        if (executor != null) executor.Active = false;
        if (baseHandle.IsSorted) SortedSubChunks.Release((uint)baseHandle.SortedSubCInd);
    }
    public void ReleaseGeometry() {
        this.tree?.ForEachChunk(chunk => chunk.ReleaseGeometry());
        if (executor != null) executor.Active = false;
        if (baseHandle.IsSorted) SortedSubChunks.Release((uint)baseHandle.SortedSubCInd);
    }

    public void ComputeGeoShaderGeometry(GeometryHandle vertHandle, GeometryHandle triHandle) {
        ReleaseGeometry(); tree?.VerifyChunks();
        this.executor = new IndirectUpdate(Update);
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(executor);

        int triAddress = (int)triHandle.addressIndex;
        int vertAddress = (int)vertHandle.addressIndex;
        this.baseHandle = new BaseGeoHandle(vertHandle, triHandle);

        ShaderSubchunk[] subchunks = tree.GetAllActiveChunks();//
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, offsets.triIndDictStart, 0);
        LoadBaseGeoInfo(GenerationPreset.memoryHandle, triHandle);
        FilterGeometry(GenerationPreset.memoryHandle, triAddress, vertAddress);
        SetSubChunkDetailLevel(subchunks);
        ProcessGeoShaders(GenerationPreset.memoryHandle, vertAddress, triAddress);

        uint2[][] allocs = AllocateForChunkGeometry(GenerationPreset.memoryHandle, subchunks);
        for (int i = 0; i < subchunks.Length; i++) {
            subchunks[i].ApplyAllocToChunk(allocs[i]);
        }
    }

    public bool RecalculateSubChunkGeoShader(ShaderSubchunk chunk) {
        if (!baseHandle.IsValid) return false;
        if (!baseHandle.IsSorted) SortBaseGeometry(GenerationPreset.memoryHandle);

        int2 SCInfo = chunk.GetInfoRegion();
        int triAddress = (int)baseHandle.triangles.addressIndex;
        int vertAddress = (int)baseHandle.vertex.addressIndex;
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, offsets.triIndDictStart, 0);
        LoadBaseSubChunkGeoInfo(SCInfo, baseHandle.SortedSubCInd);
        FilterGeometry(GenerationPreset.memoryHandle, triAddress, vertAddress);
        SetGlobalDetailLevel(chunk.detailLevel);
        ProcessGeoShaders(GenerationPreset.memoryHandle, vertAddress, triAddress);
        uint2[] allocs = AllocateForSubChunkGeometry(GenerationPreset.memoryHandle);
        chunk.ApplyAllocToChunk(allocs);
        return true;
    }

    void FilterGeometry(MemoryBufferHandler memory, int triAddress, int vertAddress) {
        int numShaders = shaders.Count;
        ComputeBuffer triStorage = memory.GetBlockBuffer(triAddress);
        ComputeBuffer vertStorage = memory.GetBlockBuffer(vertAddress);
        GraphicsBuffer memAddresses = memory.Address;

        CountGeometrySizes(vertStorage, triStorage, memAddresses, vertAddress, triAddress);

        ConstructPrefixSum(numShaders);

        FilterShaderGeometry(vertStorage, triStorage, memAddresses, vertAddress, triAddress);
    }

    void ProcessGeoShaders(MemoryBufferHandler memory, int vertAddress, int triAddress) {
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 1, 0); //clear base count
        for (int i = 0; i < shaders.Count; i++) {
            GeoShader geoShader = shaders[i];
            geoShader.ProcessGeoShader(memory, vertAddress, triAddress, offsets.matSizeCStart + i, parent.depth);
            UtilityBuffers.CopyCount(source: UtilityBuffers.GenerationBuffer, dest: UtilityBuffers.GenerationBuffer,
                readOffset: offsets.baseGeoCounter, writeOffset: offsets.shadGeoCStart + i + 1);
        }
    }


    uint2[][] AllocateForChunkGeometry(MemoryBufferHandler memory, ShaderSubchunk[] subChunks) {
        uint2[][] allocs = new uint2[subChunks.Length][];
        int3[] scAddrInfo = new int3[subChunks.Length];
        for (int i = 0; i < subChunks.Length; i++) {
            allocs[i] = new uint2[shaders.Count];
            scAddrInfo[i].xy = subChunks[i].GetInfoRegion();
        }

        for (int i = 0; i < shaders.Count; i++) {
            CountSubChunkGeoSizes(offsets.shadGeoCStart + i, subChunks);
            for (int j = 0; j < subChunks.Length; j++) {
                allocs[j][i].x = memory.AllocateMemory(
                    UtilityBuffers.TransferBuffer,
                    GEO_TRI_STRIDE,
                    j * 3 + 2
                );
                scAddrInfo[j].z = (int)allocs[j][i].x;
                allocs[j][i].y = UtilityBuffers.DrawArgs.Allocate();
                SetSubChunkDrawArgs((int)allocs[j][i].y, j);
            }
            BatchTranscribe(memory.Storage, memory.Address,
                scAddrInfo, offsets.shadGeoCStart + i);
        }
        return allocs;
    }
    
    public uint2[] AllocateForSubChunkGeometry(MemoryBufferHandler memory)
    {
        int numShaders = shaders.Count;

        uint2[] allocs = new uint2[numShaders];
        GraphicsBuffer addressesReference = memory.Address;

        for (int i = 0; i < numShaders; i++){
            CopyGeoCount(offsets.shadGeoCStart + i);
            allocs[i].x = memory.AllocateMemory(UtilityBuffers.GenerationBuffer, GEO_TRI_STRIDE, offsets.baseGeoCounter);
            ComputeBuffer memoryReference = memory.GetBlockBuffer(allocs[i].x);
            TranscribeGeometry(memoryReference, addressesReference, (int)allocs[i].x, offsets.shadGeoCStart + i);
            allocs[i].y = UtilityBuffers.DrawArgs.Allocate();
            GetDrawArgs((int)allocs[i].y, offsets.shadGeoCStart + i);
        }
        return allocs;
    }


    private void LoadBaseGeoInfo(MemoryBufferHandler memory, GeometryHandle triHandle) {
        ComputeBuffer triStorage = memory.GetBlockBuffer(triHandle.addressIndex);
        GraphicsBuffer addresses = memory.Address;
        
        int kernel = geoInfoLoader.FindKernel("GetBaseSize");
        geoInfoLoader.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, triStorage);
        geoInfoLoader.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        geoInfoLoader.SetInt(ShaderIDProps.TriAddress, (int)triHandle.addressIndex);
        geoInfoLoader.Dispatch(kernel, 1, 1, 1);
    }

    private void LoadBaseSubChunkGeoInfo(int2 SCInfo, int prefixStart) {
        int stride = NumSubChunks + 1;
        int kernel = geoInfoLoader.FindKernel("GetSubChunkSize");
        geoInfoLoader.SetInt(ShaderIDProps.StartSChunkP, prefixStart * stride);
        geoInfoLoader.SetInt(ShaderIDProps.SCStart, SCInfo.x);
        geoInfoLoader.SetInt(ShaderIDProps.SCEnd, SCInfo.y);
        geoInfoLoader.Dispatch(kernel, 1, 1, 1);
    }

    private static void CountGeometrySizes(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress) {
        int kernel = geoSizeCounter.FindKernel("CountShaderSizes");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoSizeCounter, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.Vertices, vertMemory);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.Triangles, triMemory);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        geoSizeCounter.SetInt(ShaderIDProps.VertAddress, vertAddress);
        geoSizeCounter.SetInt(ShaderIDProps.TriAddress, triAddress);

        geoSizeCounter.DispatchIndirect(kernel, args);
    }

    private static void ConstructPrefixSum(int numShaders) {
        sizePrefixSum.SetInt("numShaders", numShaders);
        sizePrefixSum.Dispatch(0, 1, 1, 1);
    }

    private static void FilterShaderGeometry(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(filterGeometry, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        int kernel = filterGeometry.FindKernel("FilterShader");
        filterGeometry.SetBuffer(kernel, ShaderIDProps.Vertices, vertMemory);
        filterGeometry.SetBuffer(kernel, ShaderIDProps.Triangles, triMemory);
        filterGeometry.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        filterGeometry.SetInt(ShaderIDProps.VertAddress, vertAddress);
        filterGeometry.SetInt(ShaderIDProps.TriAddress, triAddress);

        filterGeometry.DispatchIndirect(kernel, args);
    }


    void CountSubChunkGeoSizes(int shadGeoCount, ShaderSubchunk[] subChunks) {
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, NumSubChunks, offsets.subChunkInfoStart);
        CopyGeoCount(shadGeoCount);

        int kernel = geoSizeCalculator.FindKernel("CountSubChunkSizes");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoSizeCalculator, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoSizeCalculator.DispatchIndirect(kernel, args);

        kernel = subChunkInfo.FindKernel("CollectSubChunkSizes");
        subChunkInfo.SetInt(ShaderIDProps.NumSubChunkRegions, subChunks.Length);
        subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)subChunks.Length / threadGroupSize);
        subChunkInfo.Dispatch(kernel, numThreadsAxis, 1, 1);
    }

    void CopyGeoCount(int shadGeoCount) {
        int kernel = geoSizeCalculator.FindKernel("GetPrefixSize");
        geoSizeCalculator.SetInt(ShaderIDProps.CountOGeo, shadGeoCount);
        geoSizeCalculator.Dispatch(kernel, 1, 1, 1);
    }
    
    void TranscribeGeometry(ComputeBuffer memory, GraphicsBuffer addresses, int addressIndex, int geoSizeCounter) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        int kernel = geoTranscriber.FindKernel("Transcribe");
        geoTranscriber.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, memory);
        geoTranscriber.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        geoTranscriber.SetInt(ShaderIDProps.AddressIndex, addressIndex);
        geoTranscriber.SetInt(ShaderIDProps.CountOGeo, geoSizeCounter);
        geoTranscriber.DispatchIndirect(kernel, args);
    }

    void BatchTranscribe(ComputeBuffer memory, GraphicsBuffer addresses, int3[] SCAddressRegions, int shadGeoCount) {
        UtilityBuffers.TransferBuffer.SetData(SCAddressRegions);
        int kernel = subChunkInfo.FindKernel("SetSubChunkAddress");
        //This is safe ONLY if immediately using it after allocation
        subChunkInfo.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        subChunkInfo.SetInt(ShaderIDProps.NumSubChunkRegions, SCAddressRegions.Length);
        subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)SCAddressRegions.Length / threadGroupSize);
        subChunkInfo.Dispatch(kernel, numThreadsAxis, 1, 1);

        kernel = geoTranscriber.FindKernel("BatchTranscribe");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoTranscriber.SetBuffer(kernel, ShaderIDProps.MemoryBuffer, memory);
        geoTranscriber.SetInt(ShaderIDProps.CountOGeo, shadGeoCount);
        geoTranscriber.DispatchIndirect(kernel, args);
    }

    void SetSubChunkDetailLevel(ShaderSubchunk[] subchunks) {
        int3[] detailLevels = subchunks.Select(subchunk => new int3(
            subchunk.GetInfoRegion(), subchunk.detailLevel)).ToArray();
        UtilityBuffers.TransferBuffer.SetData(detailLevels);
        int kernel = subChunkInfo.FindKernel("SetSubChunkDetail");
        subChunkInfo.SetInt(ShaderIDProps.NumSubChunkRegions, subchunks.Length);
        subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)subchunks.Length / threadGroupSize);
        subChunkInfo.Dispatch(kernel, numThreadsAxis, 1, 1);
    }

    void SetGlobalDetailLevel(int detailLevel) {
        int kernel = subChunkInfo.FindKernel("SetGlobalDetail");
        subChunkInfo.SetInt(ShaderIDProps.DetailLevel, detailLevel);
        subChunkInfo.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)NumSubChunks / threadGroupSize);
        subChunkInfo.Dispatch(kernel, numThreadsAxis, 1, 1);
    }

    void SetSubChunkDrawArgs(int address, int subChunkInd) {
        int kernel = shaderDrawArgs.FindKernel("FromSubChunks");
        shaderDrawArgs.SetInt(ShaderIDProps.ArgOffset, address);
        shaderDrawArgs.SetInt(ShaderIDProps.SubChunkInd, subChunkInd);
        shaderDrawArgs.Dispatch(kernel, 1, 1, 1);
    }

    void GetDrawArgs(int address, int geoSizeCounter) {
        int kernel = shaderDrawArgs.FindKernel("FromPrefix");
        shaderDrawArgs.SetBuffer(kernel, "prefixSizes", UtilityBuffers.GenerationBuffer);
        shaderDrawArgs.SetInt(ShaderIDProps.CountOGeo, geoSizeCounter);
        shaderDrawArgs.SetInt(ShaderIDProps.ArgOffset, address);
        shaderDrawArgs.Dispatch(kernel, 1, 1, 1);
    }

    void SortBaseGeometry(MemoryBufferHandler memory) {
        int stride = NumSubChunks + 1;
        baseHandle.SortedSubCInd = (int)SortedSubChunks.Allocate();
        int start = baseHandle.SortedSubCInd * stride;
        ComputeBuffer triStorage = memory.GetBlockBuffer(baseHandle.triangles.addressIndex);
        ComputeBuffer vertStorage = memory.GetBlockBuffer(baseHandle.vertex.addressIndex);
        GraphicsBuffer memAddresses = memory.Address;
        int triAddress = (int)baseHandle.triangles.addressIndex;
        int vertAddress = (int)baseHandle.vertex.addressIndex;

        LoadBaseGeoInfo(memory, baseHandle.triangles);
        UtilityBuffers.ClearRange(SortedSubChunks.Get(), stride, start);

        int kernel = geoSizeCounter.FindKernel("CountSubChunkSizes");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoSizeCounter, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.Vertices, triStorage);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.Triangles, vertStorage);
        geoSizeCounter.SetBuffer(kernel, ShaderIDProps.AddressDict, memAddresses);
        geoSizeCounter.SetInt(ShaderIDProps.StartSChunkP, start);
        geoSizeCounter.SetInt(ShaderIDProps.VertAddress, vertAddress);
        geoSizeCounter.SetInt(ShaderIDProps.TriAddress, triAddress);
        geoSizeCounter.DispatchIndirect(kernel, args);

        kernel = subChunkInfo.FindKernel("ConstructPrefixSizes");
        subChunkInfo.SetInt(ShaderIDProps.StartSChunkP, start);
        subChunkInfo.Dispatch(kernel, 1, 1, 1);

        kernel = filterGeometry.FindKernel("FilterSubChunks");
        filterGeometry.SetBuffer(kernel, ShaderIDProps.Vertices, vertStorage);
        filterGeometry.SetBuffer(kernel, ShaderIDProps.Triangles, triStorage);
        filterGeometry.SetBuffer(kernel, ShaderIDProps.AddressDict, memAddresses);
        filterGeometry.SetInt(ShaderIDProps.VertAddress, vertAddress);
        filterGeometry.SetInt(ShaderIDProps.TriAddress, triAddress);
        filterGeometry.SetInt(ShaderIDProps.StartSChunkP, start);
        args = UtilityBuffers.CountToArgs(filterGeometry, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        filterGeometry.DispatchIndirect(kernel, args);

        kernel = geoTranscriber.FindKernel("TranscribeSortedBase");
        geoTranscriber.SetBuffer(kernel, ShaderIDProps.MemoryBufferBase, triStorage);
        geoTranscriber.SetBuffer(kernel, ShaderIDProps.AddressDict, memAddresses);
        geoTranscriber.SetInt(ShaderIDProps.AddressIndex, triAddress);
        args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoTranscriber.DispatchIndirect(kernel, args);
    }

    public class FixedOctree : Octree<ShaderSubchunk> {
        /// <summary> The last tracked position of the viewer in 
        /// chunk space. This value is only updated when the viewer's
        /// position exceeds the viewDistUpdate threshold. </summary>
        public int3 ViewerPosGS;
        private List<GeoShaderSettings.DetailLevel> Details;
        private WeakReference<SubChunkShaderGraph> Graph;
        public static int GetMaxDepth(List<GeoShaderSettings.DetailLevel> detailLevels) {
            int divisions = 0;
            foreach (var level in detailLevels)
                if (level.IncreaseSize) divisions++;
            return divisions;
        }

        private static int GetMaxNumChunks(int depth) {
            int length = 1 << depth;
            return length * length * length;
        }
        private static int GetMinChunkSize(int depth, int rootChunkSize) {
            return rootChunkSize / (1 << depth);
        }
        public FixedOctree(SubChunkShaderGraph graph, List<GeoShaderSettings.DetailLevel> detailLevels) :
            base(GetMaxDepth(detailLevels),
                GetMinChunkSize(GetMaxDepth(detailLevels), graph.parent.size),
                GetMaxNumChunks(GetMaxDepth(detailLevels))) {
            this.Graph = new WeakReference<SubChunkShaderGraph>(graph);
            this.Details = detailLevels;
        }
        public void Initialize(int3 center) {
            ViewerPosGS = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
            base.Initialize(1, center);
        }

        public void VerifyChunks() {
            int3 ViewerPosition = (int3)math.round(CPUMapManager.WSToGS(OctreeTerrain.viewer.position));
            if (math.distance(ViewerPosGS, ViewerPosition) < rSettings.SubchunkUpdateThresh) return;
            ViewerPosGS = ViewerPosition;

            Queue<ShaderSubchunk> frameChunks = new Queue<ShaderSubchunk>();
            ForEachChunk(chunk => frameChunks.Enqueue(chunk));
            while (frameChunks.Count > 0) {
                ShaderSubchunk chunk = frameChunks.Dequeue();
                if (!chunk.active) continue;
                chunk.VerifyChunk();
            }
        }

        public override bool IsBalanced(ref Node node) {
            int accDist = 0; int chunkSize = MinChunkSize;
            int viewerDist = node.GetMaxDist(ViewerPosGS);
            for (int i = 0; i < Details.Count; i++) {
                accDist += Details[i].Distance;
                if (viewerDist < accDist)
                    return node.size <= chunkSize;
                if (Details[i].IncreaseSize)
                    chunkSize *= 2;
            }
            return true;
        }

        protected override bool RemapRoot(uint node) => true;
        protected override void AddTerrainChunk(uint octreeIndex) {
            if (!Graph.TryGetTarget(out SubChunkShaderGraph g)) return;
            ref Node node = ref nodes[octreeIndex];
            ShaderSubchunk nChunk = new ShaderSubchunk(g, shaders, node.origin, (int)node.size, octreeIndex);
            node.Chunk = chunks.Enqueue(nChunk);
            node.IsComplete = false;
        }
    }

    /* Gen Buffer Organization
    [ baseGeoCounter(4b) | matSizeCounters(20b) |  shadGeoCounters(20b) | subchunkLUT(256b) | triIndDict(5.25 mb) | filteredBaseGeo(5.25mb) | GeoShaderGeometry(rest of buffer)]
    */
    public struct GeoShaderOffsets : BufferOffsets {
        public int baseGeoCounter; 
        public int baseGeoOffset; 
        public int matSizeCStart;
        public int shadGeoCStart;
        public int triIndDictStart;
        public int subChunkInfoStart;
        public int fBaseGeoStart;
        public int shadGeoStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the GeoShader generator. 
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}}
        /// <summary> The end of the buffer region that is used by the GeoShader generator. 
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd { get { return offsetEnd; } }
        public GeoShaderOffsets(int maxChunkSize, int maxShaderCount, int maxSubChunkDepth, int bufferStart) {
            int numPointsPerAxis = maxChunkSize + 1;
            int numOfTris = (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * 5;
            int subChunksPerAxis = (1 << maxSubChunkDepth);
            int numSubChunks = subChunksPerAxis * subChunksPerAxis * subChunksPerAxis;

            this.offsetStart = bufferStart;
            baseGeoCounter = bufferStart;
            baseGeoOffset = 1 + baseGeoCounter;
            matSizeCStart = 1 + baseGeoOffset;
            shadGeoCStart = (maxShaderCount + 1) + matSizeCStart;
            subChunkInfoStart = (maxShaderCount + 1) + shadGeoCStart;;
            triIndDictStart = numSubChunks + subChunkInfoStart;
            fBaseGeoStart = numOfTris + triIndDictStart;
            shadGeoStart = Mathf.CeilToInt(((float)numOfTris + fBaseGeoStart) / GEN_TRI_STRIDE);
            this.offsetEnd = (shadGeoStart + numOfTris) * GEN_TRI_STRIDE;
        }
    }

    private struct BaseGeoHandle{
        public GeometryHandle vertex;
        public GeometryHandle triangles;
        public int SortedSubCInd;
        public bool IsSorted => SortedSubCInd != -1;
        public bool IsValid => vertex.Active && triangles.Active;

        public BaseGeoHandle(GeometryHandle v, GeometryHandle t) {
            this.vertex = v;
            this.triangles = t;
            SortedSubCInd = -1;
        }
    }
}
