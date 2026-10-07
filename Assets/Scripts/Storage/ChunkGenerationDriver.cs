using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Arterra.Configuration;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Arterra.Engine.Terrain.Readback;
using Arterra.Core.Network;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using ChunkMapInfo = Arterra.Core.Storage.CPUMapManager.ChunkMapInfo;

namespace Arterra.Core.Storage {
    /// <summary>
    /// A player's bounded region of chunk subscriptions. CPUMapManager owns the shared records;
    /// overlapping drivers share the same data, generation operation and completion task.
    /// Allocation, subscription changes and generation run on Unity's main thread.
    /// </summary>
    public sealed class ChunkGenerationDriver {
        public ChunkSource Source { get; }
        public static ChunkGenerationDriver Local { get; private set; }
        private readonly Subscription[] region;
        private readonly int numChunksAxis;

        /// <summary>Create the local driver with its source before allocating chunks.</summary>
        public static void Initialize(ChunkSource source) {
            if (!CPUMapManager.ChunkData.IsEmpty)
                throw new InvalidOperationException("Release the previous world's chunks before initializing the local driver.");
            Local = new ChunkGenerationDriver(CPUMapManager.numChunksAxis, source);
        }

        public ChunkGenerationDriver(int numChunksAxis, ChunkSource source) {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            if (numChunksAxis <= 0) throw new ArgumentOutOfRangeException(nameof(numChunksAxis));
            this.numChunksAxis = numChunksAxis;
            region = new Subscription[checked(numChunksAxis * numChunksAxis * numChunksAxis)];
        }

        private int HashCoord(int3 coordinate) {
            int3 hash = ((coordinate % numChunksAxis) + numChunksAxis) % numChunksAxis;
            return (hash.x * numChunksAxis + hash.y) * numChunksAxis + hash.z;
        }

        /// <summary>
        /// Subscribe and start the shared load if needed. Await Ready before using the data.
        /// Coordinates within this driver's desired region must have distinct modulo hashes.
        /// </summary>
        public Subscription Allocate(IChunkConsumer owner, int3 coordinate) {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            int index = HashCoord(coordinate);
            Subscription previous = region[index];
            if (previous != null && previous.Active && math.all(previous.Chunk.CCoord == coordinate) &&
                ReferenceEquals(previous.Owner, owner)) return previous;

            ChunkMapInfo chunk = CPUMapManager.GetOrCreateChunkInfo(coordinate);
            var subscription = new Subscription(owner, chunk);
            // Subscribe first so replacing the owner at this coordinate never unloads the data.
            chunk.Generation.Subscribers.Add(subscription);
            region[index] = subscription;
            previous?.Release();
            chunk.Generation.Start(chunk, Source);
            return subscription;
        }

        public void Release(int3 coordinate) {
            int index = HashCoord(coordinate);
            Subscription subscription = region[index];
            if (subscription == null || math.any(subscription.Chunk.CCoord != coordinate)) return;
            region[index] = null;
            subscription.Release();
        }

        /// <summary>Disconnect this player without canceling another player's generation.</summary>
        public void Release() {
            for (int i = 0; i < region.Length; i++) {
                Subscription subscription = region[i];
                region[i] = null;
                subscription?.Release();
            }
        }

        public sealed class Subscription {
            public readonly IChunkConsumer Owner;
            public readonly ChunkMapInfo Chunk;
            public bool Active => Chunk.Generation.Subscribers.Contains(this);
            public Task Ready => Chunk.Generation.Ready;

            internal Subscription(IChunkConsumer owner, ChunkMapInfo chunk) {
                Owner = owner;
                Chunk = chunk;
            }

            internal void Release() {
                if (!Chunk.Generation.Subscribers.Remove(this)) return;
                _ = ReleaseUnusedAsync();
            }

