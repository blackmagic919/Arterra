using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Runtime.Serialization;
using Unity.Networking.Transport;

namespace Arterra.Core.Network {
    public enum NetworkPermission { Server, All, Owner }

    /// <summary>Full-replacement state. Setters publish through the transport's outgoing queue.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class NetworkVariableBase<T> : IDisposable, ITransportEventListener {
        private const ushort Read = 1, Write = 2, Update = 3, Rejected = 4;
        private readonly TransportManager transport = TransportManager.Instance;
        [JsonProperty("value")] private T value;
        private Task<T> pendingRead;
        private uint sessionVersion;
        [JsonProperty] private uint writerVersion;
        // A default connection denotes a server-local writer. NetworkConnection includes its generation.
        private NetworkConnection lastWriter;
        [JsonIgnore] public uint WriterVersion { get { CheckSession(); return writerVersion; } }
        private bool hasCachedValue, disposed;
        [JsonProperty] private readonly bool retainValueOnDisconnect;
        [JsonProperty] private readonly NetworkPermission readPerm, writePerm;
        [JsonIgnore] public Guid ReferenceId { get; private set; }
        [JsonIgnore] public bool HasValue { get { CheckSession(); return NetworkManager.IsActingServer || (CanAccess(readPerm, default) && hasCachedValue); } }
        [JsonIgnore] public bool IsActivelyReading { get => pendingRead != null && !pendingRead.IsCompleted; } 

        public NetworkVariableBase(Guid referenceId, bool retainValueOnDisconnect = false,
            NetworkPermission writePerm = NetworkPermission.Server, NetworkPermission readPerm = NetworkPermission.All)
            : this(retainValueOnDisconnect, writePerm, readPerm) {
            RegisterReference(referenceId);
        }

        protected NetworkVariableBase(bool retainValueOnDisconnect,
            NetworkPermission writePerm, NetworkPermission readPerm) {
            if (writePerm < NetworkPermission.Server || writePerm > NetworkPermission.Owner)
                throw new ArgumentOutOfRangeException(nameof(writePerm));
            if (readPerm < NetworkPermission.Server || readPerm > NetworkPermission.Owner)
                throw new ArgumentOutOfRangeException(nameof(readPerm));
            this.writePerm = writePerm;
            this.readPerm = readPerm;
            this.retainValueOnDisconnect = retainValueOnDisconnect;
            sessionVersion = transport.SessionVersion;
        }

        protected void RegisterReference(Guid referenceId) {
            CheckSession();
            if (referenceId == Guid.Empty) throw new ArgumentException("A variable requires a nonempty reference ID.", nameof(referenceId));
            if (ReferenceId == referenceId) return;
            if (ReferenceId != Guid.Empty) throw new InvalidOperationException("This variable is already registered.");
            transport.Register(referenceId, this);
            ReferenceId = referenceId;
        }

        // An uncreated peer represents local access, not an incoming client event.
        protected virtual bool CanAccess(NetworkPermission permission, NetworkConnection peer) =>
            (NetworkManager.IsActingServer && !peer.IsCreated) || permission == NetworkPermission.All;

        [JsonIgnore]
        public T Value {
            get {
                CheckSession();
                if (!CanAccess(readPerm, default))
                    throw new UnauthorizedAccessException("Read permission denied for this variable.");
                if (!HasValue) throw new InvalidOperationException("No cached value. Await GetAsync first.");
                return value;
            }
            set {
                CheckSession();
                if (ReferenceId == Guid.Empty) { Apply(value, writerVersion); return; }
                if (NetworkManager.IsActingServer) Publish(value);
                else {
                    if (!CanAccess(writePerm, default))
                        throw new UnauthorizedAccessException("Write permission denied for this variable.");
                    RequireConnection();
                    if (!HasValue) throw new InvalidOperationException("Read the authoritative value/version before writing.");
                    transport.SendEvent(transport.Server, ReferenceId, Write, Encode(value));
                    Apply(value, writerVersion);
                }
            }
        }

        public void SetInitialValue(T initialValue) {
            CheckSession();
            if (ReferenceId != Guid.Empty && (transport.IsRunning || transport.Configuration != null))
                throw new InvalidOperationException("Initialize before starting the transport; use Value while hosting.");
            lastWriter = default;
            Apply(initialValue, 0);
        }

        [OnDeserialized]
        private void OnDeserialized(StreamingContext _) => hasCachedValue = true;

        /// <summary>Publish nested mutations; on a client, submit them to the host. Offline edits stay local.</summary>
        public void Broadcast() => Value = Value; //Goes through setter

        /// <summary>Discard a retained value before connecting to a different authority.</summary>
        public void ClearCache() {
            CheckSession();
            if (transport.IsRunning || transport.Configuration != null)
                throw new InvalidOperationException("Clear the cache before starting the transport.");
            hasCachedValue = false; value = default; pendingRead = null;
            writerVersion = 0; lastWriter = default;
        }

