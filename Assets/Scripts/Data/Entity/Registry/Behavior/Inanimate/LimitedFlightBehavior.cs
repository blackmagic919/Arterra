using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class LimitedFlightSettings : IBehaviorSetting {
        public Optional<float> MaxFlightDistance;
        public Optional<float> MaxFlightTime;
        public Optional<float> MinFlightSpeed;
        public Optional<float> MaxFlightSpeed;

        public object Clone() => new LimitedFlightSettings {
            MaxFlightDistance = MaxFlightDistance,
            MaxFlightTime = MaxFlightTime,
            MinFlightSpeed = MinFlightSpeed,
            MaxFlightSpeed = MaxFlightSpeed
        };

        // Distances/speeds use grid units; time uses simulation seconds.
        public bool ShouldDestroy(double distance, float time, float speed) =>
            (MaxFlightDistance.Enabled && distance >= MaxFlightDistance.Value)
            || (MaxFlightTime.Enabled && time >= MaxFlightTime.Value)
            || (MinFlightSpeed.Enabled && speed < MinFlightSpeed.Value)
            || (MaxFlightSpeed.Enabled && speed > MaxFlightSpeed.Value);
    }

    public class LimitedFlightBehavior : SpeciesBehavior {
        [JsonIgnore] private LimitedFlightSettings settings;
        [JsonProperty] private double flightDistance;
        [JsonProperty] private float flightTime;
        [JsonIgnore] private double3 previousPosition;

        public override void AddBehaviorDependencies(Dictionary<Behaviors, int> hierarchy) =>
            hierarchy.TryAdd(Behaviors.Collider, hierarchy.Count);

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) =>
            hierarchy.TryAdd(typeof(LimitedFlightSettings), new LimitedFlightSettings());

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 coord) {
            Bind(self, setting);
            flightDistance = 0;
            flightTime = 0;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 coord) =>
            Bind(self, setting);

        private void Bind(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting) {
            if (!setting.Is(out settings))
                throw new InvalidOperationException("LimitedFlight requires LimitedFlightSettings.");
            // Preserve accumulated limits on restore without counting load-time repositioning.
            previousPosition = self.transform.position;
            // Launch callbacks finish positioning after Initialize/Deserialize. The
            // handler queue is flushed before simulation, so start measuring afterward.
            EntityManager.AddHandlerEvent(() => {
                if (self.active) previousPosition = self.transform.position;
            });
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context != BehaviorEntity.UpdateContext.Simulation || !self.active) return;
            double3 position = self.transform.position;
            flightDistance += math.distance(previousPosition, position);
            previousPosition = position;
            flightTime += self.DeltaTime;
            if (settings.ShouldDestroy(flightDistance, flightTime, math.length(self.velocity)))
                EntityManager.ReleaseEntity(self.info.rtEntityId);
        }
    }
}
