using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;
using static EndlessTerrain;
using static UtilityBuffers;

public class ShaderGenerator
{
    private GeneratorSettings settings;
    public static ComputeShader matSizeCounter;
    public static ComputeShader filterGeometry;
    public static ComputeShader sizePrefixSum;
    public static ComputeShader indirectThreads;
    public static ComputeShader geoSizeCalculator;
    public static ComputeShader geoTranscriber;
    public static ComputeShader shaderDrawArgs;
    public static ComputeShader geoInfoLoader;


    private Transform transform;
    const int GEO_VERTEX_STRIDE = 3 * 2 + 2;
    const int TRI_STRIDE_WORD = 3;

    private Bounds shaderBounds;
    private ShaderUpdateTask[] shaderUpdateTasks;


    static ShaderGenerator(){
        matSizeCounter = Resources.Load<ComputeShader>("GeoShader/Generation/ShaderMatSizeCounter");
        filterGeometry = Resources.Load<ComputeShader>("GeoShader/Generation/FilterShaderGeometry");
        sizePrefixSum = Resources.Load<ComputeShader>("GeoShader/Generation/ShaderPrefixConstructor");
        geoSizeCalculator = Resources.Load<ComputeShader>("GeoShader/Generation/GeometryMemorySize");
        geoTranscriber = Resources.Load<ComputeShader>("GeoShader/Generation/TranscribeGeometry");
        shaderDrawArgs = Resources.Load<ComputeShader>("GeoShader/Generation/GeoDrawArgs");
        geoInfoLoader = Resources.Load<ComputeShader>("GeoShader/Generation/GeoInfoLoader");

        indirectThreads = Resources.Load<ComputeShader>("Utility/DivideByThreads");
    }

    const int MAX_SHADERS = 4;

    private static int baseGeoCounter; 
    private static int matSizeCStart;
    private static int shadGeoCStart;
    private static int triIndDictStart;
    private static int fBaseGeoStart;
    private static int shadGeoStart;


    public static void PresetData(){
        int maxChunkSize = WorldStorageHandler.WORLD_OPTIONS.Rendering.value.mapChunkSize;
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

        //Lol look a
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
    }

    public ShaderGenerator(Transform transform, Bounds boundsOS)
    {
        this.settings = WorldStorageHandler.WORLD_OPTIONS.GeoShaders.value;
        this.transform = transform;
        this.shaderBounds = CustomUtility.TransformBounds(transform, boundsOS);
        this.shaderUpdateTasks = new ShaderUpdateTask[this.settings.shaderDictionary.value.Count];
    }

    public void ReleaseGeometry()
    {
        foreach (ShaderUpdateTask task in shaderUpdateTasks){
            if (task == null || !task.active)
                continue;
            task.active = false;

            task.Release(GenerationPreset.memoryHandle);
        }
    }


    public void ComputeGeoShaderGeometry(AsyncMeshReadback.GeometryHandle vertHandle, AsyncMeshReadback.GeometryHandle triHandle)
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
        
        for(int i = 0; i < this.settings.shaderDictionary.value.Count; i++){
            this.shaderUpdateTasks[i] = new ShaderUpdateTask(geoShaderMemAdds[i], geoShaderDispArgs[i], geoShaderParams[i]);
            MainLoopUpdateTasks.Enqueue(this.shaderUpdateTasks[i]);
        }
    }


    public void FilterGeometry(GenerationPreset.MemoryHandle memory, int triAddress, int vertAddress)
    {

        int numShaders = this.settings.shaderDictionary.value.Count;
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
        for (int i = 0; i < this.settings.shaderDictionary.value.Count; i++){
            SpecialShader geoShader = this.settings.shaderDictionary.value[i].value;
            
            geoShader.ProcessGeoShader(memory, vertAddress, triAddress, fBaseGeoStart, matSizeCStart + i, baseGeoCounter, shadGeoStart, i);
            UtilityBuffers.CopyCount(source: UtilityBuffers.GenerationBuffer, dest: UtilityBuffers.GenerationBuffer, readOffset: baseGeoCounter, writeOffset: shadGeoCStart + i + 1);
            geoShader.ReleaseTempBuffers();
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
        int numShaders = this.settings.shaderDictionary.value.Count;

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
        RenderParams[] rps = new RenderParams[this.settings.shaderDictionary.value.Count];
        for(int i = 0; i < this.settings.shaderDictionary.value.Count; i++)
        {
            RenderParams rp = new RenderParams(settings.shaderDictionary.value[i].value.GetMaterial())
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
        int numShaders = this.settings.shaderDictionary.value.Count;
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

    

    public class ShaderUpdateTask : UpdateTask{
        uint address;
        uint dispArgs;
        RenderParams rp;
        public ShaderUpdateTask(uint address, uint dispArgs, RenderParams rp){
            this.address = address;
            this.dispArgs = dispArgs;
            this.rp = rp;
            this.active = true;
        }

        public override void Update(MonoBehaviour mono = null)
        {
            Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, UtilityBuffers.ArgumentBuffer, 1, (int)dispArgs);
        }

        public void Release(GenerationPreset.MemoryHandle memory){
            memory.ReleaseMemory(address);
            UtilityBuffers.ReleaseArgs(dispArgs);
        }
    }

    /*

    ComputeBuffer CalculateArgsFromPrefix(ComputeBuffer shaderStartIndexes, int shaderIndex)
    {
        ComputeBuffer geoShaderArgs = UtilityBuffers.indirectArgs;

        geoTranscriber.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);

        prefixShaderArgs.SetBuffer(0, "_PrefixStart", shaderStartIndexes);
        prefixShaderArgs.SetInt("shaderIndex", shaderIndex);
        prefixShaderArgs.SetInt("threadGroupSize", (int)threadGroupSize);

        prefixShaderArgs.SetBuffer(0, "indirectArgs", geoShaderArgs);

        prefixShaderArgs.Dispatch(0, 1, 1, 1);
        return geoShaderArgs;
    }
     
    public MeshInfo ReadBackMesh()
    {
        MeshInfo chunk = new MeshInfo();

        int numShaders = this.settings.shaderDictionary.Count;

        int[] geoLengths = new int[numShaders + 1];
        shaderStartIndexes.GetData(geoLengths);

        if (geoLengths[numShaders - 1] == 0)
            return chunk;

        int fullLength = geoLengths[numShaders];
        Triangle[] geometry = new Triangle[fullLength];
        geoShaderGeometry.GetData(geometry);
        for (int s = 0; s < numShaders; s++)
        {
            for (int i = geoLengths[s]; i < geoLengths[s+1]; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    chunk.triangles.Add(3*i + j);
                    chunk.vertices.Add(geometry[i][j].pos);
                    chunk.UVs.Add(geometry[i][j].uv);
                    chunk.normals.Add(geometry[i][j].norm);
                    chunk.colorMap.Add(new Color(geometry[i][j].color.x, geometry[i][j].color.y, geometry[i][j].color.z, geometry[i][j].color.w));
                }
            }

            chunk.subMeshes.Add(new UnityEngine.Rendering.SubMeshDescriptor(geoLengths[s] * 3, (geoLengths[s + 1] - geoLengths[s]) * 3, MeshTopology.Triangles));
        }

        return chunk;
    }
     */


}
