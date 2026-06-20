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
        public MaterialBehavior Logic;
        public MultiLooter looter;
        public override void Preset(MaterialData materialData) {
            DynamicTypes ??= new Dictionary<System.Type, object>();
            if (Logic == null) return;
            materialData.Register(Logic.GetType(), Logic);
            Logic.Preset(materialData);
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng) 
            => Logic?.PropogateMaterialUpdate(GCoord, ref prng);
        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng)
            => Logic?.PropogateMaterialUpdate(GCoord, ref prng);

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) 
            => Logic?.OnRemoving(GCoord, caller) ?? false;

        public override bool OnPlacing(int3 GCoord, Entity.Entity caller)
            => Logic?.OnPlacing(GCoord, caller) ?? false;

        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            var item = Logic?.OnRemoved(GCoord, amount);
            if (item != null) return item;
            return looter.LootItem(amount, Names);
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) 
           => Logic?.OnPlaced(GCoord, amount);

        public override void OnEntityTouchSolid(Entity.Entity entity) 
            => Logic?.OnEntityTouchSolid(entity);
        public override void OnEntityTouchLiquid(Entity.Entity entity)
            => Logic?.OnEntityTouchLiquid(entity);

        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) 
            => Logic?.ConstructMetaData(GCoord, constructor);
    }
}
