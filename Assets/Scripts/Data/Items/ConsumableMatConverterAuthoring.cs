using Arterra.Utils;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Configuration;
using Arterra.GamePlay;
using Arterra.GamePlay.Interaction;
using Arterra.Data.Entity.Behavior;
using Arterra.Core.Storage;

namespace Arterra.Data.Item {
    [CreateAssetMenu(menuName = "Generation/Items/ConsumableMatConverter")]
    public class ConsumableMatConverterAuthoring : MatConverterAuthoring {
        public float ConsumptionRate;
        public float NutritionValue;

        public override IItem Item => new ConsumableMatConverterItem();
    }

    [System.Serializable]
    public class ConsumableMatConverterItem : MatConverterItem {
        private static Catalogue<Authoring> ItemInfo => Config.CURRENT.Generation.Items;
        private ConsumableMatConverterAuthoring settings => ItemInfo.Retrieve(Index) as ConsumableMatConverterAuthoring;

        public override object Clone() => new ConsumableMatConverterItem { data = data };

        public override void OnEnter(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;

            InputPoller.AddBinding(new ActionBind(
                "Consume",
                _ => ConsumeFood(cxt),
                ActionBind.Exclusion.ExcludeLayer),
                "ITEM::Consumable:EAT", "5.0::GamePlay"
            );

            // Converter keybinds are bound after consume so converter propagation blocking can win on overlap.
            base.OnEnter(cxt);
        }

        public override void OnLeave(ItemContext cxt) {
            if (cxt.scenario != ItemContext.Scenario.ActivePlayerSelected) return;

            InputPoller.RemoveBinding("ITEM::Consumable:EAT", "5.0::GamePlay");
            base.OnLeave(cxt);
        }

        private void ConsumeFood(ItemContext cxt) {
            if (AmountRaw == 0) return;
            int delta = CustomUtility.GetStaggeredDelta(settings.ConsumptionRate);
            if (delta == 0) return;
            if (!cxt.TryGetHolder(out BehaviorEntity.Animal player)) return;
            if (!player.Is(out HungerBehavior hung)) return;
            if (hung.IsFull) return;

            delta = AmountRaw - math.max(AmountRaw - delta, 0);
            player.eventCtrl.RaiseEvent(Core.Events.GameEvent.Item_ConsumeFood, player, this, delta);
            float nutrition = (float)delta / UnitSize * settings.NutritionValue;
            hung.Feed(ref nutrition);
            delta = CustomUtility.GetStaggeredDelta(nutrition * UnitSize / settings.NutritionValue);
            AmountRaw -= delta;
            if (AmountRaw == 0) cxt.TryRemove();
        }
    }
}
