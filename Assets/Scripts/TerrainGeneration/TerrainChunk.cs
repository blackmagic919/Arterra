using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using Utils;
using static TerrainGeneration.Readback.IVertFormat;
using WorldConfig;
using TerrainGeneration.Readback;
using MapStorage;
using System.Collections.Generic;

namespace TerrainGeneration{

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
        /// <remarks> This is equivalent to <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/> * (2^<see cref="TerrainChunk.depth"/></remarks>
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
        protected WorldConfig.Quality.Terrain rSettings;
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
            /// <summary> Whether or not the chunk needs to update its map data. Do not set this directly, <see cref="CreateMap"/>, <see cref="ShrinkMap"/>, and <see cref="SetMap"/>
            /// automatically set this value when they are set. </summary>
            public State UpdateMap {
                readonly get => (State)math.max((int)CreateMap, math.max((int)ShrinkMap, (int)SetMap));
                set {
                    CreateMap = value;
                    ShrinkMap = value;
                    SetMap = value;
                }
            }

            internal int _createMap;
            internal int _shrinkMap;
            internal int _setMap;
            internal int _updateMesh;
            internal int _canUpdateMesh;

            /// <summary> Whether or not the chunk needs to regenerate the map data. This is true by default when creating a new chunk </summary>
            public State CreateMap {
                readonly get => (State)_createMap;
                set => _createMap = (int)value;
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
            /// <summary>
            /// Whether or not the chunk can update its mesh data. Do not set this directly, <see cref="UpdateMesh"/> 
            /// automatically sets this value when the chunk is actually able to update its mesh data. 
            /// </summary>
            public State CanUpdateMesh {
                readonly get => (State)_canUpdateMesh;
                set => _canUpdateMesh = (int)value;
            }

            /// <summary> The enum describing the current 
            /// progress of any chunk update request.  </summary>
            public enum State {
                /// <summary> Whether or not the requested update is finished.  </summary>
                Finished = 0,
                /// <summary> Whether or not the requested update is pending and 
                /// waiting to be enqueued into the <see cref="RequestQueue">generation queue</see>
                /// during the next update cycle. </summary>
                Pending = 1,
                /// <summary> Whether or not the requested update is enqueued
                /// in the <see cref="RequestQueue">generation queue</see> and 
                /// will be executed when it reaches the front of the queue</summary>
                InProgress = 2,
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
            /// <summary> 
            /// The manager in charge of performing GPU-forward mesh rendering while
            /// reading back mesh data from the GPU to a Unity mesh object
            /// </summary>
            public readonly AsyncMeshReadback MeshReadback;
            /// <summary> The manager in charge of creating mesh data for the chunk </summary>
            public readonly Map.Creator MeshCreator;
            /// <summary> The manager in charge of planning, pruning, and placing structure data for the chunk </summary>
            public readonly Structure.Creator StructCreator;
            /// <summary> The manager in charge of creating the surface data for the chunk.  </summary>
            public readonly Surface.Creator SurfCreator;

            /// <summary> Creates instances of managers for the chunk given the chunk's information </summary>
            /// <param name="terrainChunk">The Terrain chunk whose information is used</param>
            public GeneratorInfo(TerrainChunk terrainChunk) {
                this.MeshCreator = new Map.Creator();
                this.StructCreator = new Structure.Creator();
                this.SurfCreator = new Surface.Creator();
                this.MeshReadback = new AsyncMeshReadback(terrainChunk.meshObject.transform,
                    terrainChunk.GetRelativeBoundsOS(terrainChunk.origin, terrainChunk.size));
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
                CreateMap = Status.State.Pending,
                SetMap = Status.State.Finished,
                ShrinkMap = Status.State.Finished,
                UpdateMesh = Status.State.Pending,
                CanUpdateMesh = Status.State.Finished,
            };
            if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                GeoShaders = new SubChunkShaderGraph(this);
            Generator = new GeneratorInfo(this);
            SetupChunk();
        }

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
            Generator.MeshReadback?.ReleaseAllGeometry(); //Release base geometry on GPU
            Generator.StructCreator.ReleaseStructure(); //Release structure data
            Generator.SurfCreator.ReleaseMap();
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

        private void SetupChunk() {
            RequestQueue.Enqueue(new GenTask {
                task = () => GetSurface(),
                id = (int)priorities.planning,
                chunk = this,
            });
            RequestQueue.Enqueue(new GenTask {
                task = () => PlanStructures(),
                id = (int)priorities.structure,
                chunk = this,
            });
        }

        /// <summary>
        /// The generation task which retrieves surface information necessary to generate the surface terrain of the world
        /// as well as conditions used in determining the surface biome. This information is stored in the <see cref="Surface.Creator"/>
        /// </summary>
        protected virtual void GetSurface() { }
        /// <summary> The generation task which plans the structures for the chunk. This is the first step in generating the chunk's structure data. 
        /// Chunks are only capable of planning structures if their depth is less than or equal to <see cref="WorldConfig.Quality.Terrain.MaxStructureDepth"/> </summary>
        /// <param name="callback">The callback function that's called once structure planning has been completed (or inserted into a GPU cmd buffer)</param>
        protected virtual void PlanStructures(Action callback = null) { }
        /// <summary> The generation task which creates the map information for the chunk. This is the middle step in generating the chunk's information. 
        /// Chunks will also prune structures if they have cached structure information from <see cref="PlanStructures"/>. </summary>
        /// <param name="callback">The callback function that's called once ReadMapData has been completed (or inserted into a GPU cmd buffer)</param>
        protected virtual void ReadMapData(Action callback = null) { }
        /// <summary> The generation task which creates the mesh information for the chunk. This is the final step in generating the chunk's information. 
        /// Optionally chunks will place structures and generate geoshaded geometry if they have cached structure information from <see cref="ReadMapData"/> 
        /// and their depth is less than or equal to <see cref="WorldConfig.Quality.GeoShaderSettings.MaxGeoShaderDepth"/> respectively.
        /// </summary> <param name="UpdateCallback">The callback function that's returned the mesh constructor once the mesh has been readback</param>
        protected virtual void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) { }
        /// <exclude />
        protected void OnChunkCreated(ReadbackTask<TVert>.SharedMeshInfo meshInfo) {
            ClearFilter();
            meshFilter.sharedMesh = meshInfo.GenerateMesh(UnityEngine.Rendering.IndexFormat.UInt32);
            meshInfo.Release();
        }

