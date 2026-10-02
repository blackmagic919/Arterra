using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.Networking.Transport;

namespace Arterra.Core.Network {
    /// <summary>Cached replicated state. Use on Unity's main thread; transport owns message delivery.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public sealed class NetworkVariable<T> : IDisposable, ITransportEventListener {
        private const ushort Read = 1, Write = 2, Update = 3, Rejected = 4;
        private readonly TransportManager transport = TransportManager.Instance;
        [JsonProperty("value")] private T value;
        private Task<T> pendingRead;
        private uint sessionVersion;
        private bool hasCachedValue, disposed;
        private readonly bool retainValueOnDisconnect;
        [JsonIgnore] public uint ReferenceId { get; }
        [JsonIgnore] public bool HasValue { get { CheckSession(); return NetworkManager.IsActingServer || hasCachedValue; } }
        [JsonIgnore] public bool IsActivelyReading { get => pendingRead != null && !pendingRead.IsCompleted; } 

        public NetworkVariable(uint referenceId, bool retainValueOnDisconnect = false) {
            ReferenceId = referenceId;
            this.retainValueOnDisconnect = retainValueOnDisconnect;
            sessionVersion = transport.SessionVersion;
            transport.Register(referenceId, this);
        }

        [JsonIgnore]
        public T Value {
            get {
                CheckSession();
                if (!HasValue) throw new InvalidOperationException("No cached value. Await GetAsync first.");
                return value;
            }
            set {
                CheckSession();
                if (NetworkManager.IsActingServer) Publish(value);
                else {
                    RequireConnection();
                    transport.SendEvent(transport.Server, ReferenceId, Write, Encode(value));
                }
            }
        }

        public void SetInitialValue(T initialValue) {
            CheckSession();
            if (transport.IsRunning || transport.Configuration != null)
                throw new InvalidOperationException("Initialize before starting the transport; use Value while hosting.");
            Apply(initialValue);
        }

        /// <summary>Publish nested mutations; on a client, submit them to the host. Offline edits stay local.</summary>
        public void Broadcast() => Value = Value; //Goes through setter

        /// <summary>Discard a retained value before connecting to a different authority.</summary>
        public void ClearCache() {
            CheckSession();
            if (transport.IsRunning || transport.Configuration != null)
                throw new InvalidOperationException("Clear the cache before starting the transport.");
            hasCachedValue = false; value = default; pendingRead = null;
        }

        public Task<T> GetAsync(CancellationToken token = default) {
            CheckSession();
            token.ThrowIfCancellationRequested();
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
            if (disposed) return;
            CheckSession();
            if (NetworkManager.IsActingServer) {
                try {
                    if (message.EventId == Read && message.Payload.Length == 0) {
                        transport.SendEvent(message.Peer, ReferenceId, Update, Encode(value), message.MessageId);
                    } else if (message.EventId == Write) Publish(Decode(message.Payload));
                    else throw new InvalidDataException("Invalid client variable event.");
                } catch (Exception exception) {
                    UnityEngine.Debug.LogException(exception);
                    transport.SendEvent(message.Peer, ReferenceId, Rejected, Array.Empty<byte>(), message.MessageId);
                }
            } else if (message.Peer == transport.Server) {
                if (message.EventId == Update) Apply(Decode(message.Payload));
                else if (message.EventId == Rejected)
                    UnityEngine.Debug.LogException(new InvalidOperationException("Server rejected the variable request."));
                else throw new InvalidDataException("Invalid server variable event.");
            }
        }

        private void RequireConnection() {
            if (!transport.IsRunning) throw new InvalidOperationException("Await the client connection before accessing uncached network state.");
        }

        private void Publish(T next) {
            if (!transport.IsRunning) { Apply(next); return; }
            byte[] payload = Encode(next);
            Apply(next);
            transport.Broadcast(ReferenceId, Update, payload);
        }

        // Updates and read replies share the transport's reliable, per-peer FIFO queue.
        private void Apply(T next) { value = next; hasCachedValue = !NetworkManager.IsActingServer; }
        private static byte[] Encode(T data) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
        private static T Decode(byte[] bytes) => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes));

        private void CheckSession() {
            if (disposed) throw new ObjectDisposedException(nameof(NetworkVariable<T>));
            if (sessionVersion == transport.SessionVersion) return;
            sessionVersion = transport.SessionVersion;
            pendingRead = null;
            hasCachedValue = false;
            if (!retainValueOnDisconnect) value = default;
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            transport.Unregister(ReferenceId, this);
            hasCachedValue = false; value = default; pendingRead = null;
        }
    }
}
