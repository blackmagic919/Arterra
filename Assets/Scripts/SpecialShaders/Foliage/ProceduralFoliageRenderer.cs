using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[CreateAssetMenu(menuName = "ShaderData/FoliageShader/Generator")]
public class ProceduralFoliageRenderer : SpecialShader
{
    [Tooltip("A mesh to create foliage from")]
    [SerializeField] private FoliageSettings foliageSettings = default; 

    private Transform objTransform;

    private int idFoliageKernel;
    private int idIndirectArgsKernel;

    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public override void Instantiate(Transform transform)
    {
        this.objTransform = transform;
    }

    public override Material GetMaterial()
    {
        return Instantiate(foliageSettings.material);
    }

    public override void ProcessGeoShader(Transform transform, MemoryBufferSettings memoryHandle, int vertAddress, int triAddress, 
                        int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart)
    {
        idFoliageKernel = foliageSettings.foliageComputeShader.FindKernel("Main");
        idIndirectArgsKernel = foliageSettings.indirectArgsShader.FindKernel("Main");

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

    ComputeBuffer SetArgs(ComputeBuffer prefixIndexes, int shaderIndex, int threadGroupSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer indirectArgs = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.Structured);
        indirectArgs.SetData(new uint[]{ 1, 1, 1});
        bufferHandle.Enqueue(indirectArgs);

        foliageSettings.indirectArgsShader.SetBuffer(idIndirectArgsKernel, "prefixStart", prefixIndexes);
        foliageSettings.indirectArgsShader.SetInt("shaderIndex", shaderIndex);
        foliageSettings.indirectArgsShader.SetInt("threadGroupSize", threadGroupSize);
        foliageSettings.indirectArgsShader.SetBuffer(idIndirectArgsKernel, "indirectArgs", indirectArgs);

        foliageSettings.indirectArgsShader.Dispatch(idIndirectArgsKernel, 1, 1, 1);

        return indirectArgs;
    }

    public Bounds TransformBounds(Bounds boundsOS)
    {
        var center = objTransform.TransformPoint(boundsOS.center);

        var extents = boundsOS.size; //Don't use boundsOS.extents, this is object space
        var axisX = objTransform.TransformVector(extents.x, 0, 0);
        var axisY = objTransform.TransformVector(0, extents.y, 0);
        var axisZ = objTransform.TransformVector(0, 0, extents.z);

        extents.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        extents.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        extents.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, extents);
    }

}

