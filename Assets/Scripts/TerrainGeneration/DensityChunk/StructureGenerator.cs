using System.Collections;
using System.Collections.Generic;
using UnityEditor.Playables;
using UnityEngine;
using static UtilityBuffers;

public static class StructureGenerator 
{
    static ComputeShader StructureLoDSampler;//
    static ComputeShader StructureIdentifier;//
    static ComputeShader structureChunkGenerator;//
    static ComputeShader structureDataTranscriber;//
    static ComputeShader structureSizeCounter;//

    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;
    const int SAMPLE_STRIDE_WORD = 3 + 1;

    static int SAMPLE_STARTU;
    static int STRUCTURE_STARTU;
    const int SAMPLE_COUNTER = 0;
    const int STRUCTURE_COUNTER = 1;
    
    static StructureGenerator(){
        StructureLoDSampler = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureLODSampler");
        StructureIdentifier = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureIdentifier");
        structureChunkGenerator = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureChunkGenerator");
        structureDataTranscriber = Resources.Load<ComputeShader>("TerrainGeneration/Structures/TranscribeStructPoints");
        structureSizeCounter = Resources.Load<ComputeShader>("TerrainGeneration/Structures/StructureSizeCounter");
    }

    static int[] calculateLoDPoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int[] points = new int[maxLoD + 2]; //suffix sum
        for(int LoD = maxLoD; LoD >= 0; LoD--)
        {
            points[LoD] = Mathf.CeilToInt(maxStructurePoints * Mathf.Pow(falloffFactor, -LoD)) + points[LoD+1];
        }
        return points;
    }

    static int calculateMaxStructurePoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int totalPoints = 0;
        int processedChunks = 0;
        int maxDist = maxLoD + 2;
        int[] pointsPerLoD = calculateLoDPoints(maxLoD, maxStructurePoints, falloffFactor);

        for (int dist = 1; dist <= maxDist; dist++)
        {
            int numChunks = dist * dist * dist - processedChunks;
            int LoD = Mathf.Max(0, dist - 2);
            int maxPointsPerChunk = pointsPerLoD[LoD];

            totalPoints += maxPointsPerChunk * numChunks;
            processedChunks += numChunks;
        }
        return totalPoints;
    }


    public static void PresetData(MeshCreatorSettings meshSettings, SurfaceCreatorSettings surfSettings)
    {
        int maxStructurePoints = calculateMaxStructurePoints(meshSettings.biomeData.maxLoD, meshSettings.biomeData.StructureChecksPerChunk, meshSettings.biomeData.LoDFalloff);
        
        int MetaLength = 2;
        SAMPLE_STARTU = Mathf.CeilToInt((float)MetaLength / SAMPLE_STRIDE_WORD); //U for unit, W for word
        int SampleEndInd_W = SAMPLE_STARTU * SAMPLE_STRIDE_WORD + maxStructurePoints * SAMPLE_STRIDE_WORD;
        
        int StructStartInd_U = Mathf.CeilToInt((float)SampleEndInd_W / STRUCTURE_STRIDE_WORD);
        STRUCTURE_STARTU = StructStartInd_U * STRUCTURE_STRIDE_WORD + maxStructurePoints * STRUCTURE_STRIDE_WORD;

        StructureLoDSampler.SetInt("maxLOD", meshSettings.biomeData.maxLoD);
        StructureLoDSampler.SetInt("numPoints0", meshSettings.biomeData.StructureChecksPerChunk);
        StructureLoDSampler.SetFloat("LoDFalloff", meshSettings.biomeData.LoDFalloff);
        StructureLoDSampler.SetBuffer(0, "structures", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetInt("bSTART", SAMPLE_STARTU);
        StructureLoDSampler.SetInt("bCOUNTER", SAMPLE_COUNTER);

        StructureIdentifier.SetInt("caveCoarseSampler", meshSettings.CoarseTerrainNoise);
        StructureIdentifier.SetInt("caveFineSampler", meshSettings.FineTerrainNoise);
        StructureIdentifier.SetInt("continentalSampler", surfSettings.TerrainContinentalDetail);
        StructureIdentifier.SetInt("erosionSampler", surfSettings.TerrainErosionDetail);
        StructureIdentifier.SetInt("PVSampler", surfSettings.TerrainPVDetail);
        StructureIdentifier.SetInt("squashSampler", surfSettings.SquashMapDetail);
        StructureIdentifier.SetInt("atmosphereSampler", surfSettings.AtmosphereDetail);
        StructureIdentifier.SetInt("humiditySampler", surfSettings.HumidityDetail);

        StructureIdentifier.SetFloat("continentalHeight", surfSettings.MaxContinentalHeight);
        StructureIdentifier.SetFloat("PVHeight", surfSettings.MaxPVHeight);
        StructureIdentifier.SetFloat("squashHeight", surfSettings.MaxSquashHeight);
        StructureIdentifier.SetFloat("heightOffset", surfSettings.terrainOffset);

        StructureIdentifier.SetBuffer(0, "structurePlan", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bSTART_plan", SAMPLE_STARTU);

        StructureIdentifier.SetBuffer(0, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bSTART_out", STRUCTURE_STARTU);

        StructureIdentifier.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bCOUNTER_plan", SAMPLE_COUNTER);
        StructureIdentifier.SetInt("bCOUNTER_out", STRUCTURE_COUNTER);

        structureDataTranscriber.SetBuffer(0, "structPoints", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetInt("bSTART", STRUCTURE_STARTU);
        structureDataTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetInt("bCOUNTER", STRUCTURE_COUNTER);
    }
    
    public static void SampleStructureLoD(int maxLoD, int chunkSize, Vector3 chunkCoord)
    {
        int numChunksPerAxis = maxLoD + 2;

        StructureLoDSampler.SetInts("originChunkCoord", new int[] { (int)chunkCoord.x, (int)chunkCoord.y, (int)chunkCoord.z });
        StructureLoDSampler.SetInt("chunkSize", chunkSize);

        StructureLoDSampler.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numChunksPerAxis / (float)threadGroupSize);
        StructureLoDSampler.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }
    
    public static void IdentifyStructures(Vector3 chunkCoord, Vector3 offset, float IsoLevel, int chunkSize)
    {
        //ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 3, ComputeBufferType.Append);
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, SAMPLE_COUNTER);

        StructureIdentifier.SetFloat("IsoLevel", IsoLevel);
        StructureIdentifier.SetInts("CCoord", new int[] { (int)chunkCoord.x, (int)chunkCoord.y, (int)chunkCoord.z });
        SetSampleData(StructureIdentifier, offset, chunkSize, 1);
        

        StructureIdentifier.DispatchIndirect(0, args);//byte offset
    }

    public static void TranscribeStructures(ComputeBuffer memory, ComputeBuffer addresses, int addressIndex)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureDataTranscriber, UtilityBuffers.GenerationBuffer, STRUCTURE_COUNTER);

        structureDataTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        structureDataTranscriber.SetBuffer(0, "_AddressDict", addresses);
        structureDataTranscriber.SetInt("addressIndex", addressIndex);

        structureDataTranscriber.DispatchIndirect(0, args);
    }

    public static ComputeBuffer GetStructCount(ComputeBuffer memory, ComputeBuffer address, int addressIndex, int STRUCTURE_STRIDE_4BYTE)
    {
        ComputeBuffer structCount = UtilityBuffers.appendCount;

        structureSizeCounter.SetBuffer(0, "_MemoryBuffer", memory);
        structureSizeCounter.SetBuffer(0, "_AddressDict", address);
        structureSizeCounter.SetInt("addressIndex", addressIndex);
        structureSizeCounter.SetInt("STRUCTURE_STRIDE_4BYTE", STRUCTURE_STRIDE_4BYTE);

        structureSizeCounter.SetBuffer(0, "structCount", structCount);
        structureSizeCounter.Dispatch(0, 1, 1, 1);

        return structCount;
    }
    
    public static void ApplyStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer count, int addressIndex, int chunkSize, int meshSkipInc, float IsoLevel)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureChunkGenerator, count);

        structureChunkGenerator.SetBuffer(0, "_MemoryBuffer", memory);
        structureChunkGenerator.SetBuffer(0, "_AddressDict", addresses);
        structureChunkGenerator.SetInt("addressIndex", addressIndex);

        structureChunkGenerator.SetBuffer(0, "numPoints", count);

        structureChunkGenerator.SetBuffer(0, "chunkData", UtilityBuffers.GenerationBuffer);
        structureChunkGenerator.SetInt("chunkSize", chunkSize);
        structureChunkGenerator.SetInt("meshSkipInc", meshSkipInc);
        structureChunkGenerator.SetFloat("IsoLevel", IsoLevel);

        structureChunkGenerator.DispatchIndirect(0, args);
    }

    /*
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

    public static void AnalyzeTerrain(ComputeBuffer checks, ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, int[] samplers, float[] heights, Vector3 offset, int chunkSize, float IsoLevel)
    {
        terrainAnalyzerGPU.SetBuffer(0, "numPoints", count);
        terrainAnalyzerGPU.SetBuffer(0, "checks", checks);
        terrainAnalyzerGPU.SetBuffer(0, "structs", structs);//output
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