using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    public class MultiAttackSettings : IBehaviorSetting {
        public Option<List<AttackVariant>> Variants;
        [Serializable]
        public struct AttackVariant {
            public string TriggerAnim;
            public float AttackChance;
            public float AttackDuration;
            public float AttackDamage;
            public float Knockback;
        }
        public object Clone() {
            return new MultiAttackSettings {
                Variants = Variants
            };
        }

        public void Preset(uint EntityType, BehaviorEntity.AnimalSetting setting) {
            float TotalChance = 0;
            Variants.value ??= new List<AttackVariant>();
            foreach(var variant in Variants.value) {
                TotalChance += variant.AttackChance;
            } TotalChance = math.max(TotalChance, 0.001f);

            float prevChance = 0;
            for(int i = 0; i < Variants.value.Count; i++) {
                AttackVariant variant = Variants.value[i];
                variant.AttackChance /= TotalChance;
                variant.AttackChance += prevChance;
                prevChance = variant.AttackChance;
                Variants.value[i] = variant;
            }
        }
    }

    public class MultiAttackBehavior : IBehavior {
        private MultiAttackSettings settings;
        private AnimatedBehavior animated;
        private AttackBehavior attack;
        public int SelectedAttack;
        private bool PlayingAttack;

        public void OnPrepareAttack(object self, object target, object cxt) {
            RefTuple<float> data = cxt as RefTuple<float>;
            float attackProb = (self as BehaviorEntity.Animal).random.NextFloat();
            
            // Binary search to find which attack variant the probability falls into
            int left = 0, right = settings.Variants.value.Count - 1;
            while (left < right) {
                int mid = left + (right - left) / 2;
                if (attackProb < settings.Variants.value[mid].AttackChance) {
                    right = mid;
                } else {
                    left = mid + 1;
                }
            }
            SelectedAttack = left;
            data.Value = settings.Variants.value[SelectedAttack].AttackDuration;
        }

        public void OnAttack(object self, object target, object cxt) {
            if (SelectedAttack < 0) return;
            RefTuple<(float dmg, float3 kb)> atk = cxt as RefTuple<(float dmg, float3 kb)>;
            atk.Value.dmg = settings.Variants.value[SelectedAttack].AttackDamage;
            atk.Value.kb = settings.Variants.value[SelectedAttack].Knockback;
            SelectedAttack = -1;
        }

        public void UpdateController(BehaviorEntity.Animal self, BehaviorEntity.AnimalController controller) {
            if(animated == null || attack == null) return;
            
            if (!attack.AttackInProgress) PlayingAttack = false;
            if (SelectedAttack == -1) return;
            if (!PlayingAttack && attack.AttackInProgress) {
                animated.SetTrigger(settings.Variants.value[SelectedAttack].TriggerAnim);
                PlayingAttack = true;
            }
        }


        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MultiAttackSettings), new MultiAttackSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiAttack Behavior Requires AnimalSettings to have MultiAttackSettings");
            if (!self.Is(out animated)) animated = null;
            if (!self.Is(out attack)) attack = null;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_ReadyAttack, OnPrepareAttack);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, OnAttack);
            SelectedAttack = -1;
            PlayingAttack = false;
        }

        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiAttack Behavior Requires AnimalSettings to have MultiAttackSettings");
            if (!self.Is(out animated)) animated = null;
            if (!self.Is(out attack)) attack = null;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_ReadyAttack, OnPrepareAttack);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, OnAttack);
            PlayingAttack = false;
        }
    }
}