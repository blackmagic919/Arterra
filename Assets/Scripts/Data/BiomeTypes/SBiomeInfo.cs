using System;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using UnityEngine;

namespace WorldConfig.Generation.Biome{

/// <summary>
/// The settings describing the generation of one surface biome. A surface biome 
/// controls generation for a thin layer immediately around the surface of the world.
/// Information regarding generation dictated by the biome is stored within <see cref="Info"/> 
/// while information regarding its placement can be found in <see cref="SurfaceBiome"/>. 
/// </summary> 
/// <remarks>This class defines a concrete implementation of <see cref="CInfo{T}"/> so 
/// that its contents can be serialized by Unity.</remarks>
[CreateAssetMenu(menuName = "Generation/Biomes/SurfaceBiome")]
public class SBiomeInfo : CInfo<SurfaceBiome> {}

/// <summary>
/// The placement conditions for a <see cref="SBiomeInfo">surface biome</see>. The aggregate
/// conditions define a cuboid region within a 6D sample space, or all possible values for
/// 6 different noise parameters each bounded between the range 0 to 1. 
/// See <seealso href="https://blackmagic919.github.io/AboutMe/2024/08/10/Biomes/#/Decision-Matrix">
/// here</seealso> for more information. </summary>
[Serializable][StructLayout(LayoutKind.Sequential)]
public struct SurfaceBiome : IBiomeCondition{
    /// <summary> The lower bound of the surface height that this biome can generate in. The surface height is
    /// determined by noise maps identified in <see cref="Surface"/>, where the range is remapped from 
    /// 0 to <see cref="Surface.MaxTerrainHeight"/> to 0 to 1. </summary>
    [Range(0, 1)]
    public float TerrainStart;
    /// <summary> The lower bound on the erosion intensity that this biome can generate in. That is the value of erosion <b>before</b>
    /// it is interpolated through an <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Surface.ErosionNoise"/> for more info. </summary>
    [Range(0, 1)]
    public float ErosionStart;
    /// <summary> The lower bound on the squash intensity that this biome can generate in. That is the value of the squash map <b>before</b>
    /// it is interpolated through an <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Surface.SquashNoise"/> for more info. </summary>
    [Range(0, 1)]
    public float SquashStart;
    /// <summary> The lower bound on the influence height that this biome can generate in. That is the height of the surface biome influences generation
    /// <b>before</b> it is interpolated through the <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Surface.InfHeightNoise"/> for more info </summary>
    [Range(0, 1)]
    public float InfluenceStart;
    /// <summary> The lower bound on the Influence Offset that this biome can generate in. That is the offset relative to the terrain's surface
    /// of the Influence's range <b>before</b> it is interpolated through the <see cref="Noise.interpolation"/> curve, which is useful in 
    /// identifying how interpolation will affect the map in its immediate vicinity. See <seealso cref="Surface.InfOffsetNoise"/> for more info </summary>
    [Range(0, 1)]
    public float InfluenceOffStart;
    ///<summary> The lower bound on the Atmosphere Falloff intensity that this biome can generate in. That is the Intensity density falls off as height 
    ///increases above the terrain's surface <b>before</b> it is interpolated through the <see cref="Noise.interpolation"/> curve, which is useful in 
    ///identifying how interpolation will affect the map in its immediate vicinity. See <seealso cref="Surface.InfOffsetNoise"/> for more info </summary>
    [Range(0, 1)]
    public float AtmosphereStart;

    /// <summary>  The upper bound of the surface height that this biome can generate in. See <seealso cref="TerrainStart"/> for more info </summary>
    [Space(20)]
    [Range(0, 1)]
    public float TerrainEnd;
    /// <summary> The upper bound on the erosion intensity that this biome can generate in. See <seealso cref="ErosionStart"/> for more info </summary>
    [Range(0, 1)]
    public float ErosionEnd;
    /// <summary> The upper bound on the squash intensity that this biome can generate in. See <seealso cref="SquashStart"/> for more info </summary>
    [Range(0, 1)]
    public float SquashEnd;
    /// <summary> The upper bound on the influence height that this biome can generate in. See <seealso cref="InfluenceStart"/> for more info </summary>
    [Range(0, 1)]
    public float InfluenceEnd;
    /// <summary> The upper bound on the Influence Offset that this biome can generate in. See <seealso cref="InfluenceOffStart"/> for more info </summary>
    [Range(0, 1)]
    public float InfluenceOffEnd;
    /// <summary>  The upper bound on the Atmosphere Falloff intensity that this biome can generate in. See <seealso cref="AtmosphereStart"/> for more info </summary>
    [Range(0, 1)]
    public float AtmosphereEnd;

    /// <exclude><remarks> This is an internal pointer to the biome's generation information that is populated by the <see cref="BDict"/> </remarks> </exclude>
    [HideInInspector]
    [UISetting(Ignore = true)]
    [JsonIgnore]
    public int biome;

    /// <summary> See <seealso cref="IBiomeCondition.GetDimensions"/> for more info </summary>
    /// <returns> The number of conditions that a surface biome consideres when being placed. 
    /// This number is always 6 for a surface biome. </returns>
    public readonly int GetDimensions() => 6;
    /// <summary> See <seealso cref="IBiomeCondition.GetBoundDimension"/> for more info </summary>
    /// <param name="bound">The RegionBound whose dimensions are set in-order from the conditions
    /// stored within the <see cref="SurfaceBiome"/>. </param>
    public readonly void GetBoundDimension(ref BDict.RegionBound bound){
        bound.SetBoundDimension(0, TerrainStart, TerrainEnd);
        bound.SetBoundDimension(1, ErosionStart, ErosionEnd);
        bound.SetBoundDimension(2, SquashStart, SquashEnd);
        bound.SetBoundDimension(3, InfluenceStart, InfluenceEnd);
        bound.SetBoundDimension(4, InfluenceOffStart, InfluenceOffEnd);
        bound.SetBoundDimension(5, AtmosphereStart, AtmosphereEnd);
    }

    /// <summary> <seealso cref="IBiomeCondition.SetNode">See Here</seealso> </summary>
    /// <param name="bound">The region bound which stores the in-order dimensions that describe
    /// the bounds of conditions within the <see cref="SurfaceBiome"/> which are set respectively </param>
    /// <param name="biome"></param>
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
    /// <summary> Asserts that the lower bounds of biomes are less than the upper bounds. </summary>
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
