using UnityEngine;

namespace WorldConfig.Quality {
    [CreateAssetMenu(menuName = "Containers/Balanced Heap")]
    public class BalancedMemory : Memory {
        /// <summary> The initial amount of compute buffer meta data to allocate.
        /// This does not indicate the actual memory initially allocated,
        /// but the maximum blocks that can be allocated without resizing 
        /// meta data buffers which is an expensive operation. </summary>
        public int InitBlockCount;
        //Percentage of BlockAllocationSize
        [Range(0, 1)]
        public float OverflowHandlerSizeReq;
    }
}