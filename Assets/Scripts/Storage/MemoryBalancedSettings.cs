using UnityEngine;

namespace Arterra.Configuration.Quality {
    /// <summary>  Settings describing a multi-buffer GPU memory handler, 
    /// mainly used for saving blocks of CPU-side unknown sizes on the GPU  </summary>
    [CreateAssetMenu(menuName = "Containers/Balanced Heap")]
    public class BalancedMemory : Memory {
        /// <summary>Capacity of each ordinary long-term storage buffer in 4-byte words.
        /// An allocation larger than this receives a dedicated, larger buffer.
        /// The inherited StorageSize is the scratch heap capacity.</summary>
        [Min(2)]
        public int LongTermBlockSize = 50_000_000; // 4-byte words (200 MB)

        /// <summary>Maximum allocation attempts that may be outstanding across one
        /// asynchronous readback window. Zero uses 1024.</summary>
        [Min(1)]
        public int MaxAllocationsPerSnapshot = 25000;
    }
}
