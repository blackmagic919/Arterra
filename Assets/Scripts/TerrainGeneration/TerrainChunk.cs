using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Arterra.Utils;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain.Readback;
using static Arterra.Engine.Terrain.Readback.IVertFormat;
using Arterra.Core.Storage;
using Arterra.Engine.Rendering;
using System.Threading.Tasks;
using Arterra.Core;
using static Arterra.Core.Storage.SharedResourceManager;
using MeshGenerator = Arterra.Engine.Terrain.MeshGeneration.Creator;

namespace Arterra.Engine.Terrain{

    using static OctreeTerrain;
    /// <summary>
    /// The unit of terrain generation, a chunk is(usually) a leaf node on the octree which bounds
    /// an mutually exclusive region of space relative to all other terrain chunks. Terrain chunks
    /// can be of various sizes and are created by the <see cref="OctreeTerrain"/> system to partition and
    /// manage all information pertaining to that specific region of space. When a different terrain chunk
    /// is created in the same region, the old chunk is destroyed and the new chunk is created in its place.
    /// </summary>
    public class TerrainChunk : IOctreeChunk{
        /// <summary> The index of the chunk's octree ndoe in the <see cref="OctreeTerrain.octree"/> </summary>
        public uint index;
        /// <summary> Whether or not the chunk is active. A chunk can exist and not be active if it's a zombie </summary>
        public bool active;
        /// <exclude />
        public bool Active => active;
        /// <summary> The origin of the chunk in grid space. The origin is the bottom left corner of the chunk</summary>
        public int3 origin;
        /// <summary> The chunk's coordinate in chunk space of the smallest terrain chunk(real chunks). This is the coordinate space
        /// such that each real chunk is assigned a unique integer coordinate. </summary>
        public int3 CCoord;
        /// <summary> The size of the chunk in grid space. The size is the length of one side of the chunk in grid units. </summary>
        /// <remarks> This is equivalent to <see cref="Quality.Terrain.mapChunkSize"/> * (2^<see cref="TerrainChunk.depth"/></remarks>
        public int size;
        ///<summary>The depth of the chunk in the <see cref="OctreeTerrain"/></summary>
        public int depth;
        /// <summary>Whether or not the chunk is a real chunk. Real chunks are the smallest chunks in the octree. A chunk is a real chunk if it's depth is 0. </summary>
        public bool IsRealChunk => depth == 0;

        /// <exclude />
        protected GameObject meshObject;
        /// <exclude />
        protected GeneratorInfo Generator;
        /// <exclude />
        protected Configuration.Quality.Terrain rSettings;
        /// <exclude />
        protected MeshRenderer meshRenderer;
        /// <exclude />
        protected readonly MeshFilter meshFilter;
        /// <exclude />
        protected readonly float IsoLevel;
        /// <exclude />
        protected readonly int mapChunkSize;
        /// <exclude />
        protected int mapSkipInc => 1 << depth;
        /// <summary> The manager for geo-shaded geometry generation </summary>
        public readonly SubChunkShaderGraph GeoShaders;
        /// <summary> The status of the chunk which describes the type of generation that needs to be done. </summary>
        public Status status;
        /// <summary> The depth of the chunk's neighbors. This is used to blend the chunk's mesh with its neighbors. </summary>
        protected uint neighborDepth;
        /// <summary> The transform of the mesh object as it is shown in the world </summary>
        public Transform MeshTransform => meshObject.transform;

        /// <summary>
        /// A bitmap container describing the types of requested tasks for the chunk. To request the
        /// chunk perform a specific task, one can set the corresponding bit information in the data field
        /// and the chunk will automatically perform the task on the next update cycle or when it is possible to do so.
        /// </summary>
        public struct Status {
            /// <summary> Whether or not the chunk needs to update its map data. Do not set this directly, <see cref="PublishMap"/>, <see cref="ShrinkMap"/>, and <see cref="SetMap"/>
            /// automatically set this value when they are set. </summary>
            public State UpdateMap {
                readonly get => (State)math.min((int)PublishMap, math.min((int)ShrinkMap, (int)SetMap));
                set {
                    PublishMap = value;
                    ShrinkMap = value;
                    SetMap = value;
                }
            }

