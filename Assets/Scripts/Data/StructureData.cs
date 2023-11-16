using System;
using System.Collections.Generic;
using UnityEngine;
using Utils;

[System.Serializable]
[CreateAssetMenu(fileName = "Structure_Data", menuName = "Structure Data")]
public class StructureData : ScriptableObject
{
    [Header("Settings")]
    public int maximumLOD;
    public bool randThetaRot;
    public bool randPhiRot;

    [Header("Generated")]
    public float[] density;
    public int[] materials;
    public CheckPoint[] checks;
    public int sizeX;
    public int sizeY;
    public int sizeZ;

    public void SetChecks(Vector3[] position, bool[] isUnderGround)
    {
        checks = new CheckPoint[position.Length];
        for (int i = 0; i < position.Length; i++)
        {
            checks[i] = new CheckPoint(position[i], isUnderGround[i]);
        }
    }

    public T[] Flatten<T>(T[,,] array)
    {
        T[] ret = new T[sizeX * sizeY * sizeZ];
        for(int x = 0; x < sizeX; x++)
        {
            for (int y = 0; y < sizeY; y++)
            {
                for (int z = 0; z < sizeZ; z++)
                {
                    ret[CustomUtility.irregularIndexFromCoord(x, y, z, sizeX, sizeY)] = array[x, y, z];
                }
            }
        }
        return ret;
    }


    [Serializable]
    public struct CheckPoint
    {
        public Vector3 position;
        public bool isUnderGround;

        public CheckPoint(Vector3 position, bool isUnderGround)
        {
            this.position = position;
            this.isUnderGround = isUnderGround;
        }
    }
}

public class StructureDictionary
{
    public Dictionary<Vector3, List<StructureSection>> dict = new Dictionary<Vector3, List<StructureSection>>();
    //Key1: ChunkCoord, Key2: ChunkPos coord, Value: Density, Material

    public List<StructureSection> GetChunk(Vector3 coord)
    {
        if (dict.TryGetValue(coord, out List<StructureSection> OUT))
            return OUT;
        else {
            List<StructureSection> newChunk = new List<StructureSection>();
            dict.Add(coord, newChunk);
            return newChunk;
        }
    }

    public void Add(Vector3 coord, StructureSection section)
    {
        if (dict.TryGetValue(coord, out List<StructureSection> OUT))
            OUT.Add(section);
        else
        {
            List<StructureSection> newChunk = new List<StructureSection>();
            dict.Add(coord, newChunk);
            newChunk.Add(section);
        }
    }

    public class StructureSection
    {
        public Vector3 chunkOrigin; //Plan +x + y +z from origin
        public Vector3 structureOrigin;
        public StructureData structure;

        public int[] transformIndex;
        public bool[] transformFlipped;
        public int[] transformSizes;

        public StructureSection(StructureData structure, Vector3 structureOrigin, Vector3 chunkOrigin, int[] transformAxis)
        {
            this.structure = structure;
            this.structureOrigin = structureOrigin;
            this.chunkOrigin = chunkOrigin;

            this.transformIndex = new int[] { Mathf.Abs(transformAxis[0]) - 1, Mathf.Abs(transformAxis[1]) - 1, Mathf.Abs(transformAxis[2]) - 1};
            this.transformFlipped = new bool[] { transformAxis[0] < 0, transformAxis[1] < 0, transformAxis[2] < 0 };
            int[] originalSize = new int[] { structure.sizeX, structure.sizeY, structure.sizeZ };
            this.transformSizes = new int[] { originalSize[transformIndex[0]], originalSize[transformIndex[1]], originalSize[transformIndex[2]] };

        }


        public (int, int, int) GetCoord(int x, int y, int z)
        {
            int xPrime = (transformFlipped[0]) ? (transformSizes[0] - 1) - x : x;
            int yPrime = (transformFlipped[1]) ? (transformSizes[1] - 1) - y : y;
            int zPrime = (transformFlipped[2]) ? (transformSizes[2] - 1) - z : z;

            int[] reverseTransform = new int[3];
            reverseTransform[transformIndex[0]] = xPrime;
            reverseTransform[transformIndex[1]] = yPrime;
            reverseTransform[transformIndex[2]] = zPrime;

            return (reverseTransform[0], reverseTransform[1], reverseTransform[2]);
        }

    }
}