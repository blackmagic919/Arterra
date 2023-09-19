using System.Collections.Generic;
using UnityEngine;
using System;
using System.Threading;
using static GenerationHeightData;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March }
    public DrawMode drawMode;

    public NoiseData TerrainNoise; //For underground terrain generation
    public GenerationHeightData GenerationData;

    public NoiseData SurfaceNoise; //For underground terrain generation
    public float surfaceMaxDepth;

    public MeshFilter meshFilter;

    public Vector3 offset;

    [HideInInspector]
    public const int mapChunkSize = 48;
    [Range(0, 4)]
    public int EditorLoD;

    Queue<MapThreadInfo<MeshData>> mapDataThreadInfoQueue = new Queue<MapThreadInfo<MeshData>>();

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
            MeshData newMap = GenerateMapData(EditorLoD, Vector3.zero);
            meshFilter.sharedMesh = newMap.GenerateMesh();

        }
    }

    public void RequestMapData(int LoD, Vector3 center, Action<MeshData> callback)
    {
        ThreadStart threadStart = delegate
        {
            MapDataThread(LoD, center, callback);
        }; ;

        new Thread(threadStart).Start();
    }

    void MapDataThread(int LoD, Vector3 center, Action<MeshData> callback)
    {
        MeshData mapData = GenerateMapData(LoD, center);

        lock (mapDataThreadInfoQueue)
        {
            mapDataThreadInfoQueue.Enqueue(new MapThreadInfo<MeshData>(callback, mapData));
        }
    }

    private void Update()
    {
        if (mapDataThreadInfoQueue.Count > 0)
        {
            while (mapDataThreadInfoQueue.Count > 0)
            {
                MapThreadInfo<MeshData> threadInfo = mapDataThreadInfoQueue.Dequeue();
                threadInfo.callback(threadInfo.parameter);
            }
        }
    }

    //Terrain Noise -> Vertices -> Generation Noise -> Material Map -> Color Map -> Color Vertices
    public MeshData GenerateMapData(int LOD, Vector3 center)
    {
        float[,,] undergroundNoiseMap = Noise.GenerateNoiseMap(TerrainNoise, mapChunkSize, mapChunkSize, mapChunkSize, center + offset, LOD); //This is so ineffecient cause it only matters when y pos is < depth but I'm lazy somebody fix this

        float[,,] surfaceNoiseMap = Noise.GenerateNoiseMap(SurfaceNoise, mapChunkSize, mapChunkSize, mapChunkSize, center + offset, LOD);

        float[,,] terrainNoiseMap = terrainBelowGround(mapChunkSize, mapChunkSize, mapChunkSize, surfaceNoiseMap, surfaceMaxDepth, center + offset, LOD, undergroundNoiseMap);

        float resizeFactor = ((LOD == 0) ? 1 : LOD * 2);

        MapData newMesh = MeshGenerator.GenerateMesh(terrainNoiseMap, 0.3f, resizeFactor);

        float[][] partialGenerationNoiseMap = new float[GenerationData.Materials.Count][];

        for (int i = 0; i < GenerationData.Materials.Count; i++)
        {
            NoiseData genDataNoise = GenerationData.Materials[i].generationNoise;
            partialGenerationNoiseMap[i] = Noise.GenerateFocusedNoiseMap(genDataNoise, mapChunkSize, mapChunkSize, mapChunkSize, center + offset, newMesh.vertexParents);
        }

        AnimationCurve[] materialHeightPrefs = GetHeightCurves(GenerationData.Materials, (int)(center.y + offset.y), mapChunkSize, LOD);

        MaterialData[,,] materialMap = GetPartialMaterialMap(partialGenerationNoiseMap, terrainNoiseMap, GenerationData, newMesh.vertexParents, LOD, materialHeightPrefs);
        Color[] colorMap = GetPartialColors(newMesh.vertices, newMesh.vertexParents, materialMap);
        
        return new MeshData(terrainNoiseMap, newMesh.vertices, newMesh.vertexParents, newMesh.triangles, partialGenerationNoiseMap, materialMap, colorMap);
    }

    public AnimationCurve[] GetHeightCurves(List<BMaterial> mats, int center, int height, int LoD)
    {
        AnimationCurve[] Curves = new AnimationCurve[mats.Count];
        for(int i = 0; i < mats.Count; i++)
        {
            Curves[i] = BiomeHeightMap.calculateDensityCurve(mats[i].VerticalPreference, center, height, LoD);
        }
        return Curves;
    }

    public MaterialData[,,] GetPartialMaterialMap(float[][] generationHeights, float[,,] actualHeights, GenerationHeightData generationData, List<Vector3> focusedNodes, int LoD, AnimationCurve[] heightPref)
    {

        int meshSimpInc = (LoD == 0) ? 1 : LoD * 2;

        MaterialData[,,] materialData = new MaterialData[mapChunkSize/ meshSimpInc + 1, mapChunkSize/ meshSimpInc + 1, mapChunkSize/ meshSimpInc + 1];
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

                float scaledDepth = Mathf.Clamp(0.0f, 1.0f, Mathf.Lerp(-1, 1, actualHeights[x, y, z]));

                float weight;

                lock ((mat)) {
                    weight = (mat.generationPref.Evaluate(scaledDepth) * heightPref[i].Evaluate(generationHeights[i][n]));
                };

                if (weight > maxWeight)
                {
                    maxWeight = weight;
                    materialData[x, y, z] = mat.mat;
                };
            }
        }

        return materialData;
    }

    public Color[] GetPartialColors(List<Vector3> Vertices, List<Vector3> parentVertices, MaterialData[,,] parantMats)
    {
        Color[] colors = new Color[Vertices.Count];
        for (int i = 0; i < parentVertices.Count; i += 2)
        {
            Vector3 p1 = parentVertices[i];
            Vector3 p2 = parentVertices[i + 1];
            Vector3 p = Vertices[Mathf.FloorToInt(i / 2)];

            MaterialData p1Mat = parantMats[(int)p1.x, (int)p1.y, (int)p1.z];
            MaterialData p2Mat = parantMats[(int)p2.x, (int)p2.y, (int)p2.z];

            float p1Affinity = Vector3.Distance(p, p1) / Vector3.Distance(p1, p2);
            colors[Mathf.FloorToInt(i / 2)] = Color.Lerp(p1Mat.color, p2Mat.color, p1Affinity);
        }
        return colors;
    }

    public float[,,] terrainBelowGround(int mapWidth, int mapLength, int mapHeight, float[,,] surfaceMesh, float depth, Vector3 offset, int LoD, float[,,] undergroundMap)
    {
        int meshSimpInc = (LoD == 0) ? 1 : LoD * 2;

        float[,,] terrainMap = new float[mapWidth / meshSimpInc + 1, mapLength / meshSimpInc + 1, mapHeight / meshSimpInc + 1];

        float halfHeight = mapHeight / 2;

        for (int x = 0; x <= mapWidth; x+= meshSimpInc)
        {
            for (int y = 0; y <= mapLength; y += meshSimpInc)
            {
                for (int z = 0; z <= mapHeight; z += meshSimpInc)
                {
                    float actualHeight = y + halfHeight + offset.y;

                    if (surfaceMesh[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] * depth > actualHeight)
                        terrainMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] = undergroundMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc];
                    else
                        terrainMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] = 0;
                }
            }
        }

        return terrainMap;
    }

    struct MapThreadInfo<T>
    {
        public readonly Action<T> callback;
        public readonly T parameter;

        public MapThreadInfo(Action<T> callback, T parameter)
        {
            this.callback = callback;
            this.parameter = parameter;
        }
    }

    public struct MeshData
    {
        public float[,,] terrainNoiseMap;

        public List<Vector3> vertices;
        public List<Vector3> vertexParents;
        public List<int> triangles;

        public float[][] partialGenerationNoiseMap;
        public MaterialData[,,] materialMap;
        public Color[] colorMap;

        public MeshData(float[,,] terrainNoiseMap, List<Vector3> vertices, List<Vector3> vertexParents, List<int> triangles,
                        float[][] partialGenerationNoiseMap, MaterialData[,,] materialMap, Color[] colorMap)
        {
            this.terrainNoiseMap = terrainNoiseMap;
            this.vertices = vertices;
            this.vertexParents = vertexParents;
            this.triangles = triangles;
            this.partialGenerationNoiseMap = partialGenerationNoiseMap;
            this.materialMap = materialMap;
            this.colorMap = colorMap;
        }

        public Mesh GenerateMesh()
        {
            Mesh mesh = new Mesh();
            mesh.vertices = vertices.ToArray();
            mesh.triangles = triangles.ToArray();
            mesh.colors = colorMap;
            mesh.RecalculateNormals();
            return mesh;
        }

    }
}
