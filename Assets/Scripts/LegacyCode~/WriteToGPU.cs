using System.Collections;
using System.Collections.Generic;
using Unity.Jobs;
using Unity.Burst;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Utils;

[BurstCompile]
public struct WriteToGPU : IJobParallelFor
{
    [ReadOnly] public NativeArray<CPUDensityManager.MapData> source;
    [ReadOnly] public int sourceMapSize;
    [ReadOnly] public int destMapSize;
    [ReadOnly] public int meshSkipInc;
    [WriteOnly] public NativeArray<CPUDensityManager.MapData> dest;
    public void Execute(int index)
    {
        int xC = index / (destMapSize * destMapSize);
        int yC = (index / destMapSize) % destMapSize;
        int zC = index % destMapSize;

        int3 fCoord = new int3(xC, yC, zC) * meshSkipInc;
        int sIndex = CustomUtility.indexFromCoord(fCoord, sourceMapSize);

        dest[index] = source[sIndex];
    }
}