        public Task<T> GetAsync(CancellationToken token = default) {
            CheckSession();
            token.ThrowIfCancellationRequested();
            if (!CanAccess(readPerm, default))
                throw new UnauthorizedAccessException("Read permission denied for this variable.");
            if (HasValue) return Task.FromResult(value);
            RequireConnection();
            if (pendingRead == null || pendingRead.IsCompleted) pendingRead = Fetch();
            return token.CanBeCanceled ? AwaitRead(pendingRead, token) : pendingRead;
        }

        private async Task<T> Fetch() {
            var response = await transport.RequestEventAsync(transport.Server, ReferenceId, Read, Array.Empty<byte>());
            if (response.EventId != Update) throw new InvalidOperationException("Server could not supply this variable.");
            return Value;
        }

        // Cancellation affects this caller, not other callers sharing the same fetch.
        private static async Task<T> AwaitRead(Task<T> read, CancellationToken token) {
            var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (token.Register(() => canceled.TrySetResult(true))) {
                if (await Task.WhenAny(read, canceled.Task) != read) {
                    _ = read.ContinueWith(t => { _ = t.Exception; }, CancellationToken.None,
                        TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    token.ThrowIfCancellationRequested();
                }
                return await read;
            }
        }

        void ITransportEventListener.OnEvent(TransportEvent message) {
            if (disposed || !message.Peer.IsCreated || message.ReferenceId != ReferenceId) return;
            CheckSession();
            if (NetworkManager.IsActingServer) {
                if (message.EventId == Read) {
                    if (message.Payload.Length != 0 || !CanAccess(readPerm, message.Peer)) {
                        Reject(message); return;
                    }
                    transport.SendEvent(message.Peer, ReferenceId, Update, Encode(value), message.MessageId);
                    return;
                }
                if (message.EventId != Write || !CanAccess(writePerm, message.Peer)) {
                    Reject(message); return;
                }
                Snapshot incoming;
                try { incoming = Decode(message.Payload); }
                catch (Exception exception) {
                    UnityEngine.Debug.LogException(exception);
                    Reject(message); return;
                }
                if (incoming.Version != writerVersion) { Reject(message); return; }
                // Accept and publish as the submitting client, not as the server processing it.
                Publish(incoming.Value, message.Peer);
            } else if (message.Peer == transport.Server) {
                if (message.EventId != Update && message.EventId != Rejected)
                    throw new InvalidDataException("Invalid server variable event.");
                if (message.Payload.Length != 0 && CanAccess(readPerm, default)) {
                    Snapshot incoming = Decode(message.Payload);
                    Apply(incoming.Value, incoming.Version);
                }
            }
        }

        private void Reject(TransportEvent message) => transport.SendEvent(message.Peer, ReferenceId,
            Rejected, CanAccess(readPerm, message.Peer) ? Encode(value) : Array.Empty<byte>(), message.MessageId);

        private void RequireConnection() {
            if (ReferenceId == Guid.Empty) throw new InvalidOperationException("Register this variable before using the network.");
            if (!transport.IsRunning) throw new InvalidOperationException("Await the client connection before accessing uncached network state.");
        }

        private void Publish(T next, NetworkConnection writer = default) {
            if (transport.IsRunning) RequireConnection();
            // Server-local writes bypass expected-version validation. Accepted remote writes reach
            // here only after validation; ordinary writes by the same writer can remain in flight.
            if (lastWriter != writer) writerVersion = unchecked(writerVersion + 1);
            lastWriter = writer;
            value = next;
            hasCachedValue = false;
            if (transport.IsRunning && readPerm != NetworkPermission.Server) {
                byte[] payload = Encode(next);
                if (readPerm == NetworkPermission.All) transport.Broadcast(ReferenceId, Update, payload);
                else foreach (var peer in transport.Peers) {
                    if (CanAccess(readPerm, peer)) transport.SendEvent(peer, ReferenceId, Update, payload);
                }
            }
        }

        // Complete snapshots and replies share the reliable per-peer FIFO transport. No client sequence.
        private void Apply(T next, uint version) {
            value = next;
            writerVersion = version;
            hasCachedValue = !NetworkManager.IsActingServer;
        }
        private byte[] Encode(T data) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(
            new Snapshot { Version = writerVersion, Value = data }));
        private static Snapshot Decode(byte[] bytes) =>
            JsonConvert.DeserializeObject<Snapshot>(Encoding.UTF8.GetString(bytes))
            ?? throw new InvalidDataException("Missing variable snapshot.");

        [JsonObject(MemberSerialization.OptIn)]
        private sealed class Snapshot {
            [JsonProperty] public uint Version;
            [JsonProperty] public T Value;
        }

        private void CheckSession() {
            if (disposed) throw new ObjectDisposedException(nameof(NetworkVariableBase<T>));
            if (sessionVersion == transport.SessionVersion) return;
            sessionVersion = transport.SessionVersion;
            pendingRead = null;
            writerVersion = 0; lastWriter = default;
            hasCachedValue = false;
            if (!retainValueOnDisconnect) value = default;
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            if (ReferenceId != Guid.Empty) transport.Unregister(ReferenceId, this);
            // Released entities can still be serialized by chunk/account storage.
            pendingRead = null;
            hasCachedValue = false;
        }
    }
}
