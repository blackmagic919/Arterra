using System;
using System.Collections.Generic;
using System.Linq;
using Arterra.Configuration;
using Arterra.Editor;
using Unity.Mathematics;

namespace Arterra.Utils {

    public interface IRangeBlock {
        public TagOrRegistryReference selection{get; set;}
        public Policy policy {get; set;}
        public enum Policy {
            Include, Exclude,
            /// <summary>Include everything NOT covered by this tag/entry.</summary>
            IncludeNot,
            /// <summary>Exclude everything NOT covered by this tag/entry.</summary>
            ExcludeNot,
        } 
    }


    [Serializable]
    public class RangeSet<T> : BaseRangeSet<T> where T : IRangeBlock {
        public void Construct(ICatalgoue catalogue) => base.Construct(catalogue);
        public bool IsAllowListed(int entry, out int preference) => base.IsAllowListed(entry, out preference); 
    }

    [Serializable]
    public class RangeMap<T> : BaseRangeSet<T> where T : IRangeBlock {
        public void Construct(ICatalgoue catalogue) {
            base.Construct(catalogue);
        }

        public bool TryGetInfo(int entry, out T info) {
            info = default(T);
            if (!base.IsAllowListed(entry, out int index))
                return false;
            if (index < 0 || index >= AllowList.value.Count)
                return false;
            info = AllowList.value[index];
            return true;
        }

        public bool IsAllowListed(int entry, out int preference) => base.IsAllowListed(entry, out preference); 
    }
    
    [Serializable]
    public abstract class BaseRangeSet<T> where T : IRangeBlock  {
        public Option<List<T>> AllowList;
        // x -> registry index, y -> preference (index in AllowList)
        // -1 if marking not included
        private List<int2> allowRanges;

        private struct Interval {
            public int start;
            public int end;
            public int priority;
        }

        protected void Construct(ICatalgoue catalgoue) {
            allowRanges = new List<int2>();
            List<T> blocks = AllowList.value;
            if (catalgoue == null || blocks == null || blocks.Count == 0) return;

            var intervals = new List<Interval>();
            for (int i = 0; i < blocks.Count; i++) {
                IRangeBlock block = blocks[i];
                bool isNot = block.policy == IRangeBlock.Policy.IncludeNot || block.policy == IRangeBlock.Policy.ExcludeNot;
                if (block.selection.IsTag) {
                    if (catalgoue.TagRanges == null) continue;
                    if (!catalgoue.TagRanges.TryGetValue(block.selection.tagValue, out LinkedList<int2> tagRanges) || tagRanges == null) continue;
                    if (!isNot) {
                        foreach (int2 range in tagRanges)
                            if (range.y > range.x)
                                intervals.Add(new Interval { start = range.x, end = range.y, priority = i });
                    } else {
                        var sorted = tagRanges.OrderBy(r => r.x).ToList();
                        int prev = 0;
                        foreach (int2 range in sorted) {
                            if (prev < range.x) intervals.Add(new Interval { start = prev, end = range.x, priority = i });
                            prev = math.max(prev, range.y);
                        }
                        intervals.Add(new Interval { start = prev, end = int.MaxValue, priority = i });
                    }
                } else {
                    string registryName = block.selection.registryValue;
                    if (string.IsNullOrEmpty(registryName) || !catalgoue.Contains(registryName)) continue;
                    int index = catalgoue.RetrieveIndex(registryName);
                    if (!isNot) {
                        intervals.Add(new Interval { start = index, end = index + 1, priority = i });
                    } else {
                        if (index > 0) intervals.Add(new Interval { start = 0, end = index, priority = i });
                        intervals.Add(new Interval { start = index + 1, end = int.MaxValue, priority = i });
                    }
                }
            }

            if (intervals.Count == 0) return;

            var boundaries = new SortedSet<int>();
            foreach (var iv in intervals) { boundaries.Add(iv.start); boundaries.Add(iv.end); }

            int[] activeCounts = new int[blocks.Count];
            int lastPreference = -1;

            foreach (int boundary in boundaries) {
                // End events before start events to maintain half-open [start, end) semantics
                foreach (var iv in intervals)
                    if (iv.end == boundary) activeCounts[iv.priority]--;
                foreach (var iv in intervals)
                    if (iv.start == boundary) activeCounts[iv.priority]++;

                // Find the highest-priority (lowest index) active block
                int currentPreference = -1;
                for (int p = 0; p < blocks.Count; p++) {
                    if (activeCounts[p] > 0) {
                        bool isInclude = blocks[p].policy == IRangeBlock.Policy.Include || blocks[p].policy == IRangeBlock.Policy.IncludeNot;
                        currentPreference = isInclude ? p : -1;
                        break;
                    }
                }

                if (currentPreference == lastPreference) continue;
                allowRanges.Add(new int2(boundary, currentPreference));
                lastPreference = currentPreference;
            }

            if (allowRanges.Count > 0 && allowRanges[0].y == -1)
                allowRanges.RemoveAt(0);
        }

        protected bool IsAllowListed(int entry, out int preference) {
            preference = -1;
            if (entry < 0 || allowRanges == null || allowRanges.Count == 0)
                return false;

            int left = 0;
            int right = allowRanges.Count - 1;
            int bestIndex = -1;

            while (left <= right) {
                int mid = left + ((right - left) >> 1);
                if (allowRanges[mid].x <= entry) {
                    bestIndex = mid;
                    left = mid + 1;
                } else {
                    right = mid - 1;
                }
            }

            if (bestIndex < 0)
                return false;

            preference = allowRanges[bestIndex].y;
            return preference >= 0;
        }
    }
}