using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using System;
using System.Collections.Concurrent;
using UnityEngine.Profiling;
using Arterra.Configuration;
using Arterra.Core.Storage;
using Arterra.Core;

namespace Arterra.Engine.Terrain{
    /// <summary>
    /// Octree Terrain is a singleton class that drives all generation and generation
    /// based events in the game. It is responsible for scheduling when generation 
    /// tasks are executed and identifying when the current generation needs to update.
    /// 
    /// System tasks connected to the games frame-by-frame update loop should hook into either
    /// MainLoopUpdateTasks, MainLateUpdateTasks, or MainFixedUpdateTasks. This is so that
    /// these static systems can hook into the update loop without needing to be a MonoBehaviour.
    /// </summary>
    public class OctreeTerrain : Core.ArterraRuntime.IUpdateSubscriber {
        public static OctreeTerrain instance {get; private set;}
        private static Configuration.Quality.Terrain s;
        /// <summary> The root octree structure responsible for dividing the world into
        /// <see cref="TerrainChunk"/>s in a manner according to the rules defined by
        /// <see cref="BalancedOctree"/> </summary>
        public static BalancedOctree octree;
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
        public static int3 ViewPosGS => ViewPosCS * s.mapChunkSize + s.mapChunkSize / 2;
        /// <summary> 
        /// The transform of the viewer around which
        /// generation(the octree) is centered.
        /// </summary>
        public static Transform viewer;
        public static Transform root;
        private int3 prevViewerPos;
        public bool Active {get => true; set{ return; }}

        public static void Initialize() {
            s = Config.CURRENT.Quality.Terrain.value;
            root = new GameObject("GeneratedContent").transform;

            instance = new OctreeTerrain();
            octree = new BalancedOctree(
                s.MaxDepth, s.Balance,
                s.MinChunkRadius, s.mapChunkSize
            );

            UpdateViewerPos();
            octree.Initialize();
            octree.ForEachChunk(chunk => chunk.Update());
            ArterraRuntime.MainLoopUpdateTasks.Enqueue(instance);
        }

        public static void Disable() => octree.ForEachChunk(chunk => chunk.Destroy());

#if UNITY_EDITOR
        public static void OnDrawGizmos() {
            UnityEditor.SceneView sceneView = UnityEditor.SceneView.lastActiveSceneView;
            float3 sceneViewPos = sceneView.camera.transform.position;
            int3 GCoord = (int3)CPUMapManager.WSToGS(math.floor(sceneViewPos));
            BalancedOctree.Node node = new() { };
            for (int depth = s.MaxDepth; depth >= 0; depth--) {
                int size = s.mapChunkSize * (1 << depth);
                int3 MCoord = ((GCoord % size) + size) % size;
                int3 CCoord = (GCoord - MCoord) / s.mapChunkSize;
                node.origin = CCoord * s.mapChunkSize;
                node.size = (uint)size;
                if (octree.IsBalanced(ref node)) break;
            }

            Gizmos.color = Color.white;
            Gizmos.DrawWireCube(CPUMapManager.GSToWS(node.origin + (float3)node.size / 2), (float3)node.size * s.lerpScale);
            //Debug.Log(node.origin/s.mapChunkSize);
            Indicators.OnDrawGizmos();
        }
#endif

        public void Update(MonoBehaviour mono) {
            VerifyChunks();
            octree.ForEachChunk(chunk => chunk.Update());
        }

        private void VerifyChunks() {
            int3 ViewerPosition = (int3)((float3)viewer.position / s.lerpScale);
            if (math.distance(prevViewerPos, ViewerPosition) < s.viewDistUpdate) return;
            prevViewerPos = ViewerPosition;
            UpdateViewerPos();

            //This is buffered because Verifying Leaf Chunks may change octree structure
            Queue<TerrainChunk> frameChunks = new Queue<TerrainChunk>();
            octree.ForEachChunk(chunk => frameChunks.Enqueue(chunk));
            while (frameChunks.Count > 0) {
                TerrainChunk chunk = frameChunks.Dequeue();
                if (!chunk.active) continue;
                chunk.VerifyChunk();
            }
        }

        private static void UpdateViewerPos() {
            int3 ViewerPosition = (int3)((float3)viewer.position / s.lerpScale + s.mapChunkSize / 2);
            int3 intraOffset = ((ViewerPosition % s.mapChunkSize) + s.mapChunkSize) % s.mapChunkSize;
            ViewPosCS = (ViewerPosition - intraOffset) / s.mapChunkSize;
        }

