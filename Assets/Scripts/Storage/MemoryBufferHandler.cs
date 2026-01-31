using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;
using Arterra.Utils;

namespace Arterra.Configuration.Quality {
    /// <summary>
    /// Responsible for managing the allocation and deallocation of memory on the GPU for generation
    /// related tasks. Rather than allowing each system to maintain its own <see cref="ComputeBuffer"/>, 
    /// memory is allocated through a shader-based malloc which allows for more efficient memory management and 
    /// fewer buffer locations to track. Settings on the size of this memory heap can be found in <see cref="Quality.Memory"/>.
    /// <seealso href = "https://blackmagic919.github.io/AboutMe/2024/08/18/Memory-Heap/"/> 
    /// </summary>
    public class MemoryBufferHandler {
        /// <exclude />
        protected ComputeShader HeapSetupShader;
         /// <exclude />
        protected ComputeShader AllocateShader;
         /// <exclude />
        protected ComputeShader DeallocateShader;
         /// <exclude />
        protected ComputeShader d_AllocateShader;
         /// <exclude />
        protected ComputeShader d_DeallocateShader;
        //Remember to set to null, this can cause a circular reference
        private BatchReleaseEmpty BatchRelease;
         /// <exclude />
        protected ComputeBuffer _GPUMemorySource;
         /// <exclude />
        protected ComputeBuffer _EmptyBlockHeap;
         /// <exclude />
        protected LogicalBlockBuffer _AddressBuffer;
         /// <exclude />
        protected bool initialized;

