using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using static BiomeHeightMap;
using static GenerationHeightData;

public class BiomeHeightMap
{
    /*
     */

    public static AnimationCurve calculateDensityCurve(List<DensityGrad> HeightRanges, int coord, int height, int meshSimpInc)
    {
        AnimationCurve chunkDensity = new AnimationCurve();
        for(int z = coord - (height/2); z <= coord + (height/2); z+= meshSimpInc)
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
            if (rangeCount != 0) //No Density Ranges will be no density
                chunkDensity.AddKey(new Keyframe(Mathf.InverseLerp(-height, height, (z - coord)), density / rangeCount));
        }
        if (chunkDensity.length == 0)
            chunkDensity.AddKey(new Keyframe(0.5f, 0)); //No height range, no density

        return chunkDensity;
    }

}


