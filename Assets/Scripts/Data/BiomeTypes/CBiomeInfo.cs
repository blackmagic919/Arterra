using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Biome{
[CreateAssetMenu(menuName = "Generation/Biomes/CaveBiome")]
public class CBiomeInfo : CInfo<CaveBiome> {
}

[Serializable][StructLayout(LayoutKind.Sequential)]
public struct CaveBiome : IBiomeCondition{
    [Range(0, 1)]
    public float CaveFreqStart;
    [Range(0, 1)]
    public float CaveSizeStart;
    [Range(0, 1)]
    public float CaveShapeStart;
    [Range(0, 1)]
    public float HeightStart;
    [Space(20)]
    [Range(0, 1)]
    public float CaveFreqEnd;
    [Range(0, 1)]
    public float CaveSizeEnd;
    [Range(0, 1)]
    public float CaveShapeEnd;
    [Range(0, 1)]
    public float HeightEnd;

    [HideInInspector]
    public int biome;

    public readonly int GetDimensions() => 4;
    public readonly void GetBoundDimension(ref BDict.RegionBound bound){
        bound.SetBoundDimension(0, HeightStart, HeightEnd);
        bound.SetBoundDimension(1, CaveFreqStart, CaveFreqEnd);
        bound.SetBoundDimension(2, CaveSizeStart, CaveSizeEnd);
        bound.SetBoundDimension(3, CaveShapeStart, CaveShapeEnd);
    }
    public void SetNode(BDict.RegionBound bound, int biome){
        HeightStart = bound.minCorner[0];
        HeightEnd = bound.maxCorner[0];
        CaveFreqStart = bound.minCorner[1];
        CaveFreqEnd = bound.maxCorner[1];
        CaveSizeStart = bound.minCorner[2];
        CaveSizeEnd = bound.maxCorner[2];
        CaveShapeStart = bound.minCorner[3];
        CaveShapeEnd = bound.maxCorner[3];
        this.biome = biome;
    }
    public void Validate(){
        HeightEnd = Mathf.Max(HeightStart, HeightEnd);
        CaveFreqEnd = Mathf.Max(CaveFreqStart, CaveFreqEnd);
        CaveSizeEnd = Mathf.Max(CaveSizeStart, CaveSizeEnd);
        CaveShapeEnd = Mathf.Max(CaveShapeStart, CaveShapeEnd);
    }
}
}
