using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Core;
using Arterra.Core.Storage;
using Arterra.Engine.Terrain.Readback;
using Arterra.Utils;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using static Arterra.Core.Storage.SharedResourceManager;

namespace Arterra.Engine.Terrain
{
    /// <summary>
    /// Generates and retains a default chunk map without a TerrainChunk or GameObject.
    /// Build, RunAll, readback requests, and Release must run on the Unity main thread.
    /// Requires initialized generation resources and the runtime's command-buffer pump.
    /// Save data, entities, and registration in world map caches remain caller responsibilities.
    /// </summary>
    public class MapGenerationPipeline {
        private static readonly LinkedList<MapGenerationPipeline> instances = new();
        public static int Count => instances.Count;

        public readonly Map.Creator MapCreator;
        public readonly Structure.Creator StructCreator;
        public readonly Surface.Creator SurfCreator;
        public readonly AsyncGenInfoReadback MetaReadback;
        public readonly EntityGenerator Entities;
        public readonly int3 Origin;
        public readonly int Depth;
        public readonly bool IncludeBorder;

        private readonly Configuration.Quality.Terrain settings;
        private readonly TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private HashSet<TaskCompletionSource<MapData[]>> readbacks;
        private LinkedListNode<MapGenerationPipeline> registration;
        private uint mapAddress;
        private bool generateMap;
        private int nativeReadbacks;

        public enum Stage {
            Idle, Pending, Surface, Structures, Map, Ready, Released
        }
        public Stage CurrentStage { get; private set; }
        public bool Active => CurrentStage != Stage.Released;
        public bool HasRetainedMap => mapAddress != 0;
        public bool IsReady => CurrentStage == Stage.Ready;
        public int3 CCoord => Origin / settings.mapChunkSize;
        public int SampleSize => settings.mapChunkSize + (IncludeBorder ? BorderSamples : 0);
        public int PointCount => checked(SampleSize * SampleSize * SampleSize);
        public int MapSkipInc => 1 << Depth;
        public int3 SampleOrigin => IncludeBorder ? Origin - MapSkipInc : Origin;

        /// <summary>
        /// Completes when generation and retention commands are recorded, not when the GPU
        /// finishes them. Use ReadbackAsync to await CPU data; CopyTo orders a GPU copy.
        /// Faults on generation failure and is canceled if released before generation finishes.
        /// </summary>
        public Task Completion => completion.Task;

        private MapGenerationPipeline(int3 origin, int depth, bool includeBorder) {
            settings = Config.CURRENT.Quality.Terrain.value;
            Origin = origin;
            Depth = depth;
            IncludeBorder = includeBorder;

            MapCreator = new Map.Creator();
            StructCreator = new Structure.Creator();
            SurfCreator = new Surface.Creator();
            MetaReadback = new AsyncGenInfoReadback(StructCreator);
            Entities = new EntityGenerator();
        }

        // Matches the visual chunk layout: one sample before the origin and two after.
        private const int BorderSamples = 3;
        private const int MapStride = 1;
        private const int MapBytes = sizeof(uint);
        private const int MaxDepth = 30;

        /// <summary>
        /// Creates and registers an idle pipeline. Call Start to request generation.
        /// Origin is in grid space and must be aligned
        /// to the configured real-chunk size. Depth controls sample spacing, not sample count.
        /// Use includeBorder for the padded layout currently consumed by visual chunks.
        /// The caller owns this instance until Release, including after Completion succeeds.
        /// </summary>
        public static MapGenerationPipeline Build(int3 origin, int depth = 0, bool includeBorder = false) {
            if (depth < 0 || depth > MaxDepth)
                throw new ArgumentOutOfRangeException(nameof(depth));
            int chunkSize = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
            if (chunkSize <= 0 || math.any(origin % chunkSize != 0))
                throw new ArgumentException("Origin must be aligned to the real-chunk size.", nameof(origin));

            var pipeline = new MapGenerationPipeline(origin, depth, includeBorder);
            pipeline.registration = instances.AddLast(pipeline);
            return pipeline;
        }

        /// <summary>Requests generation once. Repeated calls do not restart the pipeline.</summary>
        public Task Start(bool generateMap = true) {
            if (CurrentStage == Stage.Idle) {
                this.generateMap = generateMap;
                CurrentStage = Stage.Pending;
            }
            return completion.Task;
        }

