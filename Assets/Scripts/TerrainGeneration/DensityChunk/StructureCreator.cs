using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static StructureGenerator;
using Unity.Mathematics;

public class StructureCreator
{
    
    uint structureDataIndex;
    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;
    const int SAMPLE_STRIDE_WORD = 3 + 1;
    const int structCounter = 1;

    public void ReleaseStructure()
    {
        GenerationPreset.memoryHandle.ReleaseMemory(this.structureDataIndex);
    }
    public int[] calculateLoDPoints(int maxLoD, int maxStructurePoints, float falloffFactor)
    {
        int[] points = new int[maxLoD + 2]; //suffix sum
        for(int LoD = maxLoD; LoD >= 0; LoD--)
        {
            points[LoD] = Mathf.CeilToInt(maxStructurePoints * Mathf.Pow(falloffFactor, -LoD)) + points[LoD+1];
        }
        return points;
    }

    public int calculateMaxStructurePoints(int maxLoD, int maxStructurePoints, float falloffFactor)
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

    //All Done Without Readback!
    public void PlanStructuresGPU(int3 chunkCoord, float3 offset, int chunkSize, float IsoLevel, int depth=0)
    {
        ReleaseStructure();
        UtilityBuffers.ClearRange(UtilityBuffers.GenerationBuffer, 4, 0);
        SampleStructureLoD(WorldStorageHandler.WORLD_OPTIONS.Generation.Biomes.value.maxLoD, chunkSize, depth, chunkCoord);
        IdentifyStructures(offset, IsoLevel);
        this.structureDataIndex = TranscribeStructures(GenerationPreset.memoryHandle.Storage, GenerationPreset.memoryHandle.Address);

        return;
    }

    public void GenerateStrucutresGPU(int chunkSize, int skipInc, int mapStart, float IsoLevel, int wChunkSize = -1, int wOffset = 0)
    {
        if(wChunkSize == -1) wChunkSize = chunkSize;
        ComputeBuffer structCount = GetStructCount(GenerationPreset.memoryHandle.Storage, GenerationPreset.memoryHandle.Address, (int)structureDataIndex, STRUCTURE_STRIDE_WORD);
        ApplyStructures(GenerationPreset.memoryHandle.Storage, GenerationPreset.memoryHandle.Address, structCount, 
                        (int)structureDataIndex, mapStart, chunkSize, skipInc, wOffset, wChunkSize, IsoLevel);

        return;
    }

    /*
    public ComputeBuffer AnalyzeCaveTerrain(ComputeBuffer points, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints){
        ComputeBuffer coarseDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);
        ComputeBuffer fineDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);

        return null;        
    }*/

}