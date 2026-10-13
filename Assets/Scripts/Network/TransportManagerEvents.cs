using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Networking.Transport;
using Error = Unity.Networking.Transport.Error;

namespace Arterra.Core.Network {
    /// <summary>A complete, reconstructed application event. IDs are independent of packet boundaries.</summary>
    public readonly struct TransportEvent {
        public readonly NetworkConnection Peer;
        public readonly Guid ReferenceId;
        public readonly uint MessageId, ReplyTo;
        public readonly ushort EventId;
        public readonly byte[] Payload;
        internal TransportEvent(NetworkConnection peer, Guid referenceId, ushort eventId,
            uint messageId, uint replyTo, byte[] payload) {
            Peer = peer; ReferenceId = referenceId; EventId = eventId;
            MessageId = messageId; ReplyTo = replyTo; Payload = payload;
        }
    }

    public interface ITransportEventListener {
        void OnEvent(TransportEvent message);
        void OnSubscribed(NetworkConnection peer) { }
    }

    public sealed partial class TransportManager {
        // Guid.Empty is reserved for transport subscription control.
        private const ushort Subscribe = 1, Unsubscribe = 2;
        private readonly Dictionary<NetworkConnection, HashSet<Guid>> subscriptions = new();
        private const int MessageIdBytes = sizeof(uint);
        private const int ReferenceIdBytes = 16;
        private const int FragmentHeaderBytes = MessageIdBytes + sizeof(ushort);
        private const int FirstHeaderBytes = FragmentHeaderBytes + ReferenceIdBytes + sizeof(uint) + sizeof(ushort) + sizeof(int);
        private const int DefaultEventTimeoutSeconds = 60;
        public static readonly TimeSpan DefaultEventTimeout = TimeSpan.FromSeconds(DefaultEventTimeoutSeconds);
        private readonly Dictionary<Guid, ITransportEventListener> listeners = new();
        private readonly Dictionary<NetworkConnection, LinkedList<OutgoingEvent>> outgoing = new();
        private readonly Dictionary<NetworkConnection, Dictionary<uint, IncomingEvent>> incoming = new();
        private readonly Dictionary<(NetworkConnection, uint), EventRequest> requests = new();
        private readonly List<NetworkConnection> peerSnapshot = new();
        private ConcurrentQueue<(NetworkConnection peer, OutgoingEvent message)> pendingEvents = new();
        private int nextMessageId;
        private int firstSendPeer;
        private readonly Dictionary<(NetworkConnection, Guid), OutgoingEvent> variableUpdates = new();
        private readonly Queue<NetworkConnection> sendPeers = new();
        private readonly List<(LinkedListNode<OutgoingEvent> node, int count)> packetFragments = new();

        /// <summary>One listener per reference, regardless of the listener's value type.</summary>
        public void Register(Guid referenceId, ITransportEventListener listener) {
            if (referenceId == Guid.Empty) throw new ArgumentException("Reference ID zero is reserved for transport control.", nameof(referenceId));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            if (listeners.ContainsKey(referenceId)) throw new InvalidOperationException($"Reference {referenceId} is already registered.");
            listeners.Add(referenceId, listener);
            if (!IsRunning) return;
            if (!IsServer) SendSubscription(referenceId, Subscribe);
            else foreach (var pair in subscriptions) {
                if (pair.Value.Contains(referenceId)) listener.OnSubscribed(pair.Key);
            }
        }

        private void SendSubscription(Guid referenceId, ushort operation) =>
            SendEvent(server, Guid.Empty, operation, referenceId.ToByteArray());

        public bool IsSubscribed(NetworkConnection peer, Guid referenceId) =>
            subscriptions.TryGetValue(peer, out var references) && references.Contains(referenceId);

        private void ReceiveSubscription(TransportEvent message) {
            if (!IsServer || message.ReplyTo != 0 || message.Payload.Length != ReferenceIdBytes
                || (message.EventId != Subscribe && message.EventId != Unsubscribe))
                throw new InvalidDataException("Invalid subscription control message.");
            var referenceId = new Guid(message.Payload);
            if (referenceId == Guid.Empty) throw new InvalidDataException("Cannot subscribe to transport control.");

            if (message.EventId == Subscribe) {
                if (!subscriptions.TryGetValue(message.Peer, out var references))
                    subscriptions.Add(message.Peer, references = new());
                if (references.Add(referenceId) && listeners.TryGetValue(referenceId, out var listener))
                    listener.OnSubscribed(message.Peer);
            } else {
                if (subscriptions.TryGetValue(message.Peer, out var references)) {
                    references.Remove(referenceId);
                    if (references.Count == 0) subscriptions.Remove(message.Peer);
                }
                if (!outgoing.TryGetValue(message.Peer, out var queue)) return;
                foreach (var update in queue) {
                    if (update.ReferenceId == referenceId && (update.Broadcast || update.Replaceable)
                        && update.ReplyTo == 0 && update.Sent == 0)
                        update.Canceled = true;
                }
            }
        }

