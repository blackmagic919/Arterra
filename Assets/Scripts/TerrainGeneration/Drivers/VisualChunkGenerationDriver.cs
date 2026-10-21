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

        private int Index(int3 coordinate, int depth) {
            if (depth < 1 || depth > maxDepth) throw new ArgumentOutOfRangeException(nameof(depth));
            int scale = 1 << depth;
            int offset = rootOffset % scale;
            if (math.any((coordinate - offset) % scale != 0))
                throw new ArgumentException("Visual chunk origin is not aligned to its octree depth.", nameof(coordinate));
            int3 lattice = (coordinate - offset) / scale;
            int diameter = diameters[depth];
            int3 hash = ((lattice % diameter) + diameter) % diameter;
            return (hash.x * diameter + hash.y) * diameter + hash.z;
        }

        public Subscription Allocate(IChunkConsumer owner, int3 coordinate, int depth, ulong id = 0) {
            if (disposed) throw new ObjectDisposedException(nameof(VisualChunkGenerationDriver));
            int index = Index(coordinate, depth);
            var previous = regions[depth][index];
            if (previous != null && previous.Coordinate.Equals(coordinate)
                && ReferenceEquals(previous.Owner, owner) && (id == 0 || id == previous.Id)) return previous;
            if (id == 0) id = ++nextId;

            var subscription = new Subscription(this, owner, coordinate, depth, id);
            regions[depth][index] = subscription;
            previous?.Release();
            return subscription;
        }

        public void Release(int3 coordinate, int depth, ulong id) {
            int index = Index(coordinate, depth);
            var subscription = regions[depth][index];
            if (subscription == null || subscription.Id != id || !subscription.Coordinate.Equals(coordinate)) return;
            subscription.Release();
        }

        // Discover recipients without allocating a lattice of underlying real chunks.
        public static void CollectSources(int3 coordinate, HashSet<ChunkSource> sources) {
            foreach (var driver in drivers)
                driver.Visit(coordinate, _ => sources.Add(driver.Source));
        }

        public void ReflectChunk(int3 coordinate, IReadOnlyDictionary<uint, MapData> changes) {
            Visit(coordinate, subscription => {
                int scale = 1 << subscription.Depth;
                int axis = chunkSize;
                bool changed = false;
                foreach (var change in changes) {
                    int index = (int)change.Key;
                    int3 cell = new(index / (chunkSize * chunkSize), index / chunkSize % chunkSize, index % chunkSize);
                    int3 relative = (coordinate - subscription.Coordinate) * chunkSize + cell;
                    if (math.any(relative % scale != 0)) continue;
                    int3 sample = relative / scale;
                    if (math.any(sample < -1) || math.any(sample > axis + 1)) continue;
                    changed = true;
                    if (math.any(sample < 0) || math.any(sample >= axis)) continue;
                    subscription.Changes[(sample.x * axis + sample.y) * axis + sample.z] = change.Value;
                }
                if (changed) subscription.Owner?.ReflectChunk();
            });
        }

        private void Visit(int3 coordinate, Action<Subscription> visit) {
            for (int depth = 1; depth <= maxDepth; depth++) {
                int scale = 1 << depth;
                int offset = rootOffset % scale;
                int3 relative = coordinate - offset;
                int3 origin = relative - ((relative % scale) + scale) % scale + offset;
                for (int x = -1; x <= 1; x++) {
                for (int y = -1; y <= 1; y++) {
                for (int z = -1; z <= 1; z++) {
                    int3 candidate = origin + new int3(x, y, z) * scale;
                    var subscription = regions[depth][Index(candidate, depth)];
                    if (subscription == null || !subscription.Coordinate.Equals(candidate)) continue;
                    double3 min = (double3)candidate * chunkSize - scale;
                    double3 max = (double3)candidate * chunkSize + (long)(chunkSize + 1) * scale;
                    double3 changedMin = (double3)coordinate * chunkSize;
                    if (math.any(changedMin > max) || math.any(changedMin + chunkSize - 1 < min)) continue;
                    visit(subscription);
                }}}
            }
        }

        public struct Request {
            public int3 Coordinate;
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
            public readonly int3 Coordinate;
            public readonly int Depth;
            public readonly ulong Id;
            public bool Active { get; private set; } = true;
            public readonly Dictionary<int, MapData> Changes = new();
            public Request Request => new() { Coordinate = Coordinate, Depth = Depth, Id = Id };
            private readonly VisualChunkGenerationDriver driver;
            private readonly CancellationTokenSource cancellation = new();
            public CancellationToken Token { get; }

            internal Subscription(VisualChunkGenerationDriver driver, IChunkConsumer owner, int3 coordinate, int depth, ulong id) {
                this.driver = driver;
                Owner = owner;
                Coordinate = coordinate;
                Depth = depth;
                Id = id;
                Token = cancellation.Token;
            }

            public void Release() {
                if (!Active) return;
                Active = false;
                int index = driver.Index(Coordinate, Depth);
                if (ReferenceEquals(driver.regions[Depth][index], this)) driver.regions[Depth][index] = null;
                Changes.Clear();
                cancellation.Cancel();
                cancellation.Dispose();
                driver.Source.ReleaseVisual(Coordinate, Depth, Id);
            }
        }
    }
}
