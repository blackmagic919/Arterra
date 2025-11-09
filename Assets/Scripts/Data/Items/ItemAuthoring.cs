using System;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using UnityEngine;
using WorldConfig;
using WorldConfig.Generation.Material;

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
        /// <summary>An(optional) description of the item 
        /// used as helpful tooltips for the player. </summary>
        public string Description;
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
        /// <summary> How much of the item can be combined together before the item is considered full. If 
        /// an item is not full, when another item of the same <see cref="IRegistered.Index"/> is encountered it may 
        /// be combined with the current item and the <see cref="AmountRaw"/> increased to the sum the amounts of the two items. </summary>
        public int StackLimit { get => 1; }
        /// <summary> The <see cref="AmountRaw">raw amount</see> of an item that is considered a unit
        /// amount. For exmaple, if five units of an item is added to an item, <see cref="AmountRaw"/>
        /// will increase by (5 * UnitSize).  </summary>
        public int UnitSize { get => 1; }
        /// <summary> The amount of the item that is stored. Used when determing how to stack identical items </summary>
        public int AmountRaw { get; set; }
        /// <summary>The maximum amount of item that all systems can stably support.</summary>
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

        /// <summary>  An event hook that is called on the frame when the item enters the situation context given by <paramref name="cxt"/>.
        /// This is an airtight state, meaning <see cref="OnLeave"/> must be called before <see cref="OnEnter"/> can be called
        /// a second time. </summary>
        /// <param name="cxt">The context in which the item is being entered.</param>
        public void OnEnter(ItemContext cxt); //Called upon entering the primary inventory
        /// <summary> An event hook that is called on the frame when the item leaves the  situation context given by <paramref name="cxt"/>.
        /// This is an airtight state meaning <see cref="OnEnter"/> must be called before <see cref="OnLeave"/> can be called. </summary>
        /// <param name="cxt">The context in which the item is being entered.</param>/// 
        public void OnLeave(ItemContext cxt); //Called upon leaaving the primary inventory
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