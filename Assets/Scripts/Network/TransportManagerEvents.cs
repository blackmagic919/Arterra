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
using Arterra.Core.Events;

namespace Arterra.Core.Network {
    /// <summary>A complete application message delivered by LiteNetLib.</summary>
    public readonly struct TransportEvent {
        public readonly NetPeer Peer;
        public readonly Guid ReferenceId;
        // Only requests carry MessageId; only replies carry ReplyTo. Ordinary events use zero.
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
        // Default: deliver every event in reference order. Variables retain only the newest state.
        bool LatestOnly => false;
        // Temporary traffic diagnostics; absent for streams without gameplay events.
        Func<byte[], GameEvent?> GameEventTypeGetter => null;
        void OnEvent(TransportEvent message);
        void OnSubscribed(NetPeer peer) { }
    }

    public sealed partial class TransportManager {
        // Guid.Empty is reserved for transport subscription control.
        private const ushort Subscribe = 1, Unsubscribe = 2;
        private readonly Dictionary<NetPeer, ConnectionControl> connections = new();
        private const int ReferenceIdBytes = 16;
        private const int ControlHeaderBytes = sizeof(uint) + sizeof(ushort);
        private const int DataHeaderBytes = ControlHeaderBytes + sizeof(uint) + sizeof(byte);
        private enum MessageKind : byte { Event, Request, Reply }
        // Keep overflow here, where variable updates can still be replaced. A fragmented
        // message is submitted whole, so it may temporarily exceed this soft queue limit.
        private const int MaxQueuedPackets = 1024;
        public static readonly TimeSpan DefaultEventTimeout = TimeSpan.FromSeconds(60);
        private readonly Dictionary<Guid, ReferenceCommunication> references = new();
        private readonly Dictionary<(NetPeer, uint), EventRequest> requests = new();
        private ConcurrentQueue<(NetPeer peer, OutgoingEvent message)> pendingEvents = new();
        private int nextMessageId;
        private readonly NetDataWriter messageWriter = new();
        private SendTiming sendTiming;

        /// <summary>One listener per reference, regardless of the listener's value type.</summary>
        public void Register(Guid referenceId, ITransportEventListener listener) {
            if (referenceId == Guid.Empty) throw new ArgumentException("Reference ID zero is reserved for transport control.", nameof(referenceId));
            if (listener == null) throw new ArgumentNullException(nameof(listener));
            references.TryGetValue(referenceId, out var previous);
            if (previous?.Listener != null) throw new InvalidOperationException($"Reference {referenceId} is already registered.");
            references[referenceId] = new ReferenceCommunication(listener, previous?.Peers);
            if (!IsRunning) return;
            foreach (var peer in peers) {
                AnnounceReference(peer, referenceId);
                if (IsServer && IsSubscribed(peer, referenceId)) listener.OnSubscribed(peer);
            }
        }

        private void RegisterPeer(NetPeer peer) {
            connections.Add(peer, new ConnectionControl());
            foreach (var pair in references)
                if (pair.Value.Listener != null) AnnounceReference(peer, pair.Key);
        }

        private void AnnounceReference(NetPeer peer, Guid referenceId) {
            var connection = connections[peer];
            var reference = references[referenceId];
            var state = reference.GetPipeline(peer);
            if (state.RemoteStreamId != 0 && state.RemoteLatestOnly != reference.Listener.LatestOnly)
                throw new InvalidDataException("Reference ordering policies disagree.");
            if (state.ReceiveStreamId == 0) {
                // Never reuse a stream ID within a connection, even after unregistering.
                state.ReceiveStreamId = checked(++connection.NextStreamId);
                connection.Receivers.Add(state.ReceiveStreamId, referenceId);
            }
            SendSubscription(peer, referenceId, Subscribe, state.ReceiveStreamId, reference.Listener.LatestOnly, state.RemoteStreamId);
        }

        private void SendSubscription(NetPeer peer, Guid referenceId, ushort operation, uint receiveStreamId, bool latestOnly = false, uint acknowledgedStreamId = 0) {
            var writer = new NetDataWriter();
            writer.Put(referenceId);
            writer.Put(receiveStreamId);
            writer.Put(latestOnly);
            writer.Put(acknowledgedStreamId);
            SendEvent(peer, Guid.Empty, operation, writer.CopyData());
        }

