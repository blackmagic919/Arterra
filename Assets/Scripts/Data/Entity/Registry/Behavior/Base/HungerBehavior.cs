using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Data.Entity.Behavior;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    class HungerSettings : IBehaviorSetting {
        public Color MaxHungerHealthColor;
        public Color MinHungerHealthColor;
        public Option<List<Modifier>> Modifiers;
        public Option<List<Effect>> Effects;
        public Option<List<Exercise>> Exercises;
        public float startingPercent;
        public float baseStarveRate;
        public float feedMultiplier;

        [Serializable]
        public struct Modifier {
            public AnimationCurve modify;
            public SettingModifier.MType policy;
            public MSettings setting;
            
        }
        
        [Serializable]
        public struct Effect {
            public float boundary;
            public Partition partition;
            public Effects name;
            [SerializeReference]
            public ReferenceOption<TempBehavior> behavior;
            public enum Partition {
                greaterthan, lessthan
            }

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

        [Serializable]
        public struct Exercise {
            public GameEvent trigger;
            public Policy policy;
            public float cost;
            public enum Policy {
                trigger,
                deltaTime,
            }
        }

        public object Clone() {
            return new HungerSettings(){
                Modifiers = this.Modifiers,
                Effects = this.Effects,
                Exercises = this.Exercises,
                startingPercent = this.startingPercent,
                baseStarveRate = this.baseStarveRate,
                MaxHungerHealthColor = this.MaxHungerHealthColor,
                MinHungerHealthColor = this.MinHungerHealthColor,
                feedMultiplier = this.feedMultiplier
            };
        }
        
        public void OnValidate(BehaviorEntity.AnimalSetting settings) {
           //Sort effects into sortedEffects
           if (Effects.value == null) return;
            for(int i = 0; i < Effects.value.Count; i++){
                Effect e = Effects.value[i];
                e.OnValidate();
                Effects.value[i] = e;
            }
        }
    }
    class HungerBehavior : SpeciesBehavior {
        public float HungerPercent;
        private HungerSettings settings;
        private BehaviorEntity.Animal self;
        private InidcatorsBehavior indicators;
        private Modifier mod;
        public Guid[] modifiers;
        public Guid[] behaviors;
        private float StarveRate => Modifier.Get(mod, MSettings.StarveRate, settings.baseStarveRate);
        public bool IsFull => HungerPercent >= 2;

        private void Setup(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, bool resetHunger) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: Hunger Behavior expected Hunger Settings Object");
            if (!self.Is(out mod)) mod = null;
            if (!self.Is(out indicators)) indicators = null;

            this.self = self;

            settings.Exercises.value ??= new List<HungerSettings.Exercise>();
            foreach (HungerSettings.Exercise e in settings.Exercises.value) {
                self.eventCtrl.AddContextlessEventHandler(e.trigger, (s, t) => HandleExerciseEvent(e));
            }

            SyncTrackingArrays();

            UpdateEffects();
            UpdateModifiers();
            UpdateHungerDisplay();
        }

        private void SyncTrackingArrays() {
            settings.Effects.value ??= new List<HungerSettings.Effect>();
            int effectCount = settings.Effects.value.Count;
            if (behaviors == null || behaviors.Length != effectCount) {
                Guid[] previous = behaviors;
                behaviors = new Guid[effectCount];
                if (previous != null)
                    Array.Copy(previous, behaviors, Math.Min(previous.Length, behaviors.Length));
            }

            settings.Modifiers.value ??= new List<HungerSettings.Modifier>();
            int modifierCount = settings.Modifiers.value.Count;
            if (mod == null)
                return;

            if (modifiers == null || modifiers.Length != modifierCount) {
                Guid[] previous = modifiers;
                modifiers = new Guid[modifierCount];
                if (previous != null)
                    Array.Copy(previous, modifiers, Math.Min(previous.Length, modifiers.Length));
            }

            for (int i = 0; i < modifierCount; i++) {
                if (modifiers[i] != Guid.Empty)
                    continue;

                HungerSettings.Modifier m = settings.Modifiers.value[i];
                SettingModifier modifier = new () { type = m.policy, value = 1f };
                mod.ApplyModifier(m.setting, modifier);
                modifiers[i] = modifier.Id;
            }
        }


        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(HungerSettings), new HungerSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            Setup(self, setting, true);
            HungerPercent = math.clamp(settings.startingPercent, 0f, 2f);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            Setup(self, setting, false);
        }


        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (self.context == BehaviorEntity.UpdateContext.Main) return;
            this.HungerPercent = math.clamp(HungerPercent - StarveRate * self.DeltaTime, 0f, 2f);
            SyncTrackingArrays();
            UpdateEffects();
            UpdateModifiers();
            UpdateHungerDisplay();
        }

        private void UpdateHungerDisplay() {
            if (indicators == null || indicators.healthStat == null) return;
            indicators.healthStat.color = Color.Lerp(
                settings.MinHungerHealthColor,
                settings.MaxHungerHealthColor,
                HungerPercent
            );
        }

        private void UpdateModifiers() {
            if (mod == null || modifiers == null) return;
            int modifierCount = math.min(settings.Modifiers.value.Count, modifiers.Length);
            for(int i = 0; i < modifierCount; i++) {
                HungerSettings.Modifier m = settings.Modifiers.value[i];
                if(!mod.TryGetModifier(modifiers[i], out SettingModifier s)) continue;
                s.value = m.modify.Evaluate(HungerPercent);
            }
        }

        private void UpdateEffects() {
            if (settings.Effects.value == null || settings.Effects.value.Count == 0 || behaviors == null)
                return;

            int firstActiveIndex = -1;
            int effectCount = math.min(settings.Effects.value.Count, behaviors.Length);
            for (int i = 0; i < effectCount; i++) {
                HungerSettings.Effect effect = settings.Effects.value[i];
                bool active = (HungerPercent > effect.boundary) ==
                    (effect.partition == HungerSettings.Effect.Partition.greaterthan);

                if (active && firstActiveIndex < 0)
                    firstActiveIndex = i;

                if (active && behaviors[i] == Guid.Empty) {
                    if (effect.behavior.value == null) continue;
                    TempBehavior behavior = effect.behavior.value.Create(self);
                    if (self.TryAddBehavior(behavior))
                        behaviors[i] = behavior.Id;
                    continue;
                }

                if (!active && behaviors[i] != Guid.Empty) {
                    self.RemoveBehavior(behaviors[i]);
                    behaviors[i] = Guid.Empty;
                }
            }
        }

        public void Feed(float delta, bool force = false) {
            if (force) { HungerPercent += delta * settings.feedMultiplier; return; }
            HungerPercent = math.min(HungerPercent + delta * settings.feedMultiplier, 2);
        }

        public void HandleExerciseEvent(HungerSettings.Exercise exercise) {
            if (exercise.policy == HungerSettings.Exercise.Policy.deltaTime)
                HungerPercent = math.max(HungerPercent - exercise.cost * self.DeltaTime, 0f);
            else
                HungerPercent = math.max(HungerPercent - exercise.cost, 0f);
        }
    }
}