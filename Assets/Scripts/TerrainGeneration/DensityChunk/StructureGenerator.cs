using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;

public static class StructureGenerator 
{
    static ComputeShader StructureLoDSampler;//
    static ComputeShader StructureIdentifier;//
    static ComputeShader StructureChecks;//
    static ComputeShader terrainAnalyzerGPU;//
    static ComputeShader structureCheckFilter;//
    static ComputeShader structureChunkGenerator;//
    static ComputeShader structureDataTranscriber;//
    static ComputeShader structureMemorySize;//
    static ComputeShader structureSizeCounter;//

    //Surface Shaders
    static ComputeShader biomeMapGenerator; // 
    
    static StructureGenerator(){
        StructureLoDSampler = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureLODSampler");
        StructureIdentifier = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureIdentifier");
        StructureChecks = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureCheckPoint");
        terrainAnalyzerGPU = Resources.Load<ComputeShader>("TerrainGeneration/Structures/TerrainAnalyzerGPU");
        structureCheckFilter = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureCheckFilter");
        structureChunkGenerator = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureChunkGenerator");
        structureDataTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Structures/TranscribeStructPoints");
        structureMemorySize = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureMemorySize");
        structureSizeCounter = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureSizeCounter");

        biomeMapGenerator = Resources.Load<ComputeShader>("TerrainGeneration/Structures/BiomeGenerator");
    }
    
