using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace TerrainGeneration {
    /// <summary>A generic interface all chunks managed by the <see cref="Octree{T}"/>
    /// system must fulfill. </summary>
    public interface IOctreeChunk {
        /// <summary> Whether or not the chunk is active and recieving updates</summary>
        public bool Active { get; }
        /// <summary>An absolute command to destroy the chunk and 
        /// release all associated resources. </summary>
        public void Destroy();
        /// <summary>An indication to the chunk that it is in the process
        /// of being replaced and should no longer recieve updates. </summary>
        public void Kill();
    }
    /// <summary>
    /// A struct defining an octree, a tree in which every internal node has exactly 8 children and
    /// each parent node contains, in 3D space, all of its children such that the root contains the 
    /// entire tree. The Octree is the primary structure used for LoD terrain generation and management.
    /// </summary>
    public abstract class Octree<TChunk> where TChunk : IOctreeChunk {
        /// <summary> An array containing the octree. Each node may point to other nodes in a logical octree structure
        /// but it does not remember which nodes represent roots, branches, or terrain chunks. It is the job of the caller
        /// to remember what they added and removed. </summary>
        protected Node[] nodes;
        /// <exclude />
        protected ConstrainedLL<TChunk> chunks;
        /// <exclude />
        public int MaxDepth;
        /// <exclude />
        public int MinChunkSize;

        /// <summary>
        /// Creates an octree with the specified settings--depth, balance factor, and chunk radius.
        /// </summary>
        /// <param name="depth">The maximum depth of the octree</param>
        /// <param name="minChunkSize"> The size in grid space of the smallest chunk(depth = 0) handled by the octree, see <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> for more info. </param>
        /// <param name="numChunks"> The maximum amount of leaf chunks that can be held by the octree. </param>
        public Octree(int depth, int minChunkSize, int numChunks) {
            chunks = new ConstrainedLL<TChunk>((uint)numChunks * 2 + 1);
            nodes = new Node[4 * numChunks + 1];
            nodes[0].child = 1; //free list
            MinChunkSize = minChunkSize;
            MaxDepth = depth;
        }

        /// <summary>
        /// Determines whether an octree node is balanced based on its current size
        /// and distance from the viewer. A node is balanced if it obeys the balance factor
        /// of the tree; if it is  1:(<see cref="WorldConfig.Quality.Terrain.Balance">Balance</see> + 1) balanced.
        /// </summary>
        /// <param name="node">The octree node whose current state is tested to be balanced</param>
        /// <returns>Whether or not the node is balanced</returns>
        public abstract bool IsBalanced(ref Node node);
        /// <summary>Creates a real instance of the chunk type managed by this octree,
        /// associated to the given octree node.</summary>
        /// <param name="octreeIndex">The index within <see cref="nodes"/> of the node 
        /// associated with the newly created chunk</param>
        protected abstract void AddTerrainChunk(uint octreeIndex);
        /// <summary>A callback to address the situation when a root 
        /// node is no longer balanced(<see cref="IsBalanced"/>) </summary>
        /// <param name="octreeNode">The index within <see cref="nodes"/> of the
        /// root node no longer balanced</param>
        /// <returns>Whether or not to keep the current node.</returns>
        protected abstract bool RemapRoot(uint octreeNode);

        /// <summary> Populates the initial octree. </summary>
        /// <param name="numRtNodes">The number of root nodes
        /// to create. Multiple root nodes can enable seemless
        /// transitions around a target. </param>
        /// <param name="center">The center of the octree in grid space</param>
        protected virtual void Initialize(int numRtNodes = 1, int3 center = default) {
            int maxChunkSize = MinChunkSize * (1 << MaxDepth);
            Queue<uint> tree = new Queue<uint>();
            Node root = new Node {
                size = (uint)(maxChunkSize * numRtNodes),
                origin = center - (maxChunkSize * numRtNodes/2),
                child = 0
            };
            root.ClearChunk();

            AddOctreeChildren(ref root, 0, child => BuildTree(child), numRtNodes);
        }
        
        public virtual void Release(){
            ForEachChunk(chunk => chunk.Destroy());
            Array.Clear(nodes, 0, nodes.Length);
            nodes[0].child = 1;
            chunks.Release();
        }

        /// <summary> Executes a function for every real 
        /// leaf chunk in the octree. </summary>
        /// <param name="action">The function to perform.</param>
        public void ForEachChunk(Action<TChunk> action) {
            uint curChunk = chunks.Head();
            do {
                action(chunks.nodes[curChunk].Value);
                curChunk = chunks.Next(curChunk);
            } while (curChunk != chunks.Head());
        }

        public void ForEachActiveChunk(Action<TChunk> action) {
            uint curChunk = chunks.Head();
            do {
                if (chunks.nodes[curChunk].Value.Active)
                    action(chunks.nodes[curChunk].Value);
                curChunk = chunks.Next(curChunk);
            } while (curChunk != chunks.Head());
        }

        /// <summary> Retrieves all leaf chunks (including zombies)
        /// currently held by the octree </summary>
        /// <returns>An array containing all leaf chunks</returns>
        public TChunk[] GetAllChunks() {
            int count = 0;
            ForEachChunk(_ => count++);
            TChunk[] chunks = new TChunk[count];
            count = 0;

            ForEachChunk(chunk => {
                chunks[count] = chunk;
                count++;
            }); return chunks;
        }

        /// <summary> Retrieves all active leaf chunks
        /// currently held by the octree </summary>
        /// <returns>An array containing all leaf chunks</returns>
        public TChunk[] GetAllActiveChunks() {
            int count = 0;
            ForEachActiveChunk(_ => count++);
            TChunk[] chunks = new TChunk[count];
            count = 0;

            ForEachActiveChunk(chunk => {
                chunks[count] = chunk;
                count++;
            }); return chunks;
        }

        /// <summary>Subdivides the octree node to create a fully balanced subtree. </summary>
        /// <param name="root"></param>
        protected void BuildTree(uint root) {
            ref Node node = ref nodes[root];
            if (node.size <= MinChunkSize || IsBalanced(ref node)) {
                AddTerrainChunk(root);
                return;
            }

            AddOctreeChildren(ref node, root, child => BuildTree(child));
        }

        private void AddOctreeChildren(ref Node parent, uint parentIndex, Action<uint> OnAddChild, int cDim = 2) {
            uint childSize = parent.size / (uint)cDim; uint sibling = 0;
            int numChildren = cDim * cDim * cDim;
            for (int i = 0; i < numChildren; i++) {
                sibling = AddNode(new Node {
                    origin = parent.origin + new int3(i % cDim, i / cDim % cDim, i / (cDim * cDim) % cDim) * (int)childSize,
                    size = childSize,
                    sibling = sibling,
                    parent = parentIndex,
                    child = 0, //IsLeaf = true
                });
                nodes[sibling].ClearChunk();
                OnAddChild(sibling);
            }
            parent.child = sibling;
            for (int i = 0; i < 7; i++) sibling = nodes[sibling].sibling;
            nodes[sibling].sibling = parent.child;
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
        /// <see cref="nodes">octree</see> structure. 
        /// </param>
        public void SubdivideChunk(uint leafIndex) {
            ref Node node = ref nodes[leafIndex];
            if (IsBalanced(ref node) || node.size <= MinChunkSize) return;

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
        /// <see cref="nodes">octree</see> structure.
        /// </param>
        /// <returns>
        /// Whether or not the requested chunk was <b>unable</b> to be
        /// merged. Used for internal recursion purposes.
        /// </returns>
        public bool MergeSiblings(uint leaf) {
            Node node = nodes[leaf];
            if (!IsBalanced(ref nodes[node.parent])) { return true; }
            if (node.parent == 0) { return RemapRoot(leaf); }

            uint sibling = leaf;
            do {
                KillSubtree(sibling);
                sibling = nodes[sibling].sibling;
            } while (sibling != leaf);

            uint parent = nodes[leaf].parent;
            //If the parent is balanced, then it won't be subdivided
            if (MergeSiblings(parent)) {
                AddTerrainChunk(parent);
            }
            return false;
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
        /// <see cref="nodes">octree</see> structure whose
        /// dependencies will be reaped(destroyed). 
        /// </param>
        public void ReapChunk(uint octreeIndex) {
            ref Node node = ref nodes[octreeIndex];
            //If the chunk has already been reaped, it will still travel up the root path
            //but this in theory should do nothing
            node.IsComplete = true;
            if (!node.IsLeaf) DestroySubtree(node.child);
            node.child = 0; //IsLeaf = true
            MergeAncestry(node.parent);
        }

        /// <summary>Destroys all chunks and associated resources in a
        /// specific subtree of the octree. </summary>
        /// <param name="octreeIndex">The index within <see cref="nodes"/> of the
        /// node whose siblings, descendants, and siblings' descendants will be released. </param>
        protected void DestroySubtree(uint octreeIndex) {
            uint sibling = octreeIndex;
            do {
                uint nSibling = nodes[sibling].sibling;
                ref Node node = ref nodes[sibling];
                if (!node.IsLeaf) DestroySubtree(node.child);
                if (node.HasChunk) {
                    chunks.nodes[node.Chunk].Value.Destroy();
                    chunks.RemoveNode(node.Chunk);
                    node.ClearChunk();
                }
                RemoveNode(sibling);
                sibling = nSibling;
            } while (sibling != octreeIndex);
        }

        private void MergeAncestry(uint octreeIndex) {
            var self = this;
            bool IsSubtreeComplete(uint octreeIndex) {
                uint sibling = octreeIndex;
                if (octreeIndex == 0) return true;
                do {
                    ref Node node = ref self.nodes[sibling];
                    if (node.HasChunk) {
                        if (!node.IsComplete) return false;
                    } else if (!IsSubtreeComplete(node.child))
                        return false;
                    sibling = self.nodes[sibling].sibling;
                } while (sibling != octreeIndex);
                return true;
            }
            ref Node node = ref nodes[octreeIndex];
            while (node.parent != 0) {
                if (node.HasChunk && IsSubtreeComplete(node.child)) {
                    chunks.nodes[node.Chunk].Value.Destroy();
                    chunks.RemoveNode(node.Chunk);
                    node.ClearChunk();
                }
                node = ref nodes[node.parent];
            }
        }

        //Kills all chunks in the subtree turning them into zombies
        private void KillSubtree(uint octreeIndex) {
            ref Node node = ref nodes[octreeIndex];
            //This will automatically delete the subtree if it has one
            if (node.HasChunk) { chunks.nodes[node.Chunk].Value.Kill(); } 
            else if (!node.IsLeaf) {
                uint sibling = node.child;
                do {
                    KillSubtree(sibling);
                    sibling = nodes[sibling].sibling;
                } while (sibling != node.child);
            }
        }

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
        public static int3 FloorCCoord(int3 GCoord, int chunkSize) {
            int3 offset = ((GCoord % chunkSize) + chunkSize) % chunkSize; // GCoord %% chunkSize
            return (GCoord - offset) / chunkSize;
        }

        /// <summary> Returns the depth of the chunk containing a position at a 
        /// given distance from the viewer. </summary>
        /// <param name="chunkDist">The distance in chunk space from the viewer</param>
        /// <param name="balanceF">The balance factor of the octree(<paramref name="balanceF"/> + 1 : 1))</param>
        /// <param name="chunksRadius">The chunk radius around the viewer of layer depth = 0</param>
        /// <returns>The depth of the chunk at this distance</returns>
        public static int GetDepthOfDistance(float chunkDist, int balanceF, uint chunksRadius) {
            int depth;
            for (depth = 0; depth < 32; depth++) {
                int size = 1 << depth;
                int balance = size >> (balanceF - 1);
                int dist = Mathf.FloorToInt(chunkDist / size) * size;
                if (dist - chunksRadius < balance)
                    break;
            }
            return depth;
        }

        /// <summary>
        /// Inserts a node into the octree. The node is placed into the first
        /// open entry within the octree. This does not connect it to its parent, children, siblings,
        /// or mark it as a leaf node. It is expected that <paramref name="octree"/> will already
        /// be initialized(connected) by the caller.
        /// </summary>
        /// <param name="octree">The new node that is placed within the octree</param>
        /// <returns>The index within the octree that the node is inserted into</returns>
        public uint AddNode(Node octree) {
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
        public void RemoveNode(uint index) {
            nodes[index].child = nodes[0].child;
            nodes[0].child = index;
        }

        /// <summary>
        /// An octree node that contains information on its orientation, hierarchical relation to 
        /// other chunks as well as the index of its terrain chunk if it has one.
        /// </summary>
        public struct Node {
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
            public void ClearChunk() { chunkData = 0; }
            /// <summary>
            /// Whether or not the node has a chunk associated with it.
            /// </summary>
            public readonly bool HasChunk => chunkData != 0;
            /// <summary>
            /// If it has a chunk, whether or not the chunk is marked as 
            /// complete. A chunk is marked as complete if it has attempted to
            /// generate a mesh.
            /// </summary>
            public bool IsComplete {
                readonly get => (chunkData & 0x80000000) != 0;
                set => chunkData = value ? chunkData | 0x80000000 : chunkData & 0x7FFFFFFF;
            }

            /// <summary>
            /// The index of the chunk within the <see cref="ConstrainedLL{T}">chunk list</see> of the octree.
            /// </summary>
            public uint Chunk {
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
            public int GetMaxDist(int3 GCoord) {
                int3 origin = this.origin;
                int3 end = this.origin + (int)size;
                int3 dist = math.abs(math.clamp(GCoord, origin, end) - GCoord);
                return math.cmax(dist);
            }
        }
        /// <exclude />
        protected struct ConstrainedLL<T> {
            /// <exclude />
            public Node[] nodes;
            /// <exclude />
            public uint length;
            /// <exclude />
            public uint capacity;
            /// <exclude />
            public ref uint head => ref nodes[0].next;
            /// <exclude />
            public ref uint free => ref nodes[0].prev;
            /// <exclude />
            public ConstrainedLL(uint size) {
                length = 0;
                capacity = size;
                nodes = new Node[size];

                nodes[0].prev = 1; //FirstFreeNode
                nodes[0].next = 0; //FirstNode
            }
            /// <exclude />
            public void Release() {
                nodes = null;
                length = 0;
                capacity = 0;
            }
            /// <exclude />
            public uint Enqueue(T node) {
                if (length + 1 >= capacity) {
                    return 0;
                }

                uint freeNode = free; //Free Head Node
                uint nextNode = nodes[freeNode].next == 0 ? freeNode + 1 : nodes[freeNode].next;
                free = nextNode;

                nextNode = length == 0 ? freeNode : nodes[0].next;
                nodes[freeNode] = new Node {
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
            /// <exclude />
            public void RemoveNode(uint index) {
                if (index == 0 || length == 0) return;

                uint prev = nodes[index].prev;
                uint next = nodes[index].next;
                if (head == index)
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
            /// <exclude />
            public readonly uint Head() { return nodes[0].next; }
            /// <exclude />
            public readonly uint Next(uint index) { return nodes[index].next; }
            /// <exclude />
            public int GetCoord(Func<T, bool> cb) {
                uint cur = head;
                for (int i = 0; i < length; i++) {
                    if (cb(nodes[cur].Value))
                        return (int)cur;
                    cur = nodes[cur].next;
                }
                return -1;
            }
            /// <exclude />
            public struct Node {
                /// <exclude />
                public uint next;
                /// <exclude />
                public uint prev;
                /// <exclude />
                public T Value;
            }
        }
    }
}