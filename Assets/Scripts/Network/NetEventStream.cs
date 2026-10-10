using System;
using Arterra.Data.Entity;
using Arterra.GamePlay;
using Unity.Networking.Transport;

namespace Arterra.Core.Network {
    /// <summary>A named entity event stream. Register after the shared entity ID is assigned.</summary>
    public class NetEventStream<T> : NetEventStreamBase<T> {
        private readonly Guid offsetId;
        private Entity entity;

        public NetEventStream(string name, NetworkPermission writePerm = NetworkPermission.Server,
            NetworkPermission readPerm = NetworkPermission.All, Action<T> recieveEvent = null)
            : base(writePerm, readPerm, recieveEvent) {
            offsetId = NetworkReferenceId.FromName(name);
        }

        public void Register(Entity entity) {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.info.entityId == Guid.Empty)
                throw new ArgumentException("Assign the shared entity ID before registration.", nameof(entity));
            if (this.entity != null && !ReferenceEquals(this.entity, entity))
                throw new InvalidOperationException("This stream already belongs to another entity.");
            RegisterReference(NetworkReferenceId.ForEntity(entity.info.entityId, offsetId));
            this.entity = entity;
        }

        protected override bool CanAccess(NetworkPermission permission, NetworkConnection peer) {
            if (permission != NetworkPermission.Owner) return base.CanAccess(permission, peer);
            if (base.CanAccess(NetworkPermission.Server, peer)) return true;
            return entity != null && entity.info.entityId != Guid.Empty
                && PlayerHandler.GetPlayerId(peer) == entity.info.entityId;
        }
    }
}
