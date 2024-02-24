using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Utils;

[CreateAssetMenu(menuName = "ShaderData/GrassShader/Generator")]
public class ProceduralGrassRenderer : SpecialShader
{
    public GrassSettings grassSettings = default;

    private Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    private int idGrassKernel;
    private int idIndirectArgsKernel;

    public override Material GetMaterial()
    {
        return grassSettings.material;
    }

    public override void ProcessGeoShader(Transform transform, ComputeBuffer sourceTriangles, ComputeBuffer startIndices, ComputeBuffer drawTriangles, int shaderIndex)
    {
        idGrassKernel = grassSettings.grassComputeShader.FindKernel("Main");
        idIndirectArgsKernel = grassSettings.indirectArgsShader.FindKernel("Main");

        uint argsGroupSize; grassSettings.indirectArgsShader.GetKernelThreadGroupSizes(idIndirectArgsKernel, out argsGroupSize, out _, out _);
        ComputeBuffer dispatchArgs = SetArgs(startIndices, shaderIndex, (int)argsGroupSize, ref tempBuffers);

        GenerateGeometry(transform, sourceTriangles, startIndices, drawTriangles, dispatchArgs, shaderIndex);
    }

    void GenerateGeometry(Transform transform, ComputeBuffer sourceTriangles, ComputeBuffer startIndices, ComputeBuffer drawTriangles, ComputeBuffer args, int shaderIndex)
    {
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "_SourceTriangles", sourceTriangles);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "_SourceStartIndices", startIndices);
        grassSettings.grassComputeShader.SetBuffer(idGrassKernel, "_DrawTriangles", drawTriangles); //This is the output

        grassSettings.grassComputeShader.SetFloat("_TotalHeight", grassSettings.grassHeight);
        grassSettings.grassComputeShader.SetFloat("_WorldPositionToUVScale", grassSettings.worldPositionUVScale);
        grassSettings.grassComputeShader.SetInt("_MaxLayers", grassSettings.maxLayers);
        grassSettings.grassComputeShader.SetInt("_ShaderIndex", shaderIndex);
        grassSettings.grassComputeShader.SetMatrix("_LocalToWorld", transform.localToWorldMatrix);

        grassSettings.grassComputeShader.DispatchIndirect(idGrassKernel, args);
    }

    ComputeBuffer SetArgs(ComputeBuffer prefixIndexes, int shaderIndex, int threadGroupSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer indirectArgs = new ComputeBuffer(3, sizeof(uint), ComputeBufferType.Structured);
        indirectArgs.SetData(new uint[] { 1, 1, 1 });
        bufferHandle.Enqueue(indirectArgs);

        grassSettings.indirectArgsShader.SetBuffer(idIndirectArgsKernel, "prefixStart", prefixIndexes);
        grassSettings.indirectArgsShader.SetInt("shaderIndex", shaderIndex);
        grassSettings.indirectArgsShader.SetInt("threadGroupSize", threadGroupSize);
        grassSettings.indirectArgsShader.SetBuffer(idIndirectArgsKernel, "indirectArgs", indirectArgs);

        grassSettings.indirectArgsShader.Dispatch(idIndirectArgsKernel, 1, 1, 1);

        return indirectArgs;
    }

    public override void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }


    public Bounds TransformBounds(Bounds boundsOS, Transform transform)
    {
        var center = transform.TransformPoint(boundsOS.center);

        var size = boundsOS.size;
        var axisX = transform.TransformVector(size.x, 0, 0);
        var axisY = transform.TransformVector(0, size.y, 0);
        var axisZ = transform.TransformVector(0, 0, size.z);

        size.x = Mathf.Abs(axisX.x) + Mathf.Abs(axisY.x) + Mathf.Abs(axisZ.x);
        size.y = Mathf.Abs(axisX.y) + Mathf.Abs(axisY.y) + Mathf.Abs(axisZ.y);
        size.z = Mathf.Abs(axisX.z) + Mathf.Abs(axisY.z) + Mathf.Abs(axisZ.z);

        return new Bounds(center, size);
    }
}
