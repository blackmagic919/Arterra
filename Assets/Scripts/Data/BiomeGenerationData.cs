using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Utils;

[CreateAssetMenu(menuName = "Generation/BiomeGenerationData")]
public class BiomeGenerationData : UpdatableData
{
    [SerializeField]
    public List<Biome> biomes;
    [HideInInspector]
    public BiomeDictionary dictionary;
    public int StructureChecksPerChunk;
    [Range(1, 5)]
    public float LoDFalloff;
    public int maxLoD;

    ComputeBuffer biomeRTreeBuffer;
    ComputeBuffer biomeMatCountBuffer;
    ComputeBuffer materialPrefBuffer;
    ComputeBuffer materialVertPrefBuffer;
    ComputeBuffer materialIndexBuffer;

    ComputeBuffer biomeStructBuffer;
    ComputeBuffer structVertPrefBuffer;
    ComputeBuffer structFrequencyBuffer;
    ComputeBuffer structIndexBuffer;
    //
    protected override void OnValidate()
    {
        if (biomes == null || biomes.Count == 0)
            return;
        dictionary = new BiomeDictionary(biomes);

        base.OnValidate();
    }

    private void OnEnable()
    {
        SetGlobalBuffers();
    }

    void OnDisable()
    {
        biomeRTreeBuffer?.Release();
        biomeMatCountBuffer?.Release();
        materialPrefBuffer?.Release();
        materialVertPrefBuffer?.Release();
        materialIndexBuffer?.Release();

        biomeStructBuffer?.Release();
        structVertPrefBuffer?.Release();
        structFrequencyBuffer?.Release();
        structIndexBuffer?.Release();
    }

    void SetGlobalBuffers()
    {
        
        int numBiomes = biomes.Count;
        uint[] biomeMatCount = new uint[numBiomes + 1]; //Prefix sum
        List<Vector2> matPrefList = new List<Vector2>();
        List<BiomeInfo.DensityFunc> densityFuncsMat = new List<BiomeInfo.DensityFunc>();
        List<uint> materialIndexes = new List<uint>();

        for (int i = 0; i < numBiomes; i++)
        {
            biomeMatCount[i+1] = (uint)biomes[i].info.Materials.Count + biomeMatCount[i];
            matPrefList.AddRange(biomes[i].info.Materials.Select((e) => new Vector2(e.genNoiseSize, e.genNoiseShape)));
            densityFuncsMat.AddRange(biomes[i].info.Materials.Select((e) => e.VerticalPreference));
            materialIndexes.AddRange(biomes[i].info.Materials.Select((e) => (uint)e.materialIndex));
        }

        int numNodes = dictionary.GetTreeSize();
        BiomeDictionary.RNodeFlat[] RTree = dictionary.FlattenTree();

        biomeRTreeBuffer = new ComputeBuffer(numNodes, (sizeof(float) * 6) * 2 + sizeof(int), ComputeBufferType.Structured);
        biomeRTreeBuffer.SetData(RTree);

        biomeMatCountBuffer = new ComputeBuffer(numBiomes + 1, sizeof(uint), ComputeBufferType.Structured);
        biomeMatCountBuffer.SetData(biomeMatCount);

        materialPrefBuffer = new ComputeBuffer(matPrefList.Count, sizeof(float) * 2, ComputeBufferType.Structured);
        materialPrefBuffer.SetData(matPrefList);

        materialVertPrefBuffer = new ComputeBuffer(densityFuncsMat.Count, sizeof(int) * 3 + sizeof(float) * 2, ComputeBufferType.Structured);
        materialVertPrefBuffer.SetData(densityFuncsMat);

        materialIndexBuffer = new ComputeBuffer(materialIndexes.Count, sizeof(uint), ComputeBufferType.Structured);
        materialIndexBuffer.SetData(materialIndexes);

        Shader.SetGlobalBuffer("_BiomeRTree", biomeRTreeBuffer);
        Shader.SetGlobalBuffer("_BiomeMaterialCount", biomeMatCountBuffer);
        Shader.SetGlobalBuffer("_BiomeMaterialNoisePref", materialPrefBuffer);
        Shader.SetGlobalBuffer("_BiomeMaterialVerticalPref", materialVertPrefBuffer);
        Shader.SetGlobalBuffer("_BiomeMaterialIndexBuffer", materialIndexBuffer);

        uint[] biomeStructCount = new uint[numBiomes + 1]; 
        List<BiomeInfo.DensityFunc> densityFuncsStruct = new List<BiomeInfo.DensityFunc>();
        List<float> generationFrequency = new List<float>();
        List<uint> structureIndices = new List<uint>();
        for (int i = 0; i < numBiomes; i++)
        {
            biomeStructCount[i+1] = (uint)biomes[i].info.Structures.Count + biomeStructCount[i];
            densityFuncsStruct.AddRange(biomes[i].info.Structures.Select((e) => e.VerticalPreference));
            generationFrequency.AddRange(biomes[i].info.Structures.Select((e) => e.ChancePerStructurePoint));
            structureIndices.AddRange(biomes[i].info.Structures.Select((e) => e.structureIndex));
        }

        biomeStructBuffer = new ComputeBuffer(numBiomes + 1, sizeof(uint), ComputeBufferType.Structured);
        biomeStructBuffer.SetData(biomeStructCount);

        structVertPrefBuffer = new ComputeBuffer(densityFuncsStruct.Count, sizeof(int) * 3 + sizeof(float) * 2, ComputeBufferType.Structured);
        structVertPrefBuffer.SetData(densityFuncsStruct);

        structFrequencyBuffer = new ComputeBuffer(generationFrequency.Count, sizeof(float), ComputeBufferType.Structured);
        structFrequencyBuffer.SetData(generationFrequency);

        structIndexBuffer = new ComputeBuffer(structureIndices.Count, sizeof(uint), ComputeBufferType.Structured);
        structIndexBuffer.SetData(structureIndices);

        Shader.SetGlobalBuffer("_BiomeStructurePrefix", biomeStructBuffer);
        Shader.SetGlobalBuffer("_BiomeStructureVerticalPref", structVertPrefBuffer);
        Shader.SetGlobalBuffer("_BiomeStructureFrequency", structFrequencyBuffer);
        Shader.SetGlobalBuffer("_BiomeStructureIndex", structIndexBuffer);
    }
}
//
[System.Serializable]
public class Biome
{
    public BiomeConditionsData conditions;
    public BiomeInfo info;
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

