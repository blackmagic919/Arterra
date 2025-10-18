using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using MapStorage;
using TerrainGeneration;
using TerrainGeneration.Readback;
using Unity.Mathematics;
using UnityEngine;
using WorldConfig;
using WorldConfig.Quality;

public class SubChunkShaderGraph{
    public FixedOctree tree;
    private static List<GeoShader> shaders => rSettings.Categories.Reg;
    private static GeoShaderSettings rSettings => Config.CURRENT.Quality.GeoShaders.value;
    public static ComputeShader matSizeCounter;
    public static ComputeShader filterGeometry;
    public static ComputeShader sizePrefixSum;
    public static ComputeShader geoSizeCalculator;
    public static ComputeShader geoTranscriber;
    public static ComputeShader shaderDrawArgs;
    public static ComputeShader geoInfoLoader;
    private static GeoShaderOffsets offsets;
    private IndirectUpdate executor;
    private static int SubChunkSizeOS;
    private static int SubChunksPerAxis;
    private static int NumSubChunks => SubChunksPerAxis * SubChunksPerAxis * SubChunksPerAxis;
    const int GEO_TRI_STRIDE = 3*3;
    const int GEN_TRI_STRIDE = 3*3+1;
    const int TRI_STRIDE_WORD = 3;
    public static void PresetData() {
        matSizeCounter = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderMatSizeCounter");
        filterGeometry = Resources.Load<ComputeShader>("Compute/GeoShader/FilterShaderGeometry");
        sizePrefixSum = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderPrefixConstructor");
        geoSizeCalculator = Resources.Load<ComputeShader>("Compute/GeoShader/GeometryMemorySize");
        geoTranscriber = Resources.Load<ComputeShader>("Compute/GeoShader/TranscribeGeometry");
        shaderDrawArgs = Resources.Load<ComputeShader>("Compute/GeoShader/GeoDrawArgs");
        geoInfoLoader = Resources.Load<ComputeShader>("Compute/GeoShader/GeoInfoLoader");

        int maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int maxSubChunkDepth = FixedOctree.GetMaxDepth(rSettings.levels.value);
        offsets = new GeoShaderOffsets(maxChunkSize, rSettings.Categories.Count(),
            maxSubChunkDepth, 0);

        SubChunksPerAxis = 1 << maxSubChunkDepth;
        SubChunkSizeOS = maxChunkSize / SubChunksPerAxis;

        int kernel = geoInfoLoader.FindKernel("GetBaseSize");
        geoInfoLoader.SetBuffer(kernel, "counter", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetInt("bCOUNTER_tri", offsets.baseGeoCounter);

        kernel = geoInfoLoader.FindKernel("SetSubChunkDetail");
        geoInfoLoader.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        kernel = geoInfoLoader.FindKernel("CollectSubChunkSizes");
        geoInfoLoader.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        kernel = geoInfoLoader.FindKernel("SetSubChunkAddress");
        geoInfoLoader.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        geoInfoLoader.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);
        geoInfoLoader.SetInt("numPointsPerAxis", SubChunksPerAxis);

        matSizeCounter.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetBuffer(0, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetBuffer(0, "shaderIndexOffset", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetInt("bSTART_scount", offsets.matSizeCStart);
        matSizeCounter.SetInt("bSTART_tri", offsets.triIndDictStart);

        sizePrefixSum.SetBuffer(0, "shaderCountOffset", UtilityBuffers.GenerationBuffer);
        sizePrefixSum.SetInt("bSTART_scount", offsets.matSizeCStart);

        filterGeometry.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(0, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(0, "shaderPrefix", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetInt("bSTART_scount", offsets.matSizeCStart);
        filterGeometry.SetInt("bSTART_tri", offsets.triIndDictStart);
        filterGeometry.SetInt("bCOUNTER_base", offsets.baseGeoCounter);

        filterGeometry.SetBuffer(0, "filteredGeometry", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetInt("bSTART_sort", offsets.fBaseGeoStart);

        kernel = geoTranscriber.FindKernel("Transcribe");
        geoTranscriber.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_oGeo", offsets.shadGeoStart);

        kernel = geoTranscriber.FindKernel("BatchTranscribe");
        geoTranscriber.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(kernel, "SubChunkInfo", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_sChunkI", offsets.subChunkInfoStart);

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

        kernel = shaderDrawArgs.FindKernel("FromSubChunks");
        shaderDrawArgs.SetBuffer(kernel, "SubChunkRegions", UtilityBuffers.TransferBuffer);
        shaderDrawArgs.SetBuffer(kernel, "_IndirectArgsBuffer", UtilityBuffers.ArgumentBuffer);

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
        tree?.ForEachChunk(chunk => chunk.Update()); //Update
        tree?.VerifyChunks();
    }

    public static void Unset(){
        foreach (GeoShader shader in rSettings.Categories.Reg){
            shader.Release();
        }
    }

    public SubChunkShaderGraph(TerrainChunk parent) {
        this.tree = new FixedOctree(parent, rSettings.levels);
        this.tree.Initialize();
    }

    public void Release() {
        this.tree?.ForEachChunk(chunk => chunk.Destroy());
        if (executor != null) executor.Active = false;
    }
    public void ReleaseGeometry() {
        this.tree?.ForEachChunk(chunk => chunk.ReleaseGeometry());
        if (executor != null) executor.Active = false;
    }

    public void ComputeGeoShaderGeometry(GeometryHandle vertHandle, GeometryHandle triHandle) {
        tree?.VerifyChunks();
        this.executor = new IndirectUpdate(Update);
        OctreeTerrain.MainLoopUpdateTasks.Enqueue(executor);

        int triAddress = (int)triHandle.addressIndex;
        int vertAddress = (int)vertHandle.addressIndex;
        ShaderSubchunk[] subchunks = tree.GetAllChunks();
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, offsets.triIndDictStart, 0);
        FilterGeometry(GenerationPreset.memoryHandle, triAddress, vertAddress);
        ProcessGeoShaders(GenerationPreset.memoryHandle, subchunks, vertAddress, triAddress);
        uint2[][] allocs = ConvertGeometryToSubchunkRender(GenerationPreset.memoryHandle, subchunks);
        for (int i = 0; i < subchunks.Length; i++) {
            subchunks[i].ApplyAllocToChunk(shaders, allocs[i]);
        }
    }

    void FilterGeometry(MemoryBufferHandler memory, int triAddress, int vertAddress) {

        int numShaders = shaders.Count;
        ComputeBuffer triStorage = memory.GetBlockBuffer(triAddress);
        ComputeBuffer vertStorage = memory.GetBlockBuffer(vertAddress);
        ComputeBuffer memAddresses = memory.Address;

        LoadBaseGeoInfo(triStorage, memAddresses, triAddress);

        CountGeometrySizes(vertStorage, triStorage, memAddresses, vertAddress, triAddress);

        ConstructPrefixSum(numShaders);

        FilterShaderGeometry(vertStorage, triStorage, memAddresses, vertAddress, triAddress);
    }

    void ProcessGeoShaders(MemoryBufferHandler memory, ShaderSubchunk[] subchunks, int vertAddress, int triAddress) {
        SetSubChunkDetailLevel(subchunks);
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 1, 0); //clear base count
        for (int i = 0; i < shaders.Count; i++) {
            GeoShader geoShader = shaders[i];
            geoShader.ProcessGeoShader(memory, vertAddress, triAddress, offsets.matSizeCStart + i);
            UtilityBuffers.CopyCount(source: UtilityBuffers.GenerationBuffer, dest: UtilityBuffers.GenerationBuffer,
                readOffset: offsets.baseGeoCounter, writeOffset: offsets.shadGeoCStart + i + 1);
        }
    }

    
    uint2[][] ConvertGeometryToSubchunkRender(MemoryBufferHandler memory, ShaderSubchunk[] subChunks) {
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
                allocs[j][i].y = UtilityBuffers.AllocateArgs();
                SetSubChunkDrawArgs((int)allocs[j][i].y, j);
            }
            BatchTranscribe(memory.Storage, memory.Address,
                scAddrInfo, offsets.shadGeoCStart + i);
        }
        return allocs;
    }


    public void LoadBaseGeoInfo(ComputeBuffer memory, ComputeBuffer addresses, int triAddress) {
        int kernel = geoInfoLoader.FindKernel("GetBaseSize");
        geoInfoLoader.SetBuffer(kernel, "_MemoryBuffer", memory);
        geoInfoLoader.SetBuffer(kernel, "_AddressDict", addresses);
        geoInfoLoader.SetInt("triAddress", triAddress);
        geoInfoLoader.SetInt("triStride", TRI_STRIDE_WORD);
        geoInfoLoader.Dispatch(kernel, 1, 1, 1);

    }

    void CountGeometrySizes(ComputeBuffer vertMemory, ComputeBuffer triMemory, ComputeBuffer addresses, int vertAddress, int triAddress) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(matSizeCounter, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        matSizeCounter.SetBuffer(0, "vertices", vertMemory);
        matSizeCounter.SetBuffer(0, "triangles", triMemory);
        matSizeCounter.SetBuffer(0, "_AddressDict", addresses);
        matSizeCounter.SetInt("vertAddress", vertAddress);
        matSizeCounter.SetInt("triAddress", triAddress);

        matSizeCounter.DispatchIndirect(0, args);
    }

    void ConstructPrefixSum(int numShaders) {
        sizePrefixSum.SetInt("numShaders", numShaders);
        sizePrefixSum.Dispatch(0, 1, 1, 1);
    }

    void FilterShaderGeometry(ComputeBuffer vertMemory, ComputeBuffer triMemory, ComputeBuffer addresses, int vertAddress, int triAddress) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(filterGeometry, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        filterGeometry.SetBuffer(0, "vertices", vertMemory);
        filterGeometry.SetBuffer(0, "triangles", triMemory);
        filterGeometry.SetBuffer(0, "_AddressDict", addresses);
        filterGeometry.SetInt("vertAddress", vertAddress);
        filterGeometry.SetInt("triAddress", triAddress);

        filterGeometry.DispatchIndirect(0, args);
    }


    void CountSubChunkGeoSizes(int shadGeoCount, ShaderSubchunk[] subChunks) {
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, NumSubChunks, offsets.subChunkInfoStart);
        int kernel = geoSizeCalculator.FindKernel("GetPrefixSize");
        geoSizeCalculator.SetInt("bCOUNT_oGeo", shadGeoCount);
        geoSizeCalculator.Dispatch(kernel, 1, 1, 1);

        kernel = geoSizeCalculator.FindKernel("CountSubChunkSizes");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoSizeCalculator, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoSizeCalculator.DispatchIndirect(kernel, args);
        
        kernel = geoInfoLoader.FindKernel("CollectSubChunkSizes");
        geoInfoLoader.SetInt("numSubChunkRegions", subChunks.Length);
        geoInfoLoader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)subChunks.Length / threadGroupSize);
        geoInfoLoader.Dispatch(kernel, numThreadsAxis, 1, 1);
    }

    void BatchTranscribe(ComputeBuffer memory, ComputeBuffer addresses, int3[] SCAddressRegions, int shadGeoCount) {
        UtilityBuffers.TransferBuffer.SetData(SCAddressRegions);
        int kernel = geoInfoLoader.FindKernel("SetSubChunkAddress");
        //This is safe ONLY if immediately using it after allocation
        geoInfoLoader.SetBuffer(kernel, "_AddressDict", addresses);
        geoInfoLoader.SetInt("numSubChunkRegions", SCAddressRegions.Length);
        geoInfoLoader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)SCAddressRegions.Length / threadGroupSize);
        geoInfoLoader.Dispatch(kernel, numThreadsAxis, 1, 1);

        kernel = geoTranscriber.FindKernel("BatchTranscribe");
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter, kernel);
        geoTranscriber.SetBuffer(kernel, "_MemoryBuffer", memory);
        geoTranscriber.SetInt("bCOUNTER_oGeo", shadGeoCount);
        geoTranscriber.DispatchIndirect(kernel, args);
    }

    void SetSubChunkDetailLevel(ShaderSubchunk[] subchunks) {
        int3[] detailLevels = subchunks.Select(subchunk => new int3(
            subchunk.GetInfoRegion(), subchunk.detailLevel)).ToArray();
        UtilityBuffers.TransferBuffer.SetData(detailLevels);
        int kernel = geoInfoLoader.FindKernel("SetSubChunkDetail");
        geoInfoLoader.SetInt("numSubChunkRegions", subchunks.Length);
        geoInfoLoader.GetKernelThreadGroupSizes(kernel, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = (int)math.ceil((double)subchunks.Length / threadGroupSize);
        geoInfoLoader.Dispatch(kernel, numThreadsAxis, 1, 1);
    }

    void SetSubChunkDrawArgs(int address, int subChunkInd) {
        int kernel = shaderDrawArgs.FindKernel("FromSubChunks");
        shaderDrawArgs.SetInt("argOffset", address);
        shaderDrawArgs.SetInt("SubChunkInd", subChunkInd);
        shaderDrawArgs.Dispatch(kernel, 1, 1, 1);
    }
    

    /* Gen Buffer Organization
    [ baseGeoCounter(4b) | matSizeCounters(20b) |  shadGeoCounters(20b) | subchunkLUT(256b) | triIndDict(5.25 mb) | filteredBaseGeo(5.25mb) | GeoShaderGeometry(rest of buffer)]
    */
    public struct GeoShaderOffsets : BufferOffsets {
        public int baseGeoCounter; 
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
            matSizeCStart = 1 + baseGeoCounter;
            shadGeoCStart = (maxShaderCount + 1) + matSizeCStart;
            subChunkInfoStart = (maxShaderCount + 1) + shadGeoCStart;;
            triIndDictStart = numSubChunks + subChunkInfoStart;
            fBaseGeoStart = numOfTris + triIndDictStart;
            shadGeoStart = Mathf.CeilToInt(((float)numOfTris + fBaseGeoStart) / GEN_TRI_STRIDE);
            this.offsetEnd = (shadGeoStart + numOfTris) * GEN_TRI_STRIDE;
        }
    }

    public class FixedOctree : Octree<ShaderSubchunk> {
        /// <summary> The last tracked position of the viewer in 
        /// chunk space. This value is only updated when the viewer's
        /// position exceeds the viewDistUpdate threshold. </summary>
        public int3 ViewerPosGS;
        public TerrainChunk parent;
        private List<GeoShaderSettings.DetailLevel> Details;
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
        public FixedOctree(TerrainChunk parent, List<GeoShaderSettings.DetailLevel> detailLevels) :
            base(GetMaxDepth(detailLevels),
                GetMinChunkSize(GetMaxDepth(detailLevels), parent.size),
                GetMaxNumChunks(GetMaxDepth(detailLevels))) {
            this.Details = detailLevels;
            this.parent = parent;
        }
        public void Initialize() {
            int3 center = parent.origin + parent.size / 2;
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
            ref Node node = ref nodes[octreeIndex];
            ShaderSubchunk nChunk = new ShaderSubchunk(this, node.origin, (int)node.size, octreeIndex);
            node.Chunk = chunks.Enqueue(nChunk);
            node.IsComplete = false;
        }

    }
}
