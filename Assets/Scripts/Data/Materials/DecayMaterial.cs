using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using Arterra.Data.Structure;
using Arterra.Core.Storage;
using Arterra.Configuration;
using Arterra.Data.Entity;
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

    /// <summary> A concrete material that will attempt to decay to one other state
    /// after a certain amount of time. </summary>
    [BurstCompile]
    [CreateAssetMenu(menuName = "Generation/MaterialData/DecayMat")]
    public class DecayMaterial : MaterialData, IMaterialConverting {
        /// <summary>The density/viscosity bounds that this material must satisfy before decaying.</summary>
        public StructureData.CheckInfo SelfCondition;
        /// <summary>The density/viscosity bounds that at least one bordering neighbor must satisfy before decaying.</summary>
        public ProfileE NeighborCondition = new () { bounds = new () {data = 0xFF00FF00}, flags = 0 };
        /// <summary>The sampled map point used to place the decayed material result.</summary>
        public ConditionedGrowthMat.MapSamplePoint DecayResult;

        /// <summary>Maps this material's self-check bounds to <see cref="IMaterialConverting"/> conversion bounds.</summary>
        public StructureData.CheckInfo ConvertBounds => SelfCondition;
        /// <summary>Decay validation does not require neighbor checks, so this is always null/accept-all.</summary>
        public ProfileE NeighborBounds => NeighborCondition;

        /// <summary>Returns this instance to satisfy <see cref="IMaterialConverting"/> clone contract.</summary>
        public object Clone() => this;

        /// <summary>The chance that the grass will decay and become <see cref="DecayMaterial"/> when randomly updated. </summary>
        [Range(0, 1)]
        public float DecayChance = 0;


        /// <summary> Random Material Update entry used to trigger grass growth. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!IMaterialConverting.CanConvert(this, cur, GCoord)) return;
            if(DecayChance <= prng.NextFloat()) return;  
            DecayResult.PlaceEntry(this, GCoord, ref prng);
        }

        /// <summary> Mandatory callback for when grass is forcibly updated. Do nothing here. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            //nothing to do here
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
