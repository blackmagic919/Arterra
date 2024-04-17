using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using static StructureGenerator;

public class StructureCreator
{
    public MeshCreatorSettings meshSettings;
    public SurfaceCreatorSettings surfSettings;
    uint structureDataIndex;
    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    const int STRUCTURE_STRIDE_WORD = 3 + 2 + 1;
    const int SAMPLE_STRIDE_WORD = 3 + 1;

    const int samplerCounter = 0;
    const int structCounter = 1;

    public StructureCreator(MeshCreatorSettings mSettings, SurfaceCreatorSettings sSettings){
        this.meshSettings = mSettings;
        this.surfSettings = sSettings;
    }

    public void ReleaseStructure()
    {
        if(meshSettings.structureMemory != null)
            meshSettings.structureMemory.ReleaseMemory(this.structureDataIndex);
    }

    public void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue()?.Release();
        }
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
    public void PlanStructuresGPU(Vector3 chunkCoord, Vector3 offset, int chunkSize, float IsoLevel)
    {
        ReleaseStructure();
        UtilityBuffers.ClearCounters(UtilityBuffers.GenerationBuffer, 2);

        SampleStructureLoD(meshSettings.biomeData.maxLoD, chunkSize, chunkCoord);

        //ComputeBuffer planCount = UtilityBuffers.CopyCount(planPointsAppend, tempBuffers);
        IdentifyStructures(chunkCoord, offset, IsoLevel, chunkSize);

        //ComputeBuffer structureCount = UtilityBuffers.CopyCount(structureBuffer, tempBuffers);
        //ComputeBuffer structByteSize = CalculateStructureSize(structureCount, STRUCTURE_STRIDE_4BYTE, ref tempBuffers);
        this.structureDataIndex = meshSettings.structureMemory.AllocateMemory(UtilityBuffers.GenerationBuffer, STRUCTURE_STRIDE_WORD, structCounter);
        TranscribeStructures(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), (int)this.structureDataIndex);

        ReleaseTempBuffers();
        return;
    }

    public void GenerateStrucutresGPU(int chunkSize, int LOD, float IsoLevel)
    {
        int meshSkipInc = meshSkipTable[LOD];
        
        ComputeBuffer structCount = GetStructCount(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), (int)structureDataIndex, STRUCTURE_STRIDE_WORD);
        ApplyStructures(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), structCount, 
                        (int)structureDataIndex, chunkSize, meshSkipInc, IsoLevel);

        ReleaseTempBuffers();
        return;
    }

    /*
    public ComputeBuffer AnalyzeCaveTerrain(ComputeBuffer points, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints){
        ComputeBuffer coarseDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);
        ComputeBuffer fineDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);

        return null;        
    }*/

}