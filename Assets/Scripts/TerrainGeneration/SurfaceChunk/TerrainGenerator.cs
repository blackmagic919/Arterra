using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;

public static class TerrainGenerator
{
    static ComputeShader surfaceTranscriber;//
    static ComputeShader mapSimplifier; //
    static ComputeShader surfaceDataSampler;

    static TerrainGenerator(){
        mapSimplifier = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/SimplifyMap");
        surfaceTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/TranscribeSurfaceMap");
        surfaceDataSampler = Resources.Load<ComputeShader>("TerrainGeneration/SurfaceChunk/SurfaceMapSampler");
    }

    //The wonder shader that does everything (This way more parallelization is achieved)
    public static SurfaceChunk.SurfaceMap SampleSurfaceData(Vector2 offset, int chunkSize, int meshSkipInc, int[] samplerData, float[] heights, Queue<ComputeBuffer> bufferHandle){
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;
        Vector3 offset3D = new Vector3(offset.x, 0, offset.y);

        ComputeBuffer heightMap = new ComputeBuffer(numOfPoints, sizeof(float), ComputeBufferType.Structured);
        ComputeBuffer biomeMap = new ComputeBuffer(numOfPoints, sizeof(int), ComputeBufferType.Structured);
        ComputeBuffer squashMap = new ComputeBuffer(numOfPoints, sizeof(float), ComputeBufferType.Structured);
        ComputeBuffer atmosphereMap = new ComputeBuffer(numOfPoints, sizeof(float), ComputeBufferType.Structured);
        
        bufferHandle.Enqueue(heightMap);
        bufferHandle.Enqueue(biomeMap);
        bufferHandle.Enqueue(squashMap);
        bufferHandle.Enqueue(atmosphereMap);

        surfaceDataSampler.SetBuffer(0, "heightMap", heightMap);
        surfaceDataSampler.SetBuffer(0, "biomeMap", biomeMap);
        surfaceDataSampler.SetBuffer(0, "squashMap", squashMap);
        surfaceDataSampler.SetBuffer(0, "atmosphereMap", atmosphereMap);
        surfaceDataSampler.SetInt("numPointsPerAxis", numPointsAxes);
        surfaceDataSampler.SetInt("continentalSampler", samplerData[0]);
        surfaceDataSampler.SetInt("erosionSampler", samplerData[1]);
        surfaceDataSampler.SetInt("PVSampler", samplerData[2]);
        surfaceDataSampler.SetInt("squashSampler", samplerData[3]);
        surfaceDataSampler.SetInt("atmosphereSampler", samplerData[4]);
        surfaceDataSampler.SetInt("humiditySampler", samplerData[5]);

        surfaceDataSampler.SetFloat("continentalHeight", heights[0]);
        surfaceDataSampler.SetFloat("PVHeight", heights[1]);
        surfaceDataSampler.SetFloat("squashHeight", heights[2]);
        surfaceDataSampler.SetFloat("heightOffset", heights[3]);
        SetSampleData(surfaceDataSampler, offset3D, chunkSize, meshSkipInc);

        surfaceDataSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        surfaceDataSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        SurfaceChunk.SurfaceMap mapData = new SurfaceChunk.SurfaceMap(heightMap, squashMap, biomeMap, atmosphereMap, handle: false);
        return mapData;
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

    /*
    //Returns raw noise data
    public static ComputeBuffer GetNoiseMap(NoiseData noiseData, Vector2 offset, float maxInfluenceHeight, int chunkSize, int meshSkipInc, Queue<ComputeBuffer> bufferHandle, out ComputeBuffer results)
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

        SetNoiseData(noiseMapGenerator, chunkSize, meshSkipInc, noiseData, offset3D);
        noiseMapGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        noiseMapGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);

        return rawPoints;
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
    */

}