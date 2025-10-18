using System;
using Unity.Mathematics;
using UnityEngine;

namespace WorldConfig.Quality {
    /// <summary>
    /// Responsible for managing the allocation and deallocation of memory on the GPU for generation
    /// related tasks. Rather than allowing each system to maintain its own <see cref="ComputeBuffer"/>, 
    /// memory is allocated through a shader-based malloc which allows for more efficient memory management and 
    /// fewer buffer locations to track. Settings on the size of this memory heap can be found in <see cref="WorldConfig.Quality.Memory"/>.
    /// <seealso href = "https://blackmagic919.github.io/AboutMe/2024/08/18/Memory-Heap/"/> 
    /// </summary>
    public class MemoryBufferHandler {
        protected ComputeShader HeapSetupShader;
        protected ComputeShader AllocateShader;
        protected ComputeShader DeallocateShader;
        protected ComputeShader d_AllocateShader;
        protected ComputeShader d_DeallocateShader;

        protected ComputeBuffer _GPUMemorySource;
        protected ComputeBuffer _EmptyBlockHeap;
        protected ComputeBuffer _AddressBuffer;
        protected uint2[] addressLL;
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
            _AddressBuffer = new ComputeBuffer(settings.AddressSize + 1, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            Preset();

            addressLL = new uint2[settings.AddressSize + 1];
            addressLL[0].y = 1;

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
            _AddressBuffer?.Release();
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
            AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);

            d_AllocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            d_AllocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            d_AllocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);

            DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
            DeallocateShader.SetBuffer(0, "_AddressDict", _AddressBuffer);

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

            uint addressIndex = addressLL[0].y;
            //Allocate Memory
            d_AllocateShader.SetInt("addressIndex", (int)addressIndex);
            d_AllocateShader.SetInt("allocCount", count);
            d_AllocateShader.SetInt("allocStride", stride);

            d_AllocateShader.Dispatch(0, 1, 1, 1);

            //Head node always points to first free node, remove node from LL to mark allocated
            uint pAddress = addressLL[addressIndex].x;//should always be 0
            uint nAddress = addressLL[addressIndex].y == 0 ? addressIndex + 1 : addressLL[addressIndex].y;

            addressLL[pAddress].y = nAddress;
            addressLL[nAddress].x = pAddress;

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

            uint addressIndex = addressLL[0].y;

            //Allocate Memory
            AllocateShader.SetInt("addressIndex", (int)addressIndex);
            AllocateShader.SetInt("countOffset", countOffset);

            AllocateShader.SetBuffer(0, "allocCount", count);
            AllocateShader.SetInt("allocStride", stride);

            AllocateShader.Dispatch(0, 1, 1, 1);

            //Head node always points to first free node, remove node from LL to mark allocated
            uint pAddress = addressLL[addressIndex].x;//should always be 0
            uint nAddress = addressLL[addressIndex].y == 0 ? addressIndex + 1 : addressLL[addressIndex].y;

            addressLL[pAddress].y = nAddress;
            addressLL[nAddress].x = pAddress;

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
            DeallocateShader.SetInt("addressIndex", (int)addressIndex);

            DeallocateShader.Dispatch(0, 1, 1, 1);
            //Add node back to head of LL
            uint nAddress = addressLL[0].y;
            uint pAddress = addressLL[nAddress].x; //Is equivalent to 0(head node), but this is more clear
            addressLL[pAddress].y = addressIndex;
            addressLL[nAddress].x = addressIndex;
            addressLL[addressIndex] = new uint2(pAddress, nAddress);

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
            d_DeallocateShader.SetBuffer(0, "_Address", address);
            d_DeallocateShader.SetInt("countOffset", countOffset);

            d_DeallocateShader.Dispatch(0, 1, 1, 1);
        }

        /// <summary> The primary memory buffer used for long-term memory storage in the terrain generation process. </summary>
        public virtual ComputeBuffer Storage => _GPUMemorySource;
        /// <summary> A buffer containing addresses to memory blocks within the <see cref="Storage"/> buffer. This buffer tracks the 
        /// raw 4-byte address as well as the address relative to the requested stride during allocation of each memory block. </summary>
        public virtual ComputeBuffer Address => _AddressBuffer;

        public virtual ComputeBuffer GetBlockBuffer(uint index) { return _GPUMemorySource; }
        public virtual ComputeBuffer GetBlockBuffer(int index) { return _GPUMemorySource; }
    }
}
