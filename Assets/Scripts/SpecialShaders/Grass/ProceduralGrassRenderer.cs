using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[CreateAssetMenu(menuName = "ShaderData/GrassShader/Generator")]
public class ProceduralGrassRenderer : SpecialShader
{
    public GrassSettings grassSettings = default;

    [System.Serializable]
    public class GrassSettings 
    {
        [Tooltip("Total height of grass layer stack")]
        public float grassHeight = 0.5f;
        [Tooltip("Maximum # of grass layers")]
        public int maxLayers = 16;
        [Tooltip("Multiplier on World Position if using world position as UV")]
        public float worldPositionUVScale;

        [Tooltip("The grass geometry creating compute shader")]
        public ComputeShader grassComputeShader = default;
        public Material material;
    }

    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();


    public override Material GetMaterial()
    {
        return grassSettings.material;
    }

    public override void ProcessGeoShader(Transform transform, MemoryBufferSettings memoryHandle, int vertAddress, int triAddress, 
                        int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        int idGrassKernel = grassSettings.grassComputeShader.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.AccessStorage();
        ComputeBuffer addresses = memoryHandle.AccessAddresses();

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(grassSettings.grassComputeShader, UtilityBuffers.GenerationBuffer, baseGeoCount);

        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "SourceVertices", memory);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "SourceTriangles", memory);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "_AddressDict", addresses); 
        grassSettings.grassComputeShader.SetInt("vertAddress", vertAddress);
        grassSettings.grassComputeShader.SetInt("triAddress", triAddress);

        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "counters", UtilityBuffers.GenerationBuffer);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        grassSettings.grassComputeShader.SetInt("bSTART_base", baseGeoStart);
        grassSettings.grassComputeShader.SetInt("bCOUNT_base", baseGeoCount);
        grassSettings.grassComputeShader.SetInt("bSTART_oGeo", geoStart);
        grassSettings.grassComputeShader.SetInt("bCOUNT_oGeo", geoCounter);

        grassSettings.grassComputeShader.SetFloat("_TotalHeight", grassSettings.grassHeight);
        grassSettings.grassComputeShader.SetFloat("_WorldPositionToUVScale", grassSettings.worldPositionUVScale);
        grassSettings.grassComputeShader.SetInt("_MaxLayers", grassSettings.maxLayers);
        grassSettings.grassComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        grassSettings.grassComputeShader.DispatchIndirect(idGrassKernel, args);
    }

    public override void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }
}