    public BiomeDictionary(List<Biome> biomes)
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
        public Vector3 min1;
        public Vector3 min2;
        public Vector3 max1;
        public Vector3 max2;
        public int biome; 

        public RNodeFlat(RNode rNode)
        {

            min1 = new(rNode.bounds.minCorner[0], rNode.bounds.minCorner[1], rNode.bounds.minCorner[2]);
            min2 = new(rNode.bounds.minCorner[3], rNode.bounds.minCorner[4], rNode.bounds.minCorner[5]);
            max1 = new(rNode.bounds.maxCorner[0], rNode.bounds.maxCorner[1], rNode.bounds.maxCorner[2]);
            max2 = new(rNode.bounds.maxCorner[3], rNode.bounds.maxCorner[4], rNode.bounds.maxCorner[5]);

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

    List<RNode> InitializeBiomeRegions(List<Biome> biomes)
    {
        int numOfBiomes = biomes.Count;
        List<RNode> biomeRegions = new List<RNode>();
        for (int i = 0; i < numOfBiomes; i++)
        {
            Biome biome = biomes[i];
            BiomeConditionsData conditions = biome.conditions;
            regionBound bounds = new regionBound(6);
            bounds.SetDimensions(conditions);
            bounds.CalculateArea();

            for (int u = i - 1; u >= 0; u--)
            {
                if (RegionIntersects(bounds, biomeRegions[u].bounds, 6))
                    throw new ArgumentException($"Biome {biome.info.BiomeName}'s generation intersects with {biomes[u].info.BiomeName}");
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

        public void SetDimensions(BiomeConditionsData conditions)
        {
            this.SetBoundDimension(0, conditions.ContinentalStart, conditions.ContinentalEnd);
            this.SetBoundDimension(1, conditions.ErosionStart, conditions.ErosionEnd);
            this.SetBoundDimension(2, conditions.PVStart, conditions.PVEnd);
            this.SetBoundDimension(3, conditions.SquashStart, conditions.SquashEnd);
            this.SetBoundDimension(4, conditions.TempStart, conditions.TempEnd);
            this.SetBoundDimension(5, conditions.HumidStart, conditions.HumidEnd);
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
    }
}
