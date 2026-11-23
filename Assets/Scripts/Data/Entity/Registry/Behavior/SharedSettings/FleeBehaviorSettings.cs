using System;
using Newtonsoft.Json;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class FleeBehaviorSettings : IBehaviorSetting {
        [JsonIgnore] public static string Name => "Flee";
        public int fleeDist;
        public float detectDist;
        public bool FightAggressor;

        public object Clone() {
            return new FleeBehaviorSettings {
                fleeDist = this.fleeDist,
                detectDist = this.detectDist,
                FightAggressor = this.FightAggressor
            };
        }
    }
}