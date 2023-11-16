using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Containers/Terrain Generator")]
public class TerrainGenerator : ScriptableObject
{
    const int threadGroupSize = 8;
    public ComputeShader noiseMapGenerator;
    public ComputeShader terrainCombiner;
    public ComputeShader undergroundAnalyzer;

    public Queue<ComputeBuffer> buffersToRelease;

    //Returns raw noise data
    public ComputeBuffer GetNoiseMap(NoiseData noiseData, Vector2 offset, float maxInfluenceHeight, int chunkSize, int meshSkipInc, bool centerNoise, out float[] rawPoints)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;
        ComputeBuffer rawPointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

        buffersToRelease.Enqueue(rawPointBuffer);
        buffersToRelease.Enqueue(results);

        Vector3 offset3D = new Vector3(offset.x, 0, offset.y);
        noiseMapGenerator.SetBuffer(0, "rawPoints", rawPointBuffer);
        noiseMapGenerator.SetBuffer(0, "points", results);
        noiseMapGenerator.SetFloat("influenceHeight", maxInfluenceHeight);
        noiseMapGenerator.SetInt("numPointsPerAxis", numPointsAxes);

        if (centerNoise)
            noiseMapGenerator.EnableKeyword("CENTER_NOISE");
        else
            noiseMapGenerator.DisableKeyword("CENTER_NOISE");

        Generator.SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D, ref buffersToRelease);

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        noiseMapGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        rawPoints = new float[numOfPoints];
        rawPointBuffer.GetData(rawPoints);

        return results;
    }

    public ComputeBuffer CombineTerrainMaps(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int numOfPoints, float terrainOffset)
    {
        ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

        buffersToRelease.Enqueue(results);

        terrainCombiner.SetBuffer(0, "continental", contBuffer);
        terrainCombiner.SetBuffer(0, "erosion", erosionBuffer);
        terrainCombiner.SetBuffer(0, "peaksValleys", PVBuffer);
        terrainCombiner.SetBuffer(0, "Result", results);

        terrainCombiner.SetInt("numOfPoints", numOfPoints);
        terrainCombiner.SetFloat("heightOffset", terrainOffset);

        int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);
        terrainCombiner.Dispatch(0, numThreadsPerAxis, 1, 1);

        return results;
    }


    public ComputeBuffer AnalyzeNoiseMap(Vector3[] points, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, bool centerNoise)
    {
        int numPoints = points.Length;
        ComputeBuffer positions = new ComputeBuffer(numPoints, sizeof(float) * 3);
        positions.SetData(points);

        ComputeBuffer result = new ComputeBuffer(numPoints, sizeof(float));

        buffersToRelease.Enqueue(positions);
        buffersToRelease.Enqueue(result);

        undergroundAnalyzer.SetBuffer(0, "CheckPoints", positions);
        undergroundAnalyzer.SetBuffer(0, "Results", result);
        undergroundAnalyzer.SetInt("numPoints", numPoints);
        undergroundAnalyzer.SetFloat("influenceHeight", maxInfluenceHeight);
        if(centerNoise)
            undergroundAnalyzer.EnableKeyword("CENTER_NOISE");
        else
            undergroundAnalyzer.DisableKeyword("CENTER_NOISE");

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);

        Generator.SetNoiseData(undergroundAnalyzer, chunkSize, 1, noiseData, offset, ref buffersToRelease);

        undergroundAnalyzer.Dispatch(0, numThreadsPerAxis, 1, 1);

        return result;
    }
}
