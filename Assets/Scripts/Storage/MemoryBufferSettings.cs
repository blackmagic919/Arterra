using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace WorldConfig.Quality{
    /// <summary>
    /// Settings describing the long-term GPU memory customly managed and used to 
    /// store/visualize the world throughout terrain generation. 
    /// <seealso cref="TerrainGeneration.GenerationPreset.MemoryHandle"/>
    /// </summary>
    [CreateAssetMenu(menuName = "Containers/Memory Heap")]
    public class Memory : ScriptableObject
    {
        /// <summary> The amount of storage memory allocated in a GPU buffer that is used
        /// for long-term storage of intermediate and final terrain data. Measured
        /// in terms of 4-byte words. Running out of space may result in failure
        /// to generate terrain. </summary>
        [UISetting(Message = "Amount of Gross GPU Memory Allocated. Please be Aware of Device Capabilities")]
        public int StorageSize = (int)5E8; //2 gB
        /// <summary> The amount of heap blocks allocated for the free-block heap tracking addresses
        /// of free blocks within the <see cref="TerrainGeneration.GenerationPreset.MemoryHandle.Storage">Storage</see> buffer.
        /// The maximum amount of free blocks that will be used is equal to the maximum amount of concurrent allocations that 
        /// are made. Running out of space may result in failure to generate terrain. </summary>
        public int HeapSize = (int)5E4; //50k references

        /// <summary>
        /// The maximum amount of addresses that can be stored in the address buffer which holds the direct
        /// address within the storage buffer of allocated blocks. Running out of space may result in failure to generate terrain.
        /// <seealso cref="TerrainGeneration.GenerationPreset.MemoryHandle.Address"/>.
        /// </summary>
        public int AddressSize = (int)5E4; //50k addresses
    }

}