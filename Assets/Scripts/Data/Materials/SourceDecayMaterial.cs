using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using Arterra.Configuration.Generation.Structure;
using Arterra.Core.Storage;
using System.Collections.Generic;
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

namespace Arterra.Configuration.Generation.Material{

    /// <summary> A concrete material that will attempt to decay to one other state
    /// after a certain amount of time. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/SourceDecayMat")]
    public class SourceDecayMaterial : MaterialData {
        [RegistryReference("Materials")]
        public string SourceMaterial;
        public int MaxSourceDistance;
        public StructureData.CheckInfo SelfCondition;
        public StructureData.CheckInfo SourceCondition;
        public ConditionedGrowthMat.MapSamplePoint DecayResult;
        /// <summary>The chance that the grass will decay and become <see cref="DecayMaterial"/> when randomly updated. </summary>
        [Range(0, 1)]
        public float DecayChance = 0;
        [Range(0, 1)]
        public float DropChance = 0;


        /// <summary> Random Material Update entry used to trigger grass growth. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            if (DecayChance < prng.NextFloat()) return;
            if (!CheckForConnection(GCoord, ref prng)) {
                DecayResult.PlaceEntry(this, GCoord, ref prng, prng.NextFloat() < DropChance);
            }
        }

        /// <summary> Mandatory callback for when grass is forcibly updated. Do nothing here. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            //nothing to do here
        }


        private bool CheckForConnection(int3 GCoord, ref Unity.Mathematics.Random prng) {
            Catalogue<MaterialData> matReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            int sourceMat = matReg.RetrieveIndex(SourceMaterial);
            int decayMat = CPUMapManager.SampleMap(GCoord).material;
            
            Queue<uint> toCheck = new Queue<uint>();
            HashSet<uint> checkedCoords = new HashSet<uint>();
            uint start = EncodePosition(int3.zero, MaxSourceDistance);
            toCheck.Enqueue(start);
            checkedCoords.Add(start);
            while (toCheck.Count > 0) {
                uint encoding = toCheck.Dequeue();
                int3 curCoord = DecodePosition(encoding, MaxSourceDistance) + GCoord;
                MapData curData = CPUMapManager.SampleMap(curCoord);

                if (SourceCondition.Contains(curData) && curData.material == sourceMat)
                    return true;
                if (math.csum(math.abs(curCoord - GCoord)) >= MaxSourceDistance)
                    continue;
                if (curData.IsNull) continue;
                if (curData.material != decayMat) continue;
                if (!SelfCondition.Contains(curData)) continue;

                foreach(int3 delta in CustomUtility.dP){
                    if (math.all(delta == int3.zero))
                        continue;

                    int3 neigh = curCoord - GCoord + delta;
                    uint nEnc = EncodePosition(neigh, MaxSourceDistance);
                    if (checkedCoords.Contains(nEnc)) continue;
                    checkedCoords.Add(nEnc);
                    toCheck.Enqueue(nEnc);
                }
            }
            return false;
        }

        private uint EncodePosition(int3 delta, int extents){
            delta += extents;
            int size = extents * 2 + 1;
            return (uint)(delta.x * (size * size) + delta.y * size + delta.z);
        }

        private int3 DecodePosition(uint encoded, int extents){
            int size = extents * 2 + 1;
            int3 delta = new ((int)encoded / (size * size), 
                        (int)encoded / size % size, 
                        (int)encoded % size);
            return delta - extents;
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
            MapData info = CPUMapManager.SampleMap(GCoord);
            if (info.IsNull || amount.IsNull) return null;
            if (!info.IsSolid) return MaterialDrops.LootItem(amount, Names);

            int SolidDensity = info.SolidDensity - amount.SolidDensity;
            if (SolidDensity < CPUMapManager.IsoValue)
                CPUMapManager.SetExistingMapMeta<object>(GCoord, null);

            return MaterialDrops.LootItem(amount, Names);
        }
    }
}
