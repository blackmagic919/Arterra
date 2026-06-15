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
using Arterra.Utils;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class FindPlantBehaviorSettings : IBehaviorSetting {
        public Option<RangeMap<Plant>> Prey;
        public static bool HasPrey(FindPlantBehaviorSettings val) => !(val == null 
        || val.Prey.value == null || val.Prey.value.AllowList.value == null
        || val.Prey.value.AllowList.value == null || val.Prey.value.AllowList.value.Count == 0);

        [Serializable]
        public struct Plant : IRangeBlock{
            [TagOrRegistryReference("Materials")]
            public TagOrRegistryReference Material;
            public IRangeBlock.Policy Policy;
            [JsonIgnore]
            public TagOrRegistryReference selection {
                readonly get => Material;
                set => Material = value;
            }
            [JsonIgnore]
            public IRangeBlock.Policy policy {
                readonly get => Policy;
                set => Policy = value;
            }

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
            ICatalgoue mReg = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            if(Prey.value == null) return;
            Prey.value.Construct(mReg);
        }
        public bool FindPreferredPreyPlant(int3 center, int pFindDist, out int3 entry){
            int3 dx = new(0); entry = new int3(0);
            if(!HasPrey(this)) return false;
            float minDist = -1;
            for(dx.x = -pFindDist; dx.x < pFindDist; dx.x++){
            for(dx.y = -pFindDist; dx.y < pFindDist; dx.y++){
            for(dx.z = -pFindDist; dx.z < pFindDist; dx.z++){
                int3 GCoord = center + dx;
                MapData mapInfo = CPUMapManager.SampleMap(GCoord);
                if(!Prey.value.TryGetInfo(mapInfo.material, out Plant plant))
                    continue;
                if(!plant.Bounds.Contains(mapInfo)) continue;
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
            
            if(!Prey.value.TryGetInfo(mapData.material, out Plant plant))
                return null;
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
            return Prey.value.IsAllowListed(mapData.material, out int _);
        }
    }
}