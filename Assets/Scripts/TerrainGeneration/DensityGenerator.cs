using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static UtilityBuffers;

[CreateAssetMenu(menuName = "Containers/DensityGenerator")]
public class DensityGenerator : ScriptableObject
{
    [Header("Terrain Generation Shaders")]
    public ComputeShader terrainNoiseCompute;
    public ComputeShader undergroundNoiseCompute;
    public ComputeShader materialGenCompute;
    public ComputeShader meshGenerator;
    public ComputeShader densitySimplification;
    public ComputeShader TriCountToVertCount;
    
    [Space(10)]
    [Header("Structure Shaders")]
    public ComputeShader StructureLoDSampler;
    public ComputeShader StructureIdentifier;
    public ComputeShader StructureChecks;

    public ComputeShader checkNoiseSampler;
    public ComputeShader terrainAnalyzerGPU;
    public ComputeShader checkVerification;
    public ComputeShader structureCheckFilter;
    public ComputeShader structureChunkGenerator;
    public ComputeShader structureDataTranscriber;
    public ComputeShader structureMemorySize;
    public ComputeShader structureSizeCounter;

    public ComputeShader indirectThreads;
    public ComputeShader indirectCountToArgs;
    public ComputeShader indirectMapInitialize;

    public void ConvertTriCountToVert(ComputeBuffer args)
    {
        TriCountToVertCount.SetBuffer(0, "_IndirectArgsBuffer", args);
        TriCountToVertCount.Dispatch(0, 1, 1, 1);
    }

    public void SimplifyMaterials(int chunkSize, int meshSkipInc, int[] materials, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeMaterial = new ComputeBuffer(totalPoints, sizeof(int), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeMaterial.SetData(materials);
        bufferHandle.Enqueue(completeMaterial);

        densitySimplification.EnableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeMaterial);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void SimplifyDensity(int chunkSize, int meshSkipInc, float[] density, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int totalPointsAxes = chunkSize + 1;
        int totalPoints = totalPointsAxes * totalPointsAxes * totalPointsAxes;
        ComputeBuffer completeDensity = new ComputeBuffer(totalPoints, sizeof(float), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
        completeDensity.SetData(density);
        bufferHandle.Enqueue(completeDensity);

        densitySimplification.DisableKeyword("USE_INT");
        densitySimplification.SetInt("meshSkipInc", meshSkipInc);
        densitySimplification.SetInt("totalPointsPerAxis", totalPointsAxes);
        densitySimplification.SetInt("pointsPerAxis", numPointsAxes);
        densitySimplification.SetBuffer(0, "points_full", completeDensity);
        densitySimplification.SetBuffer(0, "points", pointBuffer);

        densitySimplification.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        densitySimplification.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

       
    public ComputeBuffer GenerateMat(NoiseData coarseNoise, NoiseData fineNoise, ComputeBuffer biomeBuffer, int chunkSize, int meshSkipInc, Vector3 offset, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        ComputeBuffer materialBuffer = new ComputeBuffer(numPoints, sizeof(int));

        ComputeBuffer coarseMatDetail = new ComputeBuffer(numPoints, sizeof(float));
        GenerateUnderground(chunkSize, meshSkipInc, coarseNoise, offset, coarseMatDetail, ref bufferHandle);
        ComputeBuffer fineMatDetail = new ComputeBuffer(numPoints, sizeof(float));
        GenerateUnderground(chunkSize, meshSkipInc, fineNoise, offset, fineMatDetail, ref bufferHandle);

        bufferHandle.Enqueue(coarseMatDetail);
        bufferHandle.Enqueue(fineMatDetail);
        bufferHandle.Enqueue(materialBuffer);

        materialGenCompute.SetBuffer(0, "coarseMatDetail", coarseMatDetail);
        materialGenCompute.SetBuffer(0, "fineMatDetail", fineMatDetail);
        materialGenCompute.SetBuffer(0, "biomeMap", biomeBuffer);
        materialGenCompute.SetBuffer(0, "material", materialBuffer);//Result
        materialGenCompute.SetInt("numPointsPerAxis", numPointsAxes);

        materialGenCompute.SetFloat("meshSkipInc", meshSkipInc);
        materialGenCompute.SetFloat("chunkSize", chunkSize);
        materialGenCompute.SetFloat("offsetY", offset.y);
        
        materialGenCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);
        materialGenCompute.Dispatch(0, numThreadsAxis, numThreadsAxis, numThreadsAxis);

        return materialBuffer;
    }

    public ComputeBuffer AnalyzeTerrain(ComputeBuffer checks, ComputeBuffer count, ComputeBuffer baseDensity, ComputeBuffer heights, ComputeBuffer squash, float chunkYOrigin, int maxPoints, float IsoLevel, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float), ComputeBufferType.Structured);
        ComputeBuffer args = UtilityBuffers.CountToArgs(terrainAnalyzerGPU, count);
        bufferHandle.Enqueue(result);

        terrainAnalyzerGPU.SetBuffer(0, "numPoints", count);
        terrainAnalyzerGPU.SetBuffer(0, "checks", checks);
        terrainAnalyzerGPU.SetBuffer(0, "base", baseDensity);
        terrainAnalyzerGPU.SetBuffer(0, "heights", heights);
        terrainAnalyzerGPU.SetBuffer(0, "squash", squash);
        terrainAnalyzerGPU.SetFloat("chunkYOrigin", chunkYOrigin);
        terrainAnalyzerGPU.SetFloat("IsoLevel", IsoLevel);

        terrainAnalyzerGPU.SetBuffer(0, "results", result);

        terrainAnalyzerGPU.DispatchIndirect(0, args);
        return result;
    }

