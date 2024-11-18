using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Concurrent;
using System.Numerics;
using UnityEngine.Events;

public class OctreeTerrain : MonoBehaviour
{
    public static readonly int[] taskLoadTable = { 1, 2, 5, 2, 8, 0 };
    public static UnityEvent OrderedDisable;
    public static Queue<UpdateTask> MainLoopUpdateTasks;
    public static Queue<UpdateTask> MainLateUpdateTasks;
    public static Queue<UpdateTask> MainFixedUpdateTasks;
    public static ConcurrentQueue<GenTask> RequestQueue;
    public static Transform origin;
    public static RenderSettings s;
    public static int maxFrameLoad = 50; //GPU load
    public static int viewDistUpdate = 32;
    private int3 prevViewerPos;

    public static Octree octree;
    public static ConstrainedLL<TerrainChunk> chunks;
    public static int3 ChunkPos;
    public static int3 vChunkPos => ChunkPos * s.mapChunkSize + s.mapChunkSize/2;
    private static int rootDim => s.Balance == 1 ? 3 : 2;
    // Start is called before the first frame update
    public int Layer = 1;

    private void OnEnable(){
        s = WorldStorageHandler.WORLD_OPTIONS.Quality.Rendering.value;
        origin = this.transform; //This means origin in Unity's scene heiharchy
        octree = new Octree(s.MaxDepth, s.Balance, s.MinChunkRadius);
        chunks = new ConstrainedLL<TerrainChunk>((uint)(Octree.GetNumChunks(s.MaxDepth, s.Balance, s.MinChunkRadius) + 1));
        OrderedDisable = new UnityEvent();
        UpdateViewerPos();

        MainLoopUpdateTasks = new Queue<UpdateTask>();
        MainLateUpdateTasks = new Queue<UpdateTask>();
        MainFixedUpdateTasks = new Queue<UpdateTask>();
        RequestQueue = new ConcurrentQueue<GenTask>();

        UtilityBuffers.Initialize();
        RegisterBuilder.Initialize();
        GenerationPreset.Initialize();

        InputPoller.Initialize();
        UIOrigin.Initialize();

        GPUDensityManager.Initialize();
        CPUDensityManager.Initialize();

        EntityManager.Initialize();
        TerrainUpdateManager.Initialize();

        AtmospherePass.Initialize();
        ChunkStorageManager.Initialize();

        StructureGenerator.PresetData();
        TerrainGenerator.PresetData();
        DensityGenerator.PresetData();
        ShaderGenerator.PresetData();
        WorldStorageHandler.WORLD_OPTIONS.System.ReadBack.value.Initialize();
    }
    public Transform viewer;
    void Start()
    {
        UpdateViewerPos();
        ConstructAroundPoint();
    }

    private void OnDisable()
    {
        ForEachChunk((uint chunk) => chunks.nodes[chunk].Value.Destroy());

        UIOrigin.Release();
        UtilityBuffers.Release();
        GPUDensityManager.Release();
        CPUDensityManager.Release();
        EntityManager.Release();
        GenerationPreset.Release();
        AtmospherePass.Release();
        WorldStorageHandler.WORLD_OPTIONS.System.ReadBack.value.Release();

        OrderedDisable.Invoke();
    }

    /*
    void OnDrawGizmos(){
        uint curChunk = chunks.Head();
        int count = 0;
        do{
            TerrainChunk chunk = chunks.nodes[curChunk].Value;
            curChunk = chunks.Next(curChunk);

            if(chunk == null) continue;
            if(chunk.depth != Layer) continue;
            else {
                Gizmos.color = Color.white;
                Gizmos.DrawWireCube(((float3)chunk.origin + s.mapChunkSize/2) * s.lerpScale, (float3)chunk.size * s.lerpScale);
            }
            count++;
        } while(curChunk != chunks.Head());
        //Debug.Log(count);
        //Debug.Log(Octree.GetAxisChunksDepth(math.floorlog2(s.Layer), (int)s.balanceF, (uint)s.minChunkRadius));
    }*/

