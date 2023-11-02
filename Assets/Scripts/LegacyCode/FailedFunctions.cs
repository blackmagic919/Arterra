using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static EndlessTerrain;

public class FailedFunctions
{
    //Fails because meshSkipInc does not always go up by multiples of each other
    const int threadGroupSize = 8;
    public ComputeShader lodKnitter;

    /*          5
    * y         |
    * ^         v
    * |     .--------.
    * |    /|       /|
    * |   / |   1  / |    z
    * |  .--+-----.  |   /\
    * |  | 4|     | 2|   /
    * |  |  .-3---+--.  /
    * |  | /   6  | /  /
    * | xyz_______./  /
    * +---------> x  /
    * Front, Right, Back, Left, Top, Bottom
    */
    int[] getSurroundingChunkLOD(Vector3 coord, Vector3 viewerPosition, LODInfo[] detailLevels, int size, int LOD)
    {
        int[] xD = new int[6] { 0, 1, 0, -1, 0, 0 };
        int[] zD = new int[6] { 1, 0, -1, 0, 0, 0 };
        int[] yD = new int[6] { 0, 0, 0, 0, 1, -1 };
        int[] ret = new int[6];

        for (int i = 0; i < 6; i++)
        {
            Vector3 newCoord = coord + new Vector3(xD[i], yD[i], zD[i]);
            Vector3 chunkPos = newCoord * size - Vector3.one * (size / 2f);
            Bounds chunkBounds = new Bounds(chunkPos, Vector3.one * size);
            float closestDist = Mathf.Sqrt(chunkBounds.SqrDistance(viewerPosition));

            int lodInd = 0;
            for (int u = 0; u < detailLevels.Length - 1; u++)
            {
                if (closestDist > detailLevels[u].distanceThresh)
                    lodInd = u + 1;
                else
                    break;
            }
            ret[i] = detailLevels[lodInd].LOD;
        }
        return ret;
    }

    public void KnitLODS(int[] surroundingLOD, int chunkSize, int LOD)
    {
        int meshSkipInc = MapGenerator.meshSkipTable[LOD];
        int numPointsAxis = chunkSize / meshSkipInc + 1;
        int numThreadsPerAxis = Mathf.CeilToInt(numPointsAxis / (float)threadGroupSize);

        int[] faceOrientation = new int[6] { 2, 0, 2, 0, 1, 1 };
        int[] facePosition = new int[6] { numPointsAxis, numPointsAxis, 0, 0, numPointsAxis, 0 };

        lodKnitter.SetFloat("numPointsPerAxis", numPointsAxis);

        for (int i = 0; i < 6; i++)
        {
            if (surroundingLOD[i] <= 0)
                continue;

            int surroundSkipInc = MapGenerator.meshSkipTable[surroundingLOD[i]] / meshSkipInc;

            lodKnitter.SetInt("normalAxis", faceOrientation[i]);
            lodKnitter.SetInt("normalPosition", facePosition[i]);
            lodKnitter.SetInt("faceSkipInc", surroundSkipInc);

            lodKnitter.Dispatch(0, numThreadsPerAxis, numThreadsPerAxis, 1);
        }
    }
}
