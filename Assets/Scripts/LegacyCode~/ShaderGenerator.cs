using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using TerrainGeneration;
using TerrainGeneration.Readback;
using WorldConfig;
using WorldConfig.Quality;
using System.Collections.Generic;
using Unity.Mathematics;

public class ShaderGenerator
{
    private List<GeoShader> shaders;
    private static GeoShaderSettings rSettings => Config.CURRENT.Quality.GeoShaders.value;
    public static ComputeShader matSizeCounter;
    public static ComputeShader filterGeometry;
    public static ComputeShader sizePrefixSum;
    public static ComputeShader indirectThreads;
    public static ComputeShader geoSizeCalculator;
    public static ComputeShader geoTranscriber;
    public static ComputeShader shaderDrawArgs;
    public static ComputeShader geoInfoLoader;


    private Transform transform;
    const int GEO_VERTEX_STRIDE = 3;
    const int GEN_TRI_STRIDE = 3*3+1;
    const int TRI_STRIDE_WORD = 3;

    private Bounds shaderBounds;
    private ShaderUpdateTask[] shaderUpdateTasks;


    static ShaderGenerator(){
        matSizeCounter = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderMatSizeCounter");
        filterGeometry = Resources.Load<ComputeShader>("Compute/GeoShader/FilterShaderGeometry");
        sizePrefixSum = Resources.Load<ComputeShader>("Compute/GeoShader/ShaderPrefixConstructor");
        geoSizeCalculator = Resources.Load<ComputeShader>("Compute/GeoShader/GeometryMemorySize");
        geoTranscriber = Resources.Load<ComputeShader>("Compute/GeoShader/TranscribeGeometry");
        shaderDrawArgs = Resources.Load<ComputeShader>("Compute/GeoShader/GeoDrawArgs");
        geoInfoLoader = Resources.Load<ComputeShader>("Compute/GeoShader/GeoInfoLoader");

        indirectThreads = Resources.Load<ComputeShader>("Compute/Utility/DivideByThreads");
    }

    private static GeoShaderOffsets offsets;
    public static void PresetData(){
        int maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        /* Gen Buffer Organization
        [ baseGeoCounter(4b) | matSizeCounters(20b) |  shadGeoCounters(20b) | triIndDict(5.25 mb) | filteredBaseGeo(5.25mb) | GeoShaderGeometry(rest of buffer)]
        */
        offsets = new GeoShaderOffsets(maxChunkSize, rSettings.Categories.Count(), 0);

        geoInfoLoader.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetInt("bCOUNT_tri", offsets.baseGeoCounter);

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

        geoTranscriber.SetBuffer(0, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(0, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_oGeo", offsets.shadGeoStart);

        for (int i = 0; i < rSettings.Categories.Reg.Count; i++){
            GeoShader shader = rSettings.Categories.Retrieve(i);
            shader.PresetData(
                offsets.fBaseGeoStart, offsets.matSizeCStart + i,
                offsets.baseGeoCounter, offsets.shadGeoStart, i
            );
            Material mat = shader.GetMaterial();
            LightBaker.SetupLightSampler(mat);
        }
    }

    public static void Release(){
        foreach (GeoShader shader in rSettings.Categories.Reg){
            shader.Release();
        }
    }

    public ShaderGenerator(Transform transform, Bounds boundsOS)
    {
        this.shaders = rSettings.Categories.Reg;
        this.transform = transform;
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
        this.shaderUpdateTasks = new ShaderUpdateTask[this.shaders.Count];
    }

    public void ReleaseGeometry()
    {
        foreach (ShaderUpdateTask task in shaderUpdateTasks){
            if (task == null || !task.Active)
                continue;
            task.Release(ref GenerationPreset.memoryHandle);
        }
    }


    public void ComputeGeoShaderGeometry(GeometryHandle vertHandle, GeometryHandle triHandle)
    {
        ReleaseGeometry();
        int triAddress = (int)triHandle.addressIndex;
        int vertAddress = (int)vertHandle.addressIndex;

        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, offsets.triIndDictStart, 0);
        FilterGeometry(GenerationPreset.memoryHandle, triAddress, vertAddress);
        ProcessGeoShaders(GenerationPreset.memoryHandle, vertAddress, triAddress);

        uint[] geoShaderMemAdds = TranscribeGeometries();
        uint[] geoShaderDispArgs = GetShaderDrawArgs();
        RenderParams[] geoShaderParams = SetupShaderMaterials(GenerationPreset.memoryHandle, geoShaderMemAdds);

        for (int i = 0; i < shaders.Count; i++){
            shaderUpdateTasks[i] = new ShaderUpdateTask(geoShaderMemAdds[i], geoShaderDispArgs[i], geoShaderParams[i]);
            OctreeTerrain.MainLateUpdateTasks.Enqueue(shaderUpdateTasks[i]);
            ReleaseEmptyShaders(shaderUpdateTasks[i]);
        }
    }


    public void FilterGeometry(MemoryBufferHandler memory, int triAddress, int vertAddress)
    {

        int numShaders = shaders.Count;
        ComputeBuffer triStorage = memory.GetBlockBuffer(triAddress);
        ComputeBuffer vertStorage = memory.GetBlockBuffer(vertAddress);
        GraphicsBuffer memAddresses = memory.Address;

        LoadBaseGeoInfo(triStorage, memAddresses, triAddress);

        CountGeometrySizes(vertStorage, triStorage, memAddresses, vertAddress, triAddress);

        ConstructPrefixSum(numShaders);

        FilterShaderGeometry(vertStorage, triStorage, memAddresses, vertAddress, triAddress);
    }

    public void ProcessGeoShaders(MemoryBufferHandler memory, int vertAddress, int triAddress)
    {
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 1, 0); //clear base count
        for (int i = 0; i < shaders.Count; i++){
            GeoShader geoShader = shaders[i];
            geoShader.ProcessGeoShader(memory, vertAddress, triAddress, offsets.matSizeCStart + i, -1);
            UtilityBuffers.CopyCount(source: UtilityBuffers.GenerationBuffer, dest: UtilityBuffers.GenerationBuffer,
                readOffset: offsets.baseGeoCounter, writeOffset: offsets.shadGeoCStart + i + 1);
        }
    }