    // Update is called once per frame
    void Update()
    {
        VerifyChunks();
        ForEachChunk((uint chunk) => chunks.nodes[chunk].Value.Update());
        StartGeneration();
        
        ProcessUpdateTasks(MainLoopUpdateTasks);
    }

    private void LateUpdate(){ ProcessUpdateTasks(MainLateUpdateTasks); }
    private void FixedUpdate(){ ProcessUpdateTasks(MainFixedUpdateTasks); }
    private void ProcessUpdateTasks(Queue<UpdateTask> taskQueue)
    {
        int UpdateTaskCount = taskQueue.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            UpdateTask task = taskQueue.Dequeue();
            if(!task.active)
                continue;
            task.Update(this);
            taskQueue.Enqueue(task);
        }
    }
    void StartGeneration()
    {
        int FrameGPULoad = 0;
        while(FrameGPULoad < maxFrameLoad)
        {
            if (!RequestQueue.TryDequeue(out GenTask gen))
                return;
            if(gen.chunk == null || !gen.chunk.active) 
                continue;

            gen.task();
            FrameGPULoad += taskLoadTable[gen.id];
        }
    }

    private void ForEachChunk(Action<uint> action){
        uint curChunk = chunks.Head();
        do{
            action(curChunk);
            curChunk = chunks.Next(curChunk);
        } while(curChunk != chunks.Head());

    }

    private void VerifyChunks(){
        int3 ViewerPosition = (int3)((float3)viewer.position / s.lerpScale);
        if(math.distance(prevViewerPos, ViewerPosition) < viewDistUpdate) return;
        prevViewerPos = ViewerPosition;
        UpdateViewerPos();

        //This is buffered because Verifying Leaf Chunks may change octree structure
        Queue<TerrainChunk> frameChunks = new Queue<TerrainChunk>();
        ForEachChunk((uint chunk) => frameChunks.Enqueue(chunks.nodes[chunk].Value));
        while(frameChunks.Count > 0){
            TerrainChunk chunk = frameChunks.Dequeue();
            if(!chunk.active) continue;
            chunk.VerifyChunk();
        }
    }

    private void UpdateViewerPos(){
        int3 ViewerPosition = (int3)((float3)viewer.position / s.lerpScale + s.mapChunkSize/2);
        int3 intraOffset = ((ViewerPosition % s.mapChunkSize) + s.mapChunkSize) % s.mapChunkSize;
        ChunkPos = (ViewerPosition - intraOffset) / s.mapChunkSize;
    }

    public static bool IsBalanced(ref Octree.Node node){
        int balance = (int)(node.size / s.Balance);
        return node.GetL1Dist(vChunkPos) - s.mapChunkSize * s.MinChunkRadius >= balance;
    }

    public static bool IsBordering(ref Octree.Node node){
        //nOrigin is the origin of a neighbor in the direction away from the viewer of the same level's parent
        int3 nOrigin = node.origin + ((int3)math.sign(node.origin - vChunkPos)) * (int)node.size;
        int parentSize = (int)node.size * 2;
        nOrigin -= ((nOrigin % parentSize) + parentSize) % parentSize; //make sure offset is not off -> get its parent's origin

        Octree.Node neighbor = new Octree.Node{origin = nOrigin, size = (uint)parentSize};
        return IsBalanced(ref neighbor);
    }

    private static void AddTerrainChunk(uint octreeIndex){
        ref Octree.Node node = ref octree.nodes[octreeIndex];
        TerrainChunk nChunk;
        if(node.size == s.mapChunkSize){
            nChunk = new RealChunk(origin, node.origin, (int)node.size, octreeIndex);
        } else {
            nChunk = new VisualChunk(origin, node.origin, (int)node.size, octreeIndex);
        }
        node.Chunk = chunks.Enqueue(nChunk); 
        node.IsComplete = false;
    }

    private static void BuildTree(uint root){ BuildTree(new Queue<uint>(new uint[]{root})); }
    private static void BuildTree(Queue<uint> tree){
        while(tree.Count > 0){
            uint nodeIndex = tree.Dequeue();
            ref Octree.Node node = ref octree.nodes[nodeIndex];
            if(node.size <= s.mapChunkSize || IsBalanced(ref node)){
                AddTerrainChunk(nodeIndex);
                continue;
            }

            AddOctreeChildren(ref node, nodeIndex, (uint child) => tree.Enqueue(child));
        }
    }

    private void ConstructAroundPoint(){
        int maxChunkSize = (int)s.mapChunkSize * (1 << (int)s.MaxDepth);
        int3 LCoord = octree.FloorLCoord(vChunkPos - maxChunkSize/2, maxChunkSize);

        Queue<uint> tree = new Queue<uint>(); 
        Octree.Node root = new Octree.Node{size = (uint)maxChunkSize*2, origin = -maxChunkSize, child = 0, _chunk = 0};
        AddOctreeChildren(ref root, 0, (uint child) => tree.Enqueue(child), rootDim);
        BuildTree(tree);
    }

    private static void AddOctreeChildren(ref Octree.Node parent, uint parentIndex, Action<uint> OnAddChild, int cDim = 2){
        uint childSize = parent.size >> 1; uint sibling = 0;
        int numChildren = cDim * cDim * cDim;
        for(int i = 0; i < numChildren; i++){
            sibling = octree.AddNode(new Octree.Node{
                origin = parent.origin + new int3(i % cDim, i / cDim % cDim, i / (cDim * cDim ) % cDim) * (int)childSize,
                size = childSize,
                sibling = sibling,
                parent = parentIndex,
                child = 0, //IsLeaf = true
                _chunk = 0, //HasChunk = false
            });
            OnAddChild(sibling);
        } 
        parent.child = sibling;
        for(int i = 0; i < 7; i++) sibling = octree.nodes[sibling].sibling;
        octree.nodes[sibling].sibling = parent.child;
    }

    public static void SubdivideChunk(uint leafIndex){
        ref Octree.Node node = ref octree.nodes[leafIndex];
        if(IsBalanced(ref node) || node.size <= s.mapChunkSize) return;

        //Unregistering chunk will delete subtree if it has a subtree
        KillSubtree(leafIndex);
        BuildTree(leafIndex);
    }

    //Input: the leaf we are trying to merge
    //Returns: Whether it needs to create the leaf chunk(only for the recursive case, do not read outside function)
    //Note: it is not possible for a leaf node to merge and then need to subdivide in the same frame
    public static bool MergeSiblings(uint leaf){
        Octree.Node node = octree.nodes[leaf]; uint sibling = node.sibling;
        if(!IsBalanced(ref octree.nodes[node.parent])){ return true; }
        if(node.parent == 0) {return RemapRoot(leaf);}

        sibling = leaf;
        do{
            KillSubtree(sibling);
            sibling = octree.nodes[sibling].sibling;
        } while(sibling != leaf);
        
        uint parent = octree.nodes[leaf].parent;
        //If the parent is balanced, then it won't be subdivided
        if(MergeSiblings(parent)){
            AddTerrainChunk(parent);
        }return false;
    }

    private static bool RemapRoot(uint octreeNode){
        ref Octree.Node node = ref octree.nodes[octreeNode];
        int maxChunkSize = (int)s.mapChunkSize * (1 << (int)s.MaxDepth);
        int3 VCoord = octree.FloorLCoord(vChunkPos - maxChunkSize/2, maxChunkSize);
        int3 offset = ((node.origin / maxChunkSize - VCoord) % rootDim + rootDim) % rootDim;

        int3 newOrigin = (VCoord + offset) * maxChunkSize;
        if(newOrigin.Equals(node.origin)) return true;

        //Force destroy it without creating zombies
        if(!node.IsLeaf) DestroySubtree(node.child);
        if(node.HasChunk) {
            chunks.nodes[node.Chunk].Value.Destroy();
            chunks.RemoveNode(node.Chunk);
            node._chunk = 0;
        } node.origin = newOrigin;
        BuildTree(octreeNode);
        return false;
    }

    public struct GenTask{
        public Action task;
        public int id;
        public TerrainChunk chunk;
        public GenTask(Action task, int id, TerrainChunk chunk){
            this.task = task;
            this.id = id;
            this.chunk = chunk;
        }
    }

    //Kills all chunks in the subtree turning them into zombies
    private static void KillSubtree(uint octreeIndex){
        ref Octree.Node node = ref octree.nodes[octreeIndex];
        //This will automatically delete the subtree if it has one
        if(node.HasChunk){ chunks.nodes[node.Chunk].Value.Kill(); } 
        else if(!node.IsLeaf){ 
            uint sibling = node.child;
            do{
                KillSubtree(sibling);
                sibling = octree.nodes[sibling].sibling;
            } while(sibling != node.child);
        } 
    }

    //Reaps zombie chunks
    // 1. Destroy all chunk's in its subtree
    // 2. Remove all chunk's along its path to root until it reaches a node where it has an incomplete sibling
    public static void ReapChunk(uint octreeIndex){
        ref Octree.Node node = ref octree.nodes[octreeIndex];
        //If the chunk has already been reaped, it will still travel up the root path
        //but this in theory should do nothing
        node.IsComplete = true;
        if(!node.IsLeaf) DestroySubtree(node.child);
        node.child = 0; //IsLeaf = true
        MergeAncestry(node.parent);
    }

    //Input: The child node whose siblings and itself will be deleted
    private static void DestroySubtree(uint octreeIndex){

        uint sibling = octreeIndex;
        do{
            uint nSibling = octree.nodes[sibling].sibling;

            ref Octree.Node node = ref octree.nodes[sibling];
            if(!node.IsLeaf) DestroySubtree(node.child);
            if(node.HasChunk){
                chunks.nodes[node.Chunk].Value.Destroy();
                chunks.RemoveNode(node.Chunk);
                node._chunk = 0;
            } 
            octree.RemoveNode(sibling);
            sibling = nSibling;
        } while(sibling != octreeIndex);
    }

    private static void MergeAncestry(uint octreeIndex){
        static bool IsSubtreeComplete(uint octreeIndex){
            uint sibling = octreeIndex;
            if(octreeIndex == 0) return true;
            do{
                ref Octree.Node node = ref octree.nodes[sibling];
                if(node.HasChunk){
                    if(!node.IsComplete) return false;
                } else if(!IsSubtreeComplete(node.child))
                    return false;
                sibling = octree.nodes[sibling].sibling;
            } while(sibling != octreeIndex);
            return true;
        }
        ref Octree.Node node = ref octree.nodes[octreeIndex];
        while(node.parent != 0){
            if(node.HasChunk && IsSubtreeComplete(node.child)){
                chunks.nodes[node.Chunk].Value.Destroy();
                chunks.RemoveNode(node.Chunk);
                node._chunk = 0;
            }
            node = ref octree.nodes[node.parent];
        }
    }
}

