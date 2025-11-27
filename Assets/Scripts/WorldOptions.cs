using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Newtonsoft.Json;
using System;
using System.Runtime.Serialization;
using Unity.Mathematics;

namespace WorldConfig {
    /// <summary>
    /// Config is the root class anchoring a settings tree which contains
    /// all settings for the game. Any trivial constants not inherent to the functionality
    /// of an algorithm should be sourcable from this tree. Configs are unique
    /// to each world and a different config object, rooting a unique settings tree,
    /// will be loaded before loading each world. 
    /// </summary>
    [CreateAssetMenu(menuName = "Generation/WorldOptions")]
    public class Config : ScriptableObject {
        private static Config _current;
        private static Config _template;
        /// <summary>
        /// A singleton instance rooting the settings tree for the currently selected world.
        /// Note, this does not guarantee that each reference type is a unique instance
        /// as is described in <see cref="Option{T}"/>. If a reference type's subtree is unchanged
        /// it is likely the same instance as the <see cref="TEMPLATE"/>. It only means that the tree 
        /// sufficiently describes all settings for the world.
        /// </summary>
        public static Config CURRENT {
            get => _current;
            set => _current = value;
        }

        /// <summary>
        /// A singleton instance rooting the template(default) settings of a world. This should not be
        /// used to acquire settings for a world, but primarily for creating new settings trees. Due to the lazy
        /// nature of the tree <seealso cref="Option{T}"/>, some portions of <see cref="CURRENT"/> may be subsets of this tree, 
        /// but it should not be assumed that the two are the same.
        /// </summary>
        public static Config TEMPLATE {
            get => _template;
            set => _template = value;
        }

        /// <summary>
        /// The seed used to create unique generation for each world. Any generation algorithm should source its
        /// noise from a function that considers this seed as a factor. The same world should be generated
        /// given the same seed. 
        /// </summary>
        public int Seed;
        /// <exclude />
        [UISetting(Alias = "Quality")]
        public Option<QualitySettings> _Quality;
        /// <exclude />
        [UISetting(Alias = "Generation")]
        public Option<GenerationSettings> _Generation;
        /// <exclude />
        [UISetting(Alias = "Gameplay")]
        public Option<GamePlaySettings> _GamePlay;
        /// <exclude />
        [UISetting(Alias = "System")]
        public Option<SystemSettings> _System;

        ///<summary> <see cref="QualitySettings"/>  </summary>
        [JsonIgnore]
        public ref QualitySettings Quality => ref _Quality.value;
        ///<summary> <see cref="GenerationSettings"/>  </summary>
        [JsonIgnore]
        public ref GenerationSettings Generation => ref _Generation.value;
        ///<summary> <see cref="GamePlaySettings"/>  </summary>
        [JsonIgnore]
        public ref GamePlaySettings GamePlay => ref _GamePlay.value;
        ///<summary> <see cref="SystemSettings"/>  </summary>
        [JsonIgnore]
        public ref SystemSettings System => ref _System.value;

        /// <summary>
        /// The settings describing factors controlling the quality of the world. Increasing quality
        /// will commonly decrease performance. Certain settings may also require expensive resources
        /// which the user may not have. Currently, it is the user's responsibility to know the limitations
        /// of their device. Quality settings cannot be changed during gameplay (once a world is loaded).
        /// </summary>
        [Serializable]
        public struct QualitySettings {
            /// <summary> See here for more information: <see cref="Quality.Atmosphere"/>  </summary>
            [UISetting(Message = "Improve Performance By Reducing Quality")]
            public Option<Quality.Atmosphere> Atmosphere;
            /// <summary> See here for more information: <see cref="Quality.LightBaker"/> </summary>
            public Option<Quality.LightBaker> Lighting;
            /// <summary> See here for more information: <see cref="Quality.Terrain"/>  </summary>
            public Option<Quality.Terrain> Terrain;
            /// <summary> See here for more information: <see cref="Quality.GeoShaderSettings"/> </summary>
            public Option<Quality.GeoShaderSettings> GeoShaders;
            /// <summary> See here for more information: <see cref="Quality.Memory"/>  </summary>
            public Option<Quality.BalancedMemory> Memory;
        }

