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
    [Header("Biome Maps")]
    [Tooltip("Extra details to add variation to biomes")]
    //Any curve form
    public NoiseData TemperatureDetail;
    public NoiseData HumidityDetail;

    [Space(10)]
    [Header("Dependencies")]
    public TerrainGenerator terrainGenerator;
    [HideInInspector]
    public BiomeGenerationData biomeData;

    Queue<ComputeBuffer> buffersToRelease = new Queue<ComputeBuffer>();

    public void OnValidate()
    {
        terrainGenerator.buffersToRelease = buffersToRelease;
    }

    public ComputeBuffer AnalyzeSquashMap(Vector3[] rawPositions, Vector2 offset, int chunkSize)
    {
        int numOfPoints = rawPositions.Length;
        Vector3[] position3D = rawPositions.Select(e => new Vector3(e.x, 0, e.z)).ToArray();
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer squash = terrainGenerator.AnalyzeNoiseMap(position3D, SquashMapDetail, offset3D, MaxSquashHeight, chunkSize, false);

        return squash;
    }

    public ComputeBuffer AnalyzeTerrainMap(Vector3[] rawPositions, Vector2 offset, int chunkSize)
    {
        int numOfPoints = rawPositions.Length;
        Vector3[] position3D = rawPositions.Select(e => new Vector3(e.x, 0, e.z)).ToArray();
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer continentalDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainContinentalDetail, offset3D, MaxContinentalHeight, chunkSize, false);
        ComputeBuffer pVDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainPVDetail, offset3D, MaxPVHeight, chunkSize, true);
        ComputeBuffer erosionDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainErosionDetail, offset3D, 1, chunkSize, false);

        ComputeBuffer results = terrainGenerator.CombineTerrainMaps(continentalDetail, erosionDetail, pVDetail, numOfPoints, terrainOffset);

        return results;
    }

    public float[] GenerateTerrainMaps(int chunkSize, int LOD, Vector2 offset, out float[] continentalNoise, out float[] erosionNoise, out float[] PVNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer continentalDetail = terrainGenerator.GetNoiseMap(TerrainContinentalDetail, offset, MaxContinentalHeight, chunkSize, meshSkipInc, false, out continentalNoise);
        ComputeBuffer pVDetail = terrainGenerator.GetNoiseMap(TerrainPVDetail, offset, MaxPVHeight, chunkSize, meshSkipInc, true, out PVNoise);
        ComputeBuffer erosionDetail = terrainGenerator.GetNoiseMap(TerrainErosionDetail, offset, 1, chunkSize, meshSkipInc, false, out erosionNoise);

        ComputeBuffer heightBuffer = terrainGenerator.CombineTerrainMaps(continentalDetail, erosionDetail, pVDetail, numOfPoints, terrainOffset);

        float[] heights = new float[numOfPoints];
        heightBuffer.GetData(heights);

        return heights;
    }

    public float[] GenerateSquashMap(int chunkSize, int LOD, Vector2 offset, out float[] squashNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer squashBuffer = terrainGenerator.GetNoiseMap(SquashMapDetail, offset, MaxSquashHeight, chunkSize, meshSkipInc, false, out squashNoise);

        float[] squashHeight = new float[numOfPoints];
        squashBuffer.GetData(squashHeight);

        return squashHeight;
    }

    //Use interpolated values
    public void GetBiomeNoises(int chunkSize, int LOD, Vector2 offset, out float[] tempNoise, out float[] humidNoise)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        ComputeBuffer tempBuffer = terrainGenerator.GetNoiseMap(TemperatureDetail, offset, 1, chunkSize, meshSkipInc, false, out float[] _);
        ComputeBuffer humidBuffer = terrainGenerator.GetNoiseMap(HumidityDetail, offset, 1, chunkSize, meshSkipInc, false, out float[] _);

        tempNoise = new float[numOfPoints];
        humidNoise = new float[numOfPoints];
        tempBuffer.GetData(tempNoise);
        humidBuffer.GetData(humidNoise);
    }

    public int[] ConstructBiomes(int chunkSize, int LOD, ref NoiseMaps noiseMaps)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes;

        int[] ret = new int[numOfPoints];
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
        return ret;
    }

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
    }

    public void ReleaseBuffers()
    {
        while (buffersToRelease.Count > 0)
        {
            buffersToRelease.Dequeue().Release();
        }
    }
}
