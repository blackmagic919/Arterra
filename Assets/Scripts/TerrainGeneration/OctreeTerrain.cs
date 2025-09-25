using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Concurrent;
using UnityEngine.Events;
using WorldConfig;
using UnityEngine.Profiling;
using MapStorage;

namespace TerrainGeneration{
/// <summary>
/// Octree Terrain is a singleton class that drives all generation and generation
/// based events in the game. It is responsible for scheduling when generation 
/// tasks are executed and identifying when the current generation needs to update.
/// 
/// System tasks connected to the games frame-by-frame update loop should hook into either
/// MainLoopUpdateTasks, MainLateUpdateTasks, or MainFixedUpdateTasks. This is so that
/// these static systems can hook into the update loop without needing to be a MonoBehaviour.
/// </summary>
public class OctreeTerrain : MonoBehaviour
{
    /// <summary>
    /// The load for each task as ordered in <see cref="Utils.priorities.planning"/>.
    /// Each task's load is cumilated until the frame's load is exceeded at which point generation stops.
    /// </summary>
    public static readonly int[] taskLoadTable = { 4, 3, 10, 1, 2, 0 };
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every update loop. The update loop occurs
    /// once every frame before the late update loop.
    /// </summary>
    public static Queue<IUpdateSubscriber> MainLoopUpdateTasks;
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every late update loop. The late update loop
    /// occurs once every frame after the update loop.
    /// </summary>
    public static Queue<IUpdateSubscriber> MainLateUpdateTasks;
    /// <summary>
    /// A queue containing subscribed tasks that are executed
    /// once every fixed update loop. The fixed update loop is
    /// akin to a game-tick and is frame-independent. 
    /// </summary>
    public static Queue<IUpdateSubscriber> MainFixedUpdateTasks;
    /// <summary>
    /// A queue containing coroutines which will be synchronized and updated
    /// by Unity's main update loop. Unity does not allow injection into its synchronization
    /// outside monobehavior, so OctreeTerrain has to manage this.
    /// </summary>
    public static Queue<System.Collections.IEnumerator> MainCoroutines;
    /// <summary>
    /// A queue of generation actions which are processed
    /// sequentially and discarded once they are called. All tasks 
    /// are channeled through this queue to manage the resource load
    /// and facilitate expensive operations. 
    /// </summary>
    /// <remarks>
    /// The concurrent queue may also be used to reinject tasks
    /// on different threads back into the main thread.
    /// </remarks>
    public static ConcurrentQueue<GenTask> RequestQueue;

    /// <summary> A queue to reinjects events into the main thread
    /// not directly tied to a specific chunk.
    /// </summary>
    public static ConcurrentQueue<Action> ActionReinjectionQueue;
    
    private static WorldConfig.Quality.Terrain s;
    private int3 prevViewerPos;
    private static Transform origin;
    private static Octree octree;
    private static ConstrainedLL<TerrainChunk> chunks;
    /// <summary>
    /// The last tracked position of the viewer in chunk space.
    /// This value is only updated when the viewer's position
    /// exceeds the viewDistUpdate threshold.
    /// </summary>
    public static int3 ViewPosCS;
    /// <summary>
    /// The last tracked position of the viewer in grid space.
    /// This value is only updated when the viewer's position
    /// exceeds the viewDistUpdate threshold.
    /// </summary>
    public static int3 ViewPosGS => ViewPosCS * s.mapChunkSize + s.mapChunkSize/2;
    private static int rootDim => s.Balance == 1 ? 3 : 2;
    /// <summary> 
    /// The transform of the viewer around which
    /// generation(the octree) is centered.
    /// </summary>
    public static Transform viewer; 

    /// <summary>
    /// A struct that defines a generation process. GenTasks
    /// are buffered into <see cref="RequestQueue"/> and processed sequentially.
    /// GenTasks are called from main thread and allow for reinjection of async tasks.
    /// </summary>
    public struct GenTask{
        /// <summary> The action that is executed when the task is processed.</summary>
        public Action task;
        /// <summary> 
        /// The priority of the task as defined in <see cref="Utils.priorities.planning"/>. 
        /// Used to identify the load and loading message of the task.
        /// </summary>
        public int id;
        /// <summary>
        /// The chunk that the task is associated with. If the chunk is destroyed,
        /// deactivated, or null, the task will be ignored and discarded when answering.
        /// </summary>
        public TerrainChunk chunk;
        /// <summary>
        /// Constructs a new GenTask with the given action, id, and chunk.
        /// </summary>
        public GenTask(Action task, int id, TerrainChunk chunk){
            this.task = task;
            this.id = id;
            this.chunk = chunk;
        }
    }
    
