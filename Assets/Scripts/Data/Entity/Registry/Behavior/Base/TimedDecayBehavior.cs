using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class TimedDecaySettings : IBehaviorSetting {
        public float DecayTime = 5f;
        public Option<List<InteractEvent>> InteractEvents;

        [Serializable]
        public struct InteractEvent {
            public GameEvent Event;
            public float DeltaTime;
        }

        public object Clone() {
            return new TimedDecaySettings {
                DecayTime = DecayTime,
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
        private float timer;

        [JsonIgnore]
        public float DecayTime => settings.DecayTime;

        [JsonIgnore]
        public float DecayedDuration => timer;

        public void ResetDecay() => timer = DecayTime;

        private readonly List<(GameEvent evt, RefEventHandler handler)> dynamicHandlers = new();

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(TimedDecaySettings), new TimedDecaySettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalSettings to have TimedDecaySettings");
            if (!self.Is(out vitality))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalInstance to have VitalityBehavior");

            this.self = self;
            ResetDecay();
            RegisterInteractEvents();
            self.Register<IDecaying>(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalSettings to have TimedDecaySettings");
            if (!self.Is(out vitality))
                throw new Exception("Entity: TimedDecayBehavior requires AnimalInstance to have VitalityBehavior");

            this.self = self;
            timer = timer <= 0 ? DecayTime : math.min(DecayTime, timer);
            RegisterInteractEvents();
            self.Register<IDecaying>(this);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;

            if (!vitality.IsDead) {
                timer = DecayTime;
                return;
            }

            timer -= self.DeltaTime;
            if (timer <= 0) EntityManager.ReleaseEntity(self.info.entityId);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            foreach (var entry in dynamicHandlers) {
                self.eventCtrl.RemoveEventHandler(entry.evt, entry.handler);
            }
            dynamicHandlers.Clear();
            self.Unregister(typeof(IDecaying));
            this.self = null;
        }

        private void RegisterInteractEvents() {
            dynamicHandlers.Clear();
            if (settings.InteractEvents.value == null) return;

            foreach (var interactEvent in settings.InteractEvents.value) {
                GameEvent evt = interactEvent.Event;
                float deltaTime = interactEvent.DeltaTime;
                RefEventHandler handler = (actor, target, cxt) => timer += deltaTime;
                self.eventCtrl.AddEventHandler(evt, handler);
                dynamicHandlers.Add((evt, handler));
            }
        }
    }
}
