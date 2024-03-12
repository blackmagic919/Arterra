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
        float chunkYOrigin = offset.y - (chunkSize/2);

        int maxStructurePoints = calculateMaxStructurePoints(meshSettings.biomeData.maxLoD, meshSettings.biomeData.StructureChecksPerChunk, meshSettings.biomeData.LoDFalloff);

        ComputeBuffer planPointsAppend = SampleStructureLoD(meshSettings.biomeData.maxLoD, chunkSize, meshSettings.biomeData.LoDFalloff, meshSettings.biomeData.StructureChecksPerChunk, maxStructurePoints, chunkCoord, ref tempBuffers);

        ComputeBuffer planCount = UtilityBuffers.CopyCount(planPointsAppend, tempBuffers);
        ComputeBuffer biomes = AnalyzeBiomeMap(planPointsAppend, planCount, offset3D, chunkSize, maxStructurePoints);
        ComputeBuffer structureInfo = IdentifyStructures(planPointsAppend, planCount, biomes, chunkCoord, chunkSize, maxStructurePoints, ref tempBuffers);
        
        ComputeBuffer structCount = UtilityBuffers.CopyCount(structureInfo, tempBuffers);
        ComputeBuffer checkPoints = CreateChecks(structureInfo, structCount, maxStructurePoints, ref tempBuffers);//change maxPoints

        ComputeBuffer checkCount = UtilityBuffers.CopyCount(checkPoints, tempBuffers);
        ComputeBuffer terrainHeights = AnalyzeTerrainMap(checkPoints, checkCount, offset3D, chunkSize, maxStructurePoints);
        ComputeBuffer baseDensity = AnalyzeNoiseMapGPU(sampler_terrCoarse, checkPoints, checkCount, offset, chunkSize, maxStructurePoints, tempBuffers);
        ComputeBuffer squashHeights = AnalyzeNoiseMapGPU(sampler_terrSquash, checkPoints, checkCount, offset3D, chunkSize, maxStructurePoints, tempBuffers);
        
        ComputeBuffer densities = AnalyzeTerrain(checkPoints, checkCount, baseDensity, terrainHeights, squashHeights, chunkYOrigin, maxStructurePoints, IsoLevel, ref tempBuffers);

        ComputeBuffer checkResults = InitializeIndirect(structCount, true, maxStructurePoints, ref tempBuffers);
        AnalyzeChecks(checkPoints, checkCount, densities, IsoLevel, ref checkResults, ref tempBuffers);
        ComputeBuffer structureBuffer = FilterStructures(checkResults, structureInfo, structCount, maxStructurePoints, ref tempBuffers);

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

    public ComputeBuffer AnalyzeBiomeMap(ComputeBuffer rawPositions, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints)
    {
        SurfaceChunk.NoiseMaps noiseMaps;

        ComputeBuffer args = UtilityBuffers.CountToArgs(checkNoiseSampler, count);
        noiseMaps.continental = AnalyzeNoiseMapGPU(sampler_surfContinental, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);
        noiseMaps.pvNoise = AnalyzeNoiseMapGPU(sampler_surfPV, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);
        noiseMaps.erosion = AnalyzeNoiseMapGPU(sampler_surfErosion, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);
        noiseMaps.squash = AnalyzeNoiseMapGPU(sampler_surfSquash, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);
        noiseMaps.atmosphere = AnalyzeNoiseMapGPU(sampler_surfAtmosphere, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);
        noiseMaps.humidity = AnalyzeNoiseMapGPU(sampler_surfHumidity, rawPositions, args, count, offset, chunkSize, maxPoints, tempBuffers);

        ComputeBuffer biome = AnalyzeBiome(noiseMaps, count, maxPoints, tempBuffers);

        return biome;
    }

    public ComputeBuffer AnalyzeTerrainMap(ComputeBuffer checks, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints)
    {
        ComputeBuffer args = UtilityBuffers.CountToArgs(checkNoiseSampler, count);
        ComputeBuffer continentalDetail = AnalyzeNoiseMapGPU(sampler_terrContinental, checks, args, count, offset, chunkSize, maxPoints, tempBuffers);
        ComputeBuffer pVDetail = AnalyzeNoiseMapGPU(sampler_terrPV, checks, args, count, offset, chunkSize, maxPoints, tempBuffers);
        ComputeBuffer erosionDetail = AnalyzeNoiseMapGPU(sampler_terrErosion, checks, args, count, offset, chunkSize, maxPoints, tempBuffers);

        ComputeBuffer results = CombineTerrainMapsGPU(count, continentalDetail, erosionDetail, pVDetail, maxPoints, surfSettings.terrainOffset, tempBuffers);

        return results;
    }

    /*
    public ComputeBuffer AnalyzeCaveTerrain(ComputeBuffer points, ComputeBuffer count, Vector3 offset, int chunkSize, int maxPoints){
        ComputeBuffer coarseDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);
        ComputeBuffer fineDetail = AnalyzeNoiseMapGPU(points, count, meshSettings.CoarseTerrainNoise, offset, 1, chunkSize, maxPoints, false, true, false, tempBuffers);

        return null;        
    }*/

}
