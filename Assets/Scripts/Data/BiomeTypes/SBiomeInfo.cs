using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Biome{
[CreateAssetMenu(menuName = "Generation/Biomes/SurfaceBiome")]
public class SBiomeInfo : CInfo<SurfaceBiome> {}

[Serializable][StructLayout(LayoutKind.Sequential)]
public struct SurfaceBiome : IBiomeCondition{
    [Range(0, 1)]
    public float TerrainStart;
    [Range(0, 1)]
    public float ErosionStart;
    [Range(0, 1)]
    public float SquashStart;
    [Range(0, 1)]
    public float InfluenceStart;
    [Range(0, 1)]
    public float InfluenceOffStart;
    [Range(0, 1)]
    public float AtmosphereStart;
    [Space(20)]
    [Range(0, 1)]
    public float TerrainEnd;
    [Range(0, 1)]
    public float ErosionEnd;
    [Range(0, 1)]
    public float SquashEnd;
    [Range(0, 1)]
    public float InfluenceEnd;
    [Range(0, 1)]
    public float InfluenceOffEnd;
    [Range(0, 1)]
    public float AtmosphereEnd;

    [HideInInspector]
    public int biome;

    public readonly int GetDimensions() => 6;
    public readonly void GetBoundDimension(ref BDict.RegionBound bound){
        bound.SetBoundDimension(0, TerrainStart, TerrainEnd);
        bound.SetBoundDimension(1, ErosionStart, ErosionEnd);
        bound.SetBoundDimension(2, SquashStart, SquashEnd);
        bound.SetBoundDimension(3, InfluenceStart, InfluenceEnd);
        bound.SetBoundDimension(4, InfluenceOffStart, InfluenceOffEnd);
        bound.SetBoundDimension(5, AtmosphereStart, AtmosphereEnd);
    }
    public void SetNode(BDict.RegionBound bound, int biome){
        TerrainStart = bound.minCorner[0];
        TerrainEnd = bound.maxCorner[0];
        ErosionStart = bound.minCorner[1];
        ErosionEnd = bound.maxCorner[1];
        SquashStart = bound.minCorner[2];
        SquashEnd = bound.maxCorner[2];
        InfluenceStart = bound.minCorner[3];
        InfluenceEnd = bound.maxCorner[3];
        InfluenceOffStart = bound.minCorner[4];
        InfluenceOffEnd = bound.maxCorner[4];
        AtmosphereStart = bound.minCorner[5];
        AtmosphereEnd = bound.maxCorner[5];
        this.biome = biome;
    }
    public void Validate(){
        ErosionEnd = Mathf.Max(ErosionStart, ErosionEnd);
        TerrainEnd = Mathf.Max(TerrainStart, TerrainEnd);
        SquashEnd = Mathf.Max(SquashStart, SquashEnd);
        InfluenceEnd = Mathf.Max(InfluenceStart, InfluenceEnd);
        InfluenceOffEnd = Mathf.Max(InfluenceOffStart, InfluenceOffEnd);
        AtmosphereEnd = Mathf.Max(AtmosphereStart, AtmosphereEnd);
    }
}
}
