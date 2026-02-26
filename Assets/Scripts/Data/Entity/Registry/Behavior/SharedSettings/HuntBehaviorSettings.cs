using System;
using Arterra.Configuration;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class HuntBehaviorSettings : IBehaviorSetting {
        public Genetics.GeneFeature HuntThreshold;
        public Genetics.GeneFeature StopHuntThreshold;

        public object Clone() {
            return new HuntBehaviorSettings(){
                HuntThreshold = HuntThreshold,
                StopHuntThreshold = StopHuntThreshold
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref HuntThreshold);
            Genetics.AddGene(entityType, ref StopHuntThreshold);
        }
    }
}