            private async Task ReleaseUnusedAsync() {
                try { await CPUMapManager.ReleaseChunkIfUnusedAsync(Chunk); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
        }

        /// <summary>
        /// Mutable state shared by every copy of the ChunkMapInfo struct. Updating progress,
        /// metadata or subscriptions never requires replacing the dictionary's struct value.
        /// </summary>
        public sealed class GenerationState {
            public readonly System.Collections.Concurrent.ConcurrentDictionary<uint, object> MapMetaData;

            internal GenerationState(System.Collections.Concurrent.ConcurrentDictionary<uint, object> metadata = null) {
                MapMetaData = metadata ?? new();
            }
            internal readonly HashSet<Subscription> Subscribers = new();
            internal int mapProgress;
            internal int entityProgress;
            internal ChunkMapInfo.Flags OffloadFlags;
            public MapGenerationPipeline Pipeline { get; private set; }
            public EntityGenerator Entities { get; internal set; }
            private TaskCompletionSource<bool> completion;
            public Task Ready => completion?.Task;
            internal Task Loading { get; private set; }
            // The first subscriber supplies its driver's source.
            // That source serves the shared load and release for all subscribers
            internal ChunkSource Source { get; private set; }
            internal void Cancel() => completion?.TrySetCanceled();
            private void CheckCanceled() {
                if (Ready.IsCanceled) throw new OperationCanceledException();
            }

            internal void Start(ChunkMapInfo chunk, ChunkSource source) {
                if (Ready != null) return;
                Source = source ?? throw new ArgumentNullException(nameof(source));
                completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                Loading = LoadAsync(chunk, completion);
            }

            private async Task LoadAsync(ChunkMapInfo chunk, TaskCompletionSource<bool> completion) {
                chunk.MapProgress = ChunkMapInfo.State.InProgress;
                chunk.EntityProgress = ChunkMapInfo.State.InProgress;
                try {
                    await LoadContentAsync(chunk);
                    CheckCanceled();
                    chunk.MapProgress = ChunkMapInfo.State.Finished;
                    chunk.EntityProgress = ChunkMapInfo.State.Finished;
                    completion.TrySetResult(true);
                } catch (OperationCanceledException) {
                    if (!completion.Task.IsCanceled) {
                        chunk.MapProgress = ChunkMapInfo.State.Failed;
                        chunk.EntityProgress = ChunkMapInfo.State.Failed;
                    }
                    completion.TrySetCanceled();
                } catch (Exception exception) {
                    if (!completion.Task.IsCanceled) {
                        chunk.MapProgress = ChunkMapInfo.State.Failed;
                        chunk.EntityProgress = ChunkMapInfo.State.Failed;
                    }
                    completion.TrySetException(exception);
                }
            }

            private async Task LoadContentAsync(ChunkMapInfo chunk) {
                CheckCanceled();
                Chunk.ReadbackInfo content = await Source.ReadAsync(chunk.CCoord);
                CheckCanceled();
                bool generateMap = content.map.map == null;
                bool generateEntities = content.entity.entities == null;
                bool generateMeta = content.map.mapMeta == null;

                int size = Config.CURRENT.Quality.Terrain.value.mapChunkSize;
                if (generateMap || generateEntities || generateMeta) {
                    Pipeline = MapGenerationPipeline.Build(chunk.CCoord * size);
                    await Pipeline.Start(generateMap);
                    CheckCanceled();
                }
                if (generateMap) {
                    Task write = Pipeline.ReadbackAsync(chunk.ChunkData, chunk.Offset);
                    chunk.PendingWrites.Add(write);
                    try { await write; }
                    finally { chunk.PendingWrites.Remove(write); }
                    CheckCanceled();
                } else NativeArray<MapData>.Copy(content.map.map, 0, chunk.ChunkData, chunk.Offset, chunk.Length);

                if (content.map.mapMeta != null)
                    foreach (var entry in content.map.mapMeta) MapMetaData[entry.Key] = entry.Value;
                chunk.MapProgress = ChunkMapInfo.State.Finished;

                byte context = AsyncGenInfoReadback.CREATE_PLANNED_ENTITIES;
                if (generateMeta) context |= AsyncGenInfoReadback.CREATE_META;
                if (generateEntities) {
                    context |= AsyncGenInfoReadback.CREATE_ENTITIES;
                    Pipeline.MapCreator.PopulateBiomes(Pipeline.Origin,
                        Pipeline.SurfCreator.SurfaceMapAddress, size, 1);
                }
                // Supplied chunks still resolve candidates waiting in lower neighbors.
                var readback = Pipeline?.MetaReadback ?? new AsyncGenInfoReadback(null);
                var planner = Pipeline?.Entities ?? new EntityGenerator();
                try {
                    planner.PlanEntities(readback, Engine.Terrain.Map.Creator.bufferOffsets.biomeMapStart, chunk.CCoord, generateEntities);
                    Task planning = readback.GenInfoReadback(chunk.CCoord, context);
                    await Task.WhenAny(planning, Ready);
                    CheckCanceled();
                    await planning;
                } finally {
                    if (Pipeline == null) readback.Release();
                }
                CheckCanceled();
                Task entities = EntityManager.InitializeChunkEntitiesAsync(content.entity.entities);
                await Task.WhenAny(entities, Ready);
                CheckCanceled();
                await entities;
            }

            internal void ReflectChunk(Dictionary<int, MapData> updatedValues) {
                var snapshot = new List<Subscription>(Subscribers);
                foreach (Subscription subscriber in snapshot)
                    if (subscriber.Active) subscriber.Owner.ReflectChunk(updatedValues);
            }
        }
    }

    /// <summary>A local renderer or a server-side peer subscriber; neither owns shared map resources.</summary>
    public interface IChunkConsumer {
        void ReflectChunk(Dictionary<int, MapData> updatedValues);
    }

    /// <summary>Supplies authoritative content/permissions. The driver owns generation and shared completion.</summary>
    public abstract class ChunkSource {
        public abstract Task<Chunk.ReadbackInfo> ReadAsync(int3 coordinate);
        public abstract Task<MapData[]> ReadVisualAsync(int3 coordinate, int depth);
        // Receives already-offloaded data. The caller owns disposal after every user finishes.
        public abstract Task ReleaseAsync(ChunkMapInfo chunk);
    }

    public sealed class LocalChunkSource : ChunkSource {
        private static void RequireLocalAuthority() {
            if (!NetworkManager.IsActingServer)
                throw new NotSupportedException("A remote chunk source is required; the chunk-request protocol is not installed.");
        }

        public override Task<Chunk.ReadbackInfo> ReadAsync(int3 CCoord) {
            RequireLocalAuthority();
            return Task.Run(() => Chunk.ReadChunkInfo(CCoord));
        }

        public override Task<MapData[]> ReadVisualAsync(int3 coordinate, int depth) {
            RequireLocalAuthority();
            return Task.Run(() => Chunk.ReadVisualChunkMap(coordinate, depth));
        }

        public override async Task ReleaseAsync(ChunkMapInfo chunk) {
            RequireLocalAuthority();
            bool saveMap = chunk.HasMap && chunk.Generation.OffloadFlags == ChunkMapInfo.Flags.Dirty;
            Task entities = EntityManager.ReleaseChunkEntitiesAsync(chunk.CCoord, chunk.HasEntities);
            
            Task map;
            if (saveMap) map = Chunk.SaveChunkToBinAsync(chunk, chunk.CCoord);
            else map = Task.CompletedTask;
            await Task.WhenAll(map, entities);
        }
    }
}
