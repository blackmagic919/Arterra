using System;
using System.Collections.Generic;
using Arterra.Utils;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using System.Linq;

namespace Arterra.Configuration.Quality {
    /// <summary>
    /// Uses a GPU heap as scratch space while allocation sizes are unknown, then
    /// places surviving allocations in CPU-managed long-term buffers.
    /// </summary>
    public class MemoryOccupancyBalancer : MemoryBufferHandler {
        private readonly BalancedMemory settings;
        private readonly Allocation[] addressBuffers;
        private readonly DirectManagedAllocation[] directlManagedAllocations;
        private readonly List<PendingOperation> oppToAddressIndex = new();
        private readonly List<BufferAllocation> blockStorage = new();
        private readonly ComputeShader migrationShader;
        private ComputeBuffer allocationSizes;
        private ComputeBuffer allocationShiftBuffer;
        private readonly int journalCapacity;
        private int currentOperation;

        public MemoryOccupancyBalancer(BalancedMemory settings) : base(settings) {
            this.settings = settings;
            journalCapacity = settings.MaxAllocationsPerSnapshot > 0
                ? settings.MaxAllocationsPerSnapshot : 1024;
            allocationSizes = new ComputeBuffer(journalCapacity, sizeof(uint) * 2,
                ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            allocationSizes.SetData(new uint2[journalCapacity]);
            allocationShiftBuffer = new ComputeBuffer(journalCapacity, sizeof(uint) * 2,
                ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            addressBuffers = new Allocation[settings.AddressSize + 1];
            directlManagedAllocations = new DirectManagedAllocation[settings.AddressSize + 1];
            for (int i = 0; i < addressBuffers.Length; i++)
                addressBuffers[i] = new Allocation { bufferIndex = -2, operationIndex = -1 };
            migrationShader = UnityEngine.Object.Instantiate(
                Resources.Load<ComputeShader>("Compute/MemoryStructures/Heap/MigrateBalancedBlock"));
            Preset();
            ReadbackLoopDeferredAddress();
        }

        public override ComputeBuffer Storage => _GPUMemorySource;

        private void Preset() {
            AllocateShader.EnableKeyword("TRACKING");
            d_AllocateShader.EnableKeyword("TRACKING");
            DeallocateShader.EnableKeyword("TRACKING");
            d_DeallocateShader.EnableKeyword("TRACKING");
            AllocateShader.SetBuffer(0, "Allocations", allocationSizes);
            d_AllocateShader.SetBuffer(0, "Allocations", allocationSizes);
            DeallocateShader.SetBuffer(0, "Allocations", allocationSizes);
            d_DeallocateShader.SetBuffer(0, "Allocations", allocationSizes);

            int kernel = migrationShader.FindKernel("Copy");
            migrationShader.SetBuffer(kernel, "_AddressDict", Address);
            migrationShader.SetBuffer(kernel, "_SourceMemory", _GPUMemorySource);
            kernel = migrationShader.FindKernel("Commit");
            migrationShader.SetBuffer(kernel, "_AddressDict", Address);

            kernel = migrationShader.FindKernel("ShiftJournalToTemp");
            migrationShader.SetBuffer(kernel, "Allocations", allocationSizes);
            migrationShader.SetBuffer(kernel, "AllocationShiftBuffer",
                allocationShiftBuffer);
            kernel = migrationShader.FindKernel("ShiftJournalFromTemp");
            migrationShader.SetBuffer(kernel, "Allocations", allocationSizes);
            migrationShader.SetBuffer(kernel, "AllocationShiftBuffer",
                allocationShiftBuffer);
        }

        public override void Release() {
            allocationSizes?.Release();
            allocationShiftBuffer?.Release();
            foreach (BufferAllocation block in blockStorage) block?.Release();
            if (migrationShader != null) UnityEngine.Object.Destroy(migrationShader);
            base.Release();
        }

        private void ReadbackLoopDeferredAddress() {
            if (!initialized) return;
            int count = currentOperation;
            // Keep the loop alive while idle; a zero-byte readback is invalid.
            int readCount = Math.Max(1, count);
            AsyncGPUReadback.Request(allocationSizes, readCount * sizeof(uint) * 2, 0,
                request => CompleteReadback(request, count));
        }

        private void CompleteReadback(AsyncGPUReadbackRequest request, int count) {
            if (!initialized) return;
            if (request.hasError) {
                Debug.LogError("Failed to read back balanced memory allocation sizes; retrying.");
                ReadbackLoopDeferredAddress();
                return;
            }
            var sizes = request.GetData<uint2>();
            var callbacks = new List<(uint address, int blockIndex, Action<ComputeBuffer> action)>();
            for (int i = 0; i < count; i++) {
                uint addressIndex = oppToAddressIndex[i].addressIndex;
                Allocation allocation = addressBuffers[addressIndex];
                // The CPU may have released and reused the logical address.
                if (allocation.operationIndex != i) continue;
                allocation.operationIndex = -1;
                addressBuffers[addressIndex] = allocation;
                //Debug.Log(currentOperation);
                //Debug.Log(String.Join("Mb ", blockStorage.Select(
                //    block => block?.MaxFree / 250000 ?? 0)) + "Mb");
                uint2 size = sizes[i];
                if (size.x == 0 || size.y == 0) continue;
                long wordCount = (long)size.x * size.y;
                if (wordCount > int.MaxValue || size.y > int.MaxValue) {
                    Debug.LogError($"Allocation {addressIndex} is too large to migrate.");
                    continue;
                }

                if (!TryAllocateStorage((int)wordCount, (int)size.y,
                    out int blockIndex, out int rawStart, out int alignedStart,
                    out int reservedWords)) {
                    Debug.LogError($"No long-term buffer can hold allocation {addressIndex}.");
                    continue;
                }

                DispatchCopy(addressIndex, blockIndex, alignedStart,
                    (int)wordCount, (int)size.y);
                // All earlier scratch users precede this copy on the graphics
                // queue. Future users will receive the new binding below.
                DeallocateShader.SetInt("operationIndex", -1);
                DeallocateMemoryBlock(addressIndex);
                CommitMigration(addressIndex, blockIndex, rawStart, alignedStart,
                    reservedWords, (int)size.y);

                allocation.bufferIndex = blockIndex;
                addressBuffers[addressIndex] = allocation;
                directlManagedAllocations[addressIndex] = new DirectManagedAllocation {
                    rawStart = rawStart,
                    reservedWords = reservedWords
                };
                Action<ComputeBuffer> onRebind = oppToAddressIndex[i].onRebind;
                if (onRebind != null) callbacks.Add((addressIndex, blockIndex, onRebind));
            }

            // Allocations issued after the request wrote at old indices. Their
            // commands precede this shift on the graphics queue.
            int overflow = currentOperation - count;
            if (count > 0 && overflow > 0) {
                int groups = (overflow + 255) / 256;
                int kernel = migrationShader.FindKernel("ShiftJournalToTemp");
                migrationShader.SetInt("_ShiftCount", overflow);
                migrationShader.SetInt("_ShiftBy", count);
                migrationShader.Dispatch(kernel, groups, 1, 1);

                kernel = migrationShader.FindKernel("ShiftJournalFromTemp");
                migrationShader.SetInt("_ShiftCount", overflow);
                migrationShader.Dispatch(kernel, groups, 1, 1);
            }
            for (int i = count; i < currentOperation; i++) {
                uint addressIndex = oppToAddressIndex[i].addressIndex;
                Allocation allocation = addressBuffers[addressIndex];
                if (allocation.operationIndex != i) continue;
                allocation.operationIndex = i - count;
                addressBuffers[addressIndex] = allocation;
            }
            oppToAddressIndex.RemoveRange(0, count);
            currentOperation = overflow;

            foreach (var callback in callbacks) {
                Allocation allocation = addressBuffers[callback.address];
                if (allocation.bufferIndex != callback.blockIndex) continue;
                try {
                    callback.action(blockStorage[callback.blockIndex].storage);
                } catch (Exception exception) {
                    Debug.LogException(exception);
                }
            }
            ReadbackLoopDeferredAddress();
        }

        private void DispatchCopy(uint addressIndex, int blockIndex, int alignedStart,
            int wordCount, int stride) {
            int kernel = migrationShader.FindKernel("Copy");
            migrationShader.SetBuffer(kernel, "_DestMemory",
                blockStorage[blockIndex].storage);
            migrationShader.SetInt("_AddressIndex", (int)addressIndex);
            migrationShader.SetInt("_DestinationStart", alignedStart);
            migrationShader.SetInt("_WordCount", wordCount);
            migrationShader.SetInt("_Stride", stride);
            int groups = (int)(((long)wordCount + 255) / 256);
            int groupsX = Math.Min(groups, 65535);
            int groupsY = (groups + groupsX - 1) / groupsX;
            migrationShader.SetInt("_GroupsX", groupsX);
            migrationShader.Dispatch(kernel, groupsX, groupsY, 1);
        }

        private void CommitMigration(uint addressIndex, int blockIndex, int rawStart,
            int alignedStart, int reservedWords, int stride) {
            int kernel = migrationShader.FindKernel("Commit");
            migrationShader.SetBuffer(kernel, "_DestMemory",
                blockStorage[blockIndex].storage);
            migrationShader.SetInt("_AddressIndex", (int)addressIndex);
            migrationShader.SetInt("_DestinationRaw", rawStart);
            migrationShader.SetInt("_DestinationStart", alignedStart);
            migrationShader.SetInt("_DestinationWords", reservedWords);
            migrationShader.SetInt("_Stride", stride);
            migrationShader.Dispatch(kernel, 1, 1, 1);
        }

        private bool TryAllocateStorage(int words, int stride, out int index,
            out int rawStart, out int alignedStart, out int reservedWords) {
            index = -1;
            rawStart = alignedStart = reservedWords = 0;
            long required = (long)words + stride;
            if (required > int.MaxValue) return false;
            int bestRemaining = int.MaxValue;
            for (int i = 0; i < blockStorage.Count; i++) {
                BufferAllocation block = blockStorage[i];
                if (block == null || block.MaxFree < required) continue;
                int remaining = block.MaxFree - (int)required;
                if (remaining >= bestRemaining) continue;
                index = i;
                bestRemaining = remaining;
            }

            if (index < 0) {
                int configuredSize = Math.Max(2, settings.LongTermBlockSize);
                long needed = required;

                if (needed > int.MaxValue) return false;
                int size = Math.Max(configuredSize, (int)needed);
                index = blockStorage.FindIndex(block => block == null);
                if (index < 0) {
                    index = blockStorage.Count;
                    blockStorage.Add(new BufferAllocation(size));
                } else blockStorage[index] = new BufferAllocation(size);
            }
            return blockStorage[index].Allocate(words, stride,
                out rawStart, out alignedStart, out reservedWords);
        }

        private void FreeStorage(int blockIndex, int start, int length) {
            BufferAllocation block = blockStorage[blockIndex];
            block.Free(start, length);
            if (block.liveCount != 0) return;
            blockStorage[blockIndex] = null; // Keep the indices of other blocks stable.
            block.Release();
        }

        /// <summary>Register a buffer rebind for a live logical allocation.</summary>
        public void RegisterRebind(uint addressIndex, Action<ComputeBuffer> callback) {
            if (callback == null) return;
            Allocation allocation = addressBuffers[addressIndex];
            if (allocation.bufferIndex == -2)
                throw new ArgumentException("Allocation is not live", nameof(addressIndex));
            if (allocation.operationIndex >= 0) {
                PendingOperation operation = oppToAddressIndex[allocation.operationIndex];
                operation.onRebind += callback;
                oppToAddressIndex[allocation.operationIndex] = operation;
            } else {
                callback(GetBlockBuffer(addressIndex));
            }
        }

        public override ComputeBuffer GetBlockBuffer(uint index) {
            Allocation allocation = addressBuffers[index];
            if (allocation.bufferIndex == -2)
                throw new ArgumentException("Allocation is not live", nameof(index));
            return allocation.bufferIndex < 0
                ? _GPUMemorySource : blockStorage[allocation.bufferIndex].storage;
        }

        public override ComputeBuffer GetBlockBuffer(int index) =>
            GetBlockBuffer((uint)index);

        public override bool GetBlockBufferSafe(int index, out ComputeBuffer buffer) {
            buffer = null;
            if (!initialized || index <= 0 || index >= addressBuffers.Length ||
                addressBuffers[index].bufferIndex == -2) return false;
            buffer = GetBlockBuffer(index);
            return true;
        }

        public bool GetDirectAllocation(uint addressIndex, int stride,
            out ComputeBuffer buffer, out int rawStart, out int rawWordCount,
            out int alignedStart, out int count) {
            buffer = null;
            rawStart = rawWordCount = alignedStart = count = 0;
            Allocation allocation = addressBuffers[addressIndex];
            if (allocation.bufferIndex < 0) return false;

            DirectManagedAllocation direct = directlManagedAllocations[addressIndex];
            buffer = blockStorage[allocation.bufferIndex].storage;
            rawStart = direct.rawStart;
            rawWordCount = direct.reservedWords;
            int padding = (stride - rawStart % stride) % stride;
            alignedStart = (rawStart + padding) / stride;
            count = rawWordCount / stride - 1;
            return true;
        }

        private int BeginAllocation() {
            if (currentOperation >= journalCapacity)
                throw new InvalidOperationException(
                    "Balanced memory snapshot journal is full. Increase MaxAllocationsPerSnapshot.");
            return currentOperation++;
        }

        private uint FinishAllocation(uint addressIndex, int operationIndex) {
            addressBuffers[addressIndex] = new Allocation {
                bufferIndex = -1, operationIndex = operationIndex
            };
            oppToAddressIndex.Add(new PendingOperation { addressIndex = addressIndex });
            return addressIndex;
        }

        public override uint AllocateMemory(ComputeBuffer count, int stride, int countOffset = 0) {
            if (!initialized) return 0;
            int operationIndex = BeginAllocation();
            AllocateShader.SetInt("operationIndex", operationIndex);
            return FinishAllocation(base.AllocateMemory(count, stride, countOffset),
                operationIndex);
        }

        public override uint AllocateMemoryDirect(int count, int stride) {
            if (!initialized) return 0;
            int operationIndex = BeginAllocation();
            d_AllocateShader.SetInt("operationIndex", operationIndex);
            return FinishAllocation(base.AllocateMemoryDirect(count, stride),
                operationIndex);
        }

        public override void ReleaseMemory(uint addressIndex) {
            if (!initialized || addressIndex == 0 || addressBuffers[addressIndex].bufferIndex == -2)
                return;
            Allocation allocation = addressBuffers[addressIndex];
            DirectManagedAllocation directAllocation = directlManagedAllocations[addressIndex];
            addressBuffers[addressIndex] = new Allocation {
                bufferIndex = -2, operationIndex = -1
            };
            directlManagedAllocations[addressIndex] = default;
            if (allocation.bufferIndex < 0) {
                DeallocateShader.SetInt("operationIndex", allocation.operationIndex);
                base.ReleaseMemory(addressIndex);
            } else {
                FreeStorage(allocation.bufferIndex, directAllocation.rawStart - 1,
                    directAllocation.reservedWords);
                _AddressBuffer.Release(addressIndex);
            }
        }

        private struct PendingOperation {
            public uint addressIndex;
            public Action<ComputeBuffer> onRebind;
        }

        private struct Allocation {
            public int bufferIndex; // -2 released, -1 scratch, >= 0 long-term.
            public int operationIndex;
        }

        private struct DirectManagedAllocation {
            public int rawStart;
            public int reservedWords;
        }
    }
}
