
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using Arterra.Core.Events;
using Newtonsoft.Json;
using TMPro;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.UI;
using Arterra.Config;
using Arterra.Config.Generation.Item;
using Arterra.Core.Player;

namespace Arterra.Config.Gameplay{
    /// <summary> Settings controlling the size and apperance of the inventory,
    /// a system allowing the player to hold and use 
    /// <see cref="Generation.Item"> items </see>. </summary>
    [Serializable]
    public struct Inventory{
        /// <summary> The amount of slots in the primary inventory, the hotbar. This is
        /// equivalent to the maximum amount of items that can be held in the hotbar.  </summary>
        public int PrimarySlotCount;
        /// <summary> The amount of slots in the secondary inventory, the hidden inventory. This is
        /// equivalent to the maximum amount of items that can be held in the hidden inventory. </summary>
        public int SecondarySlotCount;

        /// <summary>
        /// The color of the selected slot in the <see cref="InventoryController.Primary">Primary Inventory</see>, or
        /// the hotbar. This color is used to indicate which item currently has the status of being <see cref="InventoryController.Selected">
        /// selected. </see> 
        /// </summary>
        public Color SelectedColor;
        /// <summary> The color of the base slot in the Inventory. The color 
        /// of the slot when it is empty (the item held by the slot is null). </summary>
        public Color BaseColor;
    }
}
public static class InventoryController {
    public static Arterra.Config.Gameplay.Inventory settings => Config.CURRENT.GamePlay.Inventory.value;
    public static Inventory Primary; //Hotbar
    public static Inventory Secondary; //Inventory
    public static CursorManager Cursor;
    private static GameObject Menu;
    private static ItemDescription itemInfo;

    private static Catalogue<Authoring> ItemSettings;
    public static IItem Selected => Primary.Info[SelectedIndex];
    public static Authoring SelectedSetting => ItemSettings.Retrieve(Primary.Info[SelectedIndex].Index);
    public static int SelectedIndex = 0;

    private static ItemContext GetSecondaryCxt(ItemContext cxt) => cxt.SetupScenario(PlayerHandler.data, ItemContext.Scenario.ActivePlayerSecondary);
    private static ItemContext GetPrimaryCxt(ItemContext cxt) {
        if (cxt.InvId == SelectedIndex) {
            itemInfo.SwitchItem(Selected);
            return HeldCxt;
        } return cxt.SetupScenario(PlayerHandler.data, ItemContext.Scenario.ActivePlayerPrimary);
    }
    private static ItemContext HeldCxt => new (Primary, PlayerHandler.data, ItemContext.Scenario.ActivePlayerSelected, SelectedIndex);

    public static void Initialize() {
        Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Panel"), GameUIManager.UIHandle.transform);
        ItemSettings = Config.CURRENT.Generation.Items;

        Cursor = new CursorManager();
        itemInfo = new ItemDescription(Menu.transform.GetChild(0));
        RebindInventories(null, PlayerHandler.data);
        Secondary.Display.parent.SetActive(false);

        InputPoller.AddBinding(new ActionBind("Open Inventory", Activate), "PlayerInventory:OPN", "3.5::Window");
        AddHotbarKeybinds();
    }

    public static void Release() => Cursor?.ClearCursor(AddEntry);
    private static bool RebindInventories(PlayerStreamer.Player old, PlayerStreamer.Player cur) {
        var prms = (old, cur);
        return RebindInventories(ref prms);
    }
    private static bool RebindInventories(ref (PlayerStreamer.Player old, PlayerStreamer.Player cur) cxt) {
        cxt.old?.PrimaryI.UnapplyHandles();
        cxt.old?.SecondaryI.UnapplyHandles();
        cxt.old?.PrimaryI.ReleaseDisplay();
        cxt.old?.SecondaryI.ReleaseDisplay();

        if (Config.CURRENT.GamePlay.Gamemodes.value.KeepInventory) {
            var curPrimary = cxt.cur.PrimaryI; var curSecondary = cxt.cur.SecondaryI;
            cxt.cur.PrimaryI = cxt.old?.PrimaryI ?? cxt.cur.PrimaryI;
            cxt.cur.SecondaryI = cxt.old?.SecondaryI ?? cxt.cur.SecondaryI;
            if (cxt.old != null) {
                cxt.old.PrimaryI = curPrimary;
                cxt.old.SecondaryI = curSecondary;
            }
        }

        Primary = cxt.cur.PrimaryI;
        Secondary = cxt.cur.SecondaryI;
        Primary.InitializeDisplay(Menu.transform.GetChild(0).Find("Primary").gameObject);
        Secondary.InitializeDisplay(Menu.transform.GetChild(0).Find("Secondary").gameObject);

        //Primary.ReapplyHandles will automatically call OnEnter and OnLeave
        Secondary.AddCallbacks(GetSecondaryCxt, GetSecondaryCxt);
        Primary.AddCallbacks(GetPrimaryCxt, GetPrimaryCxt);
        Secondary.ReapplyHandles();
        Primary.ReapplyHandles();

        cxt.cur.eventCtrl.AddEventHandler(
            GameEvent.Entity_Respawn,
            delegate (object actor, object target, ref (PlayerStreamer.Player, PlayerStreamer.Player) args) {
                RebindInventories(ref args);
            }
        );
        return false;
    }


