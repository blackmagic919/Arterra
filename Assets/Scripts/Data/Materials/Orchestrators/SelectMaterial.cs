using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;

#pragma warning disable CS1591

namespace Arterra.Data.Material
{

    [CreateAssetMenu(menuName = "Generation/MaterialData/SelectMaterial")]
    public class SelectMaterial : MaterialData
    {
        [Header("Updating")]
        public MaterialBehavior PropogateMaterialUpdateBehavior;
        public MaterialBehavior RandomMaterialUpdateBehavior;
        [Header("Terraforming")]
        public MaterialBehavior OnRemovingBehavior;
        public MaterialBehavior OnRemovedBehavior;
        public MaterialBehavior OnPlacingBehavior;
        public MaterialBehavior OnPlacedBehavior;
        [Header("Entity")]
        public MaterialBehavior OnEntityTouchSolidBehavior;
        public MaterialBehavior OnEntityTouchLiquidBehavior;
        
        [Header("Construction")]
        public MaterialBehavior ConstructMetaDataBehavior;
        public MultiLooter looter;

        public override void Preset(MaterialData materialData) {
            DynamicTypes ??= new Dictionary<System.Type, object>();

            materialData.Register(PropogateMaterialUpdateBehavior.GetType(), PropogateMaterialUpdateBehavior);
            materialData.Register(RandomMaterialUpdateBehavior.GetType(), RandomMaterialUpdateBehavior);
            materialData.Register(OnRemovingBehavior.GetType(), OnRemovingBehavior);
            materialData.Register(OnPlacingBehavior.GetType(), OnPlacingBehavior);
            materialData.Register(OnPlacingBehavior.GetType(), OnPlacingBehavior);
            materialData.Register(OnRemovedBehavior.GetType(), OnRemovedBehavior);
            materialData.Register(OnPlacedBehavior.GetType(), OnPlacedBehavior);
            materialData.Register(OnEntityTouchSolidBehavior.GetType(), OnEntityTouchSolidBehavior);
            materialData.Register(OnEntityTouchLiquidBehavior.GetType(), OnEntityTouchLiquidBehavior);
            materialData.Register(ConstructMetaDataBehavior.GetType(), ConstructMetaDataBehavior);
            
            PropogateMaterialUpdateBehavior?.Preset(materialData);
            RandomMaterialUpdateBehavior?.Preset(materialData);
            OnRemovingBehavior?.Preset(materialData);
            OnPlacingBehavior?.Preset(materialData);
            OnRemovedBehavior?.Preset(materialData);
            OnPlacedBehavior?.Preset(materialData);
            OnEntityTouchSolidBehavior?.Preset(materialData);
            OnEntityTouchLiquidBehavior?.Preset(materialData);
            ConstructMetaDataBehavior?.Preset(materialData);
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) 
            => PropogateMaterialUpdateBehavior?.PropogateMaterialUpdate(GCoord, ref prng);
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default)
            => RandomMaterialUpdateBehavior?.PropogateMaterialUpdate(GCoord, ref prng);

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) 
            => OnRemovingBehavior?.OnRemoving(GCoord, caller) ?? false;

        public override bool OnPlacing(int3 GCoord, Entity.Entity caller)
            => OnPlacingBehavior?.OnPlacing(GCoord, caller) ?? false;

        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            var item = OnRemovedBehavior?.OnRemoved(GCoord, amount);
            if (item != null) return item;
            return looter.LootItem(amount, Names);
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) 
           => OnPlacedBehavior?.OnPlaced(GCoord, amount);

        public override void OnEntityTouchSolid(Entity.Entity entity) 
            => OnEntityTouchSolidBehavior?.OnEntityTouchSolid(entity);
        public override void OnEntityTouchLiquid(Entity.Entity entity)
            => OnEntityTouchLiquidBehavior?.OnEntityTouchLiquid(entity);

        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) 
            => ConstructMetaDataBehavior?.ConstructMetaData(GCoord, constructor);
    }
}

#pragma warning restore CS1591
