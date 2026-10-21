using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Data.Item;
using Arterra.GamePlay.UI;
using Unity.Mathematics;

namespace Arterra.Data.Player {
    public sealed class InventoryCursorState {
        public IItem Item;
        public List<IItem> SplitItems;
        public bool Visible;
        public bool IsSplitUp => SplitItems != null && SplitItems.Count > 0;
        public bool IsHolding => Item != null;
        public bool IsPlacing => State != PlaceState.None;
        public PlaceState State;
        public enum PlaceState { None, Split, Unit }

        private readonly Action<IItem> AddEntry;
        private readonly Action<IItem> DropItem;
        private readonly Action<IItem> Changed;

        public InventoryCursorState(Action<IItem> addEntry, Action<IItem> dropItem, Action<IItem> changed) {
            AddEntry = addEntry;
            DropItem = dropItem;
            Changed = changed;
        }

        public bool Select(Arterra.Data.Entity.Behavior.PlayerInventoriesBehavior.MouseInvSelect GetSelectedInvInfo, bool isRoot) {
            if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
                if (isRoot) ClearCursor(DropItem);
                return false;
            }

            if (IsHolding) {
                if (State == PlaceState.None) {
                    State = PlaceState.Split;
                    DeselectDrag(GetSelectedInvInfo, isRoot);
                    return true;
                } else if (State == PlaceState.Unit) {
                    Inv.AddStackable(Item, index);
                    ClearCursor(AddEntry);
                    return true;
                }
            }

            ClearCursor(AddEntry);
            //Swap the cursor with the selected slot
            IItem clickedItem = Inv.PeekItem(index);
            if (clickedItem == null) return true;
            clickedItem = clickedItem.Clone() as IItem;
            Inv.RemoveEntry(index);
            HoldItem(clickedItem);
            return true;
        }

        public bool SelectPartial(Arterra.Data.Entity.Behavior.PlayerInventoriesBehavior.MouseInvSelect GetSelectedInvInfo, bool isRoot) {
            if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
                if (isRoot) ClearCursor(DropItem);
                return false;
            } else if (!IsHolding || State == PlaceState.Split) {
                if (isRoot) ClearCursor(DropItem);
            } else {
                State = PlaceState.Unit;
                //forget drag duplicates
                SplitItems = null;
                DeselectDrag(GetSelectedInvInfo, isRoot);
                return true;
            }

            //Swap the cursor with the selected slot
            IItem clickedItem = Inv.PeekItem(index);
            if (clickedItem == null) return true;
            clickedItem = clickedItem.Clone() as IItem;
            //Removes half of the item
            int amount = clickedItem.AmountRaw / 2 + (clickedItem.AmountRaw % 2);
            amount = Inv.RemoveStackableSlot(index, amount);
            clickedItem.AmountRaw = amount;
            HoldItem(clickedItem);
            return true;
        }


        public bool DeselectDrag(Arterra.Data.Entity.Behavior.PlayerInventoriesBehavior.MouseInvSelect GetSelectedInvInfo, bool isRoot) {
            if (!IsHolding) return false;
            if (!IsPlacing) return false;
            if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
                if (isRoot) ClearCursor(AddEntry);
                return false;
            }

            IItem remainder = null;
            if (State == PlaceState.Split)
                remainder = SplitCursorItem(Inv, index);
            else if (State == PlaceState.Unit)
                remainder = PlaceUnitCursorItem(Inv, index);

            if (remainder == null || remainder.AmountRaw <= 0) return true;
            AddEntry(remainder);
            return true;
        }

        private IItem SplitCursorItem(IInventory Inv, int index) {
            IItem remainder = null;
            if (CanSplitToItem(Inv.PeekItem(index))) {
                if (SplitNewItem(out remainder)) {
                    Inv.AddStackable(remainder, index);
                    AddNewSplitItem(Inv.PeekItem(index));
                }
            } else if (!IsSplitUp) {
                remainder = Item;
                ClearCursor();
            }
            return remainder;
        }

        private IItem PlaceUnitCursorItem(IInventory Inv, int index) {
            IItem mOverSlot = Inv.PeekItem(index);
            if (Item.AmountRaw <= 0) return null;
            IItem remainder = null;
            if (CanSplitToItem(Inv.PeekItem(index))) {
                remainder = Item.Clone() as IItem;
                int amount = math.min(remainder.UnitSize, remainder.AmountRaw);
                remainder.AmountRaw = amount;
                Inv.AddStackable(remainder, index);
                AddNewSplitItem(Inv.PeekItem(index), false);
                Item.AmountRaw -= amount - remainder.AmountRaw;
                if (Item.AmountRaw == 0) ClearCursor();
            }
            return remainder;
        }


        public bool SelectAll(Inventory inv) {
            if (!IsHolding || IsPlacing)
                return false;
            IItem cItem = Item;
            cItem.AmountRaw += inv.RemoveStackableKey(cItem.Index, cItem.StackLimit - cItem.AmountRaw);
            return true;
        }

        public bool SplitNewItem(out IItem nItem) {
            SplitItems ??= new List<IItem>();
            int totalCount = SplitItems.Count + 1;
            //We use min so we don't rebalance if a split item is decremented or lost
            SplitItems.RemoveAll(s => s == null || s.Index != Item.Index);
            int amtRaw = SplitItems.Count == 0 ? Item.AmountRaw
                : SplitItems.Count * SplitItems.Min(s => s.AmountRaw);

            for (int i = 0; i < SplitItems.Count; i++) {
                int NewAmount = amtRaw / totalCount + (i < (amtRaw % totalCount) ? 1 : 0);
                int OldAmount = amtRaw / SplitItems.Count + (i < (amtRaw % SplitItems.Count) ? 1 : 0);

                IItem splitItem = SplitItems[i];
                int delta = OldAmount - NewAmount;
                delta = splitItem.AmountRaw - math.max(splitItem.AmountRaw - delta, 0);
                splitItem.AmountRaw -= delta;
                Changed(splitItem);
            }

            nItem = null;
            if (amtRaw / totalCount == 0) return false;
            nItem = Item.Clone() as IItem;
            nItem.AmountRaw = amtRaw / totalCount;
            return true;
        }

        public bool CanSplitToItem(IItem item) {
            if (!IsHolding) return false;
            if (item == null) return true;
            if (item.Index != Item.Index) return false;
            if (!IsSplitUp) return true;
            return !SplitItems.Contains(item);
        }

        public void AddNewSplitItem(IItem nItem, bool hideCursor = true) {
            SplitItems ??= new List<IItem>();
            SplitItems.Add(nItem);
            if (hideCursor) Visible = false;
        }

        public void HoldItem(IItem item) {
            Item = item;
            State = PlaceState.None;
            Visible = true;
        }
        public void ClearCursor(Action<IItem> OnDispose = null) {
            if (!(State == PlaceState.Split && IsSplitUp))
                OnDispose?.Invoke(Item);
            Visible = false;
            Item = null;
            SplitItems = null;
            State = PlaceState.None;
        }

    }
}
