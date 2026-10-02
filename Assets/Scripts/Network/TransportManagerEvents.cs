using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;

namespace Arterra.Core.Network {
    /// <summary>A complete, reconstructed application event. IDs are independent of packet boundaries.</summary>
    public readonly struct TransportEvent {
        public readonly NetworkConnection Peer;
        public readonly uint ReferenceId, MessageId, ReplyTo;
        public readonly ushort EventId;
        public readonly byte[] Payload;
        internal TransportEvent(NetworkConnection peer, uint referenceId, ushort eventId,
            uint messageId, uint replyTo, byte[] payload) {
            Peer = peer; ReferenceId = referenceId; EventId = eventId;
            MessageId = messageId; ReplyTo = replyTo; Payload = payload;
        }
    }

    public interface ITransportEventListener {
        void OnEvent(TransportEvent message);
    }

    public sealed partial class TransportManager {
        private const int MessageIdBytes = sizeof(uint);
        private const int FirstHeaderBytes = MessageIdBytes + sizeof(uint) * 2 + sizeof(ushort) + sizeof(int);
        private const int ChunkBytes = 900;
        private const int MaxChunksPerPeerPerUpdate = 8;
        private const int DefaultEventTimeoutSeconds = 60;
        public static readonly TimeSpan DefaultEventTimeout = TimeSpan.FromSeconds(DefaultEventTimeoutSeconds);
        private readonly Dictionary<uint, ITransportEventListener> listeners = new();
        private readonly Dictionary<NetworkConnection, Queue<OutgoingEvent>> outgoing = new();
        private readonly Dictionary<NetworkConnection, Dictionary<uint, IncomingEvent>> incoming = new();
        private readonly Dictionary<(NetworkConnection, uint), EventRequest> requests = new();
        private readonly List<NetworkConnection> peerSnapshot = new();
        private uint nextMessageId;

        /// <summary>One listener per reference, regardless of the listener's value type.</summary>
        public void Register(uint referenceId, ITransportEventListener listener) {
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (listeners.ContainsKey(referenceId)) throw new InvalidOperationException($"Reference {referenceId} is already registered.");
            listeners.Add(referenceId, listener);
        }

        public void Unregister(uint referenceId, ITransportEventListener listener) {
            if (!listeners.TryGetValue(referenceId, out var current) || !ReferenceEquals(current, listener)) return;
            listeners.Remove(referenceId);
            foreach (var pair in new List<KeyValuePair<(NetworkConnection, uint), EventRequest>>(requests)) {
                if (pair.Value.ReferenceId != referenceId) continue;
                pair.Value.Response.TrySetException(new ObjectDisposedException($"Reference {referenceId}"));
                requests.Remove(pair.Key);
            }
        }

        /// <summary>Queue a complete event. Transport owns its copied bytes and all packet-level work.</summary>
        public uint SendEvent(NetworkConnection peer, uint referenceId, ushort eventId,
            byte[] payload, uint replyTo = 0) {
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            return QueueEvent(peer, referenceId, eventId, (byte[])payload.Clone(), replyTo).Id;
        }

        public void Broadcast(uint referenceId, ushort eventId, byte[] payload,
            NetworkConnection replyPeer = default, uint replyTo = 0) {
            if (!IsServer) throw new InvalidOperationException("Only the server can broadcast to its peers.");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            if (eventId == 0) throw new ArgumentOutOfRangeException(nameof(eventId));
            if (peers.Count == 0) return;
            // Queue entries have independent send positions but share one immutable payload copy.
            byte[] owned = (byte[])payload.Clone();
            foreach (var peer in peers)
                QueueEvent(peer, referenceId, eventId, owned, peer == replyPeer ? replyTo : 0);
        }

        /// <summary>Await a correlated reply. Cancellation stops waiting; it does not undo remote execution.</summary>
        public async Task<TransportEvent> RequestEventAsync(NetworkConnection peer, uint referenceId,
            ushort eventId, byte[] payload, CancellationToken token = default, TimeSpan? timeout = null) {
            token.ThrowIfCancellationRequested();
            var duration = timeout ?? DefaultEventTimeout;
            ValidateTimeout(duration);
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            var queued = QueueEvent(peer, referenceId, eventId, (byte[])payload.Clone(), 0);
            var request = new EventRequest(referenceId);
            var key = (peer, queued.Id);
            requests.Add(key, request);
            try {
                using var timer = new CancellationTokenSource();
                using var cancellation = token.Register(() => request.Response.TrySetCanceled());
                var delay = Task.Delay(duration, timer.Token);
                try {
                    if (await Task.WhenAny(request.Response.Task, delay) != request.Response.Task)
                        throw new TimeoutException($"Event request for reference {referenceId} timed out.");
                    return await request.Response.Task;
                } finally { timer.Cancel(); }
            } finally {
                requests.Remove(key);
                // A canceled/timed-out request need not start sending. A partially sent
                // message must finish so subsequent messages keep valid boundaries.
                if (queued.Sent == 0) queued.Canceled = true;
            }
        }

