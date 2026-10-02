using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Arterra.Utils;
using Unity.Collections;
using Unity.Networking.Transport;
using Unity.Networking.Transport.Relay;
using Unity.Networking.Transport.Utilities;
using Unity.Services.Multiplayer;
using UnityEngine;
using TransportSettings = Unity.Networking.Transport.NetworkSettings;

namespace Arterra.Core.Network {
    /// <summary>
    /// Session connections and reliable packet transport, independent of application protocols.
    /// All operations and callbacks run on Unity's main thread.
    /// </summary>
    public sealed partial class TransportManager : INetworkHandler {
        public static TransportManager Instance { get; } = new();
        private const int ReliableWindowSize = 64;
        private const int ConnectionTimeoutMilliseconds = 30000;
        //maximum total connections. Adjust this in the future, temporary limit
        private const int MaxPeers = 128;
        private NetworkDriver driver;
        private NetworkPipeline reliable;
        private readonly List<NetworkConnection> peers = new();
        private NetworkConnection server;
        private RuntimeUpdateTask pump;
        private TaskCompletionSource<bool> connected;
        private bool starting;
        private int generation;
        public bool IsRunning { get; private set; }
        public uint SessionVersion { get; private set; }
        public NetworkConfiguration Configuration { get; private set; }
        public bool IsServer => Configuration != null && Configuration.Role != NetworkRole.Client;

        private readonly IReadOnlyList<NetworkConnection> peerView;
        private TransportManager() => peerView = peers.AsReadOnly();

        public NetworkConnection Server => server;
        public event Action<NetworkConnection> PeerDisconnected;
        public event Action<Exception> Stopped;
        public IReadOnlyList<NetworkConnection> Peers => peerView;

        // Write directly into Unity's send buffer; no temporary packet arrays or streams.
        private bool TrySendChunk(NetworkConnection peer, OutgoingEvent message, int count) {
            if (!driver.IsCreated || !peers.Contains(peer)) return false;
            bool first = message.Sent == 0;
            int headerBytes = first ? FirstHeaderBytes : MessageIdBytes;
            if (driver.BeginSend(reliable, peer, out var writer, headerBytes + count) != 0) return false;
            writer.WriteUInt(message.Id);
            if (first) {
                writer.WriteUInt(message.ReferenceId);
                writer.WriteUShort(message.EventId);
                writer.WriteUInt(message.ReplyTo);
                writer.WriteInt(message.Bytes.Length);
            }
            for (int i = 0; i < count; i++) writer.WriteByte(message.Bytes[message.Sent + i]);
            return driver.EndSend(writer) >= 0;
        }

        public void Disconnect(NetworkConnection peer) {
            if (!driver.IsCreated || !peers.Contains(peer)) return;
            if (!IsServer) { Fail(new IOException("Disconnected from host.")); return; }
            driver.Disconnect(peer);
            peers.Remove(peer);
            ForgetPeer(peer, new IOException("Peer disconnected."));
            PeerDisconnected?.Invoke(peer);
        }

        public async Task StartAsync(NetworkConfiguration configuration) {
            if (configuration == null) throw new System.ArgumentNullException(nameof(configuration));
            if (starting || driver.IsCreated) throw new InvalidOperationException("Transport already started.");
            if (configuration.Type == NetworkType.DistributedAuthority) throw new NotSupportedException("Arterra uses a host-authoritative connection.");

            starting = true;
            int epoch = ++generation;
            Configuration = configuration;
            // A new client authority must not reuse values from a local game or an old host.
            if (!IsServer) ++SessionVersion;
            connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            try {
                var settings = new TransportSettings(Allocator.Temp);
                try { //Create driver to communicate with websocket
                    settings.WithReliableStageParameters(windowSize: ReliableWindowSize);
                    if (configuration.Type == NetworkType.Relay) {
                        // MPS supplies host and joining-client allocations here; RelayClientData is Entities-specific.
                        var relay = configuration.RelayServerData;
                        settings.WithRelayParameters(ref relay);
                        driver = relay.IsWebSocket != 0
                            ? NetworkDriver.Create(new WebSocketNetworkInterface(), settings)
                            : NetworkDriver.Create(settings);
                    } else driver = NetworkDriver.Create(settings);
                } finally { settings.Dispose(); }
                reliable = driver.CreatePipeline(typeof(ReliableSequencedPipelineStage));
                var bind = IsServer && configuration.Type == NetworkType.Direct
                    ? configuration.DirectNetworkListenAddress : NetworkEndpoint.AnyIpv4;
                if (driver.Bind(bind) != 0) throw new IOException("Could not bind transport socket.");
                if (IsServer) {
                    if (driver.Listen() != 0) throw new IOException("Could not listen for peers.");
                    if (configuration.Type == NetworkType.Direct) {
                        configuration.UpdatePublishPort(driver.GetLocalEndpoint().Port);
                        connected.TrySetResult(true);
                    }
                } else {
                    var endpoint = configuration.Type == NetworkType.Direct
                        ? configuration.DirectNetworkPublishAddress : configuration.RelayServerData.Endpoint;
                    server = driver.Connect(endpoint);
                    if (!server.IsCreated) throw new IOException("Could not initiate server connection.");
                    peers.Add(server);
                }
                Application.quitting += OnQuit;
                pump = new RuntimeUpdateTask(Tick, Fail);
                pump.Invoke();
                await WithTimeout(connected.Task, ConnectionTimeoutMilliseconds);
                if (epoch != generation) throw new OperationCanceledException("Transport stopped while connecting.");
                IsRunning = true;
            } catch {
                if (epoch == generation) await StopAsync();
                throw;
            } finally { if (epoch == generation) starting = false; }
        }

