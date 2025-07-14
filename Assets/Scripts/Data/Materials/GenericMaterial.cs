using System;
using System.Collections;
using System.Collections.Generic;
using MapStorage;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace WorldConfig.Generation.Material
{
    /// <summary> A concrete material type with no explicit interaction behavior. That is,
    /// it does not need to do anything when updated. By default most materials
    /// should not need to do anything. </summary>
    [CreateAssetMenu(menuName = "Generation/MaterialData/GenericMat")]
    public class GenericMaterial : MaterialData {
        /// <summary> Even though it does nothing, it needs to fufill the contract so
        /// that it can be used in the same way as other materials. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {

        }
        /// <summary> Even though it does nothing, it needs to fufill the contract so
        /// that it can be used in the same way as other materials. </summary>
        /// <param name="GCoord">The coordinate in grid space of a map entry that is this material</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) {

        }

        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
        /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is solid. 
        /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
        public int SolidItem;
        /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
        /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is liquid. 
        /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
        public int LiquidItem;

        /// <summary> See <see cref="MaterialData.AcquireItem"/> for more information. </summary>
        /// <param name="mapData">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <returns>The item to give.</returns>
        public override Item.IItem AcquireItem(in MapData mapData) {
            return GenericItemFromMap(mapData, RetrieveKey(SolidItem), RetrieveKey(LiquidItem));
        }

        private static Catalogue<Item.Authoring> ItemInfo => Config.CURRENT.Generation.Items;

        /// <summary>  Creates a generic item from map information of what has been removed. 
        /// This is the standard logic for item acquirement for most materials. Returns a 
        /// solid item if <see cref="MapData.SolidDensity"/> is nonzero, otherwise returns 
        /// a liquid material or null. </summary>
        /// <param name="mapData">The map data indicating the amount of material removed
        /// and the state it was removed as</param>
        /// <param name="SItem">The item index to give if the material is removed as a solid</param>
        /// <param name="LItem">The item index to give if the material is removed as a liquid</param>
        /// <returns></returns>
        public static Item.IItem GenericItemFromMap(in MapData mapData, string SItem, string LItem) {
            if (mapData.IsNull) return null;
            if (mapData.density == 0) return null;
            Item.IItem item;
            if (mapData.SolidDensity > 0) {
                int SIndex = ItemInfo.RetrieveIndex(SItem);
                item = ItemInfo.Retrieve(SIndex).Item;
                item.Create(SIndex, mapData.SolidDensity);
            } else {
                int LIndex = ItemInfo.RetrieveIndex(LItem);
                item = ItemInfo.Retrieve(LIndex).Item;
                item.Create(LIndex, mapData.LiquidDensity);
            }
            return item;
        }
    }
}