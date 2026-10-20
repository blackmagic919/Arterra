using System;
using System.Collections.Generic;
using System.Text;
using Arterra.Configuration;
using Arterra.Core.Network;
using Arterra.Data.Entity;
using Arterra.Engine.Terrain;
using Newtonsoft.Json;
using Unity.Mathematics;
using LiteNetLib;
using UnityEngine;

namespace Arterra.Core.Storage {
    /// <summary>One remote player's server-side driver. Requests allocate server-local shared chunks.</summary>
    public sealed class NetworkSource : LocalChunkSource, ITransportEventListener, IChunkConsumer {
        internal const ushort Generate = 1, Content = 2, Enter = 3, Leave = 4, Failed = 5, MapChanges = 6;
        public override bool DefersMapChanges => true;

        private readonly NetPeer peer;
        private readonly Guid referenceId;
        private readonly ChunkGenerationDriver driver;
        private bool disposed;

        internal static Guid ReferenceFor(Guid playerId) =>
            NetworkReferenceId.ForEntity(playerId, NetworkReferenceId.FromName("ChunkSource"));

        public NetworkSource(NetPeer peer, Guid playerId) {
            this.peer = peer;
            referenceId = ReferenceFor(playerId);
            driver = new ChunkGenerationDriver(CPUMapManager.numChunksAxis, this);

            TransportManager.Instance.Register(referenceId, this);
        }

        public void OnEvent(TransportEvent message) {
            if (disposed || message.Peer != peer || message.EventId != Generate) return;

            GenerateAsync(message);
        }

        private async void GenerateAsync(TransportEvent request) {
            try {
                int3 coordinate = JsonConvert.DeserializeObject<int3>(Encoding.UTF8.GetString(request.Payload));
                var subscription = driver.Allocate(this, coordinate);
                await subscription.Ready;
                if (disposed || !subscription.Active) return;

                // Capture anew for every request, including requests for an already loaded chunk.
                var transfer = ChunkTransfer.Capture(subscription.Chunk);
                foreach (var entry in transfer.EntityData)
                    EntityNetworkTrace.Record(entry.Value.info.rtEntityId, $"Host captured chunk={coordinate}, request={request.MessageId}, peer={peer}");
                byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(transfer));
                uint sent = TransportManager.Instance.SendEvent(peer, referenceId, Content, bytes, request.MessageId);
                foreach (var entry in transfer.EntityData)
                    EntityNetworkTrace.Record(entry.Value.info.rtEntityId, $"Host queued chunk reply message={sent}, request={request.MessageId}, chunk={coordinate}, peer={peer}");
            } catch (Exception exception) {
                if (disposed) return;

                Debug.LogException(exception);
                TransportManager.Instance.SendEvent(peer, referenceId, Failed,
                    Encoding.UTF8.GetBytes(exception.Message), request.MessageId);
            }
        }

        public override void EnterEntity(Entity entity) {
            if (disposed) return;

            EntityNetworkTrace.Record(entity.info.rtEntityId, $"Host queues EnterEntity, peer={peer}");
            TransportManager.Instance.SendEvent(peer, referenceId, Enter,
                Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Registerable<Entity>(entity))));
        }

        public override void LeaveEntity(Guid entityId) {
            if (disposed) return;

            EntityNetworkTrace.Record(entityId, $"Host queues LeaveEntity, peer={peer}");
            TransportManager.Instance.SendEvent(peer, referenceId, Leave, entityId.ToByteArray());
        }

        void IChunkConsumer.ReflectChunk() { }

        public override void ReflectChunk(int3 coordinate, IReadOnlyDictionary<uint, MapData> changes,
            Func<byte[]> getEncodedChanges = null, Action abandoned = null) {
            if (disposed) { abandoned?.Invoke(); return; }

            // The accumulator already keeps one pending encoder per chunk/source.
            // Transport-level coalescing would incorrectly combine different chunks on this reference.
            TransportManager.Instance.SendEvent(peer, referenceId, MapChanges, getEncodedChanges, abandoned: abandoned);
        }

        public override void Dispose() {
            if (disposed) return;

            disposed = true;

            TransportManager.Instance.Unregister(referenceId, this);
            driver.Release();
        }
    }
}
