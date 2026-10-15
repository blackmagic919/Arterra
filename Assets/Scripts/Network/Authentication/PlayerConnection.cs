using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.GamePlay;
using LiteNetLib;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;
using Arterra.Core.Storage;

namespace Arterra.Core.Network.Authentication {
    /// <summary>Creates the owning player after discovery has authenticated its connection. Main-thread only.</summary>
    public sealed class PlayerConnection : ITransportEventListener {
        private static readonly Guid Reference = new("2ff008b2-eccf-46b8-b02b-7d43f266177b");
        private const ushort Begin = 1, Accepted = 4, Rejected = 5, Ready = 6;

        private static readonly PlayerConnection instance = new();
        private readonly Dictionary<NetPeer, AccountId> accounts = new();
        private readonly Dictionary<NetPeer, NetworkSource> sources = new();

        private bool initialized;
        private static string initialPlayerJson;

        public static bool TryGetAccount(NetPeer peer, out AccountId account) => instance.accounts.TryGetValue(peer, out account);

        public static void Initialize() {
            if (instance.initialized) return;

            var transport = TransportManager.Instance;
            transport.Register(Reference, instance);
            transport.PeerDisconnected += instance.Disconnected;
            transport.Stopped += instance.Stopped;
            instance.initialized = true;
        }

        public static async Task JoinAsync(CancellationToken token) {
            var transport = TransportManager.Instance;
            var response = await transport.RequestEventAsync(transport.Server, Reference, Begin, Array.Empty<byte>(), token);
            if (response.EventId != Accepted) throw new InvalidOperationException("Host rejected player setup.");
            initialPlayerJson = Encoding.UTF8.GetString(response.Payload);
            Debug.Log($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Owning player received ({response.Payload.Length} bytes).");
        }

        internal static BehaviorEntity.Animal TakeInitialPlayer() {
            if (string.IsNullOrEmpty(initialPlayerJson))
                throw new InvalidOperationException("Authenticate and receive the owning player before opening the client world.");

            var player = JsonConvert.DeserializeObject<Registerable<BehaviorEntity.Animal>>(initialPlayerJson).Value;
            initialPlayerJson = null;
            return player;
        }

        internal static void AcknowledgeReady(Guid playerId) => TransportManager.Instance.SendEvent(
            TransportManager.Instance.Server, Reference, Ready, playerId.ToByteArray());

        public void OnEvent(TransportEvent message) {
            if (!TransportManager.Instance.IsServer) return;

            Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: received event {message.EventId} from peer {message.Peer}, request {message.MessageId}.");
            if (message.EventId == Begin) SpawnAsync(message);
            else if (message.EventId == Ready) {
                if (message.Payload.Length != 16 || !accounts.ContainsKey(message.Peer)
                    || PlayerHandler.GetPlayerId(message.Peer) != new Guid(message.Payload)) Reject(message);
            }
            else Reject(message);
        }

        private async void SpawnAsync(TransportEvent message) {
            var transport = TransportManager.Instance;
            uint session = transport.SessionVersion;
            if (!transport.TryGetUser(message.Peer, out var userId) || accounts.ContainsKey(message.Peer)) {
                Reject(message);
                return;
            }

            var account = new AccountId("Arterra", NetworkManager.Service.BaseUrl + "/" + userId.ToString("D"));
            if (accounts.Values.Contains(account) || userId == NetworkManager.Service.UserId) {
                Reject(message);
                return;
            }
            accounts.Add(message.Peer, account);

            try {
                var spawned = new TaskCompletionSource<BehaviorEntity.Animal>(TaskCreationOptions.RunContinuationsAsynchronously);
                EntityManager.AddHandlerEvent(async () => {
                    try {
                        if (!Current()) throw new OperationCanceledException("Peer left during player setup.");
                        Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: deferred spawn handler running for peer {message.Peer}.");
                        spawned.SetResult(await PlayerHandler.SpawnPlayerAsync(message.Peer));
                    } catch (Exception exception) {
                        spawned.SetException(exception);
                    }
                });

                var runtimePlayer = await spawned.Task;
                if (!Current()) throw new OperationCanceledException("Peer left during player setup.");

                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: player spawn complete for peer {message.Peer}; creating network source.");
                sources.Add(message.Peer, new NetworkSource(message.Peer, runtimePlayer.info.rtEntityId));
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: network source created for peer {message.Peer}; serializing player.");

                byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Registerable<BehaviorEntity.Animal>(runtimePlayer)));
                TransportManager.Instance.SendEvent(message.Peer, Reference, Accepted, payload, message.MessageId);
                Debug.Log($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: acceptance queued for peer {message.Peer} ({payload.Length} bytes).");

            } catch (Exception exception) {
                Debug.LogException(exception);
                if (Current()) {
                    Reject(message);
                    transport.Disconnect(message.Peer);
                }
            }

            bool Current() => transport.IsRunning && transport.SessionVersion == session
                && accounts.ContainsKey(message.Peer);
        }

        private static void Reject(TransportEvent message) {
            Debug.LogWarning($"[{System.DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UTC] [Network Auth] Host: rejecting event {message.EventId} from peer {message.Peer}, request {message.MessageId}.");
            TransportManager.Instance.SendEvent(message.Peer, Reference, Rejected, Array.Empty<byte>(), message.MessageId);
        }

        private void Disconnected(NetPeer peer) {
            if (sources.Remove(peer, out var source)) source.Dispose();

            if (!accounts.Remove(peer, out var account)) return;

            PlayerHandler.RemovePeer(peer, account);
        }

        public static void DisconnectPlayers() {
            foreach (var peer in instance.accounts.Keys.ToArray())
                TransportManager.Instance.Disconnect(peer);
        }

        private void Stopped(Exception reason) {
            if (ChunkGenerationDriver.Local?.Source is ClientSource source) source.Dispose();
            initialPlayerJson = null;

            foreach (var peer in accounts.Keys.ToArray()) {
                try { Disconnected(peer); }
                catch (Exception exception) { Debug.LogException(exception); }
            }

        }

    }
}