        public bool IsSubscribed(NetPeer peer, Guid referenceId) =>
            references.TryGetValue(referenceId, out var reference)
            && reference.Peers.TryGetValue(peer, out var pipeline) && pipeline.RemoteStreamId != 0;

        private void ReceiveSubscription(TransportEvent message) {
            if (message.ReplyTo != 0 || message.Payload.Length != ReferenceIdBytes + 2 * sizeof(uint) + sizeof(byte)
                || (message.EventId != Subscribe && message.EventId != Unsubscribe))
                throw new InvalidDataException("Invalid subscription control message.");
            var reader = new NetDataReader(message.Payload);
            var referenceId = reader.GetGuid();
            uint receiveStreamId = reader.GetUInt();
            bool latestOnly = reader.GetBool();
            uint acknowledgedStreamId = reader.GetUInt();
            if (referenceId == Guid.Empty || receiveStreamId == 0)
                throw new InvalidDataException("Invalid reference registration.");

            references.TryGetValue(referenceId, out var reference);
            PeerPipeline state = null;
            reference?.Peers.TryGetValue(message.Peer, out state);
            if (message.EventId == Unsubscribe) {
                if (state == null || state.RemoteStreamId != receiveStreamId) return;
                if (state.ReceiveStreamId != 0 && acknowledgedStreamId != state.ReceiveStreamId) {
                    state.RemoteStreamId = 0;
                    state.RemoteReady = false;
                    return;
                }
                if (state.ReceiveStreamId != 0)
                    SendSubscription(message.Peer, referenceId, Unsubscribe, state.ReceiveStreamId, acknowledgedStreamId: state.RemoteStreamId);
                ForgetReference(message.Peer, referenceId);
                return;
            }

            // A reply to a registration already removed locally must not recreate its state.
            if (reference?.Listener == null && acknowledgedStreamId != 0) return;
            if (reference == null) references.Add(referenceId, reference = new ReferenceCommunication());
            state ??= reference.GetPipeline(message.Peer);
            bool ready = state.ReceiveStreamId != 0 && acknowledgedStreamId == state.ReceiveStreamId;
            if (state.RemoteStreamId == receiveStreamId) {
                state.RemoteReady |= ready;
                return;
            }
            // A new destination lifetime cannot inherit previously queued sends or sequence numbers.
            if (state.RemoteReady) {
                state.CancelOutgoing();
                CancelReferenceRequests(message.Peer, referenceId);
                state.NextSendSequence = 0;
            }
            state.RemoteStreamId = receiveStreamId;
            state.RemoteReady = ready;
            state.RemoteLatestOnly = latestOnly;
            if (reference.Listener == null) return;
            AnnounceReference(message.Peer, referenceId);
            if (IsServer) reference.Listener.OnSubscribed(message.Peer);
        }

        public void Unregister(Guid referenceId, ITransportEventListener listener) {
            if (!references.TryGetValue(referenceId, out var reference) || !ReferenceEquals(reference.Listener, listener)) return;
            foreach (var pair in new List<KeyValuePair<NetPeer, PeerPipeline>>(reference.Peers)) {
                var pipeline = pair.Value;
                if (IsRunning && pipeline.ReceiveStreamId != 0)
                    SendSubscription(pair.Key, referenceId, Unsubscribe, pipeline.ReceiveStreamId, acknowledgedStreamId: pipeline.RemoteStreamId);
                ForgetReference(pair.Key, referenceId);
            }
            references.Remove(referenceId);
        }

        private void CancelReferenceRequests(NetPeer peer, Guid referenceId) {
            foreach (var pair in new List<KeyValuePair<(NetPeer, uint), EventRequest>>(requests)) {
                if (pair.Key.Item1 != peer || pair.Value.ReferenceId != referenceId) continue;
                pair.Value.Response.TrySetException(new ObjectDisposedException($"Reference {referenceId}"));
                requests.Remove(pair.Key);
            }
        }

        private void ForgetReference(NetPeer peer, Guid referenceId) {
            var reference = references[referenceId];
            if (reference.Peers.Remove(peer, out var pipeline)) {
                connections[peer].Receivers.Remove(pipeline.ReceiveStreamId);
                pipeline.Clear();
            }
            CancelReferenceRequests(peer, referenceId);
            if (reference.Listener == null && reference.Peers.Count == 0) references.Remove(referenceId);
        }

