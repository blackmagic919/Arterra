
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
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Core;
using Arterra.Data.Material;
using Arterra.GamePlay.Interaction;
using Arterra.Utils;
using Arterra.Data.Entity.Behavior;
using Arterra.Data.Entity;


using Arterra.GamePlay.UI;
namespace Arterra.Data.Item {
        public class Inventory : IInventory {
            [JsonIgnore]
            public IItem[] Info;

            [JsonProperty("Info", ObjectCreationHandling = ObjectCreationHandling.Replace)]
            private IItem[] SerializedInfo {
                get {
                    IItem[] items = new IItem[Info.Length];
                    for (int i = 0; i < Info.Length; i++) {
                        if (Info[i] == null) continue;
                        IItem item = (IItem)Info[i].Clone();
                        item.Index = SerializedNames.IndexOf(
                            item.GetRegistry().RetrieveName(item.Index));
                        items[i] = item;
                    }
                    return items;
                }
                set => Info = value;
            }

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

            [JsonConstructor]
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


            public Inventory(MaterialData.MetaConstructor constructor, int3 GCoord, int SlotCount) : this(SlotCount) {
                int seed = GCoord.x ^ GCoord.y ^ GCoord.z ^ this.GetHashCode();
                Unity.Mathematics.Random rng = CustomUtility.SeedRng(seed);

                var lootTable = constructor.LootTable.value;
                for (int i = 0; i < capacity; i++) {
                    foreach (var loot in lootTable) {
                        if (rng.NextFloat() > loot.DropChance)
                            continue;

                        IItem item = Config.CURRENT.Generation.Items.Retrieve(loot.DropItem).Item;
                        int amount = (int)math.round(rng.NextFloat() * item.UnitSize * loot.DropMultiplier * 2);
                        amount = math.min(amount, item.StackLimit);
                        if (amount <= 0) continue;

                        item.Create(item.Index, amount);
                        AddEntry(item, i);
                        break;
                    }
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
                Info[index]?.AttachDisplay(Display.Slots[index].transform, Info[index].Index);
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

            public void RefreshSlot(int slot, Action update) {
                ClearDisplay(slot);
                update();
                DictUpdate(slot);
                AttachDisplay(slot);
            }

            private void NotifyChange(int slot) {
                ItemContext context = new(this, slot);
                (OnAddElement?.Invoke(context) ?? context).SetDirty();
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
                if (Info[SlotIndex] == null) { AddEntry(mat, SlotIndex); return; }
                if (Info[SlotIndex].Index != mat.Index) return;
                IItem sMat = Info[SlotIndex];
                if (sMat.AmountRaw < sMat.StackLimit) {
                    int delta = math.min(sMat.AmountRaw + mat.AmountRaw, sMat.StackLimit) - sMat.AmountRaw;
                    sMat.AmountRaw += delta;
                    mat.AmountRaw -= delta;
                    DictUpdate(SlotIndex);
                    if (delta != 0) NotifyChange(SlotIndex);
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
                    if (delta != 0) NotifyChange(SlotIndex);
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

                if (mat.AmountRaw != 0) {
                    DictUpdate(SlotIndex);
                    if (delta != 0) NotifyChange(SlotIndex);
                }
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
                    }
                    if (mat.AmountRaw != 0) {
                        DictUpdate(SlotIndex);
                        if (delta != 0) NotifyChange(SlotIndex);
                    }
                    else RemoveEntry(SlotIndex);
                }
                return start - remainder;
            }

            public virtual void RemoveEntry(int SlotIndex) => RemoveSlot(SlotIndex);

            private void RemoveSlot(int SlotIndex) {
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
                cxt.SetDirty();
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
                cxt.SetDirty();

                return true;
            }

            public virtual bool AddEntry(IItem entry, int index) => AddSlot(entry, index);

            private bool AddSlot(IItem entry, int index) {
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
                cxt.SetDirty();

                return true;
            }
            public void ResizeReplicated(int size) {
                if (size == capacity) return;
                // Clear truncated slots without running derived inventory compaction.
                for (int slot = size; slot < capacity; slot++) RemoveSlot(slot);
                ResizeInventory(size);
            }

            // Replication assigns a slot; it must not invoke StackInventory's compaction policy.
            public void ApplyReplicatedSlot(int slot, IItem item) {
                if ((uint)slot >= capacity) return;
                RemoveSlot(slot);
                if (item != null) AddSlot((IItem)item.Clone(), slot);
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
                // Build the name table without changing live inventory IDs.
                Dictionary<string, int> lookup = new Dictionary<string, int>();

                void Serialize(ref IItem item) {
                    if (item is null) return;
                    IRegister registry = item.GetRegistry();
                    string name = registry.RetrieveName(item.Index);
                    lookup.TryAdd(name, lookup.Count);
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
}
