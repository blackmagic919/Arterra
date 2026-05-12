using System;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class FleeBehaviorSettings : IBehaviorSetting {
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