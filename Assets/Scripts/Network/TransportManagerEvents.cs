using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LiteNetLib;
using LiteNetLib.Utils;

namespace Arterra.Core.Network {
    /// <summary>A complete application message delivered by LiteNetLib.</summary>
    public readonly struct TransportEvent {
        public readonly NetPeer Peer;
        public readonly Guid ReferenceId;
        public readonly uint MessageId, ReplyTo;
        public readonly ushort EventId;
        public readonly byte[] Payload;
        internal TransportEvent(NetPeer peer, Guid referenceId, ushort eventId,
            uint messageId, uint replyTo, byte[] payload) {
            Peer = peer; ReferenceId = referenceId; EventId = eventId;
            MessageId = messageId; ReplyTo = replyTo; Payload = payload;
        }
    }

    public interface ITransportEventListener {
        void OnEvent(TransportEvent message);
        void OnSubscribed(NetPeer peer) { }
    }

    public sealed partial class TransportManager {
        // Guid.Empty is reserved for transport subscription control.
        private const ushort Subscribe = 1, Unsubscribe = 2;
        private readonly Dictionary<NetPeer, HashSet<Guid>> subscriptions = new();
        private const int ReferenceIdBytes = 16;
        private const int MessageHeaderBytes = ReferenceIdBytes + sizeof(ushort) + 2 * sizeof(uint);
        public static readonly TimeSpan DefaultEventTimeout = TimeSpan.FromSeconds(60);
        private readonly Dictionary<Guid, ITransportEventListener> listeners = new();
        private readonly Dictionary<(NetPeer, uint), EventRequest> requests = new();
        private ConcurrentQueue<(NetPeer peer, OutgoingEvent message)> pendingEvents = new();
        private int nextMessageId;
        private readonly List<(NetPeer peer, OutgoingEvent message)> outgoing = new();
        private readonly Dictionary<(NetPeer, Guid), OutgoingEvent> variableUpdates = new();
        private readonly NetDataWriter messageWriter = new();
        private SendTiming sendTiming;

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

