using System;
using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.CPUMapManager;
using Arterra.Utils;
using Arterra.Engine.Terrain;

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

namespace Arterra.Data.Material{
    /// <summary>
    /// A concrete material that will attempt to perform liquid physics when updated. Liquid physics
    /// simulate how liquids flow using a small set of specific rules.
    /// <list type="number">
    /// <item> A liquid will try to move from the update entry to the entry below it if it can. </item>
    /// <item> Liquid above the update entry will try to move to the update entry if it can. </item>
    /// <item> Liquid around the update entry will try to average out the liquid levels with the update entry if it can </item>
    /// <item> A neighboring entry will only be updated if the state of the entry changes </item>
    /// <item> A liquid can move between two entries if the entry it is moving to is gaseous, or if both entries are the same material </item>
    /// </list>
    /// This is the default behavior liquids use to emulate liquid physics. If left unchecked, the propogation of liquid
    /// physics will eventually be ratelimited by <see cref="Intrinsic.TerrainUpdation.MaximumTickUpdates"> the maximum 
    /// amount of updates </see> defined by the system at which point liquid physics may prevent other terrain updates from occuring.
    /// </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/LiquidMat")]
    public class LiquidMaterial : MaterialBehavior {
        /// <summary> The chance that a random update will perform checks to 
        /// propogate the liquid. Increasing this value may reduce performance. </summary>
        public float RandomUpdatePropogateChance;
        [BurstCompile]
        private static void TransferLiquid(ref MapData a1, ref MapData b1) {
            int amount = a1.LiquidDensity; //Amount that's transferred
            amount = math.min(b1.density + amount, 255) - b1.density;
            b1.density += amount;
            a1.density -= amount;
        }

        [BurstCompile]
        private static void AverageLiquid(ref MapData min1, ref MapData max1) {
            //make sure max is max liquid and min is min liquid
            if (min1.LiquidDensity >= max1.LiquidDensity) {
                ref MapData temp = ref min1;
                min1 = ref max1;
                max1 = ref temp;
            }

            //Rounds down to the nearest integer
            int amount = (max1.LiquidDensity - min1.LiquidDensity) >> 1;
            amount = math.min(min1.density + amount, 255) - min1.density;
            amount = max1.density - math.max(max1.density - amount, 0);
            min1.density += amount;
            max1.density -= amount;
        }

        /// <summary> Updates the liquid material to perform liquid physics. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material (a liquid material)</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public static bool PropogateLiquid(int3 GCoord, Unity.Mathematics.Random prng) {
            const uint ValueMask = 0x7FFFFFFF; // Exclude the dirty bit from change detection.
            byte ChangeState = 0;
            MapData cur = SampleMap(GCoord);
            if (cur.IsSolid) return false;

            uint original = cur.data & ValueMask;
            int material = cur.material;
            Span<MapData> map = stackalloc MapData[6];
            Span<uint> originalNeighbors = stackalloc uint[6];
            for (int i = 0; i < 6; i++) {
                MapData neighbor = SampleMap(GCoord + CustomUtility.dP[i]);
                if (neighbor.IsSolid) return false;
                map[i] = neighbor;
                originalNeighbors[i] = neighbor.data & ValueMask;
                ChangeState |= (byte)((neighbor.IsLiquid ? 1 : 0) << i);
            }

            //If all liquid or all gaseous, ignore
            if ((ChangeState & 0x3E) == 0x3E && cur.IsLiquid) return false;
            if ((ChangeState & 0x3D) == 0 && cur.IsGaseous) return false;

            if (map[1].material == material || map[1].IsGaseous) {
                TransferLiquid(ref cur, ref map[1]);
                if (map[1].IsLiquid) map[1].material = material;
            }
            if (map[0].material == material) {
                TransferLiquid(ref map[0], ref cur);
            }

            for (int i = 2; i < 6; i++) {
                if (map[i].material == material || map[i].IsGaseous) {
                    AverageLiquid(ref cur, ref map[i]);
                    //If the point became liquid, make it the same material
                    if (map[i].IsLiquid) map[i].material = material;
                }
            }

            if ((cur.data & ValueMask) != original) SetMap(cur, GCoord, false);
            for (int i = 0; i < 6; i++) {
                MapData neighbor = map[i];
                if ((neighbor.data & ValueMask) == originalNeighbors[i]) continue;

                int3 neighborCoord = GCoord + CustomUtility.dP[i];
                SetMap(neighbor, neighborCoord, false);
                if (((ChangeState & (1 << i)) != 0) != neighbor.IsLiquid || neighbor.IsSolid)
                    TerrainUpdate.AddUpdate(neighborCoord);
            }
            return true;
        }


        /// <summary> Mandatory Random Update callback called randomly. Use this to trigger random water propogations </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            if (prng.NextFloat() < RandomUpdatePropogateChance) PropogateLiquid(GCoord, prng);
        }

        /// <summary> Updates the liquid material to perform liquid physics.  </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void PropogateMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            PropogateLiquid(GCoord, prng);
        }
    }
}