        /// <summary>
        /// A real chunk is a chunk of the lowest <see cref="depth"/>, therefore the smallest size (equal to <see cref="WorldConfig.Quality.Terrain.mapChunkSize"/>). 
        /// They are the chunks closest to the viewer and the only chunk that is capable of lossless sampling so it is the only chunk that maintains a copy of its map information 
        /// on the CPU. By extension, it is the only chunk that is capable of terrain interaction, collision, and entity pathfinding. With respect to the interactable game
        /// environment, it is all that exists.
        /// </summary>
        public class RealChunk : TerrainChunk {
            /// <summary> Creates a new real chunk with the the given origin and size. <seealso cref="TerrainChunk"/> </summary>
            public RealChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex) {
            }

            /// <summary> Verifies whether the chunk's is still valid given that a chunk is not valid if it was previously bording a chunk of a 
            /// larger size and is no longer or vice versa. If it is not valid, sets the <see cref="Status.UpdateMesh"/> flag to true. </summary>
            /// <remarks> If a chunk is bordering a chunk of a certain size, some of its out-of-bound mesh information will be invalidated once the neighboring chunk changes size. </remarks>
            public override void VerifyChunk() {
                base.VerifyChunk();
            }

            /// <summary> Based upon the chunk's status, enqueues generation tasks to the <see cref="RequestQueue"/>
            /// to reflect the flags. The status is updated accordingly after the tasks are enqueued/finished. </summary>
            public override void Update() {
                base.Update();
                if (status.CreateMap == Status.State.Pending) {
                    status.UpdateMap = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => ReadMapData(),
                        id = (int)priorities.generation,
                        chunk = this
                    });
                } else if (status.SetMap == Status.State.Pending) {
                    status.UpdateMap = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => SetChunkData(),
                        id = (int)priorities.generation,
                        chunk = this
                    });
                }

                if (status.UpdateMesh == Status.State.Pending) {
                    status.UpdateMesh = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => {
                            status.UpdateMesh = Status.Complete(status.UpdateMesh);
                            status.CanUpdateMesh = Status.Initiate(status.CanUpdateMesh);
                        }, id = (int)priorities.propogation,
                        chunk = this
                    });
                }
                if (status.CanUpdateMesh == Status.State.Pending) {
                    status.CanUpdateMesh = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => CreateMesh(OnChunkCreated),
                        id = (int)priorities.mesh,
                        chunk = this
                    });
                }
            }

            /// <exclude />
            protected override void GetSurface() => Generator.SurfCreator.SampleSurfaceMaps(origin.xz, mapChunkSize, mapSkipInc);
            /// <exclude />
            protected override void PlanStructures(Action callback = null) {
                Generator.StructCreator.PlanStructuresGPU(CCoord, origin, mapChunkSize, IsoLevel);
                callback?.Invoke();
            }
            /// <summary>Beyond what is described in <see cref="TerrainChunk.ReadMapData"/>, real chunks also reads 
            /// chunk files from storage, create/deserialize entities, and copy map data to the CPU.
            /// </summary> <param name="callback"><see cref="TerrainChunk.ReadMapData"/></param>
            protected override void ReadMapData(Action callback = null) {
                //This code will be called on a background thread
                Chunk.ReadbackInfo info = Chunk.ReadChunkInfo(CCoord);

                if (info.map != null) { //if the chunk has saved map data
                    Generator.MeshCreator.SetMapInfo(mapChunkSize, 0, info.map);
                    GPUMapManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.TransferBuffer);
                    if (info.entities == null) Generator.MeshCreator.PopulateBiomes(origin, Generator.SurfCreator.SurfaceMapAddress, mapChunkSize, mapSkipInc);
                } else { //Otherwise create new data
                    Map.Generator.GeoGenOffsets bufferOffsets = Map.Generator.bufferOffsets;
                    Generator.MeshCreator.GenerateBaseChunk(origin, Generator.SurfCreator.SurfaceMapAddress, mapChunkSize, mapSkipInc, IsoLevel);
                    Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize, mapSkipInc, bufferOffsets.rawMapStart, IsoLevel);
                    Generator.MeshCreator.CompressMap(mapChunkSize);
                    GPUMapManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.GenerationBuffer, Map.Generator.bufferOffsets.mapStart);
                }

                //Copy real chunks to CPU
                CPUMapManager.AllocateChunk(this, info.mapMeta, CCoord);
                uint entityAddress = info.entities != null ? 0 :
                    EntityManager.PlanEntities(
                        Map.Generator.bufferOffsets.biomeMapStart,
                        CCoord,
                        mapChunkSize
                    );
                
                CPUMapManager.BeginMapReadback(CCoord, () => {
                    //If the chunk has saved entities
                    if (info.entities != null) EntityManager.DeserializeEntities(info.entities);
                    //Otherwise create new entities
                    else EntityManager.BeginEntityReadback(entityAddress, CCoord);
                });
                status.UpdateMap = Status.Complete(status.UpdateMap);
                callback?.Invoke();
            }
            
            /// <summary> 
            /// To create the mesh, the information is gathered by sampling the GPU-side dictionary provided by the <see cref="GPUMapManager"/>,
            /// this allows the chunk to obtain information about the map data of neighboring chunks which is necessary for generating the mesh boundaries.
            /// <seealso cref="TerrainChunk.CreateMesh(Action{ReadbackTask{TVert}.SharedMeshInfo})"/>
            /// </summary>
            /// <param name="UpdateCallback"><see cref="TerrainChunk.CreateMesh(Action{ReadbackTask{TVert}.SharedMeshInfo})"/></param>
            protected override void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) {
                Generator.MeshCreator.GenerateRealMesh(CCoord, IsoLevel, mapChunkSize, neighborDepth);
                ClearFilter(); octree.ReapChunk(index);

                Map.Generator.GeoGenOffsets bufferOffsets = Map.Generator.bufferOffsets;
                Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)ReadbackMaterial.water);

                if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                    GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)ReadbackMaterial.terrain]);
                status.CanUpdateMesh = Status.Complete(status.CanUpdateMesh);
            }

            private void SetChunkData(Action callback = null) {
                int numPoints = mapChunkSize * mapChunkSize * mapChunkSize;
                int offset = CPUMapManager.HashCoord(CCoord) * numPoints;
                NativeArray<MapData> mapData = CPUMapManager.SectionedMemory;

                Generator.MeshCreator.SetMapInfo(mapChunkSize, offset, ref mapData);
                GPUMapManager.RegisterChunkReal(CCoord, depth, UtilityBuffers.TransferBuffer);
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
        public class VisualChunk : TerrainChunk {
            private int3 sOrigin;
            private int sChunkSize;
            private int mapHandle;

            /// <summary> Creates a new visual chunk with the the given origin and size. <seealso cref="TerrainChunk"/> </summary>
            public VisualChunk(Transform parent, int3 origin, int size, uint octreeIndex) : base(parent, origin, size, octreeIndex) {
                sOrigin = origin - mapSkipInc;
                sChunkSize = mapChunkSize + 3;
                mapHandle = -1;
            }

            /// <summary> Based upon the chunk's status, enqueues generation tasks to the <see cref="RequestQueue"/>
            /// to reflect the flags. The status is updated accordingly after the tasks are enqueued/finished. </summary>
            public override void Update() {
                base.Update();
                if (status.CreateMap == Status.State.Pending) {
                    //Readmap data starts a CPU background thread to read data and re-synchronizes by adding to the queue
                    //Therefore we need to call it directly to maintain it's on the same call-cycle as the rest of generation
                    status.UpdateMap = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => ReadMapData(),
                        id = (int)priorities.generation,
                        chunk = this
                    });
                }
                if (status.UpdateMesh == Status.State.Pending) {
                    status.UpdateMesh = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => {
                            status.UpdateMesh = Status.Complete(status.UpdateMesh);
                            status.CanUpdateMesh = Status.Initiate(status.CanUpdateMesh);
                        }, id = (int)priorities.propogation,
                        chunk = this
                    });
                }
                if (status.CanUpdateMesh == Status.State.Pending) {
                    status.CanUpdateMesh = Status.State.InProgress;
                    RequestQueue.Enqueue(new GenTask {
                        task = () => { CreateMesh(OnChunkCreated); },
                        id = (int)priorities.mesh,
                        chunk = this
                    });
                }
            }

            /// <summary> Normal visual chunks reference directly their GPU-side map information inside the <see cref="GPUMapManager"/>, and are subscribed so 
            /// that it isn't released while the chunk is holding it. On release, it's necessary to unsubscribe to allow the map to be released </summary>
            public override void ReleaseChunk() {
                //if we are still holding onto the map handle, release it
                if (mapHandle != -1) GPUMapManager.UnsubscribeHandle(mapHandle);
                base.ReleaseChunk();
            }
            /// <exclude />
            protected override void GetSurface() => Generator.SurfCreator.SampleSurfaceMaps(sOrigin.xz, sChunkSize, mapSkipInc);
            /// <exclude />
            protected override void PlanStructures(Action callback = null) {
                if (depth > rSettings.MaxStructureDepth) return;
                Generator.StructCreator.PlanStructuresGPU(CCoord, origin, mapChunkSize, IsoLevel, depth);
                callback?.Invoke();
            }

            private void GenerateDefaultMap() {
                Map.Generator.GeoGenOffsets bufferOffsets = Map.Generator.bufferOffsets;
                Generator.MeshCreator.GenerateBaseChunk(sOrigin, Generator.SurfCreator.SurfaceMapAddress, sChunkSize, mapSkipInc, IsoLevel);
                if (depth <= rSettings.MaxStructureDepth)
                    Generator.StructCreator.GenerateStrucutresGPU(mapChunkSize + 1, mapSkipInc, bufferOffsets.rawMapStart, IsoLevel, sChunkSize, 1);
                Generator.MeshCreator.CompressMap(sChunkSize);
            }
            /// <summary>
            /// A chunk is a normal visual chunk if it can be registered in the <see cref="GPUMapManager"/>. Otherwise, it is a fake visual chunk.
            /// For a fake visual chunk, ReadMapData does nothing because it will be lost immediately after generation as it is not cached anywhere.
            /// For a normal visual chunk, ReadMapData generates the default map information and registeres it into the <see cref="GPUMapManager"/>, 
            /// then it reads any dirty maps from storage and replaces the default map information within the <see cref="GPUMapManager"/>'s dictionary
            /// with the dirty map information.
            /// </summary> <param name="callback"><see cref="TerrainChunk.ReadMapData(Action)"/></param>
            protected override void ReadMapData(Action callback = null) {

                if (!GPUMapManager.IsChunkRegisterable(CCoord, depth)) {
                    callback?.Invoke();
                    return;
                }

                GenerateDefaultMap();
                Map.Generator.GeoGenOffsets bufferOffsets = Map.Generator.bufferOffsets;
                if (mapHandle != -1) GPUMapManager.UnsubscribeHandle(mapHandle);
                mapHandle = GPUMapManager.RegisterChunkVisual(CCoord, depth, UtilityBuffers.GenerationBuffer, bufferOffsets.mapStart);
                if (mapHandle == -1) return;

                MapData[] info = Chunk.ReadVisualChunkMap(CCoord, depth);
                Generator.MeshCreator.SetMapInfo(mapChunkSize, 0, info);
                GPUMapManager.TranscribeMultiMap(UtilityBuffers.TransferBuffer, CCoord, depth);
                //Subscribe once more so the chunk can't be released while we hold its handle
                GPUMapManager.SubscribeHandle((uint)mapHandle);
                status.UpdateMap = Status.Complete(status.UpdateMap);
                callback?.Invoke();
            }

            /// <summary>
            /// A visual chunk is fake if it does not have any cached information in the <see cref="GPUMapManager"/>.
            /// If it is a fake visual chunk, it will generate the default map information and create the mesh.
            /// If it is a normal visual chunk, it will create the mesh using the cached map information in the <see cref="GPUMapManager"/>.
            /// </summary> <param name="UpdateCallback"><see cref="TerrainChunk.CreateMesh(Action{ReadbackTask{TVert}.SharedMeshInfo})"/></param>
            protected override void CreateMesh(Action<ReadbackTask<TVert>.SharedMeshInfo> UpdateCallback = null) {
                if (mapHandle == -1) {
                    GenerateDefaultMap(); //We need the default mesh to be immediately in the buffer
                    Generator.MeshCreator.GenerateFakeMesh(IsoLevel, mapChunkSize, neighborDepth);
                } else {
                    int directAddress = (int)GPUMapManager.GetHandle(mapHandle).x;
                    Generator.MeshCreator.GenerateVisualMesh(CCoord, directAddress, IsoLevel, mapChunkSize, depth, neighborDepth);
                    GPUMapManager.UnsubscribeHandle(mapHandle);
                    mapHandle = -1;
                }

                octree.ReapChunk(index);
                ClearFilter();

                Map.Generator.GeoGenOffsets bufferOffsets = Map.Generator.bufferOffsets;
                Generator.MeshReadback.OffloadVerticesToGPU(bufferOffsets.vertexCounter);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.baseTriCounter, bufferOffsets.baseTriStart, (int)ReadbackMaterial.terrain);
                Generator.MeshReadback.OffloadTrisToGPU(bufferOffsets.waterTriCounter, bufferOffsets.waterTriStart, (int)ReadbackMaterial.water);

                if (depth <= Config.CURRENT.Quality.GeoShaders.value.MaxGeoShaderDepth)
                    GeoShaders.ComputeGeoShaderGeometry(Generator.MeshReadback.vertexHandle, Generator.MeshReadback.triHandles[(int)ReadbackMaterial.terrain]);
                status.CanUpdateMesh = Status.Complete(status.CanUpdateMesh);
            }
        }
    }
}
