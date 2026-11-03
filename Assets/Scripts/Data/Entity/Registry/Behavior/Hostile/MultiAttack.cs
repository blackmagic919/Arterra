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

    public class MultiAttackBehavior : SpeciesBehavior {
        private MultiAttackSettings settings;
        private AnimatedBehavior animated;
        public int SelectedAttack;

        public void OnPrepareAttack(object selfId, object targetId, object cxt) {
            if (selfId is not Guid selfGuid) return;
            if (!EntityManager.TryGetEntity(selfGuid, out Entity self)) return;
            if (self is not BehaviorEntity.Animal selfAnimal) return;
            if (!selfAnimal.IsAuthority || settings.Variants.value.Count == 0) return;
            float attackProb = selfAnimal.random.NextFloat();

            RefTuple<float> data = cxt as RefTuple<float>;
            
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
            // Attack preparation runs in simulation; deliver animation on the main thread.
            self.eventCtrl.EnqueueEvent(GameEvent.Entity_PlayAttackAnimation, selfGuid, targetId, SelectedAttack);
        }

        public void OnAttack(object srcId, object tgtId, object cxt) {
            if (SelectedAttack < 0) return;
            RefTuple<(float dmg, float3 kb)> atk = cxt as RefTuple<(float dmg, float3 kb)>;
            atk.Value.dmg = settings.Variants.value[SelectedAttack].AttackDamage;
            atk.Value.kb = math.normalizesafe(atk.Value.kb) * settings.Variants.value[SelectedAttack].Knockback;
            SelectedAttack = -1;
        }

        private void OnPlayAttackAnimation(object source, object target, object context) {
            if (context is not int variant || (uint)variant >= settings.Variants.value.Count) return;
            if (animated?.animator == null) return;
            animated.SetTrigger(settings.Variants.value[variant].TriggerAnim);
        }


        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(MultiAttackSettings), new MultiAttackSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiAttack Behavior Requires AnimalSettings to have MultiAttackSettings");
            if (!self.Is(out animated)) animated = null;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_PlayAttackAnimation, OnPlayAttackAnimation);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_ReadyAttack, OnPrepareAttack);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, OnAttack);
            SelectedAttack = -1;
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: MultiAttack Behavior Requires AnimalSettings to have MultiAttackSettings");
            if (!self.Is(out animated)) animated = null;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_PlayAttackAnimation, OnPlayAttackAnimation);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_ReadyAttack, OnPrepareAttack);
            if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Attack, OnAttack);
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_PlayAttackAnimation, OnPlayAttackAnimation);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_ReadyAttack, OnPrepareAttack);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Attack, OnAttack);
        }
    }
}
