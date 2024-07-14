using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;
using Unity.Mathematics;

public static class StructureGenerator 
{
    static ComputeShader StructureLoDSampler;//
    static ComputeShader StructureIdentifier;//
    static ComputeShader structureChunkGenerator;//
    static ComputeShader structureDataTranscriber;//
    static ComputeShader structureSizeCounter;//

    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;
    const int SAMPLE_STRIDE_WORD = 3 + 1;
    const int CHECK_STRIDE_WORD = 2;

    public static StructureOffsets offsets;
    
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


    public static void PresetData()
    {
        MeshCreatorSettings mesh = WorldStorageHandler.WORLD_OPTIONS.Terrain.value;
        SurfaceCreatorSettings surface = WorldStorageHandler.WORLD_OPTIONS.Surface.value;
        BiomeGenerationData biomes = WorldStorageHandler.WORLD_OPTIONS.Biomes.value;
        int maxStructurePoints = calculateMaxStructurePoints(biomes.maxLoD, biomes.StructureChecksPerChunk, biomes.LoDFalloff);
        offsets = new StructureOffsets(maxStructurePoints, 0);

        StructureLoDSampler.SetInt("maxLOD", biomes.maxLoD);
        StructureLoDSampler.SetInt("numPoints0", biomes.StructureChecksPerChunk);
        StructureLoDSampler.SetFloat("LoDFalloff", biomes.LoDFalloff);
        StructureLoDSampler.SetBuffer(0, "structures", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureLoDSampler.SetInt("bSTART", offsets.sampleStart);
        StructureLoDSampler.SetInt("bCOUNTER", offsets.sampleCounter);

        StructureIdentifier.SetInt("caveCoarseSampler", mesh.CoarseTerrainNoise);
        StructureIdentifier.SetInt("caveFineSampler", mesh.FineTerrainNoise);

        StructureIdentifier.SetInt("continentalSampler", surface.TerrainContinentalDetail);
        StructureIdentifier.SetInt("erosionSampler", surface.TerrainErosionDetail);
        StructureIdentifier.SetInt("PVSampler", surface.TerrainPVDetail);
        StructureIdentifier.SetInt("squashSampler", surface.SquashMapDetail);
        StructureIdentifier.SetInt("caveFreqSampler", surface.CaveFreqDetail);
        StructureIdentifier.SetInt("caveSizeSampler", surface.CaveSizeDetail);
        StructureIdentifier.SetInt("caveShapeSampler", surface.CaveShapeDetail);

        StructureIdentifier.SetFloat("maxTerrainHeight", surface.MaxTerrainHeight);
        StructureIdentifier.SetFloat("squashHeight", surface.MaxSquashHeight);
        StructureIdentifier.SetFloat("heightOffset", surface.terrainOffset);
        StructureIdentifier.SetFloat("waterHeight", mesh.waterHeight);

        StructureIdentifier.SetBuffer(0, "structurePlan", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(0, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(0, "structureChecks", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "structureChecks", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "structurePlan", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(2, "genStructures", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bSTART_plan", offsets.sampleStart);
        StructureIdentifier.SetInt("bSTART_check", offsets.checkStart);
        StructureIdentifier.SetInt("bSTART_struct", offsets.structureStart);
        StructureIdentifier.SetInt("bSTART_prune", offsets.prunedStart);

        StructureIdentifier.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(1, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetBuffer(2, "counter", UtilityBuffers.GenerationBuffer);
        StructureIdentifier.SetInt("bCOUNTER_plan", offsets.sampleCounter);
        StructureIdentifier.SetInt("bCOUNTER_check", offsets.checkCounter);
        StructureIdentifier.SetInt("bCOUNTER_struct", offsets.structureCounter);
        StructureIdentifier.SetInt("bCOUNTER_prune", offsets.prunedCounter);

        structureDataTranscriber.SetBuffer(0, "counter", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetBuffer(0, "structPoints", UtilityBuffers.GenerationBuffer);
        structureDataTranscriber.SetInt("bSTART_struct", offsets.prunedStart);
        structureDataTranscriber.SetInt("bCOUNTER_struct", offsets.prunedCounter);
    }
    
    public static void SampleStructureLoD(int maxLoD, int chunkSize, int3 chunkCoord)
    {   
        int numChunksPerAxis = maxLoD + 2;
        int numChunksMax = numChunksPerAxis * numChunksPerAxis * numChunksPerAxis;

        StructureLoDSampler.SetInts("originChunkCoord", new int[] { chunkCoord.x, chunkCoord.y, chunkCoord.z });
        StructureLoDSampler.SetInt("chunkSize", chunkSize);

        StructureLoDSampler.GetKernelThreadGroupSizes(0, out uint threadChunkSize, out uint threadLoDSize, out _);
        int numThreadsChunk = Mathf.CeilToInt(numChunksMax / (float)threadChunkSize);
        int numThreadsLoD = Mathf.CeilToInt(maxLoD / (float)threadLoDSize);
        StructureLoDSampler.Dispatch(0, numThreadsChunk, numThreadsLoD, 1);
    }
    
    public static void IdentifyStructures(int3 chunkCoord, Vector3 offset, float IsoLevel, int chunkSize)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.sampleCounter);

        StructureIdentifier.SetFloat("IsoLevel", IsoLevel);
        StructureIdentifier.SetInts("CCoord", new int[] { chunkCoord.x, chunkCoord.y, chunkCoord.z });
        SetSampleData(StructureIdentifier, offset, chunkSize, 1);

        int kernel = StructureIdentifier.FindKernel("Identify");
        StructureIdentifier.DispatchIndirect(kernel, args);//byte offset

        args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.checkCounter);
        kernel = StructureIdentifier.FindKernel("Check");
        StructureIdentifier.DispatchIndirect(kernel, args);//byte offset

        args = UtilityBuffers.CountToArgs(StructureIdentifier, UtilityBuffers.GenerationBuffer, offsets.structureCounter);
        kernel = StructureIdentifier.FindKernel("Prune");
        StructureIdentifier.DispatchIndirect(kernel, args);
    }


    public static uint TranscribeStructures(ComputeBuffer memory, ComputeBuffer addresses)
    {
        uint addressIndex = GenerationPreset.memoryHandle.AllocateMemory(UtilityBuffers.GenerationBuffer, STRUCTURE_STRIDE_WORD, offsets.prunedCounter);
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureDataTranscriber, UtilityBuffers.GenerationBuffer, offsets.structureCounter);

        structureDataTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        structureDataTranscriber.SetBuffer(0, "_AddressDict", addresses);
        structureDataTranscriber.SetInt("addressIndex", (int)addressIndex);

        structureDataTranscriber.DispatchIndirect(0, args);
        return addressIndex;
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

    public struct StructureOffsets : BufferOffsets{
        public int sampleCounter;
        public int structureCounter;
        public int checkCounter;
        public int prunedCounter;
        public int prunedStart;
        public int sampleStart;
        public int structureStart;
        public int checkStart;
        private int offsetStart; private int offsetEnd;
        public int bufferStart{get{return offsetStart;}} public int bufferEnd{get{return offsetEnd;}}

        public StructureOffsets(int maxStructurePoints, int bufferStart){
            this.offsetStart = bufferStart;
            sampleCounter = bufferStart; structureCounter = bufferStart + 1;
            checkCounter = bufferStart + 2; prunedCounter = bufferStart + 3;

            sampleStart = Mathf.CeilToInt((float)(bufferStart + 4) / SAMPLE_STRIDE_WORD); //U for unit, W for word
            int SampleEndInd_W = sampleStart * SAMPLE_STRIDE_WORD + maxStructurePoints * SAMPLE_STRIDE_WORD;
            
            structureStart = Mathf.CeilToInt((float)SampleEndInd_W / STRUCTURE_STRIDE_WORD);
            int StructureEndInd_W = structureStart * STRUCTURE_STRIDE_WORD + maxStructurePoints * STRUCTURE_STRIDE_WORD;

            prunedStart = Mathf.CeilToInt((float)StructureEndInd_W / STRUCTURE_STRIDE_WORD);
            int PrunedEndInd_W = prunedStart * STRUCTURE_STRIDE_WORD + maxStructurePoints * STRUCTURE_STRIDE_WORD;

            checkStart = Mathf.CeilToInt((float)PrunedEndInd_W / CHECK_STRIDE_WORD);
            int CheckEndInd_W = checkStart * CHECK_STRIDE_WORD + (maxStructurePoints * 5) * CHECK_STRIDE_WORD;

            this.offsetEnd = CheckEndInd_W;
        }
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