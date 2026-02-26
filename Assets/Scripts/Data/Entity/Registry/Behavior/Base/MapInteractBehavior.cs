using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arterra.GamePlay.Interaction;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class MapInteractorSettings : IBehaviorSetting{
        public InteractType interactType;
        public bool UseFallDamage = true;
        public enum InteractType {
            Terrestrial, 
            Aquatic,
            SimpleFloat,
            SimpleSink
        }

        public object Clone() {
            return new MapInteractorSettings {
                interactType = interactType,
            };
        }
    }
    public class MapInteractBehavior : IBehavior {
        [JsonIgnore] public MapInteractorSettings settings;
        private VitalityBehavior vitality;

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MapInteractorSettings), new MapInteractorSettings());
        }

        public void Update(BehaviorEntity.Animal self) {
            switch (settings.interactType) {
                case MapInteractorSettings.InteractType.Terrestrial:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => vitality.ProcessInSolid(self, dens),
                        OnInLiquid: (dens) => vitality.ProcessInLiquid(self, ref self.collider, dens),
                        OnInGas: (dens) => vitality.ProcessInGas(self, dens)
                    );
                    break;
                case MapInteractorSettings.InteractType.Aquatic:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => vitality.ProcessInGasAquatic(self, ref self.collider, dens),
                        OnInLiquid: (dens) => vitality.ProcessInLiquidAquatic(self, ref self.collider, dens),
                        OnInGas: (dens) => vitality.ProcessInGasAquatic(self, ref self.collider, dens)
                    );
                    break;
                default:
                    ADebug.LogInfo($"MapInteraction Behavior type {settings.interactType} has not been implemented yet");
                    break;
            }
        }

        public void ProcessFallDamage(float zVelDelta) {
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            double weight = math.max(vitality.stats.weight, 0) / 25;
            double falloff = (math.exp(weight) - 1) / (math.exp(weight) + 1); //rescaled sigmoid
            damage *= (float)falloff;
            EntityManager.AddHandlerEvent(() => vitality.TakeDamage(damage, 0, null));
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
        }

        
    }
}