        public Task StopAsync() => Stop(null);

        private Task Stop(Exception reason) {
            ++SessionVersion;
            ++generation;
            starting = false;
            IsRunning = false;
            Application.quitting -= OnQuit;
            pump?.Disable();
            pump = null;
            connected?.TrySetCanceled();
            if (driver.IsCreated) {
                foreach (var peer in peers) driver.Disconnect(peer);
                driver.ScheduleUpdate().Complete();
                driver.Dispose();
            }
            driver = default;
            peers.Clear();
            server = default;
            Configuration = null;
            ResetEvents(reason);
            Stopped?.Invoke(reason);
            return Task.CompletedTask;
        }

        private static async Task<T> WithTimeout<T>(Task<T> task, int milliseconds) {
            using (var timer = new CancellationTokenSource()) {
                var delay = Task.Delay(milliseconds, timer.Token);
                if (await Task.WhenAny(task, delay) != task) throw new TimeoutException("Network operation timed out.");
                timer.Cancel();
                return await task;
            }
        }

        private void Tick() {
            if (!driver.IsCreated) return;
            driver.ScheduleUpdate().Complete();
            if (Configuration.Type == NetworkType.Relay) {
                var status = driver.GetRelayConnectionStatus();
                if (status == RelayConnectionStatus.AllocationInvalid)
                    throw new IOException("Relay allocation expired or is invalid.");
                if (IsServer && status == RelayConnectionStatus.Established) connected.TrySetResult(true);
            }
            if (IsServer) {
                NetworkConnection peer;
                while ((peer = driver.Accept()).IsCreated) {
                    if (peers.Count >= MaxPeers) driver.Disconnect(peer);
                    else peers.Add(peer);
                }
            }
            // Protocol callbacks may disconnect peers while processing a packet.
            peerSnapshot.Clear();
            peerSnapshot.AddRange(peers);
            foreach (var peer in peerSnapshot) {
                if (!peers.Contains(peer)) continue;
                NetworkEvent.Type type;
                while ((type = driver.PopEventForConnection(peer, out var reader)) != NetworkEvent.Type.Empty) {
                    if (type == NetworkEvent.Type.Connect && !IsServer) connected.TrySetResult(true);
                    else if (type == NetworkEvent.Type.Disconnect) {
                        peers.Remove(peer);
                        if (!IsServer) { Fail(new IOException("Host disconnected.")); return; }
                        ForgetPeer(peer, new IOException("Peer disconnected."));
                        PeerDisconnected?.Invoke(peer);
                        if (!driver.IsCreated) return;
                        break;
                    } else if (type == NetworkEvent.Type.Data) {
                        try { ReceivePacket(peer, reader); }
                        catch (InvalidDataException exception) {
                            Debug.LogException(exception);
                            Disconnect(peer);
                        }
                        if (!driver.IsCreated) return;
                        if (!peers.Contains(peer)) break;
                    }
                }
            }
            UpdateEvents();
        }

        private void Fail(Exception exception) {
            connected?.TrySetException(exception);
            Debug.LogException(exception);
            _ = Stop(exception);
        }
        private void OnQuit() => _ = StopAsync();
    }
}
