using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GenerationHeightData;

public class LegacyGeneration 
{
    const int mapChunkSize = 48;

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

    public static float[][] GetHeightCurves(List<BMaterial> mats, int center, int height, int meshSkipInc)
    {
        float[][] Curves = new float[mats.Count][];
        for (int i = 0; i < mats.Count; i++)
        {
            Curves[i] = BiomeHeightMap.calculateDensityCurve(mats[i].VerticalPreference, center, height, meshSkipInc);
        }
        return Curves;
    }

    public static int[,,] GetPartialMaterialMap(float[][] generationHeights, GenerationHeightData generationData, List<Vector3> focusedNodes, int meshSkipInc, float[][] heightPref)
    {
        int[,,] materialData = new int[mapChunkSize / meshSkipInc + 1, mapChunkSize / meshSkipInc + 1, mapChunkSize / meshSkipInc + 1];
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

                float weight;

                weight = (mat.generationPref.Evaluate(generationHeights[i][n]) * heightPref[i][y]);

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
            colors[Mathf.FloorToInt(i / 2)] = new Color(p1Mat, p2Mat, p1Affinity * 255);
        }
        return colors;
    }

    public static float[,,] terrainBelowGround(int mapWidth, int mapLength, int mapHeight, float[,,] surfaceMesh, float[,,] undergroundMap, float depth, float IsoLevel, Vector3 offset, int meshSimpInc)
    {
        float[,,] terrainMap = new float[mapWidth / meshSimpInc + 1, mapLength / meshSimpInc + 1, mapHeight / meshSimpInc + 1];

        float halfHeight = mapHeight / 2;

        for (int x = 0; x <= mapWidth; x += meshSimpInc)
        {
            for (int y = 0; y <= mapLength; y += meshSimpInc)
            {
                for (int z = 0; z <= mapHeight; z += meshSimpInc)
                {
                    float actualHeight = y - halfHeight + offset.y;

                    float clampedHeight = Mathf.Clamp(actualHeight, -depth, depth);

                    float disToSurface = (surfaceMesh[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] * depth + clampedHeight) / depth;

                    terrainMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc] = Mathf.Min(Mathf.Clamp((-1 * disToSurface + IsoLevel), 0, 1), undergroundMap[x / meshSimpInc, y / meshSimpInc, z / meshSimpInc]);

                    //The cave entrances are blocky but I've tried every other way of doing this. The only way will make this run 8 times slower
                }
            }
        }

        return terrainMap;
    }

}
