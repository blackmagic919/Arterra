using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Arterra.Configuration;

namespace Arterra.Data.Biome{
/// <summary>
/// A settings container identifying all biomes and biome related settings
/// that are used during generation. Biomes are placed depending on conditions
/// describing the shape of the terrain <see cref="BiomeRegion"/> and control
/// aspects of the world during structure, entity, and material generation.
/// </summary> <remarks><b>
/// In the case that the sample space is not fully covered by the condition bounds defined within
/// a Biome registry, the first biome within the registry will be placed if no biome can be resolved </b></remarks>
[CreateAssetMenu(menuName = "Generation/Biomes/BiomeGenerationData")]
public class Generation : ScriptableObject
{
    /// <summary> The registry containing all surface biomes that can be generated.
    /// Surface biomes have their unique selection space and only need to not overlap
    /// other surface biomes within <see cref="SurfaceBiomes"/>. </summary>
    [SerializeField]
    public BiomeSelection SurfaceBiomes;
    /// <summary> The registry containing all seafloor biomes that can be generated.
    /// Seafloor biomes are biomes that generate on the seafloor and can pick where to generate
    /// based on the features of the seafloor. Importantly, seafloor biomes may not span all query space,
    /// and if no seafloor biome is selected, the biome is instead decided by the <see cref="SeaBiomes"/>. </summary>
    [SerializeField]
    public BiomeSelection SeafloorBiomes;
    /// <summary>
    /// The registry containing all cave biomes that can be generated.
    /// Cave biomes generate underneath the surface of the world and have their unique
    /// selection space. Cave biomes only need to not overlap other cave biomes within
    /// <see cref="CaveBiomes"/>.
    /// </summary> <remarks> Cave biomes can be used to create different cave formations, mineral placement,
    /// or underground biomes independent of what the surface biome is. </remarks>
    [SerializeField]
    public BiomeSelection CaveBiomes;
    /// <summary>
    /// The registry containing all sky biomes that can be generated.
    /// Sky biomes generate above the surface of the world and have their unique sample
    /// space. Sky biomes only need to not overlap other sky biomes within <see cref="SkyBiomes"/>.
    /// </summary>  <remarks> Sky biomes can be used to create different cloud patterns, weather or effects in the
    /// sky independently of what the surface biome is. </remarks>
    [SerializeField]
    public BiomeSelection SkyBiomes;
    /// <summary> The registry containing all sky biomes that can be generated.
    /// Sea biomes generate only between an underwater seafloor and the ocean's
    /// water surface. Seabiomes may be overwritten by <see cref="SeafloorBiomes"/>
    /// and only need to not overlap with other sea biomes </summary>
    [SerializeField]
    public BiomeSelection SeaBiomes;
}

/// <summary>A spatial lookup tree built from resolved biome placement regions.</summary>
public class BDict {
    private RNode root;
    private int dimensions;
    private int leafCount;

    /// <summary>Builds a tree of named regions; catalogue indices are resolved during serialization.</summary>
    public static BDict Create(BiomePlacement placement) {
        if (placement == null) throw new ArgumentNullException(nameof(placement));
        string error = placement.ValidationError();
        if (error != null) throw new ArgumentException(error, nameof(placement));

        var dictionary = new BDict { dimensions = placement.numDimensions };
        var leaves = new List<RNode>();
        foreach (var source in placement.Biomes) {
            if (string.IsNullOrWhiteSpace(source.BiomeName)) continue;
            var region = source.Copy();
            leaves.Add(new RNode { Region = region });
        }

        dictionary.leafCount = leaves.Count;
        dictionary.root = dictionary.BuildTree(leaves);
        return dictionary;
    }

    /// <summary>
    /// Breadth-first heap layout: children of index i are 2*i+1 and 2*i+2.
    /// Branches and empty slots have no biome name. Every entry owns its span array.
    /// </summary>
    public List<BiomeRegion> FlattenTree() {
        int treeSize = checked(math.ceilpow2(math.max(leafCount, 2)) * 2 - 1);
        var flattened = new List<BiomeRegion>(treeSize);
        var pending = new Queue<RNode>();
        pending.Enqueue(root);

        for (int index = 0; index < treeSize; index++) {
            var node = pending.Dequeue();
            flattened.Add(node == null ? BiomeRegion.CreateEmpty(dimensions) : node.Region.Copy());
            pending.Enqueue(node?.FirstChild);
            pending.Enqueue(node?.SecondChild);
        }
        return flattened;
    }

    public string Query(float[] point) {
        if (point == null || point.Length != dimensions) {
            throw new ArgumentException("Point dimensions must match the tree.", nameof(point));
        }
        return QueryNode(root, point);
    }

    private static string QueryNode(RNode node, float[] point) {
        if (node == null || !node.Region.Contains(point)) return null;
        if (!string.IsNullOrWhiteSpace(node.Region.BiomeName)) return node.Region.BiomeName;

        string biome = QueryNode(node.FirstChild, point);
        return biome ?? QueryNode(node.SecondChild, point);
    }

    private RNode BuildTree(List<RNode> nodes) {
        if (nodes.Count == 0) return null;

        // Keep a branch at the root even for a single region: GPU upload stores fallback metadata there.
        do {
            nodes = nodes.OrderBy(node => node.Region.Volume).ToList();
            var paired = new bool[nodes.Count];
            var parents = new List<RNode>();

            for (int index = 0; index < nodes.Count; index++) {
                if (paired[index]) continue;
                paired[index] = true;
                var first = nodes[index];
                int partner = -1;
                BiomeRegion bounds = null;
                double smallestVolume = double.PositiveInfinity;

                for (int candidate = index + 1; candidate < nodes.Count; candidate++) {
                    if (paired[candidate]) continue;
                    var merged = BiomeRegion.MergeBounds(first.Region, nodes[candidate].Region);
                    if (merged.Volume <= smallestVolume) {
                        partner = candidate;
                        bounds = merged;
                        smallestVolume = merged.Volume;
                    }
                }

                if (partner >= 0) paired[partner] = true;
                bounds ??= first.Region.Copy();
                bounds.BiomeName = null;
                parents.Add(new RNode {
                    Region = bounds,
                    FirstChild = first,
                    SecondChild = partner >= 0 ? nodes[partner] : null
                });
            }
            nodes = parents;
        } while (nodes.Count > 1);

        return nodes[0];
    }

    private class RNode {
        public BiomeRegion Region;
        public RNode FirstChild;
        public RNode SecondChild;
    }
}
}
