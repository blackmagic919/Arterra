using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public enum Effects {
        None,
        Poison,
        Bleeding,
        Nausea,
        Dizziness,
        Blindness
    }
    public class EffectorSettings : IBehaviorSetting {
        public static Dictionary<Effects, Func<TempBehavior>> EffectTemplates = new () {
            { Effects.Poison, () => new PoisonEffect() },
            { Effects.Bleeding, () => new BleedingEffect() },
            { Effects.Nausea, () => new NauseaEffect() },
            { Effects.Dizziness, () => new DizzinessEffect() },
            { Effects.Blindness, () => new BlindnessEffect() }
        };
        public enum Subject {
            Source, Target
        }
        [Serializable]
        public struct EventEffect {
            public GameEvent trigger;
            public Effect effect;
        }
        [Serializable]
        public struct Effect {
            public Effects name;
            public Subject subject;
            public float chance;
            [SerializeReference]
            public ReferenceOption<TempBehavior> behavior;

            public void OnValidate() {
                if (!EffectTemplates.TryGetValue(name, out Func<TempBehavior> getBehavior))
                    return;
                TempBehavior newBehavior = getBehavior.Invoke();
                if (newBehavior == null) return;
                TempBehavior existingBehavior = behavior.value;
                if (existingBehavior != null && newBehavior.GetType() == existingBehavior.GetType())
                    return;

                behavior.value = newBehavior;
            }
        }
        public Option<List<EventEffect>> Effectors;
        [UISetting(Defaulting = true)][HideInInspector]
        public Dictionary<GameEvent, LinkedList<Effect>> TriggerEffects;
        public object Clone() {

            return new EffectorSettings {
                Effectors = Effectors
            };
        }

        public void Preset(uint entityType, BehaviorEntity.AnimalSetting setting) {
            TriggerEffects = new Dictionary<GameEvent, LinkedList<Effect>>();
            foreach (var eventEffect in Effectors.value) {
                if (!TriggerEffects.ContainsKey(eventEffect.trigger))
                    TriggerEffects[eventEffect.trigger] = new LinkedList<Effect>();
                TriggerEffects[eventEffect.trigger].AddLast(eventEffect.effect);
            }
        }

        public void OnValidate(BehaviorEntity.AnimalSetting settings) {
            Effectors.value ??= new List<EventEffect>();
            for (int i = 0; i < Effectors.value.Count; i++) {
                Effectors.value[i].effect.OnValidate();
            }
        }
    }
    public class EffectorBehavior : SpeciesBehavior {
        private EffectorSettings settings;
        private BehaviorEntity.Animal self;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(EffectorSettings), new EffectorSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Effector Behavior Requires AnimalSettings to have EffectorSettings");
            this.self = self;
            HookEffectors();
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Effector Behavior Requires AnimalSettings to have EffectorSettings");
            this.self = self;
            HookEffectors();
        }

        public override void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }

        private void ApplyEffector(object source, object target, GameEvent trigger) {
            if (!settings.TriggerEffects.TryGetValue(trigger, out LinkedList<EffectorSettings.Effect> effects))
                return;
            
            if (source is not BehaviorEntity.Animal src)
                src = null;
            if (target is not BehaviorEntity.Animal tgt)
                tgt = null;

            var node = effects.First;
            while (node != null) {
                EffectorSettings.Effect effect = node.Value;
                node = node.Next;

                if (effect.chance < self.random.NextFloat())
                    continue;
                var subject = effect.subject == EffectorSettings.Subject.Source ? src : tgt;
                if (subject == null) continue;
                subject.TryAddBehavior(effect.behavior.value.Create(self));
            }
        }

        private void HookEffectors() {
            foreach(var Trigger in settings.TriggerEffects.Keys) {
                self.eventCtrl.AddContextlessEventHandler(Trigger,
                    (src, target) => ApplyEffector(src, target, Trigger));
            }
        }
    }
}