    public void GenerateMesh(GPUDensityManager densityManager, Vector3 CCoord, int chunkSize, int meshSkipInc, float IsoLevel, ComputeBuffer triangleBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numCubesAxes = chunkSize / meshSkipInc;
        meshGenerator.SetBuffer(0, "_MemoryBuffer", densityManager.AccessStorage());
        meshGenerator.SetBuffer(0, "_AddressDict", densityManager.AccessAddresses());
        meshGenerator.SetInts("CCoord", new int[] { (int)CCoord.x, (int)CCoord.y, (int)CCoord.z });
        densityManager.SetCCoordHash(meshGenerator);

        meshGenerator.SetFloat("IsoLevel", IsoLevel);
        meshGenerator.SetInt("numPointsPerAxis", numPointsAxes);
        meshGenerator.SetInt("numCubesPerAxis", numCubesAxes);
        meshGenerator.SetFloat("ResizeFactor", meshSkipInc);
        meshGenerator.SetBuffer(0, "triangles", triangleBuffer);

        meshGenerator.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numCubesAxes / (float)threadGroupSize);

        meshGenerator.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateUnderground(int chunkSize, int meshSkipInc, NoiseData noiseData, Vector3 offset, ComputeBuffer pointBuffer, ref Queue<ComputeBuffer> bufferHandle)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        undergroundNoiseCompute.SetBuffer(0, "points", pointBuffer);
        undergroundNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
        Generator.SetNoiseData(undergroundNoiseCompute, chunkSize, meshSkipInc, noiseData, offset, ref bufferHandle);

        undergroundNoiseCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        undergroundNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    public void GenerateTerrain(int chunkSize, int meshSkipInc, SurfaceChunk.SurfaceMap surfaceData, Vector3 offset, float IsoValue, ComputeBuffer pointBuffer)
    {
        int numPointsAxes = chunkSize / meshSkipInc + 1;

        terrainNoiseCompute.SetBuffer(0, "points", pointBuffer);
        terrainNoiseCompute.SetBuffer(0, "heights", surfaceData.heightMap);
        terrainNoiseCompute.SetBuffer(0, "squash", surfaceData.squashMap);
        terrainNoiseCompute.SetInt("numPointsPerAxis", numPointsAxes);
        terrainNoiseCompute.SetFloat("meshSkipInc", meshSkipInc);
        terrainNoiseCompute.SetFloat("chunkSize", chunkSize);
        terrainNoiseCompute.SetFloat("offsetY", offset.y);
        terrainNoiseCompute.SetFloat("IsoLevel", IsoValue);

        terrainNoiseCompute.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxes / (float)threadGroupSize);

