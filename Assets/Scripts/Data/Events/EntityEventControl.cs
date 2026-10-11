using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Arterra.Core.Network;
using Arterra.Data.Entity;
using Arterra.GamePlay;
using Unity.Networking.Transport;

namespace Arterra.Core.Events {
    /// <summary>Entity event forwarding. Register after assigning the shared entity ID; dispose when released.</summary>
    public sealed class EntityEventControl : EventControl, IDisposable {
        private readonly NetEventStream<RaisedEvent> networkEventStream;
        private readonly NetEventStream<Subscription> subscriptionStream;
        private readonly NetEventStream<Subscription> unsubscriptionStream;
        private readonly NetEventStream<Guid> syncStream;
        private readonly ConcurrentDictionary<GameEvent, HashSet<Guid>> remoteSubscription = new();

        public EntityEventControl() {
            networkEventStream = new NetEventStream<RaisedEvent>("EventControl:Events",
                NetworkPermission.All, NetworkPermission.All, RecieveEventAsync);
            subscriptionStream = new NetEventStream<Subscription>("EventControl:Subscriptions",
                NetworkPermission.All, NetworkPermission.All, RecieveRemoteSubscription);
            unsubscriptionStream = new NetEventStream<Subscription>("EventControl:Unsubscriptions",
                NetworkPermission.All, NetworkPermission.All, RecieveRemoteUnsubscription);
            syncStream = new NetEventStream<Guid>("EventControl:Sync",
                NetworkPermission.All, NetworkPermission.All, RecieveSync);
        }

        public void Register(Entity self) {
            networkEventStream.Register(self);
            subscriptionStream.Register(self);
            unsubscriptionStream.Register(self);
            syncStream.Register(self);

            Sync();
        }

        // Exchange current subscriptions; gameplay events are never replayed.
        public void Sync() {
            if (!TransportManager.Instance.IsRunning) return;
            PublishSubscriptions();
            syncStream.Fire(PlayerHandler.GetPlayerId());
        }

        private void RecieveSync(Guid requester) {
            if (requester == PlayerHandler.GetPlayerId()) return;
            PublishSubscriptions();
        }

        private void PublishSubscriptions() {
            Guid playerId = PlayerHandler.GetPlayerId();
            foreach (int type in events.Keys)
                subscriptionStream.Fire(new Subscription((GameEvent)type, playerId));
        }

        public override void AddEventHandler(GameEvent type, RefEventHandler handler, EventHandlePriority priority = EventHandlePriority.Commutative3) {
            bool hadHandlers = events.ContainsKey((int)type);
            base.AddEventHandler(type, handler, priority);
            if (!hadHandlers) {
                Guid playerId = PlayerHandler.GetPlayerId();
                subscriptionStream.Fire(new Subscription(type, playerId));
            }
        }

        public override void RemoveEventHandler(GameEvent type, RefEventHandler handler) {
            bool hadHandlers = events.ContainsKey((int)type);
            base.RemoveEventHandler(type, handler);
            if (hadHandlers && !events.ContainsKey((int)type)) {
                Guid playerId = PlayerHandler.GetPlayerId();
                unsubscriptionStream.Fire(new Subscription(type, playerId));
            }
        }

        public override void RemoveEventHandler(GameEvent type) {
            bool hadHandlers = events.ContainsKey((int)type);
            base.RemoveEventHandler(type);
            if (hadHandlers) {
                Guid playerId = PlayerHandler.GetPlayerId();
                unsubscriptionStream.Fire(new Subscription(type, playerId));
            }
        }

        public override void RaiseEvent(GameEvent type, object actor, object target, object ctx = null) {
            RaisedEvent newEvent = new (type, actor, target, ctx);
            if (remoteSubscription.TryGetValue(type, out var subscribers)) {
                var recipients = NetworkManager.IsActingServer
                    ? PlayerHandler.Players.Where(pair => subscribers.Contains(pair.Value.info.rtEntityId)).Select(pair => pair.Key)
                    : null;
                networkEventStream.Fire(newEvent, RecieveEvent, recipients);
            } else RecieveEvent(newEvent);
        }

