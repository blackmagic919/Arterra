using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using static TerrainGenerator;

[CreateAssetMenu(menuName = "Containers/Surface Settings")]
public class SurfaceCreatorSettings : ScriptableObject{
    public float terrainOffset = 0;
    public float MaxTerrainHeight;
    public float MaxSquashHeight;
    public float MaxInfluenceHeight;

    [UISetting(Message = "Surface Generation")]
    public string ContinentalNoise;
    public string ErosionNoise;
    //Any curve form
    public string PVNoise;
    //Low Values: More terrain-like, High Values: More overhangs
    public string SquashNoise;

    [UISetting(Message = "Surface Control")]
    public string InfHeightNoise;
    public string InfOffsetNoise;
    public string AtmosphereNoise;

    public int ContinentalIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(ContinentalNoise);
    public int ErosionIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(ErosionNoise);
    public int PVIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(PVNoise);
    public int SquashIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(SquashNoise);
    public int InfHeightIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(InfHeightNoise);
    public int InfOffsetIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(InfOffsetNoise);
    public int AtmosphereIndex => WorldStorageHandler.WORLD_OPTIONS.Generation.Noise.RetrieveIndex(AtmosphereNoise);
}

public static class SurfaceCreator
{
    const uint SURFDATA_STRIDE_4BYTE = 6;   
    public static void SampleSurfaceMaps(float2 offset, int ChunkSize, int SkipInc){
        SampleSurfaceData(offset, ChunkSize, SkipInc);
    }

    public static uint StoreSurfaceMap(int SampleSize)
    {
        int numPointsAxes = SampleSize;
        int numOfPoints = numPointsAxes * numPointsAxes;
        
        uint mapAddressIndex = GenerationPreset.memoryHandle.AllocateMemoryDirect(numOfPoints, (int)SURFDATA_STRIDE_4BYTE);
        TranscribeSurfaceMap(GenerationPreset.memoryHandle.Storage, GenerationPreset.memoryHandle.Address,
                            (int)mapAddressIndex, numOfPoints);

        return mapAddressIndex;
    }

    /*
    public ComputeBuffer SimplifyMap(int addressIndex, int sourceLOD, int destLOD, int chunkSize, bool isFloat)
    {
        int sourceSkipInc = meshSkipTable[sourceLOD];
        int destSkipInc = meshSkipTable[destLOD];

        ComputeBuffer simplified = TerrainGenerator.SimplifyMap(settings.surfaceMemoryBuffer.AccessStorage(), settings.surfaceMemoryBuffer.AccessAddresses(), 
                                                addressIndex, chunkSize, sourceSkipInc, destSkipInc, isFloat, null);

        return simplified;
    }*/

    /*
    //Small error at edges--someone fix
    public T[] SimplifyMap<T>(T[] sourceMap, int sourceLOD, int destLOD, int chunkSize)
    {
        int sourceSkipInc = meshSkipTable[sourceLOD];
        int sourcePointsAxes = chunkSize / sourceSkipInc + 1;

        int destSkipInc = meshSkipTable[destLOD];
        int destPointsAxes = chunkSize / destSkipInc + 1;
        int destPointCount = destPointsAxes * destPointsAxes;

        T[] destMap = new T[destPointCount];

        //Simplify Area
        for(int x = 0; x <= chunkSize; x += destSkipInc)
        {
            for (int y = 0; y <= chunkSize; y += destSkipInc)
            {
                int destIndex = CustomUtility.indexFromCoord2D(x / destSkipInc, y / destSkipInc, destPointsAxes);

                T sourceNoise = sourceMap[CustomUtility.indexFromCoord2D(x / sourceSkipInc, y / sourceSkipInc, sourcePointsAxes)];
                destMap[destIndex] = sourceNoise;
            }
        }

        return destMap;
    }*/

     /*
    public ComputeBuffer GenerateTerrainMaps(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer continentalNoise, out ComputeBuffer erosionNoise, out ComputeBuffer PVNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer continentalDetail; ComputeBuffer pVDetail; ComputeBuffer erosionDetail;
        continentalNoise = GetNoiseMap(settings.TerrainContinentalDetail, offset, settings.MaxContinentalHeight, chunkSize, meshSkipInc, tempBuffers, out continentalDetail);
        PVNoise = GetNoiseMap(settings.TerrainPVDetail, offset, settings.MaxPVHeight, chunkSize, meshSkipInc, tempBuffers, out pVDetail);
        erosionNoise = GetNoiseMap(settings.TerrainErosionDetail, offset, 1, chunkSize, meshSkipInc, tempBuffers, out erosionDetail);

        tempBuffers.Enqueue(continentalDetail);
        tempBuffers.Enqueue(pVDetail);
        tempBuffers.Enqueue(erosionDetail);

        ComputeBuffer heightBuffer = CombineTerrainMaps(continentalDetail, erosionDetail, pVDetail, numOfPoints, settings.terrainOffset, tempBuffers);

        return heightBuffer;
    }

    public ComputeBuffer GenerateSquashMap(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer squashNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;

        ComputeBuffer squashBuffer;
        squashNoise = GetNoiseMap(settings.SquashMapDetail, offset, settings.MaxSquashHeight, chunkSize, meshSkipInc, tempBuffers, out squashBuffer);
        tempBuffers.Enqueue(squashBuffer);

        return squashBuffer;
    }

    //Use interpolated values
    public void GetBiomeNoises(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer humidNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        GetNoiseMap(settings.HumidityDetail, offset, 1, chunkSize, meshSkipInc, tempBuffers, out humidNoise);

        tempBuffers.Enqueue(humidNoise);
    }
    
    public ComputeBuffer GetAtmosphereMap(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer atmosphereNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer atmosphereBuffer;
        atmosphereNoise = GetNoiseMap(settings.AtmosphereDetail, offset, 1, chunkSize, meshSkipInc, tempBuffers, out atmosphereBuffer);
        tempBuffers.Enqueue(atmosphereBuffer);
        
        return atmosphereBuffer;
    }

    public ComputeBuffer ConstructBiomes(int chunkSize, int LOD, ref SurfaceChunk.NoiseMaps noiseMaps)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer biomeMap = GetBiomeMap(chunkSize, meshSkipInc, noiseMaps, tempBuffers);
        int[] ret = new int[numOfPoints];
        biomeMap.GetData(ret);

        for(int i = 0; i < numOfPoints; i++)
        {
            float[] point = new float[6]
            {
                noiseMaps.continental[i],
                noiseMaps.erosion[i],
                noiseMaps.pvNoise[i],
                noiseMaps.squash[i],
                noiseMaps.temperature[i],
                noiseMaps.humidity[i]
            };

            ret[i] = biomeData.dictionary.Query(point);
        }
        return biomeMap;
    }
    */
}
