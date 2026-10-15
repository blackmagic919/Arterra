using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using LiteNetLib;

namespace Arterra.Core.Network {
    /// <summary>Typed transient events. Use on Unity's main thread; events are neither cached nor replayed.</summary>
    [JsonObject(MemberSerialization.OptIn)]
    public class NetEventStreamBase<T> : IDisposable, ITransportEventListener {
        private const ushort Write = 1, Delivery = 2, Rejected = 3;
        private readonly TransportManager transport = TransportManager.Instance;
        private readonly NetworkPermission readPerm, writePerm;
        private bool disposed;
        [JsonIgnore] public Guid ReferenceId { get; private set; }
        public event Action<T> RecieveEvent;

        public NetEventStreamBase(Guid referenceId, NetworkPermission writePerm = NetworkPermission.Server,
            NetworkPermission readPerm = NetworkPermission.All, Action<T> recieveEvent = null)
            : this(writePerm, readPerm, recieveEvent) {
            RegisterReference(referenceId);
        }

        protected NetEventStreamBase(NetworkPermission writePerm, NetworkPermission readPerm, Action<T> recieveEvent) {
            if (writePerm < NetworkPermission.Server || writePerm > NetworkPermission.Owner)
                throw new ArgumentOutOfRangeException(nameof(writePerm));
            if (readPerm < NetworkPermission.Server || readPerm > NetworkPermission.Owner)
                throw new ArgumentOutOfRangeException(nameof(readPerm));
            this.writePerm = writePerm;
            this.readPerm = readPerm;
            RecieveEvent = recieveEvent;
        }

        protected void RegisterReference(Guid referenceId) {
            CheckDisposed();
            if (referenceId == Guid.Empty) throw new ArgumentException("A stream requires a nonempty reference ID.", nameof(referenceId));
            if (ReferenceId == referenceId) return;
            if (ReferenceId != Guid.Empty) throw new InvalidOperationException("This stream is already registered.");
            transport.Register(referenceId, this);
            ReferenceId = referenceId;
        }

        // A null peer represents local access, not an incoming client event.
        protected virtual bool CanAccess(NetworkPermission permission, NetPeer peer) =>
            (NetworkManager.IsActingServer && peer == null) || permission == NetworkPermission.All;

        // Specializations can validate application identities and route both local and relayed events.
        protected virtual bool ValidateRemoteWrite(T data, NetPeer peer) => true;
        protected virtual IEnumerable<NetPeer> GetRecipients(T data) => null;

        /// <summary>Submit an event to the server. On the server, null recipients broadcasts;
        /// an explicit recipient list limits remote delivery without changing local delivery.</summary>
        public void Fire(T data, Action<T> localOverride = null, IEnumerable<NetPeer> recipients = null) {
            CheckDisposed();
            if (!CanAccess(writePerm, default)) throw new UnauthorizedAccessException("Write permission denied for this stream.");
            if (NetworkManager.IsActingServer) Publish(data, localOverride, recipients);
            else {
                if (recipients != null)
                    throw new InvalidOperationException("Recipient connections are server-local; clients must route through the server.");
                RequireConnection();
                transport.SendEvent(transport.Server, ReferenceId, Write, Encode(data));
            }
        }

        void ITransportEventListener.OnEvent(TransportEvent message) {
            if (disposed || message.Peer == null || message.ReferenceId != ReferenceId) return;
            if (NetworkManager.IsActingServer) {
                if (message.EventId != Write || !CanAccess(writePerm, message.Peer)) {
                    transport.SendEvent(message.Peer, ReferenceId, Rejected, Array.Empty<byte>(), message.MessageId);
                    return;
                }
                T data;
                try { data = Decode(message.Payload); }
                catch (Exception exception) {
                    UnityEngine.Debug.LogException(exception);
                    transport.SendEvent(message.Peer, ReferenceId, Rejected, Array.Empty<byte>(), message.MessageId);
                    return;
                }
                if (!ValidateRemoteWrite(data, message.Peer)) {
                    transport.SendEvent(message.Peer, ReferenceId, Rejected, Array.Empty<byte>(), message.MessageId);
                    return;
                }
                // Callback failures must not turn an accepted, already forwarded event into a rejection.
                Publish(data);
            } else if (message.Peer == transport.Server) {
                if (message.EventId == Delivery) {
                    if (CanAccess(readPerm, default)) RecieveEvent?.Invoke(Decode(message.Payload));
                } else if (message.EventId == Rejected)
                    UnityEngine.Debug.LogException(new InvalidOperationException("Server rejected the stream event."));
                else throw new InvalidDataException("Invalid server stream event.");
            }
        }

        private void Publish(T data, Action<T> localOverride = null, IEnumerable<NetPeer> recipients = null) {
            if (transport.IsRunning) {
                RequireConnection();
                recipients ??= GetRecipients(data);
                if (readPerm == NetworkPermission.All && recipients == null) 
                    transport.Broadcast(ReferenceId, Delivery, Encode(data));
                else if (readPerm != NetworkPermission.Server) {
                    byte[] payload = null;
                    foreach (var peer in recipients ?? transport.Peers) {
                        if (peer != null && CanAccess(readPerm, peer))
                            transport.SendEvent(peer, ReferenceId, Delivery, payload ??= Encode(data));
                    }
                }
            }
            // Forward first so local handlers cannot mutate the outgoing event or reorder nested Fire calls.
            if (CanAccess(readPerm, default)) 
                (localOverride ?? RecieveEvent)?.Invoke(data);
        }

        private void RequireConnection() {
            if (ReferenceId == Guid.Empty) throw new InvalidOperationException("Register this stream before using the network.");
            if (!transport.IsRunning) throw new InvalidOperationException("Await the client connection before firing network events.");
        }

        private static byte[] Encode(T data) => Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(data));
        private static T Decode(byte[] bytes) => JsonConvert.DeserializeObject<T>(Encoding.UTF8.GetString(bytes));
        private void CheckDisposed() {
            if (disposed) throw new ObjectDisposedException(nameof(NetEventStreamBase<T>));
        }

        public void Dispose() {
            if (disposed) return;
            disposed = true;
            if (ReferenceId != Guid.Empty) transport.Unregister(ReferenceId, this);
            RecieveEvent = null;
        }
    }
}