    public static ComputeBuffer SampleStructureLoD(int maxLoD, int chunkSize, float LoDFalloff, int structurePoints0, int maxStructurePoints, Vector3 chunkCoord, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numChunksPerAxis = maxLoD + 2;

        ComputeBuffer structurePoints = new ComputeBuffer(maxStructurePoints, sizeof(float) * 3 + sizeof(uint), ComputeBufferType.Append);
        structurePoints.SetCounterValue(0);
        bufferHandle.Enqueue(structurePoints);

        StructureLoDSampler.SetBuffer(0, "structures", structurePoints);
        StructureLoDSampler.SetInt("maxLOD", maxLoD);
        StructureLoDSampler.SetInts("originChunkCoord", new int[] { (int)chunkCoord.x, (int)chunkCoord.y, (int)chunkCoord.z });
        StructureLoDSampler.SetInt("chunkSize", chunkSize);
        StructureLoDSampler.SetInt("numPoints0", structurePoints0);

        StructureLoDSampler.SetFloat("LoDFalloff", LoDFalloff);

        StructureLoDSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numChunksPerAxis / (float)threadGroupSize);
        StructureLoDSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);

        return structurePoints;
    }

    public static ComputeBuffer IdentifyStructures(ComputeBuffer structurePoints, ComputeBuffer args, ComputeBuffer count, ComputeBuffer biomes, Vector3 chunkCoord, int chunkSize, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 5, ComputeBufferType.Append);
        bufferHandle.Enqueue(results);

        StructureIdentifier.SetBuffer(0, "biome", biomes);
        StructureIdentifier.SetBuffer(0, "structurePoints", structurePoints);
        StructureIdentifier.SetBuffer(0, "numPoints", count);
        StructureIdentifier.SetInts("originChunkCoord", new int[] { (int)chunkCoord.x, (int)chunkCoord.y, (int)chunkCoord.z });
        StructureIdentifier.SetInt("chunkSize", chunkSize);

        StructureIdentifier.SetBuffer(0, "structurePlans", results);

        StructureIdentifier.DispatchIndirect(0, args);

        return results;
    }

    public static ComputeBuffer CreateChecks(ComputeBuffer structures, ComputeBuffer args, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(uint) * 2 + sizeof(float) * 3, ComputeBufferType.Append);
        bufferHandle.Enqueue(results);

        StructureChecks.SetBuffer(0, "structures", structures);
        StructureChecks.SetBuffer(0, "numPoints", count);
        StructureChecks.SetBuffer(0, "checks", results);

        StructureChecks.DispatchIndirect(0, args);

        return results;
    }
    public static ComputeBuffer FilterStructures(ComputeBuffer structures, ComputeBuffer args, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 3, ComputeBufferType.Append);
        result.SetCounterValue(0);
        bufferHandle.Enqueue(result);

        structureCheckFilter.SetBuffer(0, "numPoints", count);
        structureCheckFilter.SetBuffer(0, "structureInfos", structures);
        structureCheckFilter.SetBuffer(0, "validStructures", result);

        structureCheckFilter.DispatchIndirect(0, args);

        return result;
    }

    public static ComputeBuffer CalculateStructureSize(ComputeBuffer structureCount, int structureStride, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(result);

        structureMemorySize.SetBuffer(0, "structureCount", structureCount);
        structureMemorySize.SetInt("structStride4Byte", structureStride);
        structureMemorySize.SetBuffer(0, "byteLength", result);

        structureMemorySize.Dispatch(0, 1, 1, 1);

        return result;
    }

    public static void TranscribeStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer structures, ComputeBuffer count, int addressIndex, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureDataTranscriber, count);

        structureDataTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        structureDataTranscriber.SetBuffer(0, "_AddressDict", addresses);
        structureDataTranscriber.SetInt("addressIndex", addressIndex);

        structureDataTranscriber.SetBuffer(0, "structPoints", structures);
        structureDataTranscriber.SetBuffer(0, "numStructPoints", count);

        structureDataTranscriber.DispatchIndirect(0, args);
    }

    public static ComputeBuffer AnalyzeBiome(ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, int[] samplers, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(int), ComputeBufferType.Structured);
        bufferHandle.Enqueue(result);

        biomeMapGenerator.SetBuffer(0, "structOrigins", structs);
        biomeMapGenerator.SetBuffer(0, "numPoints", count);
        biomeMapGenerator.SetBuffer(0, "biomeMap", result);
        biomeMapGenerator.SetInt("continentalSampler", samplers[0]);
        biomeMapGenerator.SetInt("erosionSampler", samplers[1]);
        biomeMapGenerator.SetInt("PVSampler", samplers[2]);
        biomeMapGenerator.SetInt("squashSampler", samplers[3]);
        biomeMapGenerator.SetInt("atmosphereSampler", samplers[4]);
        biomeMapGenerator.SetInt("humiditySampler", samplers[5]);
        SetSampleData(biomeMapGenerator, offset, chunkSize, 1);

        biomeMapGenerator.DispatchIndirect(0, args);

        return result;
    }

    public static void PresetSampleShader(ComputeShader sampler, NoiseData noiseData, float maxInfluenceHeight, bool sample2D, bool interp, bool centerNoise){
        sampler.SetFloat("influenceHeight", maxInfluenceHeight);

        if(sample2D)
            sampler.EnableKeyword("SAMPLE_2D");
        else
            sampler.DisableKeyword("SAMPLE_2D");

        if(interp)
            sampler.EnableKeyword("INTERP");
        else
            sampler.DisableKeyword("INTERP");
        
        if (centerNoise)
            sampler.EnableKeyword("CENTER_NOISE");
        else
            sampler.DisableKeyword("CENTER_NOISE");
        
        PresetNoiseData(sampler, noiseData);
    }

    public static void AnalyzeTerrain(ComputeBuffer checks, ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, int[] samplers, float[] heights, Vector3 offset, int chunkSize, float IsoLevel)
    {
        float chunkYOrigin = offset.y - (chunkSize/2);

        terrainAnalyzerGPU.SetBuffer(0, "numPoints", count);
        terrainAnalyzerGPU.SetBuffer(0, "checks", checks);
        terrainAnalyzerGPU.SetBuffer(0, "structs", structs);//output

        terrainAnalyzerGPU.SetFloat("chunkYOrigin", chunkYOrigin);
        terrainAnalyzerGPU.SetFloat("IsoLevel", IsoLevel);

        terrainAnalyzerGPU.SetInt("caveCoarseSampler", samplers[0]);
        terrainAnalyzerGPU.SetInt("caveFineSampler", samplers[1]);
        terrainAnalyzerGPU.SetInt("continentalSampler", samplers[2]);
        terrainAnalyzerGPU.SetInt("erosionSampler", samplers[3]);
        terrainAnalyzerGPU.SetInt("PVSampler", samplers[4]);
        terrainAnalyzerGPU.SetInt("squashSampler", samplers[5]);

        terrainAnalyzerGPU.SetFloat("continentalHeight", heights[0]);
        terrainAnalyzerGPU.SetFloat("PVHeight", heights[1]);
        terrainAnalyzerGPU.SetFloat("squashHeight", heights[2]);
        terrainAnalyzerGPU.SetFloat("heightOffset", heights[3]);
        SetSampleData(terrainAnalyzerGPU, offset, chunkSize, 1);

        terrainAnalyzerGPU.DispatchIndirect(0, args);
    }

    public static ComputeBuffer GetStructCount(ComputeBuffer memory, ComputeBuffer address, int addressIndex, int STRUCTURE_STRIDE_4BYTE, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer structCount = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(structCount);

        structureSizeCounter.SetBuffer(0, "_MemoryBuffer", memory);
        structureSizeCounter.SetBuffer(0, "_AddressDict", address);
        structureSizeCounter.SetInt("addressIndex", addressIndex);
        structureSizeCounter.SetInt("STRUCTURE_STRIDE_4BYTE", STRUCTURE_STRIDE_4BYTE);

        structureSizeCounter.SetBuffer(0, "structCount", structCount);
        structureSizeCounter.Dispatch(0, 1, 1, 1);

        return structCount;
    }
    
    public static void ApplyStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer count, ComputeBuffer density, ComputeBuffer material, int addressIndex, int chunkSize, int meshSkipInc, float IsoLevel, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureChunkGenerator, count);

        structureChunkGenerator.SetBuffer(0, "_MemoryBuffer", memory);
        structureChunkGenerator.SetBuffer(0, "_AddressDict", addresses);
        structureChunkGenerator.SetInt("addressIndex", addressIndex);

        structureChunkGenerator.SetBuffer(0, "numPoints", count);

        structureChunkGenerator.SetBuffer(0, "density", density);
        structureChunkGenerator.SetBuffer(0, "material", material);
        structureChunkGenerator.SetInt("chunkSize", chunkSize);
        structureChunkGenerator.SetInt("meshSkipInc", meshSkipInc);
        structureChunkGenerator.SetFloat("IsoLevel", IsoLevel);

        structureChunkGenerator.DispatchIndirect(0, args);
    }

    /*
    public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeBuffer checks, ComputeBuffer count, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, int maxPoints, bool sample2D, bool interp, bool centerNoise, Queue<ComputeBuffer> bufferHandle){
        ComputeBuffer args = UtilityBuffers.CountToArgs(checkNoiseSampler, count);
        return AnalyzeNoiseMapGPU(checks, args, count, noiseData, offset, maxInfluenceHeight, chunkSize, maxPoints, sample2D, interp, centerNoise, bufferHandle);
    }
    public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, NoiseData noiseData, Vector3 offset, float maxInfluenceHeight, int chunkSize, int maxPoints, bool sample2D, bool interp, bool centerNoise, Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Append);
        bufferHandle.Enqueue(result);

        checkNoiseSampler.SetBuffer(0, "CheckPoints", checks);
        checkNoiseSampler.SetBuffer(0, "Results", result);
        checkNoiseSampler.SetBuffer(0, "numPoints", count);
        checkNoiseSampler.SetFloat("influenceHeight", maxInfluenceHeight);

        if(sample2D)
            checkNoiseSampler.EnableKeyword("SAMPLE_2D");
        else
            checkNoiseSampler.DisableKeyword("SAMPLE_2D");

        if(interp)
            checkNoiseSampler.EnableKeyword("INTERP");
        else
            checkNoiseSampler.DisableKeyword("INTERP");
        
        if (centerNoise)
            checkNoiseSampler.EnableKeyword("CENTER_NOISE");
        else
            checkNoiseSampler.DisableKeyword("CENTER_NOISE");


        SetNoiseData(checkNoiseSampler, chunkSize, 1, noiseData, offset);

        checkNoiseSampler.DispatchIndirect(0, args);

        return result;
    }

    public static void AnalyzeChecks(ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, ComputeBuffer density, float IsoValue, ref ComputeBuffer valid, ref Queue<ComputeBuffer> bufferHandle)
    {
        checkVerification.SetBuffer(0, "numPoints", count);
        checkVerification.SetBuffer(0, "checks", checks);
        checkVerification.SetBuffer(0, "density", density);
        checkVerification.SetFloat("IsoValue", IsoValue);

        checkVerification.SetBuffer(0, "validity", valid);

        checkVerification.DispatchIndirect(0, args);
    }

    public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeShader sampler, ComputeBuffer checks, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle){
        ComputeBuffer args = UtilityBuffers.CountToArgs(sampler, count);
        return AnalyzeNoiseMapGPU(sampler, checks, args, count, offset, chunkSize, maxPoints, bufferHandle);
    }
    public static ComputeBuffer AnalyzeNoiseMapGPU(ComputeShader sampler, ComputeBuffer checks, ComputeBuffer args, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints, Queue<ComputeBuffer> bufferHandle){
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Append);
        bufferHandle.Enqueue(result);

        sampler.SetBuffer(0, "CheckPoints", checks);
        sampler.SetBuffer(0, "Results", result);
        sampler.SetBuffer(0, "numPoints", count);

        SetSampleData(sampler, offset, chunkSize, 1);
        sampler.DispatchIndirect(0, args);
        return result;
    }

    public static ComputeBuffer CombineTerrainMapsGPU(ComputeBuffer args, ComputeBuffer count, ComputeBuffer contBuffer, ComputeBuffer erosionBuffer, ComputeBuffer PVBuffer, int maxPoints, float terrainOffset, Queue<ComputeBuffer> bufferHandle)
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


    public static ComputeBuffer InitializeIndirect<T>(ComputeBuffer args, ComputeBuffer count, T val, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer map;
        indirectMapInitialize.DisableKeyword("USE_BOOL");
        indirectMapInitialize.DisableKeyword("USE_INT");

        //Size of int and float are technically the same, but it's more unreadable
        if (val.GetType() == typeof(int))
        {
            indirectMapInitialize.EnableKeyword("USE_INT");
            indirectMapInitialize.SetInt("value", (int)(object)val);
            map = new ComputeBuffer(maxPoints, sizeof(int), ComputeBufferType.Structured);
        }
        else if (val.GetType() == typeof(bool))
        {
            indirectMapInitialize.EnableKeyword("USE_BOOL");
            indirectMapInitialize.SetBool("value", (bool)(object)val);
            map = new ComputeBuffer(maxPoints, sizeof(bool), ComputeBufferType.Structured);
        }
        else { 
            indirectMapInitialize.SetFloat("value", (float)(object)val);
            map = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
        }

        bufferHandle.Enqueue(map);
        indirectMapInitialize.SetBuffer(0, "numPoints", count);
        indirectMapInitialize.SetBuffer(0, "map", map);

        indirectMapInitialize.DispatchIndirect(0, args);

        return map;
    }
    */
}