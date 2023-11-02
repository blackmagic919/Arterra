using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DensityAdjuster : MonoBehaviour
{
    public StructureData structure;
    public float deltaDensity;

    public void TransformDensity()
    {
        for(int x = 0; x < structure.sizeX; x++)
        {
            for (int y = 0; y < structure.sizeY; y++)
            {
                for (int z = 0; z < structure.sizeZ; z++)
                {
                    int index = Utility.irregularIndexFromCoord(x, y, z, structure.sizeX, structure.sizeY);
                    if (structure.density[index] == 0)
                        continue;
                    structure.density[index] += deltaDensity;
                }
            }
        }
    }
}
