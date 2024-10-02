/*
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static BiomeInfo;
using static MeshCreator;
using Utils;

public class StructureCode
{
    public NoiseData TerrainNoise; //For underground terrain generation
    public NoiseData MaterialCoarseNoise;
    public NoiseData MaterialFineNoise;


    public StructureAnalyzer densityGenerator; //Depreceated Code
    public TerrainGenerator terrainGenerator; //Still using code
    public MapCreator mapCreator;
    [HideInInspector]
    public BiomeGenerationData biomeData;
    public TextureData textureData;

    ComputeBuffer pointBuffer;
    ComputeBuffer materialBuffer;
    ComputeBuffer structureBuffer;

    public float terrainOffset = 0;
    [Header("Continental Detail")]
    [Tooltip("Base Terrain Height")]
    public NoiseData TerrainContinentalDetail;
    public float MaxContinentalHeight;

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

    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();
    Queue<ComputeBuffer> persistantBuffers = new Queue<ComputeBuffer>();
    StructureDictionary structureDictionary = new StructureDictionary();

    int[] meshSkipTable = new int[6];

    public void PlanStructures(SurfaceChunk.SurfaceMap surfaceData, Vector3 chunkCoord, Vector3 offset, int chunkSize, float IsoLevel)
    {
        int chunkSeed = (int)chunkCoord.x * 2 + (int)chunkCoord.y * 3 + (int)chunkCoord.z * 5;
        System.Random prng = new System.Random(chunkSeed);

        Queue<StructureInfo> structures = new Queue<StructureInfo>();
        List<Vector3> checkPositions = new List<Vector3>();

        int[] biomeMap = new int[(chunkSize + 1) * (chunkSize + 1)];
        surfaceData.biomeMap.GetData(biomeMap);

        for (int _ = 0; _ < biomeData.StructureChecksPerChunk; _++)
        {
            //If 0, nearby chunk edges aren't updated
            int x; int y; int z;
            (x, y, z) = (prng.Next(1, chunkSize + 1), prng.Next(1, chunkSize + 1), prng.Next(1, chunkSize + 1));
            int biome = biomeMap[CustomUtility.indexFromCoord2D(x, z, chunkSize + 1)];
            if (biomeData.biomes[biome].info.structurePointDensity < prng.NextDouble())
                continue;

            BiomeInfo info = biomeData.biomes[biome].info;
            TerrainStructure structure = info.GetStructure((float)prng.NextDouble());

            float actualHeight = y - chunkSize / 2 + offset.y;
            float heightDensity = structure.VerticalPreference.GetDensity(actualHeight);

            if (heightDensity < prng.NextDouble())
                continue;

            Vector3 position = new(x, y, z);
            for (int i = 0; i < structure.structureData.checks.Length; i++)
            {
                checkPositions.Add(position + structure.structureData.checks[i].position);
            }

            StructureInfo newStructureInfo = new StructureInfo(structure.structureData.checks, structure.structureData, structure.structureSettings, position);
            structures.Enqueue(newStructureInfo);
        }

        Vector3[] rawPositions = checkPositions.ToArray();
        float heightOffest = offset.y - (chunkSize / 2);
        float[] posY = rawPositions.Select((e) => e.y + heightOffest).ToArray();
        Vector2 offset2D = new Vector2(offset.x, offset.z);

        if (rawPositions.Length <= 0)
            return;

        ComputeBuffer baseDensity = densityGenerator.AnalyzeBase(rawPositions, TerrainNoise, offset, chunkSize, ref tempBuffers);
        ComputeBuffer terrainHeights = AnalyzeTerrainMap(rawPositions, offset2D, chunkSize);
        ComputeBuffer squashHeights = mapCreator.AnalyzeSquashMap(rawPositions, offset2D, chunkSize);

        float[] densities = densityGenerator.AnalyzeTerrain(posY, baseDensity, terrainHeights, squashHeights);
        mapCreator.ReleaseTempBuffers();

        int checkCount = 0;
        int structureCount = structures.Count;
        for (int _ = 0; _ < structureCount; _++)
        {
            StructureInfo structure = structures.Dequeue();
            bool passed = true;
            for (int i = 0; i < structure.checks.Length; i++)
            {
                bool isUnderGround = densities[checkCount] >= IsoLevel;
                passed = passed && (isUnderGround == (structure.checks[i].isUnderGround == 1));
                checkCount++;
            }

            if (passed)
                structures.Enqueue(structure);
        }

        while (structures.Count > 0)
        {
            StructureInfo structureInfo = structures.Dequeue();
            StructureData data = structureInfo.data;
            StructureSettings settings = structureInfo.settings;

            int theta90 = 0;
            int phi90 = 0;

            if (settings.randThetaRot)
                theta90 = prng.Next(0, 4); //0, pi/2, pi, 3pi/2
            if (settings.randPhiRot)
                phi90 = prng.Next(0, 3); //0, pi/2, pi

            int[] transformAxis = CustomUtility.RotateAxis(theta90, phi90);

            float[] originalSize = { settings.sizeX, settings.sizeY, settings.sizeZ };
            float sizeX = originalSize[Mathf.Abs(transformAxis[0]) - 1];
            float sizeY = originalSize[Mathf.Abs(transformAxis[1]) - 1];
            float sizeZ = originalSize[Mathf.Abs(transformAxis[2]) - 1];

            Vector3 cornerCC = (structureInfo.position + new Vector3(sizeX, sizeY, sizeZ)) / chunkSize;

            for (int x = 0; x < cornerCC.x; x++)
            {
                for (int y = 0; y < cornerCC.y; y++)
                {
                    for (int z = 0; z < cornerCC.z; z++)
                    {
                        Vector3 deltaCC = new Vector3(x, y, z);

                        //Where is the structure in the structure map?
                        Vector3 structureOrigin = deltaCC * chunkSize - structureInfo.position;
                        structureOrigin = new Vector3(
                            Mathf.Max(structureOrigin.x, 0),
                            Mathf.Max(structureOrigin.y, 0),
                            Mathf.Max(structureOrigin.z, 0)
                        );

                        //Where is the structure in the chunk?
                        Vector3 chunkOrigin = new Vector3(
                            x != 0 ? 0 : structureInfo.position.x,
                            y != 0 ? 0 : structureInfo.position.y,
                            z != 0 ? 0 : structureInfo.position.z
                        );

                        structureDictionary.Add(chunkCoord + deltaCC, new(data, settings, structureOrigin, chunkOrigin, transformAxis));
                    }
                }
            }

        }
    }
    public void GenerateStructures(Vector3 CCoord, float IsoLevel, int LOD, int chunkSize)
    {
        GenerateStructures(CCoord, IsoLevel, LOD, chunkSize, apply: true);
    }

    public void GenerateStructures(Vector3 CCoord, float IsoLevel, int LOD, int chunkSize, bool apply = false)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        float[] densityMap = new float[numOfPoints];
        int[] materialMap = Enumerable.Repeat(-1, numOfPoints).ToArray();

        List<StructureDictionary.StructureSection> structures = structureDictionary.GetChunk(CCoord);
        foreach (StructureDictionary.StructureSection structure in structures)
        {
            StructureData data = structure.data;
            StructureSettings settings = structure.settings;

            int startX = Mathf.CeilToInt(structure.chunkOrigin.x / meshSkipInc) * meshSkipInc;
            int endX = (int)Mathf.Min(structure.transformSizes[0] - structure.structureOrigin.x + structure.chunkOrigin.x, chunkSize + 1);
            int startY = Mathf.CeilToInt(structure.chunkOrigin.y / meshSkipInc) * meshSkipInc;
            int endY = (int)Mathf.Min(structure.transformSizes[1] - structure.structureOrigin.y + structure.chunkOrigin.y, chunkSize + 1);
            int startZ = Mathf.CeilToInt(structure.chunkOrigin.z / meshSkipInc) * meshSkipInc;
            int endZ = (int)Mathf.Min(structure.transformSizes[2] - structure.structureOrigin.z + structure.chunkOrigin.z, chunkSize + 1);

            for (int x = startX; x < endX; x += meshSkipInc)
            {
                for (int y = startY; y < endY; y += meshSkipInc)
                {
                    for (int z = startZ; z < endZ; z += meshSkipInc)
                    {
                        int structureX = (x - (int)structure.chunkOrigin.x) + (int)structure.structureOrigin.x;
                        int structureY = (y - (int)structure.chunkOrigin.y) + (int)structure.structureOrigin.y;
                        int structureZ = (z - (int)structure.chunkOrigin.z) + (int)structure.structureOrigin.z;
                        (structureX, structureY, structureZ) = structure.GetCoord(structureX, structureY, structureZ);
                        int structureIndex = CustomUtility.irregularIndexFromCoord(structureX, structureY, structureZ, settings.sizeX, settings.sizeY);

                        int chunkX = x / meshSkipInc;
                        int chunkY = y / meshSkipInc;
                        int chunkZ = z / meshSkipInc;
                        int chunkIndex = CustomUtility.indexFromCoord(chunkX, chunkY, chunkZ, numPointsAxes);

                        if (data.density[structureIndex] > densityMap[chunkIndex])
                        {
                            densityMap[chunkIndex] = data.density[structureIndex];
                            materialMap[chunkIndex] = data.materials[structureIndex];
                        }

                    }
                }
            }
        }



        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(int));
        materialBuffer.SetData(materialMap);
        tempBuffers.Enqueue(materialBuffer);

        if (apply) densityGenerator.SetStructureData(pointBuffer, densityMap, IsoLevel, meshSkipInc, chunkSize, ref tempBuffers);
    }

    public ComputeBuffer AnalyzeTerrainMap(Vector3[] rawPositions, Vector2 offset, int chunkSize)
    {
        int numOfPoints = rawPositions.Length;
        Vector3[] position3D = rawPositions.Select(e => new Vector3(e.x, 0, e.z)).ToArray();
        Vector3 offset3D = new(offset.x, 0, offset.y);

        ComputeBuffer continentalDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainContinentalDetail, offset3D, MaxContinentalHeight, chunkSize, false, tempBuffers);
        ComputeBuffer pVDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainPVDetail, offset3D, MaxPVHeight, chunkSize, true, tempBuffers);
        ComputeBuffer erosionDetail = terrainGenerator.AnalyzeNoiseMap(position3D, TerrainErosionDetail, offset3D, 1, chunkSize, false, tempBuffers);

        ComputeBuffer results = terrainGenerator.CombineTerrainMaps(continentalDetail, erosionDetail, pVDetail, numOfPoints, terrainOffset, tempBuffers);

        return results;
    }

    //Get the random points for all structures
    //Calculate all the density checks for these structures(<4 hopefully)
    //For all the successful structures, plan out their space in structureDict
    //When readback, round points in dict to closest approximation
    //Time: O(n*(m^3)) where n is # of structures and m is average dimension of structures in this LOD
    public class StructureInfo
    {
        public StructureData.CheckPoint[] checks;
        public StructureData data;
        public StructureSettings settings;
        public Vector3 position;

        public StructureInfo(StructureData.CheckPoint[] checks, StructureData data, StructureSettings settings, Vector3 position)
        {
            this.checks = checks;
            this.data = data;
            this.settings = settings;
            this.position = position;
        }
    }
}
*/