        private static void ValidateTimeout(TimeSpan duration) {
            if (duration.TotalMilliseconds < 1 || duration.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // Payload ownership has already been transferred at the public API boundary.
        private OutgoingEvent QueueEvent(NetworkConnection peer, uint referenceId, ushort eventId,
            byte[] payload, uint replyTo) {
            if (!IsRunning || !peers.Contains(peer)) throw new InvalidOperationException("Peer is not connected.");
            if (eventId == 0) throw new ArgumentOutOfRangeException(nameof(eventId), "Event ID zero is reserved.");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            uint id = ++nextMessageId;
            if (id == 0) id = ++nextMessageId;
            if (!outgoing.TryGetValue(peer, out var queue)) outgoing.Add(peer, queue = new());
            var message = new OutgoingEvent {
                ReferenceId = referenceId, EventId = eventId, Id = id, ReplyTo = replyTo,
                Bytes = payload,
                Deadline = Stopwatch.GetTimestamp() + (long)(DefaultEventTimeout.TotalSeconds * Stopwatch.Frequency)
            };
            queue.Enqueue(message);
            return message;
        }

        private void UpdateEvents() {
            long now = Stopwatch.GetTimestamp();
            peerSnapshot.Clear();
            peerSnapshot.AddRange(peers);
            foreach (var peer in peerSnapshot) {
                bool expired = false;
                if (incoming.TryGetValue(peer, out var messages)) {
                    foreach (var partial in messages)
                        if (now >= partial.Value.Deadline) { expired = true; break; }
                }
                if (expired) { Disconnect(peer); continue; }
                if (!outgoing.TryGetValue(peer, out var queue)) continue;
                for (int n = 0; n < MaxChunksPerPeerPerUpdate && queue.Count > 0; n++) {
                    var message = queue.Peek();
                    if (message.Canceled) { queue.Dequeue(); continue; }
                    if (now >= message.Deadline) { Disconnect(peer); break; }
                    int count = Math.Min(ChunkBytes, message.Bytes.Length - message.Sent);
                    if (!TrySendChunk(peer, message, count)) break;
                    message.Sent += count;
                    if (message.Sent == message.Bytes.Length) queue.Dequeue();
                }
                if (queue.Count == 0) outgoing.Remove(peer);
            }
        }

        private void ReceivePacket(NetworkConnection peer, DataStreamReader reader) {
            if (reader.Length < MessageIdBytes) throw new InvalidDataException("Missing message ID.");
            uint messageId = reader.ReadUInt();
            if (messageId == 0) throw new InvalidDataException("Message ID zero is reserved.");

            incoming.TryGetValue(peer, out var messages);
            IncomingEvent message = null;
            int count = reader.Length - MessageIdBytes;
            if (messages == null || !messages.TryGetValue(messageId, out message)) {
                // An unknown ID starts a message; only this packet carries its metadata.
                if (reader.Length < FirstHeaderBytes) throw new InvalidDataException("Incomplete message header.");
                uint referenceId = reader.ReadUInt();
                ushort eventId = reader.ReadUShort();
                uint replyTo = reader.ReadUInt();
                int total = reader.ReadInt();
                count = reader.Length - FirstHeaderBytes;
                if (eventId == 0 || total < 0 || count > total || count > ChunkBytes || (count == 0 && total != 0))
                    throw new InvalidDataException("Invalid message size or event ID.");
                message = new IncomingEvent {
                    ReferenceId = referenceId, EventId = eventId, Id = messageId, ReplyTo = replyTo,
                    Bytes = new byte[total],
                    Deadline = Stopwatch.GetTimestamp() + (long)(DefaultEventTimeout.TotalSeconds * Stopwatch.Frequency)
                };
                if (messages == null) incoming.Add(peer, messages = new());
                messages.Add(messageId, message);
            }
            if (count > ChunkBytes || count > message.Bytes.Length - message.Received
                || (count == 0 && message.Bytes.Length != 0))
                throw new InvalidDataException("Invalid message payload length.");
            // Reliable delivery preserves each message's chunk order, including when interleaved.
            for (int i = 0; i < count; i++) message.Bytes[message.Received + i] = reader.ReadByte();
            message.Received += count;
            if (message.Received != message.Bytes.Length) return;
            messages.Remove(messageId);
            var complete = new TransportEvent(peer, message.ReferenceId, message.EventId,
                messageId, message.ReplyTo, message.Bytes);
            requests.TryGetValue((peer, message.ReplyTo), out var request);
            if (request != null && request.ReferenceId != message.ReferenceId)
                throw new InvalidDataException("Reply targets the wrong reference.");
            // Apply state before waking a caller awaiting this response.
            try {
                if (listeners.TryGetValue(message.ReferenceId, out var listener)) listener.OnEvent(complete);
                request?.Response.TrySetResult(complete);
            } catch (Exception exception) {
                request?.Response.TrySetException(exception);
                UnityEngine.Debug.LogException(exception);
            }
        }

        private void ForgetPeer(NetworkConnection peer, Exception reason) {
            outgoing.Remove(peer);
            incoming.Remove(peer);
            foreach (var pair in new List<KeyValuePair<(NetworkConnection, uint), EventRequest>>(requests)) {
                if (pair.Key.Item1 != peer) continue;
                pair.Value.Response.TrySetException(reason);
                requests.Remove(pair.Key);
            }
        }

        private void ResetEvents(Exception reason) {
            foreach (var request in requests.Values) {
                if (reason == null) request.Response.TrySetCanceled();
                else request.Response.TrySetException(reason);
            }
            requests.Clear(); outgoing.Clear(); incoming.Clear();
        }

        private sealed class EventRequest {
            public readonly uint ReferenceId;
            public readonly TaskCompletionSource<TransportEvent> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public EventRequest(uint referenceId) => ReferenceId = referenceId;
        }
        private sealed class OutgoingEvent {
            public uint ReferenceId, Id, ReplyTo;
            public ushort EventId;
            public byte[] Bytes;
            public int Sent;
            public bool Canceled;
            public long Deadline;
        }
        private sealed class IncomingEvent {
            public uint ReferenceId, Id, ReplyTo;
            public ushort EventId;
            public byte[] Bytes;
            public int Received;
            public long Deadline;
        }
    }
}