        terrainNoiseCompute.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, numThreadsPerAxis);
    }

    //Indirect GPU functions

    //Rewriting all analyze functions as indirect

    public ComputeBuffer SampleStructureLoD(int maxLoD, int chunkSize, float LoDFalloff, int structurePoints0, int maxStructurePoints, Vector3 chunkCoord, ref Queue<ComputeBuffer> bufferHandle)
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

    public ComputeBuffer IdentifyStructures(ComputeBuffer structurePoints, ComputeBuffer count, ComputeBuffer biomes, Vector3 chunkCoord, int chunkSize, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 3, ComputeBufferType.Append);
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructureIdentifier, count);
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

    public ComputeBuffer CreateChecks(ComputeBuffer structures, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(uint) * 2 + sizeof(float) * 3, ComputeBufferType.Append);
        ComputeBuffer args = UtilityBuffers.CountToArgs(StructureChecks, count);
        bufferHandle.Enqueue(results);

        StructureChecks.SetBuffer(0, "structures", structures);
        StructureChecks.SetBuffer(0, "numPoints", count);
        StructureChecks.SetBuffer(0, "checks", results);

        StructureChecks.DispatchIndirect(0, args);

        return results;
    }

    public ComputeBuffer AnalyzeBase(ComputeBuffer points, ComputeBuffer count, NoiseData undergroundNoise, Vector3 offset, int chunkSize, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer results = new ComputeBuffer(maxPoints, sizeof(float));
        ComputeBuffer args = UtilityBuffers.CountToArgs(terrainNoiseCompute, count);
        bufferHandle.Enqueue(results);

        checkNoiseSampler.SetBuffer(0, "CheckPoints", points);
        checkNoiseSampler.SetBuffer(0, "Results", results);
        checkNoiseSampler.SetBuffer(0, "numPoints", count);
        checkNoiseSampler.SetFloat("influenceHeight", 1.0f);
        checkNoiseSampler.DisableKeyword("CENTER_NOISE");
        checkNoiseSampler.DisableKeyword("SAMPLE_2D");

        Generator.SetNoiseData(checkNoiseSampler, chunkSize, 1, undergroundNoise, offset, ref bufferHandle);

        checkNoiseSampler.DispatchIndirect(0, args);

        return results;
    }

    public ComputeBuffer InitializeIndirect<T>(ComputeBuffer count, T val, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
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

        ComputeBuffer args = UtilityBuffers.CountToArgs(indirectMapInitialize, count);

        bufferHandle.Enqueue(map);
        indirectMapInitialize.SetBuffer(0, "numPoints", count);
        indirectMapInitialize.SetBuffer(0, "map", map);

        indirectMapInitialize.DispatchIndirect(0, args);

        return map;

    }

    public ComputeBuffer FilterStructures(ComputeBuffer valid, ComputeBuffer structures, ComputeBuffer count, int maxPoints, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(maxPoints, sizeof(float) * 3 + sizeof(uint) * 3, ComputeBufferType.Append);
        result.SetCounterValue(0);
        bufferHandle.Enqueue(result);

        ComputeBuffer args = UtilityBuffers.CountToArgs(structureCheckFilter, count);

        structureCheckFilter.SetBuffer(0, "numPoints", count);
        structureCheckFilter.SetBuffer(0, "valid", valid);
        structureCheckFilter.SetBuffer(0, "structureInfos", structures);
        structureCheckFilter.SetBuffer(0, "validStructures", result);

        structureCheckFilter.DispatchIndirect(0, args);

        return result;
    }

    public ComputeBuffer CalculateStructureSize(ComputeBuffer structureCount, int structureStride, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer result = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(result);

        structureMemorySize.SetBuffer(0, "structureCount", structureCount);
        structureMemorySize.SetInt("structStride4Byte", structureStride);
        structureMemorySize.SetBuffer(0, "byteLength", result);

        structureMemorySize.Dispatch(0, 1, 1, 1);

        return result;
    }

    public void TranscribeStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer structures, ComputeBuffer count, int addressIndex, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(structureDataTranscriber, count);

        structureDataTranscriber.SetBuffer(0, "_MemoryBuffer", memory);
        structureDataTranscriber.SetBuffer(0, "_AddressDict", addresses);
        structureDataTranscriber.SetInt("addressIndex", addressIndex);

        structureDataTranscriber.SetBuffer(0, "structPoints", structures);
        structureDataTranscriber.SetBuffer(0, "numStructPoints", count);

        structureDataTranscriber.DispatchIndirect(0, args);
    }

    public void AnalyzeChecks(ComputeBuffer checks, ComputeBuffer count, ComputeBuffer density, float IsoValue, ref ComputeBuffer valid, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(checkVerification, count);
        checkVerification.SetBuffer(0, "numPoints", count);
        checkVerification.SetBuffer(0, "checks", checks);
        checkVerification.SetBuffer(0, "density", density);
        checkVerification.SetFloat("IsoValue", IsoValue);

        checkVerification.SetBuffer(0, "validity", valid);

        checkVerification.DispatchIndirect(0, args);
    }

    public void ApplyStructures(ComputeBuffer memory, ComputeBuffer addresses, ComputeBuffer count, ComputeBuffer density, ComputeBuffer material, int addressIndex, int chunkSize, int meshSkipInc, float IsoLevel, ref Queue<ComputeBuffer> bufferHandle)
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

    public ComputeBuffer GetStructCount(ComputeBuffer memory, ComputeBuffer address, int addressIndex, int STRUCTURE_STRIDE_4BYTE, ref Queue<ComputeBuffer> bufferHandle)
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

    public ComputeBuffer CopyCount(ComputeBuffer data, ref Queue<ComputeBuffer> bufferHandle)
    {
        ComputeBuffer count = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        bufferHandle.Enqueue(count);

        count.SetData(new int[] { 0 });
        ComputeBuffer.CopyCount(data, count, 0);

        return count;
    }
}
