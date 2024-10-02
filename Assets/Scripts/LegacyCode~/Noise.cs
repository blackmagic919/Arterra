using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Noise
{
    private static float Perlin3D(float x, float y, float z)
    {
        float xy = Mathf.PerlinNoise(x, y);
        float yz = Mathf.PerlinNoise(y, z);
        float xz = Mathf.PerlinNoise(x, z);

        float yx = Mathf.PerlinNoise(y, x);
        float zy = Mathf.PerlinNoise(z, y);
        float zx = Mathf.PerlinNoise(z, x);

        float xyz = xy + yz + xz + yx + zy + zx;
        return xyz / 6f;
    }

    public static float[,,] GenerateNoiseMap(NoiseData noiseData, int mapWidth, int mapLength, int mapHeight, Vector3 offset, int meshSimpInc)
    {

        float[,,] noiseMap = new float[mapWidth/ meshSimpInc + 1, mapLength / meshSimpInc + 1, mapHeight /meshSimpInc + 1];
        float epsilon = (float)10E-9;


        float scale = Mathf.Max(epsilon, noiseData.noiseScale);
        System.Random prng = new System.Random(noiseData.seedOffset);

        float maxPossibleHeight = 0;
        float amplitude = 1;

        Vector3[] octaveOffsets = new Vector3[noiseData.octaves];
        for(int i = 0; i < noiseData.octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5) + offset.x;
            float offsetY = prng.Next((int)-10E5, (int)10E5) + offset.y;
            float offsetZ = prng.Next((int)-10E5, (int)10E5) + offset.z;
            octaveOffsets[i] = new Vector3(offsetX, offsetY, offsetZ);

            maxPossibleHeight += amplitude;
            amplitude *= noiseData.persistance;
        }

        float halfWidth = mapWidth / 2;
        float halfLength = mapLength / 2;
        float halfHeight = mapHeight / 2;

        for (int x = 0; x <= mapWidth; x += meshSimpInc)
        {
            for (int y = 0; y <= mapLength; y += meshSimpInc)
            {
                for (int z = 0; z <= mapHeight; z += meshSimpInc)
                {
                    amplitude = 1;
                    float frequency = 1;
                    float noiseHeight = 0;

                    
                    for(int i = 0; i < noiseData.octaves; i++)
                    {
                        float sampleX = (x-halfWidth + octaveOffsets[i].x) / scale * frequency;
                        float sampleY = (y-halfLength + octaveOffsets[i].y) / scale * frequency;
                        float sampleZ = (z-halfHeight + octaveOffsets[i].z) / scale * frequency;

                        float perlinValue = Perlin3D(sampleX, sampleY, sampleZ) * 2 - 1;//Range -1 to 1;
                        noiseHeight += perlinValue * amplitude;

                        amplitude *= noiseData.persistance; //amplitude decreases -> effect of samples decreases 
                        frequency *= noiseData.lacunarity; //frequency increases -> size of noise sampling increases -> more random
                    }


                    noiseMap[x/meshSimpInc, y/meshSimpInc, z/meshSimpInc] = (noiseHeight + 1) / (maxPossibleHeight / 0.9f);

                }
            }
        }

        return noiseMap;
    }

    public static float[] GenerateFocusedNoiseMap(NoiseData noiseData, int mapWidth, int mapLength, int mapHeight, Vector3 offset, List<Vector3> Vertices)
    {
        float[] noiseMap = new float[Vertices.Count];
        float epsilon = (float)10E-9;

        float scale = Mathf.Max(epsilon, noiseData.noiseScale);
        System.Random prng = new System.Random(noiseData.seedOffset);

        Vector3[] octaveOffsets = new Vector3[noiseData.octaves];

        float maxPossibleHeight = 0;
        float amplitude = 1;

        for (int i = 0; i < noiseData.octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5) + offset.x;
            float offsetY = prng.Next((int)-10E5, (int)10E5) + offset.y;
            float offsetZ = prng.Next((int)-10E5, (int)10E5) + offset.z;
            octaveOffsets[i] = new Vector3(offsetX, offsetY, offsetZ);

            maxPossibleHeight += amplitude;
            amplitude *= noiseData.persistance;
        }


        float halfWidth = mapWidth / 2;
        float halfLength = mapLength / 2;
        float halfHeight = mapHeight / 2;

        for (int i = 0; i < Vertices.Count; i++)
        {
            Vector3 Vertex = Vertices[i];

            amplitude = 1;
            float frequency = 1;
            float noiseHeight = 0;


            for (int u = 0; u < noiseData.octaves; u++)
            {
                float sampleX = ((int)Vertex.x - halfWidth + octaveOffsets[u].x) / scale * frequency;
                float sampleY = ((int)Vertex.y - halfLength + octaveOffsets[u].y) / scale * frequency;
                float sampleZ = ((int)Vertex.z - halfHeight + octaveOffsets[u].z) / scale * frequency;

                float perlinValue = Perlin3D(sampleX, sampleY, sampleZ) * 2 - 1;//Range -1 to 1;
                noiseHeight += perlinValue * amplitude;

                amplitude *= noiseData.persistance; //amplitude decreases -> effect of samples decreases 
                frequency *= noiseData.lacunarity; //frequency increases -> size of noise sampling increases -> more random
            }

            noiseMap[i] = (noiseHeight + 1) / (maxPossibleHeight/0.9f);
        }
        return noiseMap;
    }
}
