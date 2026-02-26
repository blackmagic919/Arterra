using System;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class FleeBehaviorSettings : IBehaviorSetting {
        public int fleeDist;
        public Genetics.GeneFeature detectDist;
        public bool FightAggressor;

        public object Clone() {
            return new FleeBehaviorSettings {
                fleeDist = this.fleeDist,
                detectDist = this.detectDist,
                FightAggressor = this.FightAggressor
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            Genetics.AddGene(entityType, ref detectDist);
        }
    }
}