        public bool IsSubscribed(NetPeer peer, Guid referenceId) =>
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
                ForgetVariable((message.Peer, referenceId));
                if (subscriptions.TryGetValue(message.Peer, out var references)) {
                    references.Remove(referenceId);
                    if (references.Count == 0) subscriptions.Remove(message.Peer);
                }
            }
        }

        public void Unregister(Guid referenceId, ITransportEventListener listener) {
            if (!listeners.TryGetValue(referenceId, out var current) || !ReferenceEquals(current, listener)) return;
            listeners.Remove(referenceId);
            if (IsRunning && !IsServer) SendSubscription(referenceId, Unsubscribe);
            foreach (var key in new List<(NetPeer, Guid)>(variableUpdates.Keys))
                if (key.Item2 == referenceId) ForgetVariable(key);
            foreach (var pair in new List<KeyValuePair<(NetPeer, uint), EventRequest>>(requests)) {
                if (pair.Value.ReferenceId != referenceId) continue;
                pair.Value.Response.TrySetException(new ObjectDisposedException($"Reference {referenceId}"));
                requests.Remove(pair.Key);
            }
        }

        /// <summary>Queue a complete event. LiteNetLib owns fragmentation and packet delivery.</summary>
        public uint SendEvent(NetPeer peer, Guid referenceId, ushort eventId,
            byte[] payload, uint replyTo = 0, bool replacePending = false) {
            return QueueEvent(peer, referenceId, eventId, payload, replyTo, replacePending: replacePending).Id;
        }

        public void Broadcast(Guid referenceId, ushort eventId, byte[] payload,
            NetPeer replyPeer = default, uint replyTo = 0, bool replacePending = false) {
            if (!IsServer) throw new InvalidOperationException("Only the server can broadcast to its peers.");
            QueueEvent(replyPeer, referenceId, eventId, payload, replyTo, broadcast: true, replacePending: replacePending);
        }

        /// <summary>Await a correlated reply. Cancellation stops waiting; it does not undo remote execution.</summary>
        public async Task<TransportEvent> RequestEventAsync(NetPeer peer, Guid referenceId,
            ushort eventId, byte[] payload, CancellationToken token = default, TimeSpan? timeout = null) {
            token.ThrowIfCancellationRequested();
            var duration = timeout ?? DefaultEventTimeout;
            ValidateTimeout(duration);
            var queued = QueueEvent(peer, referenceId, eventId, payload, 0);
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
                // Cancel an unsent request; a submitted message already belongs to LiteNetLib.
                queued.Canceled = true;
            }
        }

        private static void ValidateTimeout(TimeSpan duration) {
            if (duration.TotalMilliseconds < 1 || duration.TotalMilliseconds > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(duration));
        }

        // Validate and copy once at the shared entry point for sends, broadcasts, and requests.
        private OutgoingEvent QueueEvent(NetPeer peer, Guid referenceId, ushort eventId,
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

        // Only Tick expands broadcasts and coalesces this update's outgoing batch.
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
                        // Replacement/cancellation belongs to each recipient; payload bytes are shared.
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

        private void EnqueueEvent(NetPeer peer, OutgoingEvent message) {
            if (message.Replaceable) {
                var key = (peer, message.ReferenceId);
                if (variableUpdates.TryGetValue(key, out var first)) {
                    // Keep only the latest value while the first awaits delivery acknowledgment.
                    first.Pending = message;
                    return;
                } else variableUpdates.Add(key, message);
            }
            outgoing.Add((peer, message));
        }

        private void OnVariableDelivered(NetPeer peer, object userData) {
            if (userData is not OutgoingEvent delivered) return;
            var key = (peer, delivered.ReferenceId);
            if (!variableUpdates.TryGetValue(key, out var current) || current != delivered) return;

            variableUpdates.Remove(key);
            var pending = delivered.Pending;
            delivered.Pending = null;
            if (pending != null) EnqueueEvent(peer, pending);
        }

        private void ForgetVariable((NetPeer, Guid) key) {
            if (!variableUpdates.TryGetValue(key, out var message)) return;
            message.Canceled = true;
            message.Pending = null;
            variableUpdates.Remove(key);
        }

        private void UpdateEvents() {
            long started = Stopwatch.GetTimestamp();
            DrainEvents();
            foreach (var (peer, message) in outgoing) {
                if (message.Canceled || !peers.Contains(peer)) continue;
                try {
                    if (Stopwatch.GetTimestamp() >= message.Deadline)
                        throw new IOException($"Outgoing event {message.EventId}, message {message.Id} to {peer} expired before submission.");

                    messageWriter.Reset();
                    messageWriter.Put(message.Id);
                    messageWriter.Put(message.ReferenceId);
                    messageWriter.Put(message.EventId);
                    messageWriter.Put(message.ReplyTo);
                    messageWriter.Put(message.Bytes);
                    if (message.Replaceable)
                        peer.SendWithDeliveryEvent(messageWriter, DeliveryMethod.ReliableOrdered, message);
                    else peer.Send(messageWriter, DeliveryMethod.ReliableOrdered);
                    listeners.TryGetValue(message.ReferenceId, out var listener);
                    sendTiming.Count(listener?.GetType(), message.EventId, messageWriter.Length);
                } catch (IOException exception) {
                    UnityEngine.Debug.LogException(exception);
                    Disconnect(peer);
                    if (!IsRunning) return;
                }
            }
            outgoing.Clear();
            sendTiming.Record(started);
        }

        private void ReceiveMessage(NetPeer peer, NetDataReader reader) {
            if (reader.AvailableBytes < MessageHeaderBytes)
                throw new InvalidDataException("Incomplete event header.");
            uint messageId = reader.GetUInt();
            Guid referenceId = reader.GetGuid();
            ushort eventId = reader.GetUShort();
            uint replyTo = reader.GetUInt();
            if (messageId == 0 || eventId == 0)
                throw new InvalidDataException("Message and event ID zero are reserved.");

            DispatchEvent(new TransportEvent(peer, referenceId, eventId,
                messageId, replyTo, reader.GetRemainingBytes()));
        }

        private void DispatchEvent(TransportEvent message) {
            if (message.ReferenceId == Guid.Empty) {
                ReceiveSubscription(message);
                return;
            }
            var peer = message.Peer;
            requests.TryGetValue((peer, message.ReplyTo), out var request);
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

        private void ForgetPeer(NetPeer peer, Exception reason) {
            subscriptions.Remove(peer);
            foreach (var key in new List<(NetPeer, Guid)>(variableUpdates.Keys))
                if (key.Item1 == peer) ForgetVariable(key);
            foreach (var pair in new List<KeyValuePair<(NetPeer, uint), EventRequest>>(requests)) {
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
            requests.Clear(); outgoing.Clear(); variableUpdates.Clear(); subscriptions.Clear();
            sendTiming = default;
        }

        // Aggregate once per second; this measures queue draining, packing and submission,
        // not LiteNetLib's background retransmissions or network delivery latency.
        private struct SendTiming {
            private long bytes;
            private int messages;
            private long windowStart, totalTicks, peakTicks;
            private Dictionary<(Type Type, ushort Event), (long Bytes, int Messages)> composition;

            public void Count(Type type, ushort eventId, int length) {
                bytes += length;
                messages++;
                composition ??= new();
                var key = (type, eventId);
                composition.TryGetValue(key, out var count);
                composition[key] = (count.Bytes + length, count.Messages + 1);
            }

            public void Record(long started) {
                long now = Stopwatch.GetTimestamp();
                long elapsed = now - started;
                if (windowStart == 0) windowStart = started;
                totalTicks += elapsed;
                peakTicks = Math.Max(peakTicks, elapsed);
                if (now - windowStart < Stopwatch.Frequency) return;

                if (messages > 0) {
                    double milliseconds = 1000.0 / Stopwatch.Frequency;
                    var breakdown = new StringBuilder();
                    var entries = new List<KeyValuePair<(Type Type, ushort Event), (long Bytes, int Messages)>>(composition);
                    entries.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
                    foreach (var entry in entries) {
                        var count = entry.Value;
                        breakdown.Append($"\n  {TypeLabel(entry.Key.Type)} event {entry.Key.Event}: {count.Bytes / 1048576.0:F3} MiB ({100.0 * count.Bytes / bytes:F1}%), {count.Messages} messages, {count.Bytes / count.Messages} B/message");
                    }
                    UnityEngine.Debug.Log($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Outgoing work over {(now - windowStart) * milliseconds:F0} ms: {totalTicks * milliseconds:F2} ms total, {peakTicks * milliseconds:F2} ms peak/update; {messages} messages, {bytes / 1048576.0:F2} MiB submitted.{breakdown}");
                }
                composition?.Clear();
                this = new SendTiming { composition = composition };
            }

            private static string TypeLabel(Type type) {
                if (type == null) return "Control/unregistered";
                if (!type.IsGenericType) return type.Name;
                var arguments = Array.ConvertAll(type.GetGenericArguments(), TypeLabel);
                return type.Name.Split('`')[0] + "<" + string.Join(",", arguments) + ">";
            }
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
            public bool Canceled, Replaceable, Broadcast;
            public OutgoingEvent Pending;
            public long Deadline;
        }
    }
}
