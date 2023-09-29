using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GenerationHeightData;

public class BiomeHeightMap
{

    public static float[] calculateDensityCurve(List<DensityGrad> HeightRanges, int coord, int height, int meshSkipInc)
    {
        float[] chunkDensity = new float[(height)/ meshSkipInc + 1];
        for(int z = coord - (height/2); z <= coord + (height/2); z += meshSkipInc)
        {
            int rangeCount = 0;
            float density = 0;
            foreach(DensityGrad range in HeightRanges)
            {
                if (range.lowerLimit > z || range.upperLimit < z)
                    continue;
                rangeCount++;
                density += range.DensityCurve.Evaluate(Mathf.InverseLerp(range.lowerLimit, range.upperLimit, z));
            }
            if (rangeCount == 0) //No Density Ranges will be no density
                chunkDensity[(z - coord + (height / 2)) / meshSkipInc] = 0;
            chunkDensity[(z - coord + (height/2)) / meshSkipInc] = density / rangeCount;
        }

        return chunkDensity;
    }

}


