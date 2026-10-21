using System;
using System.Threading.Tasks;
using System.Threading;
using System.Collections.Generic;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Unity.Mathematics;
using ChunkMapInfo = Arterra.Core.Storage.CPUMapManager.ChunkMapInfo;

namespace Arterra.Core.Storage {
    /// <summary>Supplies authoritative content/permissions. The driver owns generation and shared completion.</summary>
    public abstract class ChunkSource : IDisposable {
        public abstract Task<Chunk.ReadbackInfo> ReadAsync(int3 coordinate, CancellationToken token = default);

        public abstract Task<MapData[]> ReadVisualAsync(VisualChunkGenerationDriver.Subscription subscription);

        public virtual void ReleaseVisual(int3 coordinate, int depth, ulong allocationId) { }

        // Receives already-offloaded data. The caller owns disposal after every user finishes.
        public abstract Task ReleaseAsync(ChunkMapInfo chunk);

        public virtual Task InitializeEntitiesAsync(int3 coordinate, List<Entity> entities, CancellationToken token) =>
            EntityManager.InitializeChunkEntitiesAsync(entities, token);

        public virtual void Dispose() { }

        public abstract void EnterEntity(Entity entity);

        public abstract void LeaveEntity(Guid entityId);
        public virtual bool DefersMapChanges => false;

        // Local sources consume changes immediately; remote sources retain only the encoder.
        // Encoded layout: int3 coordinate followed by (uint cellIndex, uint packedMapData).
        public abstract void ReflectChunk(int3 coordinate, IReadOnlyDictionary<uint, MapData> changes,
            Func<byte[]> getEncodedChanges = null, Action abandoned = null);
    }

}
