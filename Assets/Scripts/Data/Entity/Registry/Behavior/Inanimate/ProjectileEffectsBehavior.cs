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

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            var setting = self.settings;
            if (!setting.Is(out settings))
                throw new Exception("Entity: ProjectileEffectsBehavior requires AnimalSettings to have ProjectileEffectsSettings");

            hasCollided = false;
            active = true;
            self.eventCtrl.AddEventHandler(Core.Events.GameEvent.Entity_ProjectileHit, OnProjectileHit);
            flybyInstance = AudioManager.CreateEventAttached(settings.FlybySound, self.controller.gameObject);
        }



        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Visual) return;
            if (!active || hasCollided) return;

            float speed = math.length(self.velocity);
            speed = 1.0f - math.exp(-0.1f * speed);
            flybyInstance.setParameterByName("Speed", speed);
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (!active) return;
            active = false;

            self.eventCtrl.RemoveEventHandler(Core.Events.GameEvent.Entity_ProjectileHit, OnProjectileHit);
            flybyInstance.stop(STOP_MODE.ALLOWFADEOUT);
        }

        private void OnProjectileHit(object source, object target, object cxt) {
            if (hasCollided) return;
            hasCollided = true;
            if (source is not Guid sourceId) return;
            if (!EntityManager.TryGetEntity(sourceId, out Entity sourceEntity)) return;
            EntityManager.AddHandlerEvent(() => {
                if (sourceEntity.VisualActive && sourceEntity.active) AudioManager.CreateEvent(settings.HitSound, sourceEntity.position);
            });
        }
    }
}
