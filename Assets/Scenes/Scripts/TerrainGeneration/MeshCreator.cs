using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System.Linq;
using static MapGenerator;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Containers/MeshCreator")]
public class MeshCreator : ScriptableObject
{

    public NoiseData TerrainNoise; //For underground terrain generation'
    public NoiseData SurfaceNoise; //For surface generation
    public DensityGenerator densityGenerator;
    [HideInInspector]
    public GenerationHeightData GenerationData;
    public TextureData textureData;

    ComputeBuffer pointBuffer;
    ComputeBuffer materialBuffer;
    ComputeBuffer triangleBuffer;
    ComputeBuffer triangleCountBuffer;
    ComputeBuffer vertexColor;

    Queue<ComputeBuffer> buffersToRelease = new Queue<ComputeBuffer>();


    public float[] GetDensity(float IsoLevel, int surfaceMaxDepth, Vector3 offset)
    {
        int numPointsAxes = mapChunkSize + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        float[] density = new float[numOfPoints];

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        densityGenerator.SetPoints(pointBuffer);
        buffersToRelease.Enqueue(pointBuffer);

        densityGenerator.GenerateUnderground(mapChunkSize, 1, TerrainNoise, offset);
        densityGenerator.GenerateTerrain(mapChunkSize, 1, SurfaceNoise, offset, surfaceMaxDepth, IsoLevel);

        pointBuffer.GetData(density);

        ReleaseBuffers();

        return density;
    }

    public void GetFocusedMaterials(int blockHalfLength, Vector3 center, Vector3 chunkOffset, ref float[] OUT)
    {
        List<Vector3> points = new List<Vector3>();
        List<int> pointIndices = new List<int>();

        float[][] heightCurves = LegacyGeneration.GetHeightCurves(GenerationData.Materials, (int)chunkOffset.y, mapChunkSize, 1);

        for (int x = -blockHalfLength; x <= blockHalfLength; x++)
        {
            for (int y = -blockHalfLength; y <= blockHalfLength; y++)
            {
                for (int z = -blockHalfLength; z <= blockHalfLength; z++)
                {
                    Vector3 position = center + new Vector3(x, y, z);
                    if (Mathf.Max(position.x, position.y, position.z) > mapChunkSize)
                        continue;
                    if (Mathf.Min(position.x, position.y, position.z) < 0)
                        continue;

                    int index = Utility.indexFromCoordV(position, mapChunkSize + 1);

                    if (OUT[index] != -1)
                        continue;

                    pointIndices.Add(index);
                    points.Add(position);
                }
            }
        }
        if(points.Count % 2 == 1) //ensure even so divisible by 2
        {
            points.Add(points[0]);
            pointIndices.Add(pointIndices[0]);
        }

        Vector3[] pointArray = points.ToArray();
        int pointCount = pointArray.Length/2;

        if (pointCount == 0)
            return;

        vertexColor = new ComputeBuffer(pointCount, (sizeof(float) + sizeof(uint) + sizeof(uint)));
        buffersToRelease.Enqueue(vertexColor);

        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, null, pointArray.Where((e,i)=>i%2 == 0).ToArray(), pointArray, pointCount, heightCurves, mapChunkSize, 1, chunkOffset);

        VertexColor[] vertColors = new VertexColor[pointCount];
        int[] pointIndicesArray = pointIndices.ToArray();
        vertexColor.GetData(vertColors);

        for(int i = 0; i < pointCount; i++)
        {
            OUT[pointIndicesArray[2 * i]] = vertColors[i].p1Index;
            OUT[pointIndicesArray[2 * i + 1]] = vertColors[i].p2Index;
        }

