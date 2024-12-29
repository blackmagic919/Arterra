using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Utils;

namespace Biome{
[CreateAssetMenu(menuName = "Generation/Biomes/BiomeGenerationData")]
public class GenerationData : ScriptableObject
{
    [SerializeField]
    public Registry<SBiomeInfo> SurfaceBiomes;
    [SerializeField]
    public Registry<CBiomeInfo> CaveBiomes;
    [SerializeField]
    public Registry<CBiomeInfo> SkyBiomes;
    [UISetting(Message = "Describes How Dense & Large Structures Generate At the Cost of Performance")]
    public int StructureChecksPerChunk;
    [Range(1, 5)]
    public float LoDFalloff;
    public int maxLoD;
}



public class BDict
{
    public RNode _rTree;

    public int Query(float[] point)
    {
        LeafNode node = QueryNode(_rTree, ref point);
        if (node == null)
            return -1;
        return node.biome;
    }

    public static BDict Create<TCond>(CInfo<TCond>[] biomes, int offset = 0) where TCond : IBiomeCondition
    {
        BDict nDict = new BDict();
        List<RNode> leaves = nDict.InitializeBiomeRegions(biomes, offset);
        nDict._rTree = nDict.ConstructRTree(leaves, biomes[0].BiomeConditions.value.GetDimensions());
        return nDict;
    }

    public TCond[] FlattenTree<TCond>() where TCond : IBiomeCondition
    {
        int treeSize = GetTreeSize();

        TCond[] flattenedNodes = new TCond[treeSize];
        Queue<RNode> treeNodes = new Queue<RNode>();
        treeNodes.Enqueue(_rTree);

        for(int i = 0; i < treeSize; i++)
        {
            RNode cur = treeNodes.Dequeue();
            if(cur == null){
                flattenedNodes[i].SetNode(RegionBound.GetNoSpace(flattenedNodes[i].GetDimensions()), -1);
                continue;
            }
            else if (cur.GetType() == typeof(LeafNode)) { 
                flattenedNodes[i].SetNode(cur.bounds, ((LeafNode)cur).biome);
                continue;//
            }
            flattenedNodes[i].SetNode(cur.bounds, -1);

            BranchNode branch = (BranchNode)cur;
            treeNodes.Enqueue(branch.childOne);
            treeNodes.Enqueue(branch.childTwo);
        }

        return flattenedNodes;
        //Based on the nature of queues, it will be filled exactly in order of the array
    }

    public int GetTreeSize()
    {
        Queue<RNode> treeNodes = new Queue<RNode>();
        treeNodes.Enqueue(_rTree);
        int treeSize = 0;
        while (treeNodes.Count != 0)
        {
            RNode cur = treeNodes.Dequeue();
            treeSize++;

            if(cur == null)
                continue;
            if (cur.GetType() == typeof(LeafNode))
                continue;

            BranchNode branch = (BranchNode)cur;
            treeNodes.Enqueue(branch.childOne);
            treeNodes.Enqueue(branch.childTwo);
        }
        return treeSize;
    }

    LeafNode QueryNode(RNode node, ref float[] point)
    {
        LeafNode OUT = null;
        if (node.GetType() == typeof(LeafNode))
            return (LeafNode)node;

        BranchNode branch = (BranchNode)node;

        if (branch.childOne.bounds.Contains(ref point))
            OUT = QueryNode(branch.childOne, ref point);
        if (OUT == null && branch.childTwo != null && branch.childTwo.bounds.Contains(ref point))
            OUT = QueryNode(branch.childTwo, ref point);

        return OUT;
    }

    RNode ConstructRTree(List<RNode> nodes, int dimensions)
    {
        int nodesInLayer = nodes.Count;
        HashSet<RNode> linked = new HashSet<RNode>();
        List<RNode> ret = new List<RNode>();

        nodes.Sort((RNode a, RNode b) => a.bounds.area.CompareTo(b.bounds.area));

        for (int i = 0; i < nodesInLayer; i++)
        {
            if (linked.Contains(nodes[i]))
                continue;

            BranchNode node = new BranchNode();
            node.bounds = RegionBound.GetAllSpace(dimensions);
            node.childOne = nodes[i];
            node.childTwo = null;


            for (int u = i + 1; u < nodesInLayer; u++)
            {
                RNode matchNode = nodes[u];
                if (linked.Contains(matchNode))
                    continue;

                RegionBound newRegion = RegionBound.mergeRegion(node.childOne.bounds, matchNode.bounds, dimensions);
                if (newRegion.area <= node.bounds.area)
                {
                    node.bounds = newRegion;
                    node.childTwo = matchNode;
                }
            }
            if (node.childTwo == null)
                node.bounds = node.childOne.bounds;

            ret.Add(node);
            linked.Add(node.childOne);
            linked.Add(node.childTwo);
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
                if (RegionIntersects(bounds, biomeRegions[u].bounds, dimensions))
                    throw new ArgumentException($"Biome {biomes[i].name}'s generation intersects with {biomes[u].name}");
            }

            biomeRegions.Add(new LeafNode(bounds, i + offset));
        }

        return biomeRegions;
    }

    bool RegionIntersects(RegionBound a, RegionBound b, int dimensions)
    {
        for (int i = 0; i < dimensions; i++)
        {
            if (a.maxCorner[i] <= b.minCorner[i] || a.minCorner[i] >= b.maxCorner[i])
                return false;
        }
        return true;
    }

    public abstract class RNode
    {
        public RegionBound bounds;
    }

    public class BranchNode : RNode
    {
        public RNode childOne; //compiles both leaf and branch nodes
        public RNode childTwo;
    }

    public class LeafNode : RNode
    {
        public int biome;

        public LeafNode(RegionBound bounds, int biome)
        {
            this.bounds = bounds;
            this.biome = biome;
        }
    }


    public struct RegionBound
    {
        public float[] maxCorner;
        public float[] minCorner;
        public double area;

        public RegionBound(int dimensions)
        {
            maxCorner = new float[dimensions];
            minCorner = new float[dimensions];
            area = -1;
        }

        public void SetBoundDimension(int dimension, float min, float max)
        {
            minCorner[dimension] = min;
            maxCorner[dimension] = max;
        }

        public void CalculateArea()
        {
            area = 1;
            for (int i = 0; i < maxCorner.Length; i++)
            {
                area *= maxCorner[i] - minCorner[i];
            }
        }

        public bool Contains(ref float[] point)
        {
            for (int i = 0; i < maxCorner.Length; i++)
            {
                if (point[i] < minCorner[i] || point[i] > maxCorner[i])
                    return false;
            }
            return true;
        }

        public static RegionBound mergeRegion(RegionBound a, RegionBound b, int dimensions)
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

        public static RegionBound GetAllSpace(int dimensions)
        {
            RegionBound ret = new RegionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MinValue, float.MaxValue);
            ret.area = float.MaxValue;
            return ret;
        }

        public static RegionBound GetNoSpace(int dimensions)
        {
            RegionBound ret = new RegionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MaxValue, float.MinValue);
            ret.area = 0;
            return ret;
        }
    }
}

}