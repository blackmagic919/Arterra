using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public static class Utility
{
    public static int indexFromCoord(int x, int y, int z, int numPointsAxis)
    {
        return x * numPointsAxis * numPointsAxis + y * numPointsAxis + z;
    }

    public static int indexFromCoordV(Vector3 coord, int numPointsPerAxis)
    {

        return (int)coord.x * numPointsPerAxis * numPointsPerAxis + (int)coord.y * numPointsPerAxis + (int)coord.z;
    }

}