    private void OnEnable(){
        s = Config.CURRENT.Quality.Terrain.value;
        origin = this.transform; //This means origin in Unity's scene heiharchy
        octree = new Octree(s.MaxDepth, s.Balance, s.MinChunkRadius);
        chunks = new ConstrainedLL<TerrainChunk>((uint)(Octree.GetMaxNodes(s.MaxDepth, s.Balance, s.MinChunkRadius) + 1));

        MainLoopUpdateTasks = new Queue<IUpdateSubscriber>();
        MainLateUpdateTasks = new Queue<IUpdateSubscriber>();
        MainFixedUpdateTasks = new Queue<IUpdateSubscriber>();
        MainCoroutines = new Queue<System.Collections.IEnumerator>();
        RequestQueue = new ConcurrentQueue<GenTask>();
        SystemProtocol.Startup();
    }

    void Start()
    {
        UpdateViewerPos();
        ConstructAroundPoint();
    }

    private void OnDisable()
    {
        ForEachChunk((uint chunk) => chunks.nodes[chunk].Value.Destroy());
        SystemProtocol.Shutdown();
    }

    
    #if UNITY_EDITOR
    private void OnDrawGizmos() {
        UnityEditor.SceneView sceneView = UnityEditor.SceneView.lastActiveSceneView;
        float3 sceneViewPos = sceneView.camera.transform.position;
        int3 GCoord = (int3)CPUMapManager.WSToGS(math.floor(sceneViewPos));
        Octree.Node node = new() { };
        for (int depth = s.MaxDepth; depth >= 0; depth--) {
            int size = s.mapChunkSize * (1 << depth);
            int3 MCoord = ((GCoord % size) + size) % size;
            int3 CCoord = (GCoord - MCoord) / s.mapChunkSize;
            node.origin = CCoord * s.mapChunkSize;
            node.size = (uint)size;
            if (IsBalanced(ref node)) break;
        }

        Gizmos.color = Color.white;
        Gizmos.DrawWireCube(CPUMapManager.GSToWS(node.origin + (float3)node.size / 2), (float3)node.size * s.lerpScale);
        Indicators.OnDrawGizmos();
    }
    #endif

    private void Update()
    {
        VerifyChunks();
        ForEachChunk((uint chunk) => chunks.nodes[chunk].Value.Update());
        StartGeneration();
        
        ProcessUpdateTasks(MainLoopUpdateTasks);
        ProcessCoroutines(MainCoroutines);
    }
    private void LateUpdate(){ ProcessUpdateTasks(MainLateUpdateTasks); }
    private void FixedUpdate(){ ProcessUpdateTasks(MainFixedUpdateTasks); }
    private void ProcessUpdateTasks(Queue<IUpdateSubscriber> taskQueue)
    {
        int UpdateTaskCount = taskQueue.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            IUpdateSubscriber task = taskQueue.Dequeue();
            if(!task.Active)
                continue;
            task.Update(this);
            taskQueue.Enqueue(task);
        }
    }

    private void ProcessCoroutines(Queue<System.Collections.IEnumerator> taskQueue){
        int UpdateTaskCount = taskQueue.Count;
        for(int i = 0; i < UpdateTaskCount; i++){
            var task = taskQueue.Dequeue();
            if(task != null) StartCoroutine(task);
        }
    }
    private void StartGeneration()
    {
        int FrameGPULoad = 0;
        while(FrameGPULoad < s.maxFrameLoad)
        {
            if (!RequestQueue.TryDequeue(out GenTask gen))
                return;
            if(gen.chunk != null && !gen.chunk.active) 
                continue;
            Profiler.BeginSample("Task Number: " + gen.id);
            gen.task();
            Profiler.EndSample();
            FrameGPULoad += taskLoadTable[gen.id];
        } 
    }

