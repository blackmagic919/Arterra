using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Generic;
using UnityEditor;
using MapStorage;


namespace WorldConfig.Generation.Material
{
    /// <summary> All settings related to the apperance and interaction of 
    /// materials in the game world. Different materials are allowed
    /// to define their own subclass of <see cref="MaterialData"/> to define 
    /// different interaction behaviors. </summary>
    public abstract class MaterialData : Category<MaterialData>
    {
        /// <summary>
        /// The registry names of all entries referencing registries within <see cref="MaterialData"/>. When an element needs to 
        /// reference an entry in an external registry, they can indicate the index within this list of the name of the entry 
        /// within the registry that they are referencing. This allows for the material module to be decoupled from the rest 
        /// of the world's configuration. 
        /// </summary>
        public List<string> Names;

        /// <summary> Settings controlling the appearance of the terrain when the material is solid. See <see cref="TerrainData"/> for more info. </summary>
        public TerrainData terrainData;
        /// <summary> Settings controlling the apperance of the terrain when the material is atmospheric. See <see cref="AtmosphericData"/> for more info. </summary>
        public AtmosphericData AtmosphereScatter;
        /// <summary> Settings controlling the appearance of the terrain when the material is liquid. See <see cref="LiquidData"/> for more info. </summary>
        public LiquidData liquidData;

        /// <summary>
        /// Called whenever a map entry of this material has been modified. This method can be
        /// overrided to provide specific behavior when a certain material has been modified. See
        /// <see cref="TerrainGeneration.TerrainUpdate"/> for more information.
        /// </summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that has been updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update. </param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public abstract void PropogateMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default);
        /// <summary> Called whenever a map entry of this material has been randomly updated. Random updates are used to
        /// simulate the natural changes of the material over time. They cannot be added externally and
        /// are sampled at random from an internal system. See <see cref="TerrainGeneration.TerrainUpdate"/> 
        /// for more information </summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that has been updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update.</param>
        /// <param name="prng">Optional per-thread pseudo-random seed, to use for randomized behaviors</param>
        public abstract void RandomMaterialUpdate(int3 GCoord, Unity.Mathematics.Random prng = default);

        /// <summary> Called whenever the player is about to remove the terrain; called for all the materials
        /// that will be removed by the player if not prevented. This handle is answered before any part of the
        /// terrain is actually updated and can be used to trigger material-specific behaviors. </summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that will be updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update.</param>
        /// <param name="caller"> The entity responsible for removing the terrain. If the terrain is not being removed 
        /// by an entity this will be null </param>
        /// <returns>Whether or not to prevent the user from modifying the terrain. Also stops answering all
        /// calls to <see cref="OnRemoving"/> after this point. </returns>
        public virtual bool OnRemoving(int3 GCoord, Entity.Entity caller) {
            return false; //Don't block by default
        }

        /// <summary> Called whenever the player is about to place the terrain; called for all the materials
        /// that will be placed by the player if not prevented. This handle is answered before any part of the
        /// terrain is actually updated and can be used to trigger material-specific behaviors. </summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that will be updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update.</param>
        /// <param name="caller"> The entity responsible for removing the terrain. If the terrain is not being removed 
        /// by an entity this will be null </param>
        /// <returns>Whether or not to prevent the user from modifying the terrain. Also stops answering all
        /// calls to <see cref="OnPlacing"/> after this point. </returns>
        public virtual bool OnPlacing(int3 GCoord, Entity.Entity caller) {
            return false; //Don't block by default
        }
        
        /// <summary> Called whenever the player is during the process of removing the terrain; called for all the materials
        /// that will be removed by the player. Importantly, this is answered <b>after</b> the material has been removed
        /// from the terrain. Also indicates what item should be given in return for removing this amount of this material </summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that has been updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update.</param>
        /// <param name="amount">The amount of material that was removed from the terrain</param>
        /// <returns> The item to be given to the caller through this action </returns>
        public virtual Item.IItem OnRemoved(int3 GCoord, in MapData amount) { return null;  }

        /// <summary> Called whenever the player is during the process of placing the terrain; called for all the materials
        /// that are placed by the player. Importantly, this is answered for the new material states that are placed; if a previous 
        /// material(e.g. air) is replaced by a new material(e.g. dirt), this handle is answered only for the new material(dirt) 
        /// while <see cref="OnPlacing"/> will be answered only for the old material(air)</summary>
        /// <param name="GCoord">The coordinate in grid space of the entry that has been updated. It is guaranteed
        /// that the map entry at GCoord will be of the same material as the instance that recieves the update.</param>
        /// <param name="amount">The amount of material that was added to the terrain</param>
        public virtual void OnPlaced(int3 GCoord, in MapData amount){}

        
        /// <summary> Called whenever an entity touches the solid form of this material.
        /// Specifically, when an entity's collider overlaps a point that <see cref="MapData.IsSolid"/>
        /// and this material is the main contributor to that point's density </summary>
        /// <param name="entity">The entity that is touching the solid ground</param>
        public virtual void OnEntityTouchSolid(Entity.Entity entity) { }