        /// <summary>
        /// The settings describing factors controlling the generation of the world. This is exposed
        /// enabling consumer modification of the world's generation. However it is the user's responsibility
        /// to understand what each setting does and improperly modified settings may cause the world to 
        /// fail to generate or be corrupted. Generation settings cannot be changed during gameplay (once a world is loaded).
        /// </summary>
        /// <remarks> 
        /// Though generation settings may be changed at any time in the <see cref="MenuHandler"/>, modification after the world
        /// is loaded may result in corruption of the world or the world changing around the player, potentially causing the player
        /// to be trapped within the terrain. It is recommended to adjust these settings only before first loading a world.
        /// </remarks>
        [Serializable]
        public struct GenerationSettings {
            /// <summary> The registry containing settings for all noise functions used in the world's generation. 
            /// The number of noise functions available to be sample is limited to whatever is in this registry. 
            /// See <see cref="Generation.Noise"/> for more information. </summary>
            [UISetting(Message = "Controls How The World Is Generated")]
            public Catalogue<Generation.Noise> Noise;
            /// <summary> See here for more information: <see cref="Generation.Map"/> </summary>
            public Option<Generation.Map> Terrain;
            /// <summary> See here for more information: <see cref="Generation.Surface"/>  </summary>
            public Option<Generation.Surface> Surface;
            /// <summary> See here for more information: <see cref="Generation.Biome.Generation"/> </summary>
            public Option<Generation.Biome.Generation> Biomes;
            /// <summary> See here for more information: <see cref="Generation.Structure.Generation"/> </summary>
            public Option<Generation.Structure.Generation> Structures;
            /// <summary> The registry containing settings for all materials used in the world's generation. 
            /// Any material not in this registry will not be recognized by the game and usage/deserialization of it may 
            /// result in undefined behavior. See here for more information: <see cref="Generation.Material.Generation"/> </summary>
            public Option<Generation.Material.Generation> Materials;
            /// <summary> The registry containing all items referencable in any way throughout the game. Any item
            /// not in this registry will not be recognized by the game and usage/deserialization of it may 
            /// result in undefined behavior. See here for more information: <see cref="Generation.Item.Authoring"/> </summary>
            public Catalogue<Generation.Item.Authoring> Items;
            /// <summary>The registry containing all entities referencable in any way throughout the game. Any entity
            /// not in this registry will not be recognized by the game and usage/deserialization of it may 
            /// result in undefined behavior. See here for more information: <see cref="Generation.Entity.Authoring"/> </summary>
            public Catalogue<Generation.Entity.Authoring> Entities;
            /// <summary> A registry containing all textures used within the game. Similar to a texture 
            /// atlas, this registry is copied to the GPU and to be referenced by shaders. </summary>
            public Catalogue<TextureContainer> Textures;
        }

        /// <summary>
        /// The settings describing factors controlling the user's gameplay experience. Some factors
        /// may result in varying difficulty but it is up to the user to decide. These settings are <b>volatile</b>
        /// during gameplay meaning they can be changed at any time (primarily through the <see cref="PauseHandler"/>).
        /// Thus, systems referencing them should be prepared for changes at any time during runtime.
        /// </summary>
        [Serializable]
        public struct GamePlaySettings {
            /// <summary> The registry of all keybinds that are used to bind player input to actions within the game.
            /// See <see cref="Gameplay.KeyBind"/> for more information. </summary>
            [UIModifiable(CallbackName = "KeyBindReconstruct")]
            public Catalogue<Gameplay.KeyBind> Input;
            /// <summary> Controls how the player moves through the world. See <see cref="Gameplay.Movement"/> for more information. </summary>
            [UISetting(Message = "Controls How The Player Interacts With The World")]
            public Option<Gameplay.Player.Settings> Player;
            /// <summary> Controls how the player experiences the world. See <see cref="Gameplay.Interaction"/> for more information. </summary>
            public Option<Gameplay.Gamemodes> Gamemodes;
            /// <summary> Controls the players inventory. See <see cref="Gameplay.Inventory"/> for more information. </summary>
            public Option<Gameplay.Inventory> Inventory;
            /// <summary> Settings controlling environment constants of the world. See <see cref="Gameplay.Environment"/> for more information. </summary>
            public Option<Gameplay.Environment> Time;
            /// <summary> Settings controlling the optional visual statistics displayed to the player. See <see cref="Gameplay.Statistics"/> for more information. </summary>
            public Option<Gameplay.Statistics> Statistics;
        }