            internal int _readSaveState;
            internal int _publishMap;
            internal int _shrinkMap;
            internal int _setMap;
            internal int _updateMesh;

            /// <summary> Whether or not the chunk has read its save state. </summary>
            public State ReadSaveState {
                readonly get => (State)_readSaveState;
                set => _readSaveState = (int)value;
            }

            /// <summary> Whether or not the chunk needs to regenerate the map data. This is true by default when creating a new chunk </summary>
            public State PublishMap {
                readonly get => (State)_publishMap;
                set => _publishMap = (int)value;
            }
            /// <summary> If it is of higher resolution, whether or not it can be shrunk down to a lower resolution. This is not used
            /// currently with an Octree model, but for a flat grid model, this would be used to shrink the map down to a lower resolution. </summary>
            public State ShrinkMap {
                readonly get => (State)_shrinkMap;
                set => _shrinkMap = (int)value;
            }
            /// <summary>
            /// Whether or not the chunk needs to copy its map information from the CPU to the GPU.
            /// To visually update chunk, it must be copied to the GPU.
            /// </summary>
            public State SetMap {
                readonly get => (State)_setMap;
                set => _setMap = (int)value;
            }
            /// <summary>
            /// Whether or not the chunk can recreate its mesh information. This will eventually allow the mesh to be updated
            /// when it reaches the proper update cycle to recieve out-of-bound information from chunks outside itself.
            /// This is true by default when creating a new chunk.
            /// </summary>
            public State UpdateMesh {
                readonly get => (State)_updateMesh;
                set => _updateMesh = (int)value;
            }

            /// <summary> The enum describing the current
            /// progress of any chunk update request.  </summary>
            public enum State {
                /// <summary> Whether or not the requested update is pending and
                /// waiting to be enqueued into the <see cref="RequestQueue">generation queue</see>
                /// during the next update cycle. </summary>
                Pending = 0,
                /// <summary> Whether or not the requested update is enqueued
                /// in the <see cref="RequestQueue">generation queue</see> and
                /// will be executed when it reaches the front of the queue</summary>
                InProgress = 1,
                /// <summary> Whether or not the requested update is finished.  </summary>
                Finished = 2,
                Failed = 3,
            }

            /// <summary> Progresses the given state to <see cref="State.Pending"/> only if it is currently <see cref="State.Finished"/> </summary>
            /// <param name="s">The state to progress</param>
            /// <returns>The current or pending state</returns>
            public static State Initiate(State s) => s == State.Finished ? State.Pending : s;
            /// <summary> Progresses the given state to <see cref="State.Finished"/> only if it is currently <see cref="State.InProgress"/> </summary>
            /// <param name="s">The state to progress</param>
            /// <returns>The current or pending state</returns>
            public static State Complete(State s) => s == State.InProgress ? State.Finished : s;
        }

        /// <summary>
        /// A container containing instances of systems pertinent to
        /// different processes in terrain generation
        /// </summary>
        protected readonly struct GeneratorInfo {
            public readonly AsyncMeshReadback MeshReadback;
            public readonly MeshGenerator MeshCreator;
            // Real chunks share their pipeline through the driver; visual LoDs own one directly.
            public readonly MapGenerationPipeline Pipeline;

            public GeneratorInfo(TerrainChunk chunk) {
                MeshCreator = new MeshGenerator();
                MeshReadback = new AsyncMeshReadback(chunk.meshObject.transform,
                    chunk.GetRelativeBoundsOS(chunk.origin, chunk.size));
                Pipeline = chunk.depth == 0 ? null : MapGenerationPipeline.Build(chunk.origin, chunk.depth, true);
            }
        }

