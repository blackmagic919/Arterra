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

    public Queue<ComputeBuffer> buffersToRelease;

    public void SimplifyDensity(int chunkSize, int meshSkipInc, float[] density, ComputeBuffer pointBuffer)
    {
        int totalPoints = (chunkSize+1)*(chunkSize+1)*(chunkSize+1);
        ComputeBuffer completeDensity = new ComputeBuffer(totalPoints, sizeof(float));
        completeDensity.SetData(density);
        buffersToRelease.Enqueue(completeDensity);

        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", chunkSize+1);
        densitySimplification.SetInt("pointsPerAxis", chunkSize/meshSkipInc + 1);
        densitySimplification.SetBuffer(0, "points_full", completeDensity);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

       
    public void GenerateMat(List<GenerationHeightData.BMaterial> materials, ComputeBuffer vertexBuffer, ComputeBuffer existingMats, Vector3[] triangles, Vector3[] triangleParents, int numVerts, float[][] heights, int chunkSize, int meshSkipInc, Vector3 offset)
    {
        int numVertsAxis = Mathf.CeilToInt(Mathf.Pow(numVerts, 1f/3f));
        int numThreadsAxis = Mathf.CeilToInt(numVertsAxis / (float)threadGroupSize);

        ComputeBuffer triangleRBuffer = new ComputeBuffer(numVerts, sizeof(float) * 3);
        triangleRBuffer.SetData(triangles);
        ComputeBuffer materialBuffer = new ComputeBuffer(numVerts * 2, sizeof(uint) + sizeof(float));
        ComputeBuffer parentTriRBuffer = new ComputeBuffer(numVerts * 2, sizeof(float) * 3);
        parentTriRBuffer.SetData(triangleParents);

        buffersToRelease.Enqueue(materialBuffer);
        buffersToRelease.Enqueue(triangleRBuffer);
        buffersToRelease.Enqueue(parentTriRBuffer);

        materialGenCompute.SetFloat("numVerts", numVerts);
        materialGenCompute.SetFloat("numMats", materials.Count);
        materialGenCompute.SetFloat("numVertsAxis", numVertsAxis);
        materialGenCompute.SetBuffer(0, "trianglesR", triangleRBuffer);
        materialGenCompute.SetBuffer(0, "parentTriR", parentTriRBuffer);
        materialGenCompute.SetBuffer(0, "vertexColor", vertexBuffer);
        materialGenCompute.SetBuffer(0, "materials", materialBuffer);
        materialGenCompute.SetBool("hasMats", false);

        if (existingMats != null)
        {
            materialGenCompute.EnableKeyword("HAS_EXISTING_MATERIALS");
            materialGenCompute.SetBuffer(0, "existingMats", existingMats);
            materialGenCompute.SetFloat("pointsPerAxis", chunkSize / meshSkipInc + 1);
        }
        else
        {
            materialGenCompute.DisableKeyword("HAS_EXISTING_MATERIALS");
        }

        for (int i = 0; i < materials.Count; i++)
        {
            ComputeBuffer heightRef = new ComputeBuffer(heights[i].Length, sizeof(float));
            heightRef.SetData(heights[i]);
            buffersToRelease.Enqueue(heightRef);

            materialGenCompute.SetInt("genOrder", i);
            materialGenCompute.SetInt("matIndex", materials[i].materialIndex);
            materialGenCompute.SetBuffer(0, "heights", heightRef);

            SetNoiseData(materialGenCompute, chunkSize, meshSkipInc, materials[i].generationNoise, offset);

            materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
        }
    }

    public float[] GetPointDensity(Vector3[] points, NoiseData undergroundNoise, NoiseData terrainNoise, Vector3 offset, float IsoValue, float depth, int chunkSize)
    {
        int numPoints = points.Length;
        ComputeBuffer positions = new ComputeBuffer(numPoints, sizeof(float) * 3);
        positions.SetData(points);

        ComputeBuffer results = new ComputeBuffer(numPoints, sizeof(float));

        buffersToRelease.Enqueue(positions);
        buffersToRelease.Enqueue(results);

        undergroundAnalyzer.SetBuffer(0, "CheckPoints", positions);
        terrainAnalyzer.SetBuffer(0, "CheckPoints", positions);
        undergroundAnalyzer.SetBuffer(0, "Results", results);
        terrainAnalyzer.SetBuffer(0, "Results", results);
        undergroundAnalyzer.SetInt("numPoints", numPoints);
        terrainAnalyzer.SetInt("numPoints", numPoints);

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize) + 1;

        SetNoiseData(undergroundAnalyzer, chunkSize, 1, undergroundNoise, offset);

        undergroundAnalyzer.Dispatch(0, numThreadsPerAxis, 1, 1);

        terrainAnalyzer.SetFloat("offsetY", offset.y);
        terrainAnalyzer.SetFloat("depth", depth);
        terrainAnalyzer.SetFloat("IsoLevel", IsoValue);

        SetNoiseData(terrainAnalyzer, chunkSize, 1, terrainNoise, offset);

        terrainAnalyzer.Dispatch(0, numThreadsPerAxis, 1, 1);

        float[] ret = new float[numPoints];
        results.GetData(ret);
        return ret;
    }

    public void SetStructureData(ComputeBuffer pointBuffer, float[] density, float IsoLevel, int meshSkipInc, int chunkSize)
    {
        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        ComputeBuffer structureDensity = new ComputeBuffer(density.Length, sizeof(float));
        structureDensity.SetData(density);

        buffersToRelease.Enqueue(structureDensity);
        
        structureApplier.SetBuffer(0, "structureDensity", structureDensity);
        structureApplier.SetBuffer(0, "points", pointBuffer);
        structureApplier.SetInt("pointsPerAxis", chunkSize / meshSkipInc + 1);
        structureApplier.SetFloat("IsoLevel", IsoLevel);

        structureApplier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateMesh(int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer triangleBuffer, ComputeBuffer pointBuffer)
    {
        meshGenerator.SetBuffer(0, "points", pointBuffer);
        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetFloat("numPointsPerAxis", chunkSize / meshSkipInc + 1);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateUnderground(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ComputeBuffer pointBuffer)
    {
        undergroundNoiseCompute.SetBuffer(0, "points", pointBuffer);
        SetNoiseData(undergroundNoiseCompute, chunkSize, meshSkipInc, noiseData, offset);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        undergroundNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateTerrain(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, float depth, float IsoValue, ComputeBuffer pointBuffer)
    {
        terrainNoiseCompute.SetBuffer(0, "points", pointBuffer);
        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("depth", depth);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);

        SetNoiseData(terrainNoiseCompute, chunkSize, meshSkipInc, noiseData, offset);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void SetNoiseData(ComputeShader noiseGen, int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset)
    {
        float epsilon = (float)10E-9;

        float scale = Mathf.Max(epsilon, noiseData.noiseScale);
        System.Random prng = new System.Random(noiseData.seed);

        float maxPossibleHeight = 0;
        float amplitude = 1;

        Vector3[] octaveOffsets = new Vector3[noiseData.octaves];
        for (int i = 0; i < noiseData.octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5) + offset.x;
            float offsetY = prng.Next((int)-10E5, (int)10E5) + offset.y;
            float offsetZ = prng.Next((int)-10E5, (int)10E5) + offset.z;
            octaveOffsets[i] = new Vector3(offsetX, offsetY, offsetZ);

            maxPossibleHeight += amplitude;
            amplitude *= noiseData.persistance;
        }


        var offsetsBuffer = new ComputeBuffer(octaveOffsets.Length, sizeof(float) * 3);
        offsetsBuffer.SetData(octaveOffsets);

        var splineBuffer = new ComputeBuffer(noiseData.splinePoints.Length, sizeof(float) * 4);
        splineBuffer.SetData(noiseData.splinePoints);

        buffersToRelease.Enqueue(offsetsBuffer);
        buffersToRelease.Enqueue(splineBuffer);

        noiseGen.SetBuffer(0, "offsets", offsetsBuffer);
        noiseGen.SetBuffer(0, "SplinePoints", splineBuffer);
        noiseGen.SetInt("numSplinePoints", noiseData.splinePoints.Length);
        noiseGen.SetInt("chunkSize", chunkSize);
        noiseGen.SetInt("octaves", noiseData.octaves);
        noiseGen.SetInt("meshSkipInc", meshSkipInc);
        noiseGen.SetInt("numPointsPerAxis", chunkSize / meshSkipInc + 1);
        noiseGen.SetFloat("persistence", noiseData.persistance);
        noiseGen.SetFloat("lacunarity", noiseData.lacunarity);
        noiseGen.SetFloat("noiseScale", scale);
        noiseGen.SetFloat("maxPossibleHeight", maxPossibleHeight);
    }
}
