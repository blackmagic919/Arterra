using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
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
        /// The color of the selected slot in the <see cref="InventoryController.Primary">Primary Inventory</see>, or
        /// the hotbar. This color is used to indicate which item currently has the status of being <see cref="InventoryController.Selected">
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

		public InventoryController.Inventory PrimaryI;
		public InventoryController.Inventory SecondaryI;
		public ArmorInventory ArmorI;
		public int SelectedIndex;

        private readonly Dictionary<(GameEvent, InventoryKind, int), RefEventHandler> itemHandlers = new();

        public enum InventoryKind { Primary, Secondary, Armor }

		private BehaviorEntity.Animal self;

		private float RecoverSpeedMultiplier => Modifier.GetInt(mod, MSettings.RecoverRate, settings.RecoverSpeedMultiplier);

		[JsonIgnore]
		public IItem Selected {
			get {
				if (PrimaryI == null || PrimaryI.Info == null) return null;
				if (SelectedIndex < 0 || SelectedIndex >= PrimaryI.Info.Length) return null;
				return PrimaryI.Info[SelectedIndex];
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
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, HandleCollect);
			if (self.IsAuthority) self.eventCtrl.AddEventHandler(GameEvent.Entity_RecieveItem, ReceiveItem);
		}

		public override void Disable(BehaviorEntity.Animal self) {
            PrimaryI?.UnapplyHandles();
            SecondaryI?.UnapplyHandles();
            ArmorI?.UnapplyHandles();
			self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Collect, HandleCollect);
			self.eventCtrl.RemoveEventHandler(GameEvent.Entity_RecieveItem, ReceiveItem);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Damaged, DamageArmor);
            self.eventCtrl.RemoveEventHandler(GameEvent.Item_MutateItem, MutateItem);
            self.eventCtrl.RemoveEventHandler(GameEvent.Entity_SetInventorySlot, ApplySlot);
            foreach (var pair in itemHandlers)
                self.eventCtrl.RemoveEventHandler(pair.Key.Item1, pair.Value);
            itemHandlers.Clear();
			this.self = null;
		}

		private void EnsureInventories() {
			PrimaryI ??= new InventoryController.Inventory(settings.PrimarySlotCount);
			SecondaryI ??= new InventoryController.Inventory(settings.SecondarySlotCount);
			ArmorI ??= new ArmorInventory();
			SelectedIndex = math.clamp(SelectedIndex, 0, math.max(settings.PrimarySlotCount - 1, 0));
		}

        private void BindEvents() {
            PrimaryI.AddCallbacks(SetupContext, SetupContext);
            SecondaryI.AddCallbacks(SetupContext, SetupContext);
            ArmorI.AddCallbacks(SetupContext, SetupContext);
            PrimaryI.ReapplyHandles();
            SecondaryI.ReapplyHandles();
            ArmorI.ReapplyHandles();

            if (self.IsAuthority) {
                self.eventCtrl.AddEventHandler(GameEvent.Item_MutateItem, MutateItem);
                self.eventCtrl.AddEventHandler(GameEvent.Entity_Damaged, DamageArmor);
            } else
                self.eventCtrl.AddEventHandler(GameEvent.Entity_SetInventorySlot, ApplySlot);
        }

        private void DamageArmor(object source, object target, object context) {
            Entity attacker = null;
            if (target is Guid id) EntityManager.TryGetEntity(id, out attacker);
            ArmorI.OnDamaged(attacker, (RefTuple<(float, float3)>)context);
        }

        private ItemContext SetupContext(ItemContext context) =>
            context.SetupScenario(self, ItemContext.Scenario.Default);

        public IInventory GetInventory(InventoryKind kind) => kind switch {
            InventoryKind.Primary => PrimaryI,
            InventoryKind.Secondary => SecondaryI,
            InventoryKind.Armor => ArmorI,
            _ => null
        };

        public bool TryGetInventoryKind(IInventory inventory, out InventoryKind kind) {
            kind = InventoryKind.Primary;
            if (inventory == PrimaryI) return true;
            kind = InventoryKind.Secondary;
            if (inventory == SecondaryI) return true;
            kind = InventoryKind.Armor;
            return inventory == ArmorI;
        }

        public void RegisterEvent(IItem item, ItemContext context, GameEvent type, Action<ItemContext, object> callback, bool authority = true) {
            if (self.IsAuthority != authority || !context.TryGetInventory(out IInventory inventory)
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
            if (!context.TryGetInventory(out IInventory inventory)
                || !TryGetInventoryKind(inventory, out InventoryKind kind)) return;
            if (itemHandlers.Remove((type, kind, context.InvId), out RefEventHandler handler))
                self.eventCtrl.RemoveEventHandler(type, handler);
        }

        public void SetDirty(InventoryKind kind, int slot) {
            if (self == null || !self.IsAuthority) return;
            IInventory inventory = GetInventory(kind);
            if (inventory == null || (uint)slot >= inventory.Capacity) return;
            self.eventCtrl.RaiseEvent(GameEvent.Entity_SetInventorySlot,
                self.info.rtEntityId, kind, (slot, inventory.PeekItem(slot)));
        }

        private void MutateItem(object source, object target, object context) {
            if (context is not int slot) return;
            if (target == PrimaryI) SetDirty(InventoryKind.Primary, slot);
            else if (target == SecondaryI) SetDirty(InventoryKind.Secondary, slot);
            else if (target == ArmorI) SetDirty(InventoryKind.Armor, slot);
        }

        private void ApplySlot(object source, object target, object context) {
            if (target is not InventoryKind kind || context is not ValueTuple<int, IItem> update) return;
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
                return;
            }

            inventory.RemoveEntry(update.Item1);
            if (incoming != null) inventory.AddEntry(incoming, update.Item1);
        }

        public int RemoveStackable(int amount, int itemIndex) {
            if (Selected?.Index == itemIndex)
                return PrimaryI.RemoveStackableSlot(SelectedIndex, amount);
            if (PrimaryI.EntryDict.ContainsKey(itemIndex))
                return PrimaryI.RemoveStackableKey(itemIndex, amount);
            return SecondaryI.RemoveStackableKey(itemIndex, amount);
        }

        public void AddItem(IItem item) {
            if (item == null) return;
            if (PrimaryI.EntryDict.ContainsKey(item.Index)) PrimaryI.AddStackable(item);
            if (item.AmountRaw == 0) return;
            SecondaryI.AddStackable(item);
            if (item.AmountRaw == 0) return;
            PrimaryI.AddStackable(item);
            if (item.AmountRaw != 0) InventoryController.DropItem(item, self.position);
        }

        private void ReceiveItem(object source, object target, object context) {
            if (self == null || context is not IItem item) return;
            AddItem(item);
        }

		private void HandleCollect(object actor, object target, object ctx) {
			if (self == null) return;
			if (!self.Is(out VitalityBehavior vitality)) return;

			int itemCount = PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count;
			if (itemCount == 0) return;

			if (ctx is not float amount || target is not Guid receiverId) return;
			if (!EntityManager.TryGetEntity(receiverId, out Entity receiver)) return;
			amount *= RecoverSpeedMultiplier;

			if (PrimaryI.EntryDict.Count > 0) receiver.eventCtrl.EnqueueEvent(
                GameEvent.Entity_RecieveItem, self.info.rtEntityId, receiverId, PrimaryI.LootInventory(amount));
			if (SecondaryI.EntryDict.Count > 0) receiver.eventCtrl.EnqueueEvent(
                GameEvent.Entity_RecieveItem, self.info.rtEntityId, receiverId, SecondaryI.LootInventory(amount));
		}
	}
}
