using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Core.Events;
using Arterra.Core.Network;
using Arterra.Core.Storage;
using Arterra.Data.Entity;
using Arterra.Data.Entity.Behavior;
using Arterra.Data.Item;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay.UI;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Data.Entity.Behavior.PlayerInventoriesBehavior;

namespace Arterra.Data.Material {
    // Owns the placed object's inventories and network handlers. Display context is local only.
    public abstract class MaterialInventory : MaterialInstance, PanelNavbarManager.INavPanel {
        [JsonIgnore] public NetworkEventControl Events { get; private set; }
        [JsonIgnore] public bool Initialized { get; private set; }
        [JsonIgnore] protected BehaviorEntity.Animal Viewer;
        [JsonIgnore] protected PlayerInventoriesBehavior ViewerInventory;
        [JsonIgnore] protected abstract Inventory[] Inventories { get; }
        private readonly HashSet<(int inventory, int slot)> dirty = new();
        private readonly HashSet<PlayerInventoriesBehavior> users = new();
        public void AddUser(PlayerInventoriesBehavior player) => users.Add(player);
        public void RemoveUser(PlayerInventoriesBehavior player) => users.Remove(player);
        private string PanelName => Id.ToString();

        protected MaterialInventory() { }
        protected MaterialInventory(int3 coordinate) : base(coordinate) { }

        public override void Initialize() {
            if (Initialized) return;
            Initialized = true;
            Events = new NetworkEventControl();
            Events.Register(Id);
            foreach (var inventory in Inventories) {
                inventory.AddCallbacks(SetupContext, SetupContext);
                inventory.ReapplyHandles();
            }

            if (NetworkManager.IsActingServer) {
                Events.AddEventHandler(GameEvent.Apply_MaterialInventory, Interact);
                Events.AddEventHandler(GameEvent.Apply_CloseMaterial, Close);
                Events.RemoteSubscribed += OnSubscribed;
            } else Events.AddEventHandler(GameEvent.Material_InventorySlots, ReceiveSlots);
            InitializeSimulation();
        }

        protected virtual void InitializeSimulation() { }
        protected virtual void AfterInteraction() { }

        private ItemContext SetupContext(ItemContext context) => context.SetupScenario(this, ItemContext.Scenario.MaterialContainer);

        public void SetDirty(IInventory inventory, int slot) {
            if (!NetworkManager.IsActingServer) return;
            var inventories = Inventories;
            for (int i = 0; i < inventories.Length; i++)
                if (ReferenceEquals(inventories[i], inventory)) dirty.Add((i, slot));
            CPUMapManager.MarkMetadataDirty(position);
        }

        public bool PublishSplitItem(IItem item) {
            foreach (var inventory in Inventories) {
                for (int i = 0; i < inventory.capacity; i++) {
                    if (!ReferenceEquals(inventory.PeekItem(i), item)) continue;
                    inventory.RefreshSlot(i, () => { });
                    SetDirty(inventory, i);
                    return true;
                }
            }
            return false;
        }

        private void OnSubscribed(GameEvent type, Guid player) {
            if (type != GameEvent.Material_InventorySlots) return;
            EntityManager.AddHandlerEvent(() => { if (Initialized) PublishAll(); });
        }

        public void PublishAll() {
            if (Events == null) return;
            var inventories = Inventories;
            for (int i = 0; i < inventories.Length; i++) {
                var slots = new List<SlotValue>();
                for (int slot = 0; slot < inventories[i].capacity; slot++)
                    slots.Add(new SlotValue { Slot = slot, Item = inventories[i].PeekItem(slot) });
                Publish(i, slots);
            }
        }

        public void FlushChanges() {
            if (!NetworkManager.IsActingServer || Events == null) return;
            var inventories = Inventories;
            for (int i = 0; i < inventories.Length; i++) {
                var slots = new List<SlotValue>();
                bool changed = false;
                foreach (var entry in dirty) {
                    if (entry.inventory != i) continue;
                    changed = true;
                    if ((uint)entry.slot >= inventories[i].capacity) continue;
                    slots.Add(new SlotValue { Slot = entry.slot, Item = inventories[i].PeekItem(entry.slot) });
                }
                // A removed tail slot may be outside the new capacity; still publish the resize.
                if (changed) Publish(i, slots);
            }
            dirty.Clear();
        }

        private void Publish(int inventory, List<SlotValue> slots) =>
            Events.RaiseEvent(GameEvent.Material_InventorySlots, Id, inventory,
                new SlotUpdate { Capacity = (int)Inventories[inventory].capacity, Slots = slots });

        public struct SlotValue { public int Slot; public IItem Item; }
        public struct SlotUpdate { public int Capacity; public List<SlotValue> Slots; }

        private void ReceiveSlots(object actor, object target, object context) {
            if (target is not int index || context is not SlotUpdate update || (uint)index >= Inventories.Length) return;
            var inventory = Inventories[index];
            inventory.ResizeReplicated(update.Capacity);
            foreach (var slot in update.Slots) inventory.ApplyReplicatedSlot(slot.Slot, slot.Item);
            if (inventory is StackInventory stack) stack.RefreshReplicatedDisplay();
            RefreshDisplay();
        }

