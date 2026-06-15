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
			self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, HandleCollect);
		}

		public override void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: PlayerInventoriesBehavior requires PlayerInventorySettings");
			if (!self.Is(out mod)) mod = null;
			this.self = self;
			self.Register(this);
			EnsureInventories();
			self.eventCtrl.AddEventHandler(GameEvent.Entity_Collect, HandleCollect);
		}

		public override void Disable(BehaviorEntity.Animal self) {
			self.eventCtrl.RemoveEventHandler(GameEvent.Entity_Collect, HandleCollect);
			this.self = null;
		}

		private void EnsureInventories() {
			PrimaryI ??= new InventoryController.Inventory(settings.PrimarySlotCount);
			SecondaryI ??= new InventoryController.Inventory(settings.SecondarySlotCount);
			ArmorI ??= new ArmorInventory();
			SelectedIndex = math.clamp(SelectedIndex, 0, math.max(settings.PrimarySlotCount - 1, 0));
		}

		private void HandleCollect(object actor, object target, object ctx) {
			if (self == null) return;
			if (!self.Is(out VitalityBehavior vitality)) return;

			int itemCount = PrimaryI.EntryDict.Count + SecondaryI.EntryDict.Count;
			if (itemCount == 0) return;

			Action<IItem> collect; float amount;
			(collect, amount) = ((Action<IItem>, float))ctx;
			amount *= RecoverSpeedMultiplier;

			if (PrimaryI.EntryDict.Count > 0) collect(PrimaryI.LootInventory(amount));
			if (SecondaryI.EntryDict.Count > 0) collect(SecondaryI.LootInventory(amount));
		}
	}
}