        /// <summary>
        /// Settings imperative to the function of the game. These are hidden from the user by default as they are
        /// somewhat inherent to the game's function. However, this exists to organize advanced settings 
        /// future for advanced users and developers to modify. 
        /// </summary>
        [Serializable]
        public struct SystemSettings {
            /// <summary> Controls how the player can create items. See <see cref="Gameplay.Crafting"/> for more information. </summary>
            public Option<Intrinsic.Crafting> Crafting;
            /// <summary> Controls how the player's armor is displayed. See <see cref="Gameplay.Armor"/> for more information. </summary>
            public Option<Intrinsic.Armor> Armor;
            /// <summary> Controls how the terrain is updated. See <see cref="Intrinsic.TerrainUpdation"/> for more information. </summary>
            public Option<Intrinsic.TerrainUpdation> TerrainUpdation;
            /// <summary> Controls how the world looks in the main menu. See <see cref="Intrinsic.WorldApperance"/> for mor information. </summary>
            public Option<Intrinsic.WorldApperance> WorldApperance;

            /// <summary> The settings for the readback system. See <see cref="Intrinsic.Readback"/> for more information. </summary>
            [UISetting(Ignore = true)]
            public Option<Intrinsic.Readback> ReadBack;
            /// <summary>
            /// Registry for hooks that are called when a certain setting is modified. This registry is generated
            /// during runtime and is not customizable by the user. If a certain member has a UIModifiable attribute
            /// defined, the gameplay menu will trigger a hook associated with that name if it or any sub-member is changed.
            /// </summary>
            [NonSerialized]
            [HideInInspector]
            [JsonIgnore]
            [UISetting(Ignore = true, Defaulting = true)]
            public Registry<ChildUpdate> GameplayModifyHooks;
        }

        [OnDeserialized]
        internal void OnDeserialized(StreamingContext context = default) {
            object defaultOptions = TEMPLATE;
            object thisRef = this;
            SegmentedUIEditor.SupplementTree(ref thisRef, ref defaultOptions);
        }

        /// <summary>
        /// Creates a new instance of the <see cref="Config"/> object by instantiating
        /// the <see cref="TEMPLATE"/> and setting the seed to a random value.
        /// </summary> <returns>The newly created<see cref="Config"/> object.</returns>
        public static Config Create() {
            Config newOptions = Instantiate(TEMPLATE);
            newOptions.Seed = UnityEngine.Random.Range(int.MinValue, int.MaxValue);
            return newOptions;
        }
    }

    /// <summary>
    /// The interface for an Option template. Specifies
    /// basic functionality necessary for modification and storage
    /// of <see cref="Option{T}"/> objects within the <see cref="Config"/> tree.
    /// </summary>
    public interface IOption {
        /// <summary> Returns whether the object has been modified. An option
        /// is dirty if its subtree is not the same as the template.  </summary>
        bool IsDirty { get; }
        /// <summary> Clones the object. If the object is a reference type, it will be cloned.
        /// Value types are clones by default. THis also sets the object as dirty. </summary>
        void Clone();
    }

    /// <summary>
    /// A wrapper representing a logical node within the <see cref="Config"/> tree. An option consistutes
    /// the atomic unit within our lazy storage system, meaning that if an option is dirty only the immediate
    /// subtree up until another option is encountered will be stored. By the same token, a higher density of
    /// options within the tree will result in finer granularity of what can be lazily ignored and vice versa. 
    /// </summary>
    /// <remarks> 
    /// Upon modification, all options along the path to the root will be cloned and marked as dirty before any setting
    /// is modified. When this happens, only options along this path will be different from the template while the 
    /// rest of the tree will point to subsections of the template. 
    /// 
    /// Consequently, there are several rules one must adhere to when building or adding onto the <see cref="Config"/> tree.
    /// <list type="bullet">
    /// <item> 
    ///     <term><b>Non-Option containers can hold value types and options only.</b></term> 
    ///     <description> 
    ///     If a non-option type holds a reference type when the non-option type is cloned (a shallow copy),
    ///     the reference type will not be cloned. The system is not capable of identifying this and thus this
    ///     will result in the reference type being shared between the two instances.
    ///     </description>
    ///  </item>
    /// <item> 
    ///     <term> <b>Options can hold class or value types only.</b></term> 
    ///     <description> 
    ///      Options are the logical unit within the tree and are capable of holding any type 
    ///      as it tracks whether or not it is a clone and how to clone it. 
    ///     </description>
    /// </item>
    /// <item> 
    ///     <term> <b>Primitives are treated the same as value types.</b></term>
    ///     <description>
    ///     Primitives include all <see href="https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/builtin-types/built-in-types">
    ///     C# built-in types</see>, strings, and enums.
    ///     </description>
    /// </item>
    /// </list>
    /// </remarks>
    /// <typeparam name="T"> The type of the object to be stored within the option.  </typeparam>
    [Serializable]
    public struct Option<T> : IOption {
        /// <summary> The instance stored within the option wrapper. </summary>
        [SerializeField]
        public T value;
        [HideInInspector]
        [UISetting(Ignore = true)]
        private bool isDirty;

