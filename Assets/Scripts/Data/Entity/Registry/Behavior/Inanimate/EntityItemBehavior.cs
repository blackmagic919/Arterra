using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Editor;
using Arterra.Data.Item;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Core.Storage;

namespace Arterra.Data.Entity.Behavior {
    [Serializable]
    public class EntityItemSettings : IBehaviorSetting {
        public float DecayTime;
        public float MergeRadius;
        [RegistryReference("Items")]
        public string DefaultItem;
        public object Clone() {
            return new EntityItemSettings {
                DecayTime = DecayTime,
                MergeRadius = MergeRadius,
            };
        }
    }

    public class EntityItemBehavior : SpeciesBehavior, IEntitySearchItem, IDecaying {
        [JsonProperty]
        private Registerable<IItem> item = new Registerable<IItem>();

        [JsonProperty]
        private float decomposition;

        private EntityItemSettings settings;
        private BehaviorEntity.Animal self;

        [JsonIgnore]
        public IItem Item => item.Value;

        [JsonIgnore] public float DecayTime => settings.DecayTime;
        [JsonIgnore] public float DecayedDuration => decomposition;
        public void ResetDecay() => decomposition = DecayTime;
        public IItem[] GetItems() => item.Value == null ? null : new[] { item.Value };
        public void SetItem(IItem value) => item.Value = value;

        public bool Collect(Entity target, Action<IItem> collect, float amount) {
            if (item.Value == null) return false;

            IItem ret = (IItem)item.Value.Clone();
            amount *= ret.UnitSize;
            int delta = Mathf.FloorToInt(amount) + (self.random.NextFloat() < math.frac(amount) ? 1 : 0);
            ret.AmountRaw = math.min(delta, ret.AmountRaw);
            item.Value.AmountRaw -= ret.AmountRaw;

            if (item.Value.AmountRaw <= 0) item.Value = null;
            collect(ret);
            TryReleaseIfEmpty();
            return true;
        }

        public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(EntityItemSettings), new EntityItemSettings());
        }

        public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: EntityItemBehavior requires AnimalSettings to have EntityItemSettings");

            this.self = self;
            decomposition = settings.DecayTime;

            if (self.TryGetConstructor(out IItem ctorItem)) {
                item.Value = ctorItem;
            } if (Item == null) {
                var rItems = Config.CURRENT.Generation.Items;
                item.Value = rItems.Retrieve(settings.DefaultItem).Item;
                Item.Create(rItems.RetrieveIndex(settings.DefaultItem), Item.UnitSize);
            }

            self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, OnCollectedFrom);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_AttemptMerge, OnAttemptMerge);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_MergeAbsorb, OnMergedWith);

            self.Register<IEntitySearchItem>(this);
            self.Register<IDecaying>(this);
        }

        public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new Exception("Entity: EntityItemBehavior requires AnimalSettings to have EntityItemSettings");

            this.self = self;
            decomposition = math.min(settings.DecayTime, decomposition);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, OnCollectedFrom);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_AttemptMerge, OnAttemptMerge);
            self.eventCtrl.AddEventHandler(GameEvent.Entity_MergeAbsorb, OnMergedWith);
            self.Register<IEntitySearchItem>(this);
            self.Register<IDecaying>(this);
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.JobSync) return;
            if (TryReleaseIfEmpty()) return;

            decomposition -= self.DeltaTime;
            if (decomposition <= 0) {
                item.Value = null;
                return;
            }
        }

        public override void Disable(BehaviorEntity.Animal self) {
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Collect, OnCollectedFrom);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_AttemptMerge, OnAttemptMerge);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_MergeAbsorb, OnMergedWith);
            self.Unregister(typeof(IDecaying));
            this.self = null;
        }

        private void OnCollectedFrom(object source, object collector, object cxt) {
            if (cxt == null || item.Value == null) return;
            Action<IItem> collect; float amount;
            (collect, amount) = ((Action<IItem>, float))cxt;
            Collect(collector as Entity, collect, amount);
        }

        private void OnAttemptMerge(object source, object target, object cxt) {
            if (cxt is not RefTuple<bool> allowRef) return;
            if (!allowRef.Value) return;

            bool allow = CanMergeByRequestedRule(target);
            allowRef.Value = allow;
        }

        private bool CanMergeByRequestedRule(object target) {
            if (target is not Entity targetEntity) return false;
            if (self == null || !self.active) return false;
            if (!targetEntity.Is(out EntityItemBehavior targetItem)) return false;
            if (targetItem.self == null || !targetItem.self.active) return false;
            if (targetItem.self.info.rtEntityId == self.info.rtEntityId) return false;

            if (Item == null || targetItem.Item == null) return false;
            if (Item.Index != targetItem.Item.Index) return false;
            if (Item.AmountRaw < targetItem.Item.AmountRaw) return false;
            if (Item.AmountRaw >= Item.StackLimit) return false;
            return true;
        }

        private void OnMergedWith(object soruce, object target, object cxt) {
            if (target == null || target is not Entity tEnt) return;
            if (!tEnt.Is(out EntityItemBehavior neighbor)) return;
            if (Item == null || neighbor.Item == null) return;

            int delta = (int)math.floor(neighbor.Item.AmountRaw / neighbor.Item.UnitSize * Item.UnitSize);
            if (delta == 0) return;

            delta = math.min(Item.AmountRaw + delta, Item.StackLimit) - Item.AmountRaw;
            Item.AmountRaw += delta;
            
            delta = (int)math.ceil(delta / Item.UnitSize * neighbor.Item.UnitSize);
            neighbor.Item.AmountRaw -= delta;

            if (neighbor.Item.AmountRaw <= 0) neighbor.item.Value = null;
            else if (cxt is RefTuple<bool> success) success.Value = false;
        }

        private bool TryReleaseIfEmpty() {
            if (self == null || !self.active) return false;
            if (item.Value != null && item.Value.AmountRaw > 0) return false;

            item.Value = null;
            EntityManager.ReleaseEntity(self.info.entityId);
            return true;
        }
    }
}
