using Newtonsoft.Json;
using UnityEngine;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Quality;
using System.Linq;
using Unity.Mathematics;

[CreateAssetMenu(menuName = "ShaderData/QuadShader/Generator")]
public class ProceduralInsetQuads : GeoShader
{
    [Tooltip("A mesh to create foliage from")]
    [SerializeField] public Registry<QuadSetting> settings = default;
    [JsonIgnore][UISetting(Ignore = true)]
    public Option<Material> material;
    [JsonIgnore] private ComputeShader quadCompute;
    [JsonIgnore] private ComputeBuffer variantTable;

    public override Material GetMaterial() => material.value;
    public override IRegister GetRegistry() => settings;
    public override void SetRegistry(IRegister reg) => settings = (Registry<QuadSetting>)reg;

    public override void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        if (settings.Reg.Count == 0) return;
        QuadSetting.Data[] data = settings.Reg.Select(e => e.GetInfo()).ToArray();
        variantTable = new ComputeBuffer(data.Length, QuadSetting.DataSize, ComputeBufferType.Constant);
        variantTable.SetData(data);

        quadCompute = Resources.Load<ComputeShader>("Compute/GeoShader/Registry/FoliageQuads");

        int kernel = quadCompute.FindKernel("Main");
        quadCompute.SetBuffer(kernel, "Counters", UtilityBuffers.GenerationBuffer);
        quadCompute.SetBuffer(kernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        quadCompute.SetBuffer(kernel, "VariantSettings", variantTable);
        material.value.SetBuffer("VariantSettings", variantTable);
        quadCompute.SetInt("bSTART_base", baseGeoStart);
        quadCompute.SetInt("bCOUNT_base", baseGeoCount);
        quadCompute.SetInt("bSTART_oGeo", geoStart);
        quadCompute.SetInt("bCOUNT_oGeo", geoCounter);
        quadCompute.SetInt("geoInd", geoInd);
    }


    public override void Release(){
        variantTable?.Release();
    }

    public override void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, int baseGeoCount)
    {
        if (settings.Reg.Count == 0) return;
        int idFoliageKernel = quadCompute.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.Storage;
        ComputeBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(quadCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        quadCompute.SetBuffer(idFoliageKernel, "SourceVertices", memory);
        quadCompute.SetBuffer(idFoliageKernel, "SourceTriangles", memory);
        quadCompute.SetBuffer(idFoliageKernel, "_AddressDict", addresses);
        quadCompute.SetInt("vertAddress", vertAddress);
        quadCompute.SetInt("triAddress", triAddress);
        quadCompute.DispatchIndirect(idFoliageKernel, args);
    }



}

