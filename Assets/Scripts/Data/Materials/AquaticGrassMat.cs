using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
/*
y
^      0  5        z
|      | /        /\
|      |/         /
| 4 -- c -- 2    /
|     /|        /
|    / |       /
|   3  1      /
+----------->x
*/

namespace WorldConfig.Generation.Material{
/// <summary>  A concrete material that will attempt to perform liquid physics when updated and
/// will also attempt to spread grass to neighboring entries when randomly updated.
/// See <see cref="GrassMaterial"/> and <see cref="LiquidMaterial"/> for more information 
/// on how these two behaviors work. </summary>
[BurstCompile]
[CreateAssetMenu(menuName = "Generation/MaterialData/AquaticGrassMat")]
public class AquaticGrassMaterial : GrassMaterial
{
    /// <summary> Updates the liquid material to perform liquid physics. </summary>
    /// <param name="GCoord">The coordinate in grid space of a map entry that is this material (a liquid material)</param>
    /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
    [BurstCompile]
    public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng){
        LiquidMaterial.PropogateLiquid(GCoord, prng); //Perform liquid physics first
    }
}}
