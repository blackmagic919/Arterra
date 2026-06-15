
using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Editor;
using Arterra.Utils;
using Newtonsoft.Json;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ConsumeBehaviorSettings : IBehaviorSetting {
        public Option<RangeMap<Consumable>> Edibles;
        public float ConsumptionRate;

        public static bool HasEdibles(ConsumeBehaviorSettings val) => !(val == null 
            || val.Edibles.value == null || val.Edibles.value.AllowList.value == null
            || val.Edibles.value.AllowList.value == null || val.Edibles.value.AllowList.value.Count == 0);

        public object Clone() {
            return new ConsumeBehaviorSettings(){
                Edibles = Edibles,
                ConsumptionRate = ConsumptionRate,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Catalogue<Item.Authoring> iReg = Config.CURRENT.Generation.Items;
            if(Edibles.value == null) return;
            Edibles.value.Construct(iReg);
        }

        public bool CanConsume(Modifier mod, IItem item, out float nutrition) {
            nutrition = 0;
            if (Edibles.value == null) return false;
            if (!Edibles.value.TryGetInfo(item.Index, out Consumable consumable)) return false;
            nutrition = Modifier.Get(mod, MSettings.Nutrition, consumable.Nutrition);
            nutrition *= (float)item.AmountRaw / item.UnitSize;
            return true;
        }

        public bool CanConsume(int itemIndex, out int preference) {
            return Edibles.value.IsAllowListed(itemIndex, out preference);
        }

        [Serializable]
        public struct Consumable : IRangeBlock {
            [TagOrRegistryReference("Items")]
            public TagOrRegistryReference EdibleType;
            public IRangeBlock.Policy Policy;
            [JsonIgnore]
            public TagOrRegistryReference selection {
                readonly get => EdibleType;
                set => EdibleType = value;
            }
            [JsonIgnore]
            public IRangeBlock.Policy policy {
                readonly get => Policy;
                set => Policy = value;
            }
            public float Nutrition;
        }
    }
}