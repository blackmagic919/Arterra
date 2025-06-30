using Newtonsoft.Json;
using UnityEngine;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Quality;
using System.Linq;
using Unity.Mathematics;

[CreateAssetMenu(menuName = "ShaderData/Tesselation/Generator")]
public class ProceduralTesselator : GeoShader
{
    public Registry<TesselSettings> settings = default;
    [JsonIgnore][UISetting(Ignore = true)]
    public Option<Material> material;
    [JsonIgnore] private ComputeShader tesselCompute;
    [JsonIgnore] private ComputeBuffer variantTable;

    // Start is called before the first frame update
    public override Material GetMaterial() => material.value;
    public override IRegister GetRegistry() => settings;
    public override void SetRegistry(IRegister reg) => settings = (Registry<TesselSettings>)reg;
    
    public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        if (settings.Reg.Count == 0) return;
        TesselSettings.Data[] data = settings.Reg.Select(e => e.info).ToArray();
        variantTable = new ComputeBuffer(data.Length, TesselSettings.DataSize, ComputeBufferType.Structured);
        variantTable.SetData(data);

        tesselCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/SnowTessel");

        int kernel = tesselCompute.FindKernel("Main");
        tesselCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
        tesselCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        tesselCompute.SetBuffer(kernel, "VariantSettings", variantTable);
        tesselCompute.SetInt("bSTART_base", baseGeoStart);
        tesselCompute.SetInt("bCOUNT_base", baseGeoCount);
        tesselCompute.SetInt("bSTART_oGeo", geoStart);
        tesselCompute.SetInt("bCOUNT_oGeo", geoCounter);
        tesselCompute.SetInt("geoInd", geoInd);
    }

    public override void Release(){
        variantTable?.Release();
    }
    
    public override void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, int baseGeoCount)
    {
        if (settings.Reg.Count == 0) return;
        int kernel = tesselCompute.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.Storage;
        ComputeBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(tesselCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        tesselCompute.SetBuffer(kernel, "SourceVertices", memory);
        tesselCompute.SetBuffer(kernel, "SourceTriangles", memory);
        tesselCompute.SetBuffer(kernel, "_AddressDict", addresses);
        tesselCompute.SetInt("vertAddress", vertAddress);
        tesselCompute.SetInt("triAddress", triAddress);
        tesselCompute.DispatchIndirect(kernel, args);
    }
}
