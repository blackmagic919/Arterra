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
        ///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Medium";
        public InteractType interactType = InteractType.Terrestrial;
        public float HoldBreathTime;
        public float Bouyancy;
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
                Bouyancy = Bouyancy
            };
        }
    }
    public class MapInteractBehavior : SpeciesBehavior {
        [JsonIgnore] public MapInteractorSettings settings;
        public MapInteractorSettings.InteractType Interaction;
        private Modifier mod;
        private VitalityBehavior vitality;

        private float maxHoldBreathTime => Modifier.Get(mod, MSettings.HoldBreathTime, settings.HoldBreathTime);
        [JsonIgnore] public float breathPercent => breath / maxHoldBreathTime;
        public const float FallDmgThresh = 10;
        public float breath;


        public void SetInteractionType(MapInteractorSettings.InteractType type) => Interaction = type;
        public void ResetInteractionType() => Interaction = settings.interactType;

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync)
                return;
            if (self.context == BehaviorEntity.UpdateContext.Main)
                return;

            switch (Interaction) {
                case MapInteractorSettings.InteractType.Terrestrial:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolid(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquid(self, self.Collider, dens),
                        OnInGas: (dens) => ProcessInGas(self, dens)
                    );
                    break;
                case MapInteractorSettings.InteractType.Aquatic:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolid(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquidAquatic(self, self.Collider, dens),
                        OnInGas: (dens) => ProcessInGasAquatic(self, self.Collider, dens)
                    );
                    break;
                case MapInteractorSettings.InteractType.SubTerraneal:
                    TerrainInteractor.DetectMapInteraction(self.position,
                        OnInSolid: (dens) => ProcessInSolidSubterraneal(self, dens),
                        OnInLiquid: (dens) => ProcessInLiquidAquatic(self, self.Collider, dens),
                        OnInGas: (dens) => ProcessInGas(self, dens)
                    );
                    break;
                default:
                    ADebug.LogInfo($"MapInteraction Behavior type {settings.interactType} has not been implemented yet");
                    break;
            }
        }

        public void ProcessInSolid(BehaviorEntity.Animal self, float density) {
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_InSolid, self, null, density);
            ProcessSuffocation(self, density);
        }

        public void ProcessSuffocation(BehaviorEntity.Animal self, float density) {
            if (density <= 0) return;
            if (!self.Is(out IAttackable target)) return;
            if (target.IsDead) return;
            EntityManager.AddHandlerEvent(() => target.TakeDamage(density / 255.0f, 0, null));
        }

        public void ProcessInGas(BehaviorEntity.Animal self, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, density);
            breath = maxHoldBreathTime;
        }

        public void ProcessInLiquid(BehaviorEntity.Animal self, TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, density);
            breath = math.max(breath - self.DeltaTime, 0);
            tCollider.transform.velocity += self.DeltaTime * settings.Bouyancy * -EntityJob.cxt.gravity;
            tCollider.useGravity = false;
            if (breath > 0) return;
            //If dead don't process suffocation
            if (self.Is(out IAttackable target) && target.IsDead) return;
            ProcessSuffocation(self, density);
        }

        public void ProcessInLiquidAquatic(BehaviorEntity.Animal self, TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InLiquid, self, null, density);
            breath = math.max(breath - self.DeltaTime, 0);

            if (self.Is(out IAttackable target) && target.IsDead) { //If dead float to the surface
                tCollider.transform.velocity += self.DeltaTime * settings.Bouyancy * -EntityJob.cxt.gravity;
                return; //don't process suffocation
            }

            if (breath > 0) return;
            ProcessSuffocation(self, density);
        }

        public void ProcessInGasAquatic(Entity self, TerrainCollider tCollider, float density) {
            self.eventCtrl.RaiseEvent(Arterra.Core.Events.GameEvent.Entity_InGas, self, null, density);
            breath = maxHoldBreathTime;
        }

        public void ProcessInSolidSubterraneal(Entity self, float density) {
            self.eventCtrl.RaiseEvent(Core.Events.GameEvent.Entity_InSolid, self, null, density);
            //Just don't take damage
        }

        private void ProcessFallDamage(object src, object _, object cxt) {
            bool useGrav; float zVelDelta;
            (useGrav, zVelDelta) = ((bool, float))cxt; 
            if (!useGrav) return;
            if (zVelDelta <= FallDmgThresh) return;
            float damage = zVelDelta - FallDmgThresh;
            double weight = math.max(vitality.stats.weight, 0) / 25;
            double falloff = (math.exp(weight) - 1) / (math.exp(weight) + 1); //rescaled sigmoid
            damage *= (float)falloff;
            EntityManager.AddHandlerEvent(() => vitality.TakeDamage(damage, 0, null));
        }

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MapInteractorSettings), new MapInteractorSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
            if (!self.Is(out mod)) mod = null;
            breath = maxHoldBreathTime;
            if (settings.UseFallDamage) self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_HitGround, ProcessFallDamage);
            ResetInteractionType();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have VitalityBehavior");
            if (!self.Is(out mod)) mod = null;
            if (settings.UseFallDamage) self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_HitGround, ProcessFallDamage);
            ResetInteractionType();
        }

        
    }
}