using System;
using System.Collections.Generic;
using UnityEngine;
using System.Linq;
using Utils;
using static EditorMesh;
using static BiomeInfo;
using UnityEngine.Rendering;
using Unity.Mathematics;
using static UnityEngine.Mesh;

[CreateAssetMenu(menuName = "Containers/MeshCreator")]
public class MeshCreator : ScriptableObject
{

    public NoiseData TerrainNoise; //For underground terrain generation
    public NoiseData MaterialCoarseNoise;
    public NoiseData MaterialFineNoise;


    public DensityGenerator densityGenerator;
    public MapCreator mapCreator;
    [HideInInspector]
    public BiomeGenerationData biomeData;
    public TextureData textureData;

    ComputeBuffer pointBuffer;
    ComputeBuffer materialBuffer;

    Queue<ComputeBuffer> buffersToRelease = new Queue<ComputeBuffer>();
    StructureDictionary structureDictionary = new StructureDictionary();

    const int MESH_TRIANGLES_STRIDE = (sizeof(float) * 3 * 2 + sizeof(int) * (2 + 1)) * 3;


    public void OnValidate()
    {
        densityGenerator.buffersToRelease = buffersToRelease;
    }

    public void ResetStructureDictionary()
    {
        structureDictionary = new StructureDictionary();
    }

