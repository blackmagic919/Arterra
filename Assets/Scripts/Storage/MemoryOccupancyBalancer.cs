using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace Arterra.Configuration.Quality{
    //Strategy: 
    public class MemoryOccupancyBalancer : MemoryBufferHandler {
        private ComputeBuffer _OccupancyBuffer;
        private List<BufferAllocation> MemoryBlocks;
        private BufferAllocation curAlloc => MemoryBlocks[AllocBufferIndex];
        private int AllocBufferIndex = 0;
        private BalancedMemory settings;
        private int[] addressBuffers;

        public MemoryOccupancyBalancer(BalancedMemory settings) : base(settings) {
            this.settings = settings;
            MemoryBlocks = new List<BufferAllocation>() { new(_GPUMemorySource, _EmptyBlockHeap) };
            _OccupancyBuffer = new ComputeBuffer(settings.InitBlockCount, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            _OccupancyBuffer.SetData(new uint2[settings.InitBlockCount]); //Zero out

            AllocBufferIndex = 0;

            addressBuffers = new int[settings.AddressSize + 1];
            Preset();
            ReadbackLoopDeferredAddress();
        }

        private void Preset() {
            this.AllocateShader.EnableKeyword("TRACKING");
            this.DeallocateShader.EnableKeyword("TRACKING");
            this.d_DeallocateShader.EnableKeyword("TRACKING");
            this.d_AllocateShader.EnableKeyword("TRACKING");

            this._GPUMemorySource = MemoryBlocks[AllocBufferIndex]._Storage;
            this._EmptyBlockHeap = MemoryBlocks[AllocBufferIndex]._Heap;
            this.AllocateShader.SetBuffer(0, "_Occupancy", _OccupancyBuffer);
            this.d_AllocateShader.SetBuffer(0, "_Occupancy", _OccupancyBuffer);
            this.DeallocateShader.SetBuffer(0, "_Occupancy", _OccupancyBuffer);

            this.d_DeallocateShader.SetInt("buffIndex", AllocBufferIndex);
            this.d_DeallocateShader.SetBuffer(0, "_Occupancy", _OccupancyBuffer);
            this.d_DeallocateShader.SetBuffer(0, "_SourceMemory", _GPUMemorySource);
            this.d_DeallocateShader.SetBuffer(0, "_Heap", _EmptyBlockHeap);
        }

        private void PrepareNewBlock(BufferAllocation alloc) {
            int kernel = HeapSetupShader.FindKernel("Prepare");
            HeapSetupShader.SetBuffer(kernel, ShaderIDProps.SourceMemory, alloc._Storage);
            HeapSetupShader.SetBuffer(kernel, ShaderIDProps.Heap, alloc._Heap);
            HeapSetupShader.SetInt(ShaderIDProps.BufferSize4Bytes, settings.StorageSize);
            HeapSetupShader.Dispatch(kernel, 1, 1, 1);
        }

        public override void Release()
        {
            _OccupancyBuffer.Release();
            foreach (BufferAllocation block in MemoryBlocks) {
                block.Release();
            }

            base.Release();
        }


        private void ReadbackLoopDeferredAddress() {
            void OnReadbackComplete(AsyncGPUReadbackRequest req) {
                if (!initialized) return;
                if (req.hasError) {
                    Debug.LogError("Failed to readback Deferred Address Buffer");
                    //Continue trying to readback
                    ReadbackLoopDeferredAddress();
                    return;
                }

                List<uint3> bufferMeta = req.GetData<uint2>()
                    .Select((u, i) => new uint3(u.x, u.y, (uint)i))
                    .Take(MemoryBlocks.Count)
                    .ToList();

                //Debug.Log(string.Join(", ", bufferMeta.Select(a => a.x / (float)settings.StorageSize + " : " + a.y)));
                bufferMeta.Sort((a, b) => {
                    int cmp = a.x.CompareTo(b.x);
                    if (cmp == 0) return a.y.CompareTo(b.y);
                    return cmp;
                });

                //Remove empty buffers
                int start = 0;
                double maxOvfAllocSize = settings.StorageSize * (1 - (double)settings.OverflowHandlerSizeReq);
                for (; start < bufferMeta.Count - 1; start++) {
                    if (bufferMeta[start + 1].x > maxOvfAllocSize) break;
                    if (bufferMeta[start].x != 0) break;
                }

                uint3[] emptyArray = bufferMeta.Take(start).ToArray();
                bufferMeta = bufferMeta.Skip(start).ToList();
                List<uint3> sorted = emptyArray.OrderByDescending(x => x.z).ToList();
                foreach (uint3 idx in sorted) {
                    //We cannot deallocate this buffer because there may be unseen queued commands
                    if (idx.z == AllocBufferIndex) continue;
                    MemoryBlocks[(int)idx.z].Release();
                    MemoryBlocks.RemoveAt((int)idx.z);
                }

                //Add new overflow buffer if needed
                if (bufferMeta[0].x > maxOvfAllocSize) {
                    BufferAllocation newAlloc = new(settings);
                    PrepareNewBlock(newAlloc);
                    MemoryBlocks.Add(newAlloc);

                    //Add to beginning of bufferMeta
                    bufferMeta.Insert(0, new(0, 0, (uint)MemoryBlocks.Count - 1));
                }

                //Set new alloc buffer as smallest buffer with > OverflowHandlerSizeReq space
                int i = 0;
                for (; i < bufferMeta.Count - 1; i++){
                    if (bufferMeta[i + 1].x > maxOvfAllocSize) break;
                } AllocBufferIndex = (int)bufferMeta[i].z;

                //Resize OccupancyBuffer if needed
                if (_OccupancyBuffer.count < MemoryBlocks.Count) {
                    ComputeBuffer o_OccupancyBuffer = _OccupancyBuffer;
                    int o_size = o_OccupancyBuffer.count; int n_size = MemoryBlocks.Count;
                    _OccupancyBuffer = new ComputeBuffer(n_size, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
                    UtilityBuffers.CopyBuffer(o_OccupancyBuffer, _OccupancyBuffer, 0, 0, o_size); //Copy over original tracking
                    _OccupancyBuffer.SetData(new uint2[n_size - o_size], 0, o_size, n_size - o_size); //Zero new entries
                    o_OccupancyBuffer.Release(); //Release original tracking 
                    Preset(); //Update Bindings
                }

                ReadbackLoopDeferredAddress();
            }

            //Re-request readback
            AsyncGPUReadback.Request(_OccupancyBuffer, OnReadbackComplete);
        }

        public override ComputeBuffer GetBlockBuffer(uint index) {
            index = (uint)addressBuffers[index];
            if (index < 0 || index >= MemoryBlocks.Count)
                throw new Exception("Buffer index out of range");
            return MemoryBlocks[(int)index]._Storage;
        }

        public override ComputeBuffer GetBlockBuffer(int index) {
            index = addressBuffers[index];
            if (index < 0 || index >= MemoryBlocks.Count)
                throw new Exception("Buffer index out of range");
            return MemoryBlocks[index]._Storage;
        }

        //Only call if using immediately and no interrupt has happened yet
        public override ComputeBuffer Storage => curAlloc._Storage;

        public override bool GetBlockBufferSafe(int index, out ComputeBuffer buffer) {
            index = addressBuffers[index]; buffer = null;
            if (!initialized) return false;
            if (index < 0 || index >= MemoryBlocks.Count)
                return false;
            buffer = MemoryBlocks[(int)index]._Storage;
            return true;
        }

        public override uint AllocateMemory(ComputeBuffer count, int stride, int countOffset = 0) {
            if (!initialized) return 0;
            this.AllocateShader.SetBuffer(0, ShaderIDProps.SourceMemory, curAlloc._Storage);
            this.AllocateShader.SetBuffer(0, ShaderIDProps.Heap, curAlloc._Heap);
            this.AllocateShader.SetInt(ShaderIDProps.BufferIndex, AllocBufferIndex);
            uint allocIndex = base.AllocateMemory(count, stride, countOffset);
            addressBuffers[allocIndex] = AllocBufferIndex;
            return allocIndex;
        }

        public override uint AllocateMemoryDirect(int count, int stride) {
            if (!initialized) return 0;
            this.d_AllocateShader.SetBuffer(0, ShaderIDProps.SourceMemory, curAlloc._Storage);
            this.d_AllocateShader.SetBuffer(0, ShaderIDProps.Heap, curAlloc._Heap);
            this.d_AllocateShader.SetInt(ShaderIDProps.BufferIndex, AllocBufferIndex);
            uint allocIndex = base.AllocateMemoryDirect(count, stride);
            addressBuffers[allocIndex] = AllocBufferIndex;
            return allocIndex;
        }

        public override void ReleaseMemory(uint addressIndex) {
            if (!initialized) return;
            int buffInd = addressBuffers[addressIndex];
            BufferAllocation alloc = MemoryBlocks[buffInd];
            this.DeallocateShader.SetBuffer(0, ShaderIDProps.SourceMemory, alloc._Storage);
            this.DeallocateShader.SetBuffer(0, ShaderIDProps.Heap, alloc._Heap);
            this.DeallocateShader.SetInt(ShaderIDProps.BufferIndex, buffInd);
            base.ReleaseMemory(addressIndex);
        }

        //This module cannot support direct deallocs unfortunately
        internal class BufferAllocation {
            internal ComputeBuffer _Storage;
            internal ComputeBuffer _Heap;

            public BufferAllocation(Memory settings) {
                _Storage = new ComputeBuffer(settings.StorageSize, sizeof(uint), ComputeBufferType.Structured, ComputeBufferMode.Immutable);
                _Heap = new ComputeBuffer(settings.HeapSize, sizeof(uint) * 2, ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            }

            public BufferAllocation(ComputeBuffer s, ComputeBuffer h) {
                _Storage = s;
                _Heap = h;
            }

            public void Release() {
                _Storage?.Release();
                _Heap?.Release();
            }
        }
    }
}