        /// <summary>Gets the lowest/first state any chunk in the dependent region is blocked on.
        /// This means for any given chunk in the processed region, the returned status for any
        /// sub-state can never be greater than the sub-state of that chunk. </summary>
        /// <param name="MinGCoord">The minimum coordinate in grid space of the dependent region</param>
        /// <param name="MaxGCoord">The maximum coordinate in grid space of the dependent region</param>
        /// <returns>The aggregate minimal status</returns>
        public static TerrainChunk.Status GetRegionMinimalStatus(int3 MinGCoord, int3 MaxGCoord) {
            TerrainChunk.Status status  = new () {
                ReadSaveState = TerrainChunk.Status.State.Finished,
                CreateMap = TerrainChunk.Status.State.Finished,
                SetMap = TerrainChunk.Status.State.Finished,
                ShrinkMap = TerrainChunk.Status.State.Finished,
                UpdateMesh = TerrainChunk.Status.State.Finished,
            };
            
            int3 size = MaxGCoord - MinGCoord;
            int3 center = MinGCoord + size/2;
            Bounds region = new Bounds((float3)center, (float3)size);
            octree.IterateChunksInRegion(region, c => {
                status.ReadSaveState = (TerrainChunk.Status.State)math.min((int)c.status.ReadSaveState, (int)status.ReadSaveState);
                status.CreateMap = (TerrainChunk.Status.State)math.min((int)c.status.CreateMap, (int)status.CreateMap);
                status.SetMap = (TerrainChunk.Status.State)math.min((int)c.status.SetMap, (int)status.SetMap);
                status.ShrinkMap = (TerrainChunk.Status.State)math.min((int)c.status.ShrinkMap, (int)status.ShrinkMap);
                status.UpdateMesh = (TerrainChunk.Status.State)math.min((int)c.status.UpdateMesh, (int)status.UpdateMesh);
            });
            return status;
        }

        /// <summary>
        /// An <see cref="Octree{T}"/> that infinitely tiles <see cref="TerrainChunk"/>s and is balanced using an 
        /// explicit balancing factor (e.g. 1:2, 1:3) to determine different depth regions.
        /// See <see cref="Octree{T}"/> for more information.
        /// </summary>
        public class BalancedOctree : Octree<TerrainChunk> {
            private int MinChunkRadius;
            private int Balance;
            private int RootDim => Balance == 1 ? 3 : 2;
            /// <summary> Creates an octree with the specified settings--depth, balance factor, and chunk radius. </summary>
            /// <param name="depth">The maximum depth of the octree</param>
            /// <param name="balanceF">The balance factor of the octree. See <see cref="Quality.Terrain.Balance"/> for more info.</param>
            /// <param name="chunksRadius">The minimum radius of the smallest chunks within the octree. See <see cref="Quality.Terrain.MinChunkRadius"/> for more info.</param>
            /// <param name="minChunkSize"> The size in grid space of the smallest chunk(depth = 0) handled by the octree, see <see cref="Quality.Terrain.mapChunkSize"/> for more info. </param>
            public BalancedOctree(int depth, int balanceF, int chunksRadius, int minChunkSize)
                : base(depth, minChunkSize, GetMaxNodes(depth, balanceF, chunksRadius)) {
                MinChunkRadius = chunksRadius;
                Balance = balanceF;
            }

            /// <summary> Populates the initial octree with root nodes (may not be 8) </summary>
            public void Initialize() => Initialize(RootDim);

