using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using static EndlessTerrain;
using static DensityGenerator;
using Unity.Collections;


[CreateAssetMenu(menuName = "Containers/Mesh Creator Settings")]
public class MeshCreatorSettings : ScriptableObject{
    [Header("Material Generation Noise")]
    public int CoarseMaterialNoise;
    public int FineMaterialNoise;

    [Header("Underground Generation Noise")]
    public int CoarseTerrainNoise; //For underground terrain generation
    public int FineTerrainNoise;
    public BiomeGenerationData biomeData;
    
    [Header("Water Generation Data")]
    public float waterHeight;
    public int waterMat;

    [Header("Dependencies")]
    public MemoryBufferSettings structureMemory;
    public TextureData textureData;
}
public class MeshCreator
{
    MeshCreatorSettings settings; //Funny it's not used...

    Queue<ComputeBuffer> tempBuffers = new Queue<ComputeBuffer>();

    public MeshCreator(MeshCreatorSettings settings){
        this.settings = settings;
    }

    public CPUDensityManager.MapData[] GetChunkInfo(StructureCreator structCreator, SurfaceChunk.SurfData surfaceData, Vector3 offset, float IsoLevel, int chunkSize)
    {
        int numPointsAxes = chunkSize + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        GenerateBaseChunk(surfaceData, offset, 0, chunkSize, IsoLevel);
        structCreator.GenerateStrucutresGPU(chunkSize, 0, IsoLevel);

        CPUDensityManager.MapData[] chunkMap = new CPUDensityManager.MapData[numOfPoints];

        UtilityBuffers.GenerationBuffer.GetData(chunkMap);
        ReleaseTempBuffers();

        return chunkMap;
    }

    public void GenerateBaseChunk(SurfaceChunk.SurfData surfaceData, Vector3 offset, int LOD, int chunkSize, float IsoLevel)
    {
        int meshSkipInc = meshSkipTable[LOD];
        GenerateBaseData(surfaceData, IsoLevel, chunkSize, meshSkipInc, offset);
    }

    public void SetMapInfo(int LOD, int chunkSize, int offset, CPUDensityManager.MapData[] chunkData){
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        UtilityBuffers.TransferBuffer.SetData(chunkData, offset, 0, numPoints);
    }
    public void SetMapInfo(int LOD, int chunkSize, int offset, ref NativeArray<CPUDensityManager.MapData> chunkData)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc;
        int numPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        

        UtilityBuffers.TransferBuffer.SetData(chunkData, offset, 0, numPoints);
    }

    struct structurInfo
    {
        public float3 structurePos;
        public uint structureInd;
        public uint2 rotation;
    }


    public void GenerateMapData(int3 CCoord, float IsoLevel, int LOD, int chunkSize)
    {
        int meshSkipInc = meshSkipTable[LOD];
        GenerateMesh(CCoord, chunkSize, meshSkipInc, IsoLevel);
    }

    public void ReleaseTempBuffers()
    {
        while (tempBuffers.Count > 0)
        {
            tempBuffers.Dequeue()?.Release();
        }
    }
}

/*
public ComputeBuffer GenerateDensity(SurfaceChunk.SurfData surfaceData, Vector3 offset, int LOD, int chunkSize, float IsoLevel)
{
    int meshSkipInc = meshSkipTable[LOD];
    ComputeBuffer density = GenerateTerrain(chunkSize, meshSkipInc, surfaceData, settings.CoarseTerrainNoise, settings.FineTerrainNoise, offset, IsoLevel, ref tempBuffers);

    return density;
}*/

