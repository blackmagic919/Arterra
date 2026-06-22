using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Entity.Behavior;
using Arterra.GamePlay.Interaction;
using Newtonsoft.Json;
using UnityEngine;

namespace Arterra.Data.Item {
    [CreateAssetMenu(menuName = "Generation/Items/ConsumableEffector")]
    public class ConsumableEffectorItemAuthoring : AuthoringTemplate<ConsumableEffectorItem> {
        public float ConsumptionRate;
        public float NutritionValue;
        public Option<List<ConsumptionEffect>> Effects;

        [Serializable]
        public struct ConsumptionEffect {
            public Effects name;
            [Range(0f, 1f)] public float chance;
            [SerializeReference]
            public ReferenceOption<TempBehavior> behavior;

            public void OnValidate() {
                if (!EffectorSettings.EffectTemplates.TryGetValue(name, out Func<TempBehavior> getBehavior))
                    return;
                TempBehavior newBehavior = getBehavior.Invoke();
                if (newBehavior == null) return;

                TempBehavior existingBehavior = behavior.value;
                if (existingBehavior != null && newBehavior.GetType() == existingBehavior.GetType())
                    return;

                behavior.value = newBehavior;
            }
        }

        public new void OnValidate() {
            Effects.value ??= new List<ConsumptionEffect>();
            for (int i = 0; i < Effects.value.Count; i++) {
                ConsumptionEffect effect = Effects.value[i];
                effect.OnValidate();
                Effects.value[i] = effect;
            }
        }
    }

    [Serializable]
    public class ConsumableEffectorItem : ConsumbaleItem {
        [JsonIgnore]
        private ConsumableEffectorItemAuthoring EffectorSettings =>
            Config.CURRENT.Generation.Items.Retrieve(Index) as ConsumableEffectorItemAuthoring;

        protected override float ConsumptionRate => EffectorSettings != null ? EffectorSettings.ConsumptionRate : 0f;
        protected override float NutritionValue => EffectorSettings != null ? EffectorSettings.NutritionValue : 0f;

        public override object Clone() => new ConsumableEffectorItem { data = data };

        protected override int ConsumeFood(ItemContext cxt) {
            int consumed = base.ConsumeFood(cxt);
            if (consumed <= 0) return consumed;

            ConsumableEffectorItemAuthoring settings = EffectorSettings;
            if (settings == null || settings.Effects.value == null || settings.Effects.value.Count == 0)
                return consumed;

            if (!cxt.TryGetHolder(out BehaviorEntity.Animal consumer))
                return consumed;

            float multiplier = consumed / (float)UnitSize;
            List<ConsumableEffectorItemAuthoring.ConsumptionEffect> validEffects = settings.Effects.value;
            int start = UnityEngine.Random.Range(0, validEffects.Count);
            for (int i = 0; i < validEffects.Count; i++) {
                ConsumableEffectorItemAuthoring.ConsumptionEffect effect =
                    validEffects[(start + i) % validEffects.Count];
                if ((effect.chance * multiplier) < UnityEngine.Random.value)
                    continue;

                TempBehavior template = effect.behavior.value;
                if (template == null) continue;

                consumer.TryAddBehavior(template.Create(consumer));
                break;
            }

            return consumed;
        }
    }
}