        /// <summary> Implicitly converts the option to the value it holds. This is useful for obtaining the value</summary>
        /// <param name="option"> The option itself </param>
        public static implicit operator T(Option<T> option) => option.value;
        /// <summary> Implicitly converts the value to an option. This is useful for setting the value  </summary>
        /// <param name="val">The value which we want to set to <see cref="value"/></param>
        public static implicit operator Option<T>(T val) => new Option<T> { value = val };

        /// <summary>
        /// Returns whether one should serialize(save) this field (i.e. the type it holds).
        /// An option should be serialized if it <see cref="isDirty"/>. 
        /// Used by <see cref="Newtonsoft.Json"/> to determine if the field needs to be saved.
        /// </summary>
        /// <returns></returns>
        public bool ShouldSerializevalue() { return isDirty; }
        //Default value is false so it's the same if we don't store it

        /// <summary> Whether or not the option has been modified (i.e. different from the template) </summary>
        public bool IsDirty {
            readonly get { return isDirty; }
            set { isDirty = value; }
        }

        /// <summary>
        /// Clones the object. If it is a value type, it will be cloned by default. 
        /// If it is a reference type, it will be cloned if it implements <see cref="ICloneable"/>,
        /// ILists are cloned by default by creating a new instance of the list and copying the elements.
        /// </summary>
        public void Clone() {
            if (isDirty) return;
            isDirty = true;

            if (value is UnityEngine.Object)
                value = (T)(object)UnityEngine.Object.Instantiate((UnityEngine.Object)(object)value);
            else if (value is ICloneable cloneable)
                value = (T)cloneable.Clone();
            else if (value is IList list) {
                value = (T)Activator.CreateInstance(list.GetType(), list);
            }
        }
    }

    /// <summary> Similar to <see cref="Option{T}"/> except that it is serialized by reference
    /// instead of value by Unity(into a .asset file). This may be a requirement for
    /// serializing abstract types/interfaces to Unity's inspector. 
    /// <see cref="SerializeReference"/> for more information. </summary>
    /// <typeparam name="T"></typeparam>
    [Serializable]
    public struct ReferenceOption<T> : IOption where T : class {
        /// <summary> The instance stored within the option wrapper. </summary>
        [SerializeReference]
        public T value;
        [HideInInspector]
        [UISetting(Ignore = true)]
        private bool isDirty;

        /// <summary> Implicitly converts the option to the value it holds. This is useful for obtaining the value</summary>
        /// <param name="option"> The option itself </param>
        public static implicit operator T(ReferenceOption<T> option) => option.value;
        /// <summary> Implicitly converts the value to an option. This is useful for setting the value  </summary>
        /// <param name="val">The value which we want to set to <see cref="value"/></param>
        public static implicit operator ReferenceOption<T>(T val) => new ReferenceOption<T> { value = val };

        /// <summary>
        /// Returns whether one should serialize(save) this field (i.e. the type it holds).
        /// An option should be serialized if it <see cref="isDirty"/>. 
        /// Used by <see cref="Newtonsoft.Json"/> to determine if the field needs to be saved.
        /// </summary>
        /// <returns></returns>
        public bool ShouldSerializevalue() { return isDirty; }
        //Default value is false so it's the same if we don't store it

        /// <summary> Whether or not the option has been modified (i.e. different from the template) </summary>
        public bool IsDirty {
            readonly get { return isDirty; }
            set { isDirty = value; }
        }

        /// <summary>
        /// Clones the object. If it is a value type, it will be cloned by default. 
        /// If it is a reference type, it will be cloned if it implements <see cref="ICloneable"/>,
        /// ILists are cloned by default by creating a new instance of the list and copying the elements.
        /// </summary>
        public void Clone() {
            if (isDirty) return;
            isDirty = true;

            if (value is UnityEngine.Object)
                value = (T)(object)UnityEngine.Object.Instantiate((UnityEngine.Object)(object)value);
            else if (value is ICloneable cloneable)
                value = (T)cloneable.Clone();
            else if (value is IList list) {
                value = (T)Activator.CreateInstance(list.GetType(), list);
            }
        }
    }
}



