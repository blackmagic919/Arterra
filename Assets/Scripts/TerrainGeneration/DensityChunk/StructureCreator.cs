using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;
using static StructureGenerator;

public class StructureCreator
{
    MeshCreatorSettings meshSettings;
    SurfaceCreatorSettings surfSettings;
    uint structureDataIndex;
    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    const int STRUCTURE_STRIDE_4BYTE = 3 + 2 + 1;
    const int THREAD_GROUP_SIZE = 64;

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

        Vector3 offset3D = new Vector3(offset.x, 0, offset.z);

        int maxStructurePoints = calculateMaxStructurePoints(meshSettings.biomeData.maxLoD, meshSettings.biomeData.StructureChecksPerChunk, meshSettings.biomeData.LoDFalloff);

        ComputeBuffer planPointsAppend = SampleStructureLoD(meshSettings.biomeData.maxLoD, chunkSize, meshSettings.biomeData.LoDFalloff, meshSettings.biomeData.StructureChecksPerChunk, maxStructurePoints, chunkCoord, ref tempBuffers);

        ComputeBuffer planCount = UtilityBuffers.CopyCount(planPointsAppend, tempBuffers);
        ComputeBuffer planArgs = UtilityBuffers.CountToArgs(THREAD_GROUP_SIZE, planCount, tempBuffers);

        ComputeBuffer biomes = AnalyzeBiomeMap(planPointsAppend, planArgs, planCount, offset3D, chunkSize, maxStructurePoints);
        ComputeBuffer structureInfo = IdentifyStructures(planPointsAppend, planArgs, planCount, biomes, chunkCoord, chunkSize, maxStructurePoints, ref tempBuffers);
        
        ComputeBuffer structCount = UtilityBuffers.CopyCount(structureInfo, tempBuffers);
        ComputeBuffer structArgs = UtilityBuffers.CountToArgs(THREAD_GROUP_SIZE, structCount, tempBuffers);

        ComputeBuffer checkPoints = CreateChecks(structureInfo, structArgs, structCount, maxStructurePoints, ref tempBuffers);//change maxPoints
        ComputeBuffer checkCount = UtilityBuffers.CopyCount(checkPoints, tempBuffers);
        ComputeBuffer checkArgs = UtilityBuffers.CountToArgs(THREAD_GROUP_SIZE, checkCount, tempBuffers);
        
        AnalyzeTerrainMap(checkPoints, structureInfo, checkArgs, checkCount, offset, chunkSize, IsoLevel);
        ComputeBuffer structureBuffer = FilterStructures(structureInfo, structArgs, structCount, maxStructurePoints, ref tempBuffers);

        ComputeBuffer structureCount = UtilityBuffers.CopyCount(structureBuffer, tempBuffers);
        ComputeBuffer structByteSize = CalculateStructureSize(structureCount, STRUCTURE_STRIDE_4BYTE, ref tempBuffers);
        this.structureDataIndex = meshSettings.structureMemory.AllocateMemory(structByteSize);

        TranscribeStructures(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), 
                            structureBuffer, structureCount, (int)this.structureDataIndex, ref tempBuffers);

        ReleaseTempBuffers();
        return;
    }

    public void GenerateStrucutresGPU(ComputeBuffer pointBuffer, ComputeBuffer materialBuffer, int chunkSize, int LOD, float IsoLevel)
    {
        int meshSkipInc = meshSkipTable[LOD];
        
        ComputeBuffer structCount = GetStructCount(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), 
                                                   (int)structureDataIndex, STRUCTURE_STRIDE_4BYTE, ref tempBuffers);
        
        ApplyStructures(meshSettings.structureMemory.AccessStorage(), meshSettings.structureMemory.AccessAddresses(), structCount, pointBuffer, 
                        materialBuffer, (int)structureDataIndex, chunkSize, meshSkipInc, IsoLevel, ref tempBuffers);

        ReleaseTempBuffers();
        return;
    }

    public ComputeBuffer AnalyzeBiomeMap(ComputeBuffer rawPositions, ComputeBuffer args, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints)
    {
        int[] samplers = new int[6]{surfSettings.TerrainContinentalDetail, surfSettings.TerrainErosionDetail, surfSettings.TerrainPVDetail, 
                                    surfSettings.SquashMapDetail, surfSettings.AtmosphereDetail, surfSettings.HumidityDetail};

        ComputeBuffer biome = AnalyzeBiome(rawPositions, args, count, samplers, offset, chunkSize, maxPoints, tempBuffers);

        return biome;
    }

    public void AnalyzeTerrainMap(ComputeBuffer checks, ComputeBuffer structs, ComputeBuffer args, ComputeBuffer count, Vector3 offset, int chunkSize, float IsoLevel)
    {
        int[] samplers = new int[6]{meshSettings.CoarseTerrainNoise, meshSettings.FineTerrainNoise, surfSettings.TerrainContinentalDetail, 
                                    surfSettings.TerrainErosionDetail, surfSettings.TerrainPVDetail, surfSettings.SquashMapDetail};
        
        float[] heights = new float[4]{surfSettings.MaxContinentalHeight, surfSettings.MaxPVHeight, surfSettings.MaxSquashHeight, surfSettings.terrainOffset};

        AnalyzeTerrain(checks, structs, args, count, samplers, heights, offset, chunkSize, IsoLevel);
    }

    /*
    public ComputeBuffer AnalyzeCaveTerrain(ComputeBuffer points, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints){
        ComputeBuffer coarseDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);
        ComputeBuffer fineDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);

        return null;        
    }*/

}