        /// <summary>Call from the runtime generation loop before processing its event queue.</summary>
        public static void RunAll() {
            var current = instances.First;
            while (current != null) {
                var next = current.Next;
                current.Value.Update();
                current = next;
            }
        }

        /// <summary>Queues all generation stages once; the runtime gates their dependencies.</summary>
        public void Update() {
            if (CurrentStage != Stage.Pending) return;
            Register(Stage.Surface, priorities.planning);
            Register(Stage.Structures, priorities.structure);
            Register(Stage.Map, priorities.generation);
            CurrentStage = Stage.Surface;
        }

        private void Register(Stage stage, priorities priority) {
            ArterraRuntime.Register(new ArterraRuntime.EventTask {
                task = RunStage,
                id = (int)priority,
                cancelToken = () => !Active,
                processToken = () => CurrentStage == stage,
            });
        }

        private void RunStage() {
            if (!Active) return;
            try {
                switch (CurrentStage) {
                    case Stage.Surface:
                        SurfCreator.SampleSurfaceMaps(SampleOrigin.xz, SampleSize, MapSkipInc);
                        CurrentStage = Stage.Structures;
                        break;
                    case Stage.Structures:
                        if (Depth <= settings.MaxStructureDepth)
                            StructCreator.PlanStructuresGPU(MetaReadback, CCoord, Origin,
                                settings.mapChunkSize, settings.IsoLevel, Depth);
                        CurrentStage = Stage.Map;
                        break;
                    case Stage.Map:
                        if (generateMap) GenerateMap();
                        CurrentStage = Stage.Ready;
                        completion.TrySetResult(true);
                        break;
                }
            } catch (Exception exception) {
                completion.TrySetException(exception);
                Release();
            }
        }

        private void GenerateMap() {
            var context = GraphicsGeneration;
            var offsets = Map.Creator.bufferOffsets;
            MapCreator.GenerateBaseChunk(SampleOrigin, SurfCreator.SurfaceMapAddress,
                SampleSize, MapSkipInc, settings.IsoLevel);
            if (Depth <= settings.MaxStructureDepth)
                StructCreator.GenerateStrucutresGPU(settings.mapChunkSize + (IncludeBorder ? 1 : 0),
                    MapSkipInc, offsets.rawMapStart, settings.IsoLevel, SampleOrigin,
                    SampleSize, IncludeBorder ? 1 : 0);
            MapCreator.CompressMap(SampleSize);

            // Scratch belongs to all generation requests; retain this result in the same task.
            mapAddress = context.Memory.AllocateMemoryDirect(PointCount, MapStride);
            if (mapAddress == 0)
                throw new InvalidOperationException("Could not allocate the generated map.");
            context.Work.CopyToStorage(mapAddress, offsets.mapStart, PointCount);
        }

        /// <summary>
        /// Records a copy on the generation context. The destination must hold packed uint
        /// MapData entries and remain alive until the GPU has consumed the command.
        /// Cross-context synchronization remains the consumer's responsibility.
        /// </summary>
        public void CopyTo(ComputeBuffer destination, int destinationOffset = 0) {
            if (destination == null) throw new System.ArgumentNullException(nameof(destination));
            if (destination.stride != MapBytes || destinationOffset < 0 ||
                destinationOffset > destination.count - PointCount)
                throw new ArgumentException("Destination must have room for the packed map.", nameof(destination));
            GetMap(out ComputeBuffer source, out int sourceOffset);
            var context = GraphicsGeneration;
            if (!context.Work.CopyBuffer(source, destination, sourceOffset, destinationOffset,
                PointCount, graphicsContext: context))
                throw new InvalidOperationException("Could not copy the generated map.");
        }

