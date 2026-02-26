using System;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;

using Arterra.Configuration;
using Arterra.Editor;
using Arterra.Data.Entity;
using Unity.Mathematics;
using Arterra.Data.Item;
using Arterra.Data.Structure;
using Arterra.Core.Storage;
using Arterra.Data.Material;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class FindPlantBehaviorSettings : IBehaviorSetting {
        [JsonIgnore]
        [UISetting(Ignore = true)]
        [HideInInspector]
        internal Dictionary<int, int> AwarenessTable;
        public Option<List<Plant>> Prey;
        public bool HasPlantyPrey => Prey.value != null && Prey.value.Count > 0;

        [Serializable]
        public struct Plant{
            [RegistryReference("Materials")]
            public string Material;
            [RegistryReference("Materials")]
            //If null, gradually removes it
            public string Replacement;
            public StructureData.CheckInfo Bounds;
        }

        public object Clone() {
            return new FindPlantBehaviorSettings() {
                Prey = Prey
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            IRegister mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            AwarenessTable ??= new Dictionary<int, int>();
            if(Prey.value == null) return;

            for(int i = 0; i < Prey.value.Count; i++){
                int materialIndex = mReg.RetrieveIndex(Prey.value[i].Material);
                AwarenessTable.TryAdd(materialIndex,  i);
            }
        }
        public bool FindPreferredPreyPlant(int3 center, int pFindDist, out int3 entry){
            int3 dx = new(0); entry = new int3(0);
            if(Prey.value == null || Prey.value.Count == 0) return false;
            Dictionary<int, int> Awareness = AwarenessTable;
            float minDist = -1;
            for(dx.x = -pFindDist; dx.x < pFindDist; dx.x++){
            for(dx.y = -pFindDist; dx.y < pFindDist; dx.y++){
            for(dx.z = -pFindDist; dx.z < pFindDist; dx.z++){
                int3 GCoord = center + dx;
                MapData mapInfo = CPUMapManager.SampleMap(GCoord);
                if(!Awareness.TryGetValue(mapInfo.material, out int preference)) continue;
                if(!Prey.value[preference].Bounds.Contains(mapInfo)) continue;
                float dist = math.csum(math.abs(dx)); //manhattan distance
                if(minDist == -1 || dist < minDist) {
                    minDist = dist;
                    entry = GCoord;
                } 
            }}}
            return minDist != -1;
        }

        public IItem ConsumePlant(Entity self, int3 preyCoord){
            MapData mapData = CPUMapManager.SampleMap(preyCoord);
            if (mapData.IsNull) return null;
            
            Dictionary<int, int> Awareness = AwarenessTable;
            if(!Awareness.TryGetValue(mapData.material, out int preference)) return null;
            Plant plant = Prey.value[preference];
            if(!plant.Bounds.Contains(mapData)) return null;
            Catalogue<MaterialData> matInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;

            string key = plant.Replacement;
            if (!String.IsNullOrEmpty(key) && matInfo.Contains(key)) {
                int newMaterial = matInfo.RetrieveIndex(key);
                if (!MaterialData.SwapMaterial(preyCoord, newMaterial,
                    out IItem nMat, self))
                    return null;
                return nMat;
            } else {
                MaterialInstance authoring = new (preyCoord, mapData.material);
                if (authoring.Authoring.OnRemoving(preyCoord, self))
                    return null;
                IItem nMat =
                    authoring.Authoring.OnRemoved(preyCoord, mapData);
                self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_RemoveMaterial, self, authoring, mapData);
                mapData.viscosity = 0;
                mapData.density = 0;
                CPUMapManager.SetMap(mapData, preyCoord);
                return nMat;
            }
        }

        public bool CanConsume(int3 preyCoord) {
            MapData mapData = CPUMapManager.SampleMap(preyCoord);
            if (mapData.IsNull) return false;
            return AwarenessTable.ContainsKey(mapData.material);
        }
    }
}