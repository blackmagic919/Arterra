
using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Editor;
using Newtonsoft.Json;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ConsumeBehaviorSettings : IBehaviorSetting {
        public Option<List<Consumable>> Edibles;
        public float ConsumptionRate;

        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;

        public object Clone() {
            return new ConsumeBehaviorSettings(){
                Edibles = Edibles,
                ConsumptionRate = ConsumptionRate,
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Catalogue<Arterra.Data.Item.Authoring> iReg = Config.CURRENT.Generation.Items;
            AwarenessTable ??= new Dictionary<int, int>();
            if(Edibles.value == null) return;

            for(int i = 0; i < Edibles.value.Count; i++){
                int itemIndex = iReg.RetrieveIndex(Edibles.value[i].EdibleType);
                AwarenessTable.TryAdd(itemIndex, i);
            }
        }

        public bool CanConsume(Genetics genetics, IItem item, out float nutrition) {
            nutrition = 0;
            if (Edibles.value == null) return false;
            if (AwarenessTable == null) return false;
            if (!AwarenessTable.ContainsKey(item.Index)) return false;
            nutrition = genetics.Get(Edibles.value[AwarenessTable[item.Index]].Nutrition);
            nutrition *= (float)item.AmountRaw / item.UnitSize;
            return true;
        }

        public bool CanConsume(int itemIndex, out int preference) {
            return AwarenessTable.TryGetValue(itemIndex, out preference);
        }

        [Serializable]
        public struct Consumable {
            [RegistryReference("Items")]
            public string EdibleType;
            public Genetics.GeneFeature Nutrition;
        }
    }
}