/* old Direct Readback logic
    public MeshInfo ReadBackMesh(ComputeBuffer sourceMeshBuffer)
    {
        MeshInfo chunk = new MeshInfo();

        ComputeBuffer argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        argsBuffer.SetData(new int[] { 0, 1, 0, 0 });

        ComputeBuffer.CopyCount(sourceMeshBuffer, argsBuffer, 0);
        tempBuffers.Enqueue(argsBuffer);

        int[] data = { 0, 1, 0, 0 };
        argsBuffer.GetData(data);
        int numTris = data[0];
        
        if (numTris == 0)
        {
            ReleaseTempBuffers();
            return chunk;
        }
        
        TriangleConst[] tris = new TriangleConst[numTris];
        sourceMeshBuffer.GetData(tris, 0, 0, numTris);
        
        Dictionary<int2, int> vertDict = new Dictionary<int2, int>();
        int vertCount = 0;

        for (int i = 0; i < numTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (vertDict.TryGetValue(tris[i][j].id, out int vertIndex))
                {
                    chunk.triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(tris[i][j].id, vertCount);
                    chunk.triangles.Add(vertCount);
                    chunk.vertices.Add(tris[i][j].tri);
                    chunk.normals.Add(tris[i][j].norm);
                    chunk.colorMap.Add(new Color(tris[i][j].material, 0, 0));
                    vertCount++;
                }
            }
        }
        return chunk;

    }
    
    public Dictionary<int, Mesh> CreateSpecialMeshes(SpecialShaderData[] specialShaderData, MeshInfo terrainData)
    {
        Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        for(int i = 0; i < specialShaderData.Length; i++)
        {
            MeshInfo meshData = GetSpecialMesh(specialShaderData[i], terrainData);
            meshes.TryAdd(specialShaderData[i].materialIndex, ChunkData.GenerateMesh(meshData));
        }
        return meshes;
    }


    public static MeshInfo GetSpecialMesh(SpecialShaderData shaderData, MeshInfo meshData)
    {
        MeshInfo subMesh = new MeshInfo();

        Dictionary<int, int> vertDict = new Dictionary<int, int>();
        int vertCount = 0;
        for (int t = 0; t < meshData.triangles.Count; t += 3)
        {
            float percentMaterial = 0;
            for (int i = 0; i < 3; i++)
            {
                int vertexInd = meshData.triangles[t + i];

                int material = (int)meshData.colorMap[vertexInd].r;

                if (material == shaderData.materialIndex)
                    percentMaterial++;
            }
            percentMaterial /= 3;

            if (percentMaterial < shaderData.cuttoffThreshold)
                continue;

            for (int i = 0; i < 3; i++)
            {
                int index = meshData.triangles[t + i];
                int vertIndex;
                if (vertDict.TryGetValue(index, out vertIndex))
                {
                    subMesh.triangles.Add(vertIndex);
                }
                else
                {
                    if (shaderData.replaceOriginal)
                        meshData.colorMap[index] = new Color(meshData.colorMap[index].r, meshData.colorMap[index].g, meshData.colorMap[index].b, 0f);

                    vertDict.Add(index, vertCount);
                    subMesh.vertices.Add(meshData.vertices[index]);
                    subMesh.normals.Add(meshData.normals[index]);
                    subMesh.triangles.Add(vertCount);
                    subMesh.colorMap.Add(new Color(0, 0, 0, percentMaterial));
                    vertCount++;
                }
            }
        }

        return subMesh;
    }*/


    /*y     
    * ^
    * |     .--------.      z
    * |    /|  5    /|     /\
    * |   / |   1  / |     /
    * |  .--+-----.  |    /
    * |  |4 |     |2 |   /
    * |  |  .--3--+--.  /
    * |  | /    6 | /  /
    * | xyz_______./  /
    * +--------------->x
    */
    /*
    public ComputeBuffer GetBorderPlanes(int chunkSize, int meshSkipInc, Vector3 offset)
    {
        Vector2 offset2D = new Vector2(offset.x, offset.z);
        int[] borderAxis = new int[6] { 2, 0, 2, 0, 1, 1 };
        int[] borderChunk = new int[6] { 1, 1, -1, -1, 1, -1 };
        int[] borderPlane = new int[6] { 1, 1, chunkSize - 1, chunkSize - 1, 1, chunkSize - 1 };

        for(int i = 0; i < 6; i++)
        {
            for()
        }

        ComputeBuffer baseDensity = densityGenerator.AnalyzeBase(rawPositions, TerrainNoise, offset, chunkSize);
        ComputeBuffer terrainHeights = mapCreator.AnalyzeTerrainMap(rawPositions, offset2D, chunkSize);
        ComputeBuffer squashHeights = mapCreator.AnalyzeSquashMap(rawPositions, offset2D, chunkSize);

        float[] densities = densityGenerator.AnalyzeTerrain(posY, baseDensity, terrainHeights, squashHeights);
    }

    public void GenerateStructures(Vector3 CCoord, float IsoLevel, int LOD, int chunkSize)
    {
        GenerateStructures(CCoord, IsoLevel, LOD, chunkSize, out float[] _, apply: true);
    }
    public struct TriangleConst
    {
        #pragma warning disable 649
        public Point a;
        public Point b;
        public Point c;

        public struct Point
        {
            public Vector3 tri;
            public Vector3 norm;
            public int2 id;
            public int material;
        }

        public Point this[int i] //courtesy of sebastian laugue, this is pretty smart
        {
            get
            {
                switch (i)
                {
                    case 0:
                        return a;
                    case 1:
                        return b;
                    default:
                        return c;
                }
            }
        }
    }
*/


