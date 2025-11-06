using System;
using System.Runtime.Serialization;
using WorldConfig.Gameplay;

namespace WorldConfig.Generation.Item {

    public class ItemContext {
        private WeakReference<IInventory> Inv;
        private WeakReference<object> Holder;
        public Scenario scenario;
        public int InvId;
        public ItemContext(
            IInventory inv,
            int InvId
        ) {
            this.Inv = new WeakReference<IInventory>(inv);
            this.InvId = InvId;
            scenario = default;
            Holder = null;
        }

        public ItemContext(
            IInventory inv,
            object holder,
            Scenario scenario,
            int InvId
        ) {
            this.Inv = new WeakReference<IInventory>(inv);
            this.Holder =  new WeakReference<object>(holder);
            this.scenario = scenario;
            this.InvId = InvId;
        }
        public delegate ItemContext FinishSetup(ItemContext continuation);
        public ItemContext SetupScenario(object holder, Scenario scenario) {
            this.Holder = new WeakReference<object>(holder);
            this.scenario = scenario;
            return this;
        }
        public bool TryRemove() {
            if (!Inv.TryGetTarget(out IInventory target))
                return false;
            target.RemoveEntry(InvId);
            return true;
        }

        public bool TryGetInventory<TInv>(out TInv inv) where TInv : class {
            inv = null;
            if (!Inv.TryGetTarget(out IInventory inventory))
                return false;
            if (inventory is not TInv typedInv)
                return false;
            inv = typedInv;
            return true;
        }

        public bool TryGetHolder<THolder>(out THolder owner) where THolder : class {
            owner = null;
            if (!Holder.TryGetTarget(out object holder))
                return false;
            if (holder is not THolder hold)
                return false;
            owner = hold;
            return true;
        }

        public enum Scenario {
            ActivePlayerSecondary,
            //Note: If ActivePlayerSelected enters,
            //this handler's leave wll not be called
            ActivePlayerPrimary,
            ActivePlayerSelected,
            ActivePlayerArmor,
            ActivePlayerCraftingGrid,
        }
    }

    public interface IInventory {
        public void RemoveEntry(int id);
        public void ReapplyHandles();
        public void UnapplyHandles();
    }
}