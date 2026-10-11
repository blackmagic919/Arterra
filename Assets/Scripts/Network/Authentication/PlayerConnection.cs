using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Arterra.GamePlay;
using Unity.Networking.Transport;
using Unity.Services.Multiplayer;
using UnityEngine;
using Arterra.Configuration;
using Arterra.Data.Entity.Behavior;
using Newtonsoft.Json;

namespace Arterra.Core.Network.Authentication {
    /// <summary>Prototype Unity-session proof binding a connection to an account. Main-thread only.</summary>
    public sealed class PlayerConnection : ITransportEventListener {
        private static readonly Guid Reference = new("2ff008b2-eccf-46b8-b02b-7d43f266177b");
        private const ushort Begin = 1, Challenge = 2, Prove = 3, Accepted = 4, Rejected = 5, Ready = 6;
        private const int ChallengeBytes = 32;
        private const int ProofLifetimeSeconds = 60;
        private static readonly PlayerConnection instance = new();
        private readonly Dictionary<NetworkConnection, Pending> pending = new();
        private readonly Dictionary<NetworkConnection, AccountId> accounts = new();
        private bool initialized;
        private static string initialPlayerJson;
        public static bool TryGetAccount(NetworkConnection peer, out AccountId account) => instance.accounts.TryGetValue(peer, out account);

        public static void Initialize() {
            if (instance.initialized) return;
            var transport = TransportManager.Instance;
            transport.Register(Reference, instance);
            transport.PeerDisconnected += instance.Disconnected;
            transport.Stopped += instance.Stopped;
            instance.initialized = true;
        }

        public static async Task JoinAsync(ISession session, CancellationToken token) {
            var transport = TransportManager.Instance;
            var peer = transport.Server;
            var response = await transport.RequestEventAsync(peer, Reference, Begin, Array.Empty<byte>(), token);
            if (response.EventId != Challenge) throw new InvalidOperationException("Host refused authentication.");
            await ClientAuthentication.PublishProofAsync(session, Encoding.UTF8.GetString(response.Payload));
            token.ThrowIfCancellationRequested();
            if (NetworkManager.Session != session || !transport.IsRunning || transport.Server != peer)
                throw new OperationCanceledException("Session changed while authenticating.");

            response = await transport.RequestEventAsync(peer, Reference, Prove, Encoding.UTF8.GetBytes(session.CurrentPlayer.Id), token);
            if (response.EventId != Accepted) throw new InvalidOperationException("Host rejected player authentication.");
            initialPlayerJson = Encoding.UTF8.GetString(response.Payload);
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
            if (message.EventId == Begin) {
                var session = NetworkManager.Session;
                if (session?.IsHost != true || accounts.ContainsKey(message.Peer)
                    || pending.ContainsKey(message.Peer)) { Reject(message); return; }
                byte[] bytes = new byte[ChallengeBytes];
                using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
                var proof = new Pending { Session = session, Value = Convert.ToBase64String(bytes),
                    Expires = DateTime.UtcNow.AddSeconds(ProofLifetimeSeconds) };
                pending.Add(message.Peer, proof);
                TransportManager.Instance.SendEvent(message.Peer, Reference, Challenge, Encoding.UTF8.GetBytes(proof.Value), message.MessageId);
            } else if (message.EventId == Prove) VerifyAsync(message);
            else if (message.EventId == Ready) {
                if (message.Payload.Length != 16 || !accounts.ContainsKey(message.Peer)
                    || PlayerHandler.GetPlayerId(message.Peer) != new Guid(message.Payload)) Reject(message);
            }
            else Reject(message);
        }

        private async void VerifyAsync(TransportEvent message) {
            if (!pending.TryGetValue(message.Peer, out var proof) || proof.Verifying) { Reject(message); return; }
            proof.Verifying = true;
            try {
                if (!Current()) throw new InvalidOperationException("Authentication challenge expired.");
                string playerId = Encoding.UTF8.GetString(message.Payload);
                await proof.Session.RefreshAsync();
                if (!Current()) throw new OperationCanceledException("Session changed while verifying.");
                var player = proof.Session.Players.FirstOrDefault(p => p.Id == playerId);
                if (player == null || playerId == proof.Session.Host
                    || !player.Properties.TryGetValue(ClientAuthentication.ProofProperty, out var property)
                    || property.Value != proof.Value)
                    throw new UnauthorizedAccessException("The connection proof does not match the session player.");

                var account = new AccountId("Unity", playerId);
                if (accounts.Values.Contains(account)) throw new InvalidOperationException("This account is already connected.");
                accounts.Add(message.Peer, account);
                var spawned = new TaskCompletionSource<BehaviorEntity.Animal>(TaskCreationOptions.RunContinuationsAsynchronously);
                EntityManager.AddHandlerEvent(() => {
                    try {
                        if (!Current()) throw new OperationCanceledException("Peer left during player setup.");
                        spawned.SetResult(PlayerHandler.SpawnPlayer(message.Peer));
                    } catch (Exception exception) { spawned.SetException(exception); }
                });
                var runtimePlayer = await spawned.Task;
                if (!Current()) throw new OperationCanceledException("Peer left during player setup.");
                EntityManager.CompleteJobs();
                byte[] payload = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(new Registerable<BehaviorEntity.Animal>(runtimePlayer)));
                TransportManager.Instance.SendEvent(message.Peer, Reference, Accepted, payload, message.MessageId);
                pending.Remove(message.Peer);
            } catch (Exception exception) {
                Debug.LogException(exception);
                if (pending.TryGetValue(message.Peer, out var current) && ReferenceEquals(current, proof)) {
                    Reject(message);
                    TransportManager.Instance.Disconnect(message.Peer);
                }
            }

            bool Current() => NetworkManager.Session == proof.Session && DateTime.UtcNow < proof.Expires
                && pending.TryGetValue(message.Peer, out var current) && ReferenceEquals(current, proof);
        }

        private static void Reject(TransportEvent message) =>
            TransportManager.Instance.SendEvent(message.Peer, Reference, Rejected, Array.Empty<byte>(), message.MessageId);

        private void Disconnected(NetworkConnection peer) {
            pending.Remove(peer);
            if (!accounts.Remove(peer, out var account)) return;
            PlayerHandler.RemovePeer(peer, account);
        }
        private void Stopped(Exception reason) {
            initialPlayerJson = null;
            foreach (var peer in accounts.Keys.ToArray()) {
                try { Disconnected(peer); }
                catch (Exception exception) { Debug.LogException(exception); }
            }
            pending.Clear();
        }

        private sealed class Pending {
            public ISession Session;
            public string Value;
            public DateTime Expires;
            public bool Verifying;
        }
    }
}
