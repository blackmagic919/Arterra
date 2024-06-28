using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.Mathematics;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeGenerationData")]
public class BiomeGenerationData : ScriptableObject
{
    [SerializeField]
    public Option<List<Option<BiomeInfo> > > biomes;
    public int StructureChecksPerChunk;
    [Range(1, 5)]
    public float LoDFalloff;
    public int maxLoD;
}



public class BiomeDictionary
{
    readonly RNode _rTree;

    public int Query(float[] point)
    {
        LeafNode node = QueryNode(_rTree, ref point);
        if (node == null)
            return -1;
        return node.biome;
    }

    public BiomeDictionary(List<Option<BiomeInfo> > biomes)
    {
        List<RNode> leaves = InitializeBiomeRegions(biomes);
        _rTree = ConstructRTree(leaves);
    }

    public RNodeFlat[] FlattenTree()
    {
        int treeSize = GetTreeSize();

        RNodeFlat[] flattenedNodes = new RNodeFlat[treeSize];
        Queue<RNode> treeNodes = new Queue<RNode>();
        treeNodes.Enqueue(_rTree);

        for(int i = 0; i < treeSize; i++)
        {
            RNode cur = treeNodes.Dequeue();
            if(cur == null){
                flattenedNodes[i] = new RNodeFlat(new LeafNode(regionBound.GetNoSpace(6), -1));
                continue;
            }

            flattenedNodes[i] = new RNodeFlat(cur);

            if (cur.GetType() == typeof(LeafNode)) { 
                flattenedNodes[i].biome = ((LeafNode)cur).biome;
                continue;//
            }

            BranchNode branch = (BranchNode)cur;
            treeNodes.Enqueue(branch.childOne);
            treeNodes.Enqueue(branch.childTwo);
        }

        return flattenedNodes;
        //Based on the nature of queues, it will be filled exactly in order of the array
    }
    //
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public struct RNodeFlat
    {
        //Unfortunately, can't use fixed float minCond[6] because unsafe
        //However, this can be redefined into float[6] because it's sequential
        float min_1; float min_2; float min_3; float min_4; float min_5; float min_6;
        float max_1; float max_2; float max_3; float max_4; float max_5; float max_6;
        public int biome; 

        public RNodeFlat(RNode rNode)
        {

            min_1 = rNode.bounds.minCorner[0]; min_2 =  rNode.bounds.minCorner[1]; min_3 = rNode.bounds.minCorner[2];
            min_4 = rNode.bounds.minCorner[3]; min_5 = rNode.bounds.minCorner[4]; min_6 = rNode.bounds.minCorner[5];
            
            max_1 = rNode.bounds.maxCorner[0]; max_2 = rNode.bounds.maxCorner[1]; max_3 = rNode.bounds.maxCorner[2];
            max_4 = rNode.bounds.maxCorner[3]; max_5 = rNode.bounds.maxCorner[4]; max_6 = rNode.bounds.maxCorner[5];

            biome = -1;
        }
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

    RNode ConstructRTree(List<RNode> nodes)
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
            node.bounds = regionBound.GetAllSpace(6);
            node.childOne = nodes[i];
            node.childTwo = null;


            for (int u = i + 1; u < nodesInLayer; u++)
            {
                RNode matchNode = nodes[u];
                if (linked.Contains(matchNode))
                    continue;

                regionBound newRegion = regionBound.mergeRegion(node.childOne.bounds, matchNode.bounds, 6);
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
        return ConstructRTree(ret);
    }

    List<RNode> InitializeBiomeRegions(List<Option<BiomeInfo> > biomes)
    {
        int numOfBiomes = biomes.Count;
        List<RNode> biomeRegions = new List<RNode>();
        for (int i = 0; i < numOfBiomes; i++)
        {
            BiomeInfo.BiomeConditionsData conditions = biomes[i].value.BiomeConditions.value;
            regionBound bounds = new regionBound(6);
            bounds.SetDimensions(conditions);
            bounds.CalculateArea();

            for (int u = i - 1; u >= 0; u--)
            {
                if (RegionIntersects(bounds, biomeRegions[u].bounds, 6))
                    throw new ArgumentException($"Biome {biomes[i].value.name}'s generation intersects with {biomes[u].value.name}");
            }

            biomeRegions.Add(new LeafNode(bounds, i));
        }

        return biomeRegions;
    }

    bool RegionIntersects(regionBound a, regionBound b, int dimensions)
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
        public regionBound bounds;
    }

    public class BranchNode : RNode
    {
        public RNode childOne; //compiles both leaf and branch nodes
        public RNode childTwo;
    }

    public class LeafNode : RNode
    {
        public int biome;

        public LeafNode(regionBound bounds, int biome)
        {
            this.bounds = bounds;
            this.biome = biome;
        }
    }

    public struct regionBound
    {
        public float[] maxCorner;
        public float[] minCorner;
        public double area;

        public regionBound(int dimensions)
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

        public void SetDimensions(BiomeInfo.BiomeConditionsData conditions)
        {
            this.SetBoundDimension(0, conditions.TerrainStart, conditions.TerrainEnd);
            this.SetBoundDimension(1, conditions.ErosionStart, conditions.ErosionEnd);
            this.SetBoundDimension(2, conditions.SquashStart, conditions.SquashEnd);
            this.SetBoundDimension(3, conditions.CaveFreqStart, conditions.CaveFreqEnd);
            this.SetBoundDimension(4, conditions.CaveSizeStart, conditions.CaveSizeEnd);
            this.SetBoundDimension(5, conditions.CaveShapeStart, conditions.CaveShapeEnd);
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

        public static regionBound mergeRegion(regionBound a, regionBound b, int dimensions)
        {
            regionBound ret = new regionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
            {
                ret.maxCorner[i] = Mathf.Max(a.maxCorner[i], b.maxCorner[i]);
                ret.minCorner[i] = Mathf.Min(a.minCorner[i], b.minCorner[i]);
            }
            ret.CalculateArea();
            return ret;
        }

        public static regionBound GetAllSpace(int dimensions)
        {
            regionBound ret = new regionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MinValue, float.MaxValue);
            ret.area = float.MaxValue;
            return ret;
        }

        public static regionBound GetNoSpace(int dimensions)
        {
            regionBound ret = new regionBound(dimensions);
            for (int i = 0; i < dimensions; i++)
                ret.SetBoundDimension(i, float.MaxValue, float.MinValue);
            ret.area = 0;
            return ret;
        }
    }
}
