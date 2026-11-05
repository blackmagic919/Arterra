using System;
using System.Collections.Generic;
using System.Linq;
using System.Buffers.Binary;
using Arterra.Configuration;
using Unity.Mathematics;
using UnityEngine;

namespace Arterra.Data.Biome{
    [Serializable]
    public class BiomeRegion {
        // Normalized intervals. Touching faces do not overlap; tree lookup includes both endpoints.
        public float2[] Spans;
        public string BiomeName;

        public BiomeRegion Copy() => new BiomeRegion {
            Spans = (float2[])Spans.Clone(),
            BiomeName = BiomeName
        };

        public double Volume {
            get {
                double volume = 1;
                foreach (var span in Spans) {
                    if (span.x >= span.y) return 0;
                    volume *= (double)span.y - span.x;
                }
                return volume;
            }
        }

        /// <summary>GPU record: all minimums, all maximums, then the biome index.</summary>
        public void Serialize(Catalogue<BiomeVariety> catalogue, byte[] buffer, int bufferOffset = 0, int offset = 0, int? indexOverride = null) {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            int recordSize = checked(Spans.Length * sizeof(float) * 2 + sizeof(int));
            var record = buffer.AsSpan(bufferOffset, recordSize);

            int index = -1;
            if (indexOverride.HasValue) index = indexOverride.Value;
            else if (!string.IsNullOrWhiteSpace(BiomeName)) {
                index = catalogue.RetrieveIndex(BiomeName);
                if (index < 0) throw new ArgumentException($"Unknown biome '{BiomeName}'.", nameof(catalogue));
                index = checked(index + offset);
            }

            for (int dimension = 0; dimension < Spans.Length; dimension++) {
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice(dimension * sizeof(float), sizeof(float)), math.asint(Spans[dimension].x));
                BinaryPrimitives.WriteInt32LittleEndian(record.Slice((Spans.Length + dimension) * sizeof(float), sizeof(float)), math.asint(Spans[dimension].y));
            }
            BinaryPrimitives.WriteInt32LittleEndian(record.Slice(recordSize - sizeof(int), sizeof(int)), index);
        }

        // Tree lookup retains the inclusive boundaries used by the GPU biome samplers.
        public bool Contains(float[] point) {
            if (point == null || point.Length != Spans.Length) {
                throw new ArgumentException("Point dimensions must match the biome region.", nameof(point));
            }

            for (int dimension = 0; dimension < Spans.Length; dimension++) {
                if (float.IsNaN(point[dimension]) || point[dimension] < Spans[dimension].x ||
                    point[dimension] > Spans[dimension].y) return false;
            }
            return true;
        }

        public bool Intersects(BiomeRegion other) {
            if (other == null || other.Spans.Length != Spans.Length) {
                throw new ArgumentException("Region dimensions must match.", nameof(other));
            }

            for (int dimension = 0; dimension < Spans.Length; dimension++) {
                if (Spans[dimension].x >= Spans[dimension].y ||
                    other.Spans[dimension].x >= other.Spans[dimension].y ||
                    Spans[dimension].y <= other.Spans[dimension].x ||
                    Spans[dimension].x >= other.Spans[dimension].y) return false;
            }
            return true;
        }

        public static BiomeRegion MergeBounds(BiomeRegion first, BiomeRegion second) {
            if (first.Spans.Length != second.Spans.Length) {
                throw new ArgumentException("Region dimensions must match.");
            }

            var merged = new BiomeRegion { Spans = new float2[first.Spans.Length] };
            for (int dimension = 0; dimension < merged.Spans.Length; dimension++) {
                merged.Spans[dimension] = new float2(
                    math.min(first.Spans[dimension].x, second.Spans[dimension].x),
                    math.max(first.Spans[dimension].y, second.Spans[dimension].y));
            }
            return merged;
        }

        public static BiomeRegion CreateEmpty(int dimensions) {
            var region = new BiomeRegion { Spans = new float2[dimensions] };
            for (int dimension = 0; dimension < dimensions; dimension++) {
                region.Spans[dimension] = new float2(float.MaxValue, float.MinValue);
            }
            return region;
        }

    }

    [CreateAssetMenu(menuName = "Generation/Biomes/Partition")]
    public class BiomePlacement : ScriptableObject {
        [Min(3)]
        public int numDimensions => DimensionNames.Length;
        public string[] DimensionNames = {
            "Dimension 0",
            "Dimension 1",
            "Dimension 2"
        };
        public Registry<Color> BiomeColors;
        public List<BiomeRegion> Biomes = new List<BiomeRegion>();
        public string DimensionName(int dimension) => DimensionNames != null && dimension < DimensionNames.Length && !string.IsNullOrWhiteSpace(DimensionNames[dimension]) ? DimensionNames[dimension] : $"Dimension {dimension}";
        public void InitializePartition() {
            var region = new BiomeRegion {
                Spans = new float2[numDimensions],
                BiomeName = ""
            };
            for (int d = 0; d < numDimensions; d++) {
                region.Spans[d] = new float2(0, 1);
            }

            Biomes = new List<BiomeRegion> {
                region
            };
        }

        public string ValidationError() {
            if (numDimensions < 3) {
                return "This template requires at least three dimensions.";
            }

            if (Biomes == null) {
                return "Initialize the partition first.";
            }

            foreach (var region in Biomes) {
                if (region?.Spans == null || region.Spans.Length != numDimensions) {
                    return "Region dimensions differ from the category. Restore the dimension count or initialize a new partition.";
                }

                foreach (var span in region.Spans) {
                    if (!math.all(math.isfinite(span)) || span.x < 0 || span.y > 1 || span.x >= span.y) {
                        return "Region bounds must satisfy 0 <= minimum < maximum <= 1.";
                    }
                }
            }

            for (int i = 0; i < Biomes.Count; i++) {
                for (int j = i + 1; j < Biomes.Count; j++) {
                    if (Intersects(Biomes[i], Biomes[j])) {
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
            for (int d = 0; d < a.Spans.Length; d++) {
                float lo = math.max(core.Spans[d].x, b.Spans[d].x);
                float hi = math.min(core.Spans[d].y, b.Spans[d].y);
                if (core.Spans[d].x < lo) {
                    var slab = core.Copy();
                    slab.Spans[d].y = lo;
                    result.Add(slab);
                    core.Spans[d].x = lo;
                }

                if (hi < core.Spans[d].y) {
                    var slab = core.Copy();
                    slab.Spans[d].x = hi;
                    result.Add(slab);
                    core.Spans[d].y = hi;
                }
            }

            return result;
        }

        /// <summary>Selected region wins overlap. Vacated space becomes unassigned.</summary>
        public int ReplaceRegion(int index, BiomeRegion replacement) {
            var previous = Biomes[index];
            var next = new List<BiomeRegion>();
            for (int i = 0; i < Biomes.Count; i++) {
                if (i != index) {
                    next.AddRange(Subtract(Biomes[i], replacement));
                }
            }

            foreach (var gap in Subtract(previous, replacement)) {
                gap.BiomeName = "";
                next.Add(gap);
            }

            next.Add(replacement);
            Biomes = next;
            return next.Count - 1;
        }

        public int SplitRegion(int index, int dimension, float position) {
            var left = Biomes[index];
            if (position <= left.Spans[dimension].x || position >= left.Spans[dimension].y) {
                return index;
            }

            var right = left.Copy();
            left.Spans[dimension].y = position;
            right.Spans[dimension].x = position;
            Biomes.Add(right);
            return Biomes.Count - 1;
        }
    }
}