    /// <summary>
    /// Determines whether an octree node is balanced based on its current size
    /// and distance from the viewer. A node is balanced if it obeys the balance factor
    /// of the tree; if it is  1:(<see cref="WorldConfig.Quality.Terrain.Balance">Balance</see> + 1) balanced.
    /// </summary>
    /// <param name="node">The octree node whose current state is tested to be balanced</param>
    /// <returns>Whether or not the node is balanced</returns>
    public static bool IsBalanced(ref Octree.Node node){
        int balance = (int)(node.size >> (s.Balance - 1));
        return node.GetMaxDist(ViewPosGS) - s.mapChunkSize * s.MinChunkRadius >= balance;
    }

    /// <summary>
    /// Determines whether an octree node is bordering a chunk of a larger size.
    /// This is determined by checking if its neighbor farthest from the viewer is
    /// balanced if it were of a larger size than the current chunk.
    /// </summary> <param name="index"> The index of the octree node within the
    /// <see cref="octree">octree</see> structure.  </param>
    /// <returns>Whether or not the node is bordering a larger chunk</returns>
    public static bool IsBordering(int index){
        Octree.Node node = octree.nodes[index];
        int3 nOrigin = node.origin + ((int3)math.sign(node.origin - ViewPosGS)) * (int)node.size;
        int parentSize = (int)node.size * 2;
        nOrigin -= ((nOrigin % parentSize) + parentSize) % parentSize; //make sure offset is not off -> get its parent's origin

        Octree.Node neighbor = new Octree.Node{origin = nOrigin, size = (uint)parentSize};
        return IsBalanced(ref neighbor);
    }

    /// <summary>
    /// Determines the positive difference in <see cref="TerrainChunk.depth">depth</see> between a chunk and its 6 neighbors.
    /// If the parent is of a smaller depth(i.e. smaller) than the current chunk, 0 will be returned as there would be multiple nodes
    /// that border that side of the chunk. This information is not found through traversal of the octree, but mathematical
    /// evaluation off the current state of the game--the real neighbors may be at a different depth if not properly updated.
    /// </summary> <param name="index">The index of the octree node within the
    /// <see cref="octree">octree</see> structure. </param>
    /// <returns>A bitmask of 3 bytes describing the positive depth difference between the chunk and its neighbor in the
    /// <c>x, y, z</c> directions. The highest bit of every byte is set if describing the positive face</returns>
    /// <remarks>The difference in depth is found using progressive analysis of whether neighbor chunks of 
    /// larger and larger depths would be <see cref="IsBalanced">balanced</see>. There may be a way to find this 
    /// mathematically instead.</remarks>
    public static uint GetNeighborDepths(uint index){
        Octree.Node node = octree.nodes[index];
        int3 delta = (int3)math.sign(node.origin - ViewPosGS);
        uint neighborDepths = 0;
        for(int i = 0; i < 3; i++){
            int3 axisDelta = math.select(0, delta, new bool3(i == 0, i == 1, i == 2));
            int3 origin = node.origin + axisDelta * (int)node.size; int dDepth;
            for(dDepth = 1; dDepth <= s.Balance; dDepth++){
                int parentSize = (int)node.size << dDepth;
                int3 nOrigin = origin - ((origin % parentSize) + parentSize) % parentSize; //make sure offset is not off -> get its parent's origin
                Octree.Node neighbor = new Octree.Node{origin = nOrigin, size = (uint)parentSize};
                if(!IsBalanced(ref neighbor)) break;
            } dDepth--;
            neighborDepths |= ((uint)dDepth & 0x7F) << i*8;
            if(dDepth > 0) neighborDepths |= (uint)(math.cmax(axisDelta) & 0x1) << (i*8 + 7);
        }
        return neighborDepths;
    }

    /// <summary>
    /// Subdivides a chunk if it can be subdivided, otherwise does nothing.
    /// A chunk can be subdivided if it is unbalanced and not of the minimal chunk size.
    /// Subdividing a chunk will recursively subdivide it until all its children are balanced.
    /// </summary>
    /// <remarks>
    /// Subdividing a chunk causes the chunk to become a zombie. If it is already a zombie
    /// than it reaps all zombies in its local subtree before subdividing.
    /// </remarks>
    /// <param name="leafIndex">
    /// The index of the octree node within the
    /// <see cref="octree">octree</see> structure. 
    /// </param>
    public static void SubdivideChunk(uint leafIndex){
        ref Octree.Node node = ref octree.nodes[leafIndex];
        if(IsBalanced(ref node) || node.size <= s.mapChunkSize) return;

        //Unregistering chunk will delete subtree if it has a subtree
        KillSubtree(leafIndex);
        BuildTree(leafIndex);
    }