        /// <summary>Queue a complete event. LiteNetLib owns fragmentation and packet delivery.</summary>
        public uint SendEvent(NetPeer peer, Guid referenceId, ushort eventId,
            byte[] payload, uint replyTo = 0, bool waitForDelivery = false, bool coalescePending = false) {
            return QueueEvent(peer, referenceId, eventId, payload, replyTo, waitForDelivery: waitForDelivery, coalescePending: coalescePending).Id;
        }

        public void Broadcast(Guid referenceId, ushort eventId, byte[] payload,
            NetPeer replyPeer = default, uint replyTo = 0, bool waitForDelivery = false, bool coalescePending = false) {
            if (!IsServer) throw new InvalidOperationException("Only the server can broadcast to its peers.");
            QueueEvent(replyPeer, referenceId, eventId, payload, replyTo, broadcast: true, waitForDelivery: waitForDelivery, coalescePending: coalescePending);
        }

        /// <summary>Await a correlated reply. Cancellation stops waiting; it does not undo remote execution.</summary>
        public async Task<TransportEvent> RequestEventAsync(NetPeer peer, Guid referenceId,
            ushort eventId, byte[] payload, CancellationToken token = default, TimeSpan? timeout = null) {
            token.ThrowIfCancellationRequested();
            var duration = timeout ?? DefaultEventTimeout;
            ValidateTimeout(duration);
            var queued = QueueEvent(peer, referenceId, eventId, payload, 0, isRequest: true);
            var request = new EventRequest(referenceId);
            var key = (peer, queued.Id);
            requests.Add(key, request);
            using var timer = new CancellationTokenSource();
            try {
                using var cancellation = token.Register(() => request.Response.TrySetCanceled());
                var delay = Task.Delay(duration, timer.Token);

                if (await Task.WhenAny(request.Response.Task, delay) != request.Response.Task)
                    throw new TimeoutException($"Event request for reference {referenceId} timed out.");
                return await request.Response.Task;
            } finally {
                timer.Cancel();
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
            byte[] payload, uint replyTo, bool broadcast = false, bool waitForDelivery = false, bool coalescePending = false, bool isRequest = false) {
            if ((waitForDelivery || coalescePending) && replyTo != 0)
                throw new ArgumentException("Correlated replies cannot be replaced.", nameof(waitForDelivery));
            var pending = Volatile.Read(ref pendingEvents);
            if (!IsRunning) throw new InvalidOperationException("Peer is not connected.");
            if (eventId == 0) throw new ArgumentOutOfRangeException(nameof(eventId), "Event ID zero is reserved.");
            if (payload == null) throw new ArgumentNullException(nameof(payload));
            uint id;
            do { id = unchecked((uint)Interlocked.Increment(ref nextMessageId)); } while (id == 0);
            references.TryGetValue(referenceId, out var sender);
            var message = new OutgoingEvent {
                ReferenceId = referenceId, EventId = eventId, Id = id, ReplyTo = replyTo, Sender = sender, IsRequest = isRequest,
                Bytes = (byte[])payload.Clone(), WaitForDelivery = waitForDelivery, Coalesce = waitForDelivery || coalescePending, Broadcast = broadcast,
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
                if (message.Canceled || !IsCurrentSender(message)) continue;
                if (message.Broadcast) {
                    if (!IsServer) continue;
                    foreach (var peer in peers) {
                        if (!IsSubscribed(peer, message.ReferenceId) && !(peer == pending.peer && message.ReplyTo != 0)) continue;
                        EnqueueEvent(peer, message.ForRecipient(peer == pending.peer));
                    }
                } else if (peers.Contains(pending.peer)) {
                    EnqueueEvent(pending.peer, message);
                }
            }
        }

        private bool IsCurrentSender(OutgoingEvent message) => message.ReferenceId == Guid.Empty
            || (references.TryGetValue(message.ReferenceId, out var reference)
                && reference.Listener != null && ReferenceEquals(reference, message.Sender));

        private void EnqueueEvent(NetPeer peer, OutgoingEvent message) {
            if (message.ReferenceId == Guid.Empty) {
                connections[peer].Control.Enqueue(message);
                return;
            }
            var reference = references[message.ReferenceId];
            if (!reference.Peers.TryGetValue(peer, out var pipeline)) {
                AnnounceReference(peer, message.ReferenceId);
                pipeline = reference.Peers[peer];
            }
            pipeline.Enqueue(message);
        }

        private void OnVariableDelivered(NetPeer peer, object userData) {
            if (userData is OutgoingEvent delivered) delivered.Pipeline?.Delivered(delivered);
        }

        private void UpdateEvents() {
            long started = Stopwatch.GetTimestamp();
            DrainEvents();
            NetPeer sendingPeer = null;
            try {
                foreach (var pair in connections) {
                    sendingPeer = pair.Key;
                    while (pair.Value.Control.TryPeek(out var message)) {
                        if (!SendMessage(sendingPeer, message)) break;
                        pair.Value.Control.Dequeue();
                    }
                }
                var registered = new List<ReferenceCommunication>(references.Values);
                foreach (var pair in connections) {
                    sendingPeer = pair.Key;
                    bool progressed;
                    do {
                        progressed = false;
                        int start = pair.Value.NextReference;
                        for (int i = 0; i < registered.Count; i++) {
                            int index = (start + i) % registered.Count;
                            if (!registered[index].Peers.TryGetValue(sendingPeer, out var pipeline)) continue;
                            if (!pipeline.TrySendNext(this, sendingPeer)) continue;

                            // Resume after the last successful submission when capacity returns.
                            pair.Value.NextReference = (index + 1) % registered.Count;
                            progressed = true;
                        }
                    } while (progressed && sendingPeer.GetPacketsCountInReliableQueue(false) < MaxQueuedPackets);
                }
            } catch (IOException exception) {
                UnityEngine.Debug.LogException(exception);
                Disconnect(sendingPeer);
            } finally {
                sendTiming.Record(started, peers);
            }
        }

        private bool SendMessage(NetPeer peer, OutgoingEvent message, PeerPipeline pipeline = null) {
            if (Stopwatch.GetTimestamp() >= message.Deadline)
                throw new IOException($"Outgoing event {message.EventId}, message {message.Id} to {peer} expired before submission.");
            if (pipeline != null && !pipeline.RemoteReady) return false;

            bool ordered = pipeline == null;
            var delivery = ordered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.ReliableUnordered;
            var kind = message.IsRequest ? MessageKind.Request : message.ReplyTo != 0 ? MessageKind.Reply : MessageKind.Event;
            if (peer.GetPacketsCountInReliableQueue(ordered) >= MaxQueuedPackets) return false;

            messageWriter.Reset();
            messageWriter.Put(pipeline?.RemoteStreamId ?? 0);
            if (pipeline != null) messageWriter.Put(pipeline.NextSendSequence);
            messageWriter.Put(message.EventId);
            if (pipeline != null) {
                messageWriter.Put((byte)kind);
                if (kind != MessageKind.Event)
                    messageWriter.Put(kind == MessageKind.Request ? message.Id : message.ReplyTo);
            }
            messageWriter.Put(message.Bytes);
            if (message.WaitForDelivery) peer.SendWithDeliveryEvent(messageWriter, delivery, message);
            else peer.Send(messageWriter, delivery);
            message.Submitted = true;
            if (pipeline != null) pipeline.NextSendSequence = unchecked(pipeline.NextSendSequence + 1);

            var listener = message.Sender?.Listener;
            var gameEvent = listener?.GameEventTypeGetter?.Invoke(message.Bytes);
            sendTiming.Count(listener?.GetType(), message.EventId, gameEvent, messageWriter.Length);
            return true;
        }

        private void ReceiveMessage(NetPeer peer, NetDataReader reader, DeliveryMethod delivery) {
            if (reader.AvailableBytes < ControlHeaderBytes)
                throw new InvalidDataException("Incomplete event header.");
            uint streamId = reader.GetUInt();
            if (streamId == 0) {
                if (delivery != DeliveryMethod.ReliableOrdered) throw new InvalidDataException("Control must be ordered.");
                ushort operation = reader.GetUShort();
                DispatchEvent(new TransportEvent(peer, Guid.Empty, operation, 0, 0, reader.GetRemainingBytes()));
                return;
            }
            if (delivery != DeliveryMethod.ReliableUnordered || reader.AvailableBytes < DataHeaderBytes - sizeof(uint))
                throw new InvalidDataException("Invalid reference data header.");
            uint sequence = reader.GetUInt();
            ushort eventId = reader.GetUShort();
            var kind = (MessageKind)reader.GetByte();
            if (eventId == 0 || kind > MessageKind.Reply)
                throw new InvalidDataException("Invalid event ID or message kind.");
            uint correlationId = 0;
            if (kind != MessageKind.Event) {
                if (reader.AvailableBytes < sizeof(uint) || (correlationId = reader.GetUInt()) == 0)
                    throw new InvalidDataException("Missing request/reply correlation ID.");
            }
            if (!connections.TryGetValue(peer, out var connection)
                || !connection.Receivers.TryGetValue(streamId, out var referenceId)
                || !references.TryGetValue(referenceId, out var reference) || reference.Listener == null
                || !reference.Peers.TryGetValue(peer, out var pipeline) || pipeline.ReceiveStreamId != streamId) return;

            bool isRequest = kind == MessageKind.Request;
            var message = new TransportEvent(peer, referenceId, eventId,
                isRequest ? correlationId : 0, kind == MessageKind.Reply ? correlationId : 0, reader.GetRemainingBytes());
            pipeline.Receive(this, reference.Listener.LatestOnly, sequence, message);
        }

        private void DispatchEvent(TransportEvent message, bool apply = true) {
            if (message.ReferenceId == Guid.Empty) {
                ReceiveSubscription(message);
                return;
            }
            var peer = message.Peer;
            requests.TryGetValue((peer, message.ReplyTo), out var request);
            // Ordinary writes/rejections have no correlation ID. Late replies to canceled
            // requests must not update a reference or revive the abandoned operation.
            if (message.ReplyTo != 0 && request == null) return;
            if (request != null && request.ReferenceId != message.ReferenceId)
                throw new InvalidDataException("Reply targets the wrong reference.");
            // Apply state before waking a caller awaiting this response.
            try {
                if (apply && references.TryGetValue(message.ReferenceId, out var listener)) listener.Listener?.OnEvent(message);
                request?.Response.TrySetResult(message);
            } catch (Exception exception) {
                request?.Response.TrySetException(exception);
                UnityEngine.Debug.LogException(exception);
            }
        }

        private void ForgetPeer(NetPeer peer, Exception reason) {
            connections.Remove(peer);
            foreach (var pair in new List<KeyValuePair<Guid, ReferenceCommunication>>(references)) {
                if (pair.Value.Peers.Remove(peer, out var pipeline)) pipeline.Clear();
                if (pair.Value.Listener == null && pair.Value.Peers.Count == 0) references.Remove(pair.Key);
            }
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
            requests.Clear();
            connections.Clear();
            foreach (var pair in new List<KeyValuePair<Guid, ReferenceCommunication>>(references)) {
                foreach (var pipeline in pair.Value.Peers.Values) pipeline.Clear();
                pair.Value.Peers.Clear();
                if (pair.Value.Listener == null) references.Remove(pair.Key);
            }
            sendTiming = default;
        }

        // Aggregate once per second; this measures queue draining, packing and submission,
        // not LiteNetLib's background retransmissions or network delivery latency.
        private struct SendTiming {
            private long bytes;
            private int messages;
            private long windowStart, totalTicks, peakTicks;
            private long lastPollStart, pollTicks, peakPollTicks, peakPollGap;
            private int polls;
            private Dictionary<NetPeer, (int Queue, long Sent, long Received, long BytesSent, long BytesReceived, long Loss)> peerSamples;
            private Dictionary<(Type Type, ushort Event, GameEvent? GameEvent), (long Bytes, int Messages)> composition;

            public void RecordPoll(long started) {
                long elapsed = Stopwatch.GetTimestamp() - started;
                if (lastPollStart != 0) peakPollGap = Math.Max(peakPollGap, started - lastPollStart);
                lastPollStart = started;
                pollTicks += elapsed;
                peakPollTicks = Math.Max(peakPollTicks, elapsed);
                polls++;
            }

            public void Count(Type type, ushort eventId, GameEvent? gameEvent, int length) {
                bytes += length;
                messages++;
                composition ??= new();
                var key = (type, eventId, gameEvent);
                composition.TryGetValue(key, out var count);
                composition[key] = (count.Bytes + length, count.Messages + 1);
            }

            public void Record(long started, List<NetPeer> peers) {
                long now = Stopwatch.GetTimestamp();
                long elapsed = now - started;
                if (windowStart == 0) windowStart = started;
                totalTicks += elapsed;
                peakTicks = Math.Max(peakTicks, elapsed);
                if (now - windowStart < Stopwatch.Frequency) return;

                if (messages > 0 || peers.Count > 0) {
                    double milliseconds = 1000.0 / Stopwatch.Frequency;
                    var breakdown = new StringBuilder();
                    breakdown.Append($"\n  PollEvents: {polls} calls, {pollTicks * milliseconds:F2} ms total, {peakPollTicks * milliseconds:F2} ms peak/call, {peakPollGap * milliseconds:F2} ms maximum start-to-start gap.");
                    peerSamples ??= new();
                    foreach (var peer in new List<NetPeer>(peerSamples.Keys))
                        if (!peers.Contains(peer)) peerSamples.Remove(peer);

                    foreach (var peer in peers) {
                        var stats = peer.Statistics;
                        var current = (Queue: peer.GetPacketsCountInReliableQueue(false),
                            Sent: stats.PacketsSent, Received: stats.PacketsReceived,
                            BytesSent: stats.BytesSent, BytesReceived: stats.BytesReceived, Loss: stats.PacketLoss);
                        bool sampled = peerSamples.TryGetValue(peer, out var previous);
                        string interval = sampled ? "since previous log" : "since connection (first sample)";
                        breakdown.Append($"\n  Peer {peer}: {current.Queue} packets waiting in LiteNetLib's ReliableUnordered queue (change {current.Queue - previous.Queue:+0;-0;0}); RTT {peer.RoundTripTime} ms.");
                        breakdown.Append($"\n    LiteNetLib {interval}: sent {current.Sent - previous.Sent} packets / {(current.BytesSent - previous.BytesSent) / 1048576.0:F3} MiB; received {current.Received - previous.Received} packets / {(current.BytesReceived - previous.BytesReceived) / 1048576.0:F3} MiB; loss counter +{current.Loss - previous.Loss}.");
                        peerSamples[peer] = current;
                    }

                    var entries = composition == null
                        ? new List<KeyValuePair<(Type Type, ushort Event, GameEvent? GameEvent), (long Bytes, int Messages)>>()
                        : new List<KeyValuePair<(Type Type, ushort Event, GameEvent? GameEvent), (long Bytes, int Messages)>>(composition);
                    entries.Sort((a, b) => b.Value.Bytes.CompareTo(a.Value.Bytes));
                    foreach (var entry in entries) {
                        var count = entry.Value;
                        string gameEvent = entry.Key.GameEvent.HasValue ? $" [{entry.Key.GameEvent.Value}]" : "";
                        breakdown.Append($"\n  {TypeLabel(entry.Key.Type)} event {entry.Key.Event}{gameEvent}: {count.Bytes / 1048576.0:F3} MiB ({100.0 * count.Bytes / bytes:F1}%), {count.Messages} messages, {count.Bytes / count.Messages} B/message");
                    }
                    UnityEngine.Debug.Log($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Outgoing work over {(now - windowStart) * milliseconds:F0} ms: {totalTicks * milliseconds:F2} ms total, {peakTicks * milliseconds:F2} ms peak/update; {messages} messages, {bytes / 1048576.0:F2} MiB submitted.{breakdown}");
                }
                composition?.Clear();
                this = new SendTiming { composition = composition, peerSamples = peerSamples, lastPollStart = lastPollStart };
            }

            private static string TypeLabel(Type type) {
                if (type == null) return "Control/unregistered";
                if (!type.IsGenericType) return type.Name;
                var arguments = Array.ConvertAll(type.GetGenericArguments(), TypeLabel);
                return type.Name.Split('`')[0] + "<" + string.Join(",", arguments) + ">";
            }
        }

        // The reference object also identifies a local registration lifetime for queued producers.
        private sealed class ReferenceCommunication {
            public readonly ITransportEventListener Listener;
            public readonly Dictionary<NetPeer, PeerPipeline> Peers;

            public ReferenceCommunication(ITransportEventListener listener = null, Dictionary<NetPeer, PeerPipeline> peers = null) {
                Listener = listener;
                Peers = peers ?? new();
            }

            public PeerPipeline GetPipeline(NetPeer peer) {
                if (!Peers.TryGetValue(peer, out var pipeline)) Peers.Add(peer, pipeline = new PeerPipeline());
                return pipeline;
            }
        }

        private sealed class ConnectionControl {
            public uint NextStreamId;
            public readonly Dictionary<uint, Guid> Receivers = new();
            public int NextReference;
            public readonly Queue<OutgoingEvent> Control = new();
        }

        private sealed class PeerPipeline {
            public uint ReceiveStreamId, RemoteStreamId;
            public uint NextSendSequence;
            private uint nextReceiveSequence;
            // The peer must confirm our current registration before queued data can leave.
            public bool RemoteLatestOnly, RemoteReady;
            private Dictionary<uint, TransportEvent> buffered;
            private readonly Queue<OutgoingEvent> outgoing = new();
            private OutgoingEvent currentVariable, pendingVariable;

            public void Enqueue(OutgoingEvent message) {
                message.Pipeline = this;
                if (message.Coalesce) {
                    if (currentVariable != null) {
                        if (message.WaitForDelivery && currentVariable.Submitted) {
                            pendingVariable = message;
                            return;
                        }
                        currentVariable.Canceled = true;
                    }
                    currentVariable = message;
                }
                outgoing.Enqueue(message);
            }

            public void Delivered(OutgoingEvent message) {
                if (currentVariable != message) return;
                currentVariable = null;
                var pending = pendingVariable;
                pendingVariable = null;
                if (pending != null) Enqueue(pending);
            }

            public bool TrySendNext(TransportManager transport, NetPeer peer) {
                while (outgoing.TryPeek(out var message)) {
                    if (message.Canceled || !transport.IsCurrentSender(message)) {
                        outgoing.Dequeue();
                        continue;
                    }
                    if (!transport.SendMessage(peer, message, this)) return false;
                    outgoing.Dequeue();
                    if (message.Coalesce && !message.WaitForDelivery && currentVariable == message)
                        currentVariable = null;
                    return true;
                }
                return false;
            }

            public void Receive(TransportManager transport, bool latestOnly, uint sequence, TransportEvent message) {
                bool fresh = unchecked((int)(sequence - nextReceiveSequence)) >= 0;
                if (latestOnly) {
                    bool isRequest = message.MessageId != 0;
                    if (fresh && !isRequest) nextReceiveSequence = unchecked(sequence + 1);
                    // Reads must be answered; stale replies resolve waiters without rolling back state.
                    transport.DispatchEvent(message, apply: fresh || isRequest);
                    return;
                }
                if (!fresh) return;
                buffered ??= new();
                buffered.TryAdd(sequence, message);
                while (buffered.Remove(nextReceiveSequence, out var next)) {
                    nextReceiveSequence = unchecked(nextReceiveSequence + 1);
                    transport.DispatchEvent(next);
                    if (!transport.references.TryGetValue(message.ReferenceId, out var reference)
                        || !reference.Peers.TryGetValue(message.Peer, out var current) || current != this) break;
                }
            }

            public void CancelOutgoing() {
                outgoing.Clear();
                if (currentVariable != null) currentVariable.Canceled = true;
                currentVariable = null;
                pendingVariable = null;
            }

            public void Clear() {
                CancelOutgoing();
                buffered?.Clear();
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
            public bool Canceled, Submitted, WaitForDelivery, Coalesce, Broadcast, IsRequest;
            public ReferenceCommunication Sender;
            public PeerPipeline Pipeline;
            public long Deadline;

            // Copy before enqueueing: each recipient owns its delivery state, but shares payload bytes.
            public OutgoingEvent ForRecipient(bool replyRecipient) {
                var copy = (OutgoingEvent)MemberwiseClone();
                if (!replyRecipient) copy.ReplyTo = 0;
                return copy;
            }
        }
    }
}
