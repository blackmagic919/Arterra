using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Configuration;
using Arterra.Core.Network;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Newtonsoft.Json;
using Unity.Mathematics;

namespace Arterra.Core.Storage {
    /// <summary>Requests authoritative chunks; canceled replies have no effect on local generation.</summary>
    public sealed class ClientSource : ChunkSource, ITransportEventListener {
        public override void ReflectChunk(Func<byte[]> GetEncodedChanges = null) {
            GetEncodedChanges?.Invoke();
        }

        private readonly Guid referenceId;
        private readonly CancellationTokenSource lifetime = new();
        private readonly Dictionary<int3, PendingEntities> pending = new();
        private bool disposed;

        public ClientSource(Guid playerId) {
            referenceId = NetworkSource.ReferenceFor(playerId);
            TransportManager.Instance.Register(referenceId, this);
        }

        public override async Task<Chunk.ReadbackInfo> ReadAsync(int3 coordinate, CancellationToken token = default) {
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
            var entities = new PendingEntities(token);
            pending.Add(coordinate, entities);

            try {
                var transport = TransportManager.Instance;
                var response = await transport.RequestEventAsync(transport.Server, referenceId, NetworkSource.Generate,
                    Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(coordinate)), cancellation.Token);

                cancellation.Token.ThrowIfCancellationRequested();
                if (response.EventId != NetworkSource.Content)
                    throw new IOException(Encoding.UTF8.GetString(response.Payload));

                return JsonConvert.DeserializeObject<ChunkTransfer>(Encoding.UTF8.GetString(response.Payload)).Read();
            } catch {
                RemovePending(coordinate, entities);
                throw;
            }
        }

        // Remote visual map loading is deferred; use the existing visual-chunk default-map fallback.
        public override Task<MapData[]> ReadVisualAsync(int3 coordinate, int depth) =>
            Task.FromResult<MapData[]>(null);

        public override Task InitializeEntitiesAsync(int3 coordinate, List<Entity> entities, CancellationToken token) {
            var completion = new TaskCompletionSource<Task>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.TryGetValue(coordinate, out var changes);

            EntityManager.AddHandlerEvent(() => {
                try {
                    token.ThrowIfCancellationRequested();
                    if (disposed) throw new OperationCanceledException();

                    var merged = entities.ToDictionary(entity => entity.info.rtEntityId);
                    if (changes != null) foreach (var change in changes.Values) {
                        if (change.Value == null) merged.Remove(change.Key);
                        else merged[change.Key] = change.Value;
                    }

                    completion.TrySetResult(EntityManager.InitializeChunkEntitiesAsync(merged.Values.ToList(), token));
                } catch (OperationCanceledException) { completion.TrySetCanceled(); }
                catch (Exception exception) { completion.TrySetException(exception); }
                finally { RemovePending(coordinate, changes); }
            });

            return completion.Task.Unwrap();
        }

        public void OnEvent(TransportEvent message) {
            if (disposed || message.Peer != TransportManager.Instance.Server) return;

            if (message.EventId == NetworkSource.Enter) {
                var entity = JsonConvert.DeserializeObject<Registerable<Entity>>(Encoding.UTF8.GetString(message.Payload)).Value;

                int3 coordinate = EntityManager.GetChunkCoordinate(entity.position);

                if (pending.TryGetValue(coordinate, out var changes)) {
                    if (!changes.Token.IsCancellationRequested) changes.Values[entity.info.rtEntityId] = entity;
                } else EnterEntity(entity);
            } else if (message.EventId == NetworkSource.Leave) {
                Guid entityId = new(message.Payload);
                foreach (var changes in pending.Values)
                    if (!changes.Token.IsCancellationRequested) changes.Values[entityId] = null;

                LeaveEntity(entityId);
            }

            // Content is consumed only by the original RequestEventAsync waiter, never by this listener.
        }

        public override void EnterEntity(Entity entity) {
            EntityManager.AddHandlerEvent(() => {
                if (disposed) return;

                int3 coordinate = EntityManager.GetChunkCoordinate(entity.position);
                if (CPUMapManager.TryGetChunkInfo(coordinate, out var chunk) && chunk.Generation.Subscribers.Count != 0)
                    EntityManager.DeserializeEntity(entity);
            });
        }

        public override void LeaveEntity(Guid entityId) {
            EntityManager.AddHandlerEvent(() => {
                if (disposed) return;

                if (EntityManager.TryGetEntity(entityId, out var entity) && !entity.IsOwner)
                    EntityManager.ReleaseE(entityId);
            });
        }

        public override Task ReleaseAsync(CPUMapManager.ChunkMapInfo chunk) {
            if (pending.TryGetValue(chunk.CCoord, out var changes) && changes.Token.IsCancellationRequested)
                pending.Remove(chunk.CCoord);

            return EntityManager.ReleaseChunkEntitiesAsync(chunk.CCoord, save: false);
        }

        private void RemovePending(int3 coordinate, PendingEntities entities) {
            if (pending.TryGetValue(coordinate, out var current) && ReferenceEquals(current, entities))
                pending.Remove(coordinate);
        }

        public override void Dispose() {
            if (disposed) return;

            disposed = true;
            lifetime.Cancel();

            TransportManager.Instance.Unregister(referenceId, this);
            pending.Clear();
            lifetime.Dispose();
        }

        private sealed class PendingEntities {
            public readonly CancellationToken Token;
            public readonly Dictionary<Guid, Entity> Values = new();

            public PendingEntities(CancellationToken token) => Token = token;
        }
    }
}
