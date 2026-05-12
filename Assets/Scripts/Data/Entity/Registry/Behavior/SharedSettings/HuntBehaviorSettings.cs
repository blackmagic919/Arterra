using System;
using Arterra.Configuration;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class HuntBehaviorSettings : IBehaviorSetting {
        public float HuntThreshold;
        public float StopHuntThreshold;

        public object Clone() {
            return new HuntBehaviorSettings(){
                HuntThreshold = HuntThreshold,
                StopHuntThreshold = StopHuntThreshold
            };
        }
    }
}