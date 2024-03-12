using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static EndlessTerrain;
using static TerrainGenerator;

[CreateAssetMenu(menuName = "Containers/Surface Settings")]
public class SurfaceCreatorSettings : ScriptableObject{
    public float terrainOffset = 0;
    [Header("Continental Detail")]
    [Tooltip("Base Terrain Height")]
    /* General Curve Form:
     *            __
     *          _/
     *        _/
     *     __/
     *   _/
     *__/
     * Low Values: Low Altitude, High Values: High Altitude 
     */
    public NoiseData TerrainContinentalDetail;
    public float MaxContinentalHeight;
    /* General Curve Form:
     *_
     * \_
     *   \_
     *     \_
     *       \_
     *         \_
     * Low Values: High PV Noise, High Values: Low PV Noise
     */
    [Space(10)]
    [Header("Erosion Detail")]
    [Tooltip("Influence of PV Map")]
    public NoiseData TerrainErosionDetail;

    [Space(10)]
    [Header("Peaks and Values")]
    [Tooltip("Fine detail of terrain")]
    //Any curve form
    public NoiseData TerrainPVDetail;
    public float MaxPVHeight;

    [Space(10)]
    [Header("Squash Map")]
    //Low Values: More terrain-like, High Values: More overhangs
    public NoiseData SquashMapDetail;
    public float MaxSquashHeight;

    [Space(10)]
    [Header("Atmosphere Map")]
    [Tooltip("How quickly the atmosphere fades off")]
    public NoiseData AtmosphereDetail;

    [Space(10)]
    [Header("Biome Maps")]
    [Tooltip("Extra details to add variation to biomes")]
    //Any curve form
    public NoiseData HumidityDetail;

    [Space(10)]
    [Header("Dependencies")]
    public MemoryBufferSettings surfaceMemoryBuffer;

    [HideInInspector]
    public BiomeGenerationData biomeData;
}

public class SurfaceCreator
{
    public SurfaceCreatorSettings settings;
    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public void OnDisable()
    {
        ReleaseTempBuffers();
    }

    public SurfaceCreator(SurfaceCreatorSettings settings){
        this.settings = settings;
    }
    

    public ComputeBuffer GenerateTerrainMaps(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer continentalNoise, out ComputeBuffer erosionNoise, out ComputeBuffer PVNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer continentalDetail; ComputeBuffer pVDetail; ComputeBuffer erosionDetail;
        continentalNoise = GetNoiseMap(settings.TerrainContinentalDetail, offset, settings.MaxContinentalHeight, chunkSize, meshSkipInc, false, tempBuffers, out continentalDetail);
        PVNoise = GetNoiseMap(settings.TerrainPVDetail, offset, settings.MaxPVHeight, chunkSize, meshSkipInc, true, tempBuffers, out pVDetail);
        erosionNoise = GetNoiseMap(settings.TerrainErosionDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out erosionDetail);

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
        squashNoise = GetNoiseMap(settings.SquashMapDetail, offset, settings.MaxSquashHeight, chunkSize, meshSkipInc, false, tempBuffers, out squashBuffer);
        tempBuffers.Enqueue(squashBuffer);

        return squashBuffer;
    }

    //Use interpolated values
    public void GetBiomeNoises(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer humidNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        GetNoiseMap(settings.HumidityDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out humidNoise);

        tempBuffers.Enqueue(humidNoise);
    }
    
    public ComputeBuffer GetAtmosphereMap(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer atmosphereNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer atmosphereBuffer;
        atmosphereNoise = GetNoiseMap(settings.AtmosphereDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out atmosphereBuffer);
        tempBuffers.Enqueue(atmosphereBuffer);
        
        return atmosphereBuffer;
    }

    public uint StoreSurfaceMap(ComputeBuffer surfaceMap, int chunkSize, int LOD, bool isFloat)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;
        
        ComputeBuffer mapSize4Byte = new ComputeBuffer(1, sizeof(uint), ComputeBufferType.Structured);
        mapSize4Byte.SetData(new uint[1] { (uint)numOfPoints });
        tempBuffers.Enqueue(mapSize4Byte);

        uint mapAddressIndex = settings.surfaceMemoryBuffer.AllocateMemory(mapSize4Byte);
        TranscribeSurfaceMap(settings.surfaceMemoryBuffer.AccessStorage(), settings.surfaceMemoryBuffer.AccessAddresses(),
                                              (int)mapAddressIndex, surfaceMap, numOfPoints, isFloat);

        return mapAddressIndex;
    }

    public ComputeBuffer ConstructBiomes(int chunkSize, int LOD, ref SurfaceChunk.NoiseMaps noiseMaps)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer biomeMap = GetBiomeMap(chunkSize, meshSkipInc, noiseMaps, tempBuffers);
        /*int[] ret = new int[numOfPoints];
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
        }*/
        return biomeMap;
    }

    
    public ComputeBuffer SimplifyMap(int addressIndex, int sourceLOD, int destLOD, int chunkSize, bool isFloat)
    {
        int sourceSkipInc = meshSkipTable[sourceLOD];
        int destSkipInc = meshSkipTable[destLOD];

        ComputeBuffer simplified = TerrainGenerator.SimplifyMap(settings.surfaceMemoryBuffer.AccessStorage(), settings.surfaceMemoryBuffer.AccessAddresses(), 
                                                addressIndex, chunkSize, sourceSkipInc, destSkipInc, isFloat, null);

        return simplified;
    }

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
    public void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue().Release();
        }
    }
}