        /// <summary> Called whenever an entity touches the liquid form of this material.
        /// Specifically, when an entity's collider overlaps a point that <see cref="MapData.IsLiquid"/>
        /// and this material is the main contributor to that point's density </summary>
        /// <param name="entity">The entity that is touching the solid ground</param>
        public virtual void OnEntityTouchLiquid(Entity.Entity entity) { }

        /// <summary> A static utility function to swap a mapData's material with another material
        /// handling all the necessary handler calls to <see cref="OnPlacing"/>, <see cref="OnRemoved"/>,
        /// etc., that this requires. </summary>
        /// <param name="GCoord">The coordinate in grid space of the mapEntry whose material is being swapped</param>
        /// <param name="newMaterial">The new material that is being put at this location</param>
        /// <param name="ReplacedItem"> The item produced as a result of removing the original material at this location 
        /// given for the caller to decide how to handle </param>
        /// <param name="caller">The caller who is making this request; given to material handles </param>
        /// <returns>Whether or not the material was successfully swapped.</returns>
        public static bool SwapMaterial(int3 GCoord, int newMaterial, out Item.IItem ReplacedItem, Entity.Entity caller = null) {
            MapData mapData = CPUMapManager.SampleMap(GCoord);
            ReplacedItem = null;

            if (mapData.IsNull) return false;
            var MatInfo = Config.CURRENT.Generation.Materials.value.MaterialDictionary;
            if (MatInfo.Retrieve(mapData.material).OnRemoving(GCoord, caller))
                return false;
            ReplacedItem = MatInfo.Retrieve(mapData.material).OnRemoved(GCoord, mapData);
            if (MatInfo.Retrieve(mapData.material).OnPlacing(GCoord, caller)) {
                CPUMapManager.SetMap(new MapData { material = mapData.material }, GCoord);
                return false;
            }
            mapData.material = newMaterial;
            MatInfo.Retrieve(mapData.material).OnPlaced(GCoord, mapData);
            CPUMapManager.SetMap(mapData, GCoord);
            return true;
        }

        private static Catalogue<Item.Authoring> ItemInfo => Config.CURRENT.Generation.Items;

        /// <summary>
        /// The apperance of the terrain when the material <see cref="MapData.IsSolid">is solid</see>. 
        /// When a material is solid, it will be under or adjacent to a mesh. If it is adjacent, the mesh will display the 
        /// material of the closest solid map entry with the apperance defined below.
        /// </summary>
        [System.Serializable]
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct TerrainData {
            /// <summary>
            /// The index within the <see cref="Names"> Name Registry </see> of the name within the external <see cref="Config.GenerationSettings.Textures"/> registry,
            /// of the texture that is displayed when a mesh primitive is rendered with this material. This index must always refer to a valid texture if it can be solid.
            /// </summary>
            public int Texture;
            /// <summary>
            /// The scale of the texture when it is drawn to the terrain. The scale difference between world space and 
            /// the UV space of the texture when it is being sampled. A larger value will result in a larger texture on the terrain.
            /// </summary>
            public float textureScale;
            /// <summary> The information describing what type of <see cref="Quality.GeoShader"/> to render the current 
            /// material with if it is responsible for creating a mesh triangle. See <see cref="GeoShaderInfo"/> for more info.</summary>
            public GeoShaderInfo GeoShaderIndex;

            /// <summary> Information about how the material should be handled by <see cref="Quality.GeoShader">Geoshaders</see> which
            /// are responsible for an assortment of mildly performance intensive mesh-based visual effects. </summary>
            [Serializable]
            public struct GeoShaderInfo {
                /// <summary> The raw data described by these settings. This is the raw 
                /// compacted information recieved by the GPU </summary>
                [HideInInspector]
                public uint data;
                /// <summary> The index within the <see cref="Names"> Name Registry </see> of the name within the external <see cref="Config.QualitySettings.GeoShaders"/> registry,
                /// of the geometry shader that is generated ontop of the mesh when it is solid. If the index is -1, no extra geoshader will be
                /// generated for this material. </summary>
                public uint MajorIndex {
                    get => (data >> 16) & 0x7FFF;
                    set => data = (value & 0x7FFF) << 16 | (data & 0xFFFF);
                }
                /// <summary> The index within the specific registry at the <see cref="MajorIndex"/> within the <see cref="Config.QualitySettings.GeoShaders"/> registry
                /// containing variant settings describing how to generate/render the geoshader. Simply put, materials with the 
                /// same <see cref="MajorIndex"/> utilize the same source logic during rendering/generation, but may possess 
                /// unique settings effecting each process. </summary>
                public uint MinorIndex {
                    get => data & 0xFFFF;
                    set => data = (value & 0xFFFF) | (data & 0xFFFF0000);
                }

                /// <summary> Whether or not the material possesses a geoshader and is processed by the geoshader system.
                /// If a material does not possess a geoshader, it is filtered out at an early stage 
                /// and does not tax the geoshader/rendering system.  </summary>
                public bool HasGeoShader {
                    get => (data & 0x80000000) != 0;
                    set => data = (value ? 0x80000000 : 0) | (data & 0x7FFFFFFF);
                }
            }
        }

