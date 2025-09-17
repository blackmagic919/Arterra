using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEngine;
using WorldConfig;

namespace WorldConfig.Generation.Item {
    /// <summary> A template for creating an item. To create an inspector serializable object, the 
    /// concrete type must be known. This allows us to quickly fulfill the contract with
    /// the <see cref="IItem"/>  interface. </summary>
    /// <typeparam name="TItem"></typeparam>
    public class AuthoringTemplate<TItem> : Authoring where TItem : IItem, new() {
        /// <summary> Returns a new instance of the concrete type implementing <see cref="IItem"/>, fulfilling the contract. </summary>
        public override IItem Item => new TItem();
    }

    /// <summary>
    /// Information shared by all instances of an item containing static properties
    /// that describe the apperance of the item as well as its connection to the 
    /// <see cref="WorldConfig.Config.GenerationSettings.Materials"> material registry </see>. 
    /// </summary>
    public class Authoring : Category<Authoring> {
        /// <summary> The name of the entry within the <see cref="WorldConfig.Config.GenerationSettings.Textures"> texture registry </see>
        /// of the texture that is displayed when the item is in a UI panel. It is also used to create an <see cref="EItem"> entity item</see> mesh
        /// if the item is dropped in the world. This must always be a valid entry. </summary>
        [RegistryReference("Textures")]
        public string TextureName;
        /// <summary>
        /// The item instance that stores information specific to a specific instance of the item. The instance
        /// should store the index within <see cref="WorldConfig.Config.GenerationSettings.Items"/> of the entry it 
        /// is created from to retrieve the item's shared information contained within its <see cref="Authoring"/> object.
        /// </summary>
        public virtual IItem Item { get; }
    }

    /// <summary>
    /// A contract for an item instance that is created from an <see cref="Authoring"/> object. 
    /// An item can be anything and define however much data it needs to store and how to manage it.
    /// However, for it to be properly managed by the system, it must detail several properties
    /// related to its apperance, storage, and serialization. </summary> <remarks> 
    /// The contract also provides some hooks that the item can subscribe to which will be answered 
    /// when specific events occur for the item. There is <i>almost</i> no limit on what can't be
    /// done on a hooked event, such as reassigning keybinds, moving items, changing player effects, etc. 
    /// As long as the event itself is safe, the item can do whatever it wants allowing for a very
    /// flexible system. </remarks>
    public interface IItem : ICloneable, IRegistered, ISlot {
        /// <summary> Whether the item can be stacked with other items of the same type. If the item is stackable,
        /// when another item of the same <see cref="IRegistered.Index"/> is encountered it may be combined with the
        /// current item and the <see cref="AmountRaw"/> increased to the sum the amounts of the two items.
        /// All items representing materials should be stackable by default. </summary>
        public bool IsStackable { get; }
        /// <summary> The amount of the item that is stored. Used when determing how to stack identical items </summary>
        public int AmountRaw { get; set; }
        /// <summary>The maximum amount of item that all systems can stably support.</summary>
        public const int MaxAmountRaw = 0xFFFF;
        /// <summary> The index within the <see cref="Config.GenerationSettings.Textures"> texture registry </see> of the item's texture.
        /// This is obtained by using the <see cref="IRegistered.Index"/> within the <see cref="WorldConfig.Config.GenerationSettings.Items"> item registry </see>
        /// to obtain the <see cref="Authoring.TextureName"/> of the texture which can be used to find the texture in the external
        /// <see cref="Config.GenerationSettings.Textures"> texture registry </see>. See <seealso cref="Authoring.TextureName"/> for
        /// more information. </summary>
        public int TexIndex { get; }

        /// <summary> The constructor function called whenever an item is created.
        /// This should be used to setup Index and AmountRaw. </summary>
        /// <param name="Index">The index of the item within <see cref="Config.GenerationSettings.Items"/> registry</param>
        /// <param name="AmountRaw">The amount of the item</param>
        public void Create(int Index, int AmountRaw) { }

