using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Generator
{
    public static void SetNoiseData(ComputeShader noiseGen, int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ref Queue<ComputeBuffer> buffersToRelease)
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
        noiseGen.SetFloat("persistence", noiseData.persistance);
        noiseGen.SetFloat("lacunarity", noiseData.lacunarity);
        noiseGen.SetFloat("noiseScale", scale);
        noiseGen.SetFloat("maxPossibleHeight", maxPossibleHeight);
    }
}