            /// <summary> Gets the maximum amount of octree nodes in an octree of a given depth, balance factor, and chunk radius.
            /// This is calculated by summing the maximum amount of nodes in each layer from 0 to <paramref name="depth"/>.
            /// <seealso cref="GetAxisChunksDepth"/>. </summary>
            /// <param name="depth">The maximum depth of the octree</param>
            /// <param name="balanceF">The balance factor of the octree(<paramref name="balanceF"/> + 1 : 1))</param>
            /// <param name="chunksRadius">The chunk radius around the viewer of layer <paramref name="depth"/> = 0</param>
            /// <returns>The maximum number of nodes in the given octree settings</returns>
            public static int GetMaxNodes(int depth, int balanceF, int chunksRadius) {
                int numChunks = 0;
                int pLayerSize = 0;
                for (int i = 0; i < depth; i++) {
                    int layerDiameter = GetAxisChunksDepth(i, balanceF, (uint)chunksRadius);
                    int LayerSize = layerDiameter * layerDiameter * layerDiameter;
                    numChunks += LayerSize - pLayerSize;
                    pLayerSize = LayerSize / 8;
                }
                return numChunks;
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
            /// Determines whether an octree node is balanced based on its current size
            /// and distance from the viewer. A node is balanced if it obeys the balance factor
            /// of the tree; if it is  1:(<see cref="Quality.Terrain.Balance">Balance</see> + 1) balanced.
            /// </summary>
            /// <param name="node">The octree node whose current state is tested to be balanced</param>
            /// <returns>Whether or not the node is balanced</returns>
            public override bool IsBalanced(ref Node node) {
                int balance = (int)(node.size >> (Balance - 1));
                return node.GetMaxDist(ViewPosGS) - MinChunkSize * MinChunkRadius >= balance;
            }

            /// <summary> Associates a <see cref="TerrainChunk"/> to the given octree node.
            /// See <see cref="Octree{T}.AddTerrainChunk(uint)"/> for more info. </summary>
            /// <param name="octreeIndex">The index within <see cref="Octree{T}.nodes"/> of the node whose chunk is being created.</param>
            protected override void AddTerrainChunk(uint octreeIndex) {
                ref Node node = ref nodes[octreeIndex];
                TerrainChunk nChunk;
                if (node.size == MinChunkSize) {
                    nChunk = new TerrainChunk.RealChunk(root, node.origin, (int)node.size, octreeIndex);
                } else {
                    nChunk = new TerrainChunk.VisualChunk(root, node.origin, (int)node.size, octreeIndex);
                }
                node.Chunk = chunks.Enqueue(nChunk);
                node.IsComplete = false;
            }

            /// <summary> Remaps the root node of the <see cref="BalancedOctree"/> to
            /// the closest exclusive(non-overlapping) position around the viewer.
            /// See <see cref="Octree{T}.RemapRoot(uint)"/> for more info. </summary>
            /// <param name="octreeNode">The index within <see cref="Octree{T}.nodes"/> of the 
            /// root node being remapped</param>
            /// <returns>Whether the chunk was remapped or not. A chunk is not remapped
            /// if it currently occupies the closest exclusive position. </returns>
            protected override bool RemapRoot(uint octreeNode) {
                ref Node node = ref nodes[octreeNode];
                int maxChunkSize = MinChunkSize * (1 << MaxDepth);
                int3 VCoord = FloorCCoord(ViewPosGS - maxChunkSize / 2, maxChunkSize);
                int3 offset = ((node.origin / maxChunkSize - VCoord) % RootDim + RootDim) % RootDim;

                int3 newOrigin = (VCoord + offset) * maxChunkSize;
                if (newOrigin.Equals(node.origin)) return true;

                //Force destroy it without creating zombies
                if (!node.IsLeaf) DestroySubtree(node.child);
                if (node.HasChunk) {
                    chunks.nodes[node.Chunk].Value.Destroy();
                    chunks.RemoveNode(node.Chunk);
                    node.ClearChunk();
                }
                node.origin = newOrigin;
                BuildTree(octreeNode);
                return false;
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
            public uint GetNeighborDepths(uint index) {
                Node node = octree.nodes[index];
                int3 delta = (int3)math.sign(node.origin - ViewPosGS);
                uint neighborDepths = 0;
                for (int i = 0; i < 3; i++) {
                    int3 axisDelta = math.select(0, delta, new bool3(i == 0, i == 1, i == 2));
                    int3 origin = node.origin + axisDelta * (int)node.size; int dDepth;
                    for (dDepth = 1; dDepth <= s.Balance; dDepth++) {
                        int parentSize = (int)node.size << dDepth;
                        int3 nOrigin = origin - ((origin % parentSize) + parentSize) % parentSize; //make sure offset is not off -> get its parent's origin
                        Node neighbor = new Node { origin = nOrigin, size = (uint)parentSize };
                        if (!IsBalanced(ref neighbor)) break;
                    }
                    dDepth--;
                    neighborDepths |= ((uint)dDepth & 0x7F) << i * 8;
                    if (dDepth > 0) neighborDepths |= (uint)(math.cmax(axisDelta) & 0x1) << (i * 8 + 7);
                }
                return neighborDepths;
            }

            /// <summary>
            /// Determines whether an octree node is bordering a chunk of a larger size.
            /// This is determined by checking if its neighbor farthest from the viewer is
            /// balanced if it were of a larger size than the current chunk.
            /// </summary> <param name="index"> The index of the octree node within the
            /// <see cref="octree">octree</see> structure.  </param>
            /// <returns>Whether or not the node is bordering a larger chunk</returns>
            public bool IsBordering(int index) {
                Node node = nodes[index];
                int3 nOrigin = node.origin + ((int3)math.sign(node.origin - ViewPosGS)) * (int)node.size;
                int parentSize = (int)node.size * 2;
                nOrigin -= ((nOrigin % parentSize) + parentSize) % parentSize; //make sure offset is not off -> get its parent's origin

                Node neighbor = new Node { origin = nOrigin, size = (uint)parentSize };
                return IsBalanced(ref neighbor);
            }
        }
    }
}

