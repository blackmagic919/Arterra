using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Containers/DensityGenerator")]
public class DensityGenerator : ScriptableObject
{
    const int threadGroupSize = 8;
    public ComputeShader terrainNoiseCompute;
    public ComputeShader undergroundNoiseCompute;
    public ComputeShader materialGenCompute;
    public ComputeShader meshGenerator;
    public ComputeShader densitySimplification;

    public Queue<ComputeBuffer> buffersToRelease;

    public void SetPoints(ComputeBuffer points)
    {
        meshGenerator.SetBuffer(0, "points", points);
        undergroundNoiseCompute.SetBuffer(0, "points", points);
        terrainNoiseCompute.SetBuffer(0, "points", points);
    }

    public void SimplifyDensity(int chunkSize, int meshSkipInc, float[] density, ComputeBuffer points)
    {
        int totalPoints = (chunkSize+1)*(chunkSize+1)*(chunkSize+1);
        ComputeBuffer completeDensity = new ComputeBuffer(totalPoints, sizeof(float));
        completeDensity.SetData(density);
        buffersToRelease.Enqueue(completeDensity);

        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", chunkSize+1);
        densitySimplification.SetInt("pointsPerAxis", chunkSize/meshSkipInc + 1);
        densitySimplification.SetBuffer(0, "points_full", completeDensity);
        densitySimplification.SetBuffer(0, "points", points);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize);

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

            GenerateNoise(materialGenCompute, chunkSize, meshSkipInc, materials[i].generationNoise, offset, buffersToRelease);

            materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);
        }
    }

    public void GenerateMesh(int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer triangleBuffer)
    {
        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetFloat("numPointsPerAxis", chunkSize / meshSkipInc + 1);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateUnderground(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset)
    {
        GenerateNoise(undergroundNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        undergroundNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateTerrain(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, float depth, float IsoValue)
    {
        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("depth", depth);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);

        GenerateNoise(terrainNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt((chunkSize / meshSkipInc) / (float)threadGroupSize) + 1;

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public static void GenerateNoise(ComputeShader noiseGen, int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, Queue<ComputeBuffer> buffersToRelease)
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
        buffersToRelease.Enqueue(offsetsBuffer);

        noiseGen.SetInt("chunkSize", chunkSize);
        noiseGen.SetInt("octaves", noiseData.octaves);
        noiseGen.SetInt("meshSkipInc", meshSkipInc);
        noiseGen.SetInt("numPointsPerAxis", chunkSize / meshSkipInc + 1);
        noiseGen.SetBuffer(0, "offsets", offsetsBuffer);
        noiseGen.SetFloat("persistence", noiseData.persistance);
        noiseGen.SetFloat("lacunarity", noiseData.lacunarity);
        noiseGen.SetFloat("noiseScale", scale);
        noiseGen.SetFloat("maxPossibleHeight", maxPossibleHeight);
    }
}
