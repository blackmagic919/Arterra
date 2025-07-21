using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using WorldConfig.Generation.Structure;
using MapStorage;
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

namespace WorldConfig.Generation.Material{

    /// <summary> A concrete material that will attempt to spread itself to neighboring entries 
    /// when and only when  randomly updated. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/GrassMat")]
    public class GrassMaterial : MaterialData {
        /// <summary> The chance that grass will spread to a neighboring entry. </summary>
        [Range(0, 1)]
        public float SpreadChance;
        /// <summary> The name of the material within the <see cref="MaterialData.Names">name registry</see> of the 
        /// material within the <see cref="WorldConfig.Generation.Material">material registry</see> of the material
        /// to spread onto</summary>
        public int SpreadMaterial;
        /// <summary>The chance that the grass will decay and become <see cref="DecayMaterial"/> when randomly updated. </summary>
        [Range(0, 1)]
        public float DecayChance = 0;
        /// <summary> The name of the material within the <see cref="MaterialData.Names">name registry</see> of the 
        /// material within the <see cref="WorldConfig.Generation.Material">material registry</see> of the material
        /// that grass will become once it decays if it does decay</summary>
        public int DecayMaterial;
        /// <summary> The <see cref="MapData"/> requirements of the material that the grass can spread onto.  </summary>

        [Header("Spread Bounds")]
        public StructureData.CheckInfo SpreadBounds;
        /// <summary> The <see cref="MapData"/> requirements of at least one neighbor of the material that the grass can spread onto.  </summary>
        [Header("Neighbor Bounds")]
        public StructureData.CheckInfo NeighborBounds;
        readonly int3[] dP = new int3[6]{
            new (0,1,0),
            new (0,-1,0),
            new (1,0,0),
            new (0,0,-1),
            new (-1,0,0),
            new (0,0,1),
        };

        /// <summary> Random Material Update entry used to trigger grass growth. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!cur.IsSolid) return;
            SpreadGrass(cur, GCoord, ref prng);
            DecayGrass(GCoord, ref prng);
        }

        /// <summary> Mandatory callback for when grass is forcibly updated. Do nothing here. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            //nothing to do here
        }


        private void SpreadGrass(MapData cur, int3 GCoord, ref Unity.Mathematics.Random prng) {
            var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            if (SpreadChance <= 0) return;
 
            int spreadMat = matInfo.RetrieveIndex(RetrieveKey(SpreadMaterial));
            int3 delta = int3.zero;
            for (delta.x = -1; delta.x <= 1; delta.x++) {
                for (delta.y = -1; delta.y <= 1; delta.y++) {
                    for (delta.z = -1; delta.z <= 1; delta.z++) {
                        if (math.all(delta == int3.zero)) continue;
                        if (prng.NextFloat() >= SpreadChance) continue;
                        int3 NCoord = GCoord + delta;
                        MapData neighbor = CPUMapManager.SampleMap(NCoord);
                        if (!CanSpread(neighbor, NCoord, spreadMat)) continue;
                        if (!SwapMaterial(NCoord, cur.material, out _)) //Discard dropped item
                            continue;
                    }
                }
            }
        }

        private void DecayGrass(int3 GCoord, ref Unity.Mathematics.Random prng) {
            var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

            if (prng.NextFloat() >= DecayChance) return;
            if (!String.IsNullOrEmpty(RetrieveKey(DecayMaterial))) {
                SwapMaterial(GCoord, matInfo.RetrieveIndex(RetrieveKey(DecayMaterial)), out _);
            } else {
                MapData info = CPUMapManager.SampleMap(GCoord);
                if (matInfo.Retrieve(info.material).OnRemoving(GCoord, null))
                    return;
                matInfo.Retrieve(info.material).OnRemoved(GCoord, info);
                info.viscosity = 0;
                info.density = 0;
                CPUMapManager.SetMap(info, GCoord);
            }
        }

        private bool CanSpread(MapData neighbor, int3 GCoord, int SpreadMat) {
            if (SpreadMat != neighbor.material)
                return false;
            if (!SpreadBounds.Contains(neighbor)) return false;
            for (int i = 0; i < 6; i++) {
                MapData nNeighbor = CPUMapManager.SampleMap(GCoord + dP[i]);
                if (!NeighborBounds.Contains(nNeighbor)) continue;
                return true;
            }
            return false;
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
