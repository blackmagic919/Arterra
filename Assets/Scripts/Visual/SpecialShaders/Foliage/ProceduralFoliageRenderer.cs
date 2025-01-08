using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;
using TerrainGeneration;
using WorldConfig;
using WorldConfig.Quality;

[CreateAssetMenu(menuName = "ShaderData/FoliageShader/Generator")]
public class ProceduralFoliageRenderer : GeoShader
{
    [Tooltip("A mesh to create foliage from")]
    [SerializeField] public FoliageSettings foliageSettings = default; 

    [System.Serializable]
    public struct FoliageSettings
    {
        [Tooltip("Size of Quad Images")]
        public float QuadSize; //1.0f
        [Tooltip("Distance Extruded Along Normal")]
        public float Inflation; //0f
        
        [Tooltip("The grass geometry creating compute shader")][JsonIgnore][UISetting(Ignore = true)]
        public Option<ComputeShader> foliageComputeShader;
        [JsonIgnore][UISetting(Ignore = true)]
        public Option<Material> material;
    }

    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public override Material GetMaterial()
    {
        return foliageSettings.material.value;
    }

    public override void ProcessGeoShader(GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, 
                                          int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {
        ComputeShader foliageCompute = foliageSettings.foliageComputeShader.value;

        int idFoliageKernel = foliageCompute.FindKernel("Main");
        ComputeBuffer memory = memoryHandle.Storage;
        ComputeBuffer addresses = memoryHandle.Address;

        ComputeBuffer args = UtilityBuffers.PrefixCountToArgs(foliageCompute, UtilityBuffers.GenerationBuffer, baseGeoCount);

        foliageCompute.SetBuffer(idFoliageKernel, "SourceVertices", memory);
        foliageCompute.SetBuffer(idFoliageKernel, "SourceTriangles", memory);
        foliageCompute.SetBuffer(idFoliageKernel, "_AddressDict", addresses); 
        foliageCompute.SetInt("vertAddress", vertAddress);
        foliageCompute.SetInt("triAddress", triAddress);

        foliageCompute.SetBuffer(idFoliageKernel, "counters", UtilityBuffers.GenerationBuffer);
        foliageCompute.SetBuffer(idFoliageKernel, "BaseTriangles", UtilityBuffers.GenerationBuffer);
        foliageCompute.SetBuffer(idFoliageKernel, "DrawTriangles", UtilityBuffers.GenerationBuffer);
        foliageCompute.SetInt("bSTART_base", baseGeoStart);
        foliageCompute.SetInt("bCOUNT_base", baseGeoCount);
        foliageCompute.SetInt("bSTART_oGeo", geoStart);
        foliageCompute.SetInt("bCOUNT_oGeo", geoCounter);

        foliageCompute.SetFloat("_QuadSize", foliageSettings.QuadSize);
        foliageCompute.SetFloat("_InflationFactor", foliageSettings.Inflation);

        foliageCompute.DispatchIndirect(idFoliageKernel, args);
    }


    public override void ReleaseTempBuffers()
    {
        while(tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }

}