    /// <summary>
    /// Merges a chunk with its siblings if it can be merged, otherwise does nothing.
    /// A chunk can be merged if its parent is balanced and all its siblings are leaf chunks.
    /// Recursively merges all siblings until it reaches a parent that is unbalanced.
    /// If it reaches the root, it will remap the root to wrap around the viewer.
    /// </summary>
    /// <remarks>
    /// Merging a chunk causes the chunk to become a zombie. If it is already a zombie
    /// than it reaps all zombies in its local subtree before merging.
    /// </remarks>
    /// <param name="leaf">
    /// The index of the octree node within the
    /// <see cref="octree">octree</see> structure.
    /// </param>
    /// <returns>
    /// Whether or not the requested chunk was <b>unable</b> to be
    /// merged. Used for internal recursion purposes.
    /// </returns>
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

    private void ForEachChunk(Action<uint> action){
        uint curChunk = chunks.Head();
        do{
            action(curChunk);
            curChunk = chunks.Next(curChunk);
        } while(curChunk != chunks.Head());
    }
    
    private void VerifyChunks(){
        int3 ViewerPosition = (int3)((float3)viewer.position / s.lerpScale);
        if(math.distance(prevViewerPos, ViewerPosition) < s.viewDistUpdate) return;
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
        ViewPosCS = (ViewerPosition - intraOffset) / s.mapChunkSize;
    }

