using System;
using System.Collections.Generic;
using System.Threading;
using Arterra.Configuration;
using Arterra.Engine.Terrain;
using Unity.Mathematics;

namespace Arterra.Core.Storage {
    /// <summary>Bounded visual-only subscriptions, hashed in each depth's own chunk coordinates.</summary>
    public sealed class VisualChunkGenerationDriver : IDisposable {
        public static VisualChunkGenerationDriver Local { get; private set; }
        private static readonly HashSet<VisualChunkGenerationDriver> drivers = new();

        private static Dictionary<int3, HashSet<CPUMapManager.ChunkMapInfo>>[] chunksByDepth;
        private static int indexedRootOffset, indexedChunkSize;

        internal static void InitializeChunkIndex(int maxDepth, int balance, int chunkSize) {
            indexedRootOffset = balance == 1 && maxDepth > 0 ? 1 << (maxDepth - 1) : 0;
            indexedChunkSize = chunkSize;
            chunksByDepth = new Dictionary<int3, HashSet<CPUMapManager.ChunkMapInfo>>[maxDepth + 1];
            for (int depth = 1; depth <= maxDepth; depth++) chunksByDepth[depth] = new();
        }

        public ChunkSource Source { get; }
        private readonly Subscription[][] regions;
        private readonly int[] diameters;
        private readonly int maxDepth, rootOffset, chunkSize;

        private ulong nextId;
        private bool disposed;

        public static void Initialize(ChunkSource source) {
            Local?.Dispose();
            Local = new VisualChunkGenerationDriver(source);
        }

        public VisualChunkGenerationDriver(ChunkSource source) {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            var settings = Config.CURRENT.Quality.Terrain.value;
            maxDepth = settings.MaxDepth;
            chunkSize = settings.mapChunkSize;
            rootOffset = settings.Balance == 1 && maxDepth > 0 ? 1 << (maxDepth - 1) : 0;
            regions = new Subscription[maxDepth + 1][];
            diameters = new int[maxDepth + 1];
            for (int depth = 1; depth <= maxDepth; depth++) {
                int diameter = OctreeTerrain.BalancedOctree.GetAxisChunksDepth(depth, settings.Balance, (uint)settings.MinChunkRadius);
                diameters[depth] = diameter;
                regions[depth] = new Subscription[checked(diameter * diameter * diameter)];
            }
            drivers.Add(this);
        }

        private int Index(int3 CCoord, int depth) {
            if (depth < 1 || depth > maxDepth) throw new ArgumentOutOfRangeException(nameof(depth));
            int scale = 1 << depth;
            int offset = rootOffset % scale;
            if (math.any((CCoord - offset) % scale != 0))
                throw new ArgumentException("Visual chunk origin is not aligned to its octree depth.", nameof(CCoord));
            int3 lattice = (CCoord - offset) / scale;
            int diameter = diameters[depth];
            int3 hash = ((lattice % diameter) + diameter) % diameter;
            return (hash.x * diameter + hash.y) * diameter + hash.z;
        }

        public Subscription Allocate(IChunkConsumer owner, int3 CCoord, int depth, ulong id = 0) {
            if (disposed) throw new ObjectDisposedException(nameof(VisualChunkGenerationDriver));
            int index = Index(CCoord, depth);
            var previous = regions[depth][index];
            if (previous != null && previous.CCoord.Equals(CCoord)
                && ReferenceEquals(previous.Owner, owner) && (id == 0 || id == previous.Id)) return previous;
            if (id == 0) id = ++nextId;

            var subscription = new Subscription(this, owner, CCoord, depth, id);
            regions[depth][index] = subscription;
            if (chunksByDepth[depth].TryGetValue(CCoord, out var chunks))
                foreach (var chunk in chunks) {
                    if (chunk.VisualSubscriptions.Add(subscription))
                        CPUMapManager.MapChanges.AddSource(chunk.CCoord, Source);
                }
            previous?.Release();
            return subscription;
        }

        public void Release(int3 CCoord, int depth, ulong id) {
            int index = Index(CCoord, depth);
            var subscription = regions[depth][index];
            if (subscription == null || subscription.Id != id || !subscription.CCoord.Equals(CCoord)) return;
            subscription.Release();
        }

        // Index only loaded CPU chunks; each depth stores their overlapping visual origins.
        internal static void SubscribeChunk(CPUMapManager.ChunkMapInfo chunk) {
            VisitOrigins(chunk.CCoord, chunksByDepth.Length - 1, indexedRootOffset, indexedChunkSize, (originCCoord, depth) => {
                var layer = chunksByDepth[depth];
                if (!layer.TryGetValue(originCCoord, out var chunks)) layer.Add(originCCoord, chunks = new());
                chunks.Add(chunk);
                foreach (var driver in drivers) {
                    var subscription = driver.regions[depth][driver.Index(originCCoord, depth)];
                    if (subscription != null && subscription.CCoord.Equals(originCCoord)
                        && chunk.VisualSubscriptions.Add(subscription))
                        CPUMapManager.MapChanges.AddSource(chunk.CCoord, subscription.Source);
                }
            });
        }

