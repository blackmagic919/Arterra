using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[CreateAssetMenu(menuName = "ShaderData/GrassShader/Generator")]
public class ProceduralGrassRenderer : SpecialShader
{
    public GrassSettings grassSettings = default;

    [System.Serializable]
    public struct GrassSettings 
    {
        [Tooltip("Total height of grass layer stack")]
        public float grassHeight; //0.5f
        [Tooltip("Maximum # of grass layers")]
        public int maxLayers; //15f
        [Tooltip("Multiplier on World Position if using world position as UV")]
        public float worldPositionUVScale;

        [Tooltip("The grass geometry creating compute shader")][JsonIgnore][UISetting(Ignore = true)]
        public Option<ComputeShader> grassComputeShader;
        [JsonIgnore][UISetting(Ignore = true)]
        public Option<Material> material;
    }


    public override Material GetMaterial()
    {
        return grassSettings.material.value;
    }

    public override void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, 
                                          int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        ComputeShader grassCompute = grassSettings.grassComputeShader.value;

        int idGrassKernel = grassCompute.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.Storage;
        ComputeBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(grassCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        grassCompute.SetBuffer(idGrassKernel, "SourceVertices", memory);
        grassCompute.SetBuffer(idGrassKernel, "SourceTriangles", memory);
        grassCompute.SetBuffer(idGrassKernel, "_AddressDict", addresses); 
        grassCompute.SetInt("vertAddress", vertAddress);
        grassCompute.SetInt("triAddress", triAddress);

        grassCompute.SetBuffer(idGrassKernel, "counters", UtilityBuffers.GenerationBuffer);
        grassCompute.SetBuffer(idGrassKernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        grassCompute.SetBuffer(idGrassKernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        grassCompute.SetInt("bSTART_base", baseGeoStart);
        grassCompute.SetInt("bCOUNT_base", baseGeoCount);
        grassCompute.SetInt("bSTART_oGeo", geoStart);
        grassCompute.SetInt("bCOUNT_oGeo", geoCounter);

        grassCompute.SetFloat("_TotalHeight", grassSettings.grassHeight);
        grassCompute.SetInt("_MaxLayers", grassSettings.maxLayers);

        grassCompute.DispatchIndirect(idGrassKernel, args);
    }
}
