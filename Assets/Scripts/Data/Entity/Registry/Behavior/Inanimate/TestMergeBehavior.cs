using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class TestMergeSettings : IBehaviorSetting {
        public float MergeRadius = 2f;
        public MergeCheck Check = MergeCheck.EntityType;
        public bool ForceNoConsent = false;

        public enum MergeCheck {
            EntityType,
            Accept
        }

        public object Clone() {
            return new TestMergeSettings {
                MergeRadius = MergeRadius,
                Check = Check,
                ForceNoConsent = ForceNoConsent,
            };
        }
    }

    public class TestMergeBehavior : SpeciesBehavior {
        [SerializeField]
        private TestMergeSettings settings;

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(TestMergeSettings), new TestMergeSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: TestMergeBehavior requires AnimalSettings to have TestMergeSettings");
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: TestMergeBehavior requires AnimalSettings to have TestMergeSettings");
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (settings.MergeRadius <= 0) return;

            Bounds bounds = new Bounds(self.position, new float3(settings.MergeRadius * 2));
            EntityManager.ESTree.Query(bounds, (nEntity) => {
                if (nEntity == null) return;
                if (nEntity.info.rtEntityId == self.info.rtEntityId) return;
                if (!CheckMerge(self, nEntity)) return;

                RefTuple<bool> success = true;
                self.eventCtrl.RaiseEvent(GameEvent.Entity_MergeAbsorb, self, nEntity, success);
                self.eventCtrl.RaiseEvent(GameEvent.Entity_MergeAbsorbed, nEntity, self, success);
                if (success) EntityManager.ReleaseEntity(nEntity.info.entityId);
            });
        }

        private bool CheckMerge(BehaviorEntity.Animal self, Entity target, bool consent = true) {
            bool allow = settings.Check switch {
                TestMergeSettings.MergeCheck.Accept => true,
                TestMergeSettings.MergeCheck.EntityType => self.info.entityType == target.info.entityType,
                _ => false,
            };

            RefTuple<bool> cxt = allow;
            self.eventCtrl.RaiseEvent(GameEvent.Entity_AttemptMerge, self, target, cxt);
            if (!cxt) return false;

           if (!consent || settings.ForceNoConsent) return true;
           if (!target.Is(out TestMergeBehavior tgMerge)) return false;
           return tgMerge.CheckMerge(target.As<BehaviorEntity.Animal>(), self, false);
        }
    }
}