public struct Octree{
    public Node[] nodes;
    public int3 FloorLCoord(int3 GCoord, int chunkSize){ 
        int3 offset = ((GCoord % chunkSize) + chunkSize) % chunkSize; // GCoord %% chunkSize
        return (GCoord - offset) / chunkSize; 
    }

    //Gets the final amount of terrain chunks(leaf nodes) in the octree at any state
    public static int GetNumChunks(int depth, int balanceF, int chunksRadius){
        int numChunks = 0; chunksRadius++;
        for(int i = 0; i < depth; i++){
            int layerDiameter = GetAxisChunksDepth(i, balanceF, (uint)chunksRadius);
            numChunks += layerDiameter * layerDiameter * layerDiameter;
        }
        return numChunks;
    }

    /*
    Reasoning: 
    - The maximum amount of chunks at a layer is equivalent to the maximum amount of concurrent unbalanced
    chunks on its parent layer * 8(*2*2*2). 
    - The maximum amount of parent chunks that are unbalanced is equivalent to 
        radius = ((1 << (depth - (balanceF - 1)) + chunksRadius) >> depth)
        parentDiam = 2 * (radius >> 1 + 1) + radius % 2
    */

    public static int GetAxisChunksDepth(int depth, int balanceF, uint chunksRadius){
        int radius =(int)((1 << math.max(depth - balanceF + 2, 0)) + chunksRadius);
        int remainder = (chunksRadius % (1 << depth)) != 0 ? 1 : 0;
        int pDiam = (radius >> depth) + remainder + 1;
        return pDiam * 2;
    }