        /// <summary>
        /// Creates a new terrain chunk with the the given origin and size. The newly created chunk
        /// will maintain a reference to its octree node, which it can use to make requests to update itself.
        /// In the game hierarchy, it will be a child of the given parent transform.
        /// </summary>
        /// <param name="parent">The parent in the gameobject heiharchy of the Terrain Chunk's gameobject</param>
        /// <param name="origin">The <see cref="origin"/> of the terrain chunk in grid space</param>
        /// <param name="size"> The size of the terrain chunk in grid space</param>
        /// <param name="octreeIndex">The index of the chunk's octree node in the <see cref="OctreeTerrain.octree"/> </param>
        public TerrainChunk(Transform parent, int3 origin, int size, uint octreeIndex) {
            rSettings = Config.CURRENT.Quality.Terrain.value;
            this.IsoLevel = rSettings.IsoLevel;
            this.mapChunkSize = rSettings.mapChunkSize;
            this.origin = origin;
            this.size = size;
            this.index = octreeIndex;
            this.active = true;

            //This is ok because origin is guaranteed to be a multiple of mapChunkSize by the Octree
            CCoord = origin / rSettings.mapChunkSize;
            depth = math.floorlog2(size / rSettings.mapChunkSize);
            neighborDepth = octree.GetNeighborDepths(index);

            meshObject = new GameObject("Terrain Chunk");
            meshObject.transform.localScale = Vector3.one * rSettings.lerpScale * (1 << depth);
            meshObject.transform.parent = parent;
            meshObject.transform.position = (float3)(origin - (float3)Vector3.one * (rSettings.mapChunkSize / 2f)) * rSettings.lerpScale;

            meshFilter = meshObject.AddComponent<MeshFilter>();
            meshRenderer = meshObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterials = Config.CURRENT.System.ReadBack.value.TerrainMats.ToArray();

            status = new Status {
                ReadSaveState = Status.State.Pending,
                PublishMap = Status.State.Pending,
                SetMap = Status.State.Finished,
                ShrinkMap = Status.State.Finished,
                UpdateMesh = Status.State.Pending,
            };
            if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                GeoShaders = new SubChunkShaderGraph(this);

            Generator = new GeneratorInfo(this);
        }

        /// <summary> Transforms a childs bounds given in world space to the local
        /// bounds and offset within the chunk. </summary>
        /// <param name="origin">The origin of the bounds</param>
        /// <param name="size">The size of the bounds</param>
        /// <returns>The Object Space Bounds</returns>
        public Bounds GetRelativeBoundsOS(float3 origin, float3 size) {
            float3 center = (origin + size / 2 - this.origin) / this.size;
            float3 length = size / this.size;
            return new Bounds(center * rSettings.mapChunkSize, length * rSettings.mapChunkSize);
        }

        /// <summary>
        /// Verifies whether the chunk's is still valid given the relative
        /// position of the viewer and the octree's state. If it is not it may destroy the chunk and replace it
        /// to become valid. If the chunk is a zombie this function will do nothing.
        /// </summary>
        /// <remarks>
        /// As this function is called whenever OctreeTerrain re-validates the terrain, it can
        /// be overrided to perform additional verification steps during this event.
        /// </remarks>
        public virtual void VerifyChunk() {
            if (!active) return;
            //These two functions are self-verifying, so they will only execute if necessary
            octree.SubdivideChunk(index);
            octree.MergeSiblings(index);

            uint nNeighbor = octree.GetNeighborDepths(index);
            if (nNeighbor == neighborDepth) return;
            neighborDepth = nNeighbor;
            status.UpdateMesh = Status.Initiate(status.UpdateMesh);
        }

        /// <summary>
        /// If the chunk is <see cref="active"/>, it will be deactivated, thus turning it into a zombie.
        /// Simultaneously, all dependencies(zombies) waiting on the chunk will be reaped, <seealso cref="Octree{T}.ReapChunk(uint)"/>
        /// </summary>
        public void Kill() {
            if (!active) return;
            active = false;
            octree.ReapChunk(index);
        }

