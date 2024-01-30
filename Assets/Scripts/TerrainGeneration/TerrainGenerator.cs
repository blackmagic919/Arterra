using System.Collections;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Containers/Terrain Generator")]
public class TerrainGenerator : ScriptableObject
{
    const int threadGroupSize = 8;
    public ComputeShader noiseMapGenerator;
    public ComputeShader terrainCombinerGPU;
    public ComputeShader biomeMapGenerator;
    public ComputeShader mapSimplifier;
    public ComputeShader fullNoiseSampler;
    public ComputeShader terrainCombiner;

    public ComputeShader rawNoiseSampler;
    public ComputeShader checkNoiseSampler;


    //Returns raw noise data
    public ComputeBuffer GetNoiseMap(NoiseData noiseData, Vector2 offset, float maxInfluenceHeight, int chunkSize, int meshSkipInc, bool centerNoise, Queue<ComputeBuffer> bufferHandle, out ComputeBuffer results)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;
        ComputeBuffer rawPoints = new ComputeBuffer(numOfPoints, sizeof(float));
        results = new ComputeBuffer(numOfPoints, sizeof(float));

        bufferHandle.Enqueue(rawPoints);

        Vector3 offset3D = new Vector3(offset.x, 0, offset.y);
        noiseMapGenerator.SetBuffer(0, "rawPoints", rawPoints);
        noiseMapGenerator.SetBuffer(0, "points", results);
        noiseMapGenerator.SetFloat("influenceHeight", maxInfluenceHeight);
        noiseMapGenerator.SetInt("numPointsPerAxis", numPointsAxes);

        if (centerNoise)
            noiseMapGenerator.EnableKeyword("CENTER_NOISE");
        else
            noiseMapGenerator.DisableKeyword("CENTER_NOISE");

