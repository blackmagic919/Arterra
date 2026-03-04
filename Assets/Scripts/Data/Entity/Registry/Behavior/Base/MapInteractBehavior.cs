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
        public InteractType interactType = InteractType.Terrestrial;
        public Genetics.GeneFeature HoldBreathTime = new () {mean = 10, geneWeight = 0.1f, var = 0.3f};
        public bool UseFallDamage = true;
        public enum InteractType {
            Terrestrial, 
            Aquatic,
            SubTerraneal,
            SimpleFloat,
            SimpleSink
        }

        public object Clone() {
            return new MapInteractorSettings {
                interactType = interactType,
                HoldBreathTime = HoldBreathTime,
                UseFallDamage = UseFallDamage,
            };
        }
        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref HoldBreathTime);
        }
    }
    public class MapInteractBehavior : IBehavior {
        [JsonIgnore] public MapInteractorSettings settings;
        public MapInteractorSettings.InteractType Interaction;
        private GeneticsBehavior genetics;
        private Genetics genes => genetics.Genes;
        private VitalityBehavior vitality;

        [JsonIgnore] public float breathPercent => breath / genes.Get(settings.HoldBreathTime);
        public float breath;


        public void SetInteractionType(MapInteractorSettings.InteractType type) => Interaction = type;
        public void ResetInteractionType() => Interaction = settings.interactType;

        public void Update(BehaviorEntity.Animal self) {
            switch (Interaction) {
                case MapInteractorSettings.InteractType.Terrestrial:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolid(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquid(self, ref self.collider, dens),
                        OnInGas: (dens) => ProcessInGas(self, dens)
                    );
                    break;
                case MapInteractorSettings.InteractType.Aquatic:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolid(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquidAquatic(self, ref self.collider, dens),
                        OnInGas: (dens) => ProcessInGasAquatic(self, ref self.collider, dens)
                    );
                    break;
                case MapInteractorSettings.InteractType.SubTerraneal:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolidSubterraneal(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquidAquatic(self, ref self.collider, dens),
                        OnInGas: (dens) => ProcessInGas(self, dens)
                    );
                    break;
                default:
                    ADebug.LogInfo($"MapInteraction Behavior type {settings.interactType} has not been implemented yet");
                    break;
            }
        }

        public void ProcessInSolid(Entity self, float density) {
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_InSolid, self, null, density);
            ProcessSuffocation(self, density);
        }

        public void ProcessSuffocation(Entity self, float density) {
            if (density <= 0) return;
            if (!self.Is(out IAttackable target)) return;
            if (target.IsDead) return;
            EntityManager.AddHandlerEvent(() => target.TakeDamage(density / 255.0f, 0, null));
        }

        public void ProcessInGas(Entity self, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, density);
            breath = genes.Get(settings.HoldBreathTime);
        }

        public void ProcessInLiquid(Entity self, ref TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, density);
            breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
            tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
            tCollider.useGravity = false;
            if (breath > 0) return;
            //If dead don't process suffocation
            if (self.Is(out IAttackable target) && target.IsDead) return;
            ProcessSuffocation(self, density);
        }

        public void ProcessInLiquidAquatic(Entity self, ref TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, density);
            breath = math.max(breath - EntityJob.cxt.deltaTime, 0);
            if (self.Is(out IAttackable target) && target.IsDead) { //If dead float to the surface
                tCollider.transform.velocity += EntityJob.cxt.deltaTime * -EntityJob.cxt.gravity;
                return; //don't process suffocation
            }

            if (breath > 0) return;
            ProcessSuffocation(self, density);
        }

        public void ProcessInGasAquatic(Entity self, ref TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, density);
            breath = genes.Get(settings.HoldBreathTime);
        }

        public void ProcessInSolidSubterraneal(Entity self, float density) {
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_InSolid, self, null, density);
            //Just don't take damage
        }

        private void ProcessFallDamage(object src, object _, object cxt) {
            bool useGrav; float zVelDelta;
            (useGrav, zVelDelta) = ((bool, float))cxt; 
            if (!useGrav) return;
            if (zVelDelta <= Vitality.FallDmgThresh) return;
            float damage = zVelDelta - Vitality.FallDmgThresh;
            double weight = math.max(vitality.stats.weight, 0) / 25;
            double falloff = (math.exp(weight) - 1) / (math.exp(weight) + 1); //rescaled sigmoid
            damage *= (float)falloff;
            EntityManager.AddHandlerEvent(() => vitality.TakeDamage(damage, 0, null));
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MapInteractorSettings), new MapInteractorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have GeneticsBehavior");
            breath = this.genes.Get(settings.HoldBreathTime);
            if (settings.UseFallDamage) self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_HitGround, ProcessFallDamage);
            ResetInteractionType();
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have GeneticsBehavior");
            if (settings.UseFallDamage) self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_HitGround, ProcessFallDamage);
            ResetInteractionType();
        }

        
    }
}