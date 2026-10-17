using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Core.Network.Service;
using LiteNetLib;
using UnityEngine;

namespace Arterra.Core.Network {
    /// <summary>Reliable game traffic on the same UDP socket used for NAT introduction.</summary>
    public sealed partial class TransportManager {
        public static TransportManager Instance { get; } = new();
        private NetManager driver;
        private readonly List<NetPeer> peers = new();
        private NetPeer server;
        private TaskCompletionSource<bool> connected;
        private int generation;

        public bool IsRunning { get; private set; }
        public bool IsStarted => driver != null;
        public bool IsServer { get; private set; }
        public uint SessionVersion { get; private set; }
        public NetPeer Server => server;
        public event Action<NetPeer> PeerDisconnected;
        public event Action<Exception> Stopped;
        public IReadOnlyList<NetPeer> Peers { get; }

        private TransportManager() => Peers = peers.AsReadOnly();

        internal async Task StartAsync(Guid sessionId, ConnectionTicket ticket, bool host, CancellationToken token) {
            if (IsStarted) throw new InvalidOperationException("Transport already started.");
            if (ticket.ExpiresAt <= DateTimeOffset.UtcNow) throw new InvalidOperationException("Connection ticket expired.");
            token.ThrowIfCancellationRequested();
            int epoch = ++generation;
            IsServer = host;
            if (!host) ++SessionVersion;
            connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var listener = new EventBasedNetListener();
            driver = new NetManager(listener) {
                IPv6Enabled = false, NatPunchEnabled = true, AutoRecycle = true,
                DisconnectTimeout = 10000
            };
            listener.PeerConnectedEvent += OnPeerConnected;
            listener.PeerDisconnectedEvent += OnPeerDisconnected;
            listener.DeliveryEvent += OnVariableDelivered;
            listener.ConnectionRequestEvent += OnConnectionRequest;
            listener.NetworkReceiveEvent += (peer, reader, channel, method) => {
                try {
                    if (peer == discoveryPeer) ReceiveDiscovery(reader.GetRemainingBytes());
                    else if (peers.Contains(peer)) ReceiveMessage(peer, reader, method);
                } catch (Exception exception) {
                    Debug.LogException(exception);
                    if (peer == discoveryPeer) Fail(exception);
                    else Disconnect(peer);
                }
            };
            listener.NetworkErrorEvent += (endpoint, error) =>
                Debug.LogWarning($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Transport] Socket error: {error}.");

            try {
                if (!driver.Start()) throw new IOException("Could not bind the game UDP socket.");
                Application.quitting += OnQuit;
                BeginDiscovery(sessionId, ticket);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(30));
                using var cancellation = timeout.Token.Register(() => connected.TrySetCanceled());
                await connected.Task;
                if (epoch != generation) throw new OperationCanceledException("Transport stopped while connecting.");
            } catch {
                if (epoch == generation) await StopAsync();
                throw;
            }
        }

        private void OnPeerConnected(NetPeer peer) {
            if (peer == discoveryPeer) { AuthenticateDiscovery(); return; }
            if (!TryGetUser(peer, out _)) { peer.Disconnect(); return; }
            if (!IsServer) {
                if (server != null && server != peer) { peer.Disconnect(); return; }
                server = peer;
                IsRunning = true;
            }
            peers.Add(peer);
            RegisterPeer(peer);
            if (IsServer) return;

            ReportIntroduction(true);
            connected.TrySetResult(true);
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info) {
            if (peer == discoveryPeer) {
                discoveryPeer = null;
                if (!IsServer && server != null) return; // Introduction finished; the game socket stays open.
                string detail = info.AdditionalData.AvailableBytes > 0
                    ? System.Text.Encoding.UTF8.GetString(info.AdditionalData.GetRemainingBytes()) : info.Reason.ToString();
                Fail(new IOException("Discovery connection ended: " + detail));
                return;
            }
            peer.Tag = null;
            if (!peers.Remove(peer)) return; // An unsuccessful NAT candidate isn't a game connection.
            if (!IsServer) { Fail(new IOException("Host disconnected.")); return; }
            ForgetPeer(peer, new IOException("Peer disconnected."));
            PeerDisconnected?.Invoke(peer);
        }

        public void Disconnect(NetPeer peer) {
            if (!IsStarted || !peers.Contains(peer)) return;
            if (!IsServer) { Fail(new IOException("Disconnected from host.")); return; }
            peer.Disconnect();
            peers.Remove(peer);
            peer.Tag = null;
            ForgetPeer(peer, new IOException("Peer disconnected."));
            PeerDisconnected?.Invoke(peer);
        }

        public Task StopAsync() => Stop(null);

        private Task Stop(Exception reason) {
            ++SessionVersion;
            ++generation;
            IsRunning = false;
            Application.quitting -= OnQuit;
            connected?.TrySetCanceled();
            driver?.Stop();
            driver = null;
            foreach (var peer in peers) peer.Tag = null;
            peers.Clear();
            server = null;
            IsServer = false;
            ResetDiscovery();
            ResetEvents(reason);
            Stopped?.Invoke(reason);
            return Task.CompletedTask;
        }

        internal void Update() {
            if (!IsStarted) return;
            try {
                driver.PollEvents();
                if (!IsStarted) return;
                driver.NatPunchModule.PollEvents();
                ExpireIntroductions();
                if (IsRunning) UpdateEvents();
            } catch (Exception exception) { Fail(exception); }
        }

        private void Fail(Exception exception) {
            connected?.TrySetException(exception);
            Debug.LogException(exception);
            _ = Stop(exception);
        }

        private void OnQuit() => _ = StopAsync();
    }
}