        /// <summary>
        /// An event hook that is called on the frame when the item enters the <see cref="InventoryController.Secondary"> Secondary </see> inventory.
        /// This is an airtight state, meaning <see cref="OnLeaveSecondary"/> must be called before <see cref="OnEnterSecondary"/> can be called 
        /// a second time.
        /// </summary>
        public void OnEnterSecondary(); //Called upon entering the secondary inventory
        /// <summary>
        /// An event hook that is called on the frame when the item leaves the <see cref="InventoryController.Secondary"> Secondary </see> inventory.
        /// This is an airtight state meaning <see cref="OnEnterSecondary"/> must be called before <see cref="OnLeaveSecondary"/> can be called.
        /// </summary>
        public void OnLeaveSecondary();//Called upon leaving the secondary inventory
        /// <summary>
        /// An event hook that is called on the frame when the item enters the <see cref="InventoryController.Primary"> Primary </see> inventory.
        /// This is an airtight state, meaning <see cref="OnLeavePrimary"/> must be called before <see cref="OnEnterPrimary"/> can be called
        /// a second time.
        /// </summary>
        public void OnEnterPrimary(); //Called upon entering the primary inventory
        /// <summary>
        /// An event hook that is called on the frame when the item leaves the <see cref="InventoryController.Primary"> Primary </see> inventory.
        /// This is an airtight state meaning <see cref="OnEnterPrimary"/> must be called before <see cref="OnLeavePrimary"/> can be called.
        /// </summary>
        public void OnLeavePrimary(); //Called upon leaaving the primary inventory
        /// <summary>
        /// An event hook that is called on the frame when the item is held within the <see cref="InventoryController.Selected"> selected slot </see> of the 
        /// <see cref="InventoryController.Primary">Primary</see> inventory. This is an airtight state, meaning <see cref="OnDeselect"/> must be
        /// called before <see cref="OnSelect"/> can be called a second time. Furthermore, it is an exclusive state meaning no other item
        /// can be selected while this item is selected.
        /// </summary>
        public void OnSelect(); //Called upon becoming the selected item
        /// <summary>
        /// An event hook that is called on the frame when the item is no longer held within the <see cref="InventoryController.Selected"> selected slot </see> of the
        /// <see cref="InventoryController.Primary">Primary</see> inventory. This is an airtight state, meaning <see cref="OnSelect"/> must be called
        /// before <see cref="OnDeselect"/> can be called.
        /// </summary>
        public void OnDeselect(); //Called upon no longer being the selectd item
        /// <summary> An event hook that is called every frame the item is held by an <see cref="EItem">EntityItem</see>. </summary>
        /// <remarks> TODO: This has yet to be implemented </remarks>
        public void UpdateEItem(); //Called every frame it is an Entity Item
    }

    /// <summary> An interface implemented by items that have a direct translation to material 
    /// representation and thus are placeable in the world. </summary>
    public class PlaceableItem : Authoring {
        /// <summary>
        /// The name of the entry within the <see cref="WorldConfig.Config.GenerationSettings.Materials"> material registry </see>
        /// of the material that is placed when the item is selected when placing terrain.
        /// If the material name is not a valid entry, the item will not be able to be placed as a material (e.g. a tool).
        /// </summary>
        [RegistryReference("Materials")]
        public string MaterialName;
        /// <summary>
        /// If the material is <see cref="MaterialName">placable as a material</see>, whether it should place the material indicated
        /// by <see cref="MaterialName"/> in a solid or liquid state. If it is not placable this value is ignored. 
        /// </summary>
        public State MaterialState;
        /// <summary> If the item is <see cref="MaterialName">placable as a material</see>, whether <see cref="MaterialState"/> is solid. </summary>
        public bool IsSolid => MaterialState == State.Solid;
        /// <summary> If the item is <see cref="MaterialName">placable as a material</see>, whether <see cref="MaterialState"/> is liquid. </summary>
        public bool IsLiquid => MaterialState == State.Liquid;

        /// <summary>
        /// The states a material can be placed as from an item. 
        /// This is either a solid or liquid.
        /// </summary>
        public enum State {
            /// <summary> The material is placed as a solid. </summary>
            Solid = 0,
            /// <summary> The material is placed as a liquid. </summary>
            Liquid = 1,
        }
    }

    /// <summary> A template for creating an item. To create an inspector serializable object, the 
    /// concrete type must be known. This allows us to quickly fulfill the contract with
    /// the <see cref="IItem"/>  interface. </summary>
    /// <typeparam name="TItem"></typeparam>
    public class PlaceableTemplate<TItem> : PlaceableItem where TItem : IItem, new() {
        /// <summary> Returns a new instance of the concrete type implementing <see cref="IItem"/>, fulfilling the contract. </summary>
        public override IItem Item => new TItem();
    }
}