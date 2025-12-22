using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using Arterra.Core.Storage;
using static Arterra.Core.Storage.CPUMapManager;
using Utils;
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

namespace Arterra.Config.Generation.Material{
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
    [CreateAssetMenu(menuName = "Generation/MaterialData/LiquidMat")]
    public class LiquidMaterial : MaterialData {
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
            byte ChangeState = (byte)0;
            MapData cur = SampleMap(GCoord); //Current 
            if (cur.IsSolid) return false;

            int material = cur.material;
            MapData[] map = {
                SampleMap(GCoord + CustomUtility.dP[0]), SampleMap(GCoord + CustomUtility.dP[1]),
                SampleMap(GCoord + CustomUtility.dP[2]), SampleMap(GCoord + CustomUtility.dP[3]),
                SampleMap(GCoord + CustomUtility.dP[4]), SampleMap(GCoord + CustomUtility.dP[5])
            };


            for (int i = 0; i < 6; i++) {
                ChangeState |= (byte)((map[i].IsLiquid ? 1 : 0) << i);
                if (map[i].IsSolid) return false;
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

            SetMap(cur, GCoord, false);
            for (int i = 0; i < 6; i++) {
                //Update the map
                SetMap(map[i], GCoord + CustomUtility.dP[i], false);
                //If state changed, add it to be updated
                if ((((ChangeState >> i) & 0x1) ^ (map[i].IsLiquid ? 1 : 0)) != 0 || map[i].IsSolid)
                    Core.Terrain.TerrainUpdate.AddUpdate(GCoord + CustomUtility.dP[i]);
            }
            return true;
        }


        /// <summary> Mandatory Random Update callback called randomly. Use this to trigger random water propogations </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            if (prng.NextFloat() < RandomUpdatePropogateChance) PropogateLiquid(GCoord, prng);
        }

        /// <summary> Updates the liquid material to perform liquid physics.  </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            PropogateLiquid(GCoord, prng);
        }

        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See 
        /// <see cref="MaterialData.ItemLooter"/> for more info.  </summary>
        public ItemLooter MaterialDrops;

        /// <summary> See <see cref="MaterialData.OnRemoved"/> for more information. </summary>
        /// <param name="amount">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="GCoord">The location of the map information being</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            return MaterialDrops.LootItem(amount, Names);
        }
    }
}
