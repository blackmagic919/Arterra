using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;

public static class TerrainGenerator
{
    public static ComputeShader noiseMapGenerator; //
    public static ComputeShader terrainCombinerGPU;//
    public static ComputeShader biomeMapGenerator; // 
    public static ComputeShader surfaceTranscriber;//
    public static ComputeShader mapSimplifier; //
    public static ComputeShader fullNoiseSampler; // 
    public static ComputeShader terrainCombiner;//

    public static ComputeShader rawNoiseSampler;//

    static TerrainGenerator(){
        biomeMapGenerator = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/BiomeGenerator");
        fullNoiseSampler = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/FullNoiseSampler");
        noiseMapGenerator = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/NoiseMapSampler");
        mapSimplifier = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/SimplifyMap");
        terrainCombiner = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/TerrainMapCombiner");
        surfaceTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/TranscribeSurfaceMap");
    }
    //Returns raw noise data
    public static ComputeBuffer GetNoiseMap(NoiseData noiseData, Vector2 offset, float maxInfluenceHeight, int chunkSize, int meshSkipInc, bool centerNoise, Queue<ComputeBuffer> bufferHandle, out ComputeBuffer results)
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

        SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D);
        noiseMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        noiseMapGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return rawPoints;
    }

    public static ComputeBuffer AnalyzeNoiseMap(Vector3[] points, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, bool centerNoise, Queue<ComputeBuffer> bufferHandle)
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
        
        fullNoiseSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);

        SetNoiseData(fullNoiseSampler, chunkSize, 1, noiseData, offset);

        fullNoiseSampler.Dispatch(0, numThreadsPerAxis, 1, 1);

        return result;
    }


    public static ComputeBuffer CombineTerrainMaps(ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int numOfPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle = null)
    {
        ComputeBuffer results = new ComputeBuffer(numOfPoints, sizeof(float));

        bufferHandle?.Enqueue(results);

        terrainCombiner.SetBuffer(0, "continental", contBuffer);
        terrainCombiner.SetBuffer(0, "erosion", erosionBuffer);
        terrainCombiner.SetBuffer(0, "peaksValleys", PVBuffer);
        terrainCombiner.SetBuffer(0, "Result", results);

        terrainCombiner.SetInt("numOfPoints", numOfPoints);
        terrainCombiner.SetFloat("heightOffset", terrainOffset);

        terrainCombiner.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);
        terrainCombiner.Dispatch(0, numThreadsPerAxis, 1, 1);

        return results;
    }

    public static ComputeBuffer GetBiomeMap(int chunkSize, int meshSkipInc, SurfaceChunk.NoiseMaps noiseData, Queue<ComputeBuffer> bufferHandle = null)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer biomes = new ComputeBuffer(numOfPoints, sizeof(int), ComputeBufferType.Structured);

        bufferHandle?.Enqueue(biomes);

        biomeMapGenerator.DisableKeyword("INDIRECT");
        biomeMapGenerator.SetInt("numOfPoints", numOfPoints);
        biomeMapGenerator.SetBuffer(0, "continental", noiseData.continental);
        biomeMapGenerator.SetBuffer(0, "erosion", noiseData.erosion);
        biomeMapGenerator.SetBuffer(0, "peaksValleys", noiseData.pvNoise);
        biomeMapGenerator.SetBuffer(0, "squash", noiseData.squash);
        biomeMapGenerator.SetBuffer(0, "atmosphere", noiseData.atmosphere);
        biomeMapGenerator.SetBuffer(0, "humidity", noiseData.humidity);
        biomeMapGenerator.SetBuffer(0, "biomeMap", biomes);

        biomeMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numOfPoints / (float)threadGroupSize);

        biomeMapGenerator.Dispatch(0, numThreadsPerAxis, 1, 1);

        return biomes;
    }

    public static ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer count, ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
        ComputeBuffer args = UtilityBuffers.CountToArgs(terrainCombinerGPU, count);
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

    public static void TranscribeSurfaceMap(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, ComputeBuffer surfaceMap, int numPoints, bool isFloat){
        if(isFloat)
            surfaceTranscriber.DisableKeyword("PROCESS_INT");
        else
            surfaceTranscriber.EnableKeyword("PROCESS_INT");

        surfaceTranscriber.SetBuffer(0, "SurfaceMap", surfaceMap);
        surfaceTranscriber.SetInt("numSurfacePoints", numPoints);

        surfaceTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        surfaceTranscriber.SetBuffer(0, "_AddressDict", addresses);
        surfaceTranscriber.SetInt("addressIndex", addressIndex);

        surfaceTranscriber.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPoints / (float)threadGroupSize);
        surfaceTranscriber.Dispatch(0, numThreadsPerAxis, 1, 1);
    }

    public static ComputeBuffer SimplifyMap(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex, int chunkSize, int sourceSkipInc, int destSkipInc, bool isFloat, Queue<ComputeBuffer> bufferHandle = null)
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
        bufferHandle?.Enqueue(dest);

        mapSimplifier.SetInt("destPointsPerAxis", destPointsAxes);
        mapSimplifier.SetInt("destSkipInc", destSkipInc);

        mapSimplifier.SetInt("sourcePointsPerAxis", sourcePointsAxes);
        mapSimplifier.SetInt("sourceSkipInc", sourceSkipInc);

        mapSimplifier.SetBuffer(0, "_MemoryBuffer", memory);
        mapSimplifier.SetBuffer(0, "_AddressDict", addresses);
        mapSimplifier.SetInt("addressIndex", addressIndex);
        
        mapSimplifier.SetBuffer(0, "destination", dest);

        mapSimplifier.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(destPointsAxes / (float)threadGroupSize);
        mapSimplifier.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return dest;
    }

}
