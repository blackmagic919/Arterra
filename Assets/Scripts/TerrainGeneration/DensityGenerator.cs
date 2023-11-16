using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using System.Runtime.CompilerServices;

[CreateAssetMenu(menuName = "Containers/DensityGenerator")]
public class DensityGenerator : ScriptableObject
{
    const int threadGroupSize = 8;
    public ComputeShader terrainNoiseCompute;
    public ComputeShader undergroundNoiseCompute;
    public ComputeShader materialGenCompute;
    public ComputeShader meshGenerator;
    public ComputeShader densitySimplification;

    public ComputeShader undergroundAnalyzer;
    public ComputeShader terrainAnalyzer;
    public ComputeShader structureApplier;
    public ComputeShader TriCountToVertCount;

    public Queue<ComputeBuffer> buffersToRelease;

    public void ConvertTriCountToVert(ComputeBuffer args)
    {
        TriCountToVertCount.SetBuffer(0, "_IndirectArgsBuffer", args);
        TriCountToVertCount.Dispatch(0, 1, 1, 1);
    }

    public void SimplifyMaterials(int chunkSize, int meshSkipInc, int[] materials, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeMaterial = new ComputeBuffer(totalPoints, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeMaterial.SetData(materials);
        buffersToRelease.Enqueue(completeMaterial);

        densitySimplification.EnableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeMaterial);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void SimplifyDensity(int chunkSize, int meshSkipInc, float[] density, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeDensity = new ComputeBuffer(totalPoints, sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeDensity.SetData(density);
        buffersToRelease.Enqueue(completeDensity);

        densitySimplification.DisableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeDensity);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

       
    public ComputeBuffer GenerateMat(NoiseData coarseNoise, NoiseData fineNoise, ComputeBuffer structureMat, int[] biomeMap, int chunkSize, int meshSkipInc, Vector3 offset)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        ComputeBuffer materialBuffer = new ComputeBuffer(numPoints, sizeof(int));

        ComputeBuffer biomeBuffer = new ComputeBuffer(biomeMap.Length, sizeof(int));
        biomeBuffer.SetData(biomeMap);


        ComputeBuffer coarseMatDetail = new ComputeBuffer(numPoints, sizeof(float));
        GenerateUnderground(chunkSize, meshSkipInc, coarseNoise, offset, coarseMatDetail);
        ComputeBuffer fineMatDetail = new ComputeBuffer(numPoints, sizeof(float));
        GenerateUnderground(chunkSize, meshSkipInc, fineNoise, offset, coarseMatDetail);

        buffersToRelease.Enqueue(coarseMatDetail);
        buffersToRelease.Enqueue(fineMatDetail);
        buffersToRelease.Enqueue(biomeBuffer);
        buffersToRelease.Enqueue(materialBuffer);

        materialGenCompute.SetBuffer(0, "structureMat", structureMat);
        materialGenCompute.SetBuffer(0, "coarseMatDetail", coarseMatDetail);
        materialGenCompute.SetBuffer(0, "fineMatDetail", fineMatDetail);
        materialGenCompute.SetBuffer(0, "biomeMap", biomeBuffer);
        materialGenCompute.SetBuffer(0, "material", materialBuffer);//Result
        materialGenCompute.SetInt("numPointsPerAxis", numPointsAxes);

        materialGenCompute.SetFloat("meshSkipInc", meshSkipInc);
        materialGenCompute.SetFloat("chunkSize", chunkSize);
        materialGenCompute.SetFloat("offsetY", offset.y);
        
        materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);

        return materialBuffer;
    }

    public ComputeBuffer AnalyzeBase(Vector3[] points, NoiseData undergroundNoise, Vector3 offset, int chunkSize)
    {
        int numPoints = points.Length;
        ComputeBuffer positions = new ComputeBuffer(numPoints, sizeof(float) * 3);
        positions.SetData(points);

        ComputeBuffer results = new ComputeBuffer(numPoints, sizeof(float));
        results.SetData(Enumerable.Repeat(0, numPoints).ToArray());

        buffersToRelease.Enqueue(positions);
        buffersToRelease.Enqueue(results);

        undergroundAnalyzer.SetBuffer(0, "CheckPoints", positions);
        undergroundAnalyzer.SetBuffer(0, "Results", results);
        undergroundAnalyzer.SetInt("numPoints", numPoints);
        undergroundAnalyzer.SetFloat("influenceHeight", 1.0f);
        undergroundAnalyzer.DisableKeyword("CENTER_NOISE");

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);

        Generator.SetNoiseData(undergroundAnalyzer, chunkSize, 1, undergroundNoise, offset, ref buffersToRelease);

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

    public void SetStructureData(ComputeBuffer pointBuffer, float[] density, float IsoLevel, int meshSkipInc, int chunkSize)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        ComputeBuffer structureDensity = new ComputeBuffer(density.Length, sizeof(float));
        structureDensity.SetData(density);

        buffersToRelease.Enqueue(structureDensity);
        
        structureApplier.SetBuffer(0, "structureDensity", structureDensity);
        structureApplier.SetBuffer(0, "points", pointBuffer);
        structureApplier.SetInt("pointsPerAxis", numPointsAxes);
        structureApplier.SetFloat("IsoLevel", IsoLevel);

        structureApplier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateMesh(int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer materialBuffer, ComputeBuffer triangleBuffer, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numCubesAxes = chunkSize / meshSkipInc;
        meshGenerator.SetBuffer(0, "points", pointBuffer);
        meshGenerator.SetBuffer(0, "material", materialBuffer);
        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetInt("numPointsPerAxis", numPointsAxes);
        meshGenerator.SetInt("numCubesPerAxis", numCubesAxes);
        meshGenerator.SetFloat("ResizeFactor", meshSkipInc);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt(numCubesAxes / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateUnderground(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        undergroundNoiseCompute.SetBuffer(0, "points", pointBuffer);
        undergroundNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
        Generator.SetNoiseData(undergroundNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, ref buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        undergroundNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateTerrain(int chunkSize, int meshSkipInc, SurfaceChunk.LODMap surfaceData, Vector3 offset, float IsoValue, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;

        ComputeBuffer heightBuffer = new ComputeBuffer(surfaceData.heightMap.Length, sizeof(float));
        heightBuffer.SetData(surfaceData.heightMap);

        ComputeBuffer squashBuffer = new ComputeBuffer(surfaceData.squashMap.Length, sizeof(float));
        squashBuffer.SetData(surfaceData.squashMap);

        buffersToRelease.Enqueue(heightBuffer);
        buffersToRelease.Enqueue(squashBuffer);

        terrainNoiseCompute.SetBuffer(0, "points", pointBuffer);
        terrainNoiseCompute.SetBuffer(0, "heights", heightBuffer);
        terrainNoiseCompute.SetBuffer(0, "squash", squashBuffer);
        terrainNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
        terrainNoiseCompute.SetFloat("meshSkipInc", meshSkipInc);
        terrainNoiseCompute.SetFloat("chunkSize", chunkSize);
        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);
        

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

}
