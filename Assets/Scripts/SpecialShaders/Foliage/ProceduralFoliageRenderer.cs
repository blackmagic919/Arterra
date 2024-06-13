using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[CreateAssetMenu(menuName = "ShaderData/FoliageShader/Generator")]
public class ProceduralFoliageRenderer : SpecialShader
{
    [Tooltip("A mesh to create foliage from")]
    [SerializeField] private FoliageSettings foliageSettings = default; 

    [System.Serializable]
    public class FoliageSettings
    {
        [Tooltip("Size of Quad Images")]
        public float QuadSize = 1.0f;
        [Tooltip("Distance Extruded Along Normal")]
        public float Inflation = 0f;
        
        [Tooltip("The grass geometry creating compute shader")]
        public ComputeShader foliageComputeShader = default;
        public Material material;
    }

    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public override Material GetMaterial()
    {
        return foliageSettings.material;
    }

    public override void ProcessGeoShader(Transform transform, MemoryBufferSettings memoryHandle, int vertAddress, int triAddress, 
                        int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        int idFoliageKernel = foliageSettings.foliageComputeShader.FindKernel("Main");

        ComputeBuffer memory = memoryHandle.AccessStorage();
        ComputeBuffer addresses = memoryHandle.AccessAddresses();

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(foliageSettings.foliageComputeShader, UtilityBuffers.GenerationBuffer, baseGeoCount);

        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "SourceVertices", memory);
        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "SourceTriangles", memory);
        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "_AddressDict", addresses); 
        foliageSettings.foliageComputeShader.SetInt("vertAddress", vertAddress);
        foliageSettings.foliageComputeShader.SetInt("triAddress", triAddress);

        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "counters", UtilityBuffers.GenerationBuffer);
        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        foliageSettings.foliageComputeShader.SetBuffer(idFoliageKernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        foliageSettings.foliageComputeShader.SetInt("bSTART_base", baseGeoStart);
        foliageSettings.foliageComputeShader.SetInt("bCOUNT_base", baseGeoCount);
        foliageSettings.foliageComputeShader.SetInt("bSTART_oGeo", geoStart);
        foliageSettings.foliageComputeShader.SetInt("bCOUNT_oGeo", geoCounter);

        foliageSettings.foliageComputeShader.SetFloat("_QuadSize", foliageSettings.QuadSize);
        foliageSettings.foliageComputeShader.SetFloat("_InflationFactor", foliageSettings.Inflation);
        foliageSettings.foliageComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        foliageSettings.foliageComputeShader.DispatchIndirect(idFoliageKernel, args);
    }


    public override void ReleaseTempBuffers()
    {
        while(tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }

}