    public void LoadBaseGeoInfo(ComputeBuffer memory, GraphicsBuffer addresses, int triAddress)
    {
        geoInfoLoader.SetBuffer(0, "_MemoryBuffer", memory);
        geoInfoLoader.SetBuffer(0, "_AddressDict", addresses);
        geoInfoLoader.SetInt("triAddress", triAddress);
        geoInfoLoader.SetInt("triStride", TRI_STRIDE_WORD);

        geoInfoLoader.Dispatch(0, 1, 1, 1);

    }

    public uint[] TranscribeGeometries()
    {
        int numShaders = shaders.Count;

        uint[] geoShaderAddresses = new uint[numShaders];
        GraphicsBuffer addressesReference = GenerationPreset.memoryHandle.Address;

        for (int i = 0; i < numShaders; i++){
            CopyGeoCount(offsets.shadGeoCStart + i);
            geoShaderAddresses[i] = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, GEO_VERTEX_STRIDE * 3, offsets.baseGeoCounter);
            ComputeBuffer memoryReference = GenerationPreset.memoryHandle.GetBlockBuffer(geoShaderAddresses[i]);
            TranscribeGeometry(memoryReference, addressesReference, (int)geoShaderAddresses[i], offsets.shadGeoCStart + i);
        }

        return geoShaderAddresses;
    }

    public RenderParams[] SetupShaderMaterials(MemoryBufferHandler memoryHandle, uint[] address)
    {
        RenderParams[] rps = new RenderParams[shaders.Count];
        GraphicsBuffer addressBuffer = memoryHandle.Address;

        for (int i = 0; i < shaders.Count; i++) {
            ComputeBuffer sourceBuffer = memoryHandle.GetBlockBuffer(address[i]);
            RenderParams rp = new RenderParams(shaders[i].GetMaterial()) {
                worldBounds = shaderBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                matProps = new MaterialPropertyBlock()
            };

            rp.matProps.SetBuffer("_StorageMemory", sourceBuffer);
            rp.matProps.SetBuffer("_AddressDict", addressBuffer);
            rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
            rp.matProps.SetInt("addressIndex", (int)address[i]);

            rps[i] = rp;
        }
        return rps;
    }

    public uint[] GetShaderDrawArgs() {
        int numShaders = shaders.Count;
        uint[] shaderDrawArgs = new uint[numShaders];

        for (int i = 0; i < numShaders; i++) {
            shaderDrawArgs[i] = UtilityBuffers.DrawArgs.Allocate();
            GetDrawArgs(UtilityBuffers.DrawArgs.Get(), (int)shaderDrawArgs[i], offsets.shadGeoCStart + i);
        }
        return shaderDrawArgs;
    }

    void GetDrawArgs(GraphicsBuffer indirectArgs, int address, int geoSizeCounter) {
        shaderDrawArgs.SetBuffer(0, "prefixSizes", UtilityBuffers.GenerationBuffer);
        shaderDrawArgs.SetInt("bCOUNTER_oGeo", geoSizeCounter);

        shaderDrawArgs.SetBuffer(0, "_IndirectArgsBuffer", indirectArgs);
        shaderDrawArgs.SetInt("argOffset", address);

        shaderDrawArgs.Dispatch(0, 1, 1, 1);
    }

    void CopyGeoCount(int geoSizeCounter) {
        geoSizeCalculator.SetBuffer(0, "prefixSizes", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bCOUNT_oGeo", geoSizeCounter);

        geoSizeCalculator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bCOUNT_write", offsets.baseGeoCounter);

        geoSizeCalculator.Dispatch(0, 1, 1, 1);
    }

    void ConstructPrefixSum(int numShaders) {
        sizePrefixSum.SetInt("numShaders", numShaders);
        sizePrefixSum.Dispatch(0, 1, 1, 1);
    }

    void FilterShaderGeometry(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(filterGeometry, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        filterGeometry.SetBuffer(0, "vertices", vertMemory);
        filterGeometry.SetBuffer(0, "triangles", triMemory);
        filterGeometry.SetBuffer(0, "_AddressDict", addresses);
        filterGeometry.SetInt("vertAddress", vertAddress);
        filterGeometry.SetInt("triAddress", triAddress);

        filterGeometry.DispatchIndirect(0, args);
    }

    void TranscribeGeometry(ComputeBuffer memory, GraphicsBuffer addresses, int addressIndex, int geoSizeCounter) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        geoTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        geoTranscriber.SetBuffer(0, "_AddressDict", addresses);
        geoTranscriber.SetInt("addressIndex", addressIndex);
        geoTranscriber.SetInt("bCOUNTER_oGeo", geoSizeCounter);

        geoTranscriber.DispatchIndirect(0, args);
    }

    void CountGeometrySizes(ComputeBuffer vertMemory, ComputeBuffer triMemory, GraphicsBuffer addresses, int vertAddress, int triAddress) {
        ComputeBuffer args = UtilityBuffers.CountToArgs(matSizeCounter, UtilityBuffers.GenerationBuffer, offsets.baseGeoCounter);

        matSizeCounter.SetBuffer(0, "vertices", vertMemory);
        matSizeCounter.SetBuffer(0, "triangles", triMemory);
        matSizeCounter.SetBuffer(0, "_AddressDict", addresses);
        matSizeCounter.SetInt("vertAddress", vertAddress);
        matSizeCounter.SetInt("triAddress", triAddress);

        matSizeCounter.DispatchIndirect(0, args);
    }

    private static void ReleaseEmptyShaders(ShaderUpdateTask shader){
        void OnAddressRecieved(AsyncGPUReadbackRequest request){
            if (!shader.Active) return;

            uint2 memAddress = request.GetData<uint2>().ToArray()[0];
            if (memAddress.x != 0) return; //No geometry to readback
            shader.Release(ref GenerationPreset.memoryHandle);
            return;
        }
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: 8*(int)shader.address, OnAddressRecieved);
    }

    public class ShaderUpdateTask : IUpdateSubscriber {
        private bool active = false;
        public bool Active {
            get => active;
            set => active = value;
        }
        public uint address;
        public uint dispArgs;
        RenderParams rp;
        public ShaderUpdateTask(uint address, uint dispArgs, RenderParams rp) {
            this.address = address;
            this.dispArgs = dispArgs;
            this.rp = rp;
            this.active = true;
        }

        public void Update(MonoBehaviour mono = null) {
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.DrawArgs.Get(), 1, (int)dispArgs);
        }

        public void Release(ref MemoryBufferHandler memory) {
            if (!active) return;
            active = false;

            memory.ReleaseMemory(address);
            UtilityBuffers.DrawArgs.Release(dispArgs);
        }

        public void Release(ref MemoryOccupancyBalancer memory) {
            if (!active) return;
            active = false;

            memory.ReleaseMemory(address);
            UtilityBuffers.DrawArgs.Release(dispArgs);
        }
    }
    
    public struct GeoShaderOffsets : BufferOffsets {
        public int baseGeoCounter; 
        public int matSizeCStart;
        public int shadGeoCStart;
        public int triIndDictStart;
        public int fBaseGeoStart;
        public int shadGeoStart;
        private int offsetStart; private int offsetEnd;
        /// <summary> The start of the buffer region that is used by the GeoShader generator. 
        /// See <see cref="BufferOffsets.bufferStart"/> for more info. </summary>
        public int bufferStart{get{return offsetStart;}}
        /// <summary> The end of the buffer region that is used by the GeoShader generator. 
        /// See <see cref="BufferOffsets.bufferEnd"/> for more info. </summary>
        public int bufferEnd{get{return offsetEnd;}}
        public GeoShaderOffsets(int maxChunkSize, int maxShaderCount, int bufferStart) {
            int numPointsPerAxis = maxChunkSize + 1;
            int numOfTris = (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * 5;

            this.offsetStart = bufferStart;
            baseGeoCounter = bufferStart;
            matSizeCStart = 1 + baseGeoCounter;
            shadGeoCStart = (maxShaderCount+1) + matSizeCStart;
            triIndDictStart = (maxShaderCount+1) + shadGeoCStart;
            fBaseGeoStart = numOfTris + triIndDictStart;
            shadGeoStart = Mathf.CeilToInt(((float)numOfTris + fBaseGeoStart) / GEN_TRI_STRIDE);
            this.offsetEnd = (shadGeoStart + numOfTris) * GEN_TRI_STRIDE;
        }
    }

}