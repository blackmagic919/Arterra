using Arterra.Data.Material;
using Arterra.Core.Storage;
using Arterra.GamePlay.Interaction;
using Arterra.GamePlay;
using TMPro;
using UnityEngine.UI;
using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.Data.Player;
using Arterra.GamePlay.UI;
using Arterra.Core.Events;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
	/// <summary> Settings controlling the size and apperance of the inventory,
    /// a system allowing the player to hold and use
    /// <see cref="Generation.Item"> items </see>. </summary>
    [Serializable]
    public class PlayerInventorySettings : IBehaviorSetting {
		///<summary>Name of settings object in UI generation</summary>
        [JsonIgnore] public static string Name => "Inventory";
        /// <summary> The amount of slots in the primary inventory, the hotbar. This is
        /// equivalent to the maximum amount of items that can be held in the hotbar.  </summary>
        public int PrimarySlotCount;
        /// <summary> The amount of slots in the secondary inventory, the hidden inventory. This is
        /// equivalent to the maximum amount of items that can be held in the hidden inventory. </summary>
        public int SecondarySlotCount;

		/// <summary>How fast the player can recover items from its inventory</summary>
		public float RecoverSpeedMultiplier = 25;

        /// <summary>
        /// The color of the selected slot in the <see cref="PlayerInventoriesBehavior.Primary">Primary Inventory</see>, or
        /// the hotbar. This color is used to indicate which item currently has the status of being <see cref="PlayerInventoriesBehavior.Selected">
        /// selected. </see>
        /// </summary>
        public Color SelectedColor;
        /// <summary> The color of the base slot in the Inventory. The color
        /// of the slot when it is empty (the item held by the slot is null). </summary>
        public Color BaseColor;

		public object Clone() {
			return new PlayerInventorySettings {
				PrimarySlotCount = PrimarySlotCount,
				SecondarySlotCount = SecondarySlotCount,
				SelectedColor = SelectedColor,
				BaseColor = BaseColor
			};
		}
    }

	/// <summary>
	/// Owns player inventories and item transfer/drop helpers for behavior-based player flow.
	/// </summary>
	public class PlayerInventoriesBehavior : SpeciesBehavior {
		[JsonIgnore] public PlayerInventorySettings settings;
		private Modifier mod;

		public Inventory Primary;
		public Inventory Secondary;
		[JsonIgnore] public ArmorInventory ArmorI => Armor.Inventory;
        [JsonIgnore] private PlayerArmorBehavior Armor => self.Is(out PlayerArmorBehavior armor) ? armor : null;
		public int SelectedIndex;

        private readonly Dictionary<(GameEvent, InventoryKind, int), RefEventHandler> itemHandlers = new();

        public enum InventoryKind { Primary, Secondary, Armor, CraftingMaterials, CraftingItems, Cursor }

		private BehaviorEntity.Animal self;

        [JsonIgnore] public PlayerCraftingBehavior Crafting => self.Is(out PlayerCraftingBehavior crafting) ? crafting : null;
        [JsonIgnore] public InventoryCursorState CursorState;
        private IItem restoredCursor;
        [JsonProperty] private IItem CursorItem {
            get => CursorState == null ? restoredCursor
                : CursorState.State == InventoryCursorState.PlaceState.Split && CursorState.IsSplitUp
                    ? null : CursorState.Item;
            set => restoredCursor = value;
        }

        public enum InventoryAction { Select, SelectPartial, DeselectDrag, SelectAll, Clear, Armor, Resource }

        public struct InventoryInteraction {
            public InventoryAction Action;
            public int Slot;
            public bool Root;
            public int AlternateSlot;
            public bool CraftingTarget;
        }

        private void BindInteractions() {
            if (!self.IsAuthority) return;

            self.eventCtrl.AddEventHandler(GameEvent.Apply_InventoryInteraction, InteractInventory);

        }

        private void UnbindInteractions() {
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_InventoryInteraction, InteractInventory);

        }

        private void RefreshCrafting(InventoryKind kind) {
            if (self.IsOwner && kind is InventoryKind.CraftingMaterials or InventoryKind.CraftingItems)
                Crafting?.Refresh();
        }

        private void PublishSplitItem(IItem item) {
            if (ActiveMaterial?.PublishSplitItem(item) == true) return;
            foreach (InventoryKind kind in new[] { InventoryKind.Primary, InventoryKind.Secondary,
                InventoryKind.CraftingMaterials, InventoryKind.CraftingItems }) {
                IInventory inventory = GetInventory(kind);
                for (int slot = 0; slot < inventory.Capacity; slot++) {
                    if (!ReferenceEquals(inventory.PeekItem(slot), item)) continue;
                    inventory.RefreshSlot(slot, () => { });
                    SetDirty(kind, slot);
                    return;
                }
            }
        }

        private void PublishCursor() {
            IItem visible = CursorState.Visible ? CursorState.Item : null;
            if (self.IsOwner) Cursor?.SetItem(visible);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_SetInventorySlot,
                self.info.rtEntityId, InventoryKind.Cursor, (0, visible));
        }

        public void InteractInventory(object source, object target, object context) {
            if (target is not InventoryKind kind || context is not InventoryInteraction request) return;
            int slot = request.Slot;
            if (request.CraftingTarget) {
                bool material;
                if (CursorState.IsHolding) {
                    var setting = Config.CURRENT.Generation.Items.Retrieve(CursorState.Item.Index);
                    material = setting is PlaceableItem placeable && Arterra.Configuration.Config.CURRENT.Generation.Materials.value.MaterialDictionary.Contains(placeable.MaterialName);
                } else material = (uint)request.AlternateSlot >= Crafting.Items.Capacity
                    || Crafting.Items.PeekItem(request.AlternateSlot) == null;
                kind = material ? InventoryKind.CraftingMaterials : InventoryKind.CraftingItems;
                slot = material ? request.Slot : request.AlternateSlot;
            }
            ApplyInteraction(GetInventory(kind), kind, request, slot);
        }

        public void InteractMaterial(IInventory inventory, InventoryInteraction request) =>
            ApplyInteraction(inventory, InventoryKind.Cursor, request, request.Slot);

        private void ApplyInteraction(IInventory inventory, InventoryKind kind, InventoryInteraction request, int slot) {
            IItem oldItem = CursorState.Item;
            int oldAmount = oldItem?.AmountRaw ?? 0;
            bool oldVisible = CursorState.Visible;
            bool GetSelected(out IInventory selected, out int index) {
                selected = inventory;
                index = slot;
                return selected != null && (uint)index < selected.Capacity;
            }

            switch (request.Action) {
                case InventoryAction.Select:
                    CursorState.Select(GetSelected, request.Root);
                    break;
                case InventoryAction.SelectPartial:
                    CursorState.SelectPartial(GetSelected, request.Root);
                    break;
                case InventoryAction.DeselectDrag:
                    CursorState.DeselectDrag(GetSelected, request.Root);
                    break;
                case InventoryAction.SelectAll:
                    if (inventory is Inventory stack) 
						CursorState.SelectAll(stack);
                    break;
                case InventoryAction.Clear:
                    CursorState.ClearCursor(AddItem);
                    break;
                case InventoryAction.Resource:
                    var items = Config.CURRENT.Generation.Items;
                    if (!Config.CURRENT.GamePlay.Gamemodes.value.ResourceInventory || !items.Contains(slot)) return;

                    IItem resource = items.Retrieve(slot).Item;
                    resource.Create(slot, resource.StackLimit);
                    CursorState.ClearCursor(AddItem);
                    CursorState.HoldItem(resource);
                    break;
                case InventoryAction.Armor:
                    if (kind != InventoryKind.Armor || (uint)slot >= ArmorI.Capacity || CursorState.IsSplitUp) return;
                    if (CursorState.IsHolding) {
                        if (CursorState.Item is not ArmorInventory.IArmorItem armor || !ArmorI.AddEntry(armor, slot))
                            SpawnDrop(CursorState.Item, self.position);
                        CursorState.ClearCursor();
                    } else if (ArmorI.PeekItem(slot) is IItem item) {
                        item = (IItem)item.Clone();
                        ArmorI.RemoveEntry(slot);
                        CursorState.HoldItem(item);
                    }
                    break;
            }
            if (oldItem != CursorState.Item || oldAmount != (CursorState.Item?.AmountRaw ?? 0)
                || oldVisible != CursorState.Visible) PublishCursor();
            ActiveMaterial?.FlushChanges();
        }

        [JsonIgnore] public MaterialInventory ActiveMaterial { get; private set; }

        public void OpenMaterial(MaterialInventory material) {
            if (!self.IsAuthority) return;
            if (!ReferenceEquals(ActiveMaterial, material)) {
                if (self.IsOwner) ActiveMaterial?.CloseDisplay();
                CloseMaterial();
                CursorState.ClearCursor(AddItem);
                PublishCursor();
                material.Initialize();
                ActiveMaterial = material;
                material.AddUser(this);
            }
            self.eventCtrl.RaiseEvent(GameEvent.Entity_OpenMaterial, self.info.rtEntityId, material.Id, material.position);
        }

        private Guid pendingMaterial;
        private int3 pendingMaterialPosition;

        private void DisplayMaterial(object source, object target, object context) {
            if (!self.IsOwner || target is not Guid id || context is not int3 GCoord) return;
            if (ActiveMaterial != null && ActiveMaterial.Id != id) ActiveMaterial.CloseDisplay();
            pendingMaterial = id;
            pendingMaterialPosition = GCoord;
            if (CPUMapManager.TryGetExistingMapMeta(GCoord, out MaterialInventory material))
                DisplayInstalledMaterial(material);
        }

        private void DisplayInstalledMaterial(MaterialInstance instance) {
            if (instance.Id != pendingMaterial || instance is not MaterialInventory material) return;
            EntityManager.AddHandlerEvent(() => {
                if (material.Id != pendingMaterial || !material.Initialized) return;
                pendingMaterial = Guid.Empty;
                ActiveMaterial = material;
                material.InitializeDisplay(self);
            });
        }

        private void CancelPendingMaterial(int3 chunk) {
            int size = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            int3 cell = ((pendingMaterialPosition % size) + size) % size;
            if (math.all((pendingMaterialPosition - cell) / size == chunk)) pendingMaterial = Guid.Empty;
        }

        public void CloseMaterial() {
            if (self.IsAuthority && ActiveMaterial != null) {
                CursorState.ClearCursor(AddItem);
                PublishCursor();
            }
            ActiveMaterial?.RemoveUser(this);
            ActiveMaterial = null;
            pendingMaterial = Guid.Empty;
        }

        [JsonIgnore] public CursorDisplay Cursor;
        private GameObject Menu;
        private ItemDescription itemInfo;
        private Catalogue<Item.Authoring> ItemSettings;
        [JsonIgnore] public Item.Authoring SelectedSetting => ItemSettings.Retrieve(Selected.Index);

        public override void InitializeVisual(BehaviorEntity.Animal self) {
            if (!self.IsOwner || Menu != null) return;
            self.eventCtrl.AddEventHandler(GameEvent.Entity_OpenMaterial, DisplayMaterial);
            CPUMapManager.MetadataInstalled += DisplayInstalledMaterial;
            CPUMapManager.AddOnReleaseChunk(CancelPendingMaterial);
            Menu = GameObject.Instantiate(Resources.Load<GameObject>("Prefabs/GameUI/Inventory/Panel"), GameUIManager.UIHandle.transform);
            ItemSettings = Config.CURRENT.Generation.Items;
            Cursor = new CursorDisplay(Menu.transform);
            Cursor.SetItem(CursorState.Visible ? CursorState.Item : null);
            itemInfo = new ItemDescription(Menu.transform.GetChild(0));
            BindDisplays();
            Secondary.Display.parent.SetActive(false);
            InputPoller.AddBinding(new ActionBind("Open Inventory", Activate, policy: ActionBind.Policy.Down),
                "PlayerInventory:OPN", "3.5::Window");
            AddHotbarKeybinds();
        }

        private void BindDisplays() {
            Primary.UnapplyHandles();
            Secondary.UnapplyHandles();
            Primary.InitializeDisplay(Menu.transform.GetChild(0).Find("Primary").gameObject);
            Secondary.InitializeDisplay(Menu.transform.GetChild(0).Find("Secondary").gameObject);
            Primary.AddCallbacks(GetPrimaryCxt, GetPrimaryCxt);
            Secondary.AddCallbacks(GetSecondaryCxt, GetSecondaryCxt);
            Primary.ReapplyHandles();
            Secondary.ReapplyHandles();
        }

        public override void Update(BehaviorEntity.Animal self) {
            if (self.context == BehaviorEntity.UpdateContext.Visual && self.IsOwner) Cursor?.Update();
        }

        public override void DisposeVisual(BehaviorEntity.Animal self) {
            if (Menu == null) return;
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_OpenMaterial, DisplayMaterial);
            CPUMapManager.MetadataInstalled -= DisplayInstalledMaterial;
            CPUMapManager.RemoveOnReleaseChunk(CancelPendingMaterial);
            pendingMaterial = Guid.Empty;
            ActiveMaterial?.CloseDisplay();
            InputPoller.RemoveContextFence("PlayerInventory:WndFrame", "3.5::Window");
            InputPoller.RemoveContextFence("PlayerInventory:AllFrame", "3.0::AllWindow");
            InputPoller.RemoveStackPoll("Frame:Inventory", "CursorLock");
            InputPoller.RemoveBinding("PlayerInventory:OPN", "3.5::Window");
            for (int i = 1; i <= 9; i++) InputPoller.RemoveBinding("PlayerInventory:HB" + i, "2.0::Subscene");
            Primary.UnapplyHandles();
            Secondary.UnapplyHandles();
            Primary.ReleaseDisplay();
            Secondary.ReleaseDisplay();
            Primary.AddCallbacks(SetupContext, SetupContext);
            Secondary.AddCallbacks(SetupContext, SetupContext);
            Primary.ReapplyHandles();
            Secondary.ReapplyHandles();
            Cursor?.Dispose();
            Cursor = null;
            GameObject.Destroy(Menu);
            Menu = null;
            itemInfo = null;
        }

        private ItemContext GetSecondaryCxt(ItemContext cxt) => cxt.SetupScenario(self, ItemContext.Scenario.ActivePlayerSecondary);
        private ItemContext GetPrimaryCxt(ItemContext cxt) {
            if (cxt.InvId == SelectedIndex) {
                itemInfo?.SwitchItem(Selected);
                return HeldCxt;
            }
            return cxt.SetupScenario(self, ItemContext.Scenario.ActivePlayerPrimary);
        }
        private ItemContext HeldCxt => new(Primary, self, ItemContext.Scenario.ActivePlayerSelected, SelectedIndex);

        private void Activate(float _) {
            Activate();
            PanelNavbarManager.Activate();
        }
        private void Deactivate(float _) {
            PanelNavbarManager.Deactivate();
            Deactivate();
        }
        public void Activate(bool clearCursor = true) {
            self.eventCtrl.RaiseEvent(GameEvent.Action_OpenInventory, self.info.rtEntityId, null);
            InputPoller.AddStackPoll(new ActionBind("Frame:Inventory", _ => InputPoller.SetCursorLock(false)), "CursorLock");

            InputPoller.AddContextFence("PlayerInventory:AllFrame", "3.0::AllWindow", ActionBind.Exclusion.None);
            InputPoller.AddContextFence("PlayerInventory:WndFrame", "3.5::Window", ActionBind.Exclusion.ExcludeAll);
            InputPoller.AddBinding(new ActionBind("Open Inventory", Deactivate, policy: ActionBind.Policy.Down), "PlayerInventory:CLS", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Deselect", _ => DeselectDrag(GetMouseTarget, true)), "PlayerInventory:DSL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("Select", _ => Select(GetMouseTarget, true), policy: ActionBind.Policy.Down),  "PlayerInventory:SEL", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectPartial", _ => SelectPartial(GetMouseTarget, true), policy: ActionBind.Policy.Down),  "PlayerInventory:SELP", "3.5::Window");
            InputPoller.AddBinding(new ActionBind("SelectAll", _ => { SelectAll(InventoryKind.Primary); SelectAll(InventoryKind.Secondary); },
                ActionBind.Exclusion.ExcludeLayer,  ActionBind.Policy.Down),"PlayerInventory:SELA", "3.0::AllWindow");

            if (clearCursor) RequestInteraction(InventoryAction.Clear);
            Secondary.Display.parent.SetActive(true);
        }

        public void Deactivate(bool clearCursor = true) {
            InputPoller.RemoveStackPoll("Frame:Inventory", "CursorLock");

                InputPoller.RemoveContextFence("PlayerInventory:WndFrame", "3.5::Window");
                InputPoller.RemoveContextFence("PlayerInventory:AllFrame", "3.0::AllWindow");

            Secondary.Display.parent.SetActive(false);
            if (clearCursor) RequestInteraction(InventoryAction.Clear);
        }

        private void AddHotbarKeybinds() {
            void ChangeSelected(int index) {
                Selected?.OnLeave(HeldCxt);
                Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.BaseColor;

                SelectedIndex = index % settings.PrimarySlotCount;
                Selected?.OnEnter(HeldCxt);
                Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.SelectedColor;
                itemInfo?.SwitchItem(Selected);
            }
            InputPoller.AddBinding(new ActionBind("Hotbar1", _ => ChangeSelected(0), policy: ActionBind.Policy.Down),
                 "PlayerInventory:HB1", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar2", _ => ChangeSelected(1), policy: ActionBind.Policy.Down),
                 "PlayerInventory:HB2", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar3", _ => ChangeSelected(2), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB3", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar4", _ => ChangeSelected(3), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB4", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar5", _ => ChangeSelected(4), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB5", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar6", _ => ChangeSelected(5), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB6", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar7", _ => ChangeSelected(6), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB7", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar8", _ => ChangeSelected(7), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB8", "2.0::Subscene");
            InputPoller.AddBinding(new ActionBind("Hotbar9", _ => ChangeSelected(8), policy: ActionBind.Policy.Down),
                "PlayerInventory:HB9", "2.0::Subscene");
            Primary.Display.Slots[SelectedIndex].GetComponent<Image>().color = settings.SelectedColor; //Set the inital selected color
        }

        private bool GetMouseTarget(out InventoryKind Inv, out int index) {
            Inv = InventoryKind.Cursor;
            if (Primary.Display.GetMouseSelected(out index)) {
                Inv = InventoryKind.Primary;
            } else if (Secondary.Display.GetMouseSelected(out index)) {
                Inv = InventoryKind.Secondary;
            } else return false;
            return true;
        }
        public void DropItem(IItem item) {
            if (item == null) return;
            var player = self;
            player.eventCtrl.RaiseEvent(GameEvent.Apply_DropItem, player.info.rtEntityId, null, item.Clone());
        }

        public static void SpawnDrop(IItem item, float3 location, Quaternion rotation = default) {
            if (item == null) return;
            var eReg = Config.CURRENT.Generation.Entities;
            int eIndex = eReg.RetrieveIndex("EntityItem");
            Entity Entity = eReg.Retrieve("EntityItem").Entity;
            Entity.Index = eIndex; Entity.RegisterConstructor(item);
            EntityManager.CreateEntity(location, (uint)eIndex, Entity);
        }

        public delegate bool MouseInvSelect(out InventoryKind kind, out int index);
        public bool Select(MouseInvSelect GetSelectedInvInfo) => Select(GetSelectedInvInfo, false);
        public bool SelectPartial(MouseInvSelect GetSelectedInvInfo) => SelectPartial(GetSelectedInvInfo, false);
        public bool DeselectDrag(MouseInvSelect GetSelectedInvInfo) => DeselectDrag(GetSelectedInvInfo, false);
        public void RequestInteraction(InventoryAction action,
            InventoryKind kind = InventoryKind.Cursor,
            int slot = -1, bool root = false) {
            RequestInteraction(kind, new InventoryInteraction { Action = action, Slot = slot, Root = root });
        }

        public void RequestInteraction(InventoryKind kind, InventoryInteraction request) {
            var player = self;
            if (player == null) return;
            if (ActiveMaterial != null && ActiveMaterial.Initialized)
                ActiveMaterial.Events.RaiseServerEvent(GameEvent.Apply_MaterialInventory, player.info.rtEntityId, kind, request);
            else player.eventCtrl.RaiseEvent(GameEvent.Apply_InventoryInteraction, player.info.rtEntityId, kind, request);
        }

        private bool SendInteraction(MouseInvSelect getSelected, bool root,
            InventoryAction action) {
            bool selected = getSelected(out InventoryKind kind, out int slot);
            if (selected || root) RequestInteraction(action, kind, slot, root);
            return selected;
        }

        private bool Select(MouseInvSelect getSelected, bool root) =>
            SendInteraction(getSelected, root, InventoryAction.Select);

        private bool SelectPartial(MouseInvSelect getSelected, bool root) =>
            SendInteraction(getSelected, root, InventoryAction.SelectPartial);

        private bool DeselectDrag(MouseInvSelect getSelected, bool root) =>
            SendInteraction(getSelected, root, InventoryAction.DeselectDrag);

        public bool SelectAll(InventoryKind kind) {
            RequestInteraction(PlayerInventoriesBehavior.InventoryAction.SelectAll, kind);
            return true;
        }

        //The Slot is changed to reflect what remains
        //if stackable, add to primary then secondary, else add to secondary then primary

        public bool TryClearSelected() {
            if (Selected == null) return false;
            if (Selected.AmountRaw != 0) return false;
            Primary.RemoveEntry(SelectedIndex);
            return true;
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
                Data.Item.Authoring settings = Config.CURRENT.Generation.Items.Retrieve(item.Index);
                Name.text = settings.Name;
                Description.text = settings.Description;
                animator.SetTrigger("Fade");
            }
        }
        public sealed class CursorDisplay {
            public IItem Item { get; private set; }
            private readonly InventorySlotDisplay display;

            public CursorDisplay(Transform parent) {
                display = new InventorySlotDisplay(Indicators.ItemSlots.Get());
                display.Object.transform.SetParent(parent, false);
                display.Object.SetActive(false);
            }

            public void SetItem(IItem item) {
                if (display.Object.activeSelf) Item?.ClearDisplay(display.transform);
                Item = item == null ? null : (IItem)item.Clone();
                display.Object.SetActive(Item != null);
                Item?.AttachDisplay(display.transform, Item.Index);
            }

            public void Update() => display.transform.position = Input.mousePosition;
            public void Dispose() {
                SetItem(null);
                Indicators.ItemSlots.Release(display.Object);
            }
        }

        private void TransferInventory(object source, object target, object context) {
            if (!Config.CURRENT.GamePlay.Gamemodes.value.KeepInventory || target is not Guid id
                || !EntityManager.TryGetEntity(id, out Entity next) || !next.Is(out PlayerInventoriesBehavior replacement)) return;
            bool display = replacement.Menu != null;
            if (display) replacement.DisposeVisual(replacement.self);
            Primary.UnapplyHandles();
            Secondary.UnapplyHandles();

            replacement.Primary.UnapplyHandles();
            replacement.Secondary.UnapplyHandles();
            (Primary, replacement.Primary) = (replacement.Primary, Primary);
            (Secondary, replacement.Secondary) = (replacement.Secondary, Secondary);

            Primary.AddCallbacks(SetupContext, SetupContext);
            Secondary.AddCallbacks(SetupContext, SetupContext);
            replacement.Primary.AddCallbacks(replacement.SetupContext, replacement.SetupContext);
            replacement.Secondary.AddCallbacks(replacement.SetupContext, replacement.SetupContext);

            Primary.ReapplyHandles();
            Secondary.ReapplyHandles();
            replacement.Primary.ReapplyHandles();
            replacement.Secondary.ReapplyHandles();
            if (self.IsAuthority) {
                if (CursorState.IsHolding && !(CursorState.State == InventoryCursorState.PlaceState.Split && CursorState.IsSplitUp))
                    replacement.CursorState.HoldItem(CursorState.Item);
                CursorState.ClearCursor();
            }
            if (display) replacement.InitializeVisual(replacement.self);
            if (self.IsAuthority) replacement.PublishCursor();
        }

		private float RecoverSpeedMultiplier => Modifier.GetInt(mod, MSettings.RecoverRate, settings.RecoverSpeedMultiplier);

		[JsonIgnore]
		public IItem Selected {
			get {
				if (Primary == null || Primary.Info == null) return null;
				if (SelectedIndex < 0 || SelectedIndex >= Primary.Info.Length) return null;
				return Primary.Info[SelectedIndex];
			}
		}

		public override void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
			hierarchy.TryAdd(typeof(PlayerInventorySettings), new PlayerInventorySettings());
		}

		public override void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: PlayerInventoriesBehavior requires PlayerInventorySettings");
			if (!self.Is(out mod)) mod = null;
			this.self = self;
			self.Register(this);
			EnsureInventories();
            BindEvents();
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Respawn, TransferInventory);
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, HandleCollect);
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_RecieveItem, ReceiveItem);

		}

		public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: PlayerInventoriesBehavior requires PlayerInventorySettings");
			if (!self.Is(out mod)) mod = null;
			this.self = self;
			self.Register(this);
			EnsureInventories();
            BindEvents();
            self.eventCtrl.AddEventHandler(GameEvent.Entity_Respawn, TransferInventory);
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, HandleCollect);
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_RecieveItem, ReceiveItem);
		}

		public override void Disable(BehaviorEntity.Animal self) {
            DisposeVisual(self);
            UnbindInteractions();
            ActiveMaterial?.CloseDisplay();
            CloseMaterial();
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_OpenMaterial, DisplayMaterial);
            CPUMapManager.MetadataInstalled -= DisplayInstalledMaterial;
            CPUMapManager.RemoveOnReleaseChunk(CancelPendingMaterial);
			Primary?.UnapplyHandles();
            Secondary?.UnapplyHandles();

            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Respawn, TransferInventory);
			self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Collect, HandleCollect);
			self.eventCtrl.RemoveEventHandler(GameEvent.Entity_RecieveItem, ReceiveItem);

            self.eventCtrl.RemoveEventHandler(GameEvent.Item_MutateItem, MutateItem);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_SetInventorySlot, ApplySlot);
            self.eventCtrl.RemoveEventHandler(GameEvent.Apply_DropItem, DropItem);
            foreach (var pair in itemHandlers)
                self.eventCtrl.RemoveEventHandler(pair.Key.Item1, pair.Value);
            itemHandlers.Clear();
			this.self = null;
		}

		private void EnsureInventories() {
			Primary ??= new Inventory(settings.PrimarySlotCount);
			Secondary ??= new Inventory(settings.SecondarySlotCount);
            CursorState = new InventoryCursorState(AddItem, item => {
                if (item != null) SpawnDrop(item, self.position);
            }, PublishSplitItem);
            if (restoredCursor != null) CursorState.HoldItem(restoredCursor);
            restoredCursor = null;
			SelectedIndex = math.clamp(SelectedIndex, 0, math.max(settings.PrimarySlotCount - 1, 0));
		}

        private void BindEvents() {
            BindInteractions();
            Primary.AddCallbacks(SetupContext, SetupContext);
            Secondary.AddCallbacks(SetupContext, SetupContext);
            Primary.ReapplyHandles();
            Secondary.ReapplyHandles();

            if (self.IsAuthority) {
                self.eventCtrl.AddEventHandler(GameEvent.Apply_DropItem, DropItem);
                self.eventCtrl.AddEventHandler(GameEvent.Item_MutateItem, MutateItem);

            } else
                self.eventCtrl.AddEventHandler(GameEvent.Entity_SetInventorySlot, ApplySlot);
        }

        private void DropItem(object source, object target, object context) {
            if (context is not IItem item || item.AmountRaw <= 0) return;
            SpawnDrop(item, self.position);
        }

        private ItemContext SetupContext(ItemContext context) =>
            context.SetupScenario(self, ItemContext.Scenario.Default);

        public IInventory GetInventory(InventoryKind kind) => kind switch {
            InventoryKind.Primary => Primary,
            InventoryKind.Secondary => Secondary,
            InventoryKind.Armor => ArmorI,
            InventoryKind.CraftingMaterials => Crafting.Materials,
            InventoryKind.CraftingItems => Crafting.Items,
            _ => null
        };

        public bool TryGetInventoryKind(IInventory inventory, out InventoryKind kind) {
            kind = InventoryKind.Primary;
            if (inventory == Primary) return true;
            kind = InventoryKind.Secondary;
            if (inventory == Secondary) return true;
            kind = InventoryKind.Armor;
            if (inventory == ArmorI) return true;
            kind = InventoryKind.CraftingMaterials;
            if (inventory == Crafting.Materials) return true;
            kind = InventoryKind.CraftingItems;
            return inventory == Crafting.Items;
        }

        public void RegisterEvent(IItem item, ItemContext context, GameEvent type, Action<ItemContext, object> callback, bool authority = true) {
            if (self == null || self.IsAuthority != authority || !context.TryGetInventory(out IInventory inventory)
                || !TryGetInventoryKind(inventory, out InventoryKind kind)) return;

            UnregisterEvent(context, type);
            int slot = context.InvId;
            RefEventHandler handler = (source, target, argument) => {
                if (target is not ValueTuple<InventoryKind, int> address || address != (kind, slot)) return;
                IInventory current = GetInventory(kind);
                if ((uint)slot >= current.Capacity || !ReferenceEquals(current.PeekItem(slot), item)) return;
                if (kind == InventoryKind.Primary) SelectedIndex = slot;
                callback(context, argument);
            };
            itemHandlers.Add((type, kind, slot), handler);
            self.eventCtrl.AddEventHandler(type, handler);
        }

        public void UnregisterEvent(ItemContext context, GameEvent type) {
            if (self == null) return;
            if (!context.TryGetInventory(out IInventory inventory)
                || !TryGetInventoryKind(inventory, out InventoryKind kind)) return;
            if (itemHandlers.Remove((type, kind, context.InvId), out RefEventHandler handler))
                self.eventCtrl.RemoveEventHandler(type, handler);
        }

        public void SetDirty(InventoryKind kind, int slot) {
            if (self == null || !self.IsAuthority) return;
            IInventory inventory = GetInventory(kind);
            if (inventory == null || (uint)slot >= inventory.Capacity) return;
            RefreshCrafting(kind);
            self.eventCtrl.RaiseEvent(GameEvent.Entity_SetInventorySlot,
                self.info.rtEntityId, kind, (slot, inventory.PeekItem(slot)));
        }

        private void MutateItem(object source, object target, object context) {
            if (context is not int slot) return;
            if (target is IInventory inventory && TryGetInventoryKind(inventory, out var kind))
                SetDirty(kind, slot);
        }

        private void ApplySlot(object source, object target, object context) {
            if (target is not InventoryKind kind || context is not ValueTuple<int, IItem> update) return;
            if (kind == InventoryKind.Cursor) {
                if (self.IsOwner) Cursor?.SetItem(update.Item2);
                return;
            }
            IInventory inventory = GetInventory(kind);
            if (inventory == null || (uint)update.Item1 >= inventory.Capacity) return;

            IItem current = inventory.PeekItem(update.Item1);
            IItem incoming = update.Item2;
            if (current != null && incoming != null && current.GetType() == incoming.GetType()
                && current.Index == incoming.Index) {
                // Keep nonserialized action/preview state attached to the current item.
                inventory.RefreshSlot(update.Item1, () =>
                    JsonConvert.PopulateObject(JsonConvert.SerializeObject(incoming), current,
                        new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace }));
                RefreshCrafting(kind);
                return;
            }

            inventory.RemoveEntry(update.Item1);
            if (incoming != null) inventory.AddEntry(incoming, update.Item1);
            RefreshCrafting(kind);
        }

        public int RemoveStackable(int amount, int itemIndex = -1, Action<IItem> OnRemoved = null) {
            if (Selected?.Index == itemIndex) {
                IItem removed = (IItem)Selected.Clone();
                int count = Primary.RemoveStackableSlot(SelectedIndex, amount);
                removed.AmountRaw = count;
                OnRemoved?.Invoke(removed);
                return count;
            }
            if (Primary.EntryDict.ContainsKey(itemIndex))
                return Primary.RemoveStackableKey(itemIndex, amount, OnRemoved);
            return Secondary.RemoveStackableKey(itemIndex, amount, OnRemoved);
        }

        public void AddItem(IItem item) {
            if (item == null) return;
            if (Primary.EntryDict.ContainsKey(item.Index)) Primary.AddStackable(item);
            if (item.AmountRaw == 0) return;
            Secondary.AddStackable(item);
            if (item.AmountRaw == 0) return;
            Primary.AddStackable(item);
            if (item.AmountRaw != 0) SpawnDrop(item, self.position);
        }

        private void ReceiveItem(object source, object target, object context) {
            if (self == null || context is not IItem item) return;
            AddItem(item);
        }

		private void HandleCollect(object actor, object target, object ctx) {
			if (self == null) return;

			int itemCount = Primary.EntryDict.Count + Secondary.EntryDict.Count;
			if (itemCount == 0) return;

			if (ctx is not float amount || target is not Guid receiverId) return;
			if (!EntityManager.TryGetEntity(receiverId, out Entity receiver)) return;
			amount *= RecoverSpeedMultiplier;

			if (Primary.EntryDict.Count > 0) receiver.eventCtrl.EnqueueEvent(
                GameEvent.Entity_RecieveItem, self.info.rtEntityId, receiverId, Primary.LootInventory(amount));
			if (Secondary.EntryDict.Count > 0) receiver.eventCtrl.EnqueueEvent(
                GameEvent.Entity_RecieveItem, self.info.rtEntityId, receiverId, Secondary.LootInventory(amount));
		}
	}
}
