using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Editor;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ExplodedDecaySettings : IBehaviorSetting {
        [RegistryReference("Entities")]
        public string DropEntity = "EntityItem";

        public object Clone() {
            return new ExplodedDecaySettings {
                DropEntity = DropEntity,
            };
        }
    }

    public class ExplodedDecayBehavior : SpeciesBehavior {
        [JsonIgnore]
        public ExplodedDecaySettings settings;

        private BehaviorEntity.Animal self;
        private VitalityBehavior vitality;
        private Decomposition decomposition;
        private bool exploded;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ExplodedDecaySettings), new ExplodedDecaySettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ExplodedDecayBehavior requires AnimalSettings to have ExplodedDecaySettings");
            if (!self.Is(out vitality))
                throw new Exception("Entity: ExplodedDecayBehavior requires AnimalInstance to have VitalityBehavior");
            setting.Is(out decomposition);

            this.self = self;
            exploded = false;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ExplodedDecayBehavior requires AnimalSettings to have ExplodedDecaySettings");
            if (!self.Is(out vitality))
                throw new Exception("Entity: ExplodedDecayBehavior requires AnimalInstance to have VitalityBehavior");
            setting.Is(out decomposition);

            this.self = self;
            if (!vitality.IsDead) exploded = false;
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;

            if (!vitality.IsDead) {
                exploded = false;
                return;
            }
            if (exploded) return;
            exploded = true;

            float3 dropPosition = self.position;
            EntityManager.ReleaseEntity(self.info.entityId);
            DropAllDecompositionEntries(dropPosition);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }

        private void DropAllDecompositionEntries(float3 position) {
            if (decomposition == null || decomposition.LootTable.value == null) return;

            var entityReg = Config.CURRENT.Generation.Entities;
            if (!entityReg.Contains(settings.DropEntity)) return;
            int dropEntityIndex = entityReg.RetrieveIndex(settings.DropEntity);

            var itemReg = Config.CURRENT.Generation.Items;
            foreach (var loot in decomposition.LootTable.value) {
                if (!itemReg.Contains(loot.ItemName)) continue;

                int itemIndex = itemReg.RetrieveIndex(loot.ItemName);
                IItem droppedItem = itemReg.Retrieve(itemIndex).Item;

                int amountRaw = Mathf.Max(1, Mathf.FloorToInt(loot.DropAmount * droppedItem.UnitSize));
                amountRaw = math.min(amountRaw, droppedItem.StackLimit);
                droppedItem.Create(itemIndex, amountRaw);

                Entity droppedEntity = entityReg.Retrieve(dropEntityIndex).Entity;
                droppedEntity.RegisterConstructor(droppedItem);
                EntityManager.CreateEntity(position, (uint)dropEntityIndex, droppedEntity);
            }
        }
    }
}