        ReleaseBuffers();
    }

    public ChunkData GenerateMapData(float IsoLevel, int surfaceMaxDepth, Vector3 offset, int LOD, float[] density = null, float [] material = null, bool hasDensity = false)
    {
        ChunkData chunk = new ChunkData();
        densityGenerator.buffersToRelease = buffersToRelease;

        chunk.meshData = new MeshData();
        chunk.grassMeshData = new MeshData();


        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = mapChunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        chunk.heightCurves = LegacyGeneration.GetHeightCurves(GenerationData.Materials, (int)(offset.y), mapChunkSize, meshSkipInc); //Only O(n*m)
     
        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.triangleBuffer = new ComputeBuffer(numOfTris, sizeof(float) * 3 * 9, ComputeBufferType.Append);
        this.triangleCountBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

        buffersToRelease.Enqueue(pointBuffer);
        buffersToRelease.Enqueue(materialBuffer);
        buffersToRelease.Enqueue(triangleBuffer);
        buffersToRelease.Enqueue(triangleCountBuffer);

        densityGenerator.SetPoints(pointBuffer);

        if (!hasDensity)
        {
            densityGenerator.GenerateUnderground(mapChunkSize, meshSkipInc, TerrainNoise, offset);
            densityGenerator.GenerateTerrain(mapChunkSize, meshSkipInc, SurfaceNoise, offset, surfaceMaxDepth, IsoLevel);
            this.materialBuffer = null;
        }
        else {
            densityGenerator.SimplifyDensity(mapChunkSize, meshSkipInc, density, pointBuffer);
            densityGenerator.SimplifyDensity(mapChunkSize, meshSkipInc, material, materialBuffer);
        }

        triangleBuffer.SetCounterValue(0);
        densityGenerator.GenerateMesh(mapChunkSize, meshSkipInc, IsoLevel, triangleBuffer);
        ComputeBuffer.CopyCount(triangleBuffer, triangleCountBuffer, 0);

        //AsyncGPUReadbackRequest request = AsyncGPUReadback.Request(triangelCountBuffer, onTrianglesRecieved);
        ////It seems to allocate the gpu to do this for a small amount of time each frame, but I need them generated much faster

        int[] data = { 0 };
        triangleCountBuffer.GetData(data);
        int numTris = data[0];

        if (numTris == 0)
        {
            ReleaseBuffers();
            return chunk;
        }
        
        TriangleConst[] tris = new TriangleConst[numTris];
        triangleBuffer.GetData(tris, 0, 0, numTris);

        Dictionary<int, int> vertDict = new Dictionary<int, int>();
        int vertCount = 0;

        for (int i = 0; i < numTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                Vector3 pInd1 = tris[i].p1[j];
                Vector3 pInd2 = tris[i].p2[j];
                if (pInd2.magnitude > pInd1.magnitude)
                    (pInd1, pInd2) = (pInd2, pInd1);

                int index = numOfPoints * Utility.indexFromCoordV(pInd1, numPointsAxes) + Utility.indexFromCoordV(pInd2, numPointsAxes);
                int vertIndex;
                if (vertDict.TryGetValue(index, out vertIndex))
                {
                    chunk.meshData.triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(index, vertCount);
                    chunk.meshData.triangles.Add(vertCount);
                    chunk.meshData.vertices.Add(tris[i].tri[j]);
                    chunk.vertexParents.Add(tris[i].p1[j]);
                    chunk.vertexParents.Add(tris[i].p2[j]);
                    vertCount++;
                }
            }
        }

        //Calculating this here reduces amount of vertices from 48^3(110k) to 7k.
        vertexColor = new ComputeBuffer(vertCount, (sizeof(float) + sizeof(uint) + sizeof(uint)));
        buffersToRelease.Enqueue(vertexColor);

        Vector3[] vertices = chunk.meshData.vertices.ToArray();
        Vector3[] vertexParents = chunk.vertexParents.ToArray();
        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, materialBuffer, vertices, vertexParents, vertCount, chunk.heightCurves, mapChunkSize, meshSkipInc, offset);

        VertexColor[] vertColors = new VertexColor[vertCount];
        vertexColor.GetData(vertColors);

        for(int i = 0; i < vertCount; i++)
        {
            chunk.meshData.colorMap.Add(new Color(vertColors[i].p1Index, vertColors[i].p2Index, vertColors[i].interp * 255));
            chunk.meshData.vertices[i] *= meshSkipInc;
        }

        chunk.grassMeshData = LegacyGeneration.getGrassMesh(textureData.MaterialDictionary, chunk.meshData);

        ReleaseBuffers();

        return chunk;
    }


    public void ReleaseBuffers()
    {
        while (buffersToRelease.Count > 0)
        {
            buffersToRelease.Dequeue().Release();
        }
    }

    public void OnDisable()
    {
        ReleaseBuffers();
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
            public Vector3 a;
            public Vector3 b;
            public Vector3 c;

            public Vector3 this[int i] //courtesy of sebastian laugue, this is pretty smart
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
    }
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