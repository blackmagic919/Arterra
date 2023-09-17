using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class VoxelMesh : MapGenerator
{
    public List<GameObject> TerrainObjects;

    public void GenerateVoxelMesh(float[,,] terrainNoiseMap)
    {
        float[][,,] generationNoiseMap = new float[GenerationData.Materials.Count][,,];

        for (int i = 0; i < GenerationData.Materials.Count; i++)
        {
            NoiseData genDataNoise = GenerationData.Materials[i].generationNoise;
            generationNoiseMap[i] = Noise.GenerateNoiseMap(genDataNoise.seed, mapWidth, mapLength, mapHeight, genDataNoise.noiseScale, genDataNoise.octaves, genDataNoise.persistance, genDataNoise.lacunarity, offset);
        }

        MaterialData[,,] materialMap = GetMaterialMap(generationNoiseMap, terrainNoiseMap, GenerationData, mapWidth, mapLength, mapHeight);

        GameObject TerrainChunkFolder = new GameObject("Blocks");
        TerrainChunkFolder = new GameObject("Blocks");
        TerrainChunkFolder.transform.parent = TerrainChunkFolder.transform;
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

    public MaterialData[,,] GetMaterialMap(float[][,,] generationHeights, float[,,] actualHeights, GenerationHeightData generationData , int mapWidth, int mapLength, int mapHeight)
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
                        GenerationHeightData.BMaterial mat = generationData.Materials[i];
                        float scaledDepth = Mathf.Clamp(0.0f, 1.0f, Mathf.Lerp(-1, 1, actualHeights[x, y, z]));
                        float weight = (mat.heightPreference.Evaluate(scaledDepth) * mat.generationPreference.Evaluate(generationHeights[i][x, y, z]));

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
            UnityEngine.Object.DestroyImmediate(block);
        }
    }
}
