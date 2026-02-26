
using System;
using System.Collections.Generic;
using Arterra.Core.Events;
using Arterra.Data.Item;
using Unity.Mathematics;

namespace Arterra.Data.Entity.Behavior {
    public class FeedableBehaviorSettings : IBehaviorSetting {
        public EatCondition CanEat = EatCondition.OnlyWhenHungry;

        public enum EatCondition {
            OnlyWhenHungry,
            OnlyWhenNotFull,
            AlwaysEat,
        }

        public object Clone() {
            return new FeedableBehaviorSettings {
                CanEat = CanEat,
            };
        }
    }
    public class FeedableBehavior : IBehavior {
        private FeedableBehaviorSettings settings;
        private ConsumeBehaviorSettings consume;

        private BehaviorEntity.Animal self;
        private VitalityBehavior vitality;
        private GeneticsBehavior genetics;
        private HuntBehaviorSettings hunt; //optional

        public void OnInteracted(object self, object target, object cxt) {
            if (cxt == null) return;
            IItem item = cxt as IItem;
            switch(settings.CanEat) {
                case FeedableBehaviorSettings.EatCondition.OnlyWhenHungry:
                    if (hunt != null && vitality.healthPercent > genetics.Genes.Get(hunt.HuntThreshold))
                        return;
                    break;
                case FeedableBehaviorSettings.EatCondition.OnlyWhenNotFull:
                    if (hunt != null && vitality.healthPercent > genetics.Genes.Get(hunt.StopHuntThreshold))
                        return;
                    break;
                default:
                    break;
            }

            if (!consume.CanConsume(genetics.Genes, item, out float nutrition)) return;
            RefTuple<(Entity, float, float)> context = new ((
                target as Entity,
                nutrition,
                nutrition / genetics.Genes.Get(vitality.stats.MaxHealth)
            ));

            this.self.eventCtrl.RaiseEvent(GameEvent.Entity_Fed, self, item, context);

            item.AmountRaw = 0;
            nutrition = context.Value.Item2; 
            vitality.Heal(nutrition);
        }

        public void AddBehaviorDependencies(Dictionary<Behaviors, int> heirarchy) {
            heirarchy.TryAdd(Behaviors.Genetics, heirarchy.Count);
            heirarchy.TryAdd(Behaviors.Vitality, heirarchy.Count);
        }

        public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> heirarchy) {
            heirarchy.TryAdd(typeof(FeedableBehaviorSettings), new FeedableBehaviorSettings());
            heirarchy.TryAdd(typeof(ConsumeBehaviorSettings), new ConsumeBehaviorSettings());
        }

        public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalSettings to have FeedableBehaviorSettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out hunt)) hunt = null;

            
            this.self = self;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Interact, OnInteracted);
            self.Register(this);
        }
        
        public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
            if (!setting.Is(out settings))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalSettings to have FeedableBehaviorSettings");
            if (!setting.Is(out consume))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalSettings to have ConsumeBehaviorSettings");
            if (!self.Is(out genetics))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalInstance to have GeneticsBehavior");
            if (!self.Is(out vitality))
                throw new System.Exception("Entity: Feed Behavior Requires AnimalInstance to have VitalityBehavior");
            if (!self.Is(out hunt)) hunt = null;

            
            this.self = self;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Interact, OnInteracted);
            self.Register(this);
        }
        
        public void Disable(BehaviorEntity.Animal self) {
            this.self = null;
        }
    }
}