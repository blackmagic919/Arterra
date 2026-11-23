using System;
using Newtonsoft.Json;
using Arterra.Configuration;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class HuntBehaviorSettings : IBehaviorSetting {
        [JsonIgnore] public static string Name => "Hunting";
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