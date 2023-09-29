using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DensityGenerator : MonoBehaviour
{
    const int threadGroupSize = 8;
    public ComputeShader terrainNoiseCompute;
    public ComputeShader undergroundNoiseCompute;
    public ComputeShader materialGenCompute;
    public ComputeShader meshGenerator;

    List<ComputeBuffer> buffersToRelease;

    public void SetPoints(ComputeBuffer points)
    {
        meshGenerator.SetBuffer(0, "points", points);
        undergroundNoiseCompute.SetBuffer(0, "points", points);
        terrainNoiseCompute.SetBuffer(0, "points", points);
    }

    public void GenerateMat(List<GenerationHeightData.BMaterial> materials, ComputeBuffer vertexBuffer, MeshCreator.TriangleConst[] triangles, int numTris, float[][] heights, int chunkSize, int meshSkipInc, Vector3 offset)
    {
        int numTrisAxis = Mathf.CeilToInt(Mathf.Sqrt(numTris));
        int numThreadsAxis = Mathf.CeilToInt(numTrisAxis / (float)threadGroupSize);
        ComputeBuffer materialBuffer = new ComputeBuffer(numTris * 3 * 2, sizeof(uint) + sizeof(float));
        ComputeBuffer triangleRBuffer = new ComputeBuffer(numTris, sizeof(float) * 3 * 9);
        triangleRBuffer.SetData(triangles);

        materialGenCompute.SetFloat("numTris", numTris);
        materialGenCompute.SetFloat("numMats", materials.Count);
        materialGenCompute.SetFloat("numTrisAxis", numTrisAxis);
        materialGenCompute.SetBuffer(0, "trianglesR", triangleRBuffer);
        materialGenCompute.SetBuffer(0, "materials", materialBuffer);
        materialGenCompute.SetBuffer(0, "vertexColor", vertexBuffer);

        for (int i = 0; i < materials.Count; i++)
        {
            buffersToRelease = new List<ComputeBuffer>();

            ComputeBuffer heightRef = new ComputeBuffer(heights[i].Length, sizeof(float));
            buffersToRelease.Add(heightRef);

            heightRef.SetData(heights[i]);

            materialGenCompute.SetInt("matIndex", i);
            materialGenCompute.SetBuffer(0, "heights", heightRef);

            GenerateNoise(materialGenCompute, chunkSize, meshSkipInc, materials[i].generationNoise, offset, buffersToRelease);

            materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, 1);
            foreach (ComputeBuffer buffer in buffersToRelease)
                buffer.Release();
        }

        triangleRBuffer.Release();
        materialBuffer.Release();
    }

    public void GenerateMesh(int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer triangleBuffer)
    {
        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetFloat("numPointsPerAxis", chunkSize / meshSkipInc + 1);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        int numThreadsPerAxis = Mathf.CeilToInt(((chunkSize) / meshSkipInc) / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateUnderground(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset)
    {
        buffersToRelease = new List<ComputeBuffer>();

        GenerateNoise(undergroundNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt(((chunkSize) / meshSkipInc) / (float)threadGroupSize) + 1;

        undergroundNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        foreach (ComputeBuffer buffer in buffersToRelease)
            buffer.Release();
    }

    public void GenerateTerrain(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, float depth, float IsoValue)
    {
        buffersToRelease = new List<ComputeBuffer>();

        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("depth", depth);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);

        GenerateNoise(terrainNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt(((chunkSize) / meshSkipInc) / (float)threadGroupSize) + 1;

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        foreach (ComputeBuffer buffer in buffersToRelease)
            buffer.Release();
    }

    public static void GenerateNoise(ComputeShader noiseGen, int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, List<ComputeBuffer> buffersToRelease)
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
        buffersToRelease.Add(offsetsBuffer);
        offsetsBuffer.SetData(octaveOffsets);

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
