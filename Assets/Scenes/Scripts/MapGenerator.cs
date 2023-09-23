using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using static GenerationHeightData;
using UnityEngine.AI;
using UnityEngine.TerrainUtils;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    public MeshFilter meshFilter;

    [HideInInspector]
    public const int mapChunkSize = 48;
    public Vector3 EditorOffset;
    [Range(0, 4)]
    public int EditorLoD;

    Queue<ThreadInfo> ThreadInfoQueue = new Queue<ThreadInfo>();

    public bool editorAutoUpdate;

    //[HideInInspector]
    //public VoxelMesh voxels = new VoxelMesh(); Voxel mesh crashes the game atm someone can fix this if they want


    public void GenerateMapInEditor()
    {

        if (drawMode == DrawMode.Voxel)
        {
            //voxels.GenerateVoxelMesh(terrainNoiseMap); 
        }
        else
        {
            EndlessTerrain settings = transform.GetComponent<EndlessTerrain>();
            ChunkData newMap = GenerateMapData(settings.TerrainNoise, settings.SurfaceNoise, settings.GenerationData, settings.IsoLevel, settings.surfaceMaxDepth, EditorOffset, EditorLoD);
            meshFilter.sharedMesh = newMap.GenerateMesh();
            TextureData.ApplyToMaterial(settings.mapMaterial, settings.GenerationData.Materials);

        }
    }

    //Terrain Noise -> Vertices -> Generation Noise -> Material Map -> Color Map -> Color Vertices
    public static ChunkData GenerateMapData(NoiseData TerrainNoise, NoiseData SurfaceNoise, GenerationHeightData GenerationData, float IsoLevel, int surfaceMaxDepth, Vector3 offset, int LOD)
    {
        int meshSkipInc = ((LOD == 0) ? 1 : LOD * 2);
        ChunkData chunk = new ChunkData();

        chunk.undergroundNoise = Noise.GenerateNoiseMap(TerrainNoise, mapChunkSize, mapChunkSize, mapChunkSize, offset, meshSkipInc); //This is so ineffecient cause it only matters when y pos is < depth but I'm lazy somebody fix this

        chunk.surfaceNoise = Noise.GenerateNoiseMap(SurfaceNoise, mapChunkSize, mapChunkSize, mapChunkSize, offset, meshSkipInc);

        chunk.terrainNoiseMap = terrainBelowGround(mapChunkSize, mapChunkSize, mapChunkSize, chunk.surfaceNoise, chunk.undergroundNoise, surfaceMaxDepth, IsoLevel, offset, meshSkipInc);

        chunk.meshData = MeshGenerator.GenerateMesh(chunk.terrainNoiseMap, IsoLevel);

        chunk.partialGenerationNoises = GetPartialGenerationNoises(GenerationData, offset, chunk.meshData.vertexParents);

        chunk.heightCurves = GetHeightCurves(GenerationData.Materials, (int)(offset.y), mapChunkSize, meshSkipInc);

        chunk.materialMap = GetPartialMaterialMap(chunk.partialGenerationNoises, GenerationData, chunk.meshData.vertexParents, meshSkipInc, chunk.heightCurves);

        chunk.colorMap = GetPartialColors(chunk.meshData.vertices, chunk.meshData.vertexParents, chunk.materialMap);

        chunk.meshData.vertices = RescaleVertices(chunk.meshData.vertices, meshSkipInc);

        chunk.meshData.vertexParents = RescaleVertices(chunk.meshData.vertexParents, meshSkipInc);

        return chunk;
    }

    public void RequestData(Func<object> generateDatum, Action<object> callback)
    {
        ThreadStart threadStart = delegate
        {
            dataThread(generateDatum, callback);
        }; ;

        new Thread(threadStart).Start();//int LoD, Vector3 center,
    }

    void dataThread(Func<object> generateDatum, Action<object> callback)
    {
        object data = generateDatum();

        lock (ThreadInfoQueue)
        {
            ThreadInfoQueue.Enqueue(new ThreadInfo(callback, data));
        }
    }

    private void Update()
    {
        if (ThreadInfoQueue.Count > 0)
        {
            while (ThreadInfoQueue.Count > 0)
            {
                ThreadInfo threadInfo = ThreadInfoQueue.Dequeue();
                //if (threadInfo.callback == null) continue;
                threadInfo.callback(threadInfo.parameter);
                
            }
        }
    }

    public static List<Vector3> RescaleVertices(List<Vector3> vertices, float rescaleFactor)
    {
        for (int i = 0; i < vertices.Count; i++)
            vertices[i] *= rescaleFactor;
        return vertices;
    }

    public static float[][] GetPartialGenerationNoises(GenerationHeightData GenerationData, Vector3 offset, List<Vector3> parentVertices)
    {
        float[][] partialGenerationNoiseMap = new float[GenerationData.Materials.Count][];

        for (int i = 0; i < GenerationData.Materials.Count; i++)
        {
            NoiseData genDataNoise = GenerationData.Materials[i].generationNoise;
            partialGenerationNoiseMap[i] = Noise.GenerateFocusedNoiseMap(genDataNoise, mapChunkSize, mapChunkSize, mapChunkSize, offset, parentVertices);
        }
        return partialGenerationNoiseMap;
    }

    public static AnimationCurve[] GetHeightCurves(List<BMaterial> mats, int center, int height, int meshSkipInc)
    {
        AnimationCurve[] Curves = new AnimationCurve[mats.Count];
        for(int i = 0; i < mats.Count; i++)
        {
            Curves[i] = BiomeHeightMap.calculateDensityCurve(mats[i].VerticalPreference, center, height, meshSkipInc);
        }
        return Curves;
    }

    public static int[,,] GetPartialMaterialMap(float[][] generationHeights, GenerationHeightData generationData, List<Vector3> focusedNodes, int meshSimpInc, AnimationCurve[] heightPref)
    {
        int[,,] materialData = new int[mapChunkSize/ meshSimpInc + 1, mapChunkSize/ meshSimpInc + 1, mapChunkSize/ meshSimpInc + 1];
        for (int n = 0; n < focusedNodes.Count; n++)
        {
            Vector3 node = focusedNodes[n];
            float maxWeight = float.MinValue;

            for (int i = 0; i < generationHeights.Length; i++)
            {
                BMaterial mat = generationData.Materials[i];

                int x = (int)node.x;
                int y = (int)node.y;
                int z = (int)node.z;

                float scaledDepth = Mathf.InverseLerp(0, mapChunkSize/meshSimpInc, y);

                float weight;

                weight = (mat.generationPref.Evaluate(generationHeights[i][n]) * heightPref[i].Evaluate(scaledDepth));

                if (weight > maxWeight)
                {
                    maxWeight = weight;
                    materialData[x, y, z] = i;
                };
            }
        }

        return materialData;
    }

    public static Color[] GetPartialColors(List<Vector3> Vertices, List<Vector3> parentVertices, int[,,] parantMats)
    {
        Color[] colors = new Color[Vertices.Count];
        for (int i = 0; i < parentVertices.Count; i += 2)
        {
            Vector3 p1 = parentVertices[i];
            Vector3 p2 = parentVertices[i + 1];
            Vector3 p = Vertices[Mathf.FloorToInt(i / 2)];

            int p1Mat = parantMats[(int)p1.x, (int)p1.y, (int)p1.z];
            int p2Mat = parantMats[(int)p2.x, (int)p2.y, (int)p2.z];

            float p1Affinity = Vector3.Distance(p, p1) / Vector3.Distance(p1, p2);
            colors[Mathf.FloorToInt(i / 2)] = new Color(p1Mat, p2Mat, p1Affinity*255);
        }
        return colors;
    }

    public static float[,,] terrainBelowGround(int mapWidth, int mapLength, int mapHeight, float[,,] surfaceMesh, float[,,] undergroundMap, float depth, float IsoLevel, Vector3 offset, int meshSimpInc)
    {
        float[,,] terrainMap = new float[mapWidth / meshSimpInc + 1, mapLength / meshSimpInc + 1, mapHeight / meshSimpInc + 1];

        float halfHeight = mapHeight / 2;

        for (int x = 0; x <= mapWidth; x+= meshSimpInc)
        {
            for (int y = 0; y <= mapLength; y += meshSimpInc)
            {
                for (int z = 0; z <= mapHeight; z += meshSimpInc)
                {
                    float actualHeight = y - halfHeight + offset.y;

                    float clampedHeight = Mathf.Clamp(actualHeight, -depth, depth);

                    float disToSurface = (surfaceMesh[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] * depth + clampedHeight) /depth;

                    terrainMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] = Mathf.Clamp((-1*disToSurface + IsoLevel), 0, 1) * undergroundMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc];
                }
            }
        }

        return terrainMap;
    }

    

    struct ThreadInfo
    {
        public readonly Action<object> callback;
        public readonly object parameter;

        public ThreadInfo(Action<object> callback, object parameter)
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }

    public struct ChunkData
    {
        public float[,,] undergroundNoise;
        public float[,,] surfaceNoise;
        public float[,,] terrainNoiseMap;
        public MeshData meshData;
        public float[][] partialGenerationNoises;
        public Color[] colorMap;
        public AnimationCurve[] heightCurves;
        public int[,,] materialMap;

        public Mesh GenerateMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = meshData.vertices.ToArray();
            mesh.triangles = meshData.triangles.ToArray();
            mesh.colors = colorMap;
            mesh.RecalculateNormals();
            return mesh;
        }

    }
}
