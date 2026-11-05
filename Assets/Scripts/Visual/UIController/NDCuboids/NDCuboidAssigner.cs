using Arterra.Data.Biome;
using UnityEngine;

namespace Arterra.Editor {
    /// <summary>Scene anchor for the biome partition authoring tool. Editing lives in Editor/.</summary>
    [ExecuteAlways]
    public class NDCuboidAssigner : MonoBehaviour {
        public BiomePlacement Category;
        [Min(0.1f)]
        public float DisplaySize = 10f;
    }
}