        /// <summary>
        /// Waits for generation and copies the map to caller-owned native memory.
        /// Keep the destination allocated and exclusively available until the task completes.
        /// An issued readback cannot be canceled: Release reports cancellation only after it
        /// finishes writing. Await this task before disposing the destination or shutting down graphics.
        /// Offsets are in MapData entries, not bytes. Border samples are included if requested.
        /// </summary>
        public async Task ReadbackAsync(NativeArray<MapData> destination, int destinationOffset = 0) {
            if (!destination.IsCreated || destinationOffset < 0 ||
                destinationOffset > destination.Length - PointCount)
                throw new ArgumentException("Destination must have room for the generated map.", nameof(destination));

            await Completion;
            if (!Active) throw new OperationCanceledException();
            GetMap(out ComputeBuffer source, out int sourceOffset);
            var output = destination.GetSubArray(destinationOffset, PointCount);
            var pending = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            // Do not add this task to readbacks: Release must not signal that the caller's
            // native memory is available while an issued GPU write is still outstanding.
            nativeReadbacks++;
            try {
                GraphicsGeneration.RequestAsyncReadbackIntoNativeArray(ref output, source,
                    checked(PointCount * MapBytes), checked(sourceOffset * MapBytes), request => {
                        if (!Active) pending.TrySetCanceled();
                        else if (request.hasError)
                            pending.TrySetException(new InvalidOperationException("GPU map readback failed."));
                        else pending.TrySetResult(true);
                    });
                await pending.Task;
            } finally { nativeReadbacks--; }
        }

        /// <summary>Reads the generated map into a new managed array.</summary>
        public async Task<MapData[]> ReadbackAsync() {
            await Completion;
            if (!Active) throw new OperationCanceledException();
            GetMap(out ComputeBuffer source, out int sourceOffset);
            var pending = new TaskCompletionSource<MapData[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            (readbacks ??= new()).Add(pending);
            try {
                // The context snapshots the map before its allocation can be reused.
                GraphicsGeneration.RequestAsyncReadback(source, checked(PointCount * MapBytes),
                    checked(sourceOffset * MapBytes), request => {
                        if (!Active) return;
                        try {
                            if (request.hasError)
                                throw new InvalidOperationException("GPU map readback failed.");
                            pending.TrySetResult(request.GetData<MapData>().ToArray());
                        } catch (Exception exception) {
                            pending.TrySetException(exception);
                        }
                    });
                return await pending.Task;
            } finally {
                readbacks.Remove(pending);
            }
        }

        /// <summary>Current retained buffer and offset in packed map entries. Consume on the generation context.</summary>
        public void GetMap(out ComputeBuffer source, out int offset) {
            if (!Active) throw new ObjectDisposedException(nameof(MapGenerationPipeline));
            if (!IsReady) throw new InvalidOperationException("Await Completion before accessing the GPU map.");
            if (!GraphicsGeneration.Memory.GetDirectAllocation(mapAddress, MapStride, out source,
                out _, out _, out offset, out int count) || count < PointCount)
                throw new InvalidOperationException("The generated map allocation is unavailable.");
        }

        /// <summary>Release only the retained GPU map after its readbacks and publication copy are recorded/completed.</summary>
        public void ReleaseMap() {
            if (nativeReadbacks != 0 || (readbacks != null && readbacks.Count != 0))
                throw new InvalidOperationException("Finish map readbacks before releasing retained storage.");
            if (mapAddress != 0) GraphicsGeneration.Memory.ReleaseMemory(mapAddress);
            mapAddress = 0;
        }

        public void Cancel() => Release();

        /// <summary>
        /// Cancels queued work and managed readbacks, and frees owned allocations.
        /// Native readback tasks remain pending until their GPU writes finish. Recorded GPU commands
        /// are not retracted; releases are ordered through the generation memory manager.
        /// Call before generation resources are shut down. Safe to call more than once.
        /// </summary>
        public void Release() {
            if (!Active) return;
            CurrentStage = Stage.Released;
            instances.Remove(registration);
            registration = null;
            completion.TrySetCanceled();
            if (readbacks != null) {
                foreach (var pending in readbacks) pending.TrySetCanceled();
                readbacks.Clear();
            }

            Entities.Release(CCoord);
            MetaReadback.Release();
            StructCreator.ReleaseStructure();
            SurfCreator.ReleaseMap();
            if (mapAddress != 0) GraphicsGeneration.Memory.ReleaseMemory(mapAddress);
            mapAddress = 0;
        }

        /// <summary>Release outstanding requests before shutting down the generation context.</summary>
        public static void ReleaseAll() {
            while (instances.First != null) instances.First.Value.Release();
        }
    }
}
