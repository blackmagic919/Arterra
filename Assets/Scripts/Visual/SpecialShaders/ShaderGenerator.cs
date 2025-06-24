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

    const int MAX_SHADERS = 4;

    private static int baseGeoCounter; 
    private static int matSizeCStart;
    private static int shadGeoCStart;
    private static int triIndDictStart;
    private static int fBaseGeoStart;
    private static int shadGeoStart;

    public static void PresetData(){
        int maxChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        int numPointsPerAxis = maxChunkSize + 1;
        int numOfTris = (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * (numPointsPerAxis - 1) * 5;
        /* Gen Buffer Organization
        [ baseGeoCounter(4b) | matSizeCounters(20b) |  shadGeoCounters(20b) | triIndDict(5.25 mb) | filteredBaseGeo(5.25mb) | GeoShaderGeometry(rest of buffer)]
        */
        
        baseGeoCounter = 0;
        matSizeCStart = 1 + baseGeoCounter;
        shadGeoCStart = (MAX_SHADERS+1) + matSizeCStart;
        triIndDictStart = (MAX_SHADERS+1) + shadGeoCStart;
        fBaseGeoStart = numOfTris + triIndDictStart;
        shadGeoStart = Mathf.CeilToInt(((float)numOfTris + fBaseGeoStart) / (GEO_VERTEX_STRIDE*3.0f));

        geoInfoLoader.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        geoInfoLoader.SetInt("bCOUNTER_tri", baseGeoCounter);

        matSizeCounter.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetBuffer(0, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetBuffer(0, "shaderIndexOffset", UtilityBuffers.GenerationBuffer);
        matSizeCounter.SetInt("bSTART_scount", matSizeCStart);
        matSizeCounter.SetInt("bSTART_tri", triIndDictStart);

        sizePrefixSum.SetBuffer(0, "shaderCountOffset", UtilityBuffers.GenerationBuffer);
        sizePrefixSum.SetInt("bSTART_scount", matSizeCStart);

        filterGeometry.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(0, "triangleIndexOffset", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetBuffer(0, "shaderPrefix", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetInt("bSTART_scount", matSizeCStart);
        filterGeometry.SetInt("bSTART_tri", triIndDictStart);
        filterGeometry.SetInt("bCOUNTER_base", baseGeoCounter);

        filterGeometry.SetBuffer(0, "filteredGeometry", UtilityBuffers.GenerationBuffer);
        filterGeometry.SetInt("bSTART_sort", fBaseGeoStart);

        geoTranscriber.SetBuffer(0, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetBuffer(0, "ShaderPrefixes", UtilityBuffers.GenerationBuffer);
        geoTranscriber.SetInt("bSTART_oGeo", shadGeoStart);

        for (int i = 0; i < Config.CURRENT.Quality.GeoShaders.Reg.Count; i++){
            GeoShader shader = Config.CURRENT.Quality.GeoShaders.Retrieve(i);
            shader.PresetData(fBaseGeoStart, matSizeCStart + i, baseGeoCounter, shadGeoStart, i);
            Material mat = shader.GetMaterial();
            LightBaker.SetupLightSampler(mat);
        }
    }

    public static void Release(){
        foreach (GeoShader shader in Config.CURRENT.Quality.GeoShaders.Reg){
            shader.Release();
        }
    }

    public ShaderGenerator(Transform transform, Bounds boundsOS)
    {
        this.shaders = Config.CURRENT.Quality.GeoShaders.Reg;
        this.transform = transform;
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
        this.shaderUpdateTasks = new ShaderUpdateTask[this.shaders.Count];
    }

    public void ReleaseGeometry()
    {
        foreach (ShaderUpdateTask task in shaderUpdateTasks){
            if (task == null || !task.active)
                continue;
            task.Release(GenerationPreset.memoryHandle);
        }
    }


    public void ComputeGeoShaderGeometry(GeometryHandle vertHandle, GeometryHandle triHandle)
    {
        ReleaseGeometry();
        int triAddress = (int)triHandle.addressIndex;
        int vertAddress = (int)vertHandle.addressIndex;

        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, triIndDictStart, 0);
        FilterGeometry(GenerationPreset.memoryHandle, triAddress, vertAddress);
        ProcessGeoShaders(GenerationPreset.memoryHandle, vertAddress, triAddress);

        uint[] geoShaderMemAdds = TranscribeGeometries();
        uint[] geoShaderDispArgs = GetShaderDrawArgs();
        RenderParams[] geoShaderParams = SetupShaderMaterials(GenerationPreset.memoryHandle.Storage, GenerationPreset.memoryHandle.Address, geoShaderMemAdds);

        for (int i = 0; i < shaders.Count; i++){
            shaderUpdateTasks[i] = new ShaderUpdateTask(geoShaderMemAdds[i], geoShaderDispArgs[i], geoShaderParams[i]);
            OctreeTerrain.MainLateUpdateTasks.Enqueue(shaderUpdateTasks[i]);
            ReleaseEmptyShaders(shaderUpdateTasks[i]);
        }
    }


    public void FilterGeometry(GenerationPreset.MemoryHandle memory, int triAddress, int vertAddress)
    {

        int numShaders = shaders.Count;
        ComputeBuffer memStorage = memory.Storage;
        ComputeBuffer memAddresses = memory.Address;

        LoadBaseGeoInfo(memStorage, memAddresses, triAddress);

        CountGeometrySizes(memStorage, memAddresses, vertAddress, triAddress);

        ConstructPrefixSum(numShaders);

        FilterShaderGeometry(memStorage, memAddresses, vertAddress, triAddress);
    }

    public void ProcessGeoShaders(GenerationPreset.MemoryHandle memory, int vertAddress, int triAddress)
    {
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 1, 0); //clear base count
        for (int i = 0; i < shaders.Count; i++){
            GeoShader geoShader = shaders[i];
            geoShader.ProcessGeoShader(memory, vertAddress, triAddress, matSizeCStart + i);
            UtilityBuffers.CopyCount(source: UtilityBuffers.GenerationBuffer, dest: UtilityBuffers.GenerationBuffer, readOffset: baseGeoCounter, writeOffset: shadGeoCStart + i + 1);
        }
    }

    public void LoadBaseGeoInfo(ComputeBuffer memory, ComputeBuffer addresses, int triAddress)
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
        ComputeBuffer memoryReference = GenerationPreset.memoryHandle.Storage;
        ComputeBuffer addressesReference = GenerationPreset.memoryHandle.Address;

        for (int i = 0; i < numShaders; i++)
        {
            CopyGeoCount(shadGeoCStart + i);
            geoShaderAddresses[i] = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, GEO_VERTEX_STRIDE * 3, baseGeoCounter);
            TranscribeGeometry(memoryReference, addressesReference, (int)geoShaderAddresses[i], shadGeoCStart + i);
        }

        return geoShaderAddresses;
    }

    public RenderParams[] SetupShaderMaterials(ComputeBuffer storageBuffer, ComputeBuffer addressBuffer, uint[] address)
    {
        RenderParams[] rps = new RenderParams[shaders.Count];
        for(int i = 0; i < shaders.Count; i++)
        {
            RenderParams rp = new RenderParams(shaders[i].GetMaterial())
            {
                worldBounds = shaderBounds,
                shadowCastingMode = ShadowCastingMode.Off,
                matProps = new MaterialPropertyBlock()
            };

            rp.matProps.SetBuffer("_StorageMemory", storageBuffer);
            rp.matProps.SetBuffer("_AddressDict", addressBuffer);
            rp.matProps.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);
            rp.matProps.SetInt("addressIndex", (int)address[i]);

            rps[i] = rp;
        }
        return rps;
    }

    public uint[] GetShaderDrawArgs()
    {
        int numShaders = shaders.Count;
        uint[] shaderDrawArgs = new uint[numShaders];

        for (int i = 0; i < numShaders; i++)
        {
            shaderDrawArgs[i] = UtilityBuffers.AllocateArgs();
            GetDrawArgs(UtilityBuffers.ArgumentBuffer, (int)shaderDrawArgs[i], shadGeoCStart + i);
        }
        return shaderDrawArgs;
    }

    void GetDrawArgs(GraphicsBuffer indirectArgs, int address, int geoSizeCounter)
    {
        shaderDrawArgs.SetBuffer(0, "prefixSizes", UtilityBuffers.GenerationBuffer);
        shaderDrawArgs.SetInt("bCOUNTER_oGeo", geoSizeCounter);

        shaderDrawArgs.SetBuffer(0, "_IndirectArgsBuffer", indirectArgs);
        shaderDrawArgs.SetInt("argOffset", address);

        shaderDrawArgs.Dispatch(0, 1, 1, 1);
    }

    void CopyGeoCount(int geoSizeCounter)
    {
        geoSizeCalculator.SetBuffer(0, "prefixSizes", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bCOUNT_oGeo", geoSizeCounter);

        geoSizeCalculator.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        geoSizeCalculator.SetInt("bCOUNT_write", baseGeoCounter);

        geoSizeCalculator.Dispatch(0, 1, 1, 1);
    }

    void ConstructPrefixSum(int numShaders)
    {
        sizePrefixSum.SetInt("numShaders", numShaders);
        sizePrefixSum.Dispatch(0, 1, 1, 1);
    }

    void FilterShaderGeometry(ComputeBuffer memory, ComputeBuffer addresses, int vertAddress, int triAddress)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(filterGeometry, UtilityBuffers.GenerationBuffer, baseGeoCounter);

        filterGeometry.SetBuffer(0, "vertices", memory);
        filterGeometry.SetBuffer(0, "triangles", memory);
        filterGeometry.SetBuffer(0, "_AddressDict", addresses);
        filterGeometry.SetInt("vertAddress", vertAddress);
        filterGeometry.SetInt("triAddress", triAddress);

        filterGeometry.DispatchIndirect(0, args);
    }

    void TranscribeGeometry(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, int geoSizeCounter)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(geoTranscriber, UtilityBuffers.GenerationBuffer, baseGeoCounter);

        geoTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        geoTranscriber.SetBuffer(0, "_AddressDict", addresses);
        geoTranscriber.SetInt("addressIndex", addressIndex);
        geoTranscriber.SetInt("bCOUNTER_oGeo", geoSizeCounter);

        geoTranscriber.DispatchIndirect(0, args);
    }

    void CountGeometrySizes(ComputeBuffer memory, ComputeBuffer addresses, int vertAddress, int triAddress)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(matSizeCounter, UtilityBuffers.GenerationBuffer, baseGeoCounter);

        matSizeCounter.SetBuffer(0, "vertices", memory);
        matSizeCounter.SetBuffer(0, "triangles", memory);
        matSizeCounter.SetBuffer(0, "_AddressDict", addresses);
        matSizeCounter.SetInt("vertAddress", vertAddress);
        matSizeCounter.SetInt("triAddress", triAddress);

        matSizeCounter.DispatchIndirect(0, args);
    }

    private static void ReleaseEmptyShaders(ShaderUpdateTask shader){
        void OnAddressRecieved(AsyncGPUReadbackRequest request){
            if (!shader.active) return;

            uint2 memAddress = request.GetData<uint2>().ToArray()[0];
            if (memAddress.x != 0) return; //No geometry to readback
            shader.Release(GenerationPreset.memoryHandle);
            return;
        }
        AsyncGPUReadback.Request(GenerationPreset.memoryHandle.Address, size: 8, offset: 8*(int)shader.address, OnAddressRecieved);
    }

    public class ShaderUpdateTask : UpdateTask
    {
        public uint address;
        public uint dispArgs;
        RenderParams rp;
        public ShaderUpdateTask(uint address, uint dispArgs, RenderParams rp)
        {
            this.address = address;
            this.dispArgs = dispArgs;
            this.rp = rp;
            this.active = true;
        }

        public override void Update(MonoBehaviour mono = null)
        {
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)dispArgs);
        }

        public void Release(GenerationPreset.MemoryHandle memory)
        {
            if (!active) return;
            active = false;
            
            memory.ReleaseMemory(address);
            UtilityBuffers.ReleaseArgs(dispArgs);
        }
        
    }

}