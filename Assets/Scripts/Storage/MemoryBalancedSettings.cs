using UnityEngine;

namespace Arterra.Configuration.Quality {
    /// <summary>  Settings describing a multi-buffer GPU memory handler, 
    /// mainly used for saving blocks of CPU-side unknown sizes on the GPU  </summary>
    [CreateAssetMenu(menuName = "Containers/Balanced Heap")]
    public class BalancedMemory : Memory {
        /// <summary> The initial amount of compute buffer meta data to allocate.
        /// This does not indicate the actual memory initially allocated,
        /// but the maximum blocks that can be allocated without resizing 
        /// meta data buffers which is an expensive operation. </summary>
        public int InitBlockCount;
        /// <summary>The percentage of a buffer that must be free for the system to continue trying
        /// to allocate to the buffer before it tries to redirect allocations to a different buffer. </summary>
        [Range(0, 1)]
        public float OverflowHandlerSizeReq;
    }
}