        /// <summary>
        /// Releases all information associated with the chunk in various systems
        /// and destroys the gameobject. Call this to permanently destroy a chunk, whose
        /// information should not be referenced after that point.
        /// </summary>
        public void Destroy() {
            active = false;
            ReleaseChunk();
#if UNITY_EDITOR
            GameObject.DestroyImmediate(meshObject);
#else
        GameObject.Destroy(meshObject);
#endif
        }

        /// <summary>
        /// An overridable function that is called when the chunk is destroyed. If a terrain chunk variant contains
        /// additional information that needs to be released, it should override this function to release that information.
        /// </summary>
        public virtual void ReleaseChunk() {
            ClearFilter(); //Releases Mesh Data
            GeoShaders?.Release(); //Release geoShader Geometry
            Generator.Pipeline?.Release();
            Generator.MeshReadback?.ReleaseAllGeometry(); //Release base geometry on GPU
        }

        /// <summary>
        /// Requests the chunk to reflect its CPU-side map data visually by setting status flags,
        /// Specifically the flags to <see cref="Status.SetMap"/> and <see cref="Status.UpdateMesh"/>
        /// </summary>
        public void ReflectChunk() {
            if (!active) return; //Can't reflect a chunk that hasn't been read yet
            status.SetMap = Status.Initiate(status.SetMap);
            status.UpdateMesh = Status.Initiate(status.UpdateMesh);
        }

        /// <summary>
        /// Requests the chunk to reflect its CPU-side map data visually by setting status flags,
        /// similar to <see cref="ReflectChunk"/>, but is a thread safe operation.
        /// </summary>
        public void ReflectChunkThread() {
            if (!active) return;
            System.Threading.Interlocked.CompareExchange(ref status._setMap, (int)Status.State.Pending, (int)Status.State.Finished);
            System.Threading.Interlocked.CompareExchange(ref status._updateMesh, (int)Status.State.Pending, (int)Status.State.Finished);
        }

        /// <summary>Overridable event when deleting CPU-cached mesh information </summary>
        public void ClearFilter() {
            if (meshFilter == null) return;
            if (meshFilter.sharedMesh != null) {
#if UNITY_EDITOR
                UnityEngine.Object.DestroyImmediate(meshFilter.sharedMesh);
#else
                UnityEngine.Object.Destroy(meshFilter.sharedMesh);
#endif
            }
            meshFilter.sharedMesh = null;
        }

        /// <summary>
        /// Overridable event triggered within update-loop. Primarily used to queue generation tasks
        /// based on the chunk's status flags. Expensive operations should be queued in the <see cref="RequestQueue"/>
        /// </summary>
        public virtual void Update() {}

        /// <summary>Publish available map data into the client GPU cache.</summary>
        protected virtual void PublishMap(Action callback = null) { }
        /// <summary> The generation task which creates the mesh information for the chunk. This is the final step in generating the chunk's information.
        /// Optionally chunks will place structures and generate geoshaded geometry if they have cached structure information from <see cref="ReadMapData"/>
        /// and their depth is less than or equal to <see cref="Quality.GeoShaderSettings.MaxGeoShaderDepth"/> respectively.
        /// </summary> <param name="UpdateCallback">The callback function that's returned the mesh constructor once the mesh has been readback</param>
        protected virtual void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) { }
        /// <exclude />
        protected void OnChunkCreated(ReadbackTask<TVert>.SharedMeshInfo meshInfo) {
            ClearFilter();
            meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
            meshInfo.Release();
        }

        /// <summary> Whether or not <see cref="CreateMesh"/> can be called safely for this chunk.</summary>
        /// <returns></returns>
        protected virtual bool CanCreateMesh() {
            int3 MinGCoord = origin - mapChunkSize; int3 MaxGCoord = origin + size + mapChunkSize;
            Status dependentStatus = GetRegionMinimalStatus(MinGCoord, MaxGCoord);
            if(dependentStatus.PublishMap != Status.State.Finished) return false;
            return GPUMapManager.TryAccessRegionWithGraphics(MinGCoord, MaxGCoord, GraphicsRendering);
        }

