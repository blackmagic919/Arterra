using UnityEngine;
using Unity.Mathematics;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;


namespace WorldConfig.Generation.Material{
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
    /// <summary> The index within the <see cref="Names"> name registry </see> of the name within the external registry, 
    /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is solid. 
    /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
    public int SolidItem;
    /// <summary> The index within the <see cref="Names"> name registry </see> of the name within the external registry, 
    /// <see cref="Config.GenerationSettings.Items"/>, of the item to be given when the material is picked up when it is liquid. 
    /// If the index does not point to a valid name (e.g. -1), no item will be picked up when the material is removed. </summary>
    public int LiquidItem;

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
    public abstract void UpdateMat(int3 GCoord, Unity.Mathematics.Random prng = default);

    /// <summary>
    /// The apperance of the terrain when the material <see cref="CPUMapManager.MapData.IsSolid">is solid</see>. 
    /// When a material is solid, it will be under or adjacent to a mesh. If it is adjacent, the mesh will display the 
    /// material of the closest solid map entry with the apperance defined below.
    /// </summary>
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct TerrainData{
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
        /// <summary> The index within the <see cref="Names"> Name Registry </see> of the name within the external <see cref="Config.QualitySettings.GeoShaders"/> registry,
        /// of the geometry shader that is generated ontop of the mesh when it is solid. If the index is -1, no extra geoshader will be
        /// generated for this material. </summary>
        public int GeoShaderIndex;
    }

    /// <summary>
    /// The apperance of the terrain when the material <see cref="CPUMapManager.MapData.IsGaseous">is gaseous</see>.
    /// A gaseous material will be rendered by the <see cref="AtmosphereBake">atmosphere </see> post process and must describe
    /// its optical interactions since light is permitted to pass through it. See <see href="https://blackmagic919.github.io/AboutMe/2024/09/07/Atmospheric-Scattering/">
    /// here </see> for more information.
    /// </summary>
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct AtmosphericData
    {
        /// <summary>
        /// How much this gaseous material reflects light traveling through it. The channels, ordered rgb, and each describe how 
        /// much of that wavelength is reflected when light encounters the material. As light travels through the material,
        /// lower wavelengths will be more prominent with a thinner optical density (the amount of atmosphere the light travels through)
        /// while higher wavelengths will be more prominent with a thicker optical density.
        /// </summary> <remarks>as a percentage, between 0 and 1</remarks>
        public Vector3 ScatterCoeffs;
        /// <summary>
        /// When light travels from a surface fragment to the camera, how quickly the light is scattered away. The channels, ordered rgb, 
        /// describe how much of that wavelength is reflected away from the camera as light travels through it. A higher ground extinction
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

    /// <summary>  The apperance of the terrain when the material <see cref="CPUMapManager.MapData.IsLiquid">is liquid</see>.
    /// A liquid material is under or adjacent to a seperate liquid mesh that displays the surface of the liquid terrain.
    /// If it is adjacent, the mesh will display the material of the closest liquid map entry with the apperance defined below.
    /// </summary> <remarks>If the liquid mesh borders the solid mesh, the liquid mesh will adopt the solid mesh's vertices</remarks>
    [System.Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct LiquidData{
        /// <summary> The color, ordered rgb, of the liquid as the view depth through it approaches zero. That is,
        /// as the depth of the liquid approaches zero relative to the viewer's perspective. </summary>
        public Vector3 shallowCol;
        /// <summary> The color, ordered rgb, of the liquid as the view depth through it approaches infinity. That is,
        /// as the depth of the liquid approaches infinity relative to the viewer's perspective. </summary>
        public Vector3 deepCol;
        /// <summary> How quickly does the color transition from <see cref="shallowCol"/> to <see cref="deepCol"/> as the
        /// depth increases. </summary>
        [Range(0,1)]
        public float colFalloff;
        /// <summary>  How quickly the liquid's surface transitions from being transparent to opaque as the view depth through 
        /// it approaches infinity. </summary>
        [Range(0,1)]
        public float depthOpacity;
        /// <summary> How smooth are waves on the liquid's surface. A higher value will result in smoother waves while a lower value will result
        /// in more constrasted, sharp waves. </summary>
        [Range(0,1)]
        public float smoothness;
        /// <summary> How large/coarse are the waves. Specifically, how much the waves are blended between a <see cref="Generation.liquidCoarseWave">coarse</see> wave
        /// map and a <see cref="Generation.liquidFineWave">fine</see> wave map. </summary>
        [Range(0,1)]
        public float waveBlend;
        /// <summary> How noticeable are waves on the liquid's surface. </summary>
        [Range(0,1)]
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

    /// <summary> Returns the registry entry's name of a registry reference coupled with the <see cref="Names">name register</see> </summary>
    /// <param name="index">The index within the <see cref="Names">name register</see> of the name of the reference</param>
    /// <returns>The name of the reference in an external registry. a string containing "NULL" if a name for <paramref name="index"/> cannot be found. </returns>
    public string RetrieveKey(int index){
        if(index < 0 || index >= Names.Count) return "NULL";
        return Names[index];
    }
}}