using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static GenerationHeightData;

public class BiomeHeightMap
{
    public static float[][] GetHeightCurves(List<BMaterial> mats, int center, int height, int meshSkipInc)
    {
        float[][] Curves = new float[mats.Count][];
        for (int i = 0; i < mats.Count; i++)
        {
            Curves[i] = CalculateHeightCurve(mats[i].VerticalPreference, center, height, meshSkipInc);
        }
        return Curves;
    }


    public static float[] CalculateHeightCurve(List<DensityGrad> HeightRanges, int coord, int height, int meshSkipInc)
    {
        float[] heights = new float[(height)/ meshSkipInc + 1];
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
                heights[(z - coord + (height / 2)) / meshSkipInc] = 0;
            else
                heights[(z - coord + (height/2)) / meshSkipInc] = density / rangeCount;
        }

        return heights;
    }

}


