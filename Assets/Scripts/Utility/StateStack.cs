using System;
using System.Collections.Generic;

namespace Arterra.Utils {
    [Serializable]
    public class StateStack<TState> {
        [Serializable]
        private struct Binding {
            public int priority;
            public TState state;
            public uint sequence;
        }

        private SharedLinkedList<Binding> bindings;
        private readonly Dictionary<string, uint> index;
        private readonly TState defaultState;

        private uint topIndex;
        private uint nextSequence;

        public StateStack(TState defaultState, int initialCapacity = 8) {
            this.defaultState = defaultState;
            bindings = new SharedLinkedList<Binding>(Math.Max(initialCapacity, 1) + 1);
            index = new Dictionary<string, uint>(Math.Max(initialCapacity, 0));
            topIndex = 0;
            nextSequence = 0;
        }

        public TState CurrentState => topIndex == 0 ? defaultState : bindings.Value(topIndex).state;

        public void Set(string name, int priority, TState state) {
            if (string.IsNullOrEmpty(name)) return;
            uint sequence = ++nextSequence;

            if (index.TryGetValue(name, out uint nodeIndex)) {
                bool wasTop = nodeIndex == topIndex;

                if (bindings.Length <= 1) {
                    bindings.Remove(nodeIndex);
                    topIndex = 0;
                } else {
                    if (wasTop)
                        topIndex = bindings.Next(nodeIndex);
                    bindings.Remove(nodeIndex);
                }
            }

            Binding newBinding = new Binding {
                priority = priority,
                state = state,
                sequence = sequence,
            };

            uint inserted = InsertSorted(newBinding);
            if (inserted == 0)
                return;

            index[name] = inserted;
        }

        public bool Remove(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            if (!index.TryGetValue(name, out uint nodeIndex)) return false;

            bool wasTop = nodeIndex == topIndex;

            index.Remove(name);

            if (bindings.Length <= 1) {
                bindings.Remove(nodeIndex);
                topIndex = 0;
                return true;
            }

            // The list is maintained in descending order from topIndex, so next(top) is the next-best candidate.
            if (wasTop)
                topIndex = bindings.Next(nodeIndex);

            bindings.Remove(nodeIndex);

            return true;
        }

        private uint InsertSorted(in Binding binding) {
            if (topIndex == 0) {
                uint first = bindings.Enqueue(binding, 0);
                if (first == 0) return 0;
                topIndex = first;
                return first;
            }

            uint current = topIndex;
            do {
                if (IsHigher(binding, bindings.Value(current))) {
                    uint inserted = bindings.Enqueue(binding, current);
                    if (inserted == current) return 0;

                    if (current == topIndex)
                        topIndex = inserted;
                    return inserted;
                }

                current = bindings.Next(current);
            } while (current != topIndex);

            // Lowest priority/oldest-in-tier goes to the tail (right before top/head).
            uint tailInserted = bindings.Enqueue(binding, topIndex);
            if (tailInserted == topIndex) return 0;
            return tailInserted;
        }

        private bool IsHigher(uint aIndex, uint bIndex) {
            if (bIndex == 0) return true;
            Binding a = bindings.Value(aIndex);
            Binding b = bindings.Value(bIndex);
            return IsHigher(a, b);
        }

        private static bool IsHigher(in Binding a, in Binding b) {
            return a.priority > b.priority
                || (a.priority == b.priority && a.sequence > b.sequence);
        }

        public bool Contains(string name) {
            if (string.IsNullOrEmpty(name)) return false;
            return index.ContainsKey(name);
        }
    }
}
