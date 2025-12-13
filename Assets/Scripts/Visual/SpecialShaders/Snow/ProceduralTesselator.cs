using Newtonsoft.Json;
using UnityEngine;
using WorldConfig;
using WorldConfig.Quality;
using System.Linq;
using System.Collections.Generic;

[CreateAssetMenu(menuName = "ShaderData/Tesselation/Generator")]
public class ProceduralTesselator : GeoShader
{
    public Catalogue<TesselSettings> settings = default;
    public Option<List<TesselLevel>> detailLevels = default;
    [JsonIgnore][UISetting(Ignore = true)]
    public Option<Material> material;
    [JsonIgnore] private ComputeShader tesselCompute;
    [JsonIgnore] private ComputeBuffer variantTable;
    [JsonIgnore] private ComputeBuffer detailTable;

    // Start is called before the first frame update
    public override Material GetMaterial() => material.value;
    public override IRegister GetRegistry() => settings;
    public override void SetRegistry(IRegister reg) => settings = (Catalogue<TesselSettings>)reg;
    
    public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        if (settings.Reg.Count == 0) return;
        TesselSettings.Data[] data = settings.Reg.Select(e => e.info).ToArray();
        variantTable = new ComputeBuffer(data.Length, TesselSettings.DataSize, ComputeBufferType.Structured);
        detailTable = new ComputeBuffer(detailLevels.value.Count, ShellLevel.DataSize, ComputeBufferType.Structured);
        int mapChunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
        detailTable.SetData(detailLevels.value);
        variantTable.SetData(data);

        tesselCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/SnowTessel");
        int kernel = tesselCompute.FindKernel("Main");
        tesselCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
        tesselCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        tesselCompute.SetBuffer(kernel, "VariantSettings", variantTable);
        tesselCompute.SetBuffer(kernel, "DetailSettings", detailTable);
        SubChunkShaderGraph.PresetSubChunkInfo(tesselCompute);
        tesselCompute.SetInt("numPointsPerAxis", mapChunkSize);
        tesselCompute.SetInt("bSTART_base", baseGeoStart);
        tesselCompute.SetInt("bCOUNT_base", baseGeoCount);
        tesselCompute.SetInt("bSTART_oGeo", geoStart);
        tesselCompute.SetInt("bCOUNT_oGeo", geoCounter);
        tesselCompute.SetInt("geoInd", geoInd);
    }

    public override void Release(){
        variantTable?.Release();
        detailTable?.Release();
    }
    
    public override void ProcessGeoShader(MemoryBufferHandler memoryHandle, int vertAddress, int triAddress, int baseGeoCount, int parentDepth)
    {
        if (settings.Reg.Count == 0) return;
        int kernel = tesselCompute.FindKernel("Main");
        ComputeBuffer vertSource = memoryHandle.GetBlockBuffer(vertAddress);
        ComputeBuffer triSource = memoryHandle.GetBlockBuffer(triAddress);
        GraphicsBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(tesselCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        tesselCompute.SetBuffer(kernel, ShaderIDProps.SourceVertices, vertSource);
        tesselCompute.SetBuffer(kernel, ShaderIDProps.SourceTriangles, triSource);
        tesselCompute.SetBuffer(kernel, ShaderIDProps.AddressDict, addresses);
        tesselCompute.SetInt(ShaderIDProps.VertAddress, vertAddress);
        tesselCompute.SetInt(ShaderIDProps.TriAddress, triAddress);
        tesselCompute.DispatchIndirect(kernel, args);
    }

}
