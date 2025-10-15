using System;
using System.Collections.Generic;
using UnityEngine;

namespace WorldConfig.Quality {
    /// <summary>  Settings controlling how geoshaders are managed in the world. Note: Geoshaders do not 
    /// refer to the traditional geoshader pass in the render pipeline but a seperate compute
    /// shader based system for generating non-ephemeral geometry. See <see cref="GeoShader"/> for more info. </summary>
    [CreateAssetMenu(fileName = "GeoShaderSettings", menuName = "ShaderData/Settings")]
    public class GeoShaderSettings : ScriptableObject {
        /// <summary>
        /// Contains all geoshader variants. Each entry should contain a unique geometry generation pattern. The number
        /// of entries is equal to the number of batches sorted and processed within <seealso cref="ShaderGenerator"/>. 
        /// Hence, it is recommended to not define a new variant if the generation pattern can be reused from another variant. 
        /// <seealso href="https://blackmagic919.github.io/AboutMe/2024/11/03/GeoShaders/">More Info</seealso>
        /// </summary>
        public Catalogue<GeoShader> Categories;
        /// <summary> The maximum depth within the terrain octree of chunks that will be attempted to be geoshaded. </summary>
        /// <remarks> There is a large fixed cost with rendering geoshaded geometry which makes it not feasible to render it for all chunks. 
        /// Moreover, these aesthetic effects become less noticable farther away. </remarks>
        public int MaxGeoShaderDepth;
        /// <summary> The minimum distance the player must move to update 
        /// the persisted geoshader subchunks in the world. </summary>
        public float SubchunkUpdateThresh;
        /// <summary> Settings controlling different levels of details of geoshaders within a chunk
        /// (subchunk level of detail). See <see cref="DetailLevel"/> for more information </summary>
        public Option<List<DetailLevel>> levels;
        /// <summary> A single subchunk detail level within a chunk. 
        /// A detail level indicates the amount of geometry geoshaders
        /// will expend in a designated region from the player. </summary>
        [Serializable]
        public struct DetailLevel {
            /// <summary> The amount of detail(0 being highest) geoshaders are hinted to generate.
            /// Generally geoshaders will reduce geometry by 50% for each increase in level. </summary>
            public int Level;
            /// <summary>
            /// The additive distance in grid space up until which the detail level will render.
            /// This is calculated by summing the previous distances before it in <see cref="levels"/>
            /// </summary>
            public int Distance;
            /// <summary> Whether or not this detail level is of a higher depth (twice the size) from the 
            /// previous level before it in <see cref="levels"/>. Larger subchunks reduce synchronization barriers
            /// and expedite generation and rendering. </summary>
            /// <remarks>The minimum subchunk size is decided by the number of times size is increased 
            /// in <see cref="levels"/>. (<see cref="Terrain.mapChunkSize"/>/(2 ^ N))</remarks>
            public bool IncreaseSize;
        }
    }
}
