using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Arterra.Core.Network.Service;
using LiteNetLib;
using LiteNetLib.Utils;
using Newtonsoft.Json;
using UnityEngine;

namespace Arterra.Core.Network {
    public sealed partial class TransportManager {
        private NetPeer discoveryPeer;
        private Guid discoverySession;
        private string discoveryTicket;
        private Guid joiningIntroduction;
        private readonly Dictionary<Guid, Introduction> introductions = new();

        internal bool TryGetUser(NetPeer peer, out Guid userId) {
            userId = peer?.Tag is Guid id ? id : Guid.Empty;
            return userId != Guid.Empty;
        }

        private void BeginDiscovery(Guid sessionId, ConnectionTicket ticket) {
            discoverySession = sessionId;
            discoveryTicket = ticket.Ticket;
            var nat = new EventBasedNatPunchListener();
            nat.NatIntroductionSuccess += OnNatIntroduction;
            driver.NatPunchModule.Init(nat);

            var hello = new NetDataWriter();
            hello.Put(1); // Discovery protocol version, not a game connection request.
            discoveryPeer = driver.Connect(ticket.DiscoveryEndpoint.Host, ticket.DiscoveryEndpoint.Port, hello);
        }

        private void AuthenticateDiscovery() {
            SendDiscovery(new {
                type = "authenticate", ticket = discoveryTicket,
                localEndpoint = new { host = NetUtils.GetLocalIp(LocalAddrType.IPv4), port = driver.LocalPort }
            });
            discoveryTicket = null; // Single-use credential; never send it to a game peer.
        }

        private void SendDiscovery(object message) {
            if (discoveryPeer?.ConnectionState != ConnectionState.Connected) return;
            byte[] bytes = Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(message, GameServiceClient.Json));
            discoveryPeer.Send(bytes, DeliveryMethod.ReliableOrdered);
        }

        private void ReceiveDiscovery(byte[] bytes) {
            var message = JsonConvert.DeserializeObject<DiscoveryMessage>(Encoding.UTF8.GetString(bytes), GameServiceClient.Json);
            if (message == null) throw new InvalidDataException("Empty discovery message.");
            switch (message.Type) {
                case "hostReady" when IsServer && message.SessionId == discoverySession:
                    IsRunning = true;
                    connected.TrySetResult(true);
                    break;
                case "introduction" when message.SessionId == discoverySession:
                    if (message.IntroductionId == Guid.Empty || message.RemoteUserId == Guid.Empty)
                        throw new InvalidDataException("Invalid peer introduction.");
                    var introduction = GetIntroduction(message.IntroductionId);
                    introduction.UserId = message.RemoteUserId;
                    if (!IsServer) joiningIntroduction = message.IntroductionId;
                    foreach (var request in introduction.Requests) AcceptIntroduction(request, introduction);
                    introduction.Requests.Clear();
                    foreach (var endpoint in introduction.Endpoints) ConnectCandidate(message.IntroductionId, introduction, endpoint);
                    break;
                case "introductionFinished":
                    RemoveIntroduction(message.IntroductionId);
                    if (!IsServer && !message.Connected && server == null)
                        throw new IOException("A direct connection could not be established.");
                    break;
                case "error":
                    throw new IOException("Discovery rejected the connection: " + message.Code);
                default:
                    throw new InvalidDataException("Unexpected discovery message.");
            }
        }

        // NAT callbacks and direct connection requests can precede their reliable announcement.
        private Introduction GetIntroduction(Guid id) {
            if (!introductions.TryGetValue(id, out var introduction)) {
                introduction = new Introduction();
                introductions.Add(id, introduction);
            }
            return introduction;
        }

        private void OnNatIntroduction(IPEndPoint endpoint, NatAddressType type, string token) {
            if (IsServer || server != null || !Guid.TryParseExact(token, "N", out var id)) return;
            var introduction = GetIntroduction(id);
            if (introduction.Endpoints.Add(endpoint) && introduction.UserId != Guid.Empty)
                ConnectCandidate(id, introduction, endpoint);
        }

        private void ConnectCandidate(Guid id, Introduction introduction, IPEndPoint endpoint) {
            if (IsServer || server != null) return;
            var hello = new NetDataWriter();
            hello.Put(id.ToString("N"));
            var peer = driver.Connect(endpoint, hello);
            introduction.Candidates.Add(peer);
            peer.Tag = introduction.UserId;
        }

        private void OnConnectionRequest(ConnectionRequest request) {
            if (!IsServer || !request.Data.TryGetString(out var token)
                || !Guid.TryParseExact(token, "N", out var id)) { request.Reject(); return; }
            var introduction = GetIntroduction(id);
            if (introduction.UserId != Guid.Empty) AcceptIntroduction(request, introduction);
            else introduction.Requests.Add(request);
        }

        private void AcceptIntroduction(ConnectionRequest request, Introduction introduction) {
            if (!IsServer || introduction.Accepted) { request.Reject(); return; }
            var peer = request.Accept();
            if (peer == null) return;
            introduction.Accepted = true;
            peer.Tag = introduction.UserId;
        }

        private void ReportIntroduction(bool success) {
            if (joiningIntroduction == Guid.Empty) return;
            SendDiscovery(new { type = "introductionResult", introductionId = joiningIntroduction, connected = success });
        }

        private void ExpireIntroductions() {
            foreach (var pair in new List<KeyValuePair<Guid, Introduction>>(introductions)) {
                if (DateTime.UtcNow < pair.Value.Expires) continue;
                RemoveIntroduction(pair.Key);
                if (!IsServer && pair.Key == joiningIntroduction && server == null) {
                    ReportIntroduction(false);
                    throw new TimeoutException("NAT introduction timed out; a direct connection is unavailable.");
                }
            }
        }

        private void RemoveIntroduction(Guid id) {
            if (!introductions.Remove(id, out var introduction)) return;
            foreach (var request in introduction.Requests) request.Reject();
            foreach (var peer in introduction.Candidates) {
                if (peer == server) continue;
                peer.Tag = null;
                peer.Disconnect();
            }
        }

        private void ResetDiscovery() {
            foreach (var introduction in introductions.Values)
                foreach (var peer in introduction.Candidates) peer.Tag = null;
            introductions.Clear();
            discoveryPeer = null;
            discoveryTicket = null;
            joiningIntroduction = Guid.Empty;
        }

        private sealed class Introduction {
            public Guid UserId;
            public bool Accepted;
            public readonly DateTime Expires = DateTime.UtcNow.AddSeconds(10);
            public readonly List<ConnectionRequest> Requests = new();
            public readonly HashSet<IPEndPoint> Endpoints = new();
            public readonly HashSet<NetPeer> Candidates = new();
        }
    }
}
