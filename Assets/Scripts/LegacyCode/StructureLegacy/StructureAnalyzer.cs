using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static UtilityBuffers;

public class StructureAnalyzer
{
    const int threadGroupSize = 8;

    public ComputeShader undergroundAnalyzer;
    public ComputeShader terrainAnalyzer;
    public ComputeShader structureApplier;
    public ComputeShader TriCountToVertCount;

    public ComputeShader terrainCombiner;
    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public ComputeBuffer AnalyzeBase(Vector3[] points, NoiseData undergroundNoise, Vector3 offset, int chunkSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPoints = points.Length;
        ComputeBuffer positions = new ComputeBuffer(numPoints, sizeof(float) * 3);
        positions.SetData(points);

        ComputeBuffer results = new ComputeBuffer(numPoints, sizeof(float));
        results.SetData(Enumerable.Repeat(0, numPoints).ToArray());

        bufferHandle.Enqueue(positions);
        bufferHandle.Enqueue(results);

        undergroundAnalyzer.SetBuffer(0, "CheckPoints", positions);
        undergroundAnalyzer.SetBuffer(0, "Results", results);
        undergroundAnalyzer.SetInt("numPoints", numPoints);
        undergroundAnalyzer.SetFloat("influenceHeight", 1.0f);
        undergroundAnalyzer.DisableKeyword("CENTER_NOISE");

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);

        SetNoiseData(undergroundAnalyzer, chunkSize, 1, undergroundNoise, offset);

        undergroundAnalyzer.Dispatch(0, numThreadsPerAxis, 1, 1);

        return results;
    }

    public float[] AnalyzeTerrain(float[] yPos, ComputeBuffer baseBuffer, ComputeBuffer heightBuffer, ComputeBuffer squashBuffer)
    {
        int numPoints = yPos.Length;
        ComputeBuffer yPosBuffer = new ComputeBuffer(numPoints, sizeof(float));
        yPosBuffer.SetData(yPos);

        ComputeBuffer results = new ComputeBuffer(numPoints, sizeof(float));

        terrainAnalyzer.SetBuffer(0, "results", results);
        terrainAnalyzer.SetBuffer(0, "positionY", yPosBuffer);
        terrainAnalyzer.SetBuffer(0, "base", baseBuffer);
        terrainAnalyzer.SetBuffer(0, "heights", heightBuffer);
        terrainAnalyzer.SetBuffer(0, "squash", squashBuffer);

        terrainAnalyzer.SetInt("numPoints", numPoints);

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        terrainAnalyzer.Dispatch(0, numThreadsPerAxis, 1, 1);

        float[] ret = new float[numPoints];
        results.GetData(ret);

        yPosBuffer.Release();
        baseBuffer.Release();
        heightBuffer.Release();
        squashBuffer.Release();
        results.Release();

        return ret;
    }

    public void SetStructureData(ComputeBuffer pointBuffer, float[] density, float IsoLevel, int meshSkipInc, int chunkSize, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        ComputeBuffer structureDensity = new ComputeBuffer(density.Length, sizeof(float));
        structureDensity.SetData(density);

        bufferHandle.Enqueue(structureDensity);

        structureApplier.SetBuffer(0, "structureDensity", structureDensity);
        structureApplier.SetBuffer(0, "points", pointBuffer);
        structureApplier.SetInt("pointsPerAxis", numPointsAxes);
        structureApplier.SetFloat("IsoLevel", IsoLevel);

        structureApplier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }
}
