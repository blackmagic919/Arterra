using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Core.Storage;
using Arterra.Engine.Audio;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Data.Material;

namespace Arterra.Data.Entity.Behavior {
    public class StepSoundBehaviorSettings : IBehaviorSetting {
        public float StepDistance = 1.5f;
        public State TouchState = State.Solid;
        public AudioEvents entityStepSound = AudioEvents.None;
        public enum State {
            Solid, Liquid, Both
        }

        public object Clone() {
            return new StepSoundBehaviorSettings {
                StepDistance = StepDistance,
            };
        }
    }

    public class StepSoundBehavior : SpeciesBehavior {
        [JsonIgnore] public StepSoundBehaviorSettings settings;

        private BehaviorEntity.Animal self;

        private float3 lastPosition;
        private float distFromLastStep;

        private Catalogue<MaterialData> MaterialRegistry => Config.CURRENT.Generation.Materials.value.MaterialDictionary;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
            hierarchy.TryAdd(typeof(StepSoundBehaviorSettings), new StepSoundBehaviorSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: StepSoundBehavior requires AnimalSettings to have StepSoundBehaviorSettings");
                
            this.self = self;
            lastPosition = self.position;
            distFromLastStep = 0f;
            HookStepEvents();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: StepSoundBehavior requires AnimalSettings to have StepSoundBehaviorSettings");
            
            this.self = self;
            lastPosition = self.position;
            distFromLastStep = 0f;
            HookStepEvents();
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync || 
                self.context == BehaviorEntity.UpdateContext.Main) return;

            float frameDistance = math.distance(lastPosition, self.position);
            distFromLastStep += frameDistance;
            lastPosition = self.position;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_TouchMatSolid, OnTouchMaterial);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_TouchMatLiquid, OnTouchMaterial);
            this.self = null;
            distFromLastStep = 0f;
        }

        private void OnTouchMaterial(object source, object target, object cxt) {
            if (self == null) return;
            if (distFromLastStep < math.max(settings.StepDistance, 0f)) return;
            if (target is not MapData mapData) return;
            if (cxt is not int3 contactCoord) return;
            //solids are tested underneath player
            if (mapData.IsSolid && !IsContactBelowCollider(contactCoord)) return;
            if (mapData.IsNull) return;

            AudioEvents contactEvent = GetContactSound(mapData.material);
            AudioEvents entityStepEvent = settings.entityStepSound;
            if (contactEvent == AudioEvents.None && entityStepEvent == AudioEvents.None)
                return;

            distFromLastStep = 0f;
            EntityManager.AddHandlerEvent(TryPlayStep);

            void TryPlayStep() {
                if (self == null || !self.active) return;
                float weight = math.clamp(1.0f - math.exp(-0.01f * self.weight), 0.0f, 0.999f);
                if (contactEvent != AudioEvents.None) {
                    FMOD.Studio.EventInstance e = AudioManager.CreateEvent(contactEvent, self.position);
                    e.setParameterByName("weight", weight);  
                } if (entityStepEvent != AudioEvents.None) {
                    FMOD.Studio.EventInstance e = AudioManager.CreateEvent(contactEvent, self.position);
                    e.setParameterByName("weight", weight);  
                }
            }
        }

        private AudioEvents GetContactSound(int material) {
            if (!MaterialRegistry.GetMostSpecificTag(TagRegistry.Tags.ContactSound, material, out object tagObj))
                return AudioEvents.None;
            if (tagObj is not ContactSoundTag soundTag)
                return AudioEvents.None;
            return soundTag.OnTouchEvent;
        }

        private void HookStepEvents() {
            switch (settings.TouchState) {
                case StepSoundBehaviorSettings.State.Solid:
                    self.eventCtrl.AddEventHandler(GameEvent.Entity_TouchMatSolid, OnTouchMaterial);
                    break;
                case StepSoundBehaviorSettings.State.Liquid:
                    self.eventCtrl.AddEventHandler(GameEvent.Entity_TouchMatLiquid, OnTouchMaterial);
                    break;
                default:
                    self.eventCtrl.AddEventHandler(GameEvent.Entity_TouchMatSolid, OnTouchMaterial);
                    self.eventCtrl.AddEventHandler(GameEvent.Entity_TouchMatLiquid, OnTouchMaterial);
                    break;
            }
        }

        private bool IsContactBelowCollider(int3 contactCoord) {
            float3 min = (float3)self.Collider.transform.position;
            float3 max = min + self.Collider.transform.size;
            min.xy--; min.z++; max++; //tolerance

            // Only count contacts under the collider's horizontal footprint.
            if (contactCoord.x < math.floor(min.x) || contactCoord.x > math.ceil(max.x)) return false;
            if (contactCoord.y < math.floor(min.y) || contactCoord.y > math.ceil(max.y)) return false;

            // z is vertical axis in this project; require the touched point to be at/below waist.
            return contactCoord.z <= math.floor(min.z + 1e-3f);
        }
    }
}