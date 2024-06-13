using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "ShaderData/SnowShader/Generator")]
public class ProceduralSnowRenderer : SpecialShader
{
    public SnowSettings snowSettings = default;

    [System.Serializable]
    public class SnowSettings
    {
        [Tooltip("How much to tesselate base mesh")]
        public uint tesselationFactor = 3;

        [Tooltip("The tesselation compute shader")]
        public ComputeShader tesselComputeShader = default;
        public Material material;
    }
    // Start is called before the first frame update
    public override Material GetMaterial()
    {
        return snowSettings.material;
    }
    public override void ProcessGeoShader(Transform transform, MemoryBufferSettings memoryHandle, int vertAddress, int triAddress, 
                        int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        int idGrassKernel = snowSettings.tesselComputeShader.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.AccessStorage();
        ComputeBuffer addresses = memoryHandle.AccessAddresses();

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(snowSettings.tesselComputeShader, UtilityBuffers.GenerationBuffer, baseGeoCount);

        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "SourceVertices", memory);
        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "SourceTriangles", memory);
        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "_AddressDict", addresses); 
        snowSettings.tesselComputeShader.SetInt("vertAddress", vertAddress);
        snowSettings.tesselComputeShader.SetInt("triAddress", triAddress);
        snowSettings.tesselComputeShader.SetInt("geoInd", geoInd);

        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "counters", UtilityBuffers.GenerationBuffer);
        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        snowSettings.tesselComputeShader.SetBuffer(idGrassKernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        snowSettings.tesselComputeShader.SetInt("bSTART_base", baseGeoStart);
        snowSettings.tesselComputeShader.SetInt("bCOUNT_base", baseGeoCount);
        snowSettings.tesselComputeShader.SetInt("bSTART_oGeo", geoStart);
        snowSettings.tesselComputeShader.SetInt("bCOUNT_oGeo", geoCounter);

        snowSettings.tesselComputeShader.SetInt("tesselFactor", (int)snowSettings.tesselationFactor);
        snowSettings.tesselComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        snowSettings.tesselComputeShader.DispatchIndirect(idGrassKernel, args);
    }
}
