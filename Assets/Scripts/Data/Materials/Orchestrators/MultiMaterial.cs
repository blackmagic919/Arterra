using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Unity.Mathematics;
using UnityEngine;

#pragma warning disable CS1591

namespace Arterra.Data.Material
{
    public class MaterialBehavior : ScriptableObject, IMaterial {
        /// <summary> Cached Names list from the owning SimpleMaterial, populated during Preset. </summary>
        [SerializeField] protected Option<List<string>> Names;

        public virtual void Preset(MaterialData self) {}

        /// <summary> Returns the registry entry's name of a registry reference coupled with the <see cref="Names">name register</see> </summary>
        /// <param name="index">The index within the <see cref="Names">name register</see> of the name of the reference</param>
        /// <returns>The name of the reference in an external registry or null if a name for <paramref name="index"/> cannot be found. </returns>
        public string RetrieveKey(int index) {
            if (index < 0 || index >= Names.value.Count) return null;
            return Names.value[index];
        }
        public virtual void PropogateMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng){}
        public virtual void RandomMaterialUpdate(int3 GCoord, ref Unity.Mathematics.Random prng){}
        public virtual bool OnRemoving(int3 GCoord, Entity.Entity caller) => false;
        public virtual bool OnPlacing(int3 GCoord, Entity.Entity caller)  => false;
        public virtual Item.IItem OnRemoved(int3 GCoord, in MapData amount) => null;
        public virtual void OnPlaced(int3 GCoord, in MapData amount){}
        public virtual void OnEntityTouchSolid(Entity.Entity entity){}
        public virtual void OnEntityTouchLiquid(Entity.Entity entity){}
        public virtual object ConstructMetaData(int3 GCoord, MaterialData.MetaConstructor constructor) => null;
    }

    [CreateAssetMenu(menuName = "Generation/MaterialData/MultiMaterial")]
    public class MultiMaterial : MaterialData
    {
        public Registry<MaterialBehavior> Behaviors;
        public MultiLooter looter;

        public override void Preset(MaterialData materialData) {
            DynamicTypes ??= new Dictionary<System.Type, object>();

            Behaviors.Construct();
            foreach (var material in Behaviors.Reg) {
                Register(material.Value);
                material.Value.Preset(materialData);
            }
        }

        public override void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            foreach (var material in Behaviors.Reg)
                material.Value.PropogateMaterialUpdate(GCoord, ref prng);
        }

        public override void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default) {
            foreach (var material in Behaviors.Reg)
                material.Value.RandomMaterialUpdate(GCoord, ref prng);
        }

        public override bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            foreach (var material in Behaviors.Reg)
                if (material.Value.OnRemoving(GCoord, caller)) return true;
            return false;
        }

        public override bool OnPlacing(int3 GCoord, Entity.Entity caller) {
            foreach (var material in Behaviors.Reg)
                if (material.Value.OnPlacing(GCoord, caller)) return true;
            return false;
        }

        public override Item.IItem OnRemoved(int3 GCoord, in MapData amount) {
            foreach (var material in Behaviors.Reg) {
                var item = material.Value.OnRemoved(GCoord, amount);
                if (item != null) return item;
            } return looter.LootItem(amount, Names);;
        }

        public override void OnPlaced(int3 GCoord, in MapData amount) {
            foreach (var material in Behaviors.Reg)
                material.Value.OnPlaced(GCoord, amount);
        }

        public override void OnEntityTouchSolid(Entity.Entity entity) {
            foreach (var material in Behaviors.Reg)
                material.Value.OnEntityTouchSolid(entity);
        }

        public override void OnEntityTouchLiquid(Entity.Entity entity) {
            foreach (var material in Behaviors.Reg)
                material.Value.OnEntityTouchLiquid(entity);
        }

        public override object ConstructMetaData(int3 GCoord, MetaConstructor constructor) {
            object result = constructor;
            foreach (var material in Behaviors.Reg)
                result = material.Value.ConstructMetaData(GCoord, constructor);
            return result;
        }
    }
}

#pragma warning restore CS1591
