using System;
using System.Collections.Generic;
using UnityEngine;

namespace Arterra.Configuration.Quality {
    sealed class BufferAllocation {
        public readonly ComputeBuffer storage;
        private readonly List<FreeRange> freeHeap = new();
        // These two dictionaries form the dynamic linked list.
        private readonly Dictionary<int, FreeRange> freeByStart = new();
        private readonly Dictionary<int, FreeRange> freeByEnd = new();
        public int liveCount;
        const int BLOCK_META_SIZE = 1; // Each block stores size (some existing readers depend on it)
        public int MaxFree => freeHeap.Count == 0 ? 0 : freeHeap[0].length;

        public BufferAllocation(int size) {
            storage = new ComputeBuffer(size, sizeof(uint),
                ComputeBufferType.Structured, ComputeBufferMode.Immutable);
            AddFree(new FreeRange(0, size));
        }

        public bool CanFit(int words, int stride) {
            if (words <= 0 || stride <= 0 || freeHeap.Count == 0) return false;
            // Reserve worst-case alignment. Any range this large fits in O(1),
            // and selection/removal from the max heap is O(log n).
            long needed = (long)words + stride;
            return needed <= freeHeap[0].length;
        }

        public bool Allocate(int words, int stride, out int rawStart,
            out int alignedStart, out int reservedWords) {
            rawStart = alignedStart = reservedWords = 0;
            if (!CanFit(words, stride)) return false;
            FreeRange range = freeHeap[0];
            rawStart = range.start + BLOCK_META_SIZE; 
            int padding = (stride - rawStart % stride) % stride;
            alignedStart = rawStart + padding;
            reservedWords = words + stride;
            int remainder = range.length - reservedWords;
            RemoveFree(range);
            if (remainder > 0)
                AddFree(new FreeRange(range.start + reservedWords, remainder));
            liveCount++;
            return true;
        }

        public void Free(int start, int length) {
            if (freeByEnd.TryGetValue(start, out FreeRange left)) {
                start = left.start;
                length += left.length;
                RemoveFree(left);
            }
            if (freeByStart.TryGetValue(start + length, out FreeRange right)) {
                length += right.length;
                RemoveFree(right);
            }
            AddFree(new FreeRange(start, length));
            liveCount--;
        }

        private void AddFree(FreeRange range) {
            range.heapIndex = freeHeap.Count;
            freeHeap.Add(range);
            freeByStart.Add(range.start, range);
            freeByEnd.Add(range.start + range.length, range);
            Swim(range.heapIndex);
        }

        private void RemoveFree(FreeRange range) {
            freeByStart.Remove(range.start);
            freeByEnd.Remove(range.start + range.length);
            int index = range.heapIndex;
            int last = freeHeap.Count - 1;
            if (index != last) {
                freeHeap[index] = freeHeap[last];
                freeHeap[index].heapIndex = index;
            }
            freeHeap.RemoveAt(last);
            if (index >= freeHeap.Count) return;
            if (index > 0 && freeHeap[index].length > freeHeap[(index - 1) / 2].length)
                Swim(index);
            else Sink(index);
        }

        private void Swap(int a, int b) {
            (freeHeap[a], freeHeap[b]) = (freeHeap[b], freeHeap[a]);
            freeHeap[a].heapIndex = a;
            freeHeap[b].heapIndex = b;
        }

        private void Swim(int index) {
            while (index > 0) {
                int parent = (index - 1) / 2;
                if (freeHeap[parent].length >= freeHeap[index].length) break;
                Swap(index, parent);
                index = parent;
            }
        }

        private void Sink(int index) {
            while (index * 2 + 1 < freeHeap.Count) {
                int child = index * 2 + 1;
                if (child + 1 < freeHeap.Count &&
                    freeHeap[child + 1].length > freeHeap[child].length) child++;
                if (freeHeap[index].length >= freeHeap[child].length) break;
                Swap(index, child);
                index = child;
            }
        }

        public void Release() => storage.Release();

        private sealed class FreeRange {
            public int start;
            public int length;
            public int heapIndex;

            public FreeRange(int start, int length) {
                this.start = start;
                this.length = length;
            }
        }
    }
}