    private static void Activate(float _) {
        Activate();
        PanelNavbarManager.Activate();
    }
    private static void Deactivate(float _) {
        PanelNavbarManager.Deactivate();
        Deactivate();
    }
    public static void Activate() {
        InputPoller.AddStackPoll(new ActionBind("Frame:Inventory", _ => InputPoller.SetCursorLock(false)), "CursorLock");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.AddContextFence("PlayerInventory:AllFrame", "3.0::AllWindow", ActionBind.Exclusion.None);
            InputPoller.AddContextFence("PlayerInventory:WndFrame", "3.5::Window", ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new ActionBind("Open Inventory", Deactivate), "PlayerInventory:CLS", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Deselect", _ => DeselectDrag(GetMouseTarget, true)), "PlayerInventory:DSL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Select", _ => Select(GetMouseTarget, true)),  "PlayerInventory:SEL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectPartial", _ => SelectPartial(GetMouseTarget, true)),  "PlayerInventory:SELP", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectAll", _ => { SelectAll(Primary); SelectAll(Secondary); },
                ActionBind.Exclusion.ExcludeLayer),"PlayerInventory:SELA", "3.0::AllWindow");
        });
        Cursor.ClearCursor(AddEntry);
        Secondary.Display.parent.SetActive(true);
    }

    public static void Deactivate() {
        InputPoller.RemoveStackPoll("Frame:Inventory", "CursorLock");
        InputPoller.AddKeyBindChange(() => {
            InputPoller.RemoveContextFence("PlayerInventory:WndFrame", "3.5::Window");
            InputPoller.RemoveContextFence("PlayerInventory:AllFrame", "3.0::AllWindow");
        });
        Secondary.Display.parent.SetActive(false);
        Cursor.ClearCursor(AddEntry);
    }

    private static void AddHotbarKeybinds() {
        static void ChangeSelected(int index) {
            Selected?.OnLeave(HeldCxt);
            Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.BaseColor;

            SelectedIndex = index % settings.PrimarySlotCount;
            Selected?.OnEnter(HeldCxt);
            Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.SelectedColor;
            itemInfo.SwitchItem(Selected);
        }
        InputPoller.AddBinding(new ActionBind("Hotbar1", _ => ChangeSelected(0)), "PlayerInventory:HB1", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar2", _ => ChangeSelected(1)), "PlayerInventory:HB2", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar3", _ => ChangeSelected(2)), "PlayerInventory:HB3", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar4", _ => ChangeSelected(3)), "PlayerInventory:HB4", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar5", _ => ChangeSelected(4)), "PlayerInventory:HB5", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar6", _ => ChangeSelected(5)), "PlayerInventory:HB6", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar7", _ => ChangeSelected(6)), "PlayerInventory:HB7", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar8", _ => ChangeSelected(7)), "PlayerInventory:HB8", "2.0::Subscene");
        InputPoller.AddBinding(new ActionBind("Hotbar9", _ => ChangeSelected(8)), "PlayerInventory:HB9", "2.0::Subscene");
        Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.SelectedColor; //Set the inital selected color
    }

    private static bool GetMouseTarget(out IInventory Inv, out int index) {
        Inv = null;
        if (Primary.Display.GetMouseSelected(out index)) {
            Inv = Primary;
        } else if (Secondary.Display.GetMouseSelected(out index)) {
            Inv = Secondary;
        } else return false;
        return true;
    }
    public static void DropItem(IItem item) {
        DropItem(item, PlayerHandler.data.position,
        PlayerHandler.data.collider.transform.rotation);
    }

    public static void DropItem(IItem item, float3 location, Quaternion rotation = default) {
        if (item == null) return;
        Arterra.Config.Generation.Entity.Entity Entity = new EItem.EItemEntity(item, rotation);
        uint eIndex = (uint)Config.CURRENT.Generation.Entities.RetrieveIndex("EntityItem");
        Entity.info.entityId = Guid.NewGuid();
        EntityManager.CreateEntity(math.round(location), eIndex, Entity);
    }

    public delegate bool MouseInvSelect(out IInventory Inv, out int index);
    public static bool Select(MouseInvSelect GetSelectedInvInfo) => Select(GetSelectedInvInfo, false);
    public static bool SelectPartial(MouseInvSelect GetSelectedInvInfo) => SelectPartial(GetSelectedInvInfo, false);
    public static bool DeselectDrag(MouseInvSelect GetSelectedInvInfo) => DeselectDrag(GetSelectedInvInfo, false);
    private static bool Select(MouseInvSelect GetSelectedInvInfo, bool isRoot) {
        if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
            if (isRoot) Cursor.ClearCursor(DropItem);
            return false;
        }

        if (Cursor.IsHolding) {
            if (Cursor.State == CursorManager.PlaceState.None) {
                Cursor.State = CursorManager.PlaceState.Split;
                DeselectDrag(GetSelectedInvInfo, isRoot);
                return true;
            } else if (Cursor.State == CursorManager.PlaceState.Unit) {
                Inv.AddStackable(Cursor.Item, index);
                Cursor.ClearCursor(AddEntry);
                return true;
            }
        }

        Cursor.ClearCursor(AddEntry);
        //Swap the cursor with the selected slot
        IItem clickedItem = Inv.PeekItem(index);
        if (clickedItem == null) return true;
        clickedItem = clickedItem.Clone() as IItem;
        Inv.RemoveEntry(index);
        Cursor.HoldItem(clickedItem);
        return true;
    }

    private static bool SelectPartial(MouseInvSelect GetSelectedInvInfo, bool isRoot) {
        if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
            if (isRoot) Cursor.ClearCursor(DropItem);
            return false;
        } else if (!Cursor.IsHolding || Cursor.State == CursorManager.PlaceState.Split) {
            if (isRoot) Cursor.ClearCursor(DropItem);
        } else {
            Cursor.State = CursorManager.PlaceState.Unit;
            //forget drag duplicates
            Cursor.SplitItems = null; 
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
        Cursor.HoldItem(clickedItem);
        return true;
    }


    private static bool DeselectDrag(MouseInvSelect GetSelectedInvInfo, bool isRoot) {
        if (!Cursor.IsHolding) return false;
        if (!Cursor.IsPlacing) return false;
        if (!GetSelectedInvInfo(out IInventory Inv, out int index)) {
            if(isRoot) Cursor.ClearCursor(AddEntry);
            return false;
        }

        IItem remainder = null;
        if (Cursor.State == CursorManager.PlaceState.Split)
            remainder = SplitCursorItem(Inv, index);
        else if (Cursor.State == CursorManager.PlaceState.Unit)
            remainder = PlaceUnitCursorItem(Inv, index);

        if (remainder == null || remainder.AmountRaw <= 0) return true;
        AddEntry(remainder);
        return true;
    }

    private static IItem SplitCursorItem(IInventory Inv, int index) {
        IItem remainder = null;
        if (Cursor.CanSplitToItem(Inv.PeekItem(index))) {
            if (Cursor.SplitNewItem(out remainder)) {
                Inv.AddStackable(remainder, index);
                Cursor.AddNewSplitItem(Inv.PeekItem(index));
            }
        } else if (!Cursor.IsSplitUp) {
            remainder = Cursor.Item;
            Cursor.ClearCursor();
        }
        return remainder;
    }
    
    private static IItem PlaceUnitCursorItem(IInventory Inv, int index) {
        IItem mOverSlot = Inv.PeekItem(index);
        if (Cursor.Item.AmountRaw <= 0) return null;
        IItem remainder = null;
        if (Cursor.CanSplitToItem(Inv.PeekItem(index))) {
            remainder = Cursor.Item.Clone() as IItem;
            int amount = math.min(remainder.UnitSize, remainder.AmountRaw);
            remainder.AmountRaw = amount;
            Inv.AddStackable(remainder, index);
            Cursor.AddNewSplitItem(Inv.PeekItem(index), false);
            Cursor.Item.AmountRaw -= amount - remainder.AmountRaw;
            if (Cursor.Item.AmountRaw == 0) Cursor.ClearCursor();
        }
        return remainder;
    }

    
    public static bool SelectAll(Inventory inv) {
        if (!Cursor.IsHolding || Cursor.IsPlacing)
            return false;
        IItem cItem = Cursor.Item;
        cItem.AmountRaw += inv.RemoveStackableKey(cItem.Index, cItem.StackLimit - cItem.AmountRaw);
        return true;
    }

    //The Slot is changed to reflect what remains
    //if stackable, add to primary then secondary, else add to secondary then primary
    public static void AddEntry(IItem e) {
        if (e == null) return;
        if (Primary.EntryDict.ContainsKey(e.Index))
            Primary.AddStackable(e);
        if (e.AmountRaw == 0) return;
        Secondary.AddStackable(e);
        if (e.AmountRaw == 0) return;
        Primary.AddStackable(e);
        if (e.AmountRaw == 0) return;
        DropItem(e);
    }
    public static int RemoveStackable(int delta, int itemIndex = -1, int SelIndex = -1, Action<IItem> OnRemoved = null) {
        bool TryFromIndex(int SelIndex, out int amount) {
            amount = 0;
            if (SelIndex == -1) return false;
            if (Primary.Info[SelIndex] == null) return false;
            if (itemIndex != -1 && Primary.Info[SelIndex].Index != itemIndex)
                return false;
            if (OnRemoved != null) {
                IItem removed = Primary.Info[SelIndex].Clone() as IItem;
                amount = Primary.RemoveStackableSlot(SelIndex, delta);
                removed.AmountRaw = amount;
                OnRemoved.Invoke(removed);
            } else amount = Primary.RemoveStackableSlot(SelIndex, delta);
            return true;
        }

        if (TryFromIndex(SelIndex, out int rem)) return rem;
        if (TryFromIndex(SelectedIndex, out rem)) return rem;
        if (Primary.EntryDict.ContainsKey(itemIndex)) {
            return Primary.RemoveStackableKey(itemIndex, delta, OnRemoved);
        } else if (Secondary.EntryDict.ContainsKey(itemIndex)) {
            return Secondary.RemoveStackableKey(itemIndex, delta, OnRemoved);
        }
        return 0;
    }



    public class Inventory : IInventory {
        public IItem[] Info;
        public LLNode[] EntryLL;
        public List<string> SerializedNames;
        [JsonIgnore]
        public Dictionary<int, int> EntryDict;
        [JsonIgnore]
        public GridUIManager Display;
        public uint capacity;
        public uint length;
        public int tail;
        private ItemContext.FinishSetup OnAddElement;
        private ItemContext.FinishSetup OnRemoveElement;

        public Inventory(int SlotCount) {
            Info = new IItem[SlotCount];
            EntryDict = new Dictionary<int, int>();
            EntryLL = new LLNode[SlotCount];
            capacity = (uint)SlotCount;
            tail = 0;
            length = 0;
            for (int i = 0; i < SlotCount; i++) {
                int next = (i + 1) % SlotCount;
                int prev = (((i - 1) % SlotCount) + SlotCount) % SlotCount;
                EntryLL[i] = new LLNode((uint)prev, (uint)next);
            }
        }

        [JsonIgnore]
        public virtual int Capacity => (int)capacity;
        public virtual IItem PeekItem(int index) => Info[index];
        public bool TryGetKey(int key, out int value) => EntryDict.TryGetValue(key, out value);

        public void ResizeInventory(int SlotCount, Action<IItem> OnRelease = null) {
            SlotCount = math.max(SlotCount, 0);
            ResizeDisplay(SlotCount);

            IItem[] nInfo = new IItem[SlotCount];
            LLNode[] nEntryLL = new LLNode[SlotCount];
            for (int i = SlotCount; i < capacity; i++) {
                if (Info[i] != null) OnRelease?.Invoke(Info[i].Clone() as IItem);
                ItemContext cxt = new(this, i);
                cxt = OnRemoveElement?.Invoke(cxt) ?? cxt;
                Info[i]?.OnLeave(cxt); RemoveEntry((int)i);
            }
            for (int i = 0; i < math.min(SlotCount, capacity); i++) {
                nInfo[i] = Info[i];
                nEntryLL[i] = EntryLL[i];
            }

            //Reconstruct free LL
            tail = -1;
            Info = nInfo;
            EntryLL = nEntryLL;
            for (uint i = 0; i < SlotCount; i++) {
                if (nInfo[i] != null) continue;
                if (tail == -1) {
                    EntryLL[i] = new LLNode(i, i);
                    tail = (int)i;
                } else LLAdd(i, (uint)tail);
            }
            capacity = (uint)SlotCount;
        }

        public void InitializeDisplay(GameObject Parent) {
            GameObject Root = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Inventory"), Parent.transform);
            GameObject GridContent = Root.transform.GetChild(0).GetChild(0).gameObject;
            Display = new GridUIManager(GridContent,
                Indicators.ItemSlots.Get, 
                Indicators.ItemSlots.Release,
                (int)capacity,
                Root);
            for (int i = 0; i < capacity; i++) {
                AttachDisplay(i);
            }
        }

        public void InitializeDisplay(GridUIManager gridManager) {
            Display = gridManager;
            for (int i = 0; i < capacity; i++) {
                AttachDisplay(i);
            }
        }

        public void ReleaseDisplay() {
            if (Display == null) return;
            for (int i = 0; i < Display.Slots.Length; i++)
                ClearDisplay(i);
            Display.Release();
            Display = null;
        }

        private void ResizeDisplay(int SlotCount) {
            if (Display == null) return;
            int prevSlotCount = Display.Slots.Length;
            for (int i = SlotCount; i < prevSlotCount; i++)
                ClearDisplay(i);
            Display.Resize(SlotCount);
            for (int i = prevSlotCount; i < SlotCount; i++)
                AttachDisplay(i);
        }

        private void AttachDisplay(int index) {
            if (Info == null || index >= Info.Length) return;
            if (Display == null || index >= Display.Slots.Length) return;
            Info[index]?.AttachDisplay(Display.Slots[index].transform);
        }

        private void ClearDisplay(int index) {
            if (Info == null || index >= Info.Length) return;
            if (Display == null || index >= Display.Slots.Length) return;
            Info[index]?.ClearDisplay(Display.Slots[index].transform);
        }

        public void ReapplyHandles() {
            for (int i = 0; i < Info.Length; i++) {
                if (Info[i] == null) continue;
                ItemContext cxt = new(this, i);
                cxt = OnAddElement?.Invoke(cxt) ?? cxt;
                Info[i].OnEnter(cxt);
            }
        }

        public void UnapplyHandles() {
            for (int i = 0; i < Info.Length; i++) {
                if (Info[i] == null) continue;
                ItemContext cxt = new(this, i);
                cxt = OnRemoveElement?.Invoke(cxt) ?? cxt;
                Info[i].OnLeave(cxt);
            }
        }

        public void Clear() {
            //Go backwards in case RemoveEntry shrinks Info
            for (int i = Info.Length - 1; i >= 0; i--) {
                if (Info[i] == null) continue;
                RemoveEntry(i);
            }
        }

        public bool RemoveEntry(IItem item) {
            if (item == null) return false;
            if (!EntryDict.ContainsKey(item.Index)) return false;
            int SlotIndex = EntryDict[item.Index];
            RemoveEntry(SlotIndex);
            return true;
        }

        public void CopyTo(Inventory newInv, int sourceStart = 0, int destStart = 0, int length = int.MaxValue) {
            sourceStart = (int)math.min(capacity, sourceStart);
            destStart = (int)math.min(newInv.capacity, destStart);
            length = (int)math.min(math.min(length, capacity - sourceStart), newInv.capacity - destStart);
            for (int i = 0; i < length; i++) {
                if (newInv.Info[destStart + i] != null) newInv.RemoveEntry(i);
                if (Info[sourceStart + i] == null) continue;
                newInv.AddEntry(Info[sourceStart + i].Clone() as IItem, destStart + i);
            }
        }

        public void AddCallbacks(
            ItemContext.FinishSetup OnAddItem = null,
            ItemContext.FinishSetup OnRemoveItem = null
        ) {
            OnAddElement = OnAddItem;
            OnRemoveElement = OnRemoveItem;
        }

        public IItem LootInventory(float collectRate) {
            if (EntryDict.Count <= 0) return null;
            int firstSlot = EntryDict.First().Value;
            IItem item = Info[firstSlot];
            if (item == null) return null;

            IItem ret = (IItem)item.Clone();
            collectRate *= item.UnitSize;
            int delta = Mathf.FloorToInt(collectRate)
                + (UnityEngine.Random.Range(0, 1) < math.frac(collectRate) ? 1 : 0);
            ret.AmountRaw = RemoveStackableSlot(firstSlot, delta);
            return ret;
        }

        //Input: The material id, amount to add, and index to add at
        //Input is modified with the remainder of the amount
        public void AddStackable(IItem mat, int SlotIndex) {
            if (Info[SlotIndex] == null) {AddEntry(mat, SlotIndex); return; }
            if (Info[SlotIndex].Index != mat.Index) return;
            IItem sMat = Info[SlotIndex];
            if (sMat.AmountRaw < sMat.StackLimit) {
                int delta = math.min(sMat.AmountRaw + mat.AmountRaw, sMat.StackLimit) - sMat.AmountRaw;
                sMat.AmountRaw += delta;
                mat.AmountRaw -= delta;
                DictUpdate(SlotIndex);
            }

            if (mat.AmountRaw <= 0) return;
            AddEntry(mat, out int ind);
        }


        //Input: The material id and amount to add
        //Input is modified with the remaineder of the amount
        public void AddStackable(IItem mat) {
            while (mat.AmountRaw > 0) {
                int SlotIndex = DictPeek(mat.Index);
                if (SlotIndex == -1) break;
                IItem sMat = Info[SlotIndex];
                if (sMat.AmountRaw >= sMat.StackLimit) break;

                int delta = math.min(sMat.AmountRaw + mat.AmountRaw, sMat.StackLimit) - sMat.AmountRaw;
                sMat.AmountRaw += delta;
                mat.AmountRaw -= delta;
                DictUpdate(SlotIndex);
            }
            if (mat.AmountRaw <= 0) return;
            AddEntry(mat, out int ind);
        }

        //Input: Request a certain amount of material from a slot
        //Returns: The actual amount removed
        public int RemoveStackableSlot(int SlotIndex, int delta) {
            IItem mat = Info[SlotIndex];
            delta = mat.AmountRaw - math.max(mat.AmountRaw - delta, 0);
            Info[SlotIndex].AmountRaw -= delta;
            
            if (mat.AmountRaw != 0) DictUpdate(SlotIndex);
            else RemoveEntry(SlotIndex);

            return delta;
        }

        //Input: Request a certain type of stackable of a certain amount
        //Returns: The actual amount removed
        public int RemoveStackableKey(int KeyIndex, int delta, Action<IItem> OnRemove = null) {
            if (delta == 0) return 0;
            int start = delta; int remainder = start;
            while (remainder > 0) {
                int SlotIndex = DictPeek(KeyIndex);
                if (SlotIndex == -1) break;

                IItem mat = Info[SlotIndex];
                delta = mat.AmountRaw - math.max(mat.AmountRaw - remainder, 0);
                mat.AmountRaw -= delta; remainder -= delta;
                if (OnRemove != null) {
                    IItem removed = mat.Clone() as IItem;
                    removed.AmountRaw = delta;
                    OnRemove(removed);
                } if (mat.AmountRaw != 0) DictUpdate(SlotIndex);
                else RemoveEntry(SlotIndex);
            }
            return start - remainder;
        }

        public virtual void RemoveEntry(int SlotIndex) {
            if (Info[SlotIndex] == null) return;
            int Index = Info[SlotIndex].Index;
            if (EntryDict.ContainsKey(Index) && SlotIndex == EntryDict[Index]) {
                EntryDict[Index] = (int)EntryLL[SlotIndex].n;
                if (SlotIndex == EntryDict[Index]) EntryDict.Remove(Index);
            }

            if (capacity == length) tail = SlotIndex;
            LLRemove((uint)SlotIndex);
            LLAdd((uint)SlotIndex, (uint)tail);

            ItemContext cxt = new(this, SlotIndex);
            cxt = OnRemoveElement?.Invoke(cxt) ?? cxt;
            Info[SlotIndex].OnLeave(cxt);

            ClearDisplay(SlotIndex);
            Info[SlotIndex].AmountRaw = 0;
            Info[SlotIndex] = null;
            tail = math.min(tail, SlotIndex);
            length--;
        }

        public virtual bool AddEntry(IItem entry, out int head) {
            head = tail;
            if (length >= capacity)
                return false;
            tail = (int)EntryLL[head].n;
            length++;

            LLRemove((uint)head);
            Info[head] = entry.Clone() as IItem;
            AttachDisplay(head);
            entry.AmountRaw = 0;
            DictEnqueue(head);
            ItemContext cxt = new(this, head);
            cxt = OnAddElement?.Invoke(cxt) ?? cxt;
            Info[head].OnEnter(cxt);

            return true;
        }

        public virtual bool AddEntry(IItem entry, int index) {
            if (length >= capacity) return false;
            if (Info[index] != null) return false; //Slot has to be null
            if (index == tail) tail = (int)EntryLL[tail].n;
            length++;

            LLRemove((uint)index);
            Info[index] = entry.Clone() as IItem;
            AttachDisplay(index);
            entry.AmountRaw = 0;
            DictEnqueue(index);
            ItemContext cxt = new(this, index);
            cxt = OnAddElement?.Invoke(cxt) ?? cxt;
            Info[index].OnEnter(cxt);

            return true;
        }
        private void LLAdd(uint index, uint prev) {
            if (prev == index) EntryLL[prev].n = index;
            EntryLL[index] = new LLNode(prev, EntryLL[prev].n);
            EntryLL[prev].n = index;
            EntryLL[EntryLL[index].n].p = index;
        }
        private void LLRemove(uint index) {
            EntryLL[EntryLL[index].p].n = EntryLL[index].n;
            EntryLL[EntryLL[index].n].p = EntryLL[index].p;
        }

        //Caller already has added the entry to the inventory
        //Adds the entry to sorted LinkedList
        private void DictEnqueue(int index) {
            IItem item = Info[index];
            if (item == null) return;
            int KeyIndex = item.Index;
            if (!EntryDict.ContainsKey(KeyIndex)) {
                EntryDict.Add(KeyIndex, index);
                LLAdd((uint)index, (uint)index);
                return;
            }

            int next = EntryDict[KeyIndex];
            int end = (int)EntryLL[next].p;
            while (Info[next].AmountRaw < item.AmountRaw) {
                if (next == EntryLL[end].n)
                    break;
                next = (int)EntryLL[next].n;
            }
            uint prev = EntryLL[next].p; //get the previous node
            LLAdd((uint)index, prev);
            EntryDict[KeyIndex] = (int)EntryLL[end].n;
        }

        //There is no DictDequeue because caller should just call RemoveEntry
        private int DictPeek(int KeyIndex) {
            if (!EntryDict.ContainsKey(KeyIndex)) return -1;
            int index = EntryDict[KeyIndex];
            return index;
        }

        //Caller already has removed the entry from the inventory
        private void DictUpdate(int index) {
            IItem item = Info[index];
            if (item == null) return;
            int KeyIndex = item.Index;

            int next = EntryDict[KeyIndex];
            int end = (int)EntryLL[next].p;
            while (Info[next].AmountRaw < item.AmountRaw) {
                if (next == EntryLL[end].n)
                    break;
                next = (int)EntryLL[next].n;
            }
            uint prev = EntryLL[next].p; //get the previous node
            if (prev == index) prev = EntryLL[prev].p;
            
            LLRemove((uint)index);
            LLAdd((uint)index, prev);
            EntryDict[KeyIndex] = (int)EntryLL[end].n;
        }

        [OnSerializing]
        public void OnSerializing(StreamingContext cxt) {
            //Marks updated slots dirty so they are rendered properlly when deserialized
            // (Register Name, Index) -> Name Index
            Dictionary<string, int> lookup = new Dictionary<string, int>();

            void Serialize(ref IItem item) {
                if (item is null) return;
                IRegister registry = item.GetRegistry();
                string name = registry.RetrieveName(item.Index);
                lookup.TryAdd(name, lookup.Count);
                item.Index = lookup[name];
            }

            for (int i = 0; i < Info.Length; i++) {
                Serialize(ref Info[i]);
            }

            SerializedNames = lookup.Keys.ToList();
        }

        [OnDeserialized]
        public void OnDeserialized(StreamingContext cxt) {
            List<string> names = SerializedNames;
            void Deserialize(ref IItem item) {
                if (item is null) return;
                if (item.Index >= names.Count || item.Index < 0) return;
                IRegister registry = item.GetRegistry();
                item.Index = registry.RetrieveIndex(names[item.Index]);
            }
            EntryDict = new Dictionary<int, int>();
            for (int i = 0; i < Info.Count(); i++) {
                if (Info[i] is null) continue;
                Deserialize(ref Info[i]);
                EntryDict.TryAdd(Info[i].Index, i);
            }
        }

        public struct LLNode {
            public uint p;
            public uint n;
            public LLNode(uint prev, uint next) {
                p = prev;
                n = next;
            }
        }
    }

    public struct InventorySlotDisplay {
        public Image Icon;
        public GameObject Object;
        public readonly Transform transform => Object.transform;
        public InventorySlotDisplay(GameObject obj) {
            Object = obj;
            Icon = obj.transform.GetComponent<Image>();
        }
    }

    public class CursorManager {
        public IItem Item;
        public InventorySlotDisplay Display;
        public List<IItem> SplitItems;
        public bool IsSplitUp => SplitItems != null && SplitItems.Count > 0;
        public bool IsHolding => Item != null;
        public bool IsPlacing => State != PlaceState.None;
        public PlaceState State;
        private IUpdateSubscriber EventTask;

        public CursorManager() {
            Item = null;
            SplitItems = null;
            State = PlaceState.None;
            Display = new InventorySlotDisplay(Indicators.ItemSlots.Get());
            Display.Object.transform.SetParent(Menu.transform);
            Display.Object.SetActive(false);
            EventTask = new IndirectUpdate(Update);
            Arterra.Core.Terrain.OctreeTerrain.MainLoopUpdateTasks.Enqueue(EventTask);
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
            if(hideCursor) HideCursor();
        }

        public void HoldItem(IItem item) {
            Item = item;
            State = PlaceState.None;
            ShowCursor();
        }
        public void ClearCursor(Action<IItem> OnDispose = null) {
            if (!(State == PlaceState.Split && IsSplitUp))
                OnDispose?.Invoke(Item);
            HideCursor();
            Item = null;
            SplitItems = null;
            State = PlaceState.None;
        }

        private void HideCursor() {
            if (Display.Object == null) return;
            if (!Display.Object.activeSelf) return;
            Item.ClearDisplay(Display.Object.transform);
            Display.Object.SetActive(false);
        }

        private void ShowCursor() {
            if (Display.Object == null) return;
            if (Display.Object.activeSelf) return;
            Item.AttachDisplay(Display.Object.transform);
            Display.Object.SetActive(true);
        }

        public void Update(MonoBehaviour _) {
            if (Cursor == null) return;
            Display.transform.position = Input.mousePosition;
        }

        public enum PlaceState {
            None,
            Split,
            Unit,
        }
    }

    private class ItemDescription {
        public RectTransform Display;
        private TextMeshProUGUI Description;
        private TextMeshProUGUI Name;
        private Animator animator;

        public ItemDescription(Transform parent) {
            this.Display = parent.Find("ItemInfo").GetComponent<RectTransform>();
            this.Name = Display.Find("Title").GetComponent<TextMeshProUGUI>(); 
            this.Description = Display.Find("Description").GetComponent<TextMeshProUGUI>();  
            this.animator = Display.GetComponent<Animator>();
        }

        public void SwitchItem(IItem item) {
            if (item == null) return;
            Authoring settings = Config.CURRENT.Generation.Items.Retrieve(item.Index);
            Name.text = settings.Name;
            Description.text = settings.Description;
            animator.SetTrigger("Fade");
        }
    }
}
