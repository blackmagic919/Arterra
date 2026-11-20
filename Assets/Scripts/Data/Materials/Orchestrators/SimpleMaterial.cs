using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Material
{

    [CreateAssetMenu(menuName = "Generation/MaterialData/SimpleMaterial")]
    public class SimpleMaterial : MaterialData
    {
        public Option<MaterialBehavior> Logic;
        public MultiLooter looter;
        public override void Preset(MaterialData materialData) {
            DynamicTypes ??= new Dictionary<System.Type, object>();
            if (Logic.value == null) return;
            materialData.Register(Logic.value.GetType(), Logic.value);
            Logic.value.Preset(materialData);
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) 
            => Logic.value?.PropogateMaterialUpdate(GCoord, ref prng);
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng)
            => Logic.value?.RandomMaterialUpdate(GCoord, ref prng);

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) 
            => Logic.value?.OnRemoving(GCoord, caller) ?? false;

        public override bool OnPlacing(int3 GCoord, Entity.Entity caller)
            => Logic.value?.OnPlacing(GCoord, caller) ?? false;

        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            var item = Logic.value?.OnRemoved(GCoord, amount);
            if (item != null) return item;
            return looter.LootItem(amount, Names);
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) 
           => Logic.value?.OnPlaced(GCoord, amount);

        public override void OnEntityTouchSolid(int3 GCoord, Entity.Entity entity) 
            => Logic.value?.OnEntityTouchSolid(GCoord, entity);
        public override void OnEntityTouchLiquid(int3 GCoord, Entity.Entity entity)
            => Logic.value?.OnEntityTouchLiquid(GCoord, entity);

        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) 
            => Logic.value?.ConstructMetaData(GCoord, constructor);
    }
}
