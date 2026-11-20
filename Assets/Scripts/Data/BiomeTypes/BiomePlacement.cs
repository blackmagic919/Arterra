using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Biome{
    [Serializable]
    public class BiomeRegion : ICloneable {
        // Normalized intervals. Touching faces do not overlap; tree lookup includes both endpoints.
        public Option<List<float2>> Spans;
        public string BiomeName;

        public BiomeRegion Copy() => new BiomeRegion {
            Spans = new List<float2>(Spans.value),
            BiomeName = BiomeName
        };

        public object Clone() => new BiomeRegion {
            Spans = Spans,
            BiomeName = BiomeName
        };

        public double Volume {
            get {
                double volume = 1;
                foreach (var span in Spans.value) {
                    if (span.x >= span.y) return 0;
                    volume *= (double)span.y - span.x;
                }
                return volume;
            }
        }

        /// <summary>GPU record: all minimums, all maximums, then the biome index.</summary>
        public void Serialize(Catalogue<BiomeVariety> catalogue, byte[] buffer, int bufferOffset = 0, int offset = 0, int? indexOverride = null) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int recordSize = checked(Spans.value.Count * sizeof(float) * 2 + sizeof(int));
            var record = buffer.AsSpan(bufferOffset, recordSize);

            int index = -1;
            if (indexOverride.HasValue) index = indexOverride.Value;
            else if (!string.IsNullOrWhiteSpace(BiomeName)) {
                index = catalogue.RetrieveIndex(BiomeName);
                if (index < 0) throw new ArgumentException($"Unknown biome '{BiomeName}'.", nameof(catalogue));
                index = checked(index + offset);
            }

            for (int dimension = 0; dimension < Spans.value.Count; dimension++) {
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice(dimension * sizeof(float), sizeof(float)), math.asint(Spans.value[dimension].x));
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice((Spans.value.Count + dimension) * sizeof(float), sizeof(float)), math.asint(Spans.value[dimension].y));
            }
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(recordSize - sizeof(int), sizeof(int)), index);
        }

        // Tree lookup retains the inclusive boundaries used by the GPU biome samplers.
        public bool Contains(float[] point) {
            if (point == null || point.Length != Spans.value.Count) {
                throw new ArgumentException("Point dimensions must match the biome region.", nameof(point));
            }

            for (int dimension = 0; dimension < Spans.value.Count; dimension++) {
                if (float.IsNaN(point[dimension]) || point[dimension] < Spans.value[dimension].x ||
                    point[dimension] > Spans.value[dimension].y) return false;
            }
            return true;
        }

        public bool Intersects(BiomeRegion other) {
            if (other == null || other.Spans.value.Count != Spans.value.Count) {
                throw new ArgumentException("Region dimensions must match.", nameof(other));
            }

            for (int dimension = 0; dimension < Spans.value.Count; dimension++) {
                if (Spans.value[dimension].x >= Spans.value[dimension].y ||
                    other.Spans.value[dimension].x >= other.Spans.value[dimension].y ||
                    Spans.value[dimension].y <= other.Spans.value[dimension].x ||
                    Spans.value[dimension].x >= other.Spans.value[dimension].y) return false;
            }
            return true;
        }

        public static BiomeRegion MergeBounds(BiomeRegion first, BiomeRegion second) {
            if (first.Spans.value.Count != second.Spans.value.Count) {
                throw new ArgumentException("Region dimensions must match.");
            }

            var merged = new BiomeRegion { Spans = new List<float2>(new float2[first.Spans.value.Count]) };
            for (int dimension = 0; dimension < merged.Spans.value.Count; dimension++) {
                merged.Spans.value[dimension] = new float2(
                    math.min(first.Spans.value[dimension].x, second.Spans.value[dimension].x),
                    math.max(first.Spans.value[dimension].y, second.Spans.value[dimension].y));
            }
            return merged;
        }

        public static BiomeRegion CreateEmpty(int dimensions) {
            var region = new BiomeRegion { Spans = new List<float2>(new float2[dimensions]) };
            for (int dimension = 0; dimension < dimensions; dimension++) {
                region.Spans.value[dimension] = new float2(float.MaxValue, float.MinValue);
            }
            return region;
        }

    }

    [CreateAssetMenu(menuName = "Generation/Biomes/Partition")]
    public class BiomePlacement : ScriptableObject {
        [Min(3)]
        public int numDimensions => DimensionNames.value.Count;
        public Option<List<string>> DimensionNames = new List<string> {
            "Dimension 0",
            "Dimension 1",
            "Dimension 2"
        };
        public Registry<Color> BiomeColors;
        public Option<List<BiomeRegion>> Biomes = new List<BiomeRegion>();
        public string DimensionName(int dimension) => DimensionNames.value != null && dimension < DimensionNames.value.Count && !string.IsNullOrWhiteSpace(DimensionNames.value[dimension]) ? DimensionNames.value[dimension] : $"Dimension {dimension}";
        public void InitializePartition() {
            var region = new BiomeRegion {
                Spans = new List<float2>(new float2[numDimensions]),
                BiomeName = ""
            };
            for (int d = 0; d < numDimensions; d++) {
                region.Spans.value[d] = new float2(0, 1);
            }

            Biomes.value = new List<BiomeRegion> {
                region
            };
        }

        public string ValidationError() {
            if (numDimensions < 3) {
                return "This template requires at least three dimensions.";
            }

            if (Biomes.value == null) {
                return "Initialize the partition first.";
            }

            foreach (var region in Biomes.value) {
                if (region?.Spans.value == null || region.Spans.value.Count != numDimensions) {
                    return "Region dimensions differ from the category. Restore the dimension count or initialize a new partition.";
                }

                foreach (var span in region.Spans.value) {
                    if (!math.all(math.isfinite(span)) || span.x < 0 || span.y > 1 || span.x >= span.y) {
                        return "Region bounds must satisfy 0 <= minimum < maximum <= 1.";
                    }
                }
            }

            for (int i = 0; i < Biomes.value.Count; i++) {
                for (int j = i + 1; j < Biomes.value.Count; j++) {
                    if (Intersects(Biomes.value[i], Biomes.value[j])) {
                        return "Regions overlap. Restore a valid partition before editing.";
                    }
                }
            }

            return null;
        }

        public static bool Intersects(BiomeRegion a, BiomeRegion b) => a.Intersects(b);

        /// <summary>A minus B, as at most 2*N disjoint slabs; preserves A's biome.</summary>
        public static List<BiomeRegion> Subtract(BiomeRegion a, BiomeRegion b) {
            var result = new List<BiomeRegion>();
            if (!Intersects(a, b)) {
                result.Add(a.Copy());
                return result;
            }

            var core = a.Copy();
            for (int d = 0; d < a.Spans.value.Count; d++) {
                float lo = math.max(core.Spans.value[d].x, b.Spans.value[d].x);
                float hi = math.min(core.Spans.value[d].y, b.Spans.value[d].y);
                if (core.Spans.value[d].x < lo) {
                    var slab = core.Copy();
                    slab.Spans.value[d] = new float2(slab.Spans.value[d].x, lo);
                    result.Add(slab);
                    core.Spans.value[d] = new float2(lo, core.Spans.value[d].y);
                }

                if (hi < core.Spans.value[d].y) {
                    var slab = core.Copy();
                    slab.Spans.value[d] = new float2(hi, slab.Spans.value[d].y);
                    result.Add(slab);
                    core.Spans.value[d] = new float2(core.Spans.value[d].x, hi);
                }
            }

            return result;
        }

        /// <summary>Selected region wins overlap. Vacated space becomes unassigned.</summary>
        public int ReplaceRegion(int index, BiomeRegion replacement) {
            var previous = Biomes.value[index];
            var next = new List<BiomeRegion>();
            for (int i = 0; i < Biomes.value.Count; i++) {
                if (i != index) {
                    next.AddRange(Subtract(Biomes.value[i], replacement));
                }
            }

            foreach (var gap in Subtract(previous, replacement)) {
                gap.BiomeName = "";
                next.Add(gap);
            }

            next.Add(replacement);
            Biomes.value = next;
            return next.Count - 1;
        }

        public int SplitRegion(int index, int dimension, float position) {
            var left = Biomes.value[index];
            if (position <= left.Spans.value[dimension].x || position >= left.Spans.value[dimension].y) {
                return index;
            }

            var right = left.Copy();
            left.Spans.value[dimension] = new float2(left.Spans.value[dimension].x, position);
            right.Spans.value[dimension] = new float2(position, right.Spans.value[dimension].y);
            Biomes.value.Add(right);
            return Biomes.value.Count - 1;
        }
    }
}
