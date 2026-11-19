using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Core.Network;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using TerrainCollider = Arterra.GamePlay.Interaction.TerrainCollider;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ContactDriveSettings : IBehaviorSetting {
        public float GroundSpeed = 1f;
        public float LiquidSpeed = 10f;
        public float Acceleration = 150f;
        public float RotationSpeed = 180f;
        public float ContactDistance = 0.1f;
        public float Buoyancy = 1f;

        public object Clone() => MemberwiseClone();
    }

    public class ContactDriveBehavior : SpeciesBehavior {
        private const string LiquidGravityState = "ContactDrive::Liquid";
        private ContactDriveSettings settings;
        private BehaviorEntity.Animal self;
        private IRidable rideable;
        [JsonProperty] private NetworkVariable<float> steeringAngle = new("DriveSteeringAngle", NetworkPermission.Server);
        [JsonIgnore] public float SteeringAngle => steeringAngle.Value;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.Rideable, hierarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) =>
            hierarchy.TryAdd(typeof(ContactDriveSettings), new ContactDriveSettings());

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 coord) =>
            Bind(self, setting);

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 coord) =>
            Bind(self, setting);

        private void Bind(BehaviorEntity.Animal entity, BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings) || !entity.Is(out rideable))
                throw new InvalidOperationException("ContactDrive requires ContactDriveSettings and IRidable.");
            self = entity;
            steeringAngle.Register(entity);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Guided, OnGuided);
        }

        private void OnGuided(object mountId, object riderId, object context) {
            if (mountId is not Guid mount || mount != self.info.rtEntityId ||
                riderId is not Guid rider || rider != rideable.RiderTarget ||
                context is not RefTuple<float3> direction) return;
            float3 aim = direction.Value;
            aim.y = 0;
            if (!math.all(math.isfinite(aim)) || math.lengthsq(aim) <= 1e-10f) return;
            steeringAngle.Value = Vector3.Angle(self.velocity, aim);

            bool grounded = TerrainCollider.SampleCollision(self.origin,
                new float3(self.settings.collider.size.x, -settings.ContactDistance, self.settings.collider.size.z));
            if (grounded && math.length(self.velocity) <= settings.GroundSpeed) {
                self.velocity += settings.Acceleration * self.DeltaTime * aim;
                return;
            }
            TerrainInteractor.DetectMapInteraction(self.position + new float3(0, -settings.ContactDistance, 0),
                OnInSolid: null,
                OnInLiquid: density => {
                    if (math.length(self.velocity) <= settings.LiquidSpeed)
                        self.velocity += settings.Acceleration * self.DeltaTime * aim;
                }, OnInGas: null);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation) return;
            bool inLiquid = false;
            TerrainInteractor.DetectMapInteraction(self.position,
                OnInSolid: density => self.eventCtrl.RaiseEvent(GameEvent.Entity_InSolid, self.info.rtEntityId, null, density),
                OnInLiquid: density => {
                    inLiquid = true;
                    self.eventCtrl.RaiseEvent(GameEvent.Entity_InLiquid, self.info.rtEntityId, null, density);
                    self.velocity -= self.DeltaTime * settings.Buoyancy * EntityJob.cxt.gravity;
                },
                OnInGas: density => self.eventCtrl.RaiseEvent(GameEvent.Entity_InGas, self.info.rtEntityId, null, density));
            if (inLiquid)
                self.Collider.SetGravityState(LiquidGravityState, TerrainCollider.GravityPriority.Environment, false);
            else self.Collider.RemoveGravityState(LiquidGravityState);

            float3 heading = new(self.velocity.x, 0, self.velocity.z);
            if (rideable.RiderTarget == Guid.Empty || math.lengthsq(heading) <= 1e-10f) return;
            self.Rotation = Quaternion.RotateTowards(self.Rotation,
                Quaternion.LookRotation(math.normalizesafe(heading)), settings.RotationSpeed * self.DeltaTime);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Guided, OnGuided);
            self.Collider.RemoveGravityState(LiquidGravityState);
            steeringAngle.Dispose();
            this.self = null;
        }
    }
}
