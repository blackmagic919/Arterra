using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Arterra.Data.Biome{
/// <summary>
/// The settings describing the generation of one cave biome. Cave biomes control generation for all space apart from 
/// the surface of the world. Contrary to what its name implies, cave biomes define generation for both 
/// <see cref="GenerationData.SkyBiomes"/> and <see cref="GenerationData.CaveBiomes"/>, however, each container
/// ensures that all of its contents will only generate above or below the surface respectively. Information 
/// regarding generation dictated by the biome is stored within <see cref="Info"/> while information regarding its placement 
/// can be found in <see cref="CaveBiome"/>.
/// </summary>
/// <remarks>This class defines a concrete implementation of <see cref="CInfo{T}"/> so 
/// that its contents can be serialized by Unity.</remarks>
[CreateAssetMenu(menuName = "Generation/Biomes/CaveBiome")]
public class CBiomeInfo : CInfo<CaveBiome> {}
/// <summary>
/// The placement conditions for a <see cref="CBiomeInfo">cave biome</see>. The aggregate
/// conditions define a cuboid region within a 4D sample space, or all possible values for
/// 4 different noise parameters each bounded between the range 0 to 1. See 
/// <seealso href="https://blackmagic919.github.io/AboutMe/2024/08/10/Biomes/#/Decision-Matrix">
/// here</seealso> for more information.
/// </summary>
[Serializable][StructLayout(LayoutKind.Sequential)]
public struct CaveBiome : IBiomeCondition{
    /// <summary> The lower bound on cave frequency that this biome can generate in. That is noise parameter determining the size of caves 
    /// <b>before</b> it is interpolated through an <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Map.CaveFrequencyNoise"/> for more info. </summary>
    [Range(0, 1)]
    public float CaveFreqStart;
    /// <summary> The lower bound on cave size that this biome can generate in. That is the noise parameter determining the size of caves 
    /// <b>before</b> it is interpolated through an <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Map.CaveSizeNoise"/> for more info. </summary>
    [Range(0, 1)]
    public float CaveSizeStart;
    /// <summary> The lower bound on cave shape that this biome can generate in. That is the noise parameter determining the shape of caves 
    /// <b>before</b> it is interpolated through an <see cref="Noise.interpolation"/> curve, which is useful in identifying how interpolation
    /// will affect the map in its immediate vicinity. See <seealso cref="Map.CaveShapeNoise"/> for more info. </summary>
    [Range(0, 1)]
    public float CaveShapeStart;
    /// <summary> The lower bound on surface distance that this biome can generate in. That is the distance from the surface of the map entry
    /// rescaled through an exponential function (see <seealso cref="Map.heightFalloff"/> which approaches 1 as distance approaches
    /// infinity. </summary>
    [Range(0, 1)]
    public float HeightStart;
    /// <summary> The upper bound on cave frequency that this biome can generate in. See <seealso cref="CaveFreqStart"/> for more info </summary>
    [Space(20)]
    [Range(0, 1)]
    public float CaveFreqEnd;
    /// <summary> The upper bound on cave size that this biome can generate in. See <seealso cref="CaveSizeStart"/> for more info </summary>
    [Range(0, 1)]
    public float CaveSizeEnd;
    /// <summary> The upper bound on cave shape that this biome can generate in. See <seealso cref="CaveShapeStart"/> for more info </summary>
    [Range(0, 1)]
    public float CaveShapeEnd;
    /// <summary> The upper bound on surface distance that this biome can generate in. See <seealso cref="HeightStart"/> for more info </summary>
    [Range(0, 1)]
    public float HeightEnd;
    /// <exclude><remarks> This is an internal pointer to the biome's generation information that is populated by the <see cref="BDict"/> </remarks> </exclude>
    [HideInInspector]
    public int biome;

    /// <summary> See <seealso cref="IBiomeCondition.GetDimensions"/> for more info </summary>
    /// <returns> The number of conditions that a cave biome consideres when being placed. 
    /// This number is always 4 for a cave biome. </returns>
    public readonly int GetDimensions() => 4;
    /// <summary> See <seealso cref="IBiomeCondition.GetBoundDimension"/> for more info </summary>
    /// <param name="bound">The RegionBound whose dimensions are set in-order from the conditions
    /// stored within the <see cref="CaveBiome"/>. </param>
    public readonly void GetBoundDimension(ref BDict.RegionBound bound){
        bound.SetBoundDimension(0, HeightStart, HeightEnd);
        bound.SetBoundDimension(1, CaveFreqStart, CaveFreqEnd);
        bound.SetBoundDimension(2, CaveSizeStart, CaveSizeEnd);
        bound.SetBoundDimension(3, CaveShapeStart, CaveShapeEnd);
    }
    /// <summary> <seealso cref="IBiomeCondition.SetNode">See Here</seealso> </summary>
    /// <param name="bound">The region bound which stores the in-order dimensions that describe
    /// the bounds of conditions within the <see cref="CaveBiome"/> which are set respectively </param>
    /// <param name="biome"></param>
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
    /// <summary> Asserts that the lower bounds of biomes are less than the upper bounds. </summary>
    public void Validate(){
        HeightEnd = Mathf.Max(HeightStart, HeightEnd);
        CaveFreqEnd = Mathf.Max(CaveFreqStart, CaveFreqEnd);
        CaveSizeEnd = Mathf.Max(CaveSizeStart, CaveSizeEnd);
        CaveShapeEnd = Mathf.Max(CaveShapeStart, CaveShapeEnd);
    }
}
}