        public void Unregister(Guid referenceId, ITransportEventListener listener) {
            if (!listeners.TryGetValue(referenceId, out var current) || !ReferenceEquals(current, listener)) return;
            listeners.Remove(referenceId);
            if (IsRunning && !IsServer) SendSubscription(referenceId, Unsubscribe);
            foreach (var pair in variableUpdates) {
                if (pair.Key.Item2 != referenceId) continue;
                // An in-progress message must finish, but its unsent replacement is disposable.
                if (pair.Value.Sent == 0) pair.Value.Canceled = true;
                if (pair.Value.Pending != null) pair.Value.Pending.Value.Canceled = true;
            }
            foreach (var pair in new List<KeyValuePair<(NetworkConnection, uint), EventRequest>>(requests)) {
                if (pair.Value.ReferenceId != referenceId) continue;
                pair.Value.Response.TrySetException(new ObjectDisposedException($"Reference {referenceId}"));
                requests.Remove(pair.Key);
            }
        }

        /// <summary>Queue a complete event. Transport owns its copied bytes and all packet-level work.</summary>
        public uint SendEvent(NetworkConnection peer, Guid referenceId, ushort eventId,
            byte[] payload, uint replyTo = 0, bool replacePending = false) {
            return QueueEvent(peer, referenceId, eventId, payload, replyTo, replacePending: replacePending).Id;
        }

        public void Broadcast(Guid referenceId, ushort eventId, byte[] payload,
            NetworkConnection replyPeer = default, uint replyTo = 0, bool replacePending = false) {
            if (!IsServer) throw new InvalidOperationException("Only the server can broadcast to its peers.");
            QueueEvent(replyPeer, referenceId, eventId, payload, replyTo, broadcast: true, replacePending: replacePending);
        }

