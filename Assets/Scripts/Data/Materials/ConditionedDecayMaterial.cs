using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using Arterra.Config.Generation.Structure;
using Arterra.Core.Storage;
using System.Collections.Generic;
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

    /// <summary> A concrete material that will attempt to spread itself to neighboring entries 
    /// when and only when  randomly updated. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/ConditionedDecayMat")]
    public class ConditionedDecayMat : MaterialData {
        public StructureData.CheckInfo SelfCondition;
        public Option<List<ConditionedGrowthMat.MapSamplePoint>> ConversionData;
        public Option<List<MapSampleRegion>> ConvertRegions;
        public ConditionedGrowthMat.MapSamplePoint DefaultDecay;
        public float DefaultDecayChance = 0.5f;


        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            DecayIfNotSatisfied(GCoord, ref prng);

            if(DefaultDecayChance <= prng.NextFloat()) return;  
            DefaultDecay.PlaceEntry(this, GCoord, ref prng);
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            DecayIfNotSatisfied(GCoord, ref prng);
        }

        private void DecayIfNotSatisfied(int3 GCoord, ref Unity.Mathematics.Random prng) {
            foreach (MapSampleRegion region in ConvertRegions.value) {
                if (ConditionedGrowthMat.VerifySampleChecks(
                    this, ConversionData.value,
                    new int2((int)region.checkStart, (int)region.checkEnd),
                    GCoord
                )) continue;

                for (int i = (int)region.convertStart; i <= region.convertEnd; i++) {
                    ConditionedGrowthMat.MapSamplePoint point = ConversionData.value[i];
                    point.PlaceEntry(this, GCoord, ref prng, true);
                } return;
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

        [Serializable]
        public struct MapSampleRegion {
            public uint checkStart;
            public uint checkEnd;
            public uint convertStart;
            public uint convertEnd;
        }
    }
}
