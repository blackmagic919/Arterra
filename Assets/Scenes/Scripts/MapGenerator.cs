using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MapGenerator : MonoBehaviour
{
    public enum DrawMode { Voxel, March}
    public DrawMode drawMode;
    public int mapWidth;
    public int mapLength;
    public int mapHeight;

    public NoiseData TerrainNoise; //For actual terrain generation
    public GenerationHeightData GenerationData;

    public MeshFilter meshFilter;

    public Vector3 offset;

    public bool editorAutoUpdate;

    //[HideInInspector]
    //public VoxelMesh voxels = new VoxelMesh(); Voxel mesh crashes the game atm someone can fix this if they want

    public void GenerateMap()
    {
        float[,,] terrainNoiseMap = Noise.GenerateNoiseMap(TerrainNoise.seed, mapWidth, mapLength, mapHeight, TerrainNoise.noiseScale, TerrainNoise.octaves, TerrainNoise.persistance, TerrainNoise.lacunarity, offset);

        if (drawMode == DrawMode.Voxel)
        {
            //voxels.GenerateVoxelMesh(terrainNoiseMap); 
        }
        else
        {
            MeshData newMesh = MeshGenerator.GenerateMesh(terrainNoiseMap, 0.5f);
            float[][] partialGenerationNoiseMap = new float[GenerationData.Materials.Count][];
            for (int i = 0; i < GenerationData.Materials.Count; i++)
            {
                NoiseData genDataNoise = GenerationData.Materials[i].generationNoise;
                partialGenerationNoiseMap[i] = Noise.GenerateFocusedNoiseMap(genDataNoise.seed, mapWidth, mapLength, mapHeight, genDataNoise.noiseScale, genDataNoise.octaves, genDataNoise.persistance, genDataNoise.lacunarity, offset, newMesh.vertexParents);
            }
            MaterialData[,,] materialMap = GetPartialMaterialMap(partialGenerationNoiseMap, terrainNoiseMap, GenerationData, newMesh.vertexParents);
            Color[] colorMap = GetPartialColors(newMesh.vertices, newMesh.vertexParents, materialMap);
            meshFilter.sharedMesh = newMesh.CreateMesh();
            meshFilter.sharedMesh.colors = colorMap;

        }
    }

    public MaterialData[,,] GetPartialMaterialMap(float[][] generationHeights, float[,,] actualHeights, GenerationHeightData generationData, List<Vector3> focusedNodes)
    {
        //The atrocious O(m*n^3) time, where m is the # of materials and n is the dimension
        MaterialData[,,] materialData = new MaterialData[mapWidth, mapLength, mapHeight];
        for (int n = 0; n < focusedNodes.Count; n++)
        {
            Vector3 node = focusedNodes[n];
            float maxWeight = float.MinValue;

            for (int i = 0; i < generationHeights.Length; i++)
            {
                GenerationHeightData.BMaterial mat = generationData.Materials[i];

                int x = (int)node.x;
                int y = (int)node.y;
                int z = (int)node.z;

                float scaledDepth = Mathf.Clamp(0.0f, 1.0f, Mathf.Lerp(-1, 1, actualHeights[x, y, z]));
                float weight = (mat.heightPreference.Evaluate(scaledDepth) * mat.generationPreference.Evaluate(generationHeights[i][n]));

                if (weight > maxWeight)
                {
                    maxWeight = weight;
                    materialData[x, y, z] = mat.mat;
                };
            }
        }

        return materialData;
    }

    public Color[] GetPartialColors(List<Vector3> Vertices,  List<Vector3> parentVertices, MaterialData[,,] parantMats)
    {
        Color[] colors = new Color[Vertices.Count];
        for(int i = 0; i < parentVertices.Count; i+=2)
        {
            Vector3 p1 = parentVertices[i];
            Vector3 p2 = parentVertices[i+1];
            Vector3 p = Vertices[Mathf.FloorToInt(i / 2)];

            MaterialData p1Mat = parantMats[(int)p1.x, (int)p1.y, (int)p1.z];
            MaterialData p2Mat = parantMats[(int)p2.x, (int)p2.y, (int)p2.z];

            float p1Affinity = Vector3.Distance(p, p1) / Vector3.Distance(p1, p2);
            colors[Mathf.FloorToInt(i / 2)] = Color.Lerp(p1Mat.color, p2Mat.color, p1Affinity);
        }
        return colors;
    }

    public void OnValidate()
    {
        if (mapWidth < 1) mapWidth = 1;
        if (mapLength < 1) mapLength = 1;
        if (mapHeight < 1) mapHeight = 1;
    }

    
}
