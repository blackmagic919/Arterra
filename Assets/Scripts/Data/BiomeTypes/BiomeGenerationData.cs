using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Utils;

namespace WorldConfig.Generation.Biome{
/// <summary>
/// A settings container identifying all biomes and biome related settings 
/// that are used during generation. Biomes are placed depending on conditions 
/// describing the shape of the terrain <see cref="IBiomeCondition"/> and control 
/// aspects of the world during structure, entity, and material generation.
/// </summary> <remarks><b> 
/// In the case that the sample space is not fully covered by the condition bounds defined within
/// a Biome registry, the first biome within the registry will be placed if no biome can be resolved </b></remarks> 
[CreateAssetMenu(menuName = "Generation/Biomes/BiomeGenerationData")]
public class Generation : ScriptableObject
{
    /// <summary> The registry containing all surface biomes that can be generated.
    /// Surface biomes have their unique selection space and only need to not overlap
    /// other surface biomes within <see cref="SurfaceBiomes"/>. <seealso cref="SBiomeInfo"/> </summary> 
    [SerializeField]
    public Registry<SBiomeInfo> SurfaceBiomes;
    /// <summary>
    /// The registry containing all cave biomes that can be generated.
    /// Cave biomes generate underneath the surface of the world and have their unique
    /// selection space. Cave biomes only need to not overlap other cave biomes within
    /// <see cref="CaveBiomes"/>. <seealso cref="CBiomeInfo"/>
    /// </summary> <remarks> Cave biomes can be used to create different cave formations, mineral placement,
    /// or underground biomes independent of what the surface biome is. </remarks>
    [SerializeField]
    public Registry<CBiomeInfo> CaveBiomes;
    /// <summary>
    /// The registry containing all sky biomes that can be generated.
    /// Sky biomes generate above the surface of the world and have their unique sample
    /// space. Sky biomes only need to not overlap other sky biomes within <see cref="SkyBiomes"/>.
    /// </summary>  <remarks> Sky biomes can be used to create different cloud patterns, weather or effects in the 
    /// sky independently of what the surface biome is. </remarks>
    [SerializeField]
    public Registry<CBiomeInfo> SkyBiomes;
}

/// <summary>
/// The decision matrix constructed from a biome registry that facilitates lookup of biomes.
/// More exactly, it is an R-Tree containing biomes within its leaf nodes that allows a 
/// biome to be matched with a specific set of conditions in O(log(n)) time where n is
/// the number of biomes within the registry. 
/// </summary>
public class BDict
{
    private RNode _rTree;
    private int leafSize;

    /// <summary> Constructs a decision matrix from a list of biome conditions. </summary>
    /// <typeparam name="TCond">The type of biome condition used in the decision matrix. </typeparam>
    /// <param name="biomes">The registry's biomes that define the leaf nodes of the decision matrix </param>
    /// <param name="offset">The offset of biome indices if the biomes are not zero-indexed relative to the registry</param>
    /// <returns>The constructed decision matrix</returns>
    public static BDict Create<TCond>(CInfo<TCond>[] biomes, int offset = 0) where TCond : IBiomeCondition
    {
        BDict nDict = new BDict();
        List<RNode> leaves = nDict.InitializeBiomeRegions(biomes, offset);
        nDict._rTree = nDict.ConstructRTree(leaves, biomes[0].BiomeConditions.value.GetDimensions());
        nDict.leafSize = leaves.Count;
        return nDict;
    }

    /// <summary>
    /// Obtains a flattened array of biome conditions that represents the decision matrix. Normally the decision matrix
    /// is a reference tree splayed across memory. This method collapses the tree into a single array using binary
    /// tree indexing to determine hierarchical relations. The size of this flattened tree is equal to 
    /// 2^(h+1) - 1 where h is the next highest power of 2 of the number of leaf nodes in the tree.
    /// </summary>
    /// <typeparam name="TCond">The type of bioem condition used to flatten the tree. This should be the same as
    /// was used when <see cref="Create">creating</see> the tree. </typeparam>
    /// <returns>The flattened array of biome conditions representing the decision matrix</returns>
    public TCond[] FlattenTree<TCond>() where TCond : IBiomeCondition
    {
        int treeSize = math.ceilpow2(math.max(leafSize, 2)) * 2 - 1;
        TCond[] flattenedNodes = new TCond[treeSize];
        Queue<RNode> treeNodes = new Queue<RNode>();
        treeNodes.Enqueue(_rTree);

        for(int i = 0; i < treeSize; i++){
            RNode cur = treeNodes.Dequeue();
            cur ??= new RNode{
                bounds = RegionBound.GetNoSpace(flattenedNodes[i].GetDimensions()),
                childOne = null,
                childTwo = null,
            };

            flattenedNodes[i].SetNode(cur.bounds, cur.biome);
            treeNodes.Enqueue(cur.childOne);
            treeNodes.Enqueue(cur.childTwo);
        }

        return flattenedNodes;
        //Based on the nature of queues, it will be filled exactly in order of the array
    }

    /// <summary>
    /// Queries the R-Tree for the biome that matches the given parameters. The parameters
    /// must be given in the same order that they are <see cref="IBiomeCondition.SetNode(RegionBound, int)">
    /// provided</see> when constructing the R-Tree.
    /// </summary> <param name="point">A list of parameters pertaining to each respective condition</param>
    /// <returns>The index within the registry used to construct the decision matrix of the biome. -1 if 
    /// no biome is able to be found. </returns>
    public int Query(float[] point)
    {
        RNode node = QueryNode(_rTree, ref point);
        if (node == null)
            return -1;
        return node.biome;
    }