/* old async logic that's too slow

    public void onTrianglesRecieved(AsyncGPUReadbackRequest request)
    {
        int[] data = request.GetData<int>().ToArray();
        numRealTris = data[0];
        //  Debug.Log(numRealTris);

        if (numRealTris == 0)
        {
            callback(chunk);
            return;
        }
            

        tris = new TriangleConst[numRealTris];
        triangelBuffer.GetData(tris, 0, 0, numRealTris); //Will already have calculated since count is binded

        this.vertexColor = new ComputeBuffer(numRealTris * 3, (sizeof(float) + sizeof(uint) + sizeof(uint)));
        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, tris, numRealTris, chunk.heightCurves, mapChunkSize, meshSkipInc, offset);

        AsyncGPUReadbackRequest newRequest = AsyncGPUReadback.Request(vertexColor, onColorsRecieved);
    }

    public void onColorsRecieved(AsyncGPUReadbackRequest request)
    {
        VertexColor[] vertColors = request.GetData<VertexColor>().ToArray();

        chunk.meshData.vertices = new List<Vector3>(new Vector3[numRealTris * 3]);
        chunk.meshData.triangles = new List<int>(new int[numRealTris * 3]);
        chunk.meshData.vertexParents = new List<Vector3>(new Vector3[numRealTris * 3 * 2]);
        chunk.colorMap = new Color[numRealTris * 3];

        for (int i = 0; i < numRealTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                chunk.meshData.triangles[i * 3 + j] = i * 3 + j;
                chunk.meshData.vertices[i * 3 + j] = tris[i].tri[j] * meshSkipInc;
                chunk.colorMap[i * 3 + j] = new Color(vertColors[i * 3 + j].p1Index, vertColors[i * 3 + j].p2Index, vertColors[i * 3 + j].interp * 255);
                chunk.meshData.vertexParents[2 * (i * 3 + j)] = tris[i].p1[j];
                chunk.meshData.vertexParents[2 * (i * 3 + j) + 1] = tris[i].p2[j];
            }
        }
        
        releaseBuffers();

        TextureData.ApplyToMaterial(terrainMat, GenerationData.Materials);

        callback(chunk);
    }*/

//Terrain Noise -> Vertices -> Generation Noise -> Material Map -> Color Map -> Color Vertices
/*public ChunkData GenerateMapData(NoiseData TerrainNoise, NoiseData SurfaceNoise, GenerationHeightData GenerationData, float IsoLevel, int surfaceMaxDepth, Vector3 offset, int LOD)
{
    int meshSkipInc = ((LOD == 0) ? 1 : LOD * 2);

    ChunkData chunk = new ChunkData();

    chunk.undergroundNoise = Noise.GenerateNoiseMap(TerrainNoise, mapChunkSize, mapChunkSize, mapChunkSize, offset, meshSkipInc); //This is so ineffecient cause it only matters when y pos is < depth but I'm lazy somebody fix this

    chunk.surfaceNoise = Noise.GenerateNoiseMap(SurfaceNoise, mapChunkSize, mapChunkSize, mapChunkSize, offset, meshSkipInc);

    chunk.terrainNoiseMap = LegacyGeneration.terrainBelowGround(mapChunkSize, mapChunkSize, mapChunkSize, chunk.surfaceNoise, chunk.undergroundNoise, surfaceMaxDepth, IsoLevel, offset, meshSkipInc);

    chunk.meshData = MeshGenerator.GenerateMesh(chunk.terrainNoiseMap, IsoLevel);

    chunk.partialGenerationNoises = LegacyGeneration.GetPartialGenerationNoises(GenerationData, offset, chunk.meshData.vertexParents);

    chunk.heightCurves = LegacyGeneration.GetHeightCurves(GenerationData.Materials, (int)(offset.y), mapChunkSize, meshSkipInc);

    chunk.materialMap = LegacyGeneration.GetPartialMaterialMap(chunk.partialGenerationNoises, GenerationData, chunk.meshData.vertexParents, meshSkipInc, chunk.heightCurves);

    chunk.meshData.colorMap = new List<Color>(LegacyGeneration.GetPartialColors(chunk.meshData.vertices, chunk.meshData.vertexParents, chunk.materialMap));

    chunk.meshData.vertices = LegacyGeneration.RescaleVertices(chunk.meshData.vertices, meshSkipInc);

    chunk.meshData.vertexParents = LegacyGeneration.RescaleVertices(chunk.meshData.vertexParents, meshSkipInc);

    return chunk;
}*/
