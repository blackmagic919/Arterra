using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using Arterra.Data.Structure;
using Arterra.Core.Storage;
using Arterra.Configuration;
using Arterra.GamePlay.UI;

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

    /// <summary> A concrete material that will attempt to spread itself to neighboring entries 
    /// when and only when  randomly updated. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/GrassMat")]
    public class GrassMaterial : MaterialData {
        public StructureData.CheckInfo SelfCondition;
        /// <summary> The chance that grass will spread to a neighboring entry. </summary>
        [Range(0, 1)]
        public float SpreadChance;
        /// <summary> The tag that must be present on the material for grass to spread to it.  
        /// The object associated with this tag in <see cref="TagRegistry"/> must be of type <see cref="ConvertableTag"/> </summary>
        public TagRegistry.Tags SpreadTag;


        /// <summary> Random Material Update entry used to trigger grass growth. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            SpreadGrass(cur, GCoord, ref prng);
        }

        /// <summary> Mandatory callback for when grass is forcibly updated. Do nothing here. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            //nothing to do here
        }
        
        private void SpreadGrass(MapData cur, int3 GCoord, ref Unity.Mathematics.Random prng) {
            if (SpreadChance <= 0) return;

            int3 delta = int3.zero;
            for (delta.x = -1; delta.x <= 1; delta.x++) {
                for (delta.y = -1; delta.y <= 1; delta.y++) {
                    for (delta.z = -1; delta.z <= 1; delta.z++) {
                        if (prng.NextFloat() >= SpreadChance) continue;
                        if (math.all(delta == int3.zero)) continue;
                        int3 NCoord = GCoord + delta;
                        MapData neighbor = CPUMapManager.SampleMap(NCoord);
                        if (!IMaterialConverting.CanConvert(neighbor, NCoord,
                                    SpreadTag, out ConvertibleTag tag))
                            continue;
                        if (!SwapMaterial(NCoord,
                            cur.material,
                            out Item.IItem origItem))
                            continue;
                        if (tag.GivesItem)
                            InventoryController.DropItem(origItem, GCoord);
                    }
                }
            }
        }


        /// <summary> The handler controlling how materials are dropped when
        /// <see cref="OnRemoved"/> is called. See <see cref="MaterialData.MultiLooter"/> 
        /// for more info.  </summary>
        public MultiLooter MaterialDrops;


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