        internal static void UnsubscribeChunk(CPUMapManager.ChunkMapInfo chunk) {
            VisitOrigins(chunk.CCoord, chunksByDepth.Length - 1, indexedRootOffset, indexedChunkSize, (originCCoord, depth) => {
                var layer = chunksByDepth[depth];
                if (!layer.TryGetValue(originCCoord, out var chunks)) return;
                chunks.Remove(chunk);
                if (chunks.Count == 0) layer.Remove(originCCoord);
            });
            chunk.VisualSubscriptions.Clear();
        }

        public void ReflectChunk(int3 CCoord, IReadOnlyDictionary<uint, MapData> changes) {
            Visit(CCoord, subscription => {
                int scale = 1 << subscription.Depth;
                int axis = chunkSize;
                bool changed = false;
                foreach (var change in changes) {
                    int index = (int)change.Key;
                    int3 MCoord = new(index / (chunkSize * chunkSize), index / chunkSize % chunkSize, index % chunkSize);
                    int3 relativeGCoord = (CCoord - subscription.CCoord) * chunkSize + MCoord;
                    if (math.any(relativeGCoord % scale != 0)) continue;
                    int3 visualMCoord = relativeGCoord / scale;
                    if (math.any(visualMCoord < -1) || math.any(visualMCoord > axis + 1)) continue;
                    changed = true;
                    if (math.any(visualMCoord < 0) || math.any(visualMCoord >= axis)) continue;
                    subscription.Changes[(visualMCoord.x * axis + visualMCoord.y) * axis + visualMCoord.z] = change.Value;
                }
                if (changed) subscription.Owner?.ReflectChunk();
            });
        }

        private void Visit(int3 CCoord, Action<Subscription> visit) {
            VisitOrigins(CCoord, maxDepth, rootOffset, chunkSize, (originCCoord, depth) => {
                var subscription = regions[depth][Index(originCCoord, depth)];
                if (subscription != null && subscription.CCoord.Equals(originCCoord)) visit(subscription);
            });
        }

        private static void VisitOrigins(int3 CCoord, int maxDepth, int rootOffset, int chunkSize, Action<int3, int> visit) {
            for (int depth = 1; depth <= maxDepth; depth++) {
                int scale = 1 << depth;
                int offset = rootOffset % scale;
                int3 relativeCCoord = CCoord - offset;
                int3 originCCoord = relativeCCoord - ((relativeCCoord % scale) + scale) % scale + offset;
                for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                for (int z = -1; z <= 1; z++) {
                    int3 candidateCCoord = originCCoord + new int3(x, y, z) * scale;
                    double3 minGCoord = (double3)candidateCCoord * chunkSize - scale;
                    double3 maxGCoord = (double3)candidateCCoord * chunkSize + (long)(chunkSize + 1) * scale;
                    double3 changedMinGCoord = (double3)CCoord * chunkSize;
                    if (math.any(changedMinGCoord > maxGCoord) || math.any(changedMinGCoord + chunkSize - 1 < minGCoord)) continue;
                    visit(candidateCCoord, depth);
                }}}
            }
        }

        public struct Request {
            public int3 CCoord;
            public int Depth;
            public ulong Id;
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            drivers.Remove(this);
            for (int depth = 1; depth <= maxDepth; depth++)
                foreach (var subscription in regions[depth]) subscription?.Release();
            if (ReferenceEquals(Local, this)) Local = null;
        }

        public sealed class Subscription {
            public readonly IChunkConsumer Owner;
            public ChunkSource Source => driver.Source;
            public readonly int3 CCoord;
            public readonly int Depth;
            public readonly ulong Id;
            public bool Active { get; private set; } = true;
            public readonly Dictionary<int, MapData> Changes = new();
            public Request Request => new() { CCoord = CCoord, Depth = Depth, Id = Id };
            private readonly VisualChunkGenerationDriver driver;
            private readonly CancellationTokenSource cancellation = new();
            public CancellationToken Token { get; }

            internal Subscription(VisualChunkGenerationDriver driver, IChunkConsumer owner, int3 CCoord, int depth, ulong id) {
                this.driver = driver;
                Owner = owner;
                this.CCoord = CCoord;
                Depth = depth;
                Id = id;
                Token = cancellation.Token;
            }

            public void Release() {
                if (!Active) return;
                Active = false;
                if (chunksByDepth[Depth].TryGetValue(CCoord, out var chunks))
                    foreach (var chunk in chunks) {
                        if (chunk.VisualSubscriptions.Remove(this))
                            CPUMapManager.MapChanges.RemoveSource(chunk.CCoord, Source);
                    }
                int index = driver.Index(CCoord, Depth);
                if (ReferenceEquals(driver.regions[Depth][index], this)) driver.regions[Depth][index] = null;
                Changes.Clear();
                cancellation.Cancel();
                cancellation.Dispose();
                driver.Source.ReleaseVisual(CCoord, Depth, Id);
            }
        }
    }
}