        public void EnqueueEvent(GameEvent type, object actor, object target, object ctx = null) {
            RaisedEvent newEvent = new (type, actor, target, ctx);
            if (remoteSubscription.TryGetValue(type, out var subscribers)) {
                var recipients = NetworkManager.IsActingServer
                    ? PlayerHandler.Players.Where(pair => subscribers.Contains(pair.Value.info.rtEntityId)).Select(pair => pair.Key)
                    : null;
                networkEventStream.Fire(newEvent, recipients: recipients);
            } else RecieveEventAsync(newEvent);
        }

        private void RecieveEvent(RaisedEvent ev) => 
            base.RaiseEvent(ev.type, ev.source, ev.target, ev.context);

        private void RecieveEventAsync(RaisedEvent ev) => 
            EntityManager.AddHandlerEvent(() => base.RaiseEvent(ev.type, ev.source, ev.target, ev.context));
        public override Action<object, object, object> GetRaiseEventAction(GameEvent type) =>
            (actor, target, ctx) => RaiseEvent(type, actor, target, ctx);
        

        private void RecieveRemoteSubscription(Subscription s) {
            if (s.peerId == Guid.Empty || s.peerId == PlayerHandler.GetPlayerId()) return;
            // Remote mutations run on the transport tick; worker threads only inspect the outer dictionary.
            if (remoteSubscription.TryGetValue(s.type, out var peers)) peers.Add(s.peerId);
            else remoteSubscription[s.type] = new HashSet<Guid> { s.peerId };
        }
        private void RecieveRemoteUnsubscription(Subscription s) {
            if (s.peerId == Guid.Empty || s.peerId == PlayerHandler.GetPlayerId()) return;
            if (!remoteSubscription.TryGetValue(s.type, out var peers)) return;
            peers.Remove(s.peerId);
            if (peers.Count == 0) remoteSubscription.TryRemove(s.type, out _);
        }

        public void Dispose() {
            networkEventStream.Dispose();
            subscriptionStream.Dispose();
            unsubscriptionStream.Dispose();
            syncStream.Dispose();
            remoteSubscription.Clear();
        }

        [JsonConverter(typeof(RaisedEventConverter))]
        private struct RaisedEvent {
            public GameEvent type;
            public object source;
            public object target;
            public object context;

            public RaisedEvent(GameEvent type, object source, object target, object context) {
                this.type = type;
                this.source = source;
                this.target = target;
                this.context = context;
            }
        }

        // Object-typed arguments otherwise lose Guid/float and custom-converter types.
        private sealed class RaisedEventConverter : JsonConverter {
            public override bool CanConvert(Type type) => type == typeof(RaisedEvent);

            public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
                RaisedEvent ev = (RaisedEvent)value;
                writer.WriteStartObject();
                writer.WritePropertyName("type"); serializer.Serialize(writer, ev.type);
                writer.WritePropertyName("source"); WriteArgument(writer, ev.source, serializer);
                writer.WritePropertyName("target"); WriteArgument(writer, ev.target, serializer);
                writer.WritePropertyName("context"); WriteArgument(writer, ev.context, serializer);
                writer.WriteEndObject();
            }

            private static void WriteArgument(JsonWriter writer, object value, JsonSerializer serializer) {
                if (value == null) { writer.WriteNull(); return; }
                Type type = value.GetType();
                serializer.SerializationBinder.BindToName(type, out string assembly, out string name);
                writer.WriteStartObject();
                writer.WritePropertyName("assembly"); writer.WriteValue(assembly);
                writer.WritePropertyName("type"); writer.WriteValue(name);
                writer.WritePropertyName("value"); serializer.Serialize(writer, value, type);
                writer.WriteEndObject();
            }

            public override object ReadJson(JsonReader reader, Type type, object existing, JsonSerializer serializer) {
                JObject ev = JObject.Load(reader);
                return new RaisedEvent(ev["type"].ToObject<GameEvent>(serializer),
                    ReadArgument(ev["source"], serializer), ReadArgument(ev["target"], serializer),
                    ReadArgument(ev["context"], serializer));
            }

            private static object ReadArgument(JToken argument, JsonSerializer serializer) {
                if (argument == null || argument.Type == JTokenType.Null) return null;
                Type type = serializer.SerializationBinder.BindToType(
                    (string)argument["assembly"], (string)argument["type"]);
                return argument["value"].ToObject(type, serializer);
            }
        }

        private struct Subscription {
            public GameEvent type;
            public Guid peerId;
            public Subscription(GameEvent type, Guid peerId) {
                this.type = type;
                this.peerId = peerId;
            }
        }
    }
}
