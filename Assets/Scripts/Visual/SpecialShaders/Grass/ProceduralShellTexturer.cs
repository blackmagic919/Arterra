using Newtonsoft.Json;
using UnityEngine;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Quality;
using System.Linq;
using Unity.Mathematics;

[CreateAssetMenu(menuName = "ShaderData/ShellTexture/Generator")]
public class ProceduralShellTexturer : GeoShader
{
    public Registry<ShellSetting> settings = default;
    [JsonIgnore]
    [UISetting(Ignore = true)]
    public Option<Material> material;
    [JsonIgnore] private ComputeShader shellCompute;
    [JsonIgnore] private ComputeBuffer variantTable;

    public override Material GetMaterial() => material.value;
    public override IRegister GetRegistry() => settings;
    public override void SetRegistry(IRegister reg) => settings = (Registry<ShellSetting>)reg;

    public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        if (settings.Reg.Count == 0) return;
        ShellSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
        variantTable = new ComputeBuffer(data.Length, ShellSetting.DataSize, ComputeBufferType.Structured);
        variantTable.SetData(data);

        shellCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/GrassLayers");
        int kernel = shellCompute.FindKernel("Main");
        shellCompute.SetInt("bSTART_base", baseGeoStart);
        shellCompute.SetInt("bCOUNT_base", baseGeoCount);
        shellCompute.SetInt("bSTART_oGeo", geoStart);
        shellCompute.SetInt("bCOUNT_oGeo", geoCounter);
        shellCompute.SetInt("geoInd", geoInd);

        shellCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
        shellCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        shellCompute.SetBuffer(kernel, "VariantSettings", variantTable);
        material.value.SetBuffer("VariantSettings", variantTable);
    }

    public override void Release(){
        variantTable?.Release();
    }

    public override void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, int baseGeoCount)
    {
        if (settings.Reg.Count == 0) return;
        int kernel = shellCompute.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.Storage;
        ComputeBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(shellCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        shellCompute.SetBuffer(kernel, "SourceVertices", memory);
        shellCompute.SetBuffer(kernel, "SourceTriangles", memory);
        shellCompute.SetBuffer(kernel, "_AddressDict", addresses);
        shellCompute.SetInt("vertAddress", vertAddress);
        shellCompute.SetInt("triAddress", triAddress);

        shellCompute.DispatchIndirect(kernel, args);
    }
}