    private static void AddTerrainChunk(uint octreeIndex){
        ref Octree.Node node = ref octree.nodes[octreeIndex];
        TerrainChunk nChunk;
        if(node.size == s.mapChunkSize){
            nChunk = new TerrainChunk.RealChunk(origin, node.origin, (int)node.size, octreeIndex);
        } else {
            nChunk = new TerrainChunk.VisualChunk(origin, node.origin, (int)node.size, octreeIndex);
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
        Queue<uint> tree = new Queue<uint>(); 
        Octree.Node root = new Octree.Node{size = (uint)maxChunkSize*2, origin = -maxChunkSize, child = 0};
        root.ClearChunk();

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
            });
            octree.nodes[sibling].ClearChunk();
            OnAddChild(sibling);
        } 
        parent.child = sibling;
        for(int i = 0; i < 7; i++) sibling = octree.nodes[sibling].sibling;
        octree.nodes[sibling].sibling = parent.child;
    }

    private static bool RemapRoot(uint octreeNode){
        ref Octree.Node node = ref octree.nodes[octreeNode];
        int maxChunkSize = (int)s.mapChunkSize * (1 << (int)s.MaxDepth);
        int3 VCoord = Octree.FloorCCoord(ViewPosGS - maxChunkSize/2, maxChunkSize);
        int3 offset = ((node.origin / maxChunkSize - VCoord) % rootDim + rootDim) % rootDim;

        int3 newOrigin = (VCoord + offset) * maxChunkSize;
        if(newOrigin.Equals(node.origin)) return true;

        //Force destroy it without creating zombies
        if(!node.IsLeaf) DestroySubtree(node.child);
        if(node.HasChunk) {
            chunks.nodes[node.Chunk].Value.Destroy();
            chunks.RemoveNode(node.Chunk);
            node.ClearChunk();
        } node.origin = newOrigin;
        BuildTree(octreeNode);
        return false;
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

    /// <summary>
    /// Reaps a chunk's dependencies(zombies) from the octree. Normally, terrain chunks are only defined on
    /// the leaf nodes of the octree. However when a new chunk is created but is not completed(does not possess a mesh), 
    /// the original chunk still lives as a zombie. In doing so, the old chunk may be on a branch node whereby the new 
    /// chunk is its child and exists on its subtree, or conversely the old chunk is a leaf node and the newly created
    /// chunk is its parent's branch. This old chunk is a dependency of the new chunk which calls this function to reap 
    /// the old chunk when it does complete.
    /// </summary>
    /// <remarks>
    /// Calling reap chunk on a chunk will reap(destroy) all nodes within its subtree. Logically, if a chunk is complete,
    /// it should reside on a leaf-node and can destroy its subtree to achieve this. If a parent chunk is a dependency, it will
    /// only be reaped when all of its children call reap chunk. 
    /// 
    /// Additionally, if a chunk becomes a zombie, it should reap all its dependencies even if it is not complete. This may 
    /// cause a gap to appear in the terrain if regenerating very quickly, but is necessary to ensure the integrity of the octree. 
    /// </remarks>
    /// <param name="octreeIndex">
    /// The index of the octree node within the
    /// <see cref="octree">octree</see> structure whose
    /// dependencies will be reaped(destroyed). 
    /// </param>
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
                node.ClearChunk();
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
                node.ClearChunk();
            }
            node = ref octree.nodes[node.parent];
        }
    }

    internal struct ConstrainedLL<T>{
        public Node[] nodes;
        public uint length;
        public uint capacity;

        public ref uint head => ref nodes[0].next;
        public ref uint free => ref nodes[0].prev;

        public ConstrainedLL(uint size){
            length = 0;
            capacity = size;
            nodes = new Node[size];

            nodes[0].prev = 1; //FirstFreeNode
            nodes[0].next = 0; //FirstNode
        }

        public void Release(){
            nodes = null;
            length = 0;
            capacity = 0;
        }

        public uint Enqueue(T node){
            if(length + 1 >= capacity) {
                return 0;
            }

            uint freeNode = free; //Free Head Node
            uint nextNode = nodes[freeNode].next == 0 ? freeNode + 1 : nodes[freeNode].next;
            free = nextNode;

            nextNode = length == 0 ? freeNode : nodes[0].next;
            nodes[freeNode] = new Node{
                prev = nodes[nextNode].prev,
                next = nextNode,
                Value = node,
            };
            nodes[nodes[nextNode].prev].next = freeNode;
            nodes[nextNode].prev = freeNode;
            head = freeNode;

            length++;
            return freeNode;
        }

        public void RemoveNode(uint index){
            if(index == 0 || length == 0) return;

            uint prev = nodes[index].prev;
            uint next = nodes[index].next;
            if(head == index) 
                head = next;

            nodes[prev].next = next;
            nodes[next].prev = prev;
            //This is so if T is an object, it can be garbage collected
            nodes[index] = (Node)default(Node);

            nodes[index].next = free;
            free = index;

            length--;
            return;
        }

        public readonly uint Head(){ return nodes[0].next; }
        public readonly uint Next(uint index){ return nodes[index].next; }

        public int GetCoord(Func<T, bool> cb){
            uint cur = head;
            for(int i = 0; i < length; i++){
                if(cb(nodes[cur].Value))
                    return (int)cur;
                cur = nodes[cur].next;
            }
            return -1;
        }

        public struct Node{
            public uint next;
            public uint prev;
            public T Value;
        }
    }

    /// <summary>
    /// A struct defining an octree, a tree in which every internal node has exactly 8 children and
    /// each parent node contains, in 3D space, all of its children such that the root contains the 
    /// entire tree. The Octree is the primary structure used for LoD terrain generation and management.
    /// </summary>
    public struct Octree{
        /// <summary>
        /// An array containing the octree. Each node may point to other nodes in a logical octree structure
        /// but it does not remember which nodes represent roots, branches, or terrain chunks. It is the job of the caller
        /// to remember what they added and removed.
        /// </summary>
        public Node[] nodes;
        /// <summary>
        /// Converts a grid coordinates into the chunk coordinate relative to a specific chunk size. Conversion is done
        /// with explicitly only integer mathematics and thus is not subject to floating point errors.
        /// </summary>
        /// <remarks>
        /// One can obtain the chunk coordinate for a specific depth by setting <paramref name="chunkSize"/> to
        /// <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> * (2^<see cref="TerrainChunk.depth"/>)>:
        /// </remarks>
        /// <param name="GCoord">The position whose relative chunk coordinate is sampled</param>
        /// <param name="chunkSize">The size of chunk the outputted chunk coordinate is scaled to.</param>
        /// <returns>The chunk coordinate of the point relative to <paramref name="chunkSize"/></returns>
        public static int3 FloorCCoord(int3 GCoord, int chunkSize){ 
            int3 offset = ((GCoord % chunkSize) + chunkSize) % chunkSize; // GCoord %% chunkSize
            return (GCoord - offset) / chunkSize; 
        }

        /// <summary>
        /// Gets the maximum amount of octree nodes in an octree of a given depth, balance factor, and chunk radius.
        /// This is calculated by summing the maximum amount of nodes in each layer from 0 to <paramref name="depth"/>.
        /// <seealso cref="GetAxisChunksDepth"/>.
        /// </summary>
        /// <param name="depth">The maximum depth of the octree</param>
        /// <param name="balanceF">The balance factor of the octree(<paramref name="balanceF"/> + 1 : 1))</param>
        /// <param name="chunksRadius">The chunk radius around the viewer of layer <paramref name="depth"/> = 0</param>
        /// <returns>The maximum number of nodes in the given octree settings</returns>
        public static int GetMaxNodes(int depth, int balanceF, int chunksRadius){
            int numChunks = 0;
            int pLayerSize = 0;
            for(int i = 0; i < depth; i++){
                int layerDiameter = GetAxisChunksDepth(i, balanceF, (uint)chunksRadius);
                int LayerSize = layerDiameter * layerDiameter * layerDiameter;
                numChunks += LayerSize - pLayerSize;
                pLayerSize = LayerSize / 8;
            }
            return numChunks;
        }
        
        /// <summary> Returns the depth of the chunk containing a position at a 
        /// given distance from the viewer. </summary>
        /// <param name="chunkDist">The distance in chunk space from the viewer</param>
        /// <param name="balanceF">The balance factor of the octree(<paramref name="balanceF"/> + 1 : 1))</param>
        /// <param name="chunksRadius">The chunk radius around the viewer of layer depth = 0</param>
        /// <returns>The depth of the chunk at this distance</returns>
        public static int GetDepthOfDistance(float chunkDist, int balanceF, uint chunksRadius) {
            int depth;
            for(depth = 0; depth < 32; depth++){
                int size = 1 << depth;
                int balance = size >> (balanceF - 1);
                int dist = Mathf.FloorToInt(chunkDist / size) * size;
                if (dist - chunksRadius < balance)
                    break;
            }
            return depth;
        }

        /// <summary>
            /// Gets the diameter of an octree defined by balanceFactor, and chunkRadius for nodes at a specified depth. 
            /// This is calculated by determining the maximum radius of chunks at the given depth which can be 
            /// balanced given any viewer position. 
            /// </summary>
            /// <remarks>
            /// Since the octree is centered around a viewer, nodes of a certain depth form a cubic shell around the viewer.
            /// The diameter thus is the side length of this cube. 
            /// </remarks>
            /// <param name="depth">The specific depth of the given octree whose maximum node diameter is queried</param>
            /// <param name="balanceF">The balance factor of the octree(<paramref name="balanceF"/> + 1 : 1)</param>
            /// <param name="chunksRadius">The chunk radius around the viewer of layer <paramref name="depth"/> = 0</param>
            /// <returns>
            /// The maximum diameter in terms of the amount of nodes of the specified <paramref name="depth"/> that can exist
            /// given the octree's settings.
            /// </returns>
            public static int GetAxisChunksDepth(int depth, int balanceF, uint chunksRadius) {
                int radius = (int)((1 << math.max(depth - balanceF + 2, 0)) + chunksRadius);
                int remainder = (chunksRadius % (1 << depth)) != 0 ? 1 : 0;
                int pDiam = (radius >> depth) + remainder + 1;
                return pDiam * 2;
            }

        /// <summary>
        /// Creates an octree with the specified settings--depth, balance factor, and chunk radius.
        /// </summary>
        /// <param name="depth">The maximum depth of the octree</param>
        /// <param name="balanceF">The balance factor of the octree. See <see cref="WorldConfig.Quality.Terrain.Balance"/> for more info.</param>
        /// <param name="chunksRadius">The minimum radius of the smallest chunks within the octree. See <see cref="WorldConfig.Quality.Terrain.MinChunkRadius"/> for more info.</param>
        public Octree(int depth, int balanceF, int chunksRadius){
            int numChunks = GetMaxNodes(depth, balanceF, chunksRadius);
            nodes = new Node[4*numChunks+1];
            nodes[0].child = 1; //free list
        }

        /// <summary>
        /// Inserts a node into the octree. The node is placed into the first
        /// open entry within the octree. This does not connect it to its parent, children, siblings,
        /// or mark it as a leaf node. It is expected that <paramref name="octree"/> will already
        /// be initialized(connected) by the caller.
        /// </summary>
        /// <param name="octree">The new node that is placed within the octree</param>
        /// <returns>The index within the octree that the node is inserted into</returns>
        public uint AddNode(Node octree){
            uint freeNode = nodes[0].child; //Free Head Node
            uint nextNode = nodes[freeNode].child == 0 ? freeNode + 1 : nodes[freeNode].child;
            nodes[0].child = nextNode;

            nodes[freeNode] = octree;
            return freeNode;
        }

        /// <summary>
        /// Removes a node from the octree, freeing space for new nodes to be inserted.
        /// The information is not cleared but should not be read from.
        /// </summary>
        /// <param name="index">
        /// The index within the octree of the node that should be removed. It is the caller's
        /// responsibility to only call this on allocated nodes. 
        /// </param>
        public void RemoveNode(uint index){
            nodes[index].child = nodes[0].child;
            nodes[0].child = index;
        }

        /// <summary>
        /// An octree node that contains information on its orientation, hierarchical relation to 
        /// other chunks as well as the index of its terrain chunk if it has one.
        /// </summary>
        public struct Node{
            /// <summary>
            /// The origin in grid space of the chunk. This is the coordinate
            /// of the bottom-left corner of the bounds of the chunk.
            /// </summary>
            public int3 origin; 
            /// <summary>
            /// The size of the chunk in grid space. This is equivalent to 
            /// <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> * 
            /// (2^<see cref="TerrainChunk.depth"/>).
            /// </summary>
            public uint size;
            /// <summary>
            /// The index within <see cref="nodes"/> of the first child of the node if it
            /// is not a leaf node. If it is a leaf node, this value is 0.
            /// </summary>
            public uint child; 
            /// <summary>
            /// The index within <see cref="nodes"/> of the sibling of the node. Its sibling
            /// will reference a different sibling forming a circular linked list of length 8.
            /// </summary>
            public uint sibling;
            /// <summary>
            /// The index within <see cref="nodes"/> of the parent of the node. If the node is a root
            /// of the octree, this value is 0.
            /// </summary>
            public uint parent;
            /// <summary>
            /// Whether or not the node is a leaf node. A leaf node is a node that does not have any children,
            /// or when <see cref="child"/> is 0.
            /// </summary>
            public readonly bool IsLeaf => child == 0;

            private uint chunkData;
            /// <summary>
            /// Clears the chunk data of the node. The default state is that
            /// HasChunk is false and IsComplete is false.
            /// </summary>
            public void ClearChunk(){chunkData = 0;}
            /// <summary>
            /// Whether or not the node has a chunk associated with it.
            /// </summary>
            public readonly bool HasChunk => chunkData != 0;
            /// <summary>
            /// If it has a chunk, whether or not the chunk is marked as 
            /// complete. A chunk is marked as complete if it has attempted to
            /// generate a mesh.
            /// </summary>
            public bool IsComplete{
                readonly get => (chunkData & 0x80000000) != 0;
                set => chunkData = value ? chunkData | 0x80000000 : chunkData & 0x7FFFFFFF;
            }

            /// <summary>
            /// The index of the chunk within the <see cref="ConstrainedLL{T}">chunk list</see> of the octree.
            /// </summary>
            public uint Chunk{
                readonly get => chunkData & 0x7FFFFFFF;
                set => chunkData = (value & 0x7FFFFFFF) | (chunkData & 0x80000000);
            }

            /// <summary>
            /// Obtains the maximum component distance of <paramref name="GCoord"/> to the bounds of the chunk. The 
            /// max component distance is the maximum of the distances along each dimension. If the point
            /// is within the bounds of the chunk, the max distance is 0. 
            /// </summary>
            /// <param name="GCoord">The coordinate of the point whose max distance from the chunk is queried</param>
            /// <returns>The maximum component distance from the chunk's bounds to the point</returns>
            public int GetMaxDist(int3 GCoord){
                int3 origin = this.origin;
                int3 end = this.origin + (int)size;
                int3 dist = math.abs(math.clamp(GCoord, origin, end) - GCoord);
                return math.cmax(dist);
            }
        }
    }
}}