    RNode QueryNode(RNode node, ref float[] point)
    {
        RNode OUT = null;
        if (node.IsLeaf)
            return node;

        if (node.childOne != null && node.childOne.bounds.Contains(ref point))
            OUT = QueryNode(node.childOne, ref point);
        if (OUT == null && node.childTwo != null && node.childTwo.bounds.Contains(ref point))
            OUT = QueryNode(node.childTwo, ref point);

        return OUT;
    }

    RNode ConstructRTree(List<RNode> nodes, int dimensions)
    {
        int nodesInLayer = nodes.Count;
        HashSet<int> linked = new HashSet<int>();
        List<RNode> ret = new List<RNode>();

        nodes.Sort((RNode a, RNode b) => a.bounds.area.CompareTo(b.bounds.area));

        for (int i = 0; i < nodesInLayer; i++)
        {
            if (linked.Contains(i))
                continue;

            RNode node = new RNode();
            node.bounds = RegionBound.GetAllSpace(dimensions);
            node.childOne = nodes[i];
            node.childTwo = null;

            for (int u = i + 1; u < nodesInLayer; u++){
                RNode matchNode = nodes[u];
                if (linked.Contains(u))
                    continue;

                RegionBound newRegion = RegionBound.mergeRegion(node.childOne.bounds, matchNode.bounds, dimensions);
                if (newRegion.area <= node.bounds.area){
                    node.bounds = newRegion;
                    node.childTwo = matchNode;
                    node.biome = u;
                }
            }
            if (node.childTwo == null) node.bounds = node.childOne.bounds;
            linked.Add(node.biome);
            node.biome = -1;
            ret.Add(node);
        }

        if (ret.Count == 1)
            return ret[0];
        return ConstructRTree(ret, dimensions);
    }

    List<RNode> InitializeBiomeRegions<TCond>(CInfo<TCond>[] biomes, int offset) where TCond : IBiomeCondition
    {
        int numOfBiomes = biomes.Length;
        List<RNode> biomeRegions = new List<RNode>();
        for (int i = 0; i < numOfBiomes; i++)
        {
            int dimensions = biomes[i].BiomeConditions.value.GetDimensions();
            RegionBound bounds = new RegionBound(dimensions);
            biomes[i].BiomeConditions.value.GetBoundDimension(ref bounds);
            bounds.CalculateArea();

            for (int u = i - 1; u >= 0; u--)
            {
                if (bounds.RegionIntersects(biomeRegions[u].bounds, dimensions))
                    throw new ArgumentException($"Biome {biomes[i].name}'s generation intersects with {biomes[u].name}");
            }

            biomeRegions.Add(new RNode(bounds, i + offset));
        }

        return biomeRegions;
    }

    private class RNode
    {
        public RegionBound bounds;
        public RNode childOne; //compiles both leaf and branch nodes
        public RNode childTwo;
        public int biome;
        public bool IsLeaf => biome != -1;
        public RNode(RegionBound bounds, int biome)
        {
            this.bounds = bounds;
            this.biome = biome;
        }
        public RNode()
        {
            bounds = new RegionBound(0);
            biome = -1;
        }
    }

    /// <summary>
    /// A region bound that describes a list of ranges, each representing a seperate
    /// dimension in the sample space. It is a more generalized form of representing
    /// conditions of a <see cref="IBiomeCondition"> concrete biome type </see> that
    /// allows for the construction of Decision Matricies of varying dimensions.
    /// </summary>
    public struct RegionBound
    {
        /// <summary>
        /// The maximum bound of each dimension for the region within sample space.
        /// Equaivalently, the coordinate of the maximum corner of the cuboid region.
        /// </summary>
        public float[] maxCorner;
        /// <summary>
        /// The minimum bound of each dimension for the region within sample space.
        /// Equaivalently, the coordinate of the minimum corner of the cuboid region.
        /// </summary>
        public float[] minCorner;
        internal double area;

        internal RegionBound(int dimensions)
        {
            maxCorner = new float[dimensions];
            minCorner = new float[dimensions];
            area = -1;
        }

        internal void SetBoundDimension(int dimension, float min, float max)
        {
            minCorner[dimension] = min;
            maxCorner[dimension] = max;
        }

        internal void CalculateArea()
        {
            area = 1;
            for (int i = 0; i < maxCorner.Length; i++)
            {
                area *= maxCorner[i] - minCorner[i];
            }
        }

        internal bool Contains(ref float[] point)
        {
            for (int i = 0; i < maxCorner.Length; i++)
            {
                if (point[i] < minCorner[i] || point[i] > maxCorner[i])
                    return false;
            }
            return true;
        }

        internal static RegionBound mergeRegion(RegionBound a, RegionBound b, int dimensions)
        {
            RegionBound ret = new RegionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
            {
                ret.maxCorner[i] = Mathf.Max(a.maxCorner[i], b.maxCorner[i]);
                ret.minCorner[i] = Mathf.Min(a.minCorner[i], b.minCorner[i]);
            }
            ret.CalculateArea();
            return ret;
        }

        internal static RegionBound GetAllSpace(int dimensions)
        {
            RegionBound ret = new RegionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MinValue, float.MaxValue);
            ret.area = float.MaxValue;
            return ret;
        }

        internal static RegionBound GetNoSpace(int dimensions)
        {
            RegionBound ret = new RegionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MaxValue, float.MinValue);
            ret.area = 0;
            return ret;
        }

        internal bool RegionIntersects(RegionBound b, int dimensions)
        {
            for (int i = 0; i < dimensions; i++)
            {
                if (maxCorner[i] <= b.minCorner[i] || minCorner[i] >= b.maxCorner[i])
                    return false;
            }
            return true;
        }

    }
}}