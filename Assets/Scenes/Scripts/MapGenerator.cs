using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GenerationHeightData;

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

    private GameObject TerrainChunkFolder;
    private List<GameObject> TerrainObjects;

    public void GenerateMap()
    {
        float[,,] terrainNoiseMap = Noise.GenerateNoiseMap(TerrainNoise.seed, mapWidth, mapLength, mapHeight, TerrainNoise.noiseScale, TerrainNoise.octaves, TerrainNoise.persistance, TerrainNoise.lacunarity, offset);

        float[][,,] generationNoiseMap = new float[GenerationData.Materials.Count][,,];

        for (int i = 0; i < GenerationData.Materials.Count; i++)
        {
            NoiseData genDataNoise = GenerationData.Materials[i].generationNoise;
            generationNoiseMap[i] = Noise.GenerateNoiseMap(genDataNoise.seed, mapWidth, mapLength, mapHeight, genDataNoise.noiseScale, genDataNoise.octaves, genDataNoise.persistance, genDataNoise.lacunarity, offset);
        }

        MaterialData[,,] materialMap = GetMaterialMap(generationNoiseMap, terrainNoiseMap, GenerationData);

        if (drawMode == DrawMode.Voxel)
        {
            TerrainChunkFolder = new GameObject("Blocks");
            TerrainChunkFolder.transform.parent = transform;
            TerrainObjects = new List<GameObject>() { TerrainChunkFolder };

            for (int x = 0; x < terrainNoiseMap.GetLength(0); x++)
            {
                for (int y = 0; y < terrainNoiseMap.GetLength(0); y++)
                {
                    for (int z = 0; z < terrainNoiseMap.GetLength(0); z++)
                    {
                        if (terrainNoiseMap[x, y, z] >= 0.5)
                        {
                            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                            block.transform.position = new Vector3(x, y, z);
                            block.transform.parent = TerrainChunkFolder.transform;
                            block.GetComponent<Renderer>().material.color = materialMap[x, y, z].color;

                            TerrainObjects.Add(block);
                        }
                    }
                }
            }
        }
        else
        {
            MeshData newMesh = MeshGenerator.GenerateMesh(terrainNoiseMap, 0.5f);
            meshFilter.sharedMesh = newMesh.CreateMesh();

        }
    }

    public MaterialData[,,] GetMaterialMap(float[][,,] generationHeights, float[,,] actualHeights, GenerationHeightData generationData)
    {
        //The atrocious O(m*n^3) time, where m is the # of materials and n is the dimension
        MaterialData[,,] materialData = new MaterialData[mapWidth, mapLength, mapHeight];
        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapLength; y++)
            {
                for (int z = 0; z < mapHeight; z++)
                {
                    float maxWeight = float.MinValue;
                    for (int i = 0; i < generationHeights.Length; i++)
                    {
                        BMaterial mat = generationData.Materials[i];
                        float scaledDepth = Mathf.Clamp(0.0f, 1.0f, Mathf.Lerp(-1, 1, actualHeights[x, y, z]));
                        float weight = (mat.heightPreference.Evaluate(scaledDepth)  * mat.generationPreference.Evaluate(generationHeights[i][x, y, z]));

                        if (weight > maxWeight)
                        {
                            maxWeight = weight;
                            materialData[x, y, z] = mat.mat;
                        };
                    }
                    
                }
            }
        }
        return materialData;
    }

    public void DeleteMap()
    {
        if (TerrainObjects == null) return;
        foreach (GameObject block in TerrainObjects)
        {
            DestroyImmediate(block);
        }
    }

    public void OnValidate()
    {
        if (mapWidth < 1) mapWidth = 1;
        if (mapLength < 1) mapLength = 1;
        if (mapHeight < 1) mapHeight = 1;
    }

    
}