        /// <summary>Await a correlated reply. Cancellation stops waiting; it does not undo remote execution.</summary>
        public async Task<TransportEvent> RequestEventAsync(NetworkConnection peer, Guid referenceId,
            ushort eventId, byte[] payload, CancellationToken token = default, TimeSpan? timeout = null) {
            token.ThrowIfCancellationRequested();
            var duration = timeout ?? DefaultEventTimeout;
            ValidateTimeout(duration);
            var queued = QueueEvent(peer, referenceId, eventId, payload, 0);
            var request = new EventRequest(referenceId);
            var key = (peer, queued.Id);
            requests.Add(key, request);
            UnityEngine.Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Request queued: peer {peer}, reference {referenceId}, event {eventId}, message {queued.Id}.");
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

        // Validate and copy once at the shared entry point for sends, broadcasts, and requests.
        private OutgoingEvent QueueEvent(NetworkConnection peer, Guid referenceId, ushort eventId,
            byte[] payload, uint replyTo, bool broadcast = false, bool replacePending = false) {
            if (replacePending && replyTo != 0)
                throw new ArgumentException("Correlated replies cannot be replaced.", nameof(replacePending));
            var pending = Volatile.Read(ref pendingEvents);
            if (!IsRunning) throw new InvalidOperationException("Peer is not connected.");
            if (eventId == 0) throw new ArgumentOutOfRangeException(nameof(eventId), "Event ID zero is reserved.");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            uint id;
            do { id = unchecked((uint)Interlocked.Increment(ref nextMessageId)); } while (id == 0);
            var message = new OutgoingEvent {
                ReferenceId = referenceId, EventId = eventId, Id = id, ReplyTo = replyTo,
                Bytes = (byte[])payload.Clone(), Replaceable = replacePending, Broadcast = broadcast,
                Deadline = Stopwatch.GetTimestamp() + (long)(DefaultEventTimeout.TotalSeconds * Stopwatch.Frequency)
            };
            pending.Enqueue((peer, message));
            return message;
        }

        // Only Tick touches peer lists and the per-peer outgoing queues.
        private void DrainEvents() {
            // Capture this update's batch; producers can continue queuing for the next update.
            int count = pendingEvents.Count;
            while (count-- > 0 && pendingEvents.TryDequeue(out var pending)) {
                var message = pending.message;
                if (message.Canceled || (message.Replaceable && !listeners.ContainsKey(message.ReferenceId))) continue;
                if (message.Broadcast) {
                    if (!IsServer) continue;
                    foreach (var peer in peers) {
                        if (!IsSubscribed(peer, message.ReferenceId) && !(peer == pending.peer && message.ReplyTo != 0)) continue;
                        // Each recipient owns its send position; payload bytes are shared.
                        EnqueueEvent(peer, new OutgoingEvent {
                            ReferenceId = message.ReferenceId, EventId = message.EventId, Id = message.Id,
                            ReplyTo = peer == pending.peer ? message.ReplyTo : 0,
                            Bytes = message.Bytes, Deadline = message.Deadline, Replaceable = message.Replaceable, Broadcast = true
                        });
                    }
                } else if (peers.Contains(pending.peer)
                    && (!message.Replaceable || !IsServer || IsSubscribed(pending.peer, message.ReferenceId))) {
                    EnqueueEvent(pending.peer, message);
                }
            }
        }

        private void EnqueueEvent(NetworkConnection peer, OutgoingEvent message) {
            if (!outgoing.TryGetValue(peer, out var queue)) outgoing.Add(peer, queue = new());
            if (message.Replaceable) {
                var key = (peer, message.ReferenceId);
                if (variableUpdates.TryGetValue(key, out var current)) {
                    if (current.Pending != null) {
                        queue.Remove(current.Pending);
                        current.Pending.Value = message;
                        queue.AddLast(current.Pending);
                    } else current.Pending = queue.AddLast(message);
                    return;
                }
                variableUpdates.Add(key, message);
            }
            queue.AddLast(message);
        }

        private void RemoveOutgoing(NetworkConnection peer, LinkedList<OutgoingEvent> queue,
            LinkedListNode<OutgoingEvent> node) {
            var message = node.Value;
            if (message.Replaceable) {
                var key = (peer, message.ReferenceId);
                if (message.Pending != null) variableUpdates[key] = message.Pending.Value;
                else variableUpdates.Remove(key);
            }
            queue.Remove(node);
        }

        private void UpdateEvents() {
            DrainEvents();
            long now = Stopwatch.GetTimestamp();
            peerSnapshot.Clear();
            peerSnapshot.AddRange(peers);
            foreach (var peer in peerSnapshot) {
                if (!incoming.TryGetValue(peer, out var messages)) continue;
                foreach (var partial in messages) {
                    if (now < partial.Value.Deadline) continue;
                    UnityEngine.Debug.LogWarning($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Incoming event {partial.Value.EventId}, message {partial.Key} from {peer} expired after receiving {partial.Value.Received}/{partial.Value.Bytes.Length} bytes.");
                    Disconnect(peer);
                    break;
                }
            }

            if (peerSnapshot.Count == 0) return;
            int start = firstSendPeer++ % peerSnapshot.Count;
            if (firstSendPeer == int.MaxValue) firstSendPeer = 0;
            sendPeers.Clear();
            for (int i = 0; i < peerSnapshot.Count; i++)
                sendPeers.Enqueue(peerSnapshot[(start + i) % peerSnapshot.Count]);

            while (sendPeers.TryDequeue(out var peer)) {
                if (!peers.Contains(peer) || !outgoing.TryGetValue(peer, out var queue)) continue;
                try {
                    bool sent = TrySendPacket(peer, queue, now);
                    if (queue.Count == 0) outgoing.Remove(peer);
                    else if (sent) sendPeers.Enqueue(peer);
                    // A blocked peer retries on the next transport update.
                } catch (IOException exception) {
                    UnityEngine.Debug.LogException(exception);
                    Disconnect(peer);
                }
            }
        }

        // Stage fragments without consuming queue entries; commit only after EndSend succeeds.
        private bool TrySendPacket(NetworkConnection peer, LinkedList<OutgoingEvent> queue, long now) {
            while (queue.First != null && queue.First.Value.Canceled)
                RemoveOutgoing(peer, queue, queue.First);
            if (queue.Count == 0) return false;

            int capacity = driver.GetMaxSupportedPayloadSize(peer, reliable);
            if (capacity < FirstHeaderBytes)
                throw new IOException($"Transport payload capacity is invalid: {capacity}.");
            int result = driver.BeginSend(reliable, peer, out var writer, capacity);
            if (!SendAccepted(result, peer)) return false;

            packetFragments.Clear();
            for (var node = queue.First; node != null; node = node.Next) {
                var message = node.Value;
                if (message.Canceled) continue;
                // Pending state cannot begin until its predecessor's packet has been submitted.
                if (message.Replaceable && variableUpdates[(peer, message.ReferenceId)] != message) break;
                if (now >= message.Deadline) {
                    driver.AbortSend(writer);
                    throw new IOException($"Outgoing event {message.EventId}, message {message.Id} to {peer} expired after sending {message.Sent}/{message.Bytes.Length} bytes.");
                }
                bool first = message.Sent == 0;
                int headerBytes = first ? FirstHeaderBytes : FragmentHeaderBytes;
                int available = writer.Capacity - writer.Length - headerBytes;
                int remaining = message.Bytes.Length - message.Sent;
                if (available < 0 || (available == 0 && remaining > 0)) break;
                int count = Math.Min(remaining, Math.Min(available, ushort.MaxValue));
                writer.WriteUInt(message.Id);
                writer.WriteUShort((ushort)count);
                if (first) {
                    Span<byte> referenceBytes = stackalloc byte[ReferenceIdBytes];
                    message.ReferenceId.TryWriteBytes(referenceBytes);
                    for (int i = 0; i < ReferenceIdBytes; i++) writer.WriteByte(referenceBytes[i]);
                    writer.WriteUShort(message.EventId);
                    writer.WriteUInt(message.ReplyTo);
                    writer.WriteInt(message.Bytes.Length);
                }
                for (int i = 0; i < count; i++) writer.WriteByte(message.Bytes[message.Sent + i]);
                packetFragments.Add((node, count));
                if (count < remaining) break;
            }

            result = driver.EndSend(writer);
            if (!SendAccepted(result, peer)) {
                packetFragments.Clear();
                return false;
            }
            foreach (var fragment in packetFragments) {
                var message = fragment.node.Value;
                message.Sent += fragment.count;
                if (message.ReplyTo != 0)
                    UnityEngine.Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Reply send submitted: peer {peer}, event {message.EventId}, message {message.Id}, reply to {message.ReplyTo}, {message.Sent}/{message.Bytes.Length} bytes.");
                if (message.Sent == message.Bytes.Length) RemoveOutgoing(peer, queue, fragment.node);
            }
            packetFragments.Clear();
            return true;
        }

        private static bool SendAccepted(int result, NetworkConnection peer) {
            if (result >= 0) return true;
            if (result == (int)Error.StatusCode.NetworkSendQueueFull) return false;
            throw new IOException($"Transport send to {peer} failed: {(Error.StatusCode)result} ({result}).");
        }

        private void ReceivePacket(NetworkConnection peer, DataStreamReader reader) {
            if (reader.Length == 0) throw new InvalidDataException("Empty event packet.");
            while (reader.GetBytesRead() < reader.Length) {
                ReceiveFragment(peer, ref reader);
                if (!driver.IsCreated || !peers.Contains(peer)) return;
            }
        }

        private void ReceiveFragment(NetworkConnection peer, ref DataStreamReader reader) {
            if (reader.Length - reader.GetBytesRead() < FragmentHeaderBytes)
                throw new InvalidDataException("Incomplete fragment header.");
            uint messageId = reader.ReadUInt();
            if (messageId == 0) throw new InvalidDataException("Message ID zero is reserved.");

            int count = reader.ReadUShort();
            incoming.TryGetValue(peer, out var messages);
            IncomingEvent message = null;
            if (messages == null || !messages.TryGetValue(messageId, out message)) {
                // An unknown ID starts a message; only this packet carries its metadata.
                if (reader.Length - reader.GetBytesRead() < FirstHeaderBytes - FragmentHeaderBytes) throw new InvalidDataException("Incomplete message header.");
                Span<byte> referenceBytes = stackalloc byte[ReferenceIdBytes];
                for (int i = 0; i < ReferenceIdBytes; i++) referenceBytes[i] = reader.ReadByte();
                Guid referenceId = new Guid(referenceBytes);
                ushort eventId = reader.ReadUShort();
                uint replyTo = reader.ReadUInt();
                int total = reader.ReadInt();
                if (eventId == 0 || total < 0 || count > total || (count == 0 && total != 0))
                    throw new InvalidDataException("Invalid message size or event ID.");
                message = new IncomingEvent {
                    ReferenceId = referenceId, EventId = eventId, ReplyTo = replyTo,
                    Bytes = new byte[total],
                    Deadline = Stopwatch.GetTimestamp() + (long)(DefaultEventTimeout.TotalSeconds * Stopwatch.Frequency)
                };
                if (messages == null) incoming.Add(peer, messages = new());
                messages.Add(messageId, message);
            }
            if (count > reader.Length - reader.GetBytesRead() || count > message.Bytes.Length - message.Received
                || (count == 0 && message.Bytes.Length != 0))
                throw new InvalidDataException("Invalid message payload length.");
            // Reliable delivery preserves each message's chunk order, including when interleaved.
            for (int i = 0; i < count; i++) message.Bytes[message.Received + i] = reader.ReadByte();
            message.Received += count;
            if (message.ReplyTo != 0)
                UnityEngine.Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Reply received: peer {peer}, event {message.EventId}, message {messageId}, reply to {message.ReplyTo}, {message.Received}/{message.Bytes.Length} bytes.");
            if (message.Received != message.Bytes.Length) return;
            messages.Remove(messageId);
            DispatchEvent(new TransportEvent(peer, message.ReferenceId, message.EventId,
                messageId, message.ReplyTo, message.Bytes));
        }

        private void DispatchEvent(TransportEvent message) {
            if (message.ReferenceId == Guid.Empty) {
                ReceiveSubscription(message);
                return;
            }
            var peer = message.Peer;
            requests.TryGetValue((peer, message.ReplyTo), out var request);
            if (message.ReplyTo != 0)
                UnityEngine.Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Reply assembled: message {message.MessageId}, reply to {message.ReplyTo}, pending request found: {request != null}.");
            if (request != null && request.ReferenceId != message.ReferenceId)
                throw new InvalidDataException("Reply targets the wrong reference.");
            // Apply state before waking a caller awaiting this response.
            try {
                if (listeners.TryGetValue(message.ReferenceId, out var listener)) listener.OnEvent(message);
                request?.Response.TrySetResult(message);
            } catch (Exception exception) {
                request?.Response.TrySetException(exception);
                UnityEngine.Debug.LogException(exception);
            }
        }

        private void ForgetPeer(NetworkConnection peer, Exception reason) {
            subscriptions.Remove(peer);
            if (outgoing.TryGetValue(peer, out var queue)) {
                foreach (var message in queue)
                    if (message.Replaceable) variableUpdates.Remove((peer, message.ReferenceId));
            }
            outgoing.Remove(peer);
            incoming.Remove(peer);
            foreach (var pair in new List<KeyValuePair<(NetworkConnection, uint), EventRequest>>(requests)) {
                if (pair.Key.Item1 != peer) continue;
                pair.Value.Response.TrySetException(reason);
                requests.Remove(pair.Key);
            }
        }

        private void ResetEvents(Exception reason) {
            // Late producers keep the old queue, so their sends cannot enter a new session.
            Interlocked.Exchange(ref pendingEvents, new());
            foreach (var request in requests.Values) {
                if (reason == null) request.Response.TrySetCanceled();
                else request.Response.TrySetException(reason);
            }
            requests.Clear(); outgoing.Clear(); incoming.Clear(); variableUpdates.Clear(); subscriptions.Clear();
            packetFragments.Clear();
        }

        private sealed class EventRequest {
            public readonly Guid ReferenceId;
            public readonly TaskCompletionSource<TransportEvent> Response = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public EventRequest(Guid referenceId) => ReferenceId = referenceId;
        }
        private sealed class OutgoingEvent {
            public Guid ReferenceId;
            public uint Id, ReplyTo;
            public ushort EventId;
            public byte[] Bytes;
            public int Sent;
            public bool Canceled, Replaceable, Broadcast;
            public LinkedListNode<OutgoingEvent> Pending;
            public long Deadline;
        }
        private sealed class IncomingEvent {
            public Guid ReferenceId;
            public uint ReplyTo;
            public ushort EventId;
            public byte[] Bytes;
            public int Received;
            public long Deadline;
        }
    }
}
