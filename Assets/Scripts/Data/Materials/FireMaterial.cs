using UnityEngine;
using Unity.Mathematics;
using Unity.Burst;
using System;
using System.Collections.Generic;
using Arterra.Core.Storage;
using Arterra.Configuration;
using Arterra.GamePlay.UI;
using Arterra.Data.Entity.Behavior;

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
    [CreateAssetMenu(menuName = "Generation/MaterialData/Behaviors/FireMat")]
    public class FireMaterial : DecayMaterial {
        [Serializable]
        public struct TouchEffect {
            public Effects name;
            [Range(0f, 1f)] public float chance;
            [SerializeReference]
            public ReferenceOption<TempBehavior> behavior;

            public void OnValidate() {
                if (!EffectorSettings.EffectTemplates.TryGetValue(name, out Func<TempBehavior> getBehavior))
                    return;
                TempBehavior newBehavior = getBehavior.Invoke();
                if (newBehavior == null) return;
                TempBehavior existingBehavior = behavior.value;
                if (existingBehavior != null && newBehavior.GetType() == existingBehavior.GetType())
                    return;

                behavior.value = newBehavior;
            }
        }

        /// <summary> The chance that grass will spread to a neighboring entry. </summary>
        [Range(0, 1)]
        public float SpreadChance;
        /// <summary> The tag that must be present on the material for grass to spread to it.  
        /// The object associated with this tag in <see cref="TagRegistry"/> must be of type <see cref="ConverterToolTag"/> </summary>
        public TagRegistry.Tags SpreadTag;
        public Option<List<TouchEffect>> ContactEffects;

        /// <summary> Random Material Update entry used to trigger grass growth. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        [BurstCompile]
        public override void RandomMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng) {
            MapData cur = CPUMapManager.SampleMap(GCoord); //Current 
            if (!SelfCondition.Contains(cur)) return;
            SpreadGrass(GCoord, ref prng);
            base.RandomMaterialUpdate(GCoord, ref prng);
        }


        private void SpreadGrass(int3 GCoord, ref Unity.Mathematics.Random prng) {
            var matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            if (SpreadChance <= 0) return;

            int3 delta = int3.zero;
            MapData cur = CPUMapManager.SampleMap(GCoord);
            for (delta.x = -1; delta.x <= 1; delta.x++) {
                for (delta.y = -1; delta.y <= 1; delta.y++) {
                    for (delta.z = -1; delta.z <= 1; delta.z++) {
                        if (prng.NextFloat() >= SpreadChance) continue;
                        if (math.all(delta == int3.zero)) continue;
                        int3 NCoord = GCoord + delta;
                        MapData neighbor = CPUMapManager.SampleMap(NCoord);
                        if (!IMaterialConverting.CanConvert(neighbor, NCoord,
                                    SpreadTag, out ConverterToolTag tag))
                            continue;

                        int spreadMat = matInfo.RetrieveIndex(tag.ConvertTarget);
                        //No need to spread to same material and save extra data
                        if (spreadMat == neighbor.material) continue;

                        if (!MaterialData.SwapMaterial(NCoord,
                            matInfo.RetrieveIndex(tag.ConvertTarget),
                            out Item.IItem origItem))
                            continue;
                        if (tag.GivesItem)
                            InventoryController.DropItem(origItem, GCoord);
                    }
                }
            }
        }

        public void OnValidate() {
            ContactEffects.value ??= new List<TouchEffect>();
            for (int i = 0; i < ContactEffects.value.Count; i++) {
                TouchEffect effect = ContactEffects.value[i];
                effect.OnValidate();
                ContactEffects.value[i] = effect;
            }
        }

        public override void OnEntityTouchSolid(Entity.Entity entity) {
            if (entity == null) return;
            if (!entity.Is(out BehaviorEntity.Animal target)) return;
            if (ContactEffects.value == null) return;

            for (int i = 0; i < ContactEffects.value.Count; i++) {
                TouchEffect effect = ContactEffects.value[i];
                TempBehavior behavior = effect.behavior.value;
                if (behavior == null) continue;
                if (effect.chance < target.random.NextFloat()) continue;
                target.TryAddBehavior(behavior.Create());
            }
        }
    }
}
