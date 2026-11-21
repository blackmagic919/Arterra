using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Core.Network;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class TimedDecaySettings : IBehaviorSetting {
        public float DecayTime = 300f;
        public bool OrganicDecay = true;
        public Option<List<InteractEvent>> InteractEvents;

        [Serializable]
        public struct InteractEvent {
            public GameEvent Event;
            public float DeltaTime;
        }

        public object Clone() {
            return new TimedDecaySettings {
                DecayTime = DecayTime,
                OrganicDecay = OrganicDecay,
                InteractEvents = InteractEvents,
            };
        }
    }

    public interface IDecaying {
        public float DecayTime { get; }
        public float DecayedDuration { get; }
        public float DecayProgress => DecayedDuration / DecayTime;
        public void ResetDecay();
    }

    public class TimedDecayBehavior : SpeciesBehavior, IDecaying {
        [JsonIgnore]
        public TimedDecaySettings settings;

        private BehaviorEntity.Animal self;
        private VitalityBehavior vitality;
        private Modifier mod;
        [JsonProperty] private float timer;
        [JsonProperty] private NetworkVariable<DecayAnchor> decayAnchor = new("DecayAnchor", NetworkPermission.Server);
        private bool decaying;

        [Serializable]
        private struct DecayAnchor {
            public DateTime StartedAt;
            public float Remaining;
            public bool Active;

            public float At(DateTime now) => math.max(Remaining - (float)math.max((now - StartedAt).TotalSeconds, 0), 0);
        }

        private static readonly int RotAmountId = Shader.PropertyToID("RotAmount");
        private readonly List<(Renderer renderer, int material)> rotRenderers = new();
        private MaterialPropertyBlock rotProperties;
        private float lastRotAmount;
        [JsonIgnore] public Func<bool> IsDepleted;
        [JsonIgnore] public Func<bool> CanRelease;

        [JsonIgnore]
        public float DecayTime => Modifier.Get(mod, MSettings.DecompositionTime, settings.DecayTime);

        [JsonIgnore]
        public float DecayedDuration => timer;

        public void ResetDecay() {
            timer = math.max(DecayTime, 0);
            if (self != null && self.IsAuthority && decaying) PublishDecay();
        }

        private void PublishDecay() {
            if (!settings.OrganicDecay) return;
            decayAnchor.Value = new DecayAnchor {
                StartedAt = DateTime.UtcNow,
                Remaining = timer,
                Active = true
            };
        }

        private readonly List<(GameEvent evt, RefEventHandler handler)> dynamicHandlers = new();

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(TimedDecaySettings), new TimedDecaySettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            Bind(self, setting);
            ResetDecay();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) => Bind(self, setting);

        private void Bind(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalSettings to have TimedDecaySettings");
            if (!self.Is(out vitality))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out mod)) mod = null;

            this.self = self;
            RegisterInteractEvents();
            self.Register<IDecaying>(this);
            decayAnchor.Register(self);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Visual) {
                if (!settings.OrganicDecay || !vitality.IsDead) {
                    UpdateRot(0);
                    return;
                }
                float duration = DecayTime;
                float remaining = timer;
                if (!self.IsAuthority) {
                    if (!decayAnchor.HasValue || !decayAnchor.Value.Active) return;
                    remaining = decayAnchor.Value.At(DateTime.UtcNow);
                }
                float rot = duration > 0 ? math.saturate(1 - remaining / duration) : 1;
                UpdateRot(rot);
                return;
            }
            if (!self.IsAuthority || self.context != BehaviorEntity.UpdateContext.Simulation) return;

            if (!vitality.IsDead) {
                if (decaying) {
                    decaying = false;
                    if (settings.OrganicDecay) decayAnchor.Value = default;
                }
                ResetDecay();
                return;
            }

            if (!decaying) {
                decaying = true;
                PublishDecay();
            }
            timer = math.max(timer - self.DeltaTime, 0);
            if (CanRelease != null && !CanRelease()) return;
            if (timer <= 0 || IsDepleted?.Invoke() == true) EntityManager.ReleaseEntity(self.info.rtEntityId);
        }

        private void UpdateRot(float amount) {
            if (amount == lastRotAmount) return;
            if (rotProperties == null) {
                rotProperties = new MaterialPropertyBlock();
                foreach (Renderer renderer in self.controller.gameObject.GetComponentsInChildren<Renderer>(true)) {
                    UnityEngine.Material[] materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++) {
                        if (materials[i] != null && materials[i].HasProperty(RotAmountId))
                            rotRenderers.Add((renderer, i));
                    }
                }
            }

            foreach (var entry in rotRenderers) {
                if (entry.renderer == null) continue;
                entry.renderer.GetPropertyBlock(rotProperties, entry.material);
                rotProperties.SetFloat(RotAmountId, amount);
                entry.renderer.SetPropertyBlock(rotProperties, entry.material);
            }
            lastRotAmount = amount;
        }

        public override void Disable(BehaviorEntity.Animal self) {
            foreach (var entry in dynamicHandlers) {
                self.eventCtrl.RemoveEventHandler(entry.evt, entry.handler);
            }
            dynamicHandlers.Clear();
            UpdateRot(0);
            rotRenderers.Clear();
            rotProperties = null;
            decayAnchor.Dispose();
            decaying = false;
            IsDepleted = null;
            CanRelease = null;
            self.Unregister(typeof(IDecaying));
            this.self = null;
        }

        private void RegisterInteractEvents() {
            dynamicHandlers.Clear();
            if (settings.InteractEvents.value == null) return;

            foreach (var interactEvent in settings.InteractEvents.value) {
                GameEvent evt = interactEvent.Event;
                float deltaTime = interactEvent.DeltaTime;
                RefEventHandler handler = (actor, target, cxt) => {
                    if (deltaTime == 0) return;
                    timer += deltaTime;
                    if (decaying) PublishDecay();
                };
                if (self.IsAuthority) self.eventCtrl.AddEventHandler(evt, handler);
                dynamicHandlers.Add((evt, handler));
            }
        }
    }
}