        /// <summary>
        /// Initializes the <see cref="MemoryBufferHandler"/>. Allocates a memory heap on the GPU for use in the terrain generation process.
        /// Information is stored in local GPU Buffers which should be obtained through <see cref="Storage"/> and <see cref="Address"/>
        /// properties and bound to any shader that needs to use it. 
        /// </summary>
        public MemoryBufferHandler(Memory settings) {
            if (initialized) Release();

            _GPUMemorySource = new ComputeBuffer(settings.StorageSize, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            //2 channels, 1 for size, 2 for memory address
            _EmptyBlockHeap = new ComputeBuffer(settings.HeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _AddressBuffer = new LogicalBlockBuffer(GraphicsBuffer.Target.Structured, settings.AddressSize + 1, sizeof(uint) * 2);
            BatchRelease = new BatchReleaseEmpty(this);
            Preset();

            initialized = true;
            PrepareMemory(settings);
        }

        /// <summary>
        /// Releases all buffers used by the MemoryHandle.
        /// Call this method before the program exits to prevent memory leaks.
        /// </summary>
        public virtual void Release() {
            _GPUMemorySource?.Release();
            _EmptyBlockHeap?.Release();
            _AddressBuffer?.Destroy();
            BatchRelease?.Release();
            BatchRelease = null;
            initialized = false;
        }

        private void Preset() {
            HeapSetupShader = GameObject.Instantiate(Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/PrepareHeap"));
            AllocateShader = GameObject.Instantiate(Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/AllocateData"));
            DeallocateShader = GameObject.Instantiate(Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/DeallocateData"));
            AllocateShader.DisableKeyword("DEFERRED_ALLOCATE");
            DeallocateShader.DisableKeyword("DEFERRED_DEALLOCATE");
            d_AllocateShader = GameObject.Instantiate(AllocateShader);
            d_DeallocateShader = GameObject.Instantiate(DeallocateShader);
            AllocateShader.DisableKeyword("DIRECT_ALLOCATE");
            DeallocateShader.DisableKeyword("DIRECT_DEALLOCATE");
            d_AllocateShader.EnableKeyword("DIRECT_ALLOCATE");
            d_DeallocateShader.EnableKeyword("DIRECT_DEALLOCATE");

            AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer.Get());

            d_AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            d_AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            d_AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer.Get());

            DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            DeallocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer.Get());

            d_DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            d_DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        }

        private void PrepareMemory(Memory settings) {
            int kernel = HeapSetupShader.FindKernel("Prepare");
            HeapSetupShader.SetBuffer(kernel, "_SourceMemory", _GPUMemorySource);
            HeapSetupShader.SetBuffer(kernel, "_Heap", _EmptyBlockHeap);
            HeapSetupShader.SetInt("_BufferSize4Bytes", settings.StorageSize);

            HeapSetupShader.Dispatch(kernel, 1, 1, 1);
        }

        /// <summary>
        /// Allocates a memory block of size (<paramref name="count"/> * <paramref name="stride"/>) with the 
        /// specified <paramref name="stride"/> on the GPU. The unit is a 4-byte integer(word) and byte-level
        /// allocation is not supported. 
        /// </summary>
        /// <remarks>
        /// As this malloc functions on the GPU, though its synchronous complexity is O(log N) where N is the size of
        /// the free heap, it is an inefficient use of GPU resources as it must be performed synchronously. As a result
        /// avoid using this function for small allocations or in performance-critical sections of the code.
        /// 
        /// The <paramref name="stride"/> is specified such that one can cast the <see cref="Storage"/> buffer as containing a struct of size <paramref name="stride"/>
        /// which expedites and simplifies the process of reading and writing to the buffer.
        /// </remarks>
        /// <param name="count">The amount of structures of size <paramref name = "stride" /> to be allocated adjacently. The total size of the allocation is (<paramref name="count"/> * <paramref name="stride"/>)</param>
        /// <param name="stride">The alignment of structures relative to <see cref="Storage"/>, every entry will be at a relative address that is a multiple of <paramref name = "stride" />. Stride is in terms of 4-byte words </param>
        /// <returns>
        /// The address within the <see cref="Address"/> buffer of the entry which holds the address of the first entry
        /// within the <see cref="Storage"/> buffer. To read from the memory block, a shader should follow the access pattern
        /// <b>return addres</b> -> Address Buffer -> Storage Memory. This indirection is necessary to avoid GPU readback. 
        /// <remarks> 
        /// The address within the <see cref="Address"/> buffer is will contain two entries, the first being the 4-byte relative address
        /// and the second being relative to the requested <paramref name = "stride" /> <paramref name="stride"/>.
        /// </remarks>
        /// </returns>
        public virtual uint AllocateMemoryDirect(int count, int stride) {
            if (!initialized) return 0;

            uint addressIndex = _AddressBuffer.Allocate();
            //Allocate Memory
            d_AllocateShader.SetInt(ShaderIDProps.AddressIndex, (int)addressIndex);
            d_AllocateShader.SetInt(ShaderIDProps.AllocCount, count);
            d_AllocateShader.SetInt(ShaderIDProps.AllocStride, stride);

            d_AllocateShader.Dispatch(0, 1, 1, 1);
            return addressIndex;
        }

        /// <summary>
        /// Same as <see cref="AllocateMemoryDirect(int, int)"/> but with the count being indirectly referenced. This is applicable
        /// if the amount of objects to be allocated is not known on the CPU. To reference the count, a <see cref="ComputeBuffer"/> is passed
        /// while the location of the count within the buffer is specified by <paramref name="countOffset"/>.
        /// </summary>
        /// <param name="count">A buffer containing the amount of objects of size stride to be allocated. The count must be a 4-byte integer aligned within the buffer. <seealso cref="AllocateMemoryDirect(int, int)"/></param>
        /// <param name="stride"><see cref="AllocateMemoryDirect(int, int)"/></param>
        /// <param name="countOffset">The 4-byte offset within the buffer of the count.</param>
        /// <returns> <see cref="AllocateMemoryDirect(int, int)"/> </returns>
        public virtual uint AllocateMemory(ComputeBuffer count, int stride, int countOffset = 0) {
            if (!initialized) return 0;

            uint addressIndex = _AddressBuffer.Allocate();

            //Allocate Memory
            AllocateShader.SetInt(ShaderIDProps.AddressIndex, (int)addressIndex);
            AllocateShader.SetInt(ShaderIDProps.CountOffset, countOffset);

            AllocateShader.SetBuffer(0, ShaderIDProps.AllocCount, count);
            AllocateShader.SetInt(ShaderIDProps.AllocStride, stride);

            AllocateShader.Dispatch(0, 1, 1, 1);
            return addressIndex;
        }

        /// <summary>
        /// Releases an allocated memory block from the <see cref="Storage"/> buffer. The caller
        /// must ensure that the address points to a valid allocated memory block. Improper use
        /// may corrupt the memory heap and cause undefined behavior.
        /// </summary>
        /// <param name="addressIndex">
        /// The address of the entry within the <see cref="Address"/> buffer which points to the
        /// allocated memory block within the <see cref="Storage"/> buffer. 
        /// </param>
        public virtual void ReleaseMemory(uint addressIndex) {
            if (!initialized || addressIndex == 0) return;
            //Release Memory
            DeallocateShader.SetInt(ShaderIDProps.AddressIndex, (int)addressIndex);
            DeallocateShader.Dispatch(0, 1, 1, 1);
            _AddressBuffer.Release(addressIndex);

            //uint[] heap = new uint[6];
            //_EmptyBlockHeap.GetData(heap);
            //Debug.Log("Primary: " + heap[3]/250000 + "MB");
        }

        /// <summary>
        /// Same as <see cref="ReleaseMemory(uint)"/> but with the address being directly referenced. If the caller
        /// knows the specific location of the address within the GPU, this method can be used to release memory.
        /// </summary>
        /// <param name="address">A compute buffer containing the 4-byte sized and aligned address within <see cref="Storage"/> of the memroy block.</param>
        /// <param name="countOffset">The 4-byte offset within <paramref name="address"/> of the entry containing the address to the memory block</param>
        public virtual void ReleaseMemoryDirect(ComputeBuffer address, int countOffset = 0) {
            if (!initialized) return;
            //Allocate Memory
            d_DeallocateShader.SetBuffer(0, ShaderIDProps.AddressDict, address);
            d_DeallocateShader.SetInt(ShaderIDProps.CountOffset, countOffset);

            d_DeallocateShader.Dispatch(0, 1, 1, 1);
        }

        /// <summary> The primary memory buffer used for long-term memory storage in the terrain generation process. </summary>
        public virtual ComputeBuffer Storage => _GPUMemorySource;
        /// <summary> A buffer containing addresses to memory blocks within the <see cref="Storage"/> buffer. This buffer tracks the 
        /// raw 4-byte address as well as the address relative to the requested stride during allocation of each memory block. </summary>
        public virtual GraphicsBuffer Address => _AddressBuffer.Get();
        /// <summary>Retrieves the Storage Buffer associated with this allocation. </summary>
        /// <param name="index">The address returned by <see cref="AllocateMemory"/>.</param>
        /// <returns>The Compute Buffer which contains the referenced allocation</returns>
        public virtual ComputeBuffer GetBlockBuffer(uint index) { return _GPUMemorySource; }
        /// <summary>Same as <see cref="GetBlockBuffer(uint)"/> </summary>
        public virtual ComputeBuffer GetBlockBuffer(int index) { return _GPUMemorySource; }
        /// <summary>Attempts to retrieve the Storage Buffer associated with this allocation if it is valid </summary>
        /// <param name="index">The address returned by <see cref="AllocateMemory"/>.</param>
        /// <param name="buffer">If successful, the compute buffer holding this allocation </param> 
        /// <returns>Whether or not the buffer containing thsi allocation can be successfully obtained</returns>
        public virtual bool GetBlockBufferSafe(int index, out ComputeBuffer buffer) { 
            buffer = null;
            if (!initialized) return false;
            buffer = _GPUMemorySource;
            return true;
        }

        /// <summary> Will test if the given allocation is empty(e.g. failed or allocated
        /// size zero) and callback a handler if it is. </summary>
        /// <param name="alloc">The allocation that is tested to be empty</param>
        /// <param name="OnIsEmpty">The callback that is answered if it is empty</param>
        public void TestAllocIsEmpty(int alloc, Action<int> OnIsEmpty) {
            this.BatchRelease.TryReleaseIfEmpty(new BatchReleaseEmpty.ReleaseHandle {
                OnReleasing = OnIsEmpty,
                Alloc = alloc
            });
        }

        private class BatchReleaseEmpty {
            private MemoryBufferHandler mem;
            private ComputeShader GatherAllocSizes;
            private Queue<ReleaseHandle> AllocsToCheck;
            private int BatchedCheckSize;
            private int StatusGroupAlloc;
            private bool initialized;
            public BatchReleaseEmpty(MemoryBufferHandler mem) {
                this.mem = mem;
                BatchedCheckSize = 0;
                StatusGroupAlloc = 0;
                initialized = true;

                AllocsToCheck = new Queue<ReleaseHandle>();
                GatherAllocSizes = Resources.Load<ComputeShader>("Compute/MemoryStructures/GatherAllocGroup");
                GatherAllocSizes.SetBuffer(0, "CheckAddresses", UtilityBuffers.TransferBuffer);
            }

            public void Release() {
                initialized = false;
            }

            public void TryReleaseIfEmpty(ReleaseHandle handle) {
                AllocsToCheck.Enqueue(handle);
                ReadbackAndClearEmpty();
            }

            private void ReadbackAndClearEmpty() {
                if (!initialized) return;
                //Means we are currently trying to readback
                if (StatusGroupAlloc > 0) return; 
                BatchedCheckSize = AllocsToCheck.Count;
                int[] allocs = AllocsToCheck.Select(h => h.Alloc).ToArray();
                UtilityBuffers.TransferBuffer.SetData(allocs, 0, 0, BatchedCheckSize);

                StatusGroupAlloc = (int)mem.AllocateMemoryDirect(BatchedCheckSize, 1);
                ComputeBuffer storage = mem.GetBlockBuffer(StatusGroupAlloc);
                GatherAllocSizes.SetBuffer(0, ShaderIDProps.MemoryBuffer, storage);
                GatherAllocSizes.SetBuffer(0, ShaderIDProps.AddressDict, mem.Address);
                GatherAllocSizes.SetInt(ShaderIDProps.AddressIndex, StatusGroupAlloc);
                GatherAllocSizes.SetInt(ShaderIDProps.NumAddress, BatchedCheckSize);

                GatherAllocSizes.GetKernelThreadGroupSizes(0, out uint threadGroupSize, out _, out _);
                int numThreadsAxis = Mathf.CeilToInt(BatchedCheckSize / (float)threadGroupSize);
                GatherAllocSizes.Dispatch(0, numThreadsAxis, 1, 1);;

                void OnAllocsRecieved(AsyncGPUReadbackRequest request) {
                    if (!initialized) return;
                    Unity.Collections.NativeArray<uint> success = request.GetData<uint>();
                    for (int i = 0; i < BatchedCheckSize; i++) {
                        ReleaseHandle handle = AllocsToCheck.Dequeue();
                        if (success[i] != 0) continue;
                        handle.OnReleasing(handle.Alloc);
                    } 
                    mem.ReleaseMemory((uint)StatusGroupAlloc);
                    BatchedCheckSize = 0;
                    StatusGroupAlloc = 0;

                    if (AllocsToCheck.Count == 0) return;
                    ReadbackAndClearEmpty(); //recursion cycle
                }

                void OnAddressRecieved(AsyncGPUReadbackRequest request) {
                    if (!initialized) return;
                    if (!mem.GetBlockBufferSafe(StatusGroupAlloc, out ComputeBuffer block))
                        return;
                    uint2 memHandle = request.GetData<uint2>()[0];
                    if(memHandle.x == 0) return;
                    AsyncGPUReadback.Request(block, size: 4 * BatchedCheckSize, offset: 4 * (int)memHandle.y, OnAllocsRecieved);
                }

                AsyncGPUReadback.Request(mem.Address, size: 8, offset: 8 * StatusGroupAlloc, OnAddressRecieved);
            }

            public struct ReleaseHandle {
                public int Alloc;
                public Action<int> OnReleasing;
            }
        }
    }
}