        /// <summary>
        /// The apperance of the terrain when the material <see cref="MapData.IsGaseous">is gaseous</see>.
        /// A gaseous material will be rendered by the <see cref="AtmosphereBake">atmosphere </see> post process and must describe
        /// its optical interactions since light is permitted to pass through it. See <see href="https://blackmagic919.github.io/AboutMe/2024/09/07/Atmospheric-Scattering/">
        /// here </see> for more information.
        /// </summary>
        [System.Serializable]
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct AtmosphericData
        {
            /// <summary>
            /// How much this gaseous material reflects light traveling through it towards the viewer. The channels, ordered rgb, and each describe how 
            /// much of that wavelength is reflected when light encounters the material. As light travels through the material,
            /// lower wavelengths will be more prominent with a thinner optical density (the amount of atmosphere the light travels through)
            /// while higher wavelengths will be more prominent with a thicker optical density.
            /// </summary> <remarks>as a percentage, between 0 and 1</remarks>
            public Vector3 InScatterCoeffs;
            /// <summary>
            /// How much this gaseous material reflects light traveling through it away from the viewer. The channels, ordered rgb, and each describe how 
            /// much of that wavelength is reflected when light encounters the material.
            /// </summary> <remarks>as a percentage, between 0 and 1</remarks>
            public Vector3 OutScatterCoeffs;
            /// <summary>
            /// When light travels from a surface fragment to the camera, how quickly the light is scattered away. Deescribes 
            /// how much of that wavelength is reflected away from the camera as light travels through it. A higher ground extinction
            /// will result in lower visibility of distant objects. 
            /// </summary> <remarks>as a percentage, between 0 and 1</remarks>
            public Vector3 GroundExtinction;
            /// <summary>
            /// Controls how much light is emitted from this block as both a solid and liquid.
            /// Divided into 2 15 bit sections; 0x7FFF controls light emitted as a solid; 0x7FFF0000
            /// controls the light emitted as a liquid and gas. Each 15 bit section is divided into 3 
            /// 5 bit regions, each controlling on a scale from 0->31 the intensity of the RGB channels.
            /// </summary>
            public uint LightIntensity;
        }

