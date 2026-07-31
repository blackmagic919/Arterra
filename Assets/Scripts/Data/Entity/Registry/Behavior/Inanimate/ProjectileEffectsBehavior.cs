using System;
using System.Collections.Generic;
using Arterra.Engine.Audio;
using FMOD.Studio;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ProjectileEffectsSettings : IBehaviorSetting {
        public AudioEvents FlybySound;
        public AudioEvents HitSound;

        public object Clone() {
            return new ProjectileEffectsSettings {
                FlybySound = FlybySound,
                HitSound = HitSound,
            };
        }
    }

    public class ProjectileEffectsBehavior : SpeciesBehavior {
        [JsonIgnore]
        public ProjectileEffectsSettings settings;

        private EventInstance flybyInstance;
        private bool hasCollided;
        private bool active;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ProjectileEffectsSettings), new ProjectileEffectsSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, Unity.Mathematics.float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileEffectsBehavior requires AnimalSettings to have ProjectileEffectsSettings");

            hasCollided = false;
            active = true;
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_ProjectileHit, OnProjectileHit);
            flybyInstance = AudioManager.CreateEventAttached(settings.FlybySound, self.controller.gameObject);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref Unity.Mathematics.int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileEffectsBehavior requires AnimalSettings to have ProjectileEffectsSettings");

            hasCollided = false;
            active = true;
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_ProjectileHit, OnProjectileHit);
            flybyInstance = AudioManager.CreateEventAttached(settings.FlybySound, self.controller.gameObject);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Job) return;
            if (!active || hasCollided) return;

            float speed = math.length(self.velocity);
            speed = 1.0f - math.exp(-0.1f * speed);
            flybyInstance.setParameterByName("Speed", speed);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            if (!active) return;
            active = false;

            self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_ProjectileHit, OnProjectileHit);
            flybyInstance.stop(STOP_MODE.ALLOWFADEOUT);
        }

        private void OnProjectileHit(object source, object target, object cxt) {
            if (hasCollided) return;
            hasCollided = true;
            if (source is not Entity sourceEntity) return;
            EntityManager.AddHandlerEvent(() => AudioManager.CreateEvent(settings.HitSound, sourceEntity.position));
        }
    }
}