        Generator.SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D, ref bufferHandle);

        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        noiseMapGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return rawPoints;
    }

    public ComputeBuffer AnalyzeNoiseMap(Vector3[] points, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, bool centerNoise, Queue<ComputeBuffer> bufferHandle)
    {
        int numPoints = points.Length;
        ComputeBuffer positions = new ComputeBuffer(numPoints, sizeof(float) * 3);
        positions.SetData(points);

        ComputeBuffer result = new ComputeBuffer(numPoints, sizeof(float));

        bufferHandle.Enqueue(positions);
        bufferHandle.Enqueue(result);

        fullNoiseSampler.SetBuffer(0, "CheckPoints", positions);
        fullNoiseSampler.SetBuffer(0, "Results", result);
        fullNoiseSampler.SetInt("numPoints", numPoints);
        fullNoiseSampler.SetFloat("influenceHeight", maxInfluenceHeight);
        if (centerNoise)
            fullNoiseSampler.EnableKeyword("CENTER_NOISE");
        else
            fullNoiseSampler.DisableKeyword("CENTER_NOISE");

        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);

        Generator.SetNoiseData(fullNoiseSampler, chunkSize, 1, noiseData, offset, ref bufferHandle);

        fullNoiseSampler.Dispatch(0, numThreadsPerAxis, 1, 1);

        return result;
    }


    public ComputeBuffer CombineTerrainMaps(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int numOfPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

        bufferHandle.Enqueue(results);

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

    public ComputeBuffer GetBiomeMap(int chunkSize, int meshSkipInc, SurfaceChunk.NoiseMaps noiseData, Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer biomes = new ComputeBuffer(numOfPoints, sizeof(int), ComputeBufferType.Structured);

        bufferHandle.Enqueue(biomes);

        biomeMapGenerator.DisableKeyword("INDIRECT");
        biomeMapGenerator.SetInt("numOfPoints", numOfPoints);
        biomeMapGenerator.SetBuffer(0, "continental", noiseData.continental);
        biomeMapGenerator.SetBuffer(0, "erosion", noiseData.erosion);
        biomeMapGenerator.SetBuffer(0, "peaksValleys", noiseData.pvNoise);
        biomeMapGenerator.SetBuffer(0, "squash", noiseData.squash);
        biomeMapGenerator.SetBuffer(0, "temperature", noiseData.temperature);
        biomeMapGenerator.SetBuffer(0, "humidity", noiseData.humidity);
        biomeMapGenerator.SetBuffer(0, "biomeMap", biomes);

        int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);

        biomeMapGenerator.Dispatch(0, numThreadsPerAxis, 1, 1);

        return biomes;
    }

    public ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, ComputeBuffer count, ComputeBuffer args, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);

        bufferHandle.Enqueue(results);

        terrainCombinerGPU.SetBuffer(0, "continental", contBuffer);
        terrainCombinerGPU.SetBuffer(0, "erosion", erosionBuffer);
        terrainCombinerGPU.SetBuffer(0, "peaksValleys", PVBuffer);
        terrainCombinerGPU.SetBuffer(0, "Result", results);

        terrainCombinerGPU.SetBuffer(0, "numOfPoints", count);
        terrainCombinerGPU.SetFloat("heightOffset", terrainOffset);

        terrainCombinerGPU.DispatchIndirect(0, args);

        return results;
    }

    public ComputeBuffer SimplifyMap(ComputeBuffer source, int chunkSize, int sourceSkipInc, int destSkipInc, bool isFloat, Queue<ComputeBuffer> bufferHandle)
    {
        int sourcePointsAxes = chunkSize / sourceSkipInc + 1;
        int destPointsAxes = chunkSize / destSkipInc + 1;
        int destNumOfPoints = destPointsAxes * destPointsAxes;

        ComputeBuffer dest;
        if (isFloat) {
            mapSimplifier.EnableKeyword("USE_FLOAT");
            dest = new ComputeBuffer(destNumOfPoints, sizeof(float));
        }
        else { 
            mapSimplifier.DisableKeyword("USE_FLOAT");
            dest = new ComputeBuffer(destNumOfPoints, sizeof(int));
        }
        bufferHandle.Enqueue(dest);

        mapSimplifier.SetInt("destPointsPerAxis", destPointsAxes);
        mapSimplifier.SetInt("destSkipInc", destSkipInc);

        mapSimplifier.SetInt("sourcePointsPerAxis", sourcePointsAxes);
        mapSimplifier.SetInt("sourceSkipInc", sourceSkipInc);

        mapSimplifier.SetBuffer(0, "source", source);
        mapSimplifier.SetBuffer(0, "destination", dest);

        int numThreadsPerAxis = Mathf.CeilToInt(destPointsAxes / (float)threadGroupSize);
        mapSimplifier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return dest;
    }

    public ComputeBuffer AnalyzeNoiseMapGPU(ComputeBuffer checks, ComputeBuffer count, ComputeBuffer args, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, int maxPoints, bool centerNoise, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Append);

        bufferHandle.Enqueue(result);

        checkNoiseSampler.EnableKeyword("SAMPLE_2D");
        checkNoiseSampler.SetBuffer(0, "CheckPoints", checks);
        checkNoiseSampler.SetBuffer(0, "Results", result);
        checkNoiseSampler.SetBuffer(0, "numPoints", count);
        checkNoiseSampler.SetFloat("influenceHeight", maxInfluenceHeight);
        if (centerNoise)
            checkNoiseSampler.EnableKeyword("CENTER_NOISE");
        else
            checkNoiseSampler.DisableKeyword("CENTER_NOISE");


        Generator.SetNoiseData(checkNoiseSampler, chunkSize, 1, noiseData, offset, ref bufferHandle);

        checkNoiseSampler.DispatchIndirect(0, args);

        return result;
    }

    public ComputeBuffer AnalyzeRawNoiseMap(ComputeBuffer points, ComputeBuffer count, ComputeBuffer args, NoiseData noiseData, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);

        bufferHandle.Enqueue(result);

        rawNoiseSampler.SetBuffer(0, "numPoints", count);
        rawNoiseSampler.SetBuffer(0, "structurePoints", points);
        rawNoiseSampler.SetBuffer(0, "Results", result);

        Generator.SetNoiseData(rawNoiseSampler, chunkSize, 1, noiseData, offset, ref bufferHandle);
        rawNoiseSampler.DispatchIndirect(0, args);

        return result;
    }

    public ComputeBuffer AnalyzeBiome(SurfaceChunk.NoiseMaps noiseMaps, ComputeBuffer count, ComputeBuffer args, int maxPoints, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(int), ComputeBufferType.Structured);
        bufferHandle.Enqueue(result);

        biomeMapGenerator.EnableKeyword("INDIRECT");
        biomeMapGenerator.SetBuffer(0, "continental", noiseMaps.continental);
        biomeMapGenerator.SetBuffer(0, "erosion", noiseMaps.erosion);
        biomeMapGenerator.SetBuffer(0, "peaksValleys", noiseMaps.pvNoise);
        biomeMapGenerator.SetBuffer(0, "squash", noiseMaps.squash);
        biomeMapGenerator.SetBuffer(0, "temperature", noiseMaps.temperature);
        biomeMapGenerator.SetBuffer(0, "humidity", noiseMaps.humidity);
        biomeMapGenerator.SetBuffer(0, "numOfPoints", count);

        biomeMapGenerator.SetBuffer(0, "biomeMap", result);

        biomeMapGenerator.DispatchIndirect(0, args);

        return result;

    }
}
