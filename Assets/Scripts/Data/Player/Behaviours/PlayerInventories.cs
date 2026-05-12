using System;
using System.Collections.Generic;
using Arterra.Configuration;
using Arterra.Data.Item;
using Arterra.GamePlay.UI;
using Newtonsoft.Json;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Entity.Behavior {
	/// <summary> Settings controlling the size and apperance of the inventory,
    /// a system allowing the player to hold and use 
    /// <see cref="Generation.Item"> items </see>. </summary>
    [Serializable]
    public class PlayerInventorySettings : IBehaviorSetting {
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
	public class PlayerInventoriesBehavior : IBehavior {
		[JsonIgnore] public static PlayerInventoriesBehavior Active { get; private set; }
		[JsonIgnore] public PlayerInventorySettings settings;

		public InventoryController.Inventory PrimaryI;
		public InventoryController.Inventory SecondaryI;
		public ArmorInventory ArmorI;
		public int SelectedIndex;

		private BehaviorEntity.Animal self;

		[JsonIgnore]
		public IItem Selected {
			get {
				if (PrimaryI == null || PrimaryI.Info == null) return null;
				if (SelectedIndex < 0 || SelectedIndex >= PrimaryI.Info.Length) return null;
				return PrimaryI.Info[SelectedIndex];
			}
		}

		public void AddSettingsDependencies(Dictionary<Type, IBehaviorSetting> hierarchy) {
			hierarchy.TryAdd(typeof(PlayerInventorySettings), new PlayerInventorySettings());
		}

		public void Initialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, float3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: PlayerInventoriesBehavior requires PlayerInventorySettings");

			this.self = self;
			Active = this;
			self.Register(this);
			EnsureInventories();
		}

		public void Deserialize(BehaviorEntity.Animal self, BehaviorEntity.AnimalSetting setting, ref int3 GCoord) {
			if (!setting.Is(out settings))
				throw new Exception("Entity: PlayerInventoriesBehavior requires PlayerInventorySettings");

			this.self = self;
			Active = this;
			self.Register(this);
			EnsureInventories();
		}

		public void Disable(BehaviorEntity.Animal self) {
			if (ReferenceEquals(Active, this)) {
				Active = null;
			}

			if (ReferenceEquals(this.self, self)) {
				this.self = null;
			}
		}

		private void EnsureInventories() {
			PrimaryI ??= new InventoryController.Inventory(settings.PrimarySlotCount);
			SecondaryI ??= new InventoryController.Inventory(settings.SecondarySlotCount);
			ArmorI ??= new ArmorInventory();
			SelectedIndex = math.clamp(SelectedIndex, 0, math.max(settings.PrimarySlotCount - 1, 0));
		}
	}
}
