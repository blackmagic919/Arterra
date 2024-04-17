using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static EditorMesh;
using static SurfaceChunk;

[CreateAssetMenu(menuName = "Containers/Map Creator")]
public class MapCreator : ScriptableObject
{
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
    public NoiseData AtmosphereDetail;
    [Tooltip("How quickly the atmosphere fades off")]
    public float AtmosphereFalloff;

    [Space(10)]
    [Header("Biome Maps")]
    [Tooltip("Extra details to add variation to biomes")]
    //Any curve form
    public NoiseData HumidityDetail;

    [Space(10)]
    [Header("Dependencies")]
    public MemoryBufferSettings surfaceMemoryBuffer;
    public TerrainGenerator terrainGenerator;
    [HideInInspector]
    public BiomeGenerationData biomeData;

    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public void OnDisable()
    {
        ReleaseTempBuffers();
    }
    

    public ComputeBuffer AnalyzeSquashMap(Vector3[] rawPositions, Vector2 offset, int chunkSize)
    {
        int numOfPoints = rawPositions.Length;
        Vector3[] position3D = rawPositions.Select(e => new Vector3(e.x, 0, e.z)).ToArray();
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer squash = terrainGenerator.AnalyzeNoiseMap(position3D, SquashMapDetail, offset3D, MaxSquashHeight, chunkSize, false, tempBuffers);

        return squash;
    }

    public ComputeBuffer GenerateTerrainMaps(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer continentalNoise, out ComputeBuffer erosionNoise, out ComputeBuffer PVNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer continentalDetail; ComputeBuffer pVDetail; ComputeBuffer erosionDetail;
        continentalNoise = terrainGenerator.GetNoiseMap(TerrainContinentalDetail, offset, MaxContinentalHeight, chunkSize, meshSkipInc, false, tempBuffers, out continentalDetail);
        PVNoise = terrainGenerator.GetNoiseMap(TerrainPVDetail, offset, MaxPVHeight, chunkSize, meshSkipInc, true, tempBuffers, out pVDetail);
        erosionNoise = terrainGenerator.GetNoiseMap(TerrainErosionDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out erosionDetail);

        tempBuffers.Enqueue(continentalDetail);
        tempBuffers.Enqueue(pVDetail);
        tempBuffers.Enqueue(erosionDetail);

        ComputeBuffer heightBuffer = terrainGenerator.CombineTerrainMaps(continentalDetail, erosionDetail, pVDetail, numOfPoints, terrainOffset, tempBuffers);

        return heightBuffer;
    }

    public ComputeBuffer AnalyzeBiomeMap(ComputeBuffer rawPositions, ComputeBuffer count, Vector2 offset, int chunkSize, int maxPoints)
    {
        Vector3 offset3D = new(offset.x, 0, offset.y);

        SurfaceChunk.NoiseMaps noiseMaps;

        noiseMaps.continental = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, TerrainContinentalDetail, offset3D, chunkSize, maxPoints, tempBuffers);
        noiseMaps.pvNoise = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, TerrainPVDetail, offset3D, chunkSize, maxPoints, tempBuffers);
        noiseMaps.erosion = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, TerrainErosionDetail, offset3D, chunkSize, maxPoints, tempBuffers);
        noiseMaps.squash = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, SquashMapDetail, offset3D, chunkSize, maxPoints, tempBuffers);
        noiseMaps.atmosphere = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, AtmosphereDetail, offset3D, chunkSize, maxPoints, tempBuffers);
        noiseMaps.humidity = terrainGenerator.AnalyzeRawNoiseMap(rawPositions, count, HumidityDetail, offset3D, chunkSize, maxPoints, tempBuffers);

        ComputeBuffer biome = terrainGenerator.AnalyzeBiome(noiseMaps, count, maxPoints, tempBuffers);

        return biome;
    }

    public ComputeBuffer AnalyzeSquashMap(ComputeBuffer checks, ComputeBuffer count, Vector2 offset, int chunkSize, int maxPoints)
    {
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer squash = terrainGenerator.AnalyzeNoiseMapGPU(checks, count, SquashMapDetail, offset3D, MaxSquashHeight, chunkSize, maxPoints, false, tempBuffers);

        return squash;
    }

    public ComputeBuffer AnalyzeTerrainMap(ComputeBuffer checks, ComputeBuffer count, Vector2 offset, int chunkSize, int maxPoints)
    {
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer continentalDetail = terrainGenerator.AnalyzeNoiseMapGPU(checks, count, TerrainContinentalDetail, offset3D, MaxContinentalHeight, chunkSize, maxPoints, false, tempBuffers);
        ComputeBuffer pVDetail = terrainGenerator.AnalyzeNoiseMapGPU(checks, count, TerrainPVDetail, offset3D, MaxPVHeight, chunkSize, maxPoints, true, tempBuffers);
        ComputeBuffer erosionDetail = terrainGenerator.AnalyzeNoiseMapGPU(checks, count, TerrainErosionDetail, offset3D, 1, chunkSize, maxPoints, false, tempBuffers);

        ComputeBuffer results = terrainGenerator.CombineTerrainMapsGPU(count, continentalDetail, erosionDetail, pVDetail, maxPoints, terrainOffset, tempBuffers);

        return results;
    }

    public ComputeBuffer GenerateSquashMap(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer squashNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;

        ComputeBuffer squashBuffer;
        squashNoise = terrainGenerator.GetNoiseMap(SquashMapDetail, offset, MaxSquashHeight, chunkSize, meshSkipInc, false, tempBuffers, out squashBuffer);
        tempBuffers.Enqueue(squashBuffer);

        return squashBuffer;
    }

    //Use interpolated values
    public void GetBiomeNoises(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer humidNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        terrainGenerator.GetNoiseMap(HumidityDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out humidNoise);

        tempBuffers.Enqueue(humidNoise);
    }
    
    public ComputeBuffer GetAtmosphereMap(int chunkSize, int LOD, Vector2 offset, out ComputeBuffer atmosphereNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer atmosphereBuffer;
        atmosphereBuffer = terrainGenerator.GetNoiseMap(AtmosphereDetail, offset, 1, chunkSize, meshSkipInc, false, tempBuffers, out atmosphereNoise);
        terrainGenerator.ApplyMapFalloff(atmosphereBuffer, chunkSize, meshSkipInc, AtmosphereFalloff);

        tempBuffers.Enqueue(atmosphereNoise);
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

        uint mapAddressIndex = surfaceMemoryBuffer.AllocateMemory(mapSize4Byte);
        terrainGenerator.TranscribeSurfaceMap(this.surfaceMemoryBuffer.AccessStorage(), this.surfaceMemoryBuffer.AccessAddresses(),
                                              (int)mapAddressIndex, surfaceMap, numOfPoints, isFloat);

        return mapAddressIndex;
    }

    public ComputeBuffer ConstructBiomes(int chunkSize, int LOD, ref NoiseMaps noiseMaps)
    {
        int meshSkipInc = meshSkipTable[LOD];

        ComputeBuffer biomeMap = terrainGenerator.GetBiomeMap(chunkSize, meshSkipInc, noiseMaps, tempBuffers);
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

        ComputeBuffer simplified = terrainGenerator.SimplifyMap(this.surfaceMemoryBuffer.AccessStorage(), this.surfaceMemoryBuffer.AccessAddresses(), 
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