    public Octree(int depth, int balanceF, int chunksRadius){
        int numChunks = GetNumChunks(depth, balanceF, chunksRadius);
        nodes = new Node[4*numChunks+1];
        nodes[0].child = 1; //free list
    }
    

    public uint AddNode(Node octree){
        uint freeNode = nodes[0].child; //Free Head Node
        uint nextNode = nodes[freeNode].child == 0 ? freeNode + 1 : nodes[freeNode].child;
        nodes[0].child = nextNode;

        nodes[freeNode] = octree;
        return freeNode;
    }

    public void RemoveNode(uint index){
        nodes[index].child = nodes[0].child;
        nodes[0].child = index;
    }

    public struct Node{
        public int3 origin; //GCoord, bottom left corner
        public uint size;
        public uint child; 
        //Circular LL of siblings
        public uint sibling;
        public uint parent;
        public readonly bool IsLeaf => child == 0;

        public uint _chunk;
        public readonly bool HasChunk => _chunk != 0;
        public bool IsComplete{
            readonly get => (_chunk & 0x80000000) != 0;
            set => _chunk = value ? _chunk | 0x80000000 : _chunk & 0x7FFFFFFF;
        }
        public uint Chunk{
            readonly get => _chunk & 0x7FFFFFFF;
            set => _chunk = (value & 0x7FFFFFFF) | (_chunk & 0x80000000);
        }

