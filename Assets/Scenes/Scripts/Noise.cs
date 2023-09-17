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

    public static float[,,] GenerateNoiseMap(int seed, int mapWidth, int mapLength, int mapHeight, float scale, int octaves, float persistence, float lacunarity, Vector3 offset)

    {
        float[,,] noiseMap = new float[mapWidth, mapLength, mapHeight];
        float epsilon = (float)10E-9;

        scale = Mathf.Max(epsilon, scale);
        System.Random prng = new System.Random(seed);
           
        Vector3[] octaveOffsets = new Vector3[octaves];
        for(int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5) + offset.x;
            float offsetY = prng.Next((int)-10E5, (int)10E5) + offset.y;
            float offsetZ = prng.Next((int)-10E5, (int)10E5) + offset.z;
            octaveOffsets[i] = new Vector3(offsetX, offsetY, offsetZ);
        }

        float maxNoiseHeight = float.MinValue;
        float minNoiseHeight = float.MaxValue;

        float halfWidth = mapWidth / 2;
        float halfLength = mapLength / 2;
        float halfHeight = mapHeight / 2;

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapLength; y++)
            {
                for (int z = 0; z < mapHeight; z++)
                {
                    float amplitude = 1;
                    float frequency = 1;
                    float noiseHeight = 0;


                    for(int i = 0; i < octaves; i++)
                    {
                        float sampleX = (x-halfWidth) / scale * frequency + octaveOffsets[i].x;
                        float sampleY = (y-halfLength) / scale * frequency + octaveOffsets[i].y;
                        float sampleZ = (z-halfHeight) / scale * frequency + octaveOffsets[i].z;

                        float perlinValue = Perlin3D(sampleX, sampleY, sampleZ) * 2 - 1;//Range -1 to 1;
                        noiseHeight += perlinValue * amplitude;

                        amplitude *= persistence; //amplitude decreases -> effect of samples decreases 
                        frequency *= lacunarity; //frequency increases -> size of noise sampling increases -> more random
                    }
                    maxNoiseHeight = Mathf.Max(maxNoiseHeight, noiseHeight);
                    minNoiseHeight = Mathf.Min(minNoiseHeight, noiseHeight);

                    noiseMap[x, y, z] = noiseHeight;

                }
            }
        }

        for (int x = 0; x < mapWidth; x++)
        {
            for (int y = 0; y < mapLength; y++)
            {
                for (int z = 0; z < mapHeight; z++)
                {
                    noiseMap[x, y, z] = Mathf.InverseLerp(minNoiseHeight, maxNoiseHeight, noiseMap[x, y, z]);
                }
            }
        }

        return noiseMap;
    }

    public static float[] GenerateFocusedNoiseMap(int seed, int mapWidth, int mapLength, int mapHeight, float scale, int octaves, float persistence, float lacunarity, Vector3 offset, List<Vector3> Vertices)
    {
        float[] noiseMap = new float[Vertices.Count];
        float epsilon = (float)10E-9;

        scale = Mathf.Max(epsilon, scale);
        System.Random prng = new System.Random(seed);

        Vector3[] octaveOffsets = new Vector3[octaves];

        float maxPossibleHeight = 0;
        float amplitude = 1;

        for (int i = 0; i < octaves; i++)
        {
            float offsetX = prng.Next((int)-10E5, (int)10E5) + offset.x;
            float offsetY = prng.Next((int)-10E5, (int)10E5) + offset.y;
            float offsetZ = prng.Next((int)-10E5, (int)10E5) + offset.z;
            octaveOffsets[i] = new Vector3(offsetX, offsetY, offsetZ);

            maxPossibleHeight += amplitude;
            amplitude *= persistence;
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


            for (int u = 0; u < octaves; u++)
            {
                float sampleX = ((int)Vertex.x - halfWidth) / scale * frequency + octaveOffsets[u].x;
                float sampleY = ((int)Vertex.y - halfLength) / scale * frequency + octaveOffsets[u].y;
                float sampleZ = ((int)Vertex.z - halfHeight) / scale * frequency + octaveOffsets[u].z;

                float perlinValue = Perlin3D(sampleX, sampleY, sampleZ) * 2 - 1;//Range -1 to 1;
                noiseHeight += perlinValue * amplitude;

                amplitude *= persistence; //amplitude decreases -> effect of samples decreases 
                frequency *= lacunarity; //frequency increases -> size of noise sampling increases -> more random
            }

            noiseMap[i] = (noiseHeight + 1) / (maxPossibleHeight/0.9f);
        }
        return noiseMap;
    }
}
