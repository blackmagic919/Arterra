using System;
using System.Collections.Generic;
using System.Diagnostics;
using Arterra.GamePlay.Interaction;
using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class ColliderUpdateSettings : IBehaviorSetting{
        public InteractType interactType;
        public enum InteractType {
            Regular,
            NoEntity,
            NoGround,
            None,
            NoUpdate,
        }

        public object Clone() {
            return new ColliderUpdateSettings {
                interactType = interactType,
            };
        }
    }
    public class ColliderUpdateBehavior : IBehavior {
        [JsonIgnore] public ColliderUpdateSettings settings;
        public ColliderUpdateSettings.InteractType Interaction;
        public HashSet<Guid> IgnoredEntities;

        public void Update(BehaviorEntity.Animal self) {
            switch (Interaction) {
                case ColliderUpdateSettings.InteractType.Regular:
                    self.collider.Update(self);
                    self.collider.EntityCollisionUpdate(self, IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.NoEntity:
                    self.collider.Update(self);
                    break;
                case ColliderUpdateSettings.InteractType.NoGround:
                    self.collider.Update(self, tangible: false);
                    self.collider.EntityCollisionUpdate(self, IgnoredEntities);
                    break;
                case ColliderUpdateSettings.InteractType.None:
                    self.collider.Update(self, tangible: false);
                    break;
                default:
                    break;
            }
        }

        public void SetInteractionType(ColliderUpdateSettings.InteractType type) => Interaction = type;
        public void ResetInteractionType() => Interaction = settings.interactType;

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(ColliderUpdateSettings), new ColliderUpdateSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            ResetInteractionType();
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MapInteractBehavior Requires AnimalSettings to have MapInteractorSettings");
            ResetInteractionType();
        }

        
    }
}