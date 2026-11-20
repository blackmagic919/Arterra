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
        public Option<MaterialBehavior> PropogateMaterialUpdateBehavior;
        public Option<MaterialBehavior> RandomMaterialUpdateBehavior;
        [Header("Terraforming")]
        public Option<MaterialBehavior> OnRemovingBehavior;
        public Option<MaterialBehavior> OnRemovedBehavior;
        public Option<MaterialBehavior> OnPlacingBehavior;
        public Option<MaterialBehavior> OnPlacedBehavior;
        [Header("Entity")]
        public Option<MaterialBehavior> OnEntityTouchSolidBehavior;
        public Option<MaterialBehavior> OnEntityTouchLiquidBehavior;
        
        [Header("Construction")]
        public Option<MaterialBehavior> ConstructMetaDataBehavior;
        public MultiLooter looter;

        public override void Preset(MaterialData materialData) {
            DynamicTypes ??= new Dictionary<System.Type, object>();

            materialData.Register(PropogateMaterialUpdateBehavior.value.GetType(), PropogateMaterialUpdateBehavior.value);
            materialData.Register(RandomMaterialUpdateBehavior.value.GetType(), RandomMaterialUpdateBehavior.value);
            materialData.Register(OnRemovingBehavior.value.GetType(), OnRemovingBehavior.value);
            materialData.Register(OnPlacingBehavior.value.GetType(), OnPlacingBehavior.value);
            materialData.Register(OnPlacingBehavior.value.GetType(), OnPlacingBehavior.value);
            materialData.Register(OnRemovedBehavior.value.GetType(), OnRemovedBehavior.value);
            materialData.Register(OnPlacedBehavior.value.GetType(), OnPlacedBehavior.value);
            materialData.Register(OnEntityTouchSolidBehavior.value.GetType(), OnEntityTouchSolidBehavior.value);
            materialData.Register(OnEntityTouchLiquidBehavior.value.GetType(), OnEntityTouchLiquidBehavior.value);
            materialData.Register(ConstructMetaDataBehavior.value.GetType(), ConstructMetaDataBehavior.value);
            
            PropogateMaterialUpdateBehavior.value?.Preset(materialData);
            RandomMaterialUpdateBehavior.value?.Preset(materialData);
            OnRemovingBehavior.value?.Preset(materialData);
            OnPlacingBehavior.value?.Preset(materialData);
            OnRemovedBehavior.value?.Preset(materialData);
            OnPlacedBehavior.value?.Preset(materialData);
            OnEntityTouchSolidBehavior.value?.Preset(materialData);
            OnEntityTouchLiquidBehavior.value?.Preset(materialData);
            ConstructMetaDataBehavior.value?.Preset(materialData);
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) 
            => PropogateMaterialUpdateBehavior.value?.PropogateMaterialUpdate(GCoord, ref prng);
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default)
            => RandomMaterialUpdateBehavior.value?.PropogateMaterialUpdate(GCoord, ref prng);

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) 
            => OnRemovingBehavior.value?.OnRemoving(GCoord, caller) ?? false;

        public override bool OnPlacing(int3 GCoord, Entity.Entity caller)
            => OnPlacingBehavior.value?.OnPlacing(GCoord, caller) ?? false;

        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            var item = OnRemovedBehavior.value?.OnRemoved(GCoord, amount);
            if (item != null) return item;
            return looter.LootItem(amount, Names);
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) 
           => OnPlacedBehavior.value?.OnPlaced(GCoord, amount);

        public override void OnEntityTouchSolid(int3 GCoord, Entity.Entity entity) 
            => OnEntityTouchSolidBehavior.value?.OnEntityTouchSolid(GCoord, entity);
        public override void OnEntityTouchLiquid(int3 GCoord, Entity.Entity entity)
            => OnEntityTouchLiquidBehavior.value?.OnEntityTouchLiquid(GCoord, entity);

        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) 
            => ConstructMetaDataBehavior.value?.ConstructMetaData(GCoord, constructor);
    }
}

#pragma warning restore CS1591
