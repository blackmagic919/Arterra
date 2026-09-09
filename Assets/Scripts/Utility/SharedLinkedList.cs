using System;

namespace Arterra.Utils {
    [Serializable]
    public struct SharedLinkedList<T> {
        public LListNode[] array;
        private int _length;
        public readonly int Length => _length;

        public SharedLinkedList(int length) {
            int totalNodes = Math.Max(length + 1, 2);
            array = new LListNode[totalNodes];
            array[0].next = 1;
            for (uint i = 1; i < totalNodes - 1; i++) {
                array[i].next = i + 1;
            }
            array[totalNodes - 1].next = 0;
            _length = 0;
        }

        public uint Enqueue(T node, uint head = 0) {
            if (array[0].next == 0)
                Grow();

            uint freeNode = array[0].next;
            if (freeNode == 0)
                return head;
            array[0].next = array[freeNode].next;

            array[freeNode].value = node;
            if (head == 0) {
                array[freeNode].next = freeNode;
                array[freeNode].previous = freeNode;
            } else {
                uint tailNode = array[head].previous;
                array[tailNode].next = freeNode;
                array[head].previous = freeNode;
                array[freeNode].previous = tailNode;
                array[freeNode].next = head;
            }

            _length++;
            return freeNode;
        }

        private void Grow() {
            int oldLength = array.Length;
            if (oldLength >= int.MaxValue / 2)
                throw new InvalidOperationException("SharedLinkedList exceeded max supported size.");

            int newLength = oldLength * 2;
            Array.Resize(ref array, newLength);

            uint oldFreeHead = array[0].next;
            uint start = (uint)oldLength;
            uint end = (uint)newLength;

            for (uint i = start; i < end - 1; i++) {
                array[i].next = i + 1;
            }
            array[end - 1].next = oldFreeHead;
            array[0].next = start;
        }

        public void Remove(uint index) {
            uint nextNode = array[index].next;
            uint prevNode = array[index].previous;
            array[prevNode].next = nextNode;
            array[nextNode].previous = prevNode;

            array[index].next = array[0].next;
            array[0].next = index;
            _length--;
        }

        public readonly T Value(uint index) {
            return array[index].value;
        }

        public readonly ref T RefVal(uint index) {
            return ref array[index].value;
        }

        public readonly uint Next(uint index) {
            return array[index].next;
        }

        public readonly uint Previous(uint index) {
            return array[index].previous;
        }

        [Serializable]
        public struct LListNode {
            public uint previous;
            public uint next;
            public T value;
        }
    }
}
