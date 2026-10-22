using System;
using Arterra.Data.Entity;
using Arterra.GamePlay;
using LiteNetLib;

namespace Arterra.Core.Network {
    /// <summary>A named event stream scoped to a shared instance ID, with optional player ownership.</summary>
    public class NetEventStream<T> : NetEventStreamBase<T> {
        private readonly Guid offsetId;
        private Guid ownerId;

        public NetEventStream(string name, NetworkPermission writePerm = NetworkPermission.Server,
            NetworkPermission readPerm = NetworkPermission.All, Action<T> recieveEvent = null)
            : base(writePerm, readPerm, recieveEvent) {
            offsetId = NetworkReferenceId.FromName(name);
        }

        public void Register(Entity entity) => Register(entity.info.rtEntityId, entity.info.rtEntityId);

        public void Register(Guid rootId, Guid ownerId = default) {
            if (rootId == Guid.Empty) throw new ArgumentException("Assign the shared instance ID before registration.", nameof(rootId));
            RegisterReference(NetworkReferenceId.ForEntity(rootId, offsetId));
            this.ownerId = ownerId;
        }

        protected override bool CanAccess(NetworkPermission permission, NetPeer peer) {
            if (permission != NetworkPermission.Owner) return base.CanAccess(permission, peer);
            if (base.CanAccess(NetworkPermission.Server, peer)) return true;
            return ownerId != Guid.Empty && PlayerHandler.GetPlayerId(peer) == ownerId;
        }
    }
}
