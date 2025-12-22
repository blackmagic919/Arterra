using System;
using Unity.Mathematics;

namespace Arterra.Config.Generation.Item {

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
            scenario = Scenario.Default;
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
            Default,
            ActivePlayerSecondary,
            //Note: If ActivePlayerSelected enters,
            //this handler's leave wll not be called
            ActivePlayerPrimary,
            ActivePlayerSelected,
            ActivePlayerArmor,
            ActivePlayerCraftingGrid,
            MaterialContainer,
        }
    }

    public interface IInventory {
        /// <summary> Retrieves the item at the given index without modifying the entry </summary>
        /// <param name="index">The index of the item being peeked</param>
        /// <returns>The item being peeked</returns>
        public IItem PeekItem(int index);
        /// <summary> Gets the capacity of the inventory </summary>
        /// <returns>The capacity of the inventory</returns>
        public int Capacity {get;}
        /// <summary> Removes the entry at a given index, does nothing
        /// if nothing is there. </summary>
        /// <param name="slot">The index of the item being removed</param>
        public void RemoveEntry(int slot);
        /// <summary> Adds an item to the inventory at a certain slot index. </summary>
        /// <param name="entry">The item to be added </param>
        /// <param name="index">The slot index for the item to be added at</param>
        /// <returns>Whether or not the item was succesffully added</returns>
        public bool AddEntry(IItem entry, int index);
        /// <summary>Adds an item to the next available/empty slot in the inventory.</summary>
        /// <param name="entry">The item to be added</param>
        /// <param name="head">The slot index it was added at</param>
        /// <returns>Whether or not the item was successfully added</returns>
        public bool AddEntry(IItem entry, out int head);
        /// <summary> Reapplies all OnEnter hooks to items. </summary>
        public void ReapplyHandles();
         /// <summary> Reapplies all OnLeave hooks to items. </summary>
        public void UnapplyHandles();

        /// <summary>Removes an amount of a specific type of item. </summary>
        /// <param name="KeyIndex"> The item index, or the unique <see cref="IRegistered.Index"/> for every item</param>
        /// <param name="delta">The raw amount to remove</param>
        /// <param name="OnRemove"> The callback that will be called for every item removed</param>
        /// <returns>The raw amount removed</returns>
        public int RemoveStackableKey(int KeyIndex, int delta, Action<IItem> OnRemove = null);
        /// <summary> Adds a stackable amount of an item to an inventory. By default same as AddEntry;
        /// override this to add an item to try combine into an already existing non-full slot with same item. </summary>
        /// <param name="mat">The item to be added </param>
        public void AddStackable(IItem mat) {AddEntry(mat, out _); }

        /// <summary> Adds an item of a 
        /// </summary>
        /// <param name="mat"></param>
        /// <param name="SlotIndex"></param>
        public void AddStackable(IItem mat, int SlotIndex) {
            if (PeekItem(SlotIndex) == null) {AddEntry(mat, SlotIndex); return; }
            if (PeekItem(SlotIndex).Index != mat.Index) return;
            IItem sMat = PeekItem(SlotIndex);
            if (sMat.AmountRaw < sMat.StackLimit) {
                int delta = math.min(sMat.AmountRaw + mat.AmountRaw, sMat.StackLimit) - sMat.AmountRaw;
                sMat.AmountRaw += delta;
                mat.AmountRaw -= delta;
            }

            if (mat.AmountRaw <= 0) return;
            AddEntry(mat, out int ind);
        }
        /// <summary> Removes an amount of the item at a given slot. </summary>
        /// <param name="SlotIndex">The index of the item to be removed</param>
        /// <param name="delta">The raw amount ot remove</param>
        /// <returns>The actual raw amount removed</returns>
        public int RemoveStackableSlot(int SlotIndex, int delta) {
            IItem mat = PeekItem(SlotIndex);
            delta = mat.AmountRaw - math.max(mat.AmountRaw - delta, 0);
            PeekItem(SlotIndex).AmountRaw -= delta;

            if (mat.AmountRaw == 0)
                RemoveEntry(SlotIndex);
            return delta;
        }

        /// <summary> Tries to find an item of the given item index in the inventory. </summary>
        /// <param name="itemIndex">The type index of the item to be found</param>
        /// <param name="slotIndex">The index of the slot holding the item</param>
        /// <returns></returns>
        public bool TryGetKey(int itemIndex, out int slotIndex) {
            for (slotIndex = 0; slotIndex < Capacity; slotIndex++) {
                if (PeekItem(slotIndex) == null) continue;
                if (PeekItem(slotIndex).Index == itemIndex) return true;
            } return false;
        }
    }
}