        public int GetL1Dist(int3 GCoord){
            int3 origin = this.origin;
            int3 end = this.origin + (int)size;
            int3 dist = math.abs(math.clamp(GCoord, origin, end) - GCoord);
            return math.cmax(dist);
        }

        public int GetCenterDist(int3 GCoord){
            int3 middle = this.origin + (int)size;
            int3 dist = math.abs(middle - GCoord);
            return math.cmax(dist);
        }
    }
}

public struct ConstrainedLL<T>{
    public Node[] nodes;
    public uint length;
    public uint capacity;

    public ConstrainedLL(uint size){
        length = 0;
        capacity = size;
        nodes = new Node[size];

        nodes[0].prev = 1; //FirstFreeNode
        nodes[0].next = 0; //FirstNode
    }

    public uint Enqueue(T node){
        if(length + 1 >= capacity) {
            return 0;
        }

        uint freeNode = nodes[0].prev; //Free Head Node
        uint nextNode = nodes[freeNode].next == 0 ? freeNode + 1 : nodes[freeNode].next;
        nodes[0].prev = nextNode;

        nextNode = length == 0 ? freeNode : nodes[0].next;
        nodes[freeNode] = new Node{
            prev = nodes[nextNode].prev,
            next = nextNode,
            Value = node,
        };
        nodes[nodes[nextNode].prev].next = freeNode;
        nodes[nextNode].prev = freeNode;
        nodes[0].next = freeNode;

        length++;
        return freeNode;
    }

    public void RemoveNode(uint index){
        if(index == 0 || length == 0) return;

        uint prev = nodes[index].prev;
        uint next = nodes[index].next;
        if(nodes[0].next == index) 
            nodes[0].next = next;

        nodes[prev].next = next;
        nodes[next].prev = prev;
        //This is so if T is an object, it can be garbage collected
        nodes[index] = (Node)default(Node);

        nodes[index].next = nodes[0].prev;
        nodes[0].prev = index;

        length--;
        return;
    }

    public readonly uint Head(){ return nodes[0].next; }
    public readonly uint Next(uint index){ return nodes[index].next; }

    public struct Node{
        public uint next;
        public uint prev;
        public T Value;
    }
}

