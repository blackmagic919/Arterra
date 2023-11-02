using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using static MapGenerator;
using static GenerationHeightData;
using Unity.Mathematics;

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
    StructureDictionary structureDictionary = new StructureDictionary();


    public void OnValidate()
    {
        densityGenerator.buffersToRelease = buffersToRelease;
    }

    public void ResetStructureDictionary()
    {
        structureDictionary = new StructureDictionary();
    }

    public float[] GetDensity(float IsoLevel, int surfaceMaxDepth, Vector3 offset, Vector3 CCoord)
    {
        int numPointsAxes = mapChunkSize + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        float[] density = new float[numOfPoints];

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));

        buffersToRelease.Enqueue(pointBuffer);

        densityGenerator.GenerateUnderground(mapChunkSize, 1, TerrainNoise, offset, pointBuffer);
        densityGenerator.GenerateTerrain(mapChunkSize, 1, SurfaceNoise, offset, surfaceMaxDepth, IsoLevel, pointBuffer);
        GenerateStructures(CCoord, IsoLevel, 0, mapChunkSize);
        pointBuffer.GetData(density);

        ReleaseBuffers();

        return density;
    }

    public void GetFocusedMaterials(int blockHalfLength, Vector3 center, Vector3 offset, ref float[] OUT)
    {
        List<Vector3> points = new List<Vector3>();
        List<int> pointIndices = new List<int>();

        float[][] heightCurves = BiomeHeightMap.GetHeightCurves(GenerationData.Materials, (int)offset.y, mapChunkSize, 1);

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

        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, null, pointArray.Where((e,i)=>i%2 == 0).ToArray(), pointArray, pointCount, heightCurves, mapChunkSize, 1, offset);

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

    public void GenerateDensity(Vector3 offset, int LOD, int surfaceMaxDepth, int chunkSize, float IsoLevel)
    {
        ChunkData chunk = new ChunkData();

        chunk.meshData = new MeshData();


        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/
     
        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.materialBuffer = null;

        buffersToRelease.Enqueue(pointBuffer);

        densityGenerator.GenerateUnderground(chunkSize, meshSkipInc, TerrainNoise, offset, pointBuffer);
        densityGenerator.GenerateTerrain(chunkSize, meshSkipInc, SurfaceNoise, offset, surfaceMaxDepth, IsoLevel, pointBuffer);
    }

    public void SetDensity(int LOD, int chunkSize, float[] density = null, float[] material = null)
    {
        ChunkData chunk = new ChunkData();

        chunk.meshData = new MeshData();

        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(float));

        buffersToRelease.Enqueue(pointBuffer);
        buffersToRelease.Enqueue(materialBuffer);

        densityGenerator.SimplifyDensity(chunkSize, meshSkipInc, density, pointBuffer);
        densityGenerator.SimplifyDensity(chunkSize, meshSkipInc, material, materialBuffer);
    }

    //Get the random points for all structures
    //Calculate all the density checks for these structures(<4 hopefully)
    //For all the successful structures, plan out their space in structureDict
    //When readback, round points in dict to closest approximation
    //Time: O(n*(m^3)) where n is # of structures and m is average dimension of structures in this LOD
    
    public void PlanStructures(Vector3 chunkCoord, Vector3 offset, int chunkSize, int surfaceMaxDepth, float IsoLevel)
    {
        List<TerrainStructure> structureData = GenerationData.Structures;

        int chunkSeed = GenerationData.seed + ((int)chunkCoord.x * 2 + (int)chunkCoord.y * 3 + (int)chunkCoord.z * 5);
        System.Random prng = new System.Random(chunkSeed);

        Queue<StructureInfo> structures = new Queue<StructureInfo>();

        List<Vector3> checkPositions = new List<Vector3>();

        foreach (TerrainStructure structure in structureData)
        {
            float[] heightPref = BiomeHeightMap.CalculateHeightCurve(structure.VerticalPreference, (int)offset.y, chunkSize, 1);

            int count = Mathf.FloorToInt(structure.baseFrequencyPerChunk + ((float)prng.Next(0, 10000) / 10000f)); //For percentages

            for (int _ = 0; _ < count; _++)
            {
                Vector3 position = new(prng.Next(0, chunkSize), prng.Next(0, chunkSize), prng.Next(0, chunkSize));

                if (prng.NextDouble() > heightPref[(int)position.y])
                    continue;

                List<StructureData.CheckPoint> checks = new List<StructureData.CheckPoint>();
                for (int i = 0; i < structure.structureData.checks.Length; i++)
                {
                    checks.Add(structure.structureData.checks[i]);
                    checkPositions.Add(position + structure.structureData.checks[i].position);
                }

                StructureInfo newStructureInfo = new StructureInfo(checks.ToArray(), structure.structureData, position);
                structures.Enqueue(newStructureInfo);
            }
        }

        Vector3[] rawPositions = checkPositions.ToArray();

        if (rawPositions.Length <= 0)
            return;

        float[] terrainValues = densityGenerator.GetPointDensity(rawPositions, TerrainNoise, SurfaceNoise, offset, IsoLevel, surfaceMaxDepth, chunkSize);
        
        int checkCount = 0;
        int structureCount = structures.Count;
        for (int _ = 0; _ < structureCount; _++)
        {
            StructureInfo structure = structures.Dequeue();
            bool passed = true;
            for (int i = 0; i < structure.checks.Length; i++)
            {
                bool isUnderGround = terrainValues[checkCount] >= IsoLevel;
                passed = passed && (isUnderGround == structure.checks[i].isUnderGround);
                checkCount++;
            }

            if (passed)
                structures.Enqueue(structure);
        }

        while (structures.Count > 0)
        {
            StructureInfo structureInfo = structures.Dequeue();
            StructureData structure = structureInfo.structure;

            int theta90 = 0;
            int phi90 = 0;

            if (structure.randThetaRot)
                theta90 = prng.Next(0, 4); //0, pi/2, pi, 3pi/2
            if (structure.randPhiRot)
                phi90 = prng.Next(0, 3); //0, pi/2, pi

            int[] transformAxis = Utility.RotateAxis(theta90, phi90);

            float[] originalSize = { structure.sizeX, structure.sizeY, structure.sizeZ };
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

                        structureDictionary.Add(chunkCoord + deltaCC, new(structure, structureOrigin, chunkOrigin, transformAxis));
                    }
                }
            }
            
        }
    }

    public void GenerateStructures(Vector3 CCoord, float IsoLevel, int LOD, int chunkSize)
    {
        GenerateStructures(CCoord, IsoLevel, LOD, chunkSize, out float[] materialMap, apply: true);
    }


    public void GenerateStructures(Vector3 CCoord, float IsoLevel, int LOD, int chunkSize, out float[] materialMap, bool apply = false)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        float[] densityMap = new float[numOfPoints];
        materialMap = Enumerable.Repeat(-1.0f, numOfPoints).ToArray();

        List<StructureDictionary.StructureSection> structures = structureDictionary.GetChunk(CCoord);
        foreach(StructureDictionary.StructureSection structure in structures)
        {
            StructureData data = structure.structure;
            if (LOD > data.maximumLOD)
                continue;


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
                        int structureIndex = Utility.irregularIndexFromCoord(structureX, structureY, structureZ, data.sizeX, data.sizeY);

                        int chunkX = x / meshSkipInc;
                        int chunkY = y / meshSkipInc;
                        int chunkZ = z / meshSkipInc;
                        int chunkIndex = Utility.indexFromCoord(chunkX, chunkY, chunkZ, numPointsAxes);

                        if(structure.structure.density[structureIndex] > densityMap[chunkIndex])
                        {
                            densityMap[chunkIndex] = structure.structure.density[structureIndex];
                            materialMap[chunkIndex] = structure.structure.materials[structureIndex];
                        }
                        
                    }
                }
            }
        }
        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        materialBuffer.SetData(materialMap);
        buffersToRelease.Enqueue(materialBuffer);

        if(apply) densityGenerator.SetStructureData(pointBuffer, densityMap, IsoLevel, meshSkipInc, chunkSize);
    }

    public class StructureInfo
    {
        public StructureData.CheckPoint[] checks;
        public StructureData structure;
        public Vector3 position;

        public StructureInfo(StructureData.CheckPoint[] checks, StructureData structure, Vector3 position)
        {
            this.checks = checks;
            this.structure = structure;
            this.position = position;
        }
    }

    public ChunkData GenerateMapData(float IsoLevel, Vector3 offset, int LOD, int chunkSize)
    {
        ChunkData chunk = new ChunkData();
        densityGenerator.buffersToRelease = buffersToRelease;

        chunk.meshData = new MeshData();

        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        chunk.heightCurves = BiomeHeightMap.GetHeightCurves(GenerationData.Materials, (int)(offset.y), chunkSize, meshSkipInc); //Only O(n*m)
     
        this.triangleBuffer = new ComputeBuffer(numOfTris, sizeof(float) * 3 * 9, ComputeBufferType.Append);
        this.triangleCountBuffer = new ComputeBuffer(4, sizeof(int), ComputeBufferType.IndirectArguments);

        buffersToRelease.Enqueue(triangleBuffer);
        buffersToRelease.Enqueue(triangleCountBuffer);

        triangleBuffer.SetCounterValue(0);
        densityGenerator.GenerateMesh(chunkSize, meshSkipInc, IsoLevel, triangleBuffer, pointBuffer);
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
                Vector3 pInd1 = tris[i][j].p1;
                Vector3 pInd2 = tris[i][j].p2;
                //Before we would need to make sure p1Ind1 is greater, but now as the underground point is always first, there is unique ids.

                int index = numOfPoints * Utility.indexFromCoordV(pInd1, numPointsAxes) + Utility.indexFromCoordV(pInd2, numPointsAxes);
                if (vertDict.TryGetValue(index, out int vertIndex))
                {
                    chunk.meshData.triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(index, vertCount);
                    chunk.meshData.triangles.Add(vertCount);
                    chunk.meshData.vertices.Add(tris[i][j].tri);
                    chunk.vertexParents.Add(tris[i][j].p1);
                    chunk.vertexParents.Add(tris[i][j].p2);
                    vertCount++;
                }
            }
        }

        //Calculating this here reduces amount of vertices from 48^3(110k) to 7k.
        vertexColor = new ComputeBuffer(vertCount, (sizeof(float) + sizeof(uint) + sizeof(uint)));
        buffersToRelease.Enqueue(vertexColor);

        Vector3[] vertices = chunk.meshData.vertices.ToArray();
        Vector3[] vertexParents = chunk.vertexParents.ToArray();
        densityGenerator.GenerateMat(GenerationData.Materials, vertexColor, materialBuffer, vertices, vertexParents, vertCount, chunk.heightCurves, chunkSize, meshSkipInc, offset);

        VertexColor[] vertColors = new VertexColor[vertCount];
        vertexColor.GetData(vertColors);

        for(int i = 0; i < vertCount; i++)
        {
            chunk.meshData.colorMap.Add(new Color(vertColors[i].p1Index, 0, 1));
            chunk.meshData.vertices[i] *= meshSkipInc;
        }

        return chunk;
    }

    public Dictionary<int, Mesh> CreateSpecialMeshes(SpecialShaderData[] specialShaderData, ChunkData terrainData)
    {
        Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        for(int i = 0; i < specialShaderData.Length; i++)
        {
            int material = specialShaderData[i].materialIndex;
            bool replaceOriginal = specialShaderData[i].replaceOriginal;
            MeshData meshData = LegacyGeneration.GetSpecialMesh(material, replaceOriginal, terrainData.meshData);
            meshes.TryAdd(material, terrainData.GenerateMesh(meshData));
        }
        return meshes;
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
        public Point a;
        public Point b;
        public Point c;

        public struct Point
        {
            public Vector3 tri;
            public Vector3 p1;
            public Vector3 p2;

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