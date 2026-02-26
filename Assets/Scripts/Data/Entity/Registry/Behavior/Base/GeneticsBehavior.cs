using Unity.Mathematics;
using Newtonsoft.Json;

namespace Arterra.Data.Entity.Behavior {
    public class GeneticsBehavior : IBehavior {
        [JsonProperty] public Genetics Genes;

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            this.Genes = new Genetics(self.info.entityType, ref self.random);
            self.Register(this);
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            self.Register(this);
        }
    }
}

