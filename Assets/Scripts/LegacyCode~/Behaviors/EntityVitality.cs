using UnityEngine;
using Unity.Mathematics;
using static Arterra.Experimental.BehaviorEntity;
using System;
using Arterra.Configuration;

namespace Arterra.Experimental {

    public class EntityMinimalVitalitySettings : BehaviorConfig, EntityVitality.IVitality{
        public Option<MinimalVitality.Stats> stats;
        public override Type Behavior => typeof(EntityVitality);
        public MinimalVitality.Stats Stats => stats.value;
    }

    public class EntityMediumVitalitySettings : BehaviorConfig, EntityVitality.IVitality {
        public Option<MediumVitality.Stats> stats;
        public override Type Behavior => typeof(EntityVitality);
        public MinimalVitality.Stats Stats => stats.value;
    }

    public class EntityVitalitySettings : BehaviorConfig, EntityVitality.IVitality {
        public Option<Vitality.Stats> stats;
        public override Type Behavior => typeof(EntityVitality);
        public MinimalVitality.Stats Stats => stats.value;
    }

    [Serializable]
    public class EntityVitality : EntityBehavior, IAttackable {

        public interface IVitality {
            public MinimalVitality.Stats Stats {get;}
        }
        

        [HideInInspector]
        public MinimalVitality vitality;
        [HideInInspector]
        public Instance self;
        private Genetics gs;

        public bool IsDead => vitality.IsDead;

        public void TakeDamage(float damage, float3 knockback, Configuration.Generation.Entity.Entity attacker) {
            if (!vitality.Damage(damage)) return;
            Indicators.DisplayDamageParticle(self.position, knockback);
            self.velocity += knockback;
        }

        public void Interact(Configuration.Generation.Entity.Entity caller) {
            self.eventCtrl.RaiseEvent();
        }
        public Configuration.Generation.Item.IItem Collect(float amount) {
            self.eventCtrl.RaiseEvent();
        }



        public override void Initialize(BehaviorConfig config, Instance self, float3 GCoord) {
            this.self = self;
            gs = GetComponent<EntityGenetics>()?.genes;

            if (config is EntityVitalitySettings fullSettings) {
                vitality = new Vitality(fullSettings.stats, gs);
            } else if (config is EntityMediumVitalitySettings medSettings) {
                vitality = new MediumVitality(medSettings.stats, gs);
            } else {
                EntityMinimalVitalitySettings minSettings = (EntityMinimalVitalitySettings)config;
                vitality = new MinimalVitality(minSettings.stats, gs);
            }

            self.RegisterInterface(typeof(IEntityTransform), this);
        }

        public override void Deserialize(BehaviorConfig config, Instance self) {
            this.self = self;
            gs = GetComponent<EntityGenetics>()?.genes;

            vitality.Deserialize((config as IVitality).Stats, gs);
            self.RegisterInterface(typeof(IEntityTransform), this);
        }

        public override void Update(Instance self) {
            vitality.Update(self);
        }
    }
}