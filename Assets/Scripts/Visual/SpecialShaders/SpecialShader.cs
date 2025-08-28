using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Quality{
    /// <summary>
    /// A template for a custom GeoShader, a logical mesh generation unit 
    /// that generates more geometry given a base batch of geometry. Not to be 
    /// confused with the pipeline geoshading step, this custom geoshader occurs in
    /// the compute shader and the output geometry is cached in memory as long as the chunk's 
    /// base mesh exists. <seealso href="https://blackmagic919.github.io/AboutMe/2024/11/03/GeoShaders/">More Info</seealso>
    /// </summary>
    public abstract class GeoShader : Category<GeoShader>
    {

        private const int DRAW_STRIDE = (sizeof(float) * 4) + (sizeof(float) * 3) * 2 + sizeof(float) * 2;
        /// <summary> Obtains the material using the <see cref="shader"/> stored within this reference.
        /// A material is an instance of a shader with custom properties set. </summary>
        /// <returns>The instantiated material with the geoshader's configuration. </returns>
        public virtual Material GetMaterial()
        {
            return null;
        }

        /// <summary> Preset any information related to the <see cref="shader"/> stored within this reference.
        /// This will be run once before <see cref="ProcessGeoShader"/> is called the first time. </summary>
        ///  /// <param name="baseGeoStart">The start, in the working memory buffer, of the list of offsets sorted by geoshader, 
        /// within the base mesh's primitives of the triangles belonging to the each geoshader's base mesh. </param>
        /// <param name="baseGeoCount">The length of the prefix sum, in the working memory buffer, of the amount of 
        /// triangles belonging to each geometry shader. </param>
        /// <param name="geoCounter">The index within the prefix sum in the working memory buffer indicating the 
        /// start offset and lengt of the current geoshader's base geometry </param>
        /// <param name="geoStart">The start within the working memory buffer of the region storing the output
        public virtual void PresetData(int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
        {

        }

        /// <summary> Used to release any resources tied to this geometry shader, notably compute buffers
        /// used to manage shader variants. This is called once upon exiting the game, and will be called
        /// not before <see cref="PresetData"/> is called again. </summary>
        public virtual void Release()
        {

        }

        /// <summary>
        /// Processes the geometry shader, generating additional geometry based on the base geometry.
        /// </summary>
        /// <param name="memoryHandle">The handle for the buffer that contains the base geometry and the location for where the
        /// generated geoshader geometry will be copied to. </param>
        /// <param name="vertAddress">The address of the base mesh's vertices within the <paramref name="memoryHandle"/></param>
        /// <param name="triAddress">The address of the base mesh's triangles within the <paramref name="memoryHandle"/></param>
        /// of the geoshader. When adding output, this value at this location should be increased to track the last filled position. </param>
        /// <param name="geoInd"> The index within the external registry, <see cref="Config.QualitySettings.GeoShaders"/>, of the 
        /// current geoshader. </param>
        public virtual void ProcessGeoShader(MemoryBufferHandler memoryHandle, int vertAddress, int triAddress, int baseGeoCount)
        {

        }

        /// <summary> Retrieves the registry of shader variants. If this geoshader
        /// does not define shader variants, returns null. Note that this is returned
        /// by value, so to write to it one should call <see cref="SetRegistry"/>
        /// </summary> <returns>The registry of shader variants</returns>
        public virtual IRegister GetRegistry() {
            return null;
        }

        /// <summary> Sets the shader's registry of shader variants. Because a registry
        /// is returned by value, one should call this when updating a shader's 
        /// variant registry to validate the changes. </summary>
        /// <param name="reg">The registry of shader variants that has been updated </param>
        public virtual void SetRegistry(IRegister reg) {}
} }