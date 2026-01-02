using Newtonsoft.Json;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Configuration.Quality;
using System.Linq;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "ShaderData/ShellTexture/Generator")]
public class ProceduralShellTexturer : GeoShader
{
    public Catalogue<ShellSetting> settings = default;
    public Option<List<ShellLevel>> detailLevels = default;
    [JsonIgnore]
    [UISetting(Ignore = true)]
    public Option<Material> material;
    [JsonIgnore] private ComputeShader shellCompute;
    [JsonIgnore] private ComputeBuffer variantTable;
    [JsonIgnore] private ComputeBuffer detailTable;

    public override Material GetMaterial() => material.value;
    public override IRegister GetRegistry() => settings;
    public override void SetRegistry(IRegister reg) => settings = (Catalogue<ShellSetting>)reg;

    public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        if (settings.Reg.Count == 0) return;
        ShellSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
        variantTable = new ComputeBuffer(data.Length, ShellSetting.DataSize, ComputeBufferType.Structured);
        detailTable = new ComputeBuffer(detailLevels.value.Count, ShellLevel.DataSize, ComputeBufferType.Structured);
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        detailTable.SetData(detailLevels.value);
        variantTable.SetData(data);

        shellCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/GrassLayers");
        int kernel = shellCompute.FindKernel("Main");
        shellCompute.SetInt("bSTART_base", baseGeoStart);
        shellCompute.SetInt("bCOUNT_base", baseGeoCount);
        shellCompute.SetInt("bSTART_oGeo", geoStart);
        shellCompute.SetInt("bCOUNT_oGeo", geoCounter);
        shellCompute.SetInt("numPointsPerAxis", mapChunkSize);
        shellCompute.SetInt("geoInd", geoInd);
        SubChunkShaderGraph.PresetSubChunkInfo(shellCompute);
        
        shellCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
        shellCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        shellCompute.SetBuffer(kernel, "VariantSettings", variantTable);
        shellCompute.SetBuffer(kernel, "DetailSettings", detailTable);
        material.value.SetBuffer("VariantSettings", variantTable);
    }

    public override void Release(){
        variantTable?.Release();
        detailTable?.Release();
    }

    public override void ProcessGeoShader(MemoryBufferHandler memoryHandle, int vertAddress, int triAddress, int baseGeoCount, int parentDepth) {
        if (settings.Reg.Count == 0) return;
        int kernel = shellCompute.FindKernel("Main");
        ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
        ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
        GraphicsBuffer addresses = memoryHandle.Address;
        float invScale = 1.0f / (1 << parentDepth);

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(shellCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        shellCompute.SetBuffer(kernel, ShaderIDProps.SourceVertices, vertSource);
        shellCompute.SetBuffer(kernel, ShaderIDProps.SourceTriangles, triSource);
        shellCompute.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        shellCompute.SetInt(ShaderIDProps.VertAddress, vertAddress);
        shellCompute.SetInt(ShaderIDProps.TriAddress, triAddress);
        shellCompute.SetFloat(ShaderIDProps.ScaleInverse, invScale);

        shellCompute.DispatchIndirect(kernel, args);
    }
}