        /// <summary>  The apperance of the terrain when the material <see cref="MapData.IsLiquid">is liquid</see>.
        /// A liquid material is under or adjacent to a seperate liquid mesh that displays the surface of the liquid terrain.
        /// If it is adjacent, the mesh will display the material of the closest liquid map entry with the apperance defined below.
        /// </summary> <remarks>If the liquid mesh borders the solid mesh, the liquid mesh will adopt the solid mesh's vertices</remarks>
        [System.Serializable]
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        public struct LiquidData
        {
            /// <summary> The color, ordered rgb, of the liquid as the view depth through it approaches zero. That is,
            /// as the depth of the liquid approaches zero relative to the viewer's perspective. </summary>
            public Vector3 shallowCol;
            /// <summary> The color, ordered rgb, of the liquid as the view depth through it approaches infinity. That is,
            /// as the depth of the liquid approaches infinity relative to the viewer's perspective. </summary>
            public Vector3 deepCol;
            /// <summary> How quickly does the color transition from <see cref="shallowCol"/> to <see cref="deepCol"/> as the
            /// depth increases. </summary>
            [Range(0, 1)]
            public float colFalloff;
            /// <summary>  How quickly the liquid's surface transitions from being transparent to opaque as the view depth through 
            /// it approaches infinity. </summary>
            [Range(0, 1)]
            public float depthOpacity;
            /// <summary> How smooth are waves on the liquid's surface. A higher value will result in smoother waves while a lower value will result
            /// in more constrasted, sharp waves. </summary>
            [Range(0, 1)]
            public float smoothness;
            /// <summary> How large/coarse are the waves. Specifically, how much the waves are blended between a <see cref="Generation.liquidCoarseWave">coarse</see> wave
            /// map and a <see cref="Generation.liquidFineWave">fine</see> wave map. </summary>
            [Range(0, 1)]
            public float waveBlend;
            /// <summary> How noticeable are waves on the liquid's surface. </summary>
            [Range(0, 1)]
            public float waveStrength;
            /// <summary> The scale of each wave map when they are sampled.  The x component describes the scale of <see cref="Generation.liquidCoarseWave"/> while the y component
            /// describes the scale of <see cref="Generation.liquidFineWave"/>. A smaller scale will result in larger features (zooming in) while a larger scale will result 
            /// in smaller features (zooming out). </summary>
            public Vector2 waveScale;
            /// <summary>
            /// The speed at which the waves move across the liquid's surface. The x component describes the speed of the <see cref="Generation.liquidCoarseWave"/> while the y component
            /// describes the speed of the <see cref="Generation.liquidFineWave"/>. A higher speed will result in faster waves of that respective wave map.
            /// </summary>
            public Vector2 waveSpeed;
        }
        
        /// <summary> An optional handler for most materials that handles how items
            /// are given in return for removing a specified amount of material
            /// depending on the state of the material removed. </summary>
            [Serializable]
            public struct ItemLooter {
                /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
                /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is solid. 
                /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
                public int SolidItem;
                /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
                /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is liquid. 
                /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
                public int LiquidItem;

                /// <summary>  Creates a generic item from map information of what has been removed. 
                /// This is the standard logic for item acquirement for most materials. Returns a 
                /// solid item if <see cref="MapData.SolidDensity"/> is nonzero, a liquid material otherwise,
                /// or null if a suitable item cannot be found. </summary>
                /// <param name="mapData">The map data indicating the amount of material removed
                /// and the state it was removed as</param>
                /// <param name="Names">The <see cref="Names"> name registry </see> specific to the material
                /// holding this object. </param>
                /// <returns>The item that was created from this operation, or null.</returns>
                public Item.IItem LootItem(in MapData mapData, in List<string> Names) {
                    if (mapData.IsNull) return null;
                    if (mapData.density == 0) return null;
                    Item.IItem item;
                    if (mapData.SolidDensity > 0) {
                        if (SolidItem >= Names.Count || SolidItem < 0) return null;
                        int SIndex = ItemInfo.RetrieveIndex(Names[SolidItem]);
                        item = ItemInfo.Retrieve(SIndex).Item;
                        item.Create(SIndex, mapData.SolidDensity);
                    } else {
                        if (LiquidItem >= Names.Count || LiquidItem < 0) return null;
                        int LIndex = ItemInfo.RetrieveIndex(Names[LiquidItem]);
                        item = ItemInfo.Retrieve(LIndex).Item;
                        item.Create(LIndex, mapData.LiquidDensity);
                    }
                    return item;
                }
            }
            
        /// <summary> An optional handler for materials which are capable of dropping
        /// multiple types of items on a probabilistic basis. </summary>
        [Serializable]
        public struct MultiLooter{
            /// <summary> The list of materials that can optionally be
            /// dropped if the material being removed is solid. </summary>
            public Option<List<LootInfo>> SolidLootTable;
            /// <summary> The index within the <see cref="Names"> name registry </see> of the name within the external registry, 
            /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is solid
            /// and no other material in <see cref="SolidLootTable"/> is selected. If the index does not point to a valid name 
            /// (e.g. -1), no item will be picked up when the material is removed. </summary>
            public int DefaultSolidLoot;
            /// <summary> The list of materials that can optionally be
            /// dropped if the material being removed is liquid. </summary>
            public Option<List<LootInfo>> LiquidLootTable;
            /// <summary> The index within the <see cref="Names"> name registry </see> of the name within the external registry, 
            /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is liquid
            /// and no other material in <see cref="LiquidLootTable"/> is selected. If the index does not point to a valid name 
            /// (e.g. -1), no item will be picked up when the material is removed. </summary>
            public int DefaultLiquidLoot;

