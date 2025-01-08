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
public abstract class GeoShader : ScriptableObject
{
    
    private const int DRAW_STRIDE = (sizeof(float) * 4) + (sizeof(float) * 3) * 2 + sizeof(float) * 2;
    /// <summary> Obtains the material using the <see cref="shader"/> stored within this reference.
    /// A material is an instance of a shader with custom properties set. </summary>
    /// <returns>The instantiated material with the geoshader's configuration. </returns>
    public virtual Material GetMaterial()
    {
        return null;
    }

    /// <summary> Releases any temporary buffers that were created during the generation process.
    /// This does not release the final geoshaded geometry, just any intermediate buffers. </summary> 
    /// <remarks> This functionality is deprecated as all generation occurs in a shared <see cref="UtilityBuffers"> working </see> buffer. </remarks> 
    public virtual void ReleaseTempBuffers()
    {
        
    }

    /// <summary>
    /// Processes the geometry shader, generating additional geometry based on the base geometry.
    /// </summary>
    /// <param name="memoryHandle">The handle for the buffer that contains the base geometry and the location for where the
    /// generated geoshader geometry will be copied to. </param>
    /// <param name="vertAddress">The address of the base mesh's vertices within the <paramref name="memoryHandle"/></param>
    /// <param name="triAddress">The address of the base mesh's triangles within the <paramref name="memoryHandle"/></param>
    /// <param name="baseGeoStart">The start, in the working memory buffer, of the list of offsets sorted by geoshader, 
    /// within the base mesh's primitives of the triangles belonging to the each geoshader's base mesh. </param>
    /// <param name="baseGeoCount">The length of the prefix sum, in the working memory buffer, of the amount of 
    /// triangles belonging to each geometry shader. </param>
    /// <param name="geoCounter">The index within the prefix sum in the working memory buffer indicating the 
    /// start offset and lengt of the current geoshader's base geometry </param>
    /// <param name="geoStart">The start within the working memory buffer of the region storing the output
    /// of the geoshader. When adding output, this value at this location should be increased to track the last filled position. </param>
    /// <param name="geoInd"> The index within the external registry, <see cref="Config.QualitySettings.GeoShaders"/>, of the 
    /// current geoshader. </param>
    public virtual void ProcessGeoShader(TerrainGeneration.GenerationPreset.MemoryHandle memoryHandle, int vertAddress, int triAddress, 
                                         int baseGeoStart, int baseGeoCount, int geoCounter, int geoStart, int geoInd)
    {

    }

    /// <summary> Releases any cached geometry created by the geoshader. Call this before
    /// releasing a chunk to ensure that no geoshaded geometry lingers after the chunk
    /// has been deleted. </summary>
    public virtual void Release()
    {

    }
}
}