        protected bool GetPlayer(object actor, out PlayerInventoriesBehavior player) {
            player = null;
            return actor is Guid id && EntityManager.TryGetEntity(id, out var entity)
                && entity.Is(out player) && ReferenceEquals(player.ActiveMaterial, this);
        }

        private void Interact(object actor, object target, object context) {
            if (!GetPlayer(actor, out var player) || context is not InventoryInteraction request) return;
            if (target is InventoryKind kind) player.InteractInventory(actor, kind, request);
            else if (target is int index && (uint)index < Inventories.Length)
                player.InteractMaterial(Inventories[index], request);
            else return;
            AfterInteraction();
            FlushChanges();
        }

        private void Close(object actor, object target, object context) {
            if (GetPlayer(actor, out var player)) player.CloseMaterial();
        }

        protected abstract void CreateDisplay();
        protected abstract void ReleaseDisplay();
        protected virtual void RefreshDisplay() { }
        protected abstract GameEvent OpenEvent { get; }
        public abstract Sprite GetNavIcon();
        public abstract GameObject GetDispContent();

        public void InitializeDisplay(BehaviorEntity.Animal player) {
            if (!player.IsOwner || GetDispContent() != null) return;
            Initialize();
            Viewer = player;
            player.Is(out ViewerInventory);
            CreateDisplay();
            ViewerInventory.Activate(clearCursor: false);
            InputPoller.AddContextFence("MAT::Container", "3.5::Window", ActionBind.Exclusion.None);
            InputPoller.AddBinding(new ActionBind("Open Inventory", _ => CloseDisplay(),
                ActionBind.Exclusion.ExcludeLayer, ActionBind.Policy.Down), "MAT::Close", "3.5::Window");
            Bind("Deselect", InventoryAction.DeselectDrag, ActionBind.Policy.Default);
            Bind("Select", InventoryAction.Select, ActionBind.Policy.Down);
            Bind("SelectPartial", InventoryAction.SelectPartial, ActionBind.Policy.Down);
            InputPoller.AddBinding(new ActionBind("SelectAll", _ => {
                if (!GetDispContent().activeInHierarchy) return;
                for (int i = 0; i < Inventories.Length; i++) Request(InventoryAction.SelectAll, i, -1);
            }, policy: ActionBind.Policy.Down), "MAT::SelectAll", "3.0::AllWindow");
            PanelNavbarManager.Add(this, PanelName);
            PanelNavbarManager.Activate(PanelName);
        }

        private void Bind(string key, InventoryAction action, ActionBind.Policy policy) {
            InputPoller.AddBinding(new ActionBind(key, _ => {
                if (!GetDispContent().activeInHierarchy) return;
                var inventories = Inventories;
                for (int i = 0; i < inventories.Length; i++) {
                    if (!inventories[i].Display.GetMouseSelected(out int slot))
                        continue;
                    Request(action, i, slot);
                    InputPoller.SuspendKeybindPropogation(key, ActionBind.Exclusion.ExcludeLayer);
                    break;
                }
            }, policy: policy), "MAT::" + key, "3.5::Window");
        }

        private void Request(InventoryAction action, int inventory, int slot) =>
            Events.RaiseServerEvent(GameEvent.Apply_MaterialInventory, Viewer.info.rtEntityId, inventory,
                new InventoryInteraction { Action = action, Slot = slot });

        protected void RequestRecipe(int recipe) =>
            Events.RaiseServerEvent(GameEvent.Apply_MaterialRecipe, Viewer.info.rtEntityId, recipe);

        protected void RequestGrind(float elapsed) =>
            Events.RaiseServerEvent(GameEvent.Apply_MaterialGrind, Viewer.info.rtEntityId, null, elapsed);

        public void Activate() {
            GetDispContent().SetActive(true);
            Viewer.eventCtrl.RaiseEvent(OpenEvent, Viewer.info.rtEntityId, Id);
        }

        public void Deactivate() => GetDispContent()?.SetActive(false);

        public void CloseDisplay() {
            if (Viewer == null) return;
            Events.RaiseServerEvent(GameEvent.Apply_CloseMaterial, Viewer.info.rtEntityId, null);
            PanelNavbarManager.Remove(PanelName);
            PanelNavbarManager.Deactivate();
        }

        public void Release() {
            if (Viewer == null) return;
            InputPoller.RemoveBinding("MAT::SelectAll", "3.0::AllWindow");
            InputPoller.RemoveContextFence("MAT::Container", "3.5::Window");
            ReleaseDisplay();
            ViewerInventory.Deactivate(clearCursor: false);
            ViewerInventory.CloseMaterial();
            Viewer = null;
            ViewerInventory = null;
        }

        public override void Dispose() {
            if (!Initialized) return;
            if (Viewer != null) {
                EntityManager.AddHandlerEvent(() => {
                    if (Viewer == null) return;
                    PanelNavbarManager.Remove(PanelName);
                    PanelNavbarManager.Deactivate();
                });
            }
            foreach (var user in new List<PlayerInventoriesBehavior>(users))
                EntityManager.AddHandlerEvent(() => {
                    if (ReferenceEquals(user.ActiveMaterial, this)) user.CloseMaterial();
                });
            Initialized = false;
            foreach (var inventory in Inventories) inventory.UnapplyHandles();
            Events.Dispose();
            Events = null;
            dirty.Clear();
        }
    }
}