            /// <summary>  Creates a generic item from map information of what has been removed. 
            /// This is the standard logic for item acquirement for most materials. Returns a 
            /// solid item if <see cref="MapData.SolidDensity"/> is nonzero, a liquid material otherwise,
            /// or null if a suitable item cannot be found. </summary>
            /// <param name="mapData">The map data indicating the amount of material removed
            /// and the state it was removed as</param>
            /// <param name="Names">The <see cref="Names"> name registry </see> specific to the material
            /// holding this object. </param>
            /// <returns>The item that was created from this operation, or null.</returns>
            public Item.IItem LootItem(in MapData mapData, in List<string> Names) {
                Unity.Mathematics.Random random = new Unity.Mathematics.Random(
                    (uint)Environment.TickCount ^ mapData.data
                );
                if (mapData.SolidDensity > 0)
                    return GetTableItem(mapData.SolidDensity, DefaultSolidLoot, SolidLootTable,
                    Names, ref random);
                else
                    return GetTableItem(mapData.LiquidDensity, DefaultLiquidLoot, LiquidLootTable,
                    Names, ref random);
            }
            private readonly Item.IItem GetTableItem(
                int amount,
                int DefaultLoot,
                List<LootInfo> LootTable,
                List<string> Names,
                ref Unity.Mathematics.Random random
            ) {
                if (amount <= 0) return null;

                if (LootTable != null) {
                    foreach (LootInfo info in LootTable) {
                        if (random.NextFloat() > info.DropChance)
                            continue;
                        return CreateItem(Names, info.DropItem, amount);
                    }
                }

                return CreateItem(Names, DefaultLoot, amount);
            }

            private static Item.IItem CreateItem(List<string> Names, int DropItem, int amount) {
                if (DropItem < 0 || DropItem >= Names.Count) return null;
                string name = Names[DropItem];
                if (String.IsNullOrEmpty(name)) return null;
                if (amount == 0) return null;

                int Index = ItemInfo.RetrieveIndex(name);
                Item.IItem item = ItemInfo.Retrieve(Index).Item;
                item.Create(Index, amount);
                return item;
            }
            /// <summary> The settings describing an optional material that can be dropped
            /// and how often it should be dropped. </summary>
            [Serializable]
            public struct LootInfo{
                /// <summary> The index within the <see cref="MaterialData.Names"> name registry </see> of the name within the external registry, 
                /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when if the material is randomly selected.
                /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
                public int DropItem;
                /// <summary> The chance the material is dropped. This is resampled 
                /// only once everytime any amount of material is removed. </summary>
                public float DropChance;
            }
        }

        /// <summary> Returns the registry entry's name of a registry reference coupled with the <see cref="Names">name register</see> </summary>
        /// <param name="index">The index within the <see cref="Names">name register</see> of the name of the reference</param>
        /// <returns>The name of the reference in an external registry or null if a name for <paramref name="index"/> cannot be found. </returns>
        public string RetrieveKey(int index) {
            if (index < 0 || index >= Names.Count) return null;
            return Names[index];
        }
    }


    /// <summary> A utility class to override serialization of <see cref="Structure.StructureData.PointInfo"/> into a Unity Inspector format.
    /// It exposes the internal components of the bitmap so it can be more easily understood by the developer. </summary>
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(MaterialData.TerrainData.GeoShaderInfo))]
    public class GeoShaderIndexDrawer : PropertyDrawer {
        /// <summary>  Callback for when the GUI needs to be rendered for the property. </summary>
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty dataProp = property.FindPropertyRelative("data");
            uint data = dataProp.uintValue;

            //bool isDirty = (data & 0x80000000) != 0;
            bool hasGeoShader = (data & 0x80000000) != 0;
            int[] indices = new int[] { (int)(data >> 16) & 0x7FFF, (int)data & 0xFFFF };

            Rect rect = new(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

            if (hasGeoShader){
                EditorGUI.MultiIntField(rect, new GUIContent[] { new GUIContent("Batch#"), new GUIContent("Variant#") }, indices);
                rect.y += EditorGUIUtility.singleLineHeight;
            }
            hasGeoShader = EditorGUI.Toggle(rect, "HasShader", hasGeoShader);
            rect.y += EditorGUIUtility.singleLineHeight;
            
            data = hasGeoShader ? data | 0x80000000 : data & 0x7FFFFFFF;
            data = (data & 0x8000FFFF) | (((uint)indices[0] & 0x7FFF) << 16);
            data = (data & 0xFFFF0000) | ((uint)indices[1] & 0xFFFF);
            dataProp.uintValue = data;
        }

        /// <summary>  Callback for when the GUI needs to know the height of the Inspector element. </summary>
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight * 2;
        }
    }
#endif
}