    public float[] GetDensity(SurfaceChunk.LODMap surfaceData, Vector3 offset, Vector3 CCoord, float IsoLevel, int chunkSize)
    {
        int numPointsAxes = chunkSize + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        float[] density = new float[numOfPoints];

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));

        buffersToRelease.Enqueue(pointBuffer);

        densityGenerator.GenerateUnderground(chunkSize, 1, TerrainNoise, offset, pointBuffer);
        densityGenerator.GenerateTerrain(chunkSize, 1, surfaceData, offset, IsoLevel, pointBuffer);
        GenerateStructures(CCoord, IsoLevel, 0, chunkSize);
        pointBuffer.GetData(density);

        ReleaseBuffers();

        return density;
    }

    public int[] GetMaterials(SurfaceChunk.LODMap surfaceData, Vector3 offset, Vector3 CCoord, float IsoLevel, int chunkSize)
    {
        int numPointsAxes = chunkSize + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;

        GenerateStructures(CCoord, IsoLevel, 0, chunkSize, false); //Sets materialBuffer with structures
        this.materialBuffer = densityGenerator.GenerateMat(MaterialCoarseNoise, MaterialFineNoise, materialBuffer, surfaceData.biomeMap, chunkSize, 1, offset);

        int[] materials = new int[numOfPoints];
        materialBuffer.GetData(materials);
        ReleaseBuffers();

        return materials;
    }

    public void GenerateDensity(SurfaceChunk.LODMap surfaceData, Vector3 offset, int LOD, int chunkSize, float IsoLevel)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.materialBuffer = null;

        buffersToRelease.Enqueue(pointBuffer);

        densityGenerator.GenerateUnderground(chunkSize, meshSkipInc, TerrainNoise, offset, pointBuffer);
        densityGenerator.GenerateTerrain(chunkSize, meshSkipInc, surfaceData, offset, IsoLevel, pointBuffer);
    }

    public void SetMapInfo(int LOD, int chunkSize, float[] density = null, int[] material = null)
    {
        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfPoints = numPointsAxes * numPointsAxes * numPointsAxes;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        this.pointBuffer = new ComputeBuffer(numOfPoints, sizeof(float));
        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(int));

        buffersToRelease.Enqueue(pointBuffer);
        buffersToRelease.Enqueue(materialBuffer);

        densityGenerator.SimplifyDensity(chunkSize, meshSkipInc, density, pointBuffer);
        densityGenerator.SimplifyMaterials(chunkSize, meshSkipInc, material, materialBuffer);
    }


    //Get the random points for all structures
    //Calculate all the density checks for these structures(<4 hopefully)
    //For all the successful structures, plan out their space in structureDict
    //When readback, round points in dict to closest approximation
    //Time: O(n*(m^3)) where n is # of structures and m is average dimension of structures in this LOD
    
    public void PlanStructures(SurfaceChunk.LODMap surfaceData, Vector3 chunkCoord, Vector3 offset, int chunkSize, float IsoLevel)
    {
        int chunkSeed = ((int)chunkCoord.x * 2 + (int)chunkCoord.y * 3 + (int)chunkCoord.z * 5);
        System.Random prng = new System.Random(chunkSeed);

        Queue<StructureInfo> structures = new Queue<StructureInfo>();
        List<Vector3> checkPositions = new List<Vector3>();
        
        for (int _ = 0; _ < biomeData.StructureChecksPerChunk; _++)
        {
            //If 0, nearby chunk edges aren't updated
            int x; int y; int z;
            (x, y, z) = (prng.Next(1, chunkSize+1), prng.Next(1, chunkSize+1), prng.Next(1, chunkSize+1));
            int biome = surfaceData.biomeMap[CustomUtility.indexFromCoord2D(x, z, chunkSize + 1)];
            if (biomeData.biomes[biome].info.structurePointDensity < prng.NextDouble())
                continue;

            BiomeInfo info = biomeData.biomes[biome].info;
            TerrainStructure structure =  info.GetStructure((float)prng.NextDouble());

            float actualHeight = y - chunkSize / 2 + offset.y;
            float heightDensity = structure.VerticalPreference.GetDensity(actualHeight);

            if (heightDensity < prng.NextDouble())
                continue;

            Vector3 position = new (x, y, z);
            for (int i = 0; i < structure.structureData.checks.Length; i++)
            {
                checkPositions.Add(position + structure.structureData.checks[i].position);
            }

            StructureInfo newStructureInfo = new StructureInfo(structure.structureData.checks, structure.structureData, position);
            structures.Enqueue(newStructureInfo);
        }

        Vector3[] rawPositions = checkPositions.ToArray();
        float heightOffest = offset.y - (chunkSize/2);
        float[] posY = rawPositions.Select((e) => e.y + heightOffest).ToArray();
        Vector2 offset2D = new Vector2(offset.x, offset.z);

        if (rawPositions.Length <= 0)
            return;

        ComputeBuffer baseDensity = densityGenerator.AnalyzeBase(rawPositions, TerrainNoise, offset, chunkSize);
        ComputeBuffer terrainHeights = mapCreator.AnalyzeTerrainMap(rawPositions, offset2D, chunkSize);
        ComputeBuffer squashHeights = mapCreator.AnalyzeSquashMap(rawPositions, offset2D, chunkSize);

        float[] densities = densityGenerator.AnalyzeTerrain(posY, baseDensity, terrainHeights, squashHeights);
        mapCreator.ReleaseBuffers();

        int checkCount = 0;
        int structureCount = structures.Count;
        for (int _ = 0; _ < structureCount; _++)
        {
            StructureInfo structure = structures.Dequeue();
            bool passed = true;
            for (int i = 0; i < structure.checks.Length; i++)
            {
                bool isUnderGround = densities[checkCount] >= IsoLevel;
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

            int[] transformAxis = CustomUtility.RotateAxis(theta90, phi90);

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
                        int structureIndex = CustomUtility.irregularIndexFromCoord(structureX, structureY, structureZ, data.sizeX, data.sizeY);

                        int chunkX = x / meshSkipInc;
                        int chunkY = y / meshSkipInc;
                        int chunkZ = z / meshSkipInc;
                        int chunkIndex = CustomUtility.indexFromCoord(chunkX, chunkY, chunkZ, numPointsAxes);

                        if(structure.structure.density[structureIndex] > densityMap[chunkIndex])
                        {
                            densityMap[chunkIndex] = structure.structure.density[structureIndex];
                            materialMap[chunkIndex] = structure.structure.materials[structureIndex];
                        }
                        
                    }
                }
            }
        }

        this.materialBuffer = new ComputeBuffer(numOfPoints, sizeof(int));
        materialBuffer.SetData(materialMap);
        buffersToRelease.Enqueue(materialBuffer);

        if (apply) densityGenerator.SetStructureData(pointBuffer, densityMap, IsoLevel, meshSkipInc, chunkSize);
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

    public void GenerateMaterials(SurfaceChunk.LODMap surfaceData, Vector3 offset, int LOD, int chunkSize)
    {
        int meshSkipInc = meshSkipTable[LOD];
        this.materialBuffer = densityGenerator.GenerateMat(MaterialCoarseNoise, MaterialFineNoise, materialBuffer, surfaceData.biomeMap, chunkSize, meshSkipInc, offset);
    }

    public ChunkBuffers GenerateMapData(float IsoLevel, int LOD, int chunkSize)
    {
        ChunkBuffers chunk = new ChunkBuffers();

        densityGenerator.buffersToRelease = buffersToRelease;

        int meshSkipInc = meshSkipTable[LOD];
        int numPointsAxes = chunkSize / meshSkipInc + 1;
        int numOfTris = (numPointsAxes - 1) * (numPointsAxes - 1) * (numPointsAxes - 1) * 5;
        /*(numPoints-1)^3 cubes. A cube can have a maximum of 5 triangles. Though this is correct,
        it is usually way above the amount of actual triangles that exist(as mesh gets larger)*/

        chunk.sourceMeshBuffer = new ComputeBuffer(numOfTris, MESH_TRIANGLES_STRIDE, ComputeBufferType.Append);
        chunk.argsBuffer = new ComputeBuffer(1, sizeof(int) * 4, ComputeBufferType.IndirectArguments);
        chunk.argsBuffer.SetData(new int[] { 0, 1, 0, 0 });
        chunk.released = false;

        chunk.sourceMeshBuffer.SetCounterValue(0);
        densityGenerator.GenerateMesh(chunkSize, meshSkipInc, IsoLevel, materialBuffer, chunk.sourceMeshBuffer, pointBuffer);
        ComputeBuffer.CopyCount(chunk.sourceMeshBuffer, chunk.argsBuffer, 0);

        densityGenerator.ConvertTriCountToVert(chunk.argsBuffer);

        return chunk;
        /*
        int[] data = { 0, 1, 0, 0 };
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
                if (vertDict.TryGetValue(tris[i][j].id, out int vertIndex))
                {
                    chunk.meshData.triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(tris[i][j].id, vertCount);
                    chunk.meshData.triangles.Add(vertCount);
                    chunk.meshData.vertices.Add(tris[i][j].tri * meshSkipInc);
                    chunk.meshData.normals.Add(tris[i][j].norm);
                    chunk.meshData.colorMap.Add(new Color(tris[i][j].material, 0, 0));
                    vertCount++;
                }
            }
        }
        */
    }
    /*
    public Dictionary<int, Mesh> CreateSpecialMeshes(SpecialShaderData[] specialShaderData, ChunkData terrainData)
    {
        Dictionary<int, Mesh> meshes = new Dictionary<int, Mesh>();
        for(int i = 0; i < specialShaderData.Length; i++)
        {
            MeshData meshData = GetSpecialMesh(specialShaderData[i], terrainData.meshData);
            meshes.TryAdd(specialShaderData[i].materialIndex, ChunkData.GenerateMesh(meshData));
        }
        return meshes;
    }


    public static MeshData GetSpecialMesh(SpecialShaderData shaderData, MeshData meshData)
    {
        MeshData subMesh = new MeshData();

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

    public void BeginMeshReadback(ChunkBuffers sourceBuffers, Action<Mesh> callback)
    {
        AsyncGPUReadback.Request(sourceBuffers.argsBuffer, ret => onTriCountRecieved(ret, sourceBuffers, callback));
    }

    
    void onTriCountRecieved(AsyncGPUReadbackRequest request, ChunkBuffers sourceBuffers, Action<Mesh> callback)
    {
        if (sourceBuffers.released)
            return;

        int numTris = request.GetData<int>()[0] / 3;

        if (numTris == 0)
            return;

        AsyncGPUReadback.Request(sourceBuffers.sourceMeshBuffer, size:numTris * MESH_TRIANGLES_STRIDE, offset:0, ret => onMeshDataRecieved(ret, sourceBuffers, callback));
    }

    void onMeshDataRecieved(AsyncGPUReadbackRequest request, ChunkBuffers sourceBuffers, Action<Mesh> callback)
    {
        if (sourceBuffers.released)
            return;

        TriangleConst[] meshData = request.GetData<TriangleConst>().ToArray();
        int numTris = meshData.Length;

        List<int> triangles = new List<int>();
        List<Vector3> vertices = new List<Vector3>();
        List<Vector3> normals = new List<Vector3>();
        List<Color> colorMap = new List<Color>();

        Dictionary<int2, int> vertDict = new Dictionary<int2, int>();
        int vertCount = 0;

        for (int i = 0; i < numTris; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                if (vertDict.TryGetValue(meshData[i][j].id, out int vertIndex))
                {
                    triangles.Add(vertIndex);
                }
                else
                {
                    vertDict.Add(meshData[i][j].id, vertCount);
                    triangles.Add(vertCount);
                    vertices.Add(meshData[i][j].tri);
                    normals.Add(meshData[i][j].norm);
                    colorMap.Add(new Color(meshData[i][j].material, 0, 0));
                    vertCount++;
                }
            }
        }

        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.normals = normals.ToArray();
        mesh.colors = colorMap.ToArray();
        mesh.triangles = triangles.ToArray();

        callback(mesh);
    }

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
    }*/



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
}

public class ChunkBuffers
{
    public bool released;
    public ComputeBuffer sourceMeshBuffer;
    public ComputeBuffer argsBuffer;

    ~ChunkBuffers()
    {
        Release();
    }

    public void Release()
    {
        released = true;
        sourceMeshBuffer?.Release();
        argsBuffer?.Release();
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