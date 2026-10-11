using System;
using System.Security.Cryptography;
using System.Text;
using Arterra.Data.Entity;
using Arterra.GamePlay;
using Unity.Networking.Transport;
using Newtonsoft.Json;

namespace Arterra.Core.Network {
    /// <summary>A named entity variable. Register once after the shared entity ID is assigned.</summary>
    public sealed class NetworkVariable<T> : NetworkVariableBase<T> {
        [JsonProperty] private readonly Guid offsetId;
        private Entity entity;

        public NetworkVariable(string name, NetworkPermission writePerm = NetworkPermission.Owner,
            NetworkPermission readPerm = NetworkPermission.All, bool retainValueOnDisconnect = true, T initialValue = default)
            : base(retainValueOnDisconnect, writePerm, readPerm) {
            offsetId = NetworkReferenceId.FromName(name);
            SetInitialValue(initialValue);
        }

        [JsonConstructor]
        private NetworkVariable(Guid offsetId, NetworkPermission writePerm, NetworkPermission readPerm, bool retainValueOnDisconnect)
            : base(retainValueOnDisconnect, writePerm, readPerm) => this.offsetId = offsetId;

        public void Register(Entity entity) {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (entity.info.rtEntityId == Guid.Empty)
                throw new ArgumentException("Assign the shared entity ID before registration.", nameof(entity));
            if (this.entity != null && !ReferenceEquals(this.entity, entity))
                throw new InvalidOperationException("This variable already belongs to another entity.");

            RegisterReference(NetworkReferenceId.ForEntity(entity.info.rtEntityId, offsetId));
            this.entity = entity;
        }

        protected override bool CanAccess(NetworkPermission permission, NetworkConnection peer) {
            if (permission != NetworkPermission.Owner) return base.CanAccess(permission, peer);
            if (base.CanAccess(NetworkPermission.Server, peer)) return true;
            return entity != null && entity.info.rtEntityId != Guid.Empty
                && PlayerHandler.GetPlayerId(peer) == entity.info.rtEntityId;
        }

    }

    // Shared protocol ID derivation for named entity variables and event streams.
    internal static class NetworkReferenceId {
        public static Guid FromName(string name) {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("A network member requires a stable name.", nameof(name));
            // Case-sensitive UTF-8; retain only the hash, never the name string.
            return HashId(new UTF8Encoding(false, true).GetBytes(name));
        }

        public static Guid ForEntity(Guid entityId, Guid offsetId) {
            // Protocol format: two Guid.ToByteArray() layouts, entity ID followed by name hash.
            byte[] input = new byte[GuidBytes * 2];
            entityId.TryWriteBytes(input.AsSpan(0, GuidBytes));
            offsetId.TryWriteBytes(input.AsSpan(GuidBytes, GuidBytes));
            return HashId(input);
        }

        private const int GuidBytes = 16;
        private static Guid HashId(byte[] input) {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(input);
            // UUID version 8, RFC variant, in the explicitly chosen .NET byte layout.
            hash[7] = (byte)((hash[7] & 0x0F) | 0x80);
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash.AsSpan(0, GuidBytes));
        }
    }
}