        /// <summary>
        /// A real chunk is a chunk of the lowest <see cref="depth"/>, therefore the smallest size (equal to <see cref="Quality.Terrain.mapChunkSize"/>).
        /// They are the chunks closest to the viewer and the only chunk that is capable of lossless sampling so it is the only chunk that maintains a copy of its map information
        /// on the CPU. By extension, it is the only chunk that is capable of terrain interaction, collision, and entity pathfinding. With respect to the interactable game
        /// environment, it is all that exists.
        /// </summary>
        public class RealChunk : TerrainChunk, IChunkConsumer {
            private readonly ChunkGenerationDriver.Subscription subscription;

            public RealChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex) {
                subscription = ChunkGenerationDriver.Local.Allocate(this, CCoord);
                status.ReadSaveState = Status.State.Finished;
            }

            void IChunkConsumer.ReflectChunk() => ReflectChunk();

            public override void ReleaseChunk() {
                subscription?.Release();
                base.ReleaseChunk();
            }

            public override void Update() {
                if (status.PublishMap == Status.State.Pending) {
                    status.PublishMap = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => PublishMap(), id = (int)priorities.generation,
                        cancelToken = () => !Active,
                        processToken = () => subscription.Chunk.HasGraphicsMap || subscription.Ready.IsCompleted
                    });
                } else if (status.PublishMap == Status.State.Finished && status.SetMap == Status.State.Pending) {
                    status.SetMap = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => SetChunkData(), id = (int)priorities.generation, cancelToken = () => !Active
                    });
                }
                if (status.UpdateMesh == Status.State.Pending) {
                    status.UpdateMesh = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => CreateMesh(OnChunkCreated), id = (int)priorities.mesh,
                        cancelToken = () => !Active || status.PublishMap == Status.State.Failed,
                        processToken = CanCreateMesh
                    });
                }
            }

            protected override void PublishMap(Action callback = null) {
                try {
                    var chunk = subscription.Chunk;
                    if (chunk.Ready?.IsCompleted == true) chunk.Ready.GetAwaiter().GetResult();
                    if (!chunk.HasGraphicsMap && !chunk.HasMap)
                        throw new InvalidOperationException("Chunk map is not ready for publication.");

                    var pipeline = chunk.Generation.Pipeline;
                    var graphics = GraphicsGeneration;
                    if (chunk.HasGraphicsMap) pipeline.CopyTo(graphics.Work.Transfer);
                    else graphics.SetBufferData(graphics.Work.Transfer, chunk.ChunkData, chunk.Offset, 0, chunk.Length);
                    if (GPUMapManager.RegisterChunkReal(chunk.CCoord, 0, graphics.Work.Transfer) < 0)
                        throw new InvalidOperationException("Chunk is outside the client's GPU cache region.");
                    status.UpdateMap = Status.State.Finished;
                    _ = FinishPublicationAsync();
                    callback?.Invoke();

                    async Task FinishPublicationAsync() {
                        try {
                            // Loading unwinds only after its native writes and entity planning finish.
                            // Ready can be canceled earlier during offloading.
                            await chunk.Generation.Loading;
                            if (pipeline?.Active == true && pipeline.HasRetainedMap)
                                pipeline.ReleaseMap();
                            chunk.Ready.GetAwaiter().GetResult();
                        } catch (OperationCanceledException) { }
                        catch (Exception exception) {
                            if (!Active) return;
                            status.PublishMap = Status.State.Failed;
                            status.UpdateMesh = Status.State.Failed;
                            Debug.LogException(exception);
                        }
                    }
                } catch (Exception exception) {
                    status.PublishMap = Status.State.Failed;
                    status.UpdateMesh = Status.State.Failed;
                    Debug.LogException(exception);
                }
            }

            protected override void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) {
                Generator.MeshCreator.GenerateRealMesh(CCoord, IsoLevel, mapChunkSize, neighborDepth);
                if (!GPUMapManager.TryBindRegionOperation(origin - mapChunkSize, origin + size + mapChunkSize, Generator.MeshCreator.GetGraphicsContext()))
                    throw new InvalidOperationException("Mesh generation requires an accessible map region.");
                ClearFilter(); octree.ReapChunk(index);

                Map.Creator.GeoGenOffsets bufferOffsets = Map.Creator.bufferOffsets;
                Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)ReadbackMaterial.water);

                if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                    GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)ReadbackMaterial.terrain]);
                status.UpdateMesh = Status.Complete(status.UpdateMesh);
            }

            private void SetChunkData(Action callback = null) {
                if (!CPUMapManager.TryGetChunkInfo(CCoord, out var map) || !map.HasMap) return;
                GraphicsGeneration.SetBufferData(GraphicsGeneration.Work.Transfer, map.ChunkData, map.Offset, 0, map.Length);
                GPUMapManager.RegisterChunkReal(CCoord, depth, GraphicsGeneration.Work.Transfer);
                status.UpdateMap = Status.Complete(status.UpdateMap);
                callback?.Invoke();
            }

            private void SimplifyMap(int skipInc, Action callback = null) {
                GPUMapManager.SimplifyChunk(CCoord, skipInc);
                status.UpdateMap = Status.Complete(status.UpdateMap);
                callback?.Invoke();
            }

        }

        /// <summary>
        /// A visual chunk is a chunk of a <see cref="depth"/> greater than zero. These chunks are purely visual and
        /// are not copied back to the CPU. This is because accurate map sampling is impossible with missing information,
        /// therefore, in terms of the interactable game-environment they do not exist.
        ///
        /// Visual chunks are divided into two logical types, normal and fake chunks. Normal visual chunks have cached map data on the GPU
        /// which allows them to contain atmospheric effects, and look in neighboring chunks to generate their mesh. Fake visual chunks are farther
        /// away from the viewer and do not have cached map data anywhere, their map data is generated on the fly to create a mesh and discarded immediately.
        /// Fake chunks hence only reflect a chunk's default map data while normal chunks are capable of displaying dirty map information loaded from storage.
        /// </summary>
        public class VisualChunk : TerrainChunk, IChunkConsumer {
            private readonly VisualChunkGenerationDriver.Subscription subscription;
            private Task<MapData[]> ReadSaveState;
            private int3 sOrigin;
            private int sChunkSize;
            private int mapHandle;
            private UnmanagedChunkReference unmanagedMap;

            /// <summary> Creates a new visual chunk with the the given origin and size. <seealso cref="TerrainChunk"/> </summary>
            public VisualChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex) {
                if (GPUMapManager.IsChunkRegisterable(CCoord, depth))
                    subscription = VisualChunkGenerationDriver.Local.Allocate(this, CCoord, depth);
                ReadSaveState = null;
                sOrigin = origin - mapSkipInc;
                sChunkSize = mapChunkSize + 3;
                mapHandle = -1;
            }

            /// <summary> Based upon the chunk's status, enqueues generation tasks to the <see cref="RequestQueue"/>
            /// to reflect the flags. The status is updated accordingly after the tasks are enqueued/finished. </summary>
            public override void Update() {
                base.Update();
                if (subscription?.Active == true && status.PublishMap == Status.State.Finished
                    && status.SetMap == Status.State.Pending) {
                    status.SetMap = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = ApplyMapChanges, id = (int)priorities.generation,
                        cancelToken = () => !Active,
                        processToken = () => GPUMapManager.TryAccessRegionWithGraphics(origin, origin + size, GraphicsGeneration)
                    });
                }
                if (status.ReadSaveState == Status.State.Pending) {
                   status.ReadSaveState = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => ReadSavedState(),
                        id = (int)priorities.propogation,
                        cancelToken = () => !this.Active,
                    });
                } if (status.PublishMap == Status.State.Pending) {
                    status.PublishMap = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => {
                            try { PublishMap(); }
                            catch (Exception exception) {
                                status.PublishMap = Status.State.Failed;
                                status.UpdateMesh = Status.State.Failed;
                                Debug.LogException(exception);
                            }
                        },
                        id = (int)priorities.generation,
                        cancelToken = () => !this.Active,
                        processToken = CanPublishMap,
                    });
                }
                if (status.UpdateMesh == Status.State.Pending) {
                    status.UpdateMesh = Status.State.InProgress;
                    ArterraRuntime.Register(new () {
                        task = () => CreateMesh(OnChunkCreated),
                        id = (int)priorities.mesh,
                        cancelToken = () => !this.Active || status.PublishMap == Status.State.Failed,
                        processToken = CanCreateMesh,
                    });
                }
            }

            void IChunkConsumer.ReflectChunk() => ReflectChunk();

            private void ApplyMapChanges() {
                var map = new MapData[mapChunkSize * mapChunkSize * mapChunkSize];
                foreach (var change in subscription.Changes) map[change.Key] = change.Value;
                subscription.Changes.Clear();
                Generator.Pipeline.MapCreator.SetMapInfo(mapChunkSize, 0, map);
                GPUMapManager.TranscribeMultiMap(GraphicsGeneration.Work.Transfer, CCoord, depth);
                status.SetMap = Status.State.Finished;
                status.UpdateMesh = Status.Initiate(status.UpdateMesh);
            }

            /// <summary> Normal visual chunks reference directly their GPU-side map information inside the <see cref="GPUMapManager"/>, and are subscribed so
            /// that it isn't released while the chunk is holding it. On release, it's necessary to unsubscribe to allow the map to be released </summary>
            public override void ReleaseChunk() {
                subscription?.Release();
                //if we are still holding onto the map handle, release it
                if (mapHandle != -1) GPUMapManager.UnsubscribeHandle(mapHandle);
                unmanagedMap?.Release(GraphicsContextId.Generation);
                unmanagedMap = null;
                base.ReleaseChunk();
            }
            private void ReadSavedState() {
                Generator.Pipeline.Start();
                ReadSaveState = subscription != null
                    ? VisualChunkGenerationDriver.Local.Source.ReadVisualAsync(subscription)
                    : Task.FromResult<MapData[]>(null);
                status.ReadSaveState = Status.Complete(status.ReadSaveState);
            }

            private bool CanPublishMap() {
                if (status.ReadSaveState != Status.State.Finished) return false;
                return ReadSaveState.IsCompleted && Generator.Pipeline.Completion.IsCompleted;
            }

            /// <summary>
            /// A chunk is a normal visual chunk if it can be registered in the <see cref="GPUMapManager"/>. Otherwise, it is a fake visual chunk.
            /// For a fake visual chunk, ReadMapData does nothing because it will be lost immediately after generation as it is not cached anywhere.
            /// For a normal visual chunk, ReadMapData generates the default map information and registeres it into the <see cref="GPUMapManager"/>,
            /// then it reads any dirty maps from storage and replaces the default map information within the <see cref="GPUMapManager"/>'s dictionary
            /// with the dirty map information.
            /// </summary> <param name="callback"><see cref="TerrainChunk.PublishMap(Action)"/></param>
            protected override void PublishMap(Action callback = null) {
                GraphicsResourceContext gpuContext = GraphicsGeneration;
                Generator.Pipeline.Completion.GetAwaiter().GetResult();
                ReadSaveState.GetAwaiter().GetResult(); //Copy to working buffer
                Generator.Pipeline.CopyTo(gpuContext.Work.Scratch, Map.Creator.bufferOffsets.mapStart);
                Generator.Pipeline.ReleaseMap();

                if (subscription == null || !GPUMapManager.IsChunkRegisterable(CCoord, depth)) {
                    subscription?.Release();
                    Map.Creator.GeoGenOffsets fakeOffsets = Map.Creator.bufferOffsets;
                    unmanagedMap = GPUMapManager.RegisterChunkUnmanaged(
                        gpuContext.Work.Scratch, fakeOffsets.mapStart, gpuContext.id);
                    if (unmanagedMap == null)
                        return;
                    status.UpdateMap = Status.State.Finished;
                    callback?.Invoke();
                    return;
                }

                Map.Creator.GeoGenOffsets bufferOffsets = Map.Creator.bufferOffsets;
                if (mapHandle != -1) GPUMapManager.UnsubscribeHandle(mapHandle);
                mapHandle = GPUMapManager.RegisterChunkVisual(CCoord, depth, gpuContext.Work.Scratch, bufferOffsets.mapStart);
                if (mapHandle == -1) return;

                MapData[] info;
                info = ReadSaveState.Result ?? new MapData[mapChunkSize * mapChunkSize * mapChunkSize];
                foreach (var change in subscription.Changes) info[change.Key] = change.Value;
                subscription.Changes.Clear();
                //Consume this resource to stop extra memory usage
                ReadSaveState.Dispose(); ReadSaveState = null;

                Generator.Pipeline.MapCreator.SetMapInfo(mapChunkSize, 0, info);
                GPUMapManager.TranscribeMultiMap(gpuContext.Work.Transfer, CCoord, depth);
                //Subscribe once more so the chunk can't be released while we hold its handle
                GPUMapManager.SubscribeHandle((uint)mapHandle);
                status.UpdateMap = Status.State.Finished;
                callback?.Invoke();
            }

            protected override bool CanCreateMesh() {
                if (status.PublishMap != Status.State.Finished || status.SetMap != Status.State.Finished) return false;
                if (unmanagedMap != null)
                    return unmanagedMap.CanAccess(GraphicsRendering);
                return base.CanCreateMesh();
            }

            /// <summary>
            /// A visual chunk is fake if it does not have any cached information in the <see cref="GPUMapManager"/>.
            /// If it is a fake visual chunk, it will generate the default map information and create the mesh.
            /// If it is a normal visual chunk, it will create the mesh using the cached map information in the <see cref="GPUMapManager"/>.
            /// </summary> <param name="UpdateCallback"><see cref="TerrainChunk.CreateMesh(Action{ReadbackTask{TVert}.SharedMeshInfo})"/></param>
            protected override void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) {
                if (unmanagedMap != null) {
                    GraphicsResourceContext meshContext = Generator.MeshCreator.GetGraphicsContext();
                    Map.Creator.GeoGenOffsets fakeOffsets = Map.Creator.bufferOffsets;
                    if (!unmanagedMap.CopyTo(meshContext, meshContext.Work.Scratch,
                        fakeOffsets.mapStart))
                        return;

                    Generator.MeshCreator.GenerateMesh(mapChunkSize, IsoLevel);
                    if (neighborDepth != 0)
                        Generator.MeshCreator.GenerateTransition(neighborDepth, mapChunkSize, IsoLevel);
                    unmanagedMap.Release(meshContext.id);
                    unmanagedMap = null;
                    FinishMeshCreation();
                    return;
                }

                if (mapHandle == -1)  return;
                int directAddress = GPUMapManager.GetHandle(mapHandle).Address;
                Generator.MeshCreator.GenerateVisualMesh(CCoord, directAddress, IsoLevel, mapChunkSize, depth, neighborDepth);
                if (!GPUMapManager.TryBindRegionOperation(origin - mapChunkSize, origin + size + mapChunkSize, Generator.MeshCreator.GetGraphicsContext()))
                    throw new InvalidOperationException("Mesh generation requires an accessible map region.");
                //Specifically bind the handle so we safeguard against replacement
                GPUMapManager.TryBindChunkOperation(mapHandle, Generator.MeshCreator.GetGraphicsContext()); 
                FinishMeshCreation();
            }

            private void FinishMeshCreation() {
                octree.ReapChunk(index);
                ClearFilter();

                Map.Creator.GeoGenOffsets bufferOffsets = Map.Creator.bufferOffsets;
                Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)ReadbackMaterial.water);

                if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                    GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)ReadbackMaterial.terrain]);
                status.UpdateMesh = Status.Complete(status.UpdateMesh);
            }
        }
    }
}
