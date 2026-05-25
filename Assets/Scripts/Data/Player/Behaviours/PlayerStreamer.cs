using System;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Arterra.GamePlay;
using Arterra.Utils;
using Newtonsoft.Json;
using UnityEngine;
using Arterra.GamePlay.UI;
using Unity.Mathematics;
using System.Collections.Generic;

namespace Arterra.Data.Entity.Behavior {
    /// <summary>
    /// Root player behavior that wires player-specific behavior dependencies and
    /// handles high-level stream/death detach transitions.
    /// </summary>
    public class PlayerBehavior : ISpeciesBehavior {
        /// <summary>
        /// Canonical behavior-side defaults for player entity settings.
        /// This is used to seed world options and migration bridges.
        /// </summary>
        [JsonIgnore]
        public static readonly BehaviorEntity.AnimalSetting DefaultPlayerAnimalSetting = new() {
            profile = new EntitySetting.ProfileInfo {
                bounds = new uint3(2, 3, 2),
            },
            collider = new Arterra.GamePlay.Interaction.TerrainCollider.Settings {
                size = new float3(1f, 2f, 1f),
            },
            BehaviorList = new List<Behaviors> {
                Behaviors.Collider,
                Behaviors.MapInteraction,
                Behaviors.Indicators,
                Behaviors.Vitality,
                Behaviors.PlayerInventories,
                Behaviors.PlayerCamera,
                Behaviors.PlayerMovement,
                Behaviors.PlayerInteraction,
                Behaviors.PlayerEffects,
                Behaviors.PlayerRoot,
                Behaviors.PlayerBaseLogicHandler
            },
            Settings = new List<ReferenceOption<IBehaviorSetting>> {
                new MapInteractorSettings {
                    interactType = MapInteractorSettings.InteractType.Terrestrial,
                    HoldBreathTime = 10,
                    UseFallDamage = true,
                },
                new PhysicalitySetting {
                    weight = 0.5f,
                    StartHealthPercent = 1f,
                    StartHealthVariance = 0f,
                    MaxHealth = 20,
                    NaturalRegen = 0.01f,
                    InvincTime = 0.25f,
                },
                new PlayerInventorySettings(),
                new PlayerCameraSettings(),
                new PlayerMovementSettings(),
                new PlayerInteractionSettings(),
                new PlayerEffectsSettings(),
            },
        };

        /// <summary>
        /// Creates a writable instance of the default player behavior settings.
        /// </summary>
        public static BehaviorEntity.AnimalSetting CreateDefaultPlayerAnimalSetting() {
            BehaviorEntity.AnimalSetting defaults = DefaultPlayerAnimalSetting;
            List<ReferenceOption<IBehaviorSetting>> clonedSettings = new();

            if (defaults.Settings.value != null) {
                foreach (ReferenceOption<IBehaviorSetting> setting in defaults.Settings.value) {
                    clonedSettings.Add(new ReferenceOption<IBehaviorSetting> {
                        value = setting.value == null ? null : (IBehaviorSetting)setting.value.Clone(),
                    });
                }
            }

            return new BehaviorEntity.AnimalSetting {
                profile = defaults.profile,
                collider = defaults.collider,
                BehaviorList = defaults.BehaviorList.value != null
                    ? new List<Behaviors>(defaults.BehaviorList.value)
                    : new List<Behaviors>(),
                Settings = clonedSettings,
                DynamicTypes = null,
            };
        }

        /// <summary> This player instance's relation to the actual
        /// user's input and what they see. See <see cref="StreamingStatus"/> </summary>
        [JsonProperty]
        private StreamingStatus status = StreamingStatus.Activating;
        [JsonIgnore] private BehaviorEntity.Animal self;

        /// <summary>Declares required player behavior dependencies.</summary>
        public void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) {
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.MapInteraction, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.Vitality, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerInventories, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerCamera, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerMovement, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerInteraction, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerEffects, hierarchy.Count);
            hierarchy.TryAdd(Behaviors.PlayerBaseLogicHandler, hierarchy.Count);
        }

        /// <summary>Initializes root player behavior state for a newly created player entity.</summary>
        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            this.self = self;
            status = StreamingStatus.Live;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Death, TriggerDetach);
        }

        /// <summary>Initializes root player behavior state for a deserialized player entity.</summary>
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            this.self = self;
            status = StreamingStatus.Live;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Death, TriggerDetach);

            if (self.Is(out VitalityBehavior vitality) && vitality.IsDead) {
                self.eventCtrl.RaiseEvent(GameEvent.System_Deserialize, self, null);
                if (self.controller != null) SetLayerRecursively(self.controller.transform, LayerMask.NameToLayer("Default"));
            }
        }

        /// <summary>Disables root player behavior and clears active references.</summary>
        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }

        private void TriggerDetach(object source, object _trgt, object _ctx) {
            if (status == StreamingStatus.Disconnected) return;
            status = StreamingStatus.Disconnected;

            if (source is not BehaviorEntity.Animal player) return;
            // Keep detached corpse/view objects on default rendering layer.
            SetLayerRecursively(player.controller.transform, LayerMask.NameToLayer("Default"));
            GameOverHandler.Activate();
        }

        private static void SetLayerRecursively(Transform obj, int layer) {
            if (obj == null) return;
            obj.gameObject.layer = layer;
            foreach (Transform child in obj) {
                SetLayerRecursively(child, layer);
            }
        }

        private enum StreamingStatus {
            Activating,
            Live,
            Disconnecting,
            Disconnected,
        }
    }
}