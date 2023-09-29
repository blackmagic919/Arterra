using System.Collections.Generic;
using UnityEngine;
using static MapGenerator;

public class MeshCreator : MonoBehaviour
{

    public NoiseData TerrainNoise; //For underground terrain generation
    public GenerationHeightData GenerationData;
    public NoiseData SurfaceNoise; //For surface generation
    public DensityGenerator densityGenerator;
    public TextureData texData;

    ComputeBuffer pointBuffer;
    ComputeBuffer triangelBuffer;
    ComputeBuffer triangelCountBuffer;
    ComputeBuffer vertexColor;

    ChunkData chunk;


    public ChunkData ComputeMapData(float IsoLevel, int surfaceMaxDepth, Vector3 offset, int LOD, Material terrainMat)
    {
        this.chunk = new ChunkData();

        chunk.meshData = new MeshData();
        chunk.meshData.vertices = new List<Vector3>();
        chunk.meshData.triangles = new List<int>();
        chunk.meshData.vertexParents = new List<Vector3>();
        chunk.colorMap = new Color[0];

        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = mapChunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        chunk.heightCurves = LegacyGeneration.GetHeightCurves(GenerationData.Materials, (int)(offset.y), mapChunkSize, meshSkipInc); //Only O(n*m)

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.triangelBuffer = new ComputeBuffer(numOfTris, sizeof(float) * 3 * 9, ComputeBufferType.Append);
        this.triangelCountBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

        densityGenerator.SetPoints(pointBuffer);

        densityGenerator.GenerateUnderground(mapChunkSize, meshSkipInc, TerrainNoise, offset);
        densityGenerator.GenerateTerrain(mapChunkSize, meshSkipInc, SurfaceNoise, offset, surfaceMaxDepth, IsoLevel);

        triangelBuffer.SetCounterValue(0);
        densityGenerator.GenerateMesh(mapChunkSize, meshSkipInc, IsoLevel, triangelBuffer);
        ComputeBuffer.CopyCount(triangelBuffer, triangelCountBuffer, 0);

        //AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(triangelCountBuffer, onTrianglesRecieved);
        ////It seems to allocate the gpu to do this for a small amount of time each frame, but I need them generated much faster
        
        int[] data = { 0 };
        triangelCountBuffer.GetData(data);
        int numTris = data[0];

        if (numTris == 0)
        {
            releaseBuffers();
            return chunk;
        }
        
        TriangleConst[] tris = new TriangleConst[numTris];
        triangelBuffer.GetData(tris, 0, 0, numTris);

        ComputeBuffer vertexColor = new ComputeBuffer(numTris * 3, (sizeof(float) + sizeof(uint) + sizeof(uint)));

        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, tris, numTris, chunk.heightCurves, mapChunkSize, meshSkipInc, offset);

        VertexColor[] vertColors = new VertexColor[numTris * 3];
        vertexColor.GetData(vertColors);

        chunk.meshData.vertices = new List<Vector3>(new Vector3[numTris * 3]);
        chunk.meshData.triangles = new List<int>(new int[numTris * 3]);
        chunk.meshData.vertexParents = new List<Vector3>(new Vector3[numTris * 3 * 2]);
        chunk.colorMap = new Color[numTris * 3];

        for (int i = 0; i < numTris; i++)
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
        texData.ApplyToMaterial(terrainMat, GenerationData.Materials);

        return chunk;
    }

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

    void releaseBuffers()
    {
        if(vertexColor != null) vertexColor.Release();
        if (triangelCountBuffer != null) triangelCountBuffer.Release();
        if (triangelBuffer != null) triangelBuffer.Release();
        if (pointBuffer != null) pointBuffer.Release();
    }

    //Terrain Noise -> Vertices -> Generation Noise -> Material Map -> Color Map -> Color Vertices
    public ChunkData GenerateMapData(NoiseData TerrainNoise, NoiseData SurfaceNoise, GenerationHeightData GenerationData, float IsoLevel, int surfaceMaxDepth, Vector3 offset, int LOD)
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

        chunk.colorMap = LegacyGeneration.GetPartialColors(chunk.meshData.vertices, chunk.meshData.vertexParents, chunk.materialMap);

        chunk.meshData.vertices = LegacyGeneration.RescaleVertices(chunk.meshData.vertices, meshSkipInc);

        chunk.meshData.vertexParents = LegacyGeneration.RescaleVertices(chunk.meshData.vertexParents, meshSkipInc);

        return chunk;
    }

    struct VertexColor
    {
        public uint p1Index;
        public uint p2Index;
        public float interp;
    }

    public struct TriangleConst
    {
        #pragma warning disable 649
        public Triangle tri;
        public Triangle p1;
        public Triangle p2;

        public struct Triangle
        {
            public Vector3 x;
            public Vector3 y;
            public Vector3 z;

            public Vector3 this[int i] //courtesy of sebastian laugue, this is pretty smart
            {
                get
                {
                    switch (i)
                    {
                        case 0:
                            return x;
                        case 1:
                            return y;
                        default:
                            return z;
                    }
                }
            